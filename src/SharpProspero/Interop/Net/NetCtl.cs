// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System.Runtime.InteropServices;

namespace SharpProspero.Interop.Net;

/// <summary>Where the network connection is.</summary>
public enum NetCtlState
{
    /// <summary>Not connected.</summary>
    Disconnected = 0,

    /// <summary>Connecting to the network.</summary>
    Connecting = 1,

    /// <summary>Connected; obtaining an address.</summary>
    IpObtaining = 2,

    /// <summary>Connected with an address. This is the state a working connection reports.</summary>
    IpObtained = 3,
}

/// <summary>The kind of network device in use.</summary>
public enum NetCtlDevice
{
    /// <summary>A wired connection.</summary>
    Wired = 0,

    /// <summary>A wireless connection.</summary>
    Wireless = 1,
}

/// <summary>
/// Network status bindings. Each fact about the connection is read one at a time by code into a
/// buffer; the union the service fills is 256 bytes and writes the requested member at its start.
/// </summary>
public static unsafe partial class NetCtl
{
    private const string Lib = "libSceNetCtl";

    /// <summary>The size of the buffer <see cref="sceNetCtlGetInfo"/> fills.</summary>
    public const int InfoSize = 256;

    /// <summary>A call was made before <see cref="sceNetCtlInit"/> ran.</summary>
    public const int NotInitialized = unchecked((int)0x80412101);

    // The info codes, one per field of the connection.
    public const int InfoDevice = 1;
    public const int InfoEtherAddr = 2;
    public const int InfoMtu = 3;
    public const int InfoLink = 4;
    public const int InfoBssid = 5;
    public const int InfoSsid = 6;
    public const int InfoWifiSecurity = 7;
    public const int InfoRssiDbm = 8;
    public const int InfoRssiPercentage = 9;
    public const int InfoChannel = 10;
    public const int InfoIpConfig = 11;
    public const int InfoIpAddress = 14;
    public const int InfoNetmask = 15;
    public const int InfoDefaultRoute = 16;
    public const int InfoPrimaryDns = 17;
    public const int InfoSecondaryDns = 18;

    /// <summary>
    /// Starts the network status service. This is the first network call an application makes; nothing
    /// precedes it. Zero on success, or a negative error code.
    /// </summary>
    [LibraryImport(Lib)]
    public static partial int sceNetCtlInit();

    /// <summary>Stops the network status service.</summary>
    [LibraryImport(Lib)]
    public static partial void sceNetCtlTerm();

    /// <summary>Reads the connection state into <paramref name="state"/> (a <see cref="NetCtlState"/>).</summary>
    [LibraryImport(Lib)]
    public static partial int sceNetCtlGetState(int* state);

    /// <summary>
    /// Reads one fact about the connection, named by <paramref name="code"/>, into
    /// <paramref name="info"/>, which must point at a <see cref="InfoSize"/>-byte buffer.
    /// </summary>
    [LibraryImport(Lib)]
    public static partial int sceNetCtlGetInfo(int code, void* info);
}
