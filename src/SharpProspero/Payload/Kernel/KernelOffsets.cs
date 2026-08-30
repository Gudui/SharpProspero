// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

namespace SharpProspero.Payload.Kernel;

/// <summary>
/// Firmware-versioned kernel data offsets. Each method takes a BCD-encoded firmware version
/// (as returned by the kernel's system software version query) and returns the kdata-relative
/// offset for the named kernel symbol on that firmware. Add <see cref="KdataBase"/> to obtain
/// an absolute kernel virtual address.
/// </summary>
/// <remarks>
/// <para>
/// The firmware version uses a four-byte BCD encoding where byte 3 is the major version,
/// byte 2 the minor, byte 1 the patch, and byte 0 the sub-patch. Only the upper 16 bits
/// (major + minor) vary across the offset groups. For example, firmware 10.01 is
/// <c>0x10010000</c> and firmware 4.03 is <c>0x04030000</c>.
/// </para>
/// <para>
/// Structure field offsets (process, credential, file descriptor) are firmware-invariant
/// and included as constants in this class alongside the per-firmware lookup methods.
/// </para>
/// </remarks>
public static class KernelOffsets
{
    // ---- Firmware version masks ----

    /// <summary>Masks a full version to its major.minor group.</summary>
    public const uint VersionMask = 0xFFFF0000;

    // ---- Firmware version constants (BCD-encoded) ----

    /// <summary>Firmware 1.00.</summary>
    public const uint Fw100 = 0x01000000;
    /// <summary>Firmware 1.01.</summary>
    public const uint Fw101 = 0x01010000;
    /// <summary>Firmware 1.02.</summary>
    public const uint Fw102 = 0x01020000;
    /// <summary>Firmware 1.05.</summary>
    public const uint Fw105 = 0x01050000;
    /// <summary>Firmware 1.10.</summary>
    public const uint Fw110 = 0x01100000;
    /// <summary>Firmware 1.11.</summary>
    public const uint Fw111 = 0x01110000;
    /// <summary>Firmware 1.12.</summary>
    public const uint Fw112 = 0x01120000;
    /// <summary>Firmware 1.13.</summary>
    public const uint Fw113 = 0x01130000;
    /// <summary>Firmware 1.14.</summary>
    public const uint Fw114 = 0x01140000;
    /// <summary>Firmware 2.00.</summary>
    public const uint Fw200 = 0x02000000;
    /// <summary>Firmware 2.20.</summary>
    public const uint Fw220 = 0x02200000;
    /// <summary>Firmware 2.25.</summary>
    public const uint Fw225 = 0x02250000;
    /// <summary>Firmware 2.26.</summary>
    public const uint Fw226 = 0x02260000;
    /// <summary>Firmware 2.30.</summary>
    public const uint Fw230 = 0x02300000;
    /// <summary>Firmware 2.50.</summary>
    public const uint Fw250 = 0x02500000;
    /// <summary>Firmware 2.70.</summary>
    public const uint Fw270 = 0x02700000;
    /// <summary>Firmware 3.00.</summary>
    public const uint Fw300 = 0x03000000;
    /// <summary>Firmware 3.10.</summary>
    public const uint Fw310 = 0x03100000;
    /// <summary>Firmware 3.20.</summary>
    public const uint Fw320 = 0x03200000;
    /// <summary>Firmware 3.21.</summary>
    public const uint Fw321 = 0x03210000;
    /// <summary>Firmware 4.00.</summary>
    public const uint Fw400 = 0x04000000;
    /// <summary>Firmware 4.02.</summary>
    public const uint Fw402 = 0x04020000;
    /// <summary>Firmware 4.03.</summary>
    public const uint Fw403 = 0x04030000;
    /// <summary>Firmware 4.50.</summary>
    public const uint Fw450 = 0x04500000;
    /// <summary>Firmware 4.51.</summary>
    public const uint Fw451 = 0x04510000;
    /// <summary>Firmware 5.00.</summary>
    public const uint Fw500 = 0x05000000;
    /// <summary>Firmware 5.02.</summary>
    public const uint Fw502 = 0x05020000;
    /// <summary>Firmware 5.10.</summary>
    public const uint Fw510 = 0x05100000;
    /// <summary>Firmware 5.50.</summary>
    public const uint Fw550 = 0x05500000;
    /// <summary>Firmware 6.00.</summary>
    public const uint Fw600 = 0x06000000;
    /// <summary>Firmware 6.02.</summary>
    public const uint Fw602 = 0x06020000;
    /// <summary>Firmware 6.50.</summary>
    public const uint Fw650 = 0x06500000;
    /// <summary>Firmware 7.00.</summary>
    public const uint Fw700 = 0x07000000;
    /// <summary>Firmware 7.01.</summary>
    public const uint Fw701 = 0x07010000;
    /// <summary>Firmware 7.20.</summary>
    public const uint Fw720 = 0x07200000;
    /// <summary>Firmware 7.40.</summary>
    public const uint Fw740 = 0x07400000;
    /// <summary>Firmware 7.60.</summary>
    public const uint Fw760 = 0x07600000;
    /// <summary>Firmware 7.61.</summary>
    public const uint Fw761 = 0x07610000;
    /// <summary>Firmware 8.00.</summary>
    public const uint Fw800 = 0x08000000;
    /// <summary>Firmware 8.20.</summary>
    public const uint Fw820 = 0x08200000;
    /// <summary>Firmware 8.40.</summary>
    public const uint Fw840 = 0x08400000;
    /// <summary>Firmware 8.60.</summary>
    public const uint Fw860 = 0x08600000;
    /// <summary>Firmware 9.00.</summary>
    public const uint Fw900 = 0x09000000;
    /// <summary>Firmware 9.05.</summary>
    public const uint Fw905 = 0x09050000;
    /// <summary>Firmware 9.20.</summary>
    public const uint Fw920 = 0x09200000;
    /// <summary>Firmware 9.40.</summary>
    public const uint Fw940 = 0x09400000;
    /// <summary>Firmware 9.60.</summary>
    public const uint Fw960 = 0x09600000;
    /// <summary>Firmware 10.00.</summary>
    public const uint Fw1000 = 0x10000000;
    /// <summary>Firmware 10.01.</summary>
    public const uint Fw1001 = 0x10010000;
    /// <summary>Firmware 10.20.</summary>
    public const uint Fw1020 = 0x10200000;
    /// <summary>Firmware 10.40.</summary>
    public const uint Fw1040 = 0x10400000;
    /// <summary>Firmware 10.60.</summary>
    public const uint Fw1060 = 0x10600000;
    /// <summary>Firmware 11.00.</summary>
    public const uint Fw1100 = 0x11000000;
    /// <summary>Firmware 11.20.</summary>
    public const uint Fw1120 = 0x11200000;
    /// <summary>Firmware 11.40.</summary>
    public const uint Fw1140 = 0x11400000;
    /// <summary>Firmware 11.60.</summary>
    public const uint Fw1160 = 0x11600000;
    /// <summary>Firmware 12.00.</summary>
    public const uint Fw1200 = 0x12000000;
    /// <summary>Firmware 12.02.</summary>
    public const uint Fw1202 = 0x12020000;
    /// <summary>Firmware 12.20.</summary>
    public const uint Fw1220 = 0x12200000;
    /// <summary>Firmware 12.40.</summary>
    public const uint Fw1240 = 0x12400000;
    /// <summary>Firmware 12.60.</summary>
    public const uint Fw1260 = 0x12600000;
    /// <summary>Firmware 12.70.</summary>
    public const uint Fw1270 = 0x12700000;

    /// <summary>
    /// Returns the kdata-relative offset of the <c>allproc</c> list head for the given
    /// firmware version, or zero if the firmware is not recognized.
    /// </summary>
    public static ulong Allproc(uint firmwareVersion) => (firmwareVersion & VersionMask) switch
    {
        Fw100 or Fw101 or Fw102 or Fw105
        or Fw110 or Fw111 or Fw112 or Fw113 or Fw114 => 0x26D1C18,

        Fw200 or Fw220 or Fw225 or Fw226
        or Fw230 or Fw250 or Fw270 => 0x2701C28,

        Fw300 or Fw310 or Fw320 or Fw321 => 0x276DC58,

        Fw400 or Fw402 or Fw403 or Fw450 or Fw451 => 0x27EDCB8,

        Fw500 or Fw502 or Fw510 or Fw550 => 0x291DD00,

        Fw600 or Fw602 or Fw650 => 0x2869D20,

        Fw700 or Fw701 or Fw720 or Fw740 or Fw760 or Fw761 => 0x2859D50,

        Fw800 or Fw820 or Fw840 or Fw860 => 0x2875D50,

        Fw900 or Fw905 or Fw920 or Fw940 or Fw960 => 0x2755D50,

        Fw1000 or Fw1001 or Fw1020 or Fw1040 or Fw1060 => 0x2765D70,

        Fw1100 or Fw1120 or Fw1140 or Fw1160 => 0x2875D70,

        Fw1200 or Fw1202 or Fw1220 or Fw1240 or Fw1260 or Fw1270 => 0x2885E00,

        _ => 0,
    };

    /// <summary>
    /// Returns the kdata-relative offset of the kernel security flags for the given
    /// firmware version, or zero if the firmware is not recognized.
    /// </summary>
    public static ulong SecurityFlags(uint firmwareVersion) => (firmwareVersion & VersionMask) switch
    {
        Fw100 or Fw101 or Fw102 or Fw105
        or Fw110 or Fw111 or Fw112 or Fw113 or Fw114 => 0x6241074,

        Fw200 or Fw220 or Fw225 or Fw226
        or Fw230 or Fw250 or Fw270 => 0x63E1274,

        Fw300 or Fw310 or Fw320 or Fw321 => 0x6466474,

        Fw400 => 0x6506474,

        Fw402 or Fw403 or Fw450 or Fw451 => 0x6505474,

        Fw500 or Fw502 or Fw510 or Fw550 => 0x66466EC,

        Fw600 or Fw602 or Fw650 => 0x65968EC,

        Fw700 or Fw701 or Fw720 or Fw740 or Fw760 or Fw761 => 0x0AC8064,

        Fw800 or Fw820 or Fw840 or Fw860 => 0x0AC3064,

        Fw900 => 0x0D72064,

        Fw905 or Fw920 or Fw940 or Fw960 => 0x0D73064,

        Fw1000 or Fw1001 or Fw1020 or Fw1040 or Fw1060 => 0x0D79064,

        _ => 0,
    };

    /// <summary>
    /// Returns the kdata-relative offset of the QA flags for the given firmware version,
    /// or zero if the firmware is not recognized.
    /// </summary>
    public static ulong QaFlags(uint firmwareVersion)
    {
        uint masked = firmwareVersion & VersionMask;

        if (masked is Fw100 or Fw101 or Fw102 or Fw105
            or Fw110 or Fw111 or Fw112 or Fw113 or Fw114
            or Fw200 or Fw220 or Fw225 or Fw226
            or Fw230 or Fw250 or Fw270
            or Fw300 or Fw310 or Fw320 or Fw321
            or Fw400 or Fw402 or Fw403 or Fw450 or Fw451
            or Fw500 or Fw502 or Fw510 or Fw550)
            return 0x6241098;

        ulong secFlags = SecurityFlags(firmwareVersion);
        return secFlags != 0 ? secFlags + 0x24 : 0;
    }

    /// <summary>
    /// Returns the kdata-relative offset of the utoken flags for the given firmware version,
    /// or zero if the firmware is not recognized.
    /// </summary>
    public static ulong UtokenFlags(uint firmwareVersion)
    {
        uint masked = firmwareVersion & VersionMask;

        if (masked is Fw100 or Fw101 or Fw102 or Fw105
            or Fw110 or Fw111 or Fw112 or Fw113 or Fw114
            or Fw200 or Fw220 or Fw225 or Fw226
            or Fw230 or Fw250 or Fw270
            or Fw300 or Fw310 or Fw320 or Fw321
            or Fw400 or Fw402 or Fw403 or Fw450 or Fw451
            or Fw500 or Fw502 or Fw510 or Fw550)
            return 0x6646710;

        ulong secFlags = SecurityFlags(firmwareVersion);
        return secFlags != 0 ? secFlags + 0x8C : 0;
    }

    /// <summary>
    /// Returns the kdata-relative offset of the root vnode pointer for the given firmware
    /// version, or zero if the firmware is not recognized.
    /// </summary>
    public static ulong Rootvnode(uint firmwareVersion) => (firmwareVersion & VersionMask) switch
    {
        Fw100 or Fw101 or Fw102 or Fw105
        or Fw110 or Fw111 or Fw112 or Fw113 or Fw114 => 0x6565540,

        Fw200 or Fw220 or Fw225 or Fw226
        or Fw230 or Fw250 or Fw270 => 0x67134C0,

        Fw300 or Fw310 or Fw320 or Fw321 => 0x67AB4C0,

        Fw400 or Fw402 or Fw403 or Fw450 or Fw451 => 0x66E74C0,

        Fw500 or Fw502 or Fw510 or Fw550 => 0x6853510,

        Fw600 or Fw602 or Fw650 => 0x679F510,

        Fw700 or Fw701 or Fw720 or Fw740 or Fw760 or Fw761 => 0x30C7510,

        Fw800 or Fw820 or Fw840 or Fw860 => 0x30FB510,

        Fw900 or Fw905 or Fw920 or Fw940 or Fw960 => 0x2FDB510,

        Fw1000 or Fw1001 or Fw1020 or Fw1040 or Fw1060 => 0x2FA3510,

        _ => 0,
    };

    /// <summary>
    /// Returns the kernel data section base address for the given firmware version,
    /// or zero if the firmware is not recognized.
    /// </summary>
    public static ulong KdataBase(uint firmwareVersion) => (firmwareVersion & VersionMask) switch
    {
        Fw100 or Fw101 or Fw102 or Fw105
        or Fw110 or Fw111 or Fw112 or Fw113 or Fw114 => 0xFFFFFFFF_80D40000,

        Fw200 or Fw220 or Fw225 or Fw226
        or Fw230 or Fw250 or Fw270 => 0xFFFFFFFF_80D60000,

        Fw300 or Fw310 or Fw320 or Fw321 => 0xFFFFFFFF_80D70000,

        Fw400 or Fw402 or Fw403 => 0xFFFFFFFF_80E10000,

        Fw450 or Fw451 => 0xFFFFFFFF_80E10000,

        Fw500 or Fw502 or Fw510 or Fw550 => 0xFFFFFFFF_80E10000,

        Fw600 or Fw602 or Fw650 => 0xFFFFFFFF_80E10000,

        Fw700 or Fw701 or Fw720 or Fw740 or Fw760 or Fw761 => 0xFFFFFFFF_80E10000,

        Fw800 or Fw820 or Fw840 or Fw860 => 0xFFFFFFFF_80E10000,

        Fw900 or Fw905 or Fw920 or Fw940 or Fw960 => 0xFFFFFFFF_80ED0000,

        Fw1000 or Fw1001 or Fw1020 or Fw1040 or Fw1060 => 0xFFFFFFFF_80ED0000,

        _ => 0,
    };

    /// <summary>
    /// Returns <see langword="true"/> when the firmware version is recognized and all
    /// five kernel data offsets are available.
    /// </summary>
    public static bool IsSupported(uint firmwareVersion) => Allproc(firmwareVersion) != 0;

    // ---- FW 10.01 absolute addresses (for backward compatibility) ----

    /// <summary>Kernel data section base for FW 10.01.</summary>
    public const ulong KdataBase1001 = 0xffffffff_80ED0000;

    /// <summary>Absolute <c>allproc</c> address for FW 10.01.</summary>
    public const ulong Allproc1001 = 0xffffffff_83635d70;

    /// <summary>Absolute <c>rootvnode</c> address for FW 10.01.</summary>
    public const ulong Rootvnode1001 = 0xffffffff_83e73510;

    /// <summary>The host prison (<c>prison0</c>) absolute address for FW 10.01.</summary>
    public const ulong Prison0_1001 = 0xffffffff_82cdf4e0;

    // ---- Firmware-invariant structure field offsets ----

    /// <summary><c>p_list.le_next</c>: offset zero, the link that chains every process.</summary>
    public const int ProcList = 0x00;

    /// <summary><c>p_ucred</c>: the process credential pointer.</summary>
    public const int ProcUcred = 0x40;

    /// <summary><c>p_fd</c>: the file descriptor table pointer.</summary>
    public const int ProcFd = 0x48;

    /// <summary><c>p_pid</c>: the process identifier (four bytes).</summary>
    public const int ProcPid = 0xBC;

    /// <summary>The title identifier for the running application, a ten-byte inline string.</summary>
    public const int ProcTitleId = 0x470;

    /// <summary><c>p_comm</c>: the process name, an inline seventeen-byte array.</summary>
    public const int ProcComm = 0x5DC;

    /// <summary><c>fd_rdir</c>: the root directory vnode in the file descriptor table.</summary>
    public const int FdRdir = 0x10;

    /// <summary><c>fd_jdir</c>: the jail directory vnode in the file descriptor table.</summary>
    public const int FdJdir = 0x18;

    /// <summary><c>cr_uid</c>: the effective user identifier (four bytes).</summary>
    public const int UcredUid = 0x04;

    /// <summary><c>cr_ruid</c>: the real user identifier (four bytes).</summary>
    public const int UcredRuid = 0x08;

    /// <summary><c>cr_svuid</c>: the saved user identifier (four bytes).</summary>
    public const int UcredSvuid = 0x0C;

    /// <summary><c>cr_ngroups</c>: the supplementary group count (four bytes).</summary>
    public const int UcredNgroups = 0x10;

    /// <summary><c>cr_rgid</c>: the real group identifier (four bytes).</summary>
    public const int UcredRgid = 0x14;

    /// <summary><c>cr_svgid</c>: the saved group identifier (four bytes).</summary>
    public const int UcredSvgid = 0x18;

    /// <summary><c>cr_prison</c>: the prison pointer the credential belongs to.</summary>
    public const int UcredPrison = 0x30;

    /// <summary><c>cr_sceAuthID</c>: the authorization identifier (eight bytes).</summary>
    public const int UcredSceAuthId = 0x58;

    /// <summary><c>cr_sceCaps</c>: the first eight bytes of the capability set.</summary>
    public const int UcredSceCaps = 0x60;

    /// <summary><c>cr_sceAttrs</c>: the base of the 32-byte attribute block.</summary>
    public const int UcredSceAttrs = 0x80;

    /// <summary>The privilege attribute byte within the attribute block (one byte).</summary>
    public const int UcredSceAttr0 = 0x83;

    /// <summary><c>pr_ref</c>: the prison reference count (four bytes).</summary>
    public const int PrisonRef = 0x14;

    // ---- Detailed per-firmware syscall table offsets ----

    /// <summary>
    /// Returns the kdata-relative offset of the PS5 sysent table for the given firmware.
    /// </summary>
    public static ulong Sysents(uint fw) => (fw & VersionMask) switch
    {
        Fw300 or Fw310 or Fw320 or Fw321 => 0x16F720,
        Fw400 or Fw402 or Fw403 or Fw450 or Fw451 => 0x1709C0,
        Fw500 or Fw502 => 0x1B1EF0,
        Fw510 => 0x1B2040,
        Fw550 => 0x1B2210,
        Fw600 => 0x1B49A0,
        Fw602 or Fw650 => 0x1B49F0,
        Fw700 or Fw701 => 0x1B7030,
        Fw720 or Fw740 => 0x1B71A0,
        Fw760 or Fw761 => 0x1B7260,
        Fw800 or Fw820 or Fw840 or Fw860 => 0x1A7DB0,
        Fw900 or Fw905 => 0x1AAC10,
        Fw920 or Fw940 or Fw960 => 0x1AAC60,
        Fw1000 or Fw1001 => 0x1AD100,
        Fw1020 or Fw1040 or Fw1060 => 0x1AD120,
        Fw1100 or Fw1120 => 0x1B0B70,
        Fw1140 => 0x1B0B20,
        Fw1160 => 0x1B08E0,
        Fw1200 or Fw1202 or Fw1220 or Fw1240 or Fw1260 or Fw1270 => 0x1AF4D0,
        _ => 0,
    };

    /// <summary>
    /// Returns the kdata-relative offset of the sysentvec structure.
    /// </summary>
    public static ulong Sysentvec(uint fw) => (fw & VersionMask) switch
    {
        Fw300 or Fw310 or Fw320 or Fw321 => 0xCA0CD8,
        Fw400 or Fw402 or Fw403 or Fw450 or Fw451 => 0xD11BB8,
        Fw500 or Fw502 or Fw510 or Fw550 => 0xE00BE8,
        Fw600 or Fw602 or Fw650 => 0xE210A8,
        Fw700 or Fw701 => 0xE21AB8,
        Fw720 or Fw740 or Fw760 or Fw761 => 0xE21B78,
        Fw800 or Fw820 or Fw840 or Fw860 => 0xE21CA8,
        Fw900 or Fw905 or Fw920 or Fw940 or Fw960 => 0xDBA648,
        Fw1000 or Fw1001 or Fw1020 or Fw1040 or Fw1060 => 0xDBA6D8,
        Fw1100 or Fw1120 => 0xDCBC78,
        Fw1140 or Fw1160 => 0xDCBC98,
        Fw1200 or Fw1202 or Fw1220 or Fw1240 or Fw1260 or Fw1270 => 0xDCC978,
        _ => 0,
    };

    /// <summary>
    /// Returns the offset of <c>p_sysent</c> in <c>struct proc</c> for the given firmware.
    /// </summary>
    public static int ProcSysent(uint fw) => (fw & VersionMask) switch
    {
        >= Fw1200 => 0xA08,
        >= Fw1000 => 0xA00,
        >= Fw700 => 0x9F8,
        >= Fw600 => 0x9E8,
        _ => 0x9C0,
    };
}
