// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System.Runtime.InteropServices;

namespace SharpProspero.Payload;

/// <summary>
/// Retrieves the <see cref="PayloadArgs"/> block the loader passed to the payload's entry point.
/// The start object saves the block pointer before calling constructors or <c>main</c>, and
/// this class reads it back through a small getter the start object defines.
/// </summary>
public static unsafe partial class PayloadEntryPoint
{
    /// <summary>Returns the payload arguments the loader handed to this payload at start-up.</summary>
    public static PayloadArgs* Args => __prospero_get_payload_args();

    [LibraryImport("libScePosix", EntryPoint = "__prospero_get_payload_args")]
    private static partial PayloadArgs* __prospero_get_payload_args();
}
