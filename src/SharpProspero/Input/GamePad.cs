// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop;
using SharpProspero.Interop.Pad;
using SharpProspero.Platform;
using System;
using System.Buffers.Binary;
using System.Numerics;

namespace SharpProspero.Input;

/// <summary>One touch-pad contact: its position and the identifier that tracks it across frames.</summary>
public readonly struct TouchPoint
{
    /// <summary>Horizontal position on the touch pad.</summary>
    public ushort X { get; init; }

    /// <summary>Vertical position on the touch pad.</summary>
    public ushort Y { get; init; }

    /// <summary>Identifier that stays constant while a finger remains down.</summary>
    public byte Id { get; init; }

    /// <summary>True when this slot holds a live contact.</summary>
    public bool IsActive { get; init; }
}

/// <summary>
/// A decoded controller sample: button bits, stick and trigger axes, motion (orientation, acceleration
/// and angular velocity) and touch-pad contacts.
/// </summary>
public readonly struct GamePadState
{
    /// <summary>Pressed digital buttons.</summary>
    public ScePadButton Buttons { get; init; }

    /// <summary>Left stick X, 0-255 with 128 at center.</summary>
    public byte LeftStickX { get; init; }

    /// <summary>Left stick Y, 0-255 with 128 at center.</summary>
    public byte LeftStickY { get; init; }

    /// <summary>Right stick X, 0-255 with 128 at center.</summary>
    public byte RightStickX { get; init; }

    /// <summary>Right stick Y, 0-255 with 128 at center.</summary>
    public byte RightStickY { get; init; }

    /// <summary>Left trigger travel, 0-255.</summary>
    public byte LeftTrigger { get; init; }

    /// <summary>Right trigger travel, 0-255.</summary>
    public byte RightTrigger { get; init; }

    /// <summary>Controller orientation as a quaternion, accumulated since it connected.</summary>
    public Quaternion Orientation { get; init; }

    /// <summary>Acceleration in G, one component per axis.</summary>
    public Vector3 Acceleration { get; init; }

    /// <summary>Angular velocity in radians per second, one component per axis.</summary>
    public Vector3 AngularVelocity { get; init; }

    /// <summary>The first touch-pad contact.</summary>
    public TouchPoint Touch1 { get; init; }

    /// <summary>The second touch-pad contact.</summary>
    public TouchPoint Touch2 { get; init; }

    /// <summary>Number of live touch-pad contacts (0-2).</summary>
    public int TouchCount { get; init; }

    /// <summary>True while the controller is connected.</summary>
    public bool IsConnected { get; init; }

    /// <summary>Microsecond timestamp of the sample.</summary>
    public ulong TimestampMicroseconds { get; init; }

    /// <summary>A resting sample: no buttons, both sticks centered and both triggers released. Use this
    /// instead of a zero-initialized value, whose raw stick bytes of 0 read as full lower-left
    /// deflection.</summary>
    public static GamePadState Neutral => new()
    {
        LeftStickX = 128,
        LeftStickY = 128,
        RightStickX = 128,
        RightStickY = 128,
        Orientation = Quaternion.Identity,
    };

    /// <summary>True when every button in <paramref name="button"/> is pressed.</summary>
    public bool IsPressed(ScePadButton button) => (Buttons & button) == button;

    /// <summary>Left stick as two values from -1 to 1.</summary>
    public (float X, float Y) LeftStick => (Normalize(LeftStickX), Normalize(LeftStickY));

    /// <summary>Right stick as two values from -1 to 1.</summary>
    public (float X, float Y) RightStick => (Normalize(RightStickX), Normalize(RightStickY));

    private static float Normalize(byte axis) => Math.Clamp((axis - 128) / 127f, -1f, 1f);

    /// <summary>
    /// Decodes a controller sample from its raw bytes. Returns <see cref="Neutral"/> when the buffer is
    /// too short to hold a sample.
    /// </summary>
    public static GamePadState FromSample(ReadOnlySpan<byte> sample)
    {
        // Field offsets follow the controller sample structure: buttons, two sticks, the analog
        // triggers, the orientation quaternion, acceleration, angular velocity, the touch block, the
        // connection flag, and the timestamp.
        if (sample.Length < 0x58)
            return Neutral;

        // The system sets one bit to say it has taken the controller for itself - while it is showing
        // something of its own, say. The rest of the sample then describes what the system is doing
        // with it rather than what the player is pressing, so the whole sample is dropped. Reading it
        // anyway hands the application presses nobody made, and an application that leaves on one of
        // them leaves by itself. What replaces it is the neutral reading rather than an empty one: the
        // sticks rest at the middle of their range, so zeroed sticks read as held to a corner.
        uint buttons = BinaryPrimitives.ReadUInt32LittleEndian(sample);
        if ((buttons & (uint)ScePadButton.Intercepted) != 0)
            return Neutral;

        return new GamePadState
        {
            Buttons = (ScePadButton)buttons,
            LeftStickX = sample[4],
            LeftStickY = sample[5],
            RightStickX = sample[6],
            RightStickY = sample[7],
            LeftTrigger = sample[8],
            RightTrigger = sample[9],
            Orientation = new Quaternion(F(sample, 12), F(sample, 16), F(sample, 20), F(sample, 24)),
            Acceleration = new Vector3(F(sample, 28), F(sample, 32), F(sample, 36)),
            AngularVelocity = new Vector3(F(sample, 40), F(sample, 44), F(sample, 48)),
            TouchCount = Math.Min((int)sample[52], 2),
            Touch1 = ReadTouch(sample, 60, sample[52] > 0),
            Touch2 = ReadTouch(sample, 68, sample[52] > 1),
            IsConnected = sample[76] != 0,
            TimestampMicroseconds = BinaryPrimitives.ReadUInt64LittleEndian(sample[80..]),
        };
    }

    private static float F(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadSingleLittleEndian(data[offset..]);

    private static TouchPoint ReadTouch(ReadOnlySpan<byte> data, int offset, bool active) => new()
    {
        X = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]),
        Y = BinaryPrimitives.ReadUInt16LittleEndian(data[(offset + 2)..]),
        Id = data[offset + 4],
        IsActive = active,
    };
}

/// <summary>
/// A controller handle for one user. Open it once, read a sample each frame, and dispose it at
/// shutdown. The reader decodes the stable leading fields of the sample; higher-rate motion and
/// touch data are available through the raw bindings when needed.
/// </summary>
public sealed unsafe class GamePad : IDisposable
{
    private readonly int _handle;
    private bool _disposed;

    private GamePad(int handle) => _handle = handle;

    /// <summary>
    /// Initializes the controller subsystem and opens a handle for <paramref name="userId"/>, which
    /// defaults to the user who started the application.
    /// </summary>
    /// <remarks>
    /// A standard controller belongs to a signed-in user. The platform registers the handle against
    /// the user it was opened for and routes that user's samples to it, so a handle opened for the
    /// system user is accepted and then never delivers anything - every button reads as released for
    /// as long as the module runs. Only the remote control is opened for the system user.
    /// </remarks>
    /// <exception cref="ProsperoException">
    /// The user could not be read, or initialization or opening the handle failed.
    /// </exception>
    public static GamePad Open(int userId = SceUser.Invalid)
    {
        if (userId == SceUser.Invalid)
            userId = Users.InitialUserId;
        SceResult.ThrowIfFailed(Pad.scePadInit(), nameof(Pad.scePadInit));
        int handle = Pad.scePadOpen(userId, Pad.PortTypeStandard, 0, null);
        SceResult.ThrowIfFailed(handle, nameof(Pad.scePadOpen));
        return new GamePad(handle);
    }

    /// <summary>
    /// Reads the latest sample and decodes buttons, stick axes, trigger travel, motion and touch.
    /// </summary>
    public GamePadState Read()
    {
        byte* buffer = stackalloc byte[Pad.SampleBufferSize];
        int rc = Pad.scePadReadState(_handle, buffer);
        if (SceResult.Failed(rc))
            return GamePadState.Neutral;

        return GamePadState.FromSample(new ReadOnlySpan<byte>(buffer, Pad.SampleBufferSize));
    }

    /// <summary>
    /// Sets the vibration motors: <paramref name="largeMotor"/> is the low-frequency left motor and
    /// <paramref name="smallMotor"/> the high-frequency right motor, each 0 (stop) to 255. Returns
    /// false when the controller does not accept the request.
    /// </summary>
    public bool SetVibration(byte largeMotor, byte smallMotor)
    {
        var param = new ScePadVibrationParam { LargeMotor = largeMotor, SmallMotor = smallMotor };
        return SceResult.Succeeded(Pad.scePadSetVibration(_handle, &param));
    }

    /// <summary>Stops both vibration motors.</summary>
    public bool StopVibration() => SetVibration(0, 0);

    /// <summary>Sets the light-bar color. Returns false when the controller does not accept the request.</summary>
    public bool SetLightBar(byte r, byte g, byte b)
    {
        var color = new ScePadColor { R = r, G = g, B = b };
        return SceResult.Succeeded(Pad.scePadSetLightBar(_handle, &color));
    }

    /// <summary>Restores the light bar to its default color.</summary>
    public bool ResetLightBar() => SceResult.Succeeded(Pad.scePadResetLightBar(_handle));

    /// <summary>Closes the controller handle.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        StopVibration();
        Pad.scePadClose(_handle);
    }
}
