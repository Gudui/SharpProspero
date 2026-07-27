// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop;
using SharpProspero.Interop.Dialog;
using SharpProspero.Interop.Sysmodule;
using SharpProspero.Interop.UserService;
using System;
using System.Text;

namespace SharpProspero.Platform;

/// <summary>
/// Opens the system browser over the running application. Open it for an address, then call
/// <see cref="Update"/> once per frame until it reports it has closed. Disposing shuts the browser
/// subsystem down.
/// </summary>
/// <example>
/// <code>
/// using var browser = WebBrowser.Open("https://example.com");
/// while (browser.Update() != WebBrowserState.Closed)
///     display.Present();
/// </code>
/// </example>
public sealed unsafe class WebBrowser : IDisposable
{
    private bool _disposed;
    private bool _opened;

    private WebBrowser() { }

    /// <summary>
    /// Starts the browser subsystem and opens <paramref name="url"/> for <paramref name="userId"/>,
    /// or for the user the machine started with when none is named.
    /// </summary>
    /// <exception cref="ProsperoException">The subsystem or the dialog refused to start.</exception>
    public static WebBrowser Open(string url, int userId = int.MinValue)
    {
        ArgumentException.ThrowIfNullOrEmpty(url);

        // The browser wants a signed-in user and checks the id against the list of them. Neither the
        // system's own id nor the one meaning everyone is in that list, so defaulting to either was a
        // dialog that always refused to open. Left unnamed, the user the machine started with is asked
        // for, the way the text-entry dialog beside this already does.
        if (userId == int.MinValue)
        {
            int initial;
            SceResult.ThrowIfFailed(UserService.sceUserServiceGetInitialUser(&initial),
                nameof(UserService.sceUserServiceGetInitialUser));
            userId = initial;
        }

        // The browser sits on the shared dialog subsystem and its own loadable module. Both come up
        // before its own initialize, in this order, or that initialize fails.
        CommonDialog.EnsureInitialized();
        SceResult.ThrowIfFailed(
            Sysmodule.sceSysmoduleLoadModule((ushort)SystemModuleId.WebBrowserDialog),
            "sceSysmoduleLoadModule(WebBrowserDialog)");
        SceResult.ThrowIfFailed(WebBrowserDialog.sceWebBrowserDialogInitialize(),
            nameof(WebBrowserDialog.sceWebBrowserDialogInitialize));

        var browser = new WebBrowser();
        try
        {
            int byteCount = Encoding.UTF8.GetByteCount(url);
            Span<byte> address = byteCount < 512 ? stackalloc byte[byteCount + 1] : new byte[byteCount + 1];
            int written = Encoding.UTF8.GetBytes(url, address);
            address[written] = 0;

            // The parameter block carries a check value taken from its own address, so it is filled in
            // and handed over from the one place it lives.
            WebBrowserDialogParam param;
            WebBrowserDialog.InitializeParam(&param);
            param.Mode = WebBrowserDialogMode.Default;
            param.UserId = userId;
            fixed (byte* p = address)
            {
                param.Url = p;
                SceResult.ThrowIfFailed(WebBrowserDialog.sceWebBrowserDialogOpen(&param),
                    nameof(WebBrowserDialog.sceWebBrowserDialogOpen));
            }
            browser._opened = true;
            return browser;
        }
        catch
        {
            WebBrowserDialog.sceWebBrowserDialogTerminate();
            throw;
        }
    }

    /// <summary>Advances the browser and reports where it is. Call once per frame.</summary>
    public WebBrowserState Update()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return WebBrowserDialog.sceWebBrowserDialogUpdateStatus() switch
        {
            CommonDialogStatus.Running => WebBrowserState.Running,
            CommonDialogStatus.Finished => WebBrowserState.Closed,
            _ => WebBrowserState.Closed,
        };
    }

    /// <summary>The result code of a browser that has closed.</summary>
    public int Result()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        WebBrowserDialogResult result;
        SceResult.ThrowIfFailed(WebBrowserDialog.sceWebBrowserDialogGetResult(&result),
            nameof(WebBrowserDialog.sceWebBrowserDialogGetResult));
        return result.Result;
    }

    /// <summary>Closes the browser if it is open and shuts the subsystem down.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_opened)
            WebBrowserDialog.sceWebBrowserDialogClose();
        WebBrowserDialog.sceWebBrowserDialogTerminate();
    }
}

/// <summary>Where an open browser is.</summary>
public enum WebBrowserState
{
    /// <summary>Still open.</summary>
    Running,

    /// <summary>Closed; read the result.</summary>
    Closed,
}
