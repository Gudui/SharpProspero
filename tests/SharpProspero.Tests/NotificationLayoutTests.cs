// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop.Kernel;
using System.Runtime.InteropServices;
using Xunit;

namespace SharpProspero.Tests;

// The notification request, established from a real system notification: 3120 bytes, with the message
// at offset 45 and the header fields at the offsets the traced request set.
public sealed unsafe class NotificationLayoutTests
{
    [Fact]
    public void Request_IsThirtyOneTwentyBytes()
        => Assert.Equal(3120, sizeof(SceNotificationRequest));

    [Theory]
    [InlineData("Type", 0)]
    [InlineData("RequestId", 4)]
    [InlineData("Target", 12)]
    [InlineData("Unk28", 28)]
    [InlineData("Message", 45)]
    public void Request_FieldsSitWhereTheRealRequestPutsThem(string field, int offset)
        => Assert.Equal(offset, (int)Marshal.OffsetOf<SceNotificationRequest>(field));

    [Fact]
    public void RequestSize_MatchesTheStruct()
        => Assert.Equal(KernelNotification.RequestSize, sizeof(SceNotificationRequest));
}
