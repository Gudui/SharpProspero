// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System.Runtime.InteropServices;

namespace SharpProspero.Interop.SystemService;

/// <summary>
/// System-service bindings. Covers the small set an application module needs at startup: dismissing
/// the boot splash and reading system parameters.
/// </summary>
public static unsafe partial class SystemService
{
    private const string Lib = "libSceSystemService";

    /// <summary>Parameter id for the system language.</summary>
    public const int ParamIdLanguage = 1;

    /// <summary>Parameter id for the date display format.</summary>
    public const int ParamIdDateFormat = 2;

    /// <summary>Parameter id for the time display format.</summary>
    public const int ParamIdTimeFormat = 3;

    /// <summary>Parameter id for the time-zone offset in minutes.</summary>
    public const int ParamIdTimeZone = 4;

    /// <summary>Parameter id for the summer-time flag.</summary>
    public const int ParamIdSummerTime = 5;

    /// <summary>Parameter id for the console's system name (a string).</summary>
    public const int ParamIdSystemName = 6;

    /// <summary>Event type: the application has resumed from a suspended state.</summary>
    public const int EventOnResume = 0x10000000;

    /// <summary>Event type: another application was launched over this one.</summary>
    public const int EventLaunchApp = 0x10000007;

    /// <summary>An internal failure inside the service. Value 0x80A10001.</summary>
    public const int ErrorInternal = unchecked((int)0x80A10001);

    /// <summary>The service cannot be used in the current state. Value 0x80A10002.</summary>
    public const int ErrorUnavailable = unchecked((int)0x80A10002);

    /// <summary>An argument was outside what the call accepts. Value 0x80A10003.</summary>
    public const int ErrorParameter = unchecked((int)0x80A10003);

    /// <summary>Nothing is waiting in the event queue. Value 0x80A10004.</summary>
    public const int ErrorNoEvent = unchecked((int)0x80A10004);

    /// <summary>The call was refused for the calling process. Value 0x80A10005.</summary>
    public const int ErrorRejected = unchecked((int)0x80A10005);

    /// <summary>The safe-area ratio has not been chosen yet. Value 0x80A10006.</summary>
    public const int ErrorNeedDisplaySafeAreaSettings = unchecked((int)0x80A10006);

    /// <summary>GPU load emulation off, so the GPU runs at its own pace.</summary>
    public const int GpuLoadEmulationModeOff = 0;

    /// <summary>GPU load emulation at the standard profile.</summary>
    public const int GpuLoadEmulationModeNormal = 1;

    /// <summary>Removes the boot splash so the first rendered frame is shown.</summary>
    /// <returns>Zero on success, or a negative error code.</returns>
    [LibraryImport(Lib)]
    public static partial int sceSystemServiceHideSplashScreen();

    /// <summary>Reads an integer system parameter identified by <paramref name="paramId"/>.</summary>
    /// <returns>Zero on success, or a negative error code.</returns>
    [LibraryImport(Lib)]
    public static partial int sceSystemServiceParamGetInt(int paramId, int* value);

    /// <summary>Reads a string system parameter into <paramref name="buf"/> of <paramref name="bufSize"/> bytes.</summary>
    /// <returns>Zero on success, or a negative error code.</returns>
    [LibraryImport(Lib)]
    public static partial int sceSystemServiceParamGetString(int paramId, byte* buf, nuint bufSize);

    /// <summary>
    /// Starts the installed application <paramref name="titleId"/> (a 9-character id). The running
    /// application is replaced by it. <paramref name="argv"/> is a null-terminated array of argument
    /// strings, or null; <paramref name="param"/> carries launch options, or null for the defaults.
    /// </summary>
    /// <returns>Zero on success, or a negative error code.</returns>
    [LibraryImport(Lib)]
    public static partial int sceSystemServiceLaunchApp(byte* titleId, byte** argv, void* param);

    /// <summary>
    /// Replaces the running module with the executable at <paramref name="path"/>. <paramref name="argv"/>
    /// is a null-terminated array of argument strings, or null.
    /// </summary>
    /// <returns>Zero on success, or a negative error code.</returns>
    [LibraryImport(Lib)]
    public static partial int sceSystemServiceLoadExec(byte* path, byte** argv);

    /// <summary>Resets the idle-shutdown timer, keeping the console awake during a long operation.</summary>
    /// <returns>Zero on success, or a negative error code.</returns>
    [LibraryImport(Lib)]
    public static partial int sceSystemServicePowerTick();

    /// <summary>Reads the next pending system event, if any, into <paramref name="event"/>.</summary>
    /// <returns>Zero on success, or a negative error code (including a no-event code).</returns>
    [LibraryImport(Lib)]
    public static partial int sceSystemServiceReceiveEvent(SceSystemServiceEvent* @event);

    /// <summary>Reads the current service status into <paramref name="status"/>.</summary>
    /// <returns>Zero on success, or a negative error code.</returns>
    [LibraryImport(Lib)]
    public static partial int sceSystemServiceGetStatus(SceSystemServiceStatus* status);

    /// <summary>Reads the display's safe-area ratio into <paramref name="info"/>.</summary>
    /// <returns>Zero on success, or a negative error code.</returns>
    [LibraryImport(Lib)]
    public static partial int sceSystemServiceGetDisplaySafeAreaInfo(SceSystemServiceDisplaySafeAreaInfo* info);

    /// <summary>
    /// Selects the GPU load emulation profile, one of the <c>GpuLoadEmulationMode*</c> values. The
    /// profile shapes how much GPU work the system lets through, which is what the machine's power and
    /// heat budget follows.
    /// </summary>
    /// <returns>Zero on success, or a negative error code.</returns>
    [LibraryImport(Lib)]
    public static partial int sceSystemServiceSetGpuLoadEmulationMode(int mode);

    /// <summary>
    /// Reads the GPU load emulation profile in effect. Returns one of the <c>GpuLoadEmulationMode*</c>
    /// values rather than a result code, and reports the standard profile when the state is unreadable.
    /// </summary>
    [LibraryImport(Lib)]
    public static partial int sceSystemServiceGetGpuLoadEmulationMode();

    /// <summary>
    /// Ends the process and records it as an abnormal termination. <paramref name="info"/> must be
    /// null; any other value is refused with <see cref="ErrorParameter"/>. On acceptance the call does
    /// not return, because the process is raised into a fault and torn down.
    /// </summary>
    /// <returns>A negative error code. It never returns on acceptance.</returns>
    [LibraryImport(Lib)]
    public static partial int sceSystemServiceReportAbnormalTermination(void* info);

    /// <summary>Stops the background media player, so this module has the audio output to itself.</summary>
    /// <returns>Zero on success, or a negative error code.</returns>
    [LibraryImport(Lib)]
    public static partial int sceSystemServiceDisableMediaPlay();

    /// <summary>Allows the background media player to run again.</summary>
    /// <returns>Zero on success, or a negative error code.</returns>
    [LibraryImport(Lib)]
    public static partial int sceSystemServiceReenableMediaPlay();

    /// <summary>
    /// Returns the application id of the foreground game. A negative value means no game
    /// is running.
    /// </summary>
    [LibraryImport(Lib)]
    public static partial int sceSystemServiceGetAppIdOfRunningBigApp();

    /// <summary>
    /// Terminates the application with the given <paramref name="appId"/>.
    /// </summary>
    /// <param name="appId">Application id (from <see cref="sceSystemServiceGetAppIdOfRunningBigApp"/>).</param>
    /// <param name="opt">Option flags (typically -1).</param>
    /// <param name="method">Termination method (0 for graceful, 1 for forced).</param>
    /// <param name="reason">Reason code (0 for no reason).</param>
    /// <returns>Zero on success, or a negative error code.</returns>
    [LibraryImport(Lib)]
    public static partial int sceSystemServiceKillApp(uint appId, int opt, int method, int reason);

    /// <summary>
    /// Navigates the shell to the home screen, dismissing the foreground application.
    /// </summary>
    /// <returns>Zero on success, or a negative error code.</returns>
    [LibraryImport(Lib)]
    public static partial int sceSystemServiceNavigateToGoHome();

    /// <summary>
    /// Converts a title identifier string to its application id.
    /// </summary>
    /// <param name="titleId">A NUL-terminated UTF-8 title identifier (e.g. "PPSA01234\0").</param>
    /// <returns>A non-negative application id on success, or a negative error code.</returns>
    [LibraryImport(Lib)]
    public static partial int sceSystemServiceGetAppId(byte* titleId);

    /// <summary>
    /// Converts an application id to its title identifier string.
    /// </summary>
    /// <param name="appId">The application id.</param>
    /// <param name="titleId">A buffer of at least 10 bytes to receive the NUL-terminated title id.</param>
    /// <returns>Zero on success, or a negative error code.</returns>
    [LibraryImport(Lib)]
    public static partial int sceSystemServiceGetAppTitleId(int appId, byte* titleId);

    /// <summary>
    /// Reads the title identifier for an application id through the launch-notification utility
    /// path.
    /// </summary>
    /// <param name="appId">The application id.</param>
    /// <param name="titleId">A buffer of at least 10 bytes to receive the NUL-terminated title id.</param>
    [LibraryImport(Lib)]
    public static partial void sceLncUtilGetAppTitleId(uint appId, byte* titleId);

    /// <summary>
    /// Terminates the application with the given <paramref name="appId"/> through the
    /// launch-notification utility path.
    /// </summary>
    /// <param name="appId">The application id.</param>
    /// <returns>Zero on success, or a non-zero error code.</returns>
    [LibraryImport(Lib)]
    public static partial uint sceLncUtilKillApp(uint appId);

    /// <summary>
    /// Terminates the application with the given <paramref name="appId"/> and a reason code
    /// through the launch-notification utility path.
    /// </summary>
    /// <param name="appId">The application id.</param>
    /// <param name="reason">Reason code for the termination.</param>
    /// <returns>Zero on success, or a negative error code.</returns>
    [LibraryImport(Lib)]
    public static partial int sceLncUtilKillAppWithReason(int appId, int reason);

    /// <summary>
    /// Launches an application with detailed launch parameters through the launch-notification
    /// utility path. Takes a <see cref="LncAppParam"/> with user id, application options,
    /// and launch flags.
    /// </summary>
    /// <param name="titleId">A NUL-terminated title identifier.</param>
    /// <param name="argv">A null-terminated array of argument strings, or null.</param>
    /// <param name="param">Launch parameters, or null for defaults.</param>
    [LibraryImport(Lib)]
    public static partial int sceLncUtilLaunchApp(byte* titleId, byte** argv, LncAppParam* param);

    /// <summary>
    /// Queries the detailed status of an application by its app id.
    /// </summary>
    [LibraryImport(Lib)]
    public static partial int sceSystemServiceGetAppStatus(void* status);

    /// <summary>
    /// Registers a local process entry with the system service.
    /// </summary>
    [LibraryImport(Lib)]
    public static partial int sceSystemServiceAddLocalProcess(void* param);

}

/// <summary>
/// Shell-core utility bindings for device management.
/// </summary>
public static unsafe partial class ShellCoreUtil
{
    private const string Lib = "libSceShellCoreUtil";

    /// <summary>
    /// Ejects a removable media device at the given path.
    /// </summary>
    [LibraryImport(Lib)]
    public static partial int sceShellCoreUtilRequestEjectDevice(byte* path);
}

/// <summary>
/// Launch parameters for <see cref="SystemService.sceLncUtilLaunchApp"/>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct LncAppParam
{
    /// <summary>Structure size in bytes. Set to <c>sizeof(LncAppParam)</c> before calling.</summary>
    public uint Size;

    /// <summary>The user id to launch on behalf of.</summary>
    public int UserId;

    /// <summary>Application option flags.</summary>
    public uint AppOpt;

    /// <summary>Crash report setting.</summary>
    public ulong CrashReport;

    /// <summary>Launch check flags (skip launch check, skip system update check, VR mode, etc.).</summary>
    public uint CheckFlag;
}

/// <summary>The current state of the system service, as <see cref="SystemService.sceSystemServiceGetStatus"/> reports it.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct SceSystemServiceStatus
{
    /// <summary>The number of events waiting to be received.</summary>
    public int EventNum;

    /// <summary>Non-zero when a system dialog is drawn over the application.</summary>
    public byte IsSystemUiOverlaid;

    /// <summary>Non-zero when the application is running in the background.</summary>
    public byte IsInBackgroundExecution;

    private fixed byte _reserved[128];
}

/// <summary>The display's safe-area ratio, as <see cref="SystemService.sceSystemServiceGetDisplaySafeAreaInfo"/> reports it.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct SceSystemServiceDisplaySafeAreaInfo
{
    /// <summary>The fraction of the screen, 0 to 1, that is safe to draw important content in.</summary>
    public float Ratio;

    private fixed byte _reserved[128];
}

/// <summary>One system event. Only the type is read here; the remaining bytes are event-specific data.</summary>
[StructLayout(LayoutKind.Sequential, Size = 8196)]
public struct SceSystemServiceEvent
{
    /// <summary>The event type, one of the <c>SystemService.Event*</c> values.</summary>
    public int EventType;
}
