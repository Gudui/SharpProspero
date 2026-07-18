// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System.Runtime.InteropServices;

namespace SharpProspero.Interop.AppContent;

/// <summary>The parameters the app-content service is initialized with. Reserved; pass zeroed.</summary>
[StructLayout(LayoutKind.Sequential, Size = 32)]
public unsafe struct SceAppContentInitParam
{
    private fixed byte _reserved[32];
}

/// <summary>The boot parameters the service fills in at initialization.</summary>
[StructLayout(LayoutKind.Sequential, Size = 40)]
public unsafe struct SceAppContentBootParam
{
    private fixed byte _reserved1[4];

    /// <summary>The boot attribute the service reports.</summary>
    public uint Attr;

    private fixed byte _reserved2[32];
}

/// <summary>Additional-content and application-parameter bindings.</summary>
public static unsafe partial class AppContent
{
    private const string Lib = "libSceAppContent";

    /// <summary>The first user-defined application parameter.</summary>
    public const int AppParamUserDefined1 = 1;

    /// <summary>Starts the app-content service, filling in <paramref name="bootParam"/>.</summary>
    [LibraryImport(Lib)]
    public static partial int sceAppContentInitialize(SceAppContentInitParam* initParam, SceAppContentBootParam* bootParam);

    /// <summary>Reads an integer application parameter into <paramref name="value"/>.</summary>
    [LibraryImport(Lib)]
    public static partial int sceAppContentAppParamGetInt(int paramId, int* value);
}
