// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop;
using SharpProspero.Interop.Mouse;
using System;
using Native = SharpProspero.Interop.Mouse.Mouse;

namespace SharpProspero.Input;

/// <summary>The mouse's movement and buttons at one moment.</summary>
public readonly record struct MouseState(bool Connected, MouseButton Buttons, int DeltaX, int DeltaY, int Wheel, int Tilt)
{
    /// <summary>True when <paramref name="button"/> is held.</summary>
    public bool IsButtonDown(MouseButton button) => (Buttons & button) != 0;
}

/// <summary>
/// A USB mouse. Open it for a user at startup, read its movement each frame, and dispose it at
/// shutdown. The movement is relative: it is how far the mouse moved since the last read, which an
/// application accumulates into a cursor position of its own.
/// </summary>
/// <example>
/// <code>
/// using var mouse = Mouse.Open();
/// MouseState state = mouse.Read();
/// cursorX += state.DeltaX;
/// if (state.IsButtonDown(MouseButton.Primary)) { }
/// </code>
/// </example>
public sealed unsafe class Mouse : IDisposable
{
    private readonly int _handle;
    private bool _disposed;

    private Mouse(int handle) => _handle = handle;

    /// <summary>
    /// Starts the mouse service and opens the mouse for <paramref name="userId"/> (the signed-in user
    /// by default). Every mouse is merged into one handle.
    /// </summary>
    /// <exception cref="ProsperoException">The service or the mouse could not be opened.</exception>
    public static Mouse Open(int userId = SceUser.System)
    {
        SceResult.ThrowIfFailed(Native.sceMouseInit(), nameof(Native.sceMouseInit));

        var param = new SceMouseOpenParam { BehaviorFlag = Native.OpenMerged };
        int handle = Native.sceMouseOpen(userId, 0, 0, &param);
        SceResult.ThrowIfFailed(handle, nameof(Native.sceMouseOpen));
        return new Mouse(handle);
    }

    /// <summary>Reads the latest movement and buttons.</summary>
    public MouseState Read()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        SceMouseData data;
        int read = Native.sceMouseRead(_handle, &data, 1);
        if (read <= 0)
            return new MouseState(false, MouseButton.None, 0, 0, 0, 0);
        return new MouseState(data.Connected, data.Buttons, data.XAxis, data.YAxis, data.Wheel, data.Tilt);
    }

    /// <summary>Closes the mouse.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Native.sceMouseClose(_handle);
    }
}
