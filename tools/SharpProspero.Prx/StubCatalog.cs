// SharpProspero.Prx - module inspection and stub generation.
// Copyright (C) 2026 SvenGDK

using System.Collections.Generic;

namespace SharpProspero.Prx;

/// <summary>
/// The set of modules an application module imports from, and the function names each provides. A link
/// generates one stub per entry, so a reference to any listed name resolves to the module that carries
/// it without an outside stub file. Entries cover the interop bindings and the operating-system and C
/// runtime functions the compiled image reaches. Extend an entry when a link reports a name as
/// unresolved: add it under the module that exports it.
/// </summary>
public static class StubCatalog
{
    /// <summary>One module and the names it provides.</summary>
    /// <param name="Library">The library base name (the module file is this name with a <c>.prx</c> suffix).</param>
    /// <param name="Exports">The function names the module provides.</param>
    /// <param name="ModuleVersion">
    /// The module version the library publishes, major in the high byte. An import records this, and
    /// the loader binds only when it matches, so an entry that publishes something other than the
    /// common value names it here. Most modules publish 1.1.
    /// </param>
    /// <param name="LibraryVersion">The library version the module publishes. Almost always 1.</param>
    /// <param name="ModuleName">
    /// The module name the providing module publishes, when it differs from <paramref name="Library"/>.
    /// Null means it is the same as the library name, which is the usual case.
    /// </param>
    /// <param name="Soname">
    /// The module file name the loader loads, when it differs from <c>&lt;Library&gt;.prx</c>. Null
    /// means the library name with a <c>.prx</c> suffix, which is the usual case.
    /// </param>
    public readonly record struct Entry(
        string Library,
        IReadOnlyList<string> Exports,
        ushort ModuleVersion = PrxStubEmitter.DefaultModuleVersion,
        ushort LibraryVersion = PrxStubEmitter.DefaultLibraryVersion,
        string? ModuleName = null,
        string? Soname = null);

    /// <summary>The modules the SDK links against, in order.</summary>
    public static IReadOnlyList<Entry> Core =>
    [
        new Entry("libkernel", Kernel),
        new Entry("libc", C),
        new Entry("libSceVideoOut", VideoOut),
        new Entry("libScePad", Pad),
        new Entry("libSceUserService", UserService),
        new Entry("libSceSystemService", SystemService),
        new Entry("libSceAudioOut", AudioOut),
        new Entry("libSceSysmodule", Sysmodule),
        new Entry("libScePngDec", PngDec),
        new Entry("libScePngEnc", PngEnc),
        new Entry("libSceJpegDec", JpegDec),
        new Entry("libSceJpegEnc", JpegEnc),
        new Entry("libSceAudioIn", AudioIn),
        new Entry("libSceAudiodec", Audiodec),
        new Entry("libSceM4aacEnc", M4aacEnc),
        new Entry("libSceAt9Enc", At9Enc),
        new Entry("libSceNgs2", Ngs2),
        new Entry("libSceAudio3d", Audio3d),
        new Entry("libSceAjm", Ajm),
        new Entry("libSceVideoRecording", VideoRecording),
        new Entry("libSceCesCs", Ces),
        new Entry("libSceDepth2", Depth2),
        new Entry("libSceMbus", Mbus),
        new Entry("libSceVideodec2", Videodec2),
        new Entry("libSceRtc", Rtc),
        new Entry("libSceRandom", Random),
        new Entry("libSceZlib", Zlib),
        new Entry("libSceNet", Net),
        // This library publishes module version 2.1.
        new Entry("libSceSsl", Ssl, ModuleVersion: 0x0201),
        new Entry("libSceHttp", Http),
        new Entry("libSceContentDelete", ContentDelete),
        new Entry("libSceContentExport", ContentExport),
        // This library publishes module version 1.0, like the media player and play-go.
        new Entry("libSceContentSearch", ContentSearch, ModuleVersion: 0x0100),
        // This library publishes module version 1.0, like the media player and content search.
        new Entry("libScePlayGo", PlayGo, ModuleVersion: 0x0100),
        new Entry("libSceAppContent", AppContent),
        new Entry("libSceNetCtl", NetCtl),
        // The save-data module names its file with a dot but its module and library with an
        // underscore, so the file name is given explicitly and the two names default from the library.
        new Entry("libSceSaveData_native", SaveData, Soname: "libSceSaveData.native.prx"),
        new Entry("libSceKeyboard", Keyboard),
        new Entry("libSceMouse", Mouse),
        new Entry("libSceCommonDialog", CommonDialog),
        new Entry("libSceImeDialog", ImeDialog),
        new Entry("libSceErrorDialog", ErrorDialog),
        // This module publishes its library under a name that differs from its module name and its
        // file, so all three are named: library libSceMsgDialog.native, module libSceMsgDialog, file
        // libSceMsgDialog.native.prx (the soname defaults from the library name).
        new Entry("libSceMsgDialog.native", MsgDialog, ModuleName: "libSceMsgDialog"),
        // Names split the same way as the message dialog: library libSceSaveDataDialog.native, module
        // libSceSaveDataDialog, file libSceSaveDataDialog.native.prx.
        new Entry("libSceSaveDataDialog.native", SaveDataDialog, ModuleName: "libSceSaveDataDialog"),
        new Entry("libSceWebBrowserDialog", WebBrowserDialog),
        // This library publishes module version 1.0, the one module in this set that does not use 1.1.
        new Entry("libSceAvPlayer", AvPlayer, ModuleVersion: 0x0100),
        // The font engine names its file, module and library apart the way the dialogs do: file
        // libSceFont.native.prx, module libSceFont, library libSceFont.native.
        new Entry("libSceFont.native", Font, ModuleName: "libSceFont"),
        new Entry("libSceFontFt", FontFt),
        new Entry("libSceShare", Share),
        new Entry("libSceNpTrophy2", Trophy2),
        new Entry("libSceNpUniversalDataSystem", UniversalDataSystem),
        new Entry("libSceNotification", Notification),
        new Entry("libSceBluetoothHid", BluetoothHid),
        new Entry("libSceCffMgr", CffMgr),
        new Entry("libSceAgc", Agc),
        new Entry("libSceAgcDriver", AgcDriver),
    ];

    /// <summary>
    /// Libraries the SDK resolves by name at run time rather than linking against (the package
    /// installer and USB storage). They are not linked, so they are kept out of <see cref="Core"/> and
    /// the linker never generates a stub for them; the offsets tool matches a supplied module against
    /// them as well, so a contributor can read a run-time service's coverage on a firmware. The names
    /// here are the ones the run-time wrappers resolve, kept in step with the SDK's own registry.
    /// </summary>
    public static IReadOnlyList<Entry> RuntimeResolved =>
    [
        new Entry("libSceAppInstUtil", AppInstUtil),
        new Entry("libSceUsbStorage", UsbStorage),
        new Entry("libSceAvcap2", Avcap2),
    ];

    // Kernel: direct memory, files, timing, module control, and the thread, synchronization, memory,
    // and thread-local primitives the platform layer forwards to.
    private static readonly string[] Kernel =
    [
        "sceKernelAllocateDirectMemory",
        "sceKernelReleaseDirectMemory",
        "sceKernelMapDirectMemory",
        "sceKernelMapFlexibleMemory",
        "sceKernelMapNamedFlexibleMemory",
        "sceKernelReleaseFlexibleMemory",
        "sceKernelMprotect",
        "sceKernelAvailableFlexibleMemorySize",
        "sceKernelAvailableDirectMemorySize",
        "sceKernelVirtualQuery",
        "sceKernelGetDirectMemorySize",
        "sceKernelMapNamedFlexibleMemory",
        "sceKernelMunmap",
        "sceKernelMprotect",
        "sceKernelOpen",
        "sceKernelClose",
        "sceKernelRead",
        "sceKernelWrite",
        "sceKernelLseek",
        "sceKernelGetdents",
        "sceKernelMkdir",
        "sceKernelRmdir",
        "sceKernelUnlink",
        "sceKernelRename",
        "sceKernelTruncate",
        "sceKernelCheckReachability",
        "sceKernelClockGettime",
        "sceKernelGetProcessTime",
        "sceKernelUsleep",
        "sceKernelLoadStartModule",
        "sceKernelStopUnloadModule",
        "sceKernelDlsym",
        "sceKernelGetProsperoSystemSwVersion",
        "sceKernelGetAllowedSdkVersionOnSystem",
        "sceKernelGetOpenPsId",
        "sceKernelSendNotificationRequest",
        "sysctlbyname",
        "scePthreadCreate",
        "scePthreadJoin",
        "scePthreadDetach",
        "scePthreadExit",
        "scePthreadSelf",
        "scePthreadYield",
        "scePthreadMutexInit",
        "scePthreadMutexDestroy",
        "scePthreadMutexLock",
        "scePthreadMutexTrylock",
        "scePthreadMutexUnlock",
        "scePthreadCondInit",
        "scePthreadCondDestroy",
        "scePthreadCondWait",
        "scePthreadCondSignal",
        "scePthreadCondBroadcast",
        "scePthreadKeyCreate",
        "scePthreadKeyDelete",
        "scePthreadSetspecific",
        "scePthreadGetspecific",
        // The POSIX surface the ahead-of-time runtime imports, resolved against the kernel module,
        // which publishes these names directly alongside its own.
        "read", "write", "close", "fcntl", "ioctl", "poll", "pipe", "dup2", "unlink", "flock",
        "mprotect", "munmap", "madvise", "mlock", "munlock",
        "clock_gettime", "gettimeofday", "nanosleep", "sysconf",
        "getpid", "geteuid", "kill", "waitpid", "execv",
        "sigaction", "signal", "sigemptyset", "sigaddset", "sched_yield",
        "tcgetattr", "tcsetattr", "__tls_get_addr",
        "pthread_create", "pthread_self", "pthread_kill", "pthread_sigmask",
        "pthread_key_create", "pthread_setspecific",
        "pthread_mutex_init", "pthread_mutex_destroy", "pthread_mutex_lock", "pthread_mutex_unlock",
        "pthread_mutexattr_init", "pthread_mutexattr_destroy", "pthread_mutexattr_settype",
        "pthread_cond_init", "pthread_cond_destroy", "pthread_cond_wait", "pthread_cond_timedwait",
        "pthread_cond_signal", "pthread_cond_broadcast",
        "pthread_condattr_init", "pthread_condattr_destroy", "pthread_condattr_setclock",
        "pthread_rwlock_rdlock", "pthread_rwlock_wrlock", "pthread_rwlock_unlock",
        "pthread_attr_init", "pthread_attr_destroy", "pthread_attr_setdetachstate",
        "pthread_attr_setstacksize", "pthread_attr_getstack",
        // Base names the runtime-support compat object forwards its large-file variants to, plus the
        // thread-rename entry the compat object maps the POSIX name onto.
        "open", "lseek", "mmap", "pread", "getrlimit", "fstat", "stat", "scePthreadRename",
        "ftruncate", "pwrite", "setrlimit", "lstat", "pwritev", "preadv",
        // Further POSIX names the runtime archives reference, published by the kernel module.
        "access", "chdir", "chmod", "dlopen", "dlsym", "environ", "execve", "fchmod", "fsync",
        "getegid", "getgroups", "getpriority", "getrusage", "getsid", "getsockopt", "mkdir", "msync",
        "pthread_setcancelstate", "rename", "rmdir", "seteuid", "setgroups", "setpriority", "setuid",
        "shm_open", "shm_unlink", "sigfillset", "sync",
    ];

    // C runtime: the allocation, memory, string, control, formatting, and unwind functions the
    // compiled image and its runtime reach.
    private static readonly string[] C =
    [
        "malloc", "calloc", "realloc", "free", "aligned_alloc", "posix_memalign",
        "memcpy", "memmove", "memset", "memcmp", "memchr",
        "strlen", "strcmp", "strncmp", "strcpy", "strncpy", "strcat", "strncat",
        "strchr", "strrchr", "strstr", "strdup", "strtol", "strtoul", "strtoll", "strtoull",
        "abort", "exit", "atexit", "_exit",
        "__cxa_atexit", "__cxa_finalize", "__cxa_begin_catch", "__cxa_end_catch",
        "__cxa_throw", "__cxa_rethrow", "__cxa_allocate_exception", "__cxa_free_exception",
        "__cxa_guard_acquire", "__cxa_guard_release",
        "__stack_chk_fail", "__error",
        "qsort", "bsearch",
        "snprintf", "vsnprintf",
        "setjmp", "longjmp",
        "_Unwind_Resume", "_Unwind_RaiseException", "_Unwind_DeleteException",
        "_Unwind_GetLanguageSpecificData", "_Unwind_GetRegionStart",
        "_Unwind_GetIP", "_Unwind_GetIPInfo", "_Unwind_SetIP",
        "_Unwind_GetGR", "_Unwind_SetGR", "_Unwind_GetCFA",
        "_Unwind_GetDataRelBase", "_Unwind_GetTextRelBase", "_Unwind_Backtrace",
        // The C-library calls the ahead-of-time runtime imports, resolved against the C module.
        "asprintf", "sscanf", "fprintf", "fputs", "fwrite", "fclose", "fflush",
        "opendir", "closedir", "getcwd", "getenv", "strerror", "strerror_r",
        "strcasecmp", "strtok_r", "time", "log", "lrand48", "srand48", "stderr",
        // Base names the runtime-support compat object forwards its large-file variants to.
        "fopen", "readdir",
        // Further C-library names the runtime archives reference.
        "bcmp", "malloc_usable_size", "realpath", "stdout", "syslog",
        // Math the runtime and a drawing application reach, published by the C module.
        "floor", "floorf", "ceil", "ceilf", "round", "roundf", "trunc", "truncf",
        "fabs", "fabsf", "fmod", "fmodf", "sqrt", "sqrtf", "pow", "powf", "exp", "expf",
        "logf", "sin", "sinf", "cos", "cosf", "tan", "tanf", "atan2", "atan2f",
    ];

    private static readonly string[] VideoOut =
    [
        "sceVideoOutOpen",
        "sceVideoOutClose",
        "sceVideoOutSetBufferAttribute2",
        "sceVideoOutRegisterBuffers2",
        "sceVideoOutUnregisterBuffers",
        "sceVideoOutSetFlipRate",
        "sceVideoOutSubmitFlip",
        "sceVideoOutIsFlipPending",
        "sceVideoOutWaitVblank",
    ];

    private static readonly string[] Pad =
    [
        "scePadInit",
        "scePadOpen",
        "scePadClose",
        "scePadReadState",
        "scePadSetVibration",
        "scePadSetLightBar",
        "scePadResetLightBar",
    ];

    private static readonly string[] UserService =
    [
        "sceUserServiceInitialize",
        "sceUserServiceTerminate",
        "sceUserServiceGetInitialUser",
        "sceUserServiceGetLoginUserIdList",
        "sceUserServiceGetUserName",
        "sceUserServiceGetUserNumber",
    ];

    private static readonly string[] SystemService =
    [
        "sceSystemServiceHideSplashScreen",
        "sceSystemServiceParamGetInt",
        "sceSystemServiceParamGetString",
        "sceSystemServiceLaunchApp",
        "sceSystemServiceLoadExec",
        "sceSystemServicePowerTick",
        "sceSystemServiceReceiveEvent",
        "sceSystemServiceGetStatus",
        "sceSystemServiceGetDisplaySafeAreaInfo",
        "sceSystemServiceDisableMediaPlay",
        "sceSystemServiceReenableMediaPlay",
    ];

    private static readonly string[] AudioOut =
    [
        "sceAudioOutInit",
        "sceAudioOutOpen",
        "sceAudioOutClose",
        "sceAudioOutOutput",
        "sceAudioOutSetVolume",
    ];

    private static readonly string[] Sysmodule =
    [
        "sceSysmoduleLoadModule",
        "sceSysmoduleUnloadModule",
        "sceSysmoduleIsLoaded",
    ];

    private static readonly string[] PngDec =
    [
        "scePngDecCreate",
        "scePngDecDelete",
        "scePngDecParseHeader",
        "scePngDecQueryMemorySize",
        "scePngDecDecode",
    ];

    private static readonly string[] PngEnc =
    [
        "scePngEncQueryMemorySize",
        "scePngEncCreate",
        "scePngEncEncode",
        "scePngEncDelete",
    ];

    // The scalable-font engine.
    private static readonly string[] Font =
    [
        "sceFontMemoryInit",
        "sceFontMemoryTerm",
        "sceFontCreateLibraryWithEdition",
        "sceFontDestroyLibrary",
        "sceFontSupportExternalFonts",
        "sceFontOpenFontMemory",
        "sceFontCloseFont",
        "sceFontCreateRendererWithEdition",
        "sceFontDestroyRenderer",
        "sceFontBindRenderer",
        "sceFontUnbindRenderer",
        "sceFontSetupRenderScalePixel",
        "sceFontGetRenderCharGlyphMetrics",
        "sceFontRenderCharGlyphImageHorizontal",
        "sceFontRenderSurfaceInit",
        "sceFontRenderSurfaceSetScissor",
    ];

    // The FreeType backend for the font engine.
    private static readonly string[] FontFt =
    [
        "sceFontSelectLibraryFt",
        "sceFontSelectRendererFt",
    ];

    // Screenshot and video-clip capture of the composited output.
    private static readonly string[] Share =
    [
        "sceShareInitialize",
        "sceShareTerminate",
        "sceShareCaptureScreenshot",
        "sceShareCaptureVideoClip",
        "sceShareGetCurrentStatus",
        "sceShareSetScreenshotOverlayImage",
        "sceShareFeaturePermit",
        "sceShareFeatureProhibit",
    ];

    private static readonly string[] Trophy2 =
    [
        "sceNpTrophy2CreateHandle",
        "sceNpTrophy2DestroyHandle",
        "sceNpTrophy2AbortHandle",
        "sceNpTrophy2CreateContext",
        "sceNpTrophy2DestroyContext",
        "sceNpTrophy2RegisterContext",
        "sceNpTrophy2RegisterUnlockCallback",
        "sceNpTrophy2UnregisterUnlockCallback",
        "sceNpTrophy2GetGameInfo",
        "sceNpTrophy2GetGroupInfo",
        "sceNpTrophy2GetGroupInfoArray",
        "sceNpTrophy2GetTrophyInfo",
        "sceNpTrophy2GetTrophyInfoArray",
        "sceNpTrophy2GetGameIcon",
        "sceNpTrophy2GetGroupIcon",
        "sceNpTrophy2GetTrophyIcon",
        "sceNpTrophy2GetRewardIcon",
        "sceNpTrophy2ShowTrophyList",
    ];

    private static readonly string[] UniversalDataSystem =
    [
        "sceNpUniversalDataSystemInitialize",
        "sceNpUniversalDataSystemTerminate",
        "sceNpUniversalDataSystemGetMemoryStat",
        "sceNpUniversalDataSystemCreateContext",
        "sceNpUniversalDataSystemDestroyContext",
        "sceNpUniversalDataSystemRegisterContext",
        "sceNpUniversalDataSystemCreateHandle",
        "sceNpUniversalDataSystemDestroyHandle",
        "sceNpUniversalDataSystemAbortHandle",
        "sceNpUniversalDataSystemPostEvent",
        "sceNpUniversalDataSystemCreateEvent",
        "sceNpUniversalDataSystemDestroyEvent",
        "sceNpUniversalDataSystemEventEstimateSize",
        "sceNpUniversalDataSystemEventToString",
        "sceNpUniversalDataSystemCreateEventPropertyObject",
        "sceNpUniversalDataSystemDestroyEventPropertyObject",
        "sceNpUniversalDataSystemEventPropertyObjectSetString",
        "sceNpUniversalDataSystemEventPropertyObjectSetInt32",
        "sceNpUniversalDataSystemEventPropertyObjectSetUInt32",
        "sceNpUniversalDataSystemEventPropertyObjectSetInt64",
        "sceNpUniversalDataSystemEventPropertyObjectSetUInt64",
        "sceNpUniversalDataSystemEventPropertyObjectSetFloat32",
        "sceNpUniversalDataSystemEventPropertyObjectSetFloat64",
        "sceNpUniversalDataSystemEventPropertyObjectSetBool",
        "sceNpUniversalDataSystemEventPropertyObjectSetBinary",
        "sceNpUniversalDataSystemEventPropertyObjectSetObject",
        "sceNpUniversalDataSystemEventPropertyObjectSetArray",
        "sceNpUniversalDataSystemCreateEventPropertyArray",
        "sceNpUniversalDataSystemDestroyEventPropertyArray",
        "sceNpUniversalDataSystemEventPropertyArraySetString",
        "sceNpUniversalDataSystemEventPropertyArraySetInt32",
        "sceNpUniversalDataSystemEventPropertyArraySetUInt32",
        "sceNpUniversalDataSystemEventPropertyArraySetInt64",
        "sceNpUniversalDataSystemEventPropertyArraySetUInt64",
        "sceNpUniversalDataSystemEventPropertyArraySetFloat32",
        "sceNpUniversalDataSystemEventPropertyArraySetFloat64",
        "sceNpUniversalDataSystemEventPropertyArraySetBool",
        "sceNpUniversalDataSystemEventPropertyArraySetBinary",
        "sceNpUniversalDataSystemEventPropertyArraySetObject",
        "sceNpUniversalDataSystemEventPropertyArraySetArray",
        "sceNpUniversalDataSystemGetStorageStat",
    ];

    private static readonly string[] Notification =
    [
        "sceNotificationSend",
        "sceNotificationSendById",
        "sceNotificationShowPsButtonPersistentBanner",
        "sceNotificationHidePsButtonPersistentBanner",
    ];

    private static readonly string[] BluetoothHid =
    [
        "sceBluetoothHidInit",
        "sceBluetoothHidParamInitialize",
        "sceBluetoothHidThreadParamInitialize",
        "sceBluetoothHidRegisterCallback",
        "sceBluetoothHidUnregisterCallback",
        "sceBluetoothHidRegisterDevice",
        "sceBluetoothHidUnregisterDevice",
        "sceBluetoothHidGetReportDescriptor",
        "sceBluetoothHidGetDeviceName",
        "sceBluetoothHidGetDeviceInfo",
        "sceBluetoothHidGetInputReport",
        "sceBluetoothHidGetFeatureReport",
        "sceBluetoothHidSetOutputReport",
        "sceBluetoothHidSetFeatureReport",
        "sceBluetoothHidInterruptOutput",
        "sceBluetoothHidDisconnectDevice",
        "sceBluetoothHidDebugGetVersion",
    ];

    private static readonly string[] CffMgr =
    [
        "sceConsoleFeatureFlagManagerIsOn",
        "sceConsoleFeatureFlagManagerIsWaitingReboot",
    ];

    private static readonly string[] JpegDec =
    [
        "sceJpegDecCreate",
        "sceJpegDecDelete",
        "sceJpegDecParseHeader",
        "sceJpegDecQueryMemorySize",
        "sceJpegDecDecode",
    ];

    private static readonly string[] JpegEnc =
    [
        "sceJpegEncQueryMemorySize",
        "sceJpegEncCreate",
        "sceJpegEncEncode",
        "sceJpegEncDelete",
    ];

    // Microphone and other audio capture.
    private static readonly string[] AudioIn =
    [
        "sceAudioInOpen",
        "sceAudioInInput",
        "sceAudioInGetSilentState",
        "sceAudioInClose",
    ];

    private static readonly string[] Audiodec =
    [
        "sceAudiodecInitLibrary",
        "sceAudiodecTermLibrary",
        "sceAudiodecCreateDecoder",
        "sceAudiodecDeleteDecoder",
        "sceAudiodecDecode",
        "sceAudiodecClearContext",
    ];

    private static readonly string[] M4aacEnc =
    [
        "sceM4aacEncCreateEncoder",
        "sceM4aacEncDeleteEncoder",
        "sceM4aacEncEncode",
        "sceM4aacEncFlush",
        "sceM4aacEncClearContext",
    ];

    private static readonly string[] At9Enc =
    [
        "sceAt9EncQueryMemSize",
        "sceAt9EncCreateEncoder",
        "sceAt9EncEncode",
        "sceAt9EncFlush",
        "sceAt9EncClearContext",
    ];

    private static readonly string[] Ngs2 =
    [
        "sceNgs2SystemEnumHandles",
        "sceNgs2SystemResetOption",
        "sceNgs2SystemQueryBufferSize",
        "sceNgs2SystemCreate",
        "sceNgs2SystemCreateWithAllocator",
        "sceNgs2SystemDestroy",
        "sceNgs2SystemRunCommands",
        "sceNgs2SystemQueryInfo",
        "sceNgs2SystemLock",
        "sceNgs2SystemUnlock",
        "sceNgs2SystemSetUserData",
        "sceNgs2SystemGetUserData",
        "sceNgs2SystemGetInfo",
        "sceNgs2SystemSetGrainSamples",
        "sceNgs2SystemSetSampleRate",
        "sceNgs2SystemEnumRackHandles",
        "sceNgs2SystemRender",
        "sceNgs2RackQueryBufferSize",
        "sceNgs2RackCreate",
        "sceNgs2RackCreateWithAllocator",
        "sceNgs2RackDestroy",
        "sceNgs2RackRunCommands",
        "sceNgs2RackQueryInfo",
        "sceNgs2RackLock",
        "sceNgs2RackUnlock",
        "sceNgs2RackSetUserData",
        "sceNgs2RackGetUserData",
        "sceNgs2RackGetInfo",
        "sceNgs2RackGetVoiceHandle",
        "sceNgs2VoiceRunCommands",
        "sceNgs2VoiceQueryInfo",
        "sceNgs2VoiceControl",
        "sceNgs2VoiceGetStateFlags",
        "sceNgs2VoiceGetState",
        "sceNgs2VoiceGetOwner",
        "sceNgs2VoiceGetMatrixInfo",
        "sceNgs2VoiceGetPortInfo",
        "sceNgs2StreamResetOption",
        "sceNgs2StreamQueryBufferSize",
        "sceNgs2StreamCreate",
        "sceNgs2StreamCreateWithAllocator",
        "sceNgs2StreamDestroy",
        "sceNgs2StreamRunCommands",
        "sceNgs2StreamQueryInfo",
        "sceNgs2ParseWaveformData",
        "sceNgs2ParseWaveformFile",
        "sceNgs2ParseWaveformUser",
        "sceNgs2CalcWaveformBlock",
        "sceNgs2GetWaveformFrameInfo",
        "sceNgs2JobSchedulerResetOption",
    ];

    private static readonly string[] Audio3d =
    [
        "sceAudio3dInitialize",
        "sceAudio3dTerminate",
        "sceAudio3dPortOpen",
        "sceAudio3dPortClose",
        "sceAudio3dPortSetAttribute",
        "sceAudio3dPortAdvance",
        "sceAudio3dPortPush",
        "sceAudio3dPortGetAttributesSupported",
        "sceAudio3dPortGetQueueLevel",
        "sceAudio3dObjectReserve",
        "sceAudio3dObjectUnreserve",
        "sceAudio3dObjectSetAttributes",
        "sceAudio3dBedWrite",
        "sceAudio3dBedWrite2",
        "sceAudio3dGetSpeakerArrayMemorySize",
        "sceAudio3dCreateSpeakerArray",
        "sceAudio3dDeleteSpeakerArray",
        "sceAudio3dGetSpeakerArrayMixCoefficients",
        "sceAudio3dGetSpeakerArrayMixCoefficients2",
        "sceAudio3dAudioOutOpen",
        "sceAudio3dAudioOutClose",
        "sceAudio3dAudioOutOutput",
        "sceAudio3dAudioOutOutputs",
        "sceAudio3dPortCreate",
        "sceAudio3dPortDestroy",
        "sceAudio3dPortFlush",
    ];

    private static readonly string[] Ajm =
    [
        "sceAjmInitialize",
        "sceAjmFinalize",
        "sceAjmMemoryRegister",
        "sceAjmMemoryUnregister",
        "sceAjmModuleRegister",
        "sceAjmModuleUnregister",
        "sceAjmInstanceCreate",
        "sceAjmInstanceExtend",
        "sceAjmInstanceSwitch",
        "sceAjmInstanceDestroy",
        "sceAjmBatchInitialize",
        "sceAjmBatchJobInitialize",
        "sceAjmBatchJobClearContext",
        "sceAjmBatchJobDecode",
        "sceAjmBatchJobDecodeSingle",
        "sceAjmBatchJobDecodeSplit",
        "sceAjmBatchJobEncode",
        "sceAjmBatchJobGetInfo",
        "sceAjmBatchJobGetCodecInfo",
        "sceAjmBatchJobSetGaplessDecode",
        "sceAjmBatchJobGetGaplessDecode",
        "sceAjmBatchJobSetResampleParameters",
        "sceAjmBatchJobSetResampleParametersEx",
        "sceAjmBatchJobGetResampleInfo",
        "sceAjmBatchStart",
        "sceAjmBatchWait",
        "sceAjmBatchCancel",
        "sceAjmBatchErrorDump",
        "sceAjmBatchJobGetStatistics",
        "sceAjmBatchJobControl",
        "sceAjmBatchJobRun",
        "sceAjmBatchJobRunSplit",
    ];

    private static readonly string[] VideoRecording =
    [
        "sceVideoRecordingGetStatus",
        "sceVideoRecordingQueryMemSize",
        "sceVideoRecordingOpen",
        "sceVideoRecordingStart",
        "sceVideoRecordingStop",
        "sceVideoRecordingClose",
    ];

    private static readonly string[] Ces =
    [
        "sceCesUtf8ToUtf16",
        "sceCesUtf16ToUtf8",
        "sceCesUtf8ToUtf32",
        "sceCesUtf32ToUtf8",
        "sceCesUtf16ToUtf32",
        "sceCesUtf32ToUtf16",
        "sceCesEucJpToUtf8",
        "sceCesEucKrToUtf8",
        "sceCesBig5ToUtf8",
        "sceCesUhcToUtf8",
    ];

    private static readonly string[] Depth2 =
    [
        "sceDepth2QueryMemory",
        "sceDepth2Initialize",
        "sceDepth2Terminate",
        "sceDepth2SetCommand",
        "sceDepth2Submit",
        "sceDepth2WaitAndExecutePostProcess",
        "sceDepth2SetRoi",
        "sceDepth2GetImage",
        "sceDepth2LoadCalibrationData",
    ];

    private static readonly string[] Mbus =
    [
        "sceDeviceServiceInitialize",
        "sceDeviceServiceTerminate",
        "sceDeviceServiceGetGeneration",
        "sceDeviceServiceGetEventState",
        "sceDeviceServiceQueryDeviceInfo_",
    ];

    private static readonly string[] Videodec2 =
    [
        "sceVideodec2QueryComputeMemoryInfo",
        "sceVideodec2AllocateComputeQueue",
        "sceVideodec2ReleaseComputeQueue",
        "sceVideodec2QueryDecoderMemoryInfo",
        "sceVideodec2CreateDecoder",
        "sceVideodec2DeleteDecoder",
        "sceVideodec2Decode",
        "sceVideodec2Flush",
        "sceVideodec2Reset",
    ];

    private static readonly string[] Rtc =
    [
        "sceRtcGetCurrentClock",
        "sceRtcGetCurrentClockLocalTime",
        "sceRtcGetCurrentTick",
        "sceRtcGetCurrentNetworkTick",
        "sceRtcGetTickResolution",
        "sceRtcConvertUtcToLocalTime",
        "sceRtcConvertLocalTimeToUtc",
        "sceRtcSetTick",
        "sceRtcGetTick",
    ];

    private static readonly string[] Random =
    [
        "sceRandomGetRandomNumber",
    ];

    // Save data: enumerate, mount, read parameters, delete.
    private static readonly string[] SaveData =
    [
        "sceSaveDataInitialize3",
        "sceSaveDataTerminate",
        "sceSaveDataMount3",
        "sceSaveDataUmount2",
        "sceSaveDataGetMountInfo",
        "sceSaveDataDelete",
        "sceSaveDataDirNameSearch",
    ];

    // Install and download progress.
    private static readonly string[] PlayGo =
    [
        "scePlayGoInitialize",
        "scePlayGoTerminate",
        "scePlayGoOpen",
        "scePlayGoClose",
        "scePlayGoGetLocus",
        "scePlayGoGetProgress",
    ];

    // Additional content and application parameters.
    private static readonly string[] AppContent =
    [
        "sceAppContentInitialize",
        "sceAppContentAppParamGetInt",
    ];

    // Content deletion (captures and other content).
    private static readonly string[] ContentDelete =
    [
        "sceContentDeleteInitialize",
        "sceContentDeleteTerminate",
        "sceContentDeleteById",
        "sceContentDeleteByPath",
    ];

    // Content export (copy a file into the content library).
    private static readonly string[] ContentExport =
    [
        "sceContentExportInit2",
        "sceContentExportTerm",
        "sceContentExportStart",
        "sceContentExportFinish",
        "sceContentExportFromFile",
        "sceContentExportCancel",
        "sceContentExportGetProgress",
    ];

    // Content search (the library of captures and imported media).
    private static readonly string[] ContentSearch =
    [
        "sceContentSearchInit",
        "sceContentSearchTerm",
        "sceContentSearchGetContentLastUpdateId",
        "sceContentSearchGetNumOfContent",
        "sceContentSearchGetTotalContentSize",
        "sceContentSearchSearchContent",
        "sceContentSearchOpenMetadata",
        "sceContentSearchOpenMetadataByContentId",
        "sceContentSearchCloseMetadata",
        "sceContentSearchGetMetadataFieldInfo",
        "sceContentSearchGetMetadataValue",
    ];

    // The network library: the memory pool the HTTP and TLS services draw from, the BSD-style socket
    // calls, the poller, and the name resolver.
    private static readonly string[] Net =
    [
        "sceNetPoolCreate",
        "sceNetPoolDestroy",
        "sceNetSocket",
        "sceNetBind",
        "sceNetListen",
        "sceNetAccept",
        "sceNetConnect",
        "sceNetSend",
        "sceNetSendto",
        "sceNetRecv",
        "sceNetRecvfrom",
        "sceNetSocketClose",
        "sceNetShutdown",
        "sceNetSocketAbort",
        "sceNetSetsockopt",
        "sceNetGetsockopt",
        "sceNetGetsockname",
        "sceNetGetpeername",
        "sceNetErrnoLoc",
        "sceNetEpollCreate",
        "sceNetEpollControl",
        "sceNetEpollWait",
        "sceNetEpollDestroy",
        "sceNetEpollAbort",
        "sceNetResolverCreate",
        "sceNetResolverStartNtoa",
        "sceNetResolverDestroy",
    ];

    // The TLS context the HTTP service uses.
    private static readonly string[] Ssl =
    [
        "sceSslInit",
        "sceSslTerm",
    ];

    // HTTP downloads.
    private static readonly string[] Http =
    [
        "sceHttpInit",
        "sceHttpTerm",
        "sceHttpCreateTemplate",
        "sceHttpDeleteTemplate",
        "sceHttpCreateConnectionWithURL",
        "sceHttpDeleteConnection",
        "sceHttpCreateRequestWithURL",
        "sceHttpDeleteRequest",
        "sceHttpSendRequest",
        "sceHttpGetStatusCode",
        "sceHttpGetResponseContentLength",
        "sceHttpReadData",
        "sceHttpSetConnectTimeOut",
        "sceHttpSetRecvTimeOut",
    ];

    // Asynchronous zlib decompression.
    private static readonly string[] Zlib =
    [
        "sceZlibInitialize",
        "sceZlibFinalize",
        "sceZlibInflate",
        "sceZlibWaitForDone",
        "sceZlibGetResult",
    ];

    // Network connection status.
    private static readonly string[] NetCtl =
    [
        "sceNetCtlInit",
        "sceNetCtlTerm",
        "sceNetCtlGetState",
        "sceNetCtlGetInfo",
    ];

    // USB keyboard input.
    private static readonly string[] Keyboard =
    [
        "sceKeyboardInit",
        "sceKeyboardOpen",
        "sceKeyboardClose",
        "sceKeyboardReadState",
        "sceKeyboardRead",
        "sceKeyboardGetKey2Char",
    ];

    // USB mouse input.
    private static readonly string[] Mouse =
    [
        "sceMouseInit",
        "sceMouseOpen",
        "sceMouseClose",
        "sceMouseRead",
    ];

    // The shared dialog subsystem every common dialog is brought up on before it opens.
    private static readonly string[] CommonDialog =
    [
        "sceCommonDialogInitialize",
        "sceCommonDialogIsUsed",
    ];

    // The system message dialog (progress bar, buttons, system messages).
    private static readonly string[] MsgDialog =
    [
        "sceMsgDialogInitialize",
        "sceMsgDialogTerminate",
        "sceMsgDialogOpen",
        "sceMsgDialogClose",
        "sceMsgDialogUpdateStatus",
        "sceMsgDialogGetStatus",
        "sceMsgDialogGetResult",
        "sceMsgDialogProgressBarInc",
        "sceMsgDialogProgressBarSetValue",
        "sceMsgDialogProgressBarSetMsg",
    ];

    // The system error dialog.
    private static readonly string[] ErrorDialog =
    [
        "sceErrorDialogInitialize",
        "sceErrorDialogTerminate",
        "sceErrorDialogOpen",
        "sceErrorDialogClose",
        "sceErrorDialogUpdateStatus",
        "sceErrorDialogGetStatus",
    ];

    // The save-data dialog (list, confirm and report on saves).
    private static readonly string[] SaveDataDialog =
    [
        "sceSaveDataDialogInitialize",
        "sceSaveDataDialogTerminate",
        "sceSaveDataDialogUpdateStatus",
        "sceSaveDataDialogGetStatus",
        "sceSaveDataDialogOpen",
        "sceSaveDataDialogGetResult",
        "sceSaveDataDialogClose",
        "sceSaveDataDialogIsReadyToDisplay",
    ];

    // The on-screen keyboard.
    private static readonly string[] ImeDialog =
    [
        "sceImeDialogInit",
        "sceImeDialogGetStatus",
        "sceImeDialogGetResult",
        "sceImeDialogAbort",
        "sceImeDialogTerm",
        "sceImeDialogGetPanelSizeExtended",
        "sceImeDialogGetPanelPositionAndForm",
    ];

    private static readonly string[] WebBrowserDialog =
    [
        "sceWebBrowserDialogInitialize",
        "sceWebBrowserDialogOpen",
        "sceWebBrowserDialogUpdateStatus",
        "sceWebBrowserDialogGetStatus",
        "sceWebBrowserDialogGetResult",
        "sceWebBrowserDialogClose",
        "sceWebBrowserDialogTerminate",
        "sceWebBrowserDialogResetCookie",
    ];

    private static readonly string[] AvPlayer =
    [
        "sceAvPlayerInit",
        "sceAvPlayerInitEx",
        "sceAvPlayerPostInit",
        "sceAvPlayerAddSource",
        "sceAvPlayerStart",
        "sceAvPlayerStop",
        "sceAvPlayerPause",
        "sceAvPlayerResume",
        "sceAvPlayerIsActive",
        "sceAvPlayerSetLooping",
        "sceAvPlayerGetAudioData",
        "sceAvPlayerGetVideoDataEx",
        "sceAvPlayerCurrentTime",
        "sceAvPlayerJumpToTime",
        "sceAvPlayerStreamCount",
        "sceAvPlayerGetStreamInfo",
        "sceAvPlayerEnableStream",
        "sceAvPlayerDisableStream",
        "sceAvPlayerChangeStream",
        "sceAvPlayerSetLogCallback",
        "sceAvPlayerClose",
    ];

    // The package installer, resolved by name at run time (Platform.PackageInstaller). Kept in step
    // with the SDK's own registry entry for the installer.
    private static readonly string[] AppInstUtil =
    [
        "sceAppInstUtilInitialize",
        "sceAppInstUtilTerminate",
        "sceAppInstUtilAppInstallPkg",
        "sceAppInstUtilAppExists",
        "sceAppInstUtilAppGetSize",
        "sceAppInstUtilAppUnInstall",
        "sceAppInstUtilAppUnInstall2",
    ];

    // USB mass storage, resolved by name at run time (Platform.UsbStorage). Kept in step with the
    // SDK's own registry entry for USB storage.
    private static readonly string[] UsbStorage =
    [
        "sceUsbStorageInit",
        "sceUsbStorageTerm",
        "sceUsbStorageGetDeviceList",
        "sceUsbStorageGetMountPointOfShellCore",
        "sceUsbStorageRequestMap",
        "sceUsbStorageRequestUnmap",
    ];

    // The system-composited AV-capture service. Resolved at run time and reached only by a process with
    // the privilege for it, so it is not linked; the names let the offsets tool report its coverage.
    private static readonly string[] Avcap2 =
    [
        "sceAvcap2Initialize",
        "sceAvcap2Terminate",
        "sceAvcap2OpenAudio",
        "sceAvcap2OpenVideo",
        "sceAvcap2Close",
        "sceAvcap2Start",
        "sceAvcap2Stop",
        "sceAvcap2ReadAudio",
        "sceAvcap2ReadVideo",
        "sceAvcap2GetFramePitch",
        "sceAvcap2GetSupportInformation",
        "sceAvcap2GetVideoOutMode",
        "sceAvcap2GetRecProhibit",
        "sceAvcap2GetNonVclNalUnitStream",
        "sceAvcap2GetNonVclNalUnitStream2",
        "sceAvcap2Select",
        "sceAvcap2MapMemory",
        "sceAvcap2LockInputDataBuffer",
        "sceAvcap2UnlockInputDataBuffer",
        "sceAvcap2SetVideoEncodeConfig",
        "sceAvcap2QueryVideoEncoderMemorySize",
        "sceAvcap2AllocateVideoEncoderMemory",
        "sceAvcap2FreeEncoderVideoMemory",
        "sceAvcap2SetInvalidFrame",
        "sceAvcap2SetPrivacyGuard",
        "sceAvcap2SetAudioOutVolume",
        "sceAvcap2SetAudioOutVolumeForRec",
        "sceAvcap2SetMicVolumeForRec",
        "sceAvcap2SetChapterOpenLevel",
        "sceAvcap2SetChapterPermissionLevel",
    ];

    private static readonly string[] Agc =
    [
        "sceAgcAcbAcquireMem",
        "sceAgcAcbAcquireMemGetSize",
        "sceAgcAcbAtomicGds",
        "sceAgcAcbAtomicGdsGetSize",
        "sceAgcAcbAtomicMem",
        "sceAgcAcbAtomicMemGetSize",
        "sceAgcAcbCondExec",
        "sceAgcAcbCondExecGetSize",
        "sceAgcAcbCopyData",
        "sceAgcAcbCopyDataGetSize",
        "sceAgcAcbDispatchIndirect",
        "sceAgcAcbDispatchIndirectGetSize",
        "sceAgcAcbDmaData",
        "sceAgcAcbDmaDataGetSize",
        "sceAgcAcbEventWrite",
        "sceAgcAcbEventWriteGetSize",
        "sceAgcAcbJump",
        "sceAgcAcbJumpGetSize",
        "sceAgcAcbMemSemaphore",
        "sceAgcAcbPopMarker",
        "sceAgcAcbPrimeUtcl2",
        "sceAgcAcbPrimeUtcl2GetSize",
        "sceAgcAcbPushMarker",
        "sceAgcAcbQueueEndOfShaderActionGetSize",
        "sceAgcAcbResetQueue",
        "sceAgcAcbRewind",
        "sceAgcAcbRewindGetSize",
        "sceAgcAcbSetFlip",
        "sceAgcAcbSetMarker",
        "sceAgcAcbWaitOnAddressGetSize",
        "sceAgcAcbWaitRegMem",
        "sceAgcAcbWaitUntilSafeForRendering",
        "sceAgcAcbWriteData",
        "sceAgcAsyncCondExecPatchSetCommandAddress",
        "sceAgcAsyncCondExecPatchSetEnd",
        "sceAgcAsyncRewindPatchSetRewindState",
        "sceAgcBranchPatchSetCompareAddress",
        "sceAgcBranchPatchSetElseTarget",
        "sceAgcBranchPatchSetThenTarget",
        "sceAgcCbBranch",
        "sceAgcCbBranchGetSize",
        "sceAgcCbCondWrite",
        "sceAgcCbCondWriteGetSize",
        "sceAgcCbDispatch",
        "sceAgcCbDispatchGetSize",
        "sceAgcCbMemSemaphore",
        "sceAgcCbNop",
        "sceAgcCbNopGetSize",
        "sceAgcCbQueueEndOfPipeActionGetSize",
        "sceAgcCbReleaseMem",
        "sceAgcCbSetShRegisterRangeDirect",
        "sceAgcCbSetShRegisterRangeDirectGetSize",
        "sceAgcCbSetShRegistersDirect",
        "sceAgcCbSetShRegistersDirectGetSize",
        "sceAgcCbSetUcRegisterRangeDirect",
        "sceAgcCbSetUcRegisterRangeDirectGetSize",
        "sceAgcCbSetUcRegistersDirect",
        "sceAgcCbSetUcRegistersDirectGetSize",
        "sceAgcCondExecPatchSetCommandAddress",
        "sceAgcCondExecPatchSetEnd",
        "sceAgcCreateInterpolantMapping",
        "sceAgcCreatePrimState",
        "sceAgcCreateShader",
        "sceAgcDcbAcquireMem",
        "sceAgcDcbAcquireMemGetSize",
        "sceAgcDcbAtomicGds",
        "sceAgcDcbAtomicGdsGetSize",
        "sceAgcDcbAtomicMem",
        "sceAgcDcbAtomicMemGetSize",
        "sceAgcDcbBeginOcclusionQueryGetSize",
        "sceAgcDcbClearState",
        "sceAgcDcbCondExec",
        "sceAgcDcbCondExecGetSize",
        "sceAgcDcbContextStateOp",
        "sceAgcDcbContextStateOpGetSize",
        "sceAgcDcbCopyData",
        "sceAgcDcbCopyDataGetSize",
        "sceAgcDcbDispatchIndirect",
        "sceAgcDcbDispatchIndirectGetSize",
        "sceAgcDcbDmaData",
        "sceAgcDcbDmaDataGetSize",
        "sceAgcDcbDrawIndex",
        "sceAgcDcbDrawIndexAuto",
        "sceAgcDcbDrawIndexAutoGetSize",
        "sceAgcDcbDrawIndexGetSize",
        "sceAgcDcbDrawIndexIndirect",
        "sceAgcDcbDrawIndexIndirectGetSize",
        "sceAgcDcbDrawIndexIndirectMulti",
        "sceAgcDcbDrawIndexIndirectMultiGetSize",
        "sceAgcDcbDrawIndexMultiInstanced",
        "sceAgcDcbDrawIndexMultiInstancedGetSize",
        "sceAgcDcbDrawIndexOffset",
        "sceAgcDcbDrawIndexOffsetGetSize",
        "sceAgcDcbDrawIndirect",
        "sceAgcDcbDrawIndirectGetSize",
        "sceAgcDcbDrawIndirectMulti",
        "sceAgcDcbDrawIndirectMultiGetSize",
        "sceAgcDcbEndOcclusionQueryGetSize",
        "sceAgcDcbEventWrite",
        "sceAgcDcbEventWriteGetSize",
        "sceAgcDcbGetLodStats",
        "sceAgcDcbGetLodStatsGetSize",
        "sceAgcDcbJump",
        "sceAgcDcbJumpGetSize",
        "sceAgcDcbMemSemaphore",
        "sceAgcDcbPopMarker",
        "sceAgcDcbPrimeUtcl2",
        "sceAgcDcbPrimeUtcl2GetSize",
        "sceAgcDcbPushMarker",
        "sceAgcDcbQueueEndOfShaderActionGetSize",
        "sceAgcDcbResetQueue",
        "sceAgcDcbRewind",
        "sceAgcDcbRewindGetSize",
        "sceAgcDcbSetBaseDispatchIndirectArgsGetSize",
        "sceAgcDcbSetBaseDrawIndirectArgsGetSize",
        "sceAgcDcbSetBaseIndirectArgs",
        "sceAgcDcbSetBoolPredicationEnableGetSize",
        "sceAgcDcbSetCfRegisterDirect",
        "sceAgcDcbSetCfRegisterRangeDirect",
        "sceAgcDcbSetCxRegisterDirect",
        "sceAgcDcbSetCxRegisterDirectGetSize",
        "sceAgcDcbSetCxRegistersIndirect",
        "sceAgcDcbSetCxRegistersIndirectGetSize",
        "sceAgcDcbSetFlip",
        "sceAgcDcbSetIndexBuffer",
        "sceAgcDcbSetIndexBufferGetSize",
        "sceAgcDcbSetIndexCount",
        "sceAgcDcbSetIndexCountGetSize",
        "sceAgcDcbSetIndexIndirectArgs",
        "sceAgcDcbSetIndexIndirectArgsGetSize",
        "sceAgcDcbSetIndexSize",
        "sceAgcDcbSetIndexSizeGetSize",
        "sceAgcDcbSetMarker",
        "sceAgcDcbSetNumInstances",
        "sceAgcDcbSetNumInstancesGetSize",
        "sceAgcDcbSetPredication",
        "sceAgcDcbSetPredicationDisableGetSize",
        "sceAgcDcbSetShRegisterDirect",
        "sceAgcDcbSetShRegisterDirectGetSize",
        "sceAgcDcbSetShRegistersIndirect",
        "sceAgcDcbSetShRegistersIndirectGetSize",
        "sceAgcDcbSetUcRegisterDirect",
        "sceAgcDcbSetUcRegisterDirectGetSize",
        "sceAgcDcbSetUcRegistersIndirect",
        "sceAgcDcbSetUcRegistersIndirectGetSize",
        "sceAgcDcbSetZPassPredicationEnableGetSize",
        "sceAgcDcbStallCommandBufferParser",
        "sceAgcDcbStallCommandBufferParserGetSize",
        "sceAgcDcbWaitOnAddressGetSize",
        "sceAgcDcbWaitRegMem",
        "sceAgcDcbWaitUntilSafeForRendering",
        "sceAgcDcbWriteData",
        "sceAgcDcbWriteDataGetSize",
        "sceAgcDmaDataPatchSetDstAddressOrOffset",
        "sceAgcDmaDataPatchSetSrcAddressOrOffsetOrImmediate",
        "sceAgcFuseShaderHalves",
        "sceAgcGetDataPacketPayloadAddress",
        "sceAgcGetFusedShaderSize",
        "sceAgcGetPacketSize",
        "sceAgcGetRegisterDefaults",
        "sceAgcGetRegisterDefaults2",
        "sceAgcGetRegisterDefaults2Internal",
        "sceAgcGetRegisterDefaultsInternal",
        "sceAgcInit",
        "sceAgcJumpPatchSetTarget",
        "sceAgcLinkShaders",
        "sceAgcQueueEndOfPipeActionPatchAddress",
        "sceAgcQueueEndOfPipeActionPatchData",
        "sceAgcQueueEndOfPipeActionPatchGcrCntl",
        "sceAgcQueueEndOfPipeActionPatchType",
        "sceAgcRewindPatchSetRewindState",
        "sceAgcSetCxRegIndirectPatchAddRegisters",
        "sceAgcSetCxRegIndirectPatchSetAddress",
        "sceAgcSetCxRegIndirectPatchSetNumRegisters",
        "sceAgcSetNop",
        "sceAgcSetPacketPredication",
        "sceAgcSetRangePredication",
        "sceAgcSetShRegIndirectPatchAddRegisters",
        "sceAgcSetShRegIndirectPatchSetAddress",
        "sceAgcSetShRegIndirectPatchSetNumRegisters",
        "sceAgcSetSubmitMode",
        "sceAgcSetUcRegIndirectPatchAddRegisters",
        "sceAgcSetUcRegIndirectPatchSetAddress",
        "sceAgcSetUcRegIndirectPatchSetNumRegisters",
        "sceAgcSuspendPoint",
        "sceAgcSuspendPointAndCheckStatus",
        "sceAgcUpdateInterpolantMapping",
        "sceAgcUpdatePrimState",
        "sceAgcWaitRegMemPatchAddress",
        "sceAgcWaitRegMemPatchCompareFunction",
        "sceAgcWaitRegMemPatchMask",
        "sceAgcWaitRegMemPatchReference",
    ];

    private static readonly string[] AgcDriver =
    [
        "sceAgcDriverAcquireComputeQueue",
        "sceAgcDriverAddEqEvent",
        "sceAgcDriverAgrSubmitDcb",
        "sceAgcDriverAgrSubmitMultiDcbs",
        "sceAgcDriverCreateQueue",
        "sceAgcDriverCwsrResumeAcq",
        "sceAgcDriverCwsrSuspendAcq",
        "sceAgcDriverDebugHardwareStatus",
        "sceAgcDriverDeleteEqEvent",
        "sceAgcDriverDestroyQueue",
        "sceAgcDriverFindResourcesPublic",
        "sceAgcDriverGetEqContextId",
        "sceAgcDriverGetEqEventType",
        "sceAgcDriverGetGpuRefClks",
        "sceAgcDriverGetHsOffchipParam",
        "sceAgcDriverGetOwnerName",
        "sceAgcDriverGetRegShadowInfo",
        "sceAgcDriverGetRegShadowInfoAgr",
        "sceAgcDriverGetReservedDmemForAgc",
        "sceAgcDriverGetResourceBaseAddressAndSizeInBytes",
        "sceAgcDriverGetResourceName",
        "sceAgcDriverGetResourceShaderGuid",
        "sceAgcDriverGetResourceType",
        "sceAgcDriverGetResourceUserData",
        "sceAgcDriverGetSetFlipPacketSizeInDwords",
        "sceAgcDriverGetSetWorkloadCompletePacketSize",
        "sceAgcDriverGetSetWorkloadsActivePacketSize",
        "sceAgcDriverGetTFRing",
        "sceAgcDriverGetTraceInitiator",
        "sceAgcDriverGetWaitRenderingPacketSizeInDwords",
        "sceAgcDriverGetWorkloadStreamInfo",
        "sceAgcDriverIDHSSubmit",
        "sceAgcDriverInitResourceRegistration",
        "sceAgcDriverIsCaptureInProgress",
        "sceAgcDriverIsSubmitValidationEnabled",
        "sceAgcDriverIsTraceInProgress",
        "sceAgcDriverModuleRegistration",
        "sceAgcDriverNotifyDefaultStates",
        "sceAgcDriverPassInfoDownward",
        "sceAgcDriverPatchClearState",
        "sceAgcDriverQueryResourceRegistrationUserMemoryRequirements",
        "sceAgcDriverRegisterGdsResource",
        "sceAgcDriverRegisterOwner",
        "sceAgcDriverRegisterResource",
        "sceAgcDriverRegisterWorkloadStream",
        "sceAgcDriverReleaseComputeQueue",
        "sceAgcDriverRequestCaptureStart",
        "sceAgcDriverRequestCaptureStop",
        "sceAgcDriverSetFlip",
        "sceAgcDriverSetHsOffchipParam",
        "sceAgcDriverSetResourceUserData",
        "sceAgcDriverSetTFRing",
        "sceAgcDriverSetWorkloadComplete",
        "sceAgcDriverSetWorkloadsActive",
        "sceAgcDriverSetupRegisterShadow",
        "sceAgcDriverSubmitAcb",
        "sceAgcDriverSubmitCommandBuffer",
        "sceAgcDriverSubmitDcb",
        "sceAgcDriverSubmitMultiAcbs",
        "sceAgcDriverSubmitMultiCommandBuffers",
        "sceAgcDriverSubmitMultiDcbs",
        "sceAgcDriverSuspendPointSubmit",
        "sceAgcDriverSysEnableSubmitDone45Exception",
        "sceAgcDriverSysGetClientNumber",
        "sceAgcDriverSysIsGameClosed",
        "sceAgcDriverSysSubmitFlipHandleProxy",
        "sceAgcDriverTmpInitIdhs",
        "sceAgcDriverTriggerCapture",
        "sceAgcDriverUnregisterAllResourcesForOwner",
        "sceAgcDriverUnregisterOwnerAndResources",
        "sceAgcDriverUnregisterResource",
        "sceAgcDriverUnregisterWorkloadStream",
        "sceAgcDriverUserDataGetPacketSize",
        "sceAgcDriverUserDataImmediateWrite",
        "sceAgcDriverUserDataWritePacket",
        "sceAgcDriverUserDataWritePopMarker",
        "sceAgcDriverUserDataWritePushMarker",
        "sceAgcDriverUserDataWriteSetMarker",
        "sceAgcDriverWaitUntilSafeForRendering",
    ];
}
