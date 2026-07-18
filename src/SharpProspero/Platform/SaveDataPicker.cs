// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System;
using System.Runtime.InteropServices;
using System.Text;
using SharpProspero.Interop;
using SharpProspero.Interop.Dialog;
using SharpProspero.Interop.SaveData;
using SharpProspero.Interop.Sysmodule;
using Native = SharpProspero.Interop.Dialog.SaveDataDialog;

namespace SharpProspero.Platform;

/// <summary>
/// Shows the on-screen dialog that lists a user's saves so they can pick one, and reports which they
/// chose. Open the picker, poll it each frame until it finishes, then read the chosen directory.
/// </summary>
/// <example>
/// <code>
/// using var picker = SaveDataPicker.OpenList(userId);
/// while (!picker.TryGetResult(out string? directory))
///     context.Present();
/// if (directory is not null)
///     Console.WriteLine($"Picked {directory}");
/// </code>
/// </example>
public sealed unsafe class SaveDataPicker : IDisposable
{
    private SceSaveDataDialogItems* _items;
    private bool _disposed;

    private SaveDataPicker(SceSaveDataDialogItems* items) => _items = items;

    /// <summary>
    /// Opens a dialog listing the saves of <paramref name="userId"/> for the running application, with
    /// the wording of <paramref name="type"/> (load by default).
    /// </summary>
    /// <exception cref="ProsperoException">The dialog could not be opened.</exception>
    public static SaveDataPicker OpenList(int userId, SaveDataDialogType type = SaveDataDialogType.Load)
    {
        CommonDialog.EnsureInitialized();
        SceResult.ThrowIfFailed(
            Sysmodule.sceSysmoduleLoadModule((ushort)SystemModuleId.SaveDataDialog),
            "sceSysmoduleLoadModule(SaveDataDialog)");
        SceResult.ThrowIfFailed(Native.sceSaveDataDialogInitialize(), nameof(Native.sceSaveDataDialogInitialize));

        // The item list is read by the service while the dialog is open, so it lives on the heap for
        // the picker's lifetime and is freed on dispose.
        var items = (SceSaveDataDialogItems*)NativeMemory.AllocZeroed((nuint)sizeof(SceSaveDataDialogItems));
        items->UserId = userId;
        items->ItemStyle = SaveDataDialogItemStyle.TitleDateSizeSubtitle;
        items->FocusPos = SaveDataDialogFocusPos.DataLatest;

        SceSaveDataDialogParam param;
        new Span<byte>(&param, sizeof(SceSaveDataDialogParam)).Clear();
        Native.InitializeParam(&param);
        param.Mode = SaveDataDialogMode.List;
        param.DispType = type;
        param.Items = items;

        try
        {
            SceResult.ThrowIfFailed(Native.sceSaveDataDialogOpen(&param), nameof(Native.sceSaveDataDialogOpen));
        }
        catch
        {
            NativeMemory.Free(items);
            Native.sceSaveDataDialogTerminate();
            Sysmodule.sceSysmoduleUnloadModule((ushort)SystemModuleId.SaveDataDialog);
            throw;
        }
        return new SaveDataPicker(items);
    }

    /// <summary>The dialog's status. Poll it until it is <see cref="CommonDialogStatus.Finished"/>.</summary>
    public CommonDialogStatus Status
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return (CommonDialogStatus)Native.sceSaveDataDialogUpdateStatus();
        }
    }

    /// <summary>
    /// Reads the outcome. Returns <see langword="false"/> while the dialog is still running. Once it
    /// has finished, returns <see langword="true"/> and sets <paramref name="directory"/> to the chosen
    /// save's directory, or null when the user backed out without choosing.
    /// </summary>
    /// <exception cref="ProsperoException">The result could not be read.</exception>
    public bool TryGetResult(out string? directory)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        directory = null;
        if ((CommonDialogStatus)Native.sceSaveDataDialogUpdateStatus() != CommonDialogStatus.Finished)
            return false;

        SceSaveDataDialogResult result;
        new Span<byte>(&result, sizeof(SceSaveDataDialogResult)).Clear();
        SceSaveDataDirName dirName;
        new Span<byte>(&dirName, sizeof(SceSaveDataDirName)).Clear();
        result.DirName = &dirName;

        SceResult.ThrowIfFailed(Native.sceSaveDataDialogGetResult(&result), nameof(Native.sceSaveDataDialogGetResult));
        if (result.DirName is not null)
        {
            string picked = ReadUtf8((byte*)result.DirName, 32);
            if (picked.Length > 0)
                directory = picked;
        }
        return true;
    }

    /// <summary>Stops the dialog service and unloads the module.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Native.sceSaveDataDialogTerminate();
        Sysmodule.sceSysmoduleUnloadModule((ushort)SystemModuleId.SaveDataDialog);
        if (_items is not null)
        {
            NativeMemory.Free(_items);
            _items = null;
        }
    }

    private static string ReadUtf8(byte* start, int maxLength)
    {
        int length = 0;
        while (length < maxLength && start[length] != 0)
            length++;
        return length == 0 ? string.Empty : Encoding.UTF8.GetString(start, length);
    }
}
