# Explicit GPU diagnostic shaders (fork integration candidate)

The normal `BuiltInShaders.MeshVertex()` and `MeshPixel()` resources match upstream e24e25e.
They are not replaced by the hardcoded triangle or constant-white diagnostic. Restoring those resources
does **not** establish that this fork's experimental `Renderer3D` works on firmware 5.50.

To embed the diagnostic pair, pass `-p:EmbedDiagnosticShaders=true` when building the SDK/application.
The default is off. This is independent of `EmbedBuiltInShaders=false`, which can exclude the ordinary
mesh resources from a diagnostic-only application. Calling the diagnostic API without its resources
throws a managed exception explaining the opt-in property; it does not silently use a mesh shader.

```csharp
using SharpProspero.Graphics.Agc.Diagnostics;

ShaderBinary vertex = DiagnosticShaders.HardcodedTriangleVertex();
ShaderBinary pixel = DiagnosticShaders.ConstantWhitePixel();
```

These resources preserve exact GPU-CL container bytes from fork 4ea0a34:

| Resource | Bytes | SHA-256 |
|---|---:|---|
| hardcoded_triangle_vs.sb | 992 | cc740ea480864fdd96925e89e4a623b1f42c1fea8f8e58a38f64f2063300a28c |
| constant_white_ps.sb | 968 | ab3722ac8e950ead1a53adfa9c2ff0d85be68cae0eb11ea2bc40ffb85461490d |

The original mesh .pssl files describe the upstream mesh resources, not these diagnostic programs.
The diagnostic binaries are preserved experimental derivatives of the repository's shader containers;
they are not presented as freshly compiled from those mesh sources. Their byte-level history remains
in git and the `gpu-cl-stable-2026-08-30` tag. Do not promote them upstream without a provenance and
reproducibility review.

GPU-CL observed a stable solid-white triangle over 25 exact draws and clean exit, using a standalone
probe's explicit pipeline state. It did not exercise `Renderer3D`, mesh buffers, transforms, channel
order or interpolated colour. This resource/API separation has host checks only until a separately
identified target replay qualifies the new build; keep the original CL artifact unchanged.

Host checks:

```powershell
dotnet test SharpProspero.slnx -c Release
dotnet test SharpProspero.slnx -c Release -p:EmbedDiagnosticShaders=true
```
