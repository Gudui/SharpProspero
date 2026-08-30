# Flat register-default traversal correction

Base: 4ea0a34 (frozen CL), branch codex/defaults-flat-records. Adopt upstream d24b588's
record-array semantics without merging unrelated SDK changes. Runtime change is confined to
Graphics/Agc/RegisterDefaults.cs: use blocks[0][i] for all three consumers, preserve count bounds,
reject null table/first record for nonempty descriptors, and treat null driver descriptor or zero
count as no defaults. Null/count guards do not establish that arbitrary non-null memory is readable;
the native descriptor still must satisfy its ABI and lifetime contract.

Independent synthetic public-API tests at 8cd695f: 9 failed / 3 passed against unchanged production
code. After correction: all 12 pass. Fixtures use reflection to inject a native descriptor only on
the host; no injection API or test hook is compiled into the SDK. A thirteenth host-only preflight
test documents the existing render-target guard mismatch described below. Full Release suite:
1758 passed, zero failed or skipped. Existing MeshBuffer/Renderer3D warnings remain unchanged.

Commands:

```powershell
dotnet test tests/SharpProspero.Tests/SharpProspero.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~RegisterDefaultsTests
dotnet test SharpProspero.slnx -c Release --no-restore
```

The tests cover interior records, all-record order/values, all sixteen render-target values,
missing offsets, diagnostics, empty descriptors, null table/first-block and excessive count.
The native null-return path is source-reviewed to retain upstream semantics, not invoked on the host.
Diagnostic traces identify flat layout, match/unique counts and sixteen render-target defaults.

## Target build stopped by host preflight

The application's recorded native-defaults audit supplies ATTRIB3 (0x03B8) reset 0x08C6C000.
Feeding that value through the existing AgcRenderTargetSetup.Initialize with CL's 1920x1080 tiled
UNorm/Alt target yields 0x4DC6C000, not CL's guarded 0x4506C000. The test executes actual SDK
setters and asserts that mismatch. It does not interpret the hardware meaning of the differing bits
or prove this is the target's current descriptor. No render-target setter or guard was changed.

The planned CN target test was therefore not built: unchanged CL would reject this input before
submitting the draw. Further target qualification needs an independently justified state/guard
decision. Do not replace the guard constant merely to make the test pass. CL/CM artifacts remain
preserved. This is a host-validated fork correction, not new renderer capability or an upstream PR.
