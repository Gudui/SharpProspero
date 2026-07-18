// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop;
using SharpProspero.Interop.Kernel;
using System;
using System.Text;

namespace SharpProspero.Platform;

/// <summary>
/// The on-screen notification, the toast that slides in at the top of the screen. Every homebrew
/// utility uses one to confirm a copy, report a finished install, or show a short message.
/// </summary>
public static unsafe class Notification
{
    /// <summary>
    /// Shows a notification with <paramref name="message"/>. The message is trimmed to what the
    /// request holds (1023 characters).
    /// </summary>
    /// <exception cref="ProsperoException">The request was refused.</exception>
    public static void Show(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        SceNotificationRequest request = default;
        // The header fields a working text notification sets, filled in before the message.
        request.Type = 0x64;
        request.RequestId = -1;
        request.Unk28 = -1;
        for (int i = 0; i < 16; i++)
            request.Target[i] = 0xFF;

        int written = Encoding.UTF8.GetBytes(message, new Span<byte>(request.Message, 1023));
        request.Message[written] = 0;

        SceResult.ThrowIfFailed(
            KernelNotification.sceKernelSendNotificationRequest(
                KernelNotification.ToastDevice, &request, KernelNotification.RequestSize, 0),
            nameof(KernelNotification.sceKernelSendNotificationRequest));
    }
}
