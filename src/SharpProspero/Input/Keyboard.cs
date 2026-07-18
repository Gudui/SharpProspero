// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System;
using SharpProspero.Interop;
using SharpProspero.Interop.Keyboard;
using Native = SharpProspero.Interop.Keyboard.Keyboard;

namespace SharpProspero.Input;

/// <summary>The keys held on a keyboard at one moment, plus the modifiers.</summary>
public readonly ref struct KeyboardState
{
    private readonly ReadOnlySpan<ushort> _keys;

    internal KeyboardState(bool connected, KeyModifier modifiers, ReadOnlySpan<ushort> keys)
    {
        Connected = connected;
        Modifiers = modifiers;
        _keys = keys;
    }

    /// <summary>True while a keyboard is connected.</summary>
    public bool Connected { get; }

    /// <summary>The modifier keys held.</summary>
    public KeyModifier Modifiers { get; }

    /// <summary>The USB HID usage codes of the keys held, newest last. Empty when none are held.</summary>
    public ReadOnlySpan<ushort> Keys => _keys;

    /// <summary>True when <paramref name="usageCode"/> is among the keys held.</summary>
    public bool IsKeyDown(int usageCode)
    {
        foreach (ushort key in _keys)
        {
            if (key == usageCode)
                return true;
        }
        return false;
    }
}

/// <summary>
/// A USB keyboard. Open it for a user at startup, read its state each frame, and dispose it at
/// shutdown. This is the input a file explorer or a browser wants beyond the controller.
/// </summary>
/// <example>
/// <code>
/// using var keyboard = Keyboard.Open();
/// KeyboardState state = keyboard.Read();
/// if (state.Modifiers.HasFlag(KeyModifier.LeftControl)) { }
/// </code>
/// </example>
public sealed unsafe class Keyboard : IDisposable
{
    private readonly int _handle;
    private bool _disposed;

    private Keyboard(int handle) => _handle = handle;

    /// <summary>
    /// Starts the keyboard service and opens the keyboard for <paramref name="userId"/> (the
    /// signed-in user by default).
    /// </summary>
    /// <exception cref="ProsperoException">The service or the keyboard could not be opened.</exception>
    public static Keyboard Open(int userId = SceUser.System)
    {
        SceResult.ThrowIfFailed(Native.sceKeyboardInit(), nameof(Native.sceKeyboardInit));
        int handle = Native.sceKeyboardOpen(userId, Native.PortTypeStandard, 0, null);
        SceResult.ThrowIfFailed(handle, nameof(Native.sceKeyboardOpen));
        return new Keyboard(handle);
    }

    /// <summary>Reads the keys held right now.</summary>
    /// <remarks>The returned state borrows a buffer valid until the next read; copy what you keep.</remarks>
    public KeyboardState Read()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        SceKeyboardData data;
        if (SceResult.Failed(Native.sceKeyboardReadState(_handle, &data)))
            return new KeyboardState(false, KeyModifier.None, ReadOnlySpan<ushort>.Empty);

        int count = Math.Clamp(data.Length, 0, Native.MaxKeyCodes);
        var keys = new ushort[count];
        for (int i = 0; i < count; i++)
            keys[i] = data.KeyCode[i];
        return new KeyboardState(data.Connected, data.ModifierKey, keys);
    }

    /// <summary>Closes the keyboard.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Native.sceKeyboardClose(_handle);
    }
}
