// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System.Runtime.InteropServices;

namespace SharpProspero.Interop.Text;

/// <summary>
/// Character-encoding conversion, for text the managed framework's own encodings do not cover. Each call
/// converts a source buffer into a destination buffer, writing how many source units it consumed and how
/// many destination units it produced. The Unicode transformations need no profile; the legacy Asian
/// encodings take a conversion-profile pointer, which may be null for the default profile. Signatures from
/// the ces headers; the module is <c>libSceCesCs</c>.
/// </summary>
public static unsafe partial class Ces
{
    private const string Lib = "libSceCesCs";

    /// <summary>Converts UTF-8 to UTF-16.</summary>
    [LibraryImport(Lib)]
    public static partial int sceCesUtf8ToUtf16(byte* utf8Buffer, uint utf8Max, uint* utf8Length, ushort* utf16Buffer, uint utf16Max, uint* utf16Length);

    /// <summary>Converts UTF-16 to UTF-8.</summary>
    [LibraryImport(Lib)]
    public static partial int sceCesUtf16ToUtf8(ushort* utf16Buffer, uint utf16Max, uint* utf16Length, byte* utf8Buffer, uint utf8Max, uint* utf8Length);

    /// <summary>Converts one UTF-8 sequence to a UTF-32 code point.</summary>
    [LibraryImport(Lib)]
    public static partial int sceCesUtf8ToUtf32(byte* utf8Buffer, uint utf8Max, uint* consumed, uint* utf32);

    /// <summary>Converts a UTF-32 code point to UTF-8.</summary>
    [LibraryImport(Lib)]
    public static partial int sceCesUtf32ToUtf8(uint utf32, byte* utf8Buffer, uint utf8Max, uint* utf8Length);

    /// <summary>Converts one UTF-16 sequence to a UTF-32 code point.</summary>
    [LibraryImport(Lib)]
    public static partial int sceCesUtf16ToUtf32(ushort* utf16Buffer, uint utf16Max, uint* utf16Length, uint* utf32);

    /// <summary>Converts a UTF-32 code point to UTF-16.</summary>
    [LibraryImport(Lib)]
    public static partial int sceCesUtf32ToUtf16(uint utf32, ushort* utf16Buffer, uint utf16Max, uint* utf16Length);

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
