// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System.Runtime.InteropServices;

namespace SharpProspero.Interop.Text;

/// <summary>
/// Character-encoding conversion for the legacy Asian encodings, which the managed framework's own
/// encodings do not cover. Each call converts a source buffer into a destination buffer, writing how many
/// source units it consumed and how many it produced, and takes a conversion-profile pointer which may be
/// null for the default profile. The module is <c>libSceCesCs</c>; it converts between these encodings and
/// UTF-8 only, so use the framework's own encodings to go on to UTF-16 or UTF-32.
/// </summary>
public static unsafe partial class Ces
{
    private const string Lib = "libSceCesCs";







    /// <summary>Converts EUC-JP (Japanese) to UTF-8. Pass null for the default profile.</summary>
    [LibraryImport(Lib)]
    public static partial int sceCesEucJpToUtf8(void* profile, byte* eucBuffer, uint eucMax, uint* eucLength, byte* utf8Buffer, uint utf8Max, uint* utf8Length);

    /// <summary>Converts EUC-KR (Korean) to UTF-8. Pass null for the default profile.</summary>
    [LibraryImport(Lib)]
    public static partial int sceCesEucKrToUtf8(void* profile, byte* eucBuffer, uint eucMax, uint* eucLength, byte* utf8Buffer, uint utf8Max, uint* utf8Length);

    /// <summary>Converts Big5 (Traditional Chinese) to UTF-8. Pass null for the default profile.</summary>
    [LibraryImport(Lib)]
    public static partial int sceCesBig5ToUtf8(void* profile, byte* big5Buffer, uint big5Max, uint* big5Length, byte* utf8Buffer, uint utf8Max, uint* utf8Length);

    /// <summary>Converts UHC (Korean) to UTF-8. Pass null for the default profile.</summary>
    [LibraryImport(Lib)]
    public static partial int sceCesUhcToUtf8(void* profile, byte* uhcBuffer, uint uhcMax, uint* uhcLength, byte* utf8Buffer, uint utf8Max, uint* utf8Length);
}
