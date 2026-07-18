// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop;
using SharpProspero.Interop.Dialog;
using SharpProspero.Interop.Sysmodule;
using System;
using System.Runtime.InteropServices;
using System.Text;
using Native = SharpProspero.Interop.Dialog.MessageDialog;

namespace SharpProspero.Platform;

/// <summary>Where an open message dialog is.</summary>
public enum MessageDialogState
{
    /// <summary>The dialog is on screen.</summary>
    Running,

    /// <summary>The dialog has closed; read the chosen button.</summary>
    Finished,
}

/// <summary>The buttons a message shows.</summary>
public enum MessageDialogButtons
{
    /// <summary>A single OK button.</summary>
    Ok = MsgDialogButtonType.Ok,

    /// <summary>Yes and No.</summary>
    YesNo = MsgDialogButtonType.YesNo,

    /// <summary>OK and Cancel.</summary>
    OkCancel = MsgDialogButtonType.OkCancel,
}

/// <summary>
/// The system message dialog. Open it as a message with buttons, or as a progress bar the application
/// drives — the progress bar is what a package installer shows while it works. Poll it once per frame
/// until it closes.
/// </summary>
/// <example>
/// <code>
/// using var dialog = MessageDialog.ShowProgress("Installing...");
/// while (installing)
///     dialog.SetProgress(percentDone);
/// // or a question:
/// using var ask = MessageDialog.ShowMessage("Delete this file?", MessageDialogButtons.YesNo);
/// while (ask.Update() == MessageDialogState.Running) display.Present();
/// bool yes = ask.ChosenButton == MsgDialogButtonId.Ok;
/// </code>
/// </example>
public sealed unsafe class MessageDialog : IDisposable
{
    // The message and the sub-parameter blocks are referenced by the service; they live on the
    // unmanaged heap for the dialog's lifetime and are freed on dispose.
    private byte* _message;
    private void* _subParam;
    private bool _disposed;
    private bool _finished;

    private MessageDialog() { }

    /// <summary>The button the user chose. Meaningful once <see cref="Update"/> reports finished.</summary>
    public MsgDialogButtonId ChosenButton { get; private set; }

    /// <summary>Shows a message with a set of buttons and waits for the user to choose.</summary>
    /// <exception cref="ProsperoException">The dialog could not be opened.</exception>
    public static MessageDialog ShowMessage(string text, MessageDialogButtons buttons = MessageDialogButtons.Ok, int userId = SceUser.System)
    {
        ArgumentNullException.ThrowIfNull(text);
        var dialog = Begin();
        try
        {
            dialog._message = Utf8(text);
            var user = (SceMsgDialogUserMessageParam*)NativeMemory.AllocZeroed((nuint)sizeof(SceMsgDialogUserMessageParam));
            dialog._subParam = user;
            user->ButtonType = (MsgDialogButtonType)buttons;
            user->Msg = dialog._message;

            SceMsgDialogParam param;
            Native.InitializeParam(&param);
            param.Mode = MsgDialogMode.UserMessage;
            param.UserId = userId;
            param.UserMsgParam = user;
            SceResult.ThrowIfFailed(Native.sceMsgDialogOpen(&param), nameof(Native.sceMsgDialogOpen));
            return dialog;
        }
        catch
        {
            dialog.Dispose();
            throw;
        }
    }

    /// <summary>Shows a progress bar the application drives with <see cref="SetProgress"/>.</summary>
    /// <exception cref="ProsperoException">The dialog could not be opened.</exception>
    public static MessageDialog ShowProgress(string caption, int userId = SceUser.System)
    {
        ArgumentNullException.ThrowIfNull(caption);
        var dialog = Begin();
        try
        {
            dialog._message = Utf8(caption);
            var bar = (SceMsgDialogProgressBarParam*)NativeMemory.AllocZeroed((nuint)sizeof(SceMsgDialogProgressBarParam));
            dialog._subParam = bar;
            bar->BarType = MsgDialogProgressBarType.Percentage;
            bar->Msg = dialog._message;

            SceMsgDialogParam param;
            Native.InitializeParam(&param);
            param.Mode = MsgDialogMode.ProgressBar;
            param.UserId = userId;
            param.ProgBarParam = bar;
            SceResult.ThrowIfFailed(Native.sceMsgDialogOpen(&param), nameof(Native.sceMsgDialogOpen));
            return dialog;
        }
        catch
        {
            dialog.Dispose();
            throw;
        }
    }

    /// <summary>Sets the progress bar to <paramref name="percent"/> (0 to 100).</summary>
    public void SetProgress(int percent)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Native.sceMsgDialogProgressBarSetValue((int)MsgDialogProgressBarTarget.Default, (uint)Math.Clamp(percent, 0, 100));
    }

    /// <summary>Changes the caption shown with the progress bar.</summary>
    public void SetProgressMessage(string message)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(message);
        int count = Encoding.UTF8.GetByteCount(message);
        byte* buffer = stackalloc byte[count + 1];
        Encoding.UTF8.GetBytes(message, new Span<byte>(buffer, count));
        buffer[count] = 0;
        Native.sceMsgDialogProgressBarSetMsg((int)MsgDialogProgressBarTarget.Default, buffer);
    }

    /// <summary>Advances the dialog and reports where it is. Call once per frame.</summary>
    public MessageDialogState Update()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_finished)
            return MessageDialogState.Finished;
        if (Native.sceMsgDialogUpdateStatus() != CommonDialogStatus.Finished)
            return MessageDialogState.Running;

        SceMsgDialogResult result;
        new Span<byte>(&result, sizeof(SceMsgDialogResult)).Clear();
        if (SceResult.Succeeded(Native.sceMsgDialogGetResult(&result)))
            ChosenButton = result.ButtonId;
        _finished = true;
        return MessageDialogState.Finished;
    }

    /// <summary>Closes the dialog if it is open, shuts it down, unloads the module, and frees its buffers.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (!_finished)
            Native.sceMsgDialogClose();
        Native.sceMsgDialogTerminate();
        Sysmodule.sceSysmoduleUnloadModule((ushort)SystemModuleId.MessageDialog);

        if (_message != null) { NativeMemory.Free(_message); _message = null; }
        if (_subParam != null) { NativeMemory.Free(_subParam); _subParam = null; }
    }

    private static MessageDialog Begin()
    {
        CommonDialog.EnsureInitialized();
        SceResult.ThrowIfFailed(
            Sysmodule.sceSysmoduleLoadModule((ushort)SystemModuleId.MessageDialog),
            "sceSysmoduleLoadModule(MessageDialog)");
        SceResult.ThrowIfFailed(Native.sceMsgDialogInitialize(), nameof(Native.sceMsgDialogInitialize));
        return new MessageDialog();
    }

    private static byte* Utf8(string value)
    {
        int count = Encoding.UTF8.GetByteCount(value);
        byte* buffer = (byte*)NativeMemory.AllocZeroed((nuint)count + 1);
        Encoding.UTF8.GetBytes(value, new Span<byte>(buffer, count));
        return buffer;
    }
}
