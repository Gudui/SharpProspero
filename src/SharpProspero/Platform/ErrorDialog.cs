// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop;
using SharpProspero.Interop.Dialog;
using SharpProspero.Interop.Sysmodule;
using System;
using Native = SharpProspero.Interop.Dialog.ErrorDialog;

namespace SharpProspero.Platform;

/// <summary>Where an open error dialog is.</summary>
public enum ErrorDialogState
{
    /// <summary>The dialog is on screen.</summary>
    Running,

    /// <summary>The dialog has closed.</summary>
    Closed,
}

/// <summary>
/// The system error dialog. Show it for an error code, poll it once per frame until it closes. It
/// presents the console's own message for the code, so a utility reports failures the way the system
/// does.
/// </summary>
/// <example>
/// <code>
/// using var dialog = ErrorDialog.Show(errorCode);
/// while (dialog.Update() != ErrorDialogState.Closed)
///     display.Present();
/// </code>
/// </example>
public sealed unsafe class ErrorDialog : IDisposable
{
    private bool _disposed;
    private bool _opened;

    private ErrorDialog() { }

    /// <summary>
    /// Opens the error dialog for <paramref name="errorCode"/>. Brings the dialog subsystem up and
    /// loads the error-dialog module first.
    /// </summary>
    /// <exception cref="ProsperoException">The subsystem or the dialog refused to start.</exception>
    public static ErrorDialog Show(int errorCode, int userId = SceUser.System)
    {
        CommonDialog.EnsureInitialized();
        SceResult.ThrowIfFailed(
            Sysmodule.sceSysmoduleLoadModule((ushort)SystemModuleId.ErrorDialog),
            "sceSysmoduleLoadModule(ErrorDialog)");
        SceResult.ThrowIfFailed(Native.sceErrorDialogInitialize(), nameof(Native.sceErrorDialogInitialize));

        var dialog = new ErrorDialog();
        try
        {
            SceErrorDialogParam param;
            Native.InitializeParam(&param);
            param.ErrorCode = errorCode;
            param.UserId = userId;
            SceResult.ThrowIfFailed(Native.sceErrorDialogOpen(&param), nameof(Native.sceErrorDialogOpen));
            dialog._opened = true;
            return dialog;
        }
        catch
        {
            dialog.Dispose();
            throw;
        }
    }

    /// <summary>Advances the dialog and reports where it is. Call once per frame.</summary>
    public ErrorDialogState Update()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Native.sceErrorDialogUpdateStatus() == CommonDialogStatus.Running
            ? ErrorDialogState.Running
            : ErrorDialogState.Closed;
    }

    /// <summary>Closes the dialog if it is open, shuts it down, and unloads the module.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_opened)
            Native.sceErrorDialogClose();
        Native.sceErrorDialogTerminate();
        Sysmodule.sceSysmoduleUnloadModule((ushort)SystemModuleId.ErrorDialog);
    }
}
