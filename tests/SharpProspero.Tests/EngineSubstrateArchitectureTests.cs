// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Application;
using SharpProspero.Graphics;
using SharpProspero.Graphics.Agc;
using SharpProspero.Interop.VideoOut;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using Xunit;

namespace SharpProspero.Tests;

/// <summary>
/// Ratchets the engine-substrate boundary while the replacement runtime and RHI do not exist yet.
/// Tests for those future types are activated with the slice that introduces them; these checks keep
/// today's known architectural debt from spreading in the meantime.
/// </summary>
public sealed partial class EngineSubstrateArchitectureTests
{
    private static readonly string[] RendererLegacyDependencyBudget =
    [
        "SharpProspero.Graphics.Agc",
        "SharpProspero.Interop",
        "SharpProspero.Interop.Agc",
        "SharpProspero.Interop.VideoOut",
        "SharpProspero.Memory",
    ];

    private static readonly HashSet<string> ProsperoAppLifecycleBudget =
    [
        "DisplayDevice.Open",
        "GamePad.Open",
        "SystemService.sceSystemServiceHideSplashScreen",
        "UserService.sceUserServiceInitialize",
        "UserService.sceUserServiceTerminate",
    ];

    [Fact]
    public void DependencyGate_RejectsForbiddenNamespaceAndQualifiedReference()
    {
        const string valid = "using SharpProspero.Graphics; namespace Adapter; sealed class Client {}";
        const string usingAgc = "using SharpProspero.Graphics.Agc; namespace Adapter; sealed class Client {}";
        const string qualifiedInterop = "namespace Adapter; sealed class Client { SharpProspero.Interop.Agc.SceAgc? api; }";

        Assert.Empty(FindForbiddenDependencies(valid));
        Assert.Contains("SharpProspero.Graphics.Agc", FindForbiddenDependencies(usingAgc));
        Assert.Contains("SharpProspero.Interop.Agc", FindForbiddenDependencies(qualifiedInterop));
    }

    [Fact]
    public void Renderer3D_LegacyDependencyDebtCannotExpand()
    {
        string source = File.ReadAllText(SourcePath("Graphics", "Renderer3D.cs"));
        string[] actual = FindForbiddenDependencies(source);

        Assert.All(actual, dependency => Assert.Contains(dependency, RendererLegacyDependencyBudget));
    }

    [Fact]
    public void Renderer3D_BindsBuiltInResourcesThroughSerializedMetadata()
    {
        string source = File.ReadAllText(SourcePath("Graphics", "Renderer3D.cs"));

        Assert.Contains("TryGetResourceSlot(ShaderResourceKind.ConstantBuffer, 0", source, StringComparison.Ordinal);
        Assert.Contains("TryGetResourceSlot(ShaderResourceKind.ReadOnly, 0", source, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\b(?:cbOff|vbOff|vertexDwordOffset)\s*=\s*8\b", source);
    }

    [Fact]
    public void EngineFacingRhiFiles_HaveNoLowLevelDependencies()
    {
        string graphicsRoot = Path.Combine(RepositoryRoot(), "src", "SharpProspero", "Graphics");
        string[] candidates = Directory.Exists(Path.Combine(graphicsRoot, "Rhi"))
            ? Directory.GetFiles(Path.Combine(graphicsRoot, "Rhi"), "*.cs", SearchOption.AllDirectories)
            : [];

        foreach (string candidate in candidates)
            Assert.Empty(FindForbiddenDependencies(File.ReadAllText(candidate)));
    }

    [Fact]
    public void ProsperoApp_LifecycleDebtCannotExpandOrDuplicate()
    {
        string source = File.ReadAllText(SourcePath("Application", "ProsperoApp.cs"));
        string[] calls = LifecycleCall().Matches(source).Select(match => match.Value).ToArray();

        Assert.All(calls, call => Assert.Contains(call, ProsperoAppLifecycleBudget));
        Assert.All(calls.Distinct(), call => Assert.Equal(1, calls.Count(candidate => candidate == call)));
    }

    [Fact]
    public void ExistingApplicationAndRendererSignaturesRemainAvailable()
    {
        Assert.NotNull(typeof(DisplayDevice).GetMethod(nameof(DisplayDevice.Open),
            [typeof(int), typeof(int), typeof(int), typeof(int), typeof(VideoOutTilingMode)]));
        Assert.NotNull(typeof(DisplayDevice).GetMethod(nameof(DisplayDevice.Present), [typeof(VideoOutFlipMode)]));
        Assert.NotNull(typeof(Renderer3D).GetConstructor([typeof(DisplayDevice), typeof(int), typeof(Action<string>)]));
        Assert.NotNull(typeof(Renderer3D).GetMethod(nameof(Renderer3D.DrawMesh),
            [typeof(MeshBuffer), typeof(Matrix4x4).MakeByRefType(), typeof(Matrix4x4).MakeByRefType()]));
        Assert.True(typeof(ProsperoApp).IsAssignableFrom(typeof(CompatibilityApp)));
    }

    // This body is never executed. Compiling it is the caller-side compatibility check: inheritance,
    // construction, frame access and the existing draw overload must remain expressible by source.
    private static void CompileExistingClient(DisplayDevice display, MeshBuffer mesh, in Matrix4x4 mvp, in Matrix4x4 model)
    {
        using var renderer = new Renderer3D(display);
        renderer.DrawMesh(mesh, in mvp, in model);
    }

    private sealed class CompatibilityApp : ProsperoApp
    {
        protected override void OnFrame(FrameContext context)
        {
            _ = context.Surface;
            if (context.Pressed(SharpProspero.Interop.Pad.ScePadButton.Circle))
                context.RequestExit();
        }
    }

    private static string[] FindForbiddenDependencies(string source)
    {
        string[] forbiddenRoots =
        [
            "SharpProspero.Graphics.Agc",
            "SharpProspero.Interop.Agc",
            "SharpProspero.Interop.VideoOut",
            "SharpProspero.Interop",
            "SharpProspero.Memory.DirectMemoryRegion",
            "SharpProspero.Memory",
        ];

        return forbiddenRoots
            .Where(root => Regex.IsMatch(source, $@"\b{Regex.Escape(root)}(?:\b|\.)", RegexOptions.CultureInvariant))
            .Where(root => !forbiddenRoots.Any(longer => longer.Length > root.Length &&
                longer.StartsWith(root + ".", StringComparison.Ordinal) &&
                Regex.IsMatch(source, $@"\b{Regex.Escape(longer)}(?:\b|\.)", RegexOptions.CultureInvariant)))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static string SourcePath(params string[] path) =>
        Path.Combine([RepositoryRoot(), "src", "SharpProspero", .. path]);

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SharpProspero.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the SharpProspero repository root.");
    }

    [GeneratedRegex(@"\b(?:DisplayDevice\.Open|GamePad\.Open|SystemService\.sceSystemService\w+|UserService\.sceUserService\w+)(?=\s*\()", RegexOptions.CultureInvariant)]
    private static partial Regex LifecycleCall();
}
