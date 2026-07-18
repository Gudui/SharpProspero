// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop;
using SharpProspero.Interop.Sysmodule;
using Native = SharpProspero.Interop.AppContent.AppContent;
using NativeBoot = SharpProspero.Interop.AppContent.SceAppContentBootParam;
using NativeParam = SharpProspero.Interop.AppContent.SceAppContentInitParam;

namespace SharpProspero.Platform;

/// <summary>
/// The application-content service: read the parameters the title was packaged with. Bring it up once
/// at startup, then read a parameter by its id.
/// </summary>
public static unsafe class AppContent
{
    private static bool _initialized;

    /// <summary>
    /// Starts the service. Safe to call more than once; the first call does the work.
    /// </summary>
    /// <exception cref="ProsperoException">The service could not be started.</exception>
    public static void Initialize()
    {
        if (_initialized)
            return;
        SceResult.ThrowIfFailed(
            Sysmodule.sceSysmoduleLoadModule((ushort)SystemModuleId.AppContent),
            "sceSysmoduleLoadModule(AppContent)");

        NativeParam init = default;
        NativeBoot boot = default;
        SceResult.ThrowIfFailed(Native.sceAppContentInitialize(&init, &boot), nameof(Native.sceAppContentInitialize));
        _initialized = true;
    }

    /// <summary>
    /// Reads an integer application parameter. Parameter ids 1 to 4 are the user-defined parameters a
    /// title carries in its metadata.
    /// </summary>
    /// <exception cref="ProsperoException">The parameter could not be read.</exception>
    public static int GetIntParam(int paramId)
    {
        Initialize();
        int value = 0;
        SceResult.ThrowIfFailed(Native.sceAppContentAppParamGetInt(paramId, &value), nameof(Native.sceAppContentAppParamGetInt));
        return value;
    }
}
