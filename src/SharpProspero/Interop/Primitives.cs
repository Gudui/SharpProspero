// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System;

namespace SharpProspero.Interop;

/// <summary>
/// The system user identifier passed to services that act on behalf of a signed-in user. Use
/// <see cref="System"/> for background work that is not tied to a specific profile.
/// </summary>
public static class SceUser
{
    /// <summary>The system profile. Value 0xFF.</summary>
    public const int System = 0xFF;

    /// <summary>Every signed-in user, for a service that acts on all of them. Value 0xFE.</summary>
    public const int Everyone = 0xFE;

    /// <summary>No user. The value a user id holds before a real user is chosen.</summary>
    public const int Invalid = -1;
}

/// <summary>
/// Helpers for interpreting the 32-bit result codes returned by the interop bindings. A value of
/// zero (<see cref="Ok"/>) or any non-negative value is success; a negative value is an error whose
/// high half identifies the reporting module.
/// </summary>
public static class SceResult
{
    /// <summary>The success code shared by the services.</summary>
    public const int Ok = 0;

    /// <summary>True when <paramref name="code"/> reports failure.</summary>
    public static bool Failed(int code) => code < 0;

    /// <summary>True when <paramref name="code"/> reports success.</summary>
    public static bool Succeeded(int code) => code >= 0;

    /// <summary>
    /// Throws a <see cref="ProsperoException"/> when <paramref name="code"/> reports failure.
    /// Returns <paramref name="code"/> unchanged on success so calls can be chained.
    /// </summary>
    public static int ThrowIfFailed(int code, string operation)
    {
        if (code < 0)
            throw new ProsperoException(operation, code);
        return code;
    }
}

/// <summary>
/// Raised when a service call reports a failure code. <see cref="Code"/> carries the raw value so
/// callers can branch on specific results.
/// </summary>
/// <remarks>Creates an exception describing a failed <paramref name="operation"/>.</remarks>
public sealed class ProsperoException(string operation, int code) : Exception($"{operation} failed (0x{unchecked((uint)code):X8}).")
{

    /// <summary>The name of the operation that failed.</summary>
    public string Operation { get; } = operation;

    /// <summary>The raw result code.</summary>
    public int Code { get; } = code;
}
