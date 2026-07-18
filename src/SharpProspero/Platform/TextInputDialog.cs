// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System;
using System.Runtime.InteropServices;
using SharpProspero.Interop;
using SharpProspero.Interop.Dialog;
using SharpProspero.Interop.Sysmodule;
using SharpProspero.Interop.UserService;

namespace SharpProspero.Platform;

/// <summary>Where an open on-screen keyboard is.</summary>
public enum TextInputState
{
    /// <summary>The keyboard is on screen and the user is typing.</summary>
    Running,

    /// <summary>The keyboard has closed; read <see cref="TextInputDialog.Text"/> and
    /// <see cref="TextInputDialog.EndStatus"/>.</summary>
    Finished,
}

/// <summary>
/// The on-screen keyboard. Open it for a title, poll it once per frame until it closes, then read the
/// text. This is the input surface a file explorer, browser, or any interactive utility needs to let
/// the user type.
/// </summary>
/// <example>
/// <code>
/// using var input = TextInputDialog.Open("Enter a name", maxLength: 64);
/// while (input.Update() == TextInputState.Running)
///     display.Present();
/// if (input.EndStatus == ImeDialogEndStatus.Ok)
///     Use(input.Text);
/// </code>
/// </example>
public sealed unsafe class TextInputDialog : IDisposable
{
    private char* _buffer;
    private char* _title;
    private char* _placeholder;
    private readonly int _capacity;
    private bool _disposed;
    private bool _finished;

    private TextInputDialog(char* buffer, char* title, char* placeholder, int capacity)
    {
        _buffer = buffer;
        _title = title;
        _placeholder = placeholder;
        _capacity = capacity;
    }

    /// <summary>How the keyboard closed. Meaningful once <see cref="Update"/> reports finished.</summary>
    public ImeDialogEndStatus EndStatus { get; private set; }

    /// <summary>
    /// Opens the keyboard.
    /// </summary>
    /// <param name="title">The title shown above the field, or null.</param>
    /// <param name="maxLength">The longest text the user may enter, 1 to 2048 characters.</param>
    /// <param name="type">The keyboard layout.</param>
    /// <param name="placeholder">A hint shown while the field is empty, or null.</param>
    /// <param name="initialText">Text the field starts with, or null.</param>
    /// <param name="options">Behaviour options, for example a password mask.</param>
    /// <param name="userId">The user the keyboard belongs to. Defaults to the signed-in user.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxLength"/> is out of range.</exception>
    /// <exception cref="ProsperoException">The keyboard could not be opened.</exception>
    public static TextInputDialog Open(
        string? title = null,
        int maxLength = 128,
        ImeType type = ImeType.Default,
        string? placeholder = null,
        string? initialText = null,
        ImeOption options = ImeOption.None,
        int userId = int.MinValue)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxLength, 2048);

        // The keyboard sits on the shared dialog subsystem and its own loadable module, both brought
        // up before its own init, or that init fails.
        CommonDialog.EnsureInitialized();
        SceResult.ThrowIfFailed(
            Sysmodule.sceSysmoduleLoadModule((ushort)SystemModuleId.ImeDialog),
            "sceSysmoduleLoadModule(ImeDialog)");

        int user = userId;
        if (user == int.MinValue)
        {
            int initial;
            SceResult.ThrowIfFailed(UserService.sceUserServiceGetInitialUser(&initial),
                nameof(UserService.sceUserServiceGetInitialUser));
            user = initial;
        }

        // The buffer holds the entered text and must outlive the dialog, so it lives on the unmanaged
        // heap for the object's lifetime. Room for maxLength characters plus the terminator.
        int capacity = maxLength + 1;
        char* buffer = AllocString(capacity);
        char* titlePtr = CopyString(title);
        char* placeholderPtr = CopyString(placeholder);
        if (!string.IsNullOrEmpty(initialText))
            WriteString(buffer, capacity, initialText);

        var dialog = new TextInputDialog(buffer, titlePtr, placeholderPtr, capacity);
        try
        {
            SceImeDialogParam param;
            ImeDialog.InitializeParam(&param);
            param.UserId = user;
            param.Type = type;
            param.EnterLabel = ImeEnterLabel.Default;
            param.InputMethod = ImeInputMethod.Default;
            param.Option = options;
            param.MaxTextLength = (uint)maxLength;
            param.InputTextBuffer = buffer;
            param.Title = titlePtr;
            param.Placeholder = placeholderPtr;

            // Center the keyboard on a standard 1080p screen using the size the service reports for
            // these parameters. Left/top alignment with the panel's own extent gives a true center.
            uint width = 0, height = 0;
            if (SceResult.Succeeded(ImeDialog.sceImeDialogGetPanelSizeExtended(&param, null, &width, &height)))
            {
                param.PosX = (1920f - width) / 2f;
                param.PosY = (1080f - height) / 2f;
                param.HorizontalAlignment = ImeHorizontalAlignment.Left;
                param.VerticalAlignment = ImeVerticalAlignment.Top;
            }

            SceResult.ThrowIfFailed(ImeDialog.sceImeDialogInit(&param, null),
                nameof(ImeDialog.sceImeDialogInit));
            return dialog;
        }
        catch
        {
            dialog.Dispose();
            throw;
        }
    }

    /// <summary>Advances the keyboard and reports where it is. Call once per frame.</summary>
    public TextInputState Update()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_finished)
            return TextInputState.Finished;
        if (ImeDialog.sceImeDialogGetStatus() != ImeDialogStatus.Finished)
            return TextInputState.Running;

        SceImeDialogResult result;
        SceResult.ThrowIfFailed(ImeDialog.sceImeDialogGetResult(&result),
            nameof(ImeDialog.sceImeDialogGetResult));
        EndStatus = result.EndStatus;
        _finished = true;
        return TextInputState.Finished;
    }

    /// <summary>
    /// The text the user entered. Empty until the keyboard has finished, and empty when the user
    /// canceled.
    /// </summary>
    public string Text
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_finished || EndStatus != ImeDialogEndStatus.Ok)
                return "";
            return ReadString(_buffer, _capacity);
        }
    }

    /// <summary>Closes the keyboard if it is open, shuts it down, and releases its buffers.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        if (!_finished)
            ImeDialog.sceImeDialogAbort();
        ImeDialog.sceImeDialogTerm();
        Sysmodule.sceSysmoduleUnloadModule((ushort)SystemModuleId.ImeDialog);

        Free(ref _buffer);
        Free(ref _title);
        Free(ref _placeholder);
    }

    private static char* AllocString(int capacity)
    {
        char* p = (char*)NativeMemory.AllocZeroed((nuint)capacity, sizeof(char));
        return p;
    }

    private static char* CopyString(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;
        char* p = AllocString(value.Length + 1);
        WriteString(p, value.Length + 1, value);
        return p;
    }

    // Copies at most capacity-1 characters and always terminates.
    private static void WriteString(char* destination, int capacity, string value)
    {
        int n = Math.Min(value.Length, capacity - 1);
        for (int i = 0; i < n; i++)
            destination[i] = value[i];
        destination[n] = '\0';
    }

    // Reads a UTF-16 string up to the terminator, bounded by the buffer so a service that failed to
    // terminate cannot run the read off the end.
    private static string ReadString(char* source, int capacity)
    {
        if (source == null)
            return "";
        int length = 0;
        while (length < capacity - 1 && source[length] != '\0')
            length++;
        return new string(source, 0, length);
    }

    private static void Free(ref char* p)
    {
        if (p != null)
        {
            NativeMemory.Free(p);
            p = null;
        }
    }
}
