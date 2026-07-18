// A SharpProspero library built as a relocatable module (.prx). Another module loads it by name and
// calls its exported functions. Each exported function is a static [UnmanagedCallersOnly] method whose
// EntryPoint matches a <ProsperoExportSymbol> in the project file.
//
// A [UnmanagedCallersOnly] method runs across the native boundary, so it must not let a managed
// exception escape and must use only blittable parameter and return types.

using System.Runtime.InteropServices;

namespace SampleApp;

internal static class Library
{
    /// <summary>Adds two integers. Exported as "sampleAdd".</summary>
    [UnmanagedCallersOnly(EntryPoint = "sampleAdd")]
    public static int Add(int a, int b) => a + b;

    /// <summary>Returns the library's version number. Exported as "sampleVersion".</summary>
    [UnmanagedCallersOnly(EntryPoint = "sampleVersion")]
    public static int Version() => 0x01_00;
}

// A library module still compiles as an application project, so it needs an entry point. It is never
// called for a loaded library; the exported functions above are the module's real surface.
internal static class Program
{
    private static void Main()
    {
    }
}
