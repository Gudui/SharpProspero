// SharpProspero.Packager - packs a built module folder into an installable package.
// Copyright (C) 2026 SvenGDK
//
// Thin command-line front end over the package builder. Point it at a folder that holds the
// compiled eboot.bin and an optional sce_sys metadata tree, and it writes the finished *.pkg.

using LibProsperoPkg;
using System;
using System.Collections.Generic;
using System.IO;

namespace SharpProspero.Packager;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0 || HasFlag(args, "--help") || HasFlag(args, "-h"))
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        string? input = GetOption(args, "--in");
        string? output = GetOption(args, "--out");

        if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(output))
        {
            Console.Error.WriteLine("Both --in and --out are required.");
            PrintUsage();
            return 1;
        }

        var options = new ProsperoHomebrewPackageOptions
        {
            HomebrewFolder = input,
            OutputFolder = output,
            ModuleName = GetOption(args, "--module") ?? "eboot.bin",
            ContentId = GetOption(args, "--content-id") ?? "",
            Passcode = GetOption(args, "--passcode") ?? new string('0', 32),
            Title = GetOption(args, "--title") ?? "",
            Version = GetOption(args, "--version") ?? "",
            KeepStaging = HasFlag(args, "--keep-staging"),
        };

        try
        {
            Directory.CreateDirectory(output);
            ProsperoHomebrewPackageResult result = ProsperoHomebrewPackager.Package(
                options, message => Console.WriteLine($"  {message}"));

            Console.WriteLine();
            Console.WriteLine($"Package: {result.OutputPath}");
            Console.WriteLine($"Module:  {result.ModulePath}");
            PrintWarnings(result.Warnings);
            return 0;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"Invalid input: {ex.Message}");
            return 2;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"File error: {ex.Message}");
            return 3;
        }
    }

    private static void PrintWarnings(IReadOnlyList<string> warnings)
    {
        if (warnings.Count == 0)
            return;
        Console.WriteLine();
        Console.WriteLine("Warnings:");
        foreach (string warning in warnings)
            Console.WriteLine($"  - {warning}");
    }

    private static string? GetOption(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.Ordinal))
                return args[i + 1];
        }
        return null;
    }

    private static bool HasFlag(string[] args, string name)
        => Array.Exists(args, a => string.Equals(a, name, StringComparison.Ordinal));

    private static void PrintUsage()
    {
        Console.WriteLine("Usage: sharpprospero-pack --in <module-folder> --out <output-folder> [options]");
        Console.WriteLine();
        Console.WriteLine("  --in <folder>          Folder with the compiled eboot.bin and optional sce_sys/.");
        Console.WriteLine("  --out <folder>         Folder the finished *.pkg is written to.");
        Console.WriteLine("  --module <name>        Module file name. Default: eboot.bin.");
        Console.WriteLine("  --content-id <id>      36-character content id. Default: read from param.json.");
        Console.WriteLine("  --passcode <32 chars>  Package passcode. Default: all zeros.");
        Console.WriteLine("  --title <name>         Display title. Default: read from param.json.");
        Console.WriteLine("  --version <NN.NN>      Content version. Default: read from param.json or 01.00.");
        Console.WriteLine("  --keep-staging         Keep the assembled source tree after the build.");
    }
}
