---
title: Engine substrate design
parent: Application Modules
nav_order: 4
---

# Engine substrate design

This is the living architecture and decision record for turning SharpProspero's proven platform and
AGC mechanisms into an engine-neutral substrate. It is intentionally more precise than the
[engine substrate roadmap](engine-substrate.md): the roadmap says where the SDK is going; this
document records who owns each responsibility, which transitions are legal, what must remain
compatible, and which implementation slice may start next.

| Field | Value |
|---|---|
| Document status | Approved direction; Slice 0 enforcement active; Slice 1 external-host runtime implemented and host-tested (not target-qualified); graphics RHI not started |
| Source inventory commit | `e6fa8c2574e3ae543168b5695889349dbefe7473` |
| Target-qualified code baseline | `d6b7f1c` plus the external GPU-CW application artifact |
| Last reviewed | 2026-09-02 |
| Immediate implementation boundary | External-host runtime, followed by one graphics vertical slice |

The source inventory commit identifies the exact tree examined for this design. The target-qualified
baseline is deliberately separate: the inventory commit adds documentation on top of the last code
used by GPU-CW, but has not itself changed or requalified runtime behavior.

## Why a living architecture record accelerates delivery

The missing work is no longer mainly discovering whether the console can draw. It is moving already
working mechanisms behind boundaries that an engine can safely consume. That kind of work slows down
when each session has to rediscover ownership, when APIs are invented before lifetimes are settled, or
when a locally convenient wrapper creates a second presentation or memory owner.

This document accelerates the work in six concrete ways:

1. **It removes source rediscovery.** The inventory below names the current class and the behavior that
   must move, remain, or be wrapped. A resumed session starts from an exact ownership map.
2. **It prevents shadow implementations.** Every responsibility has one intended owner. An adapter can
   be rejected during review if it starts allocating direct memory, writing registers, or presenting.
3. **It makes work sliceable.** Each delivery slice has entry criteria, a finite API surface, tests and
   a compatibility condition. Implementation can be reviewed and reverted without destabilizing the
   entire renderer.
4. **It turns hardware lessons into architecture.** Checked suspension, exact flip retirement,
   shader/container integrity and in-flight memory safety are API invariants, not comments that a new
   caller can accidentally bypass.
5. **It separates three proofs.** Host correctness, successful target execution and correct pixels are
   tracked independently. A clean build or graceful exit cannot silently promote a rendering claim.
6. **It postpones irreversible engine coupling.** MonoGame or Stride can be selected after the substrate
   proves its boundary through `Renderer3D`, rather than shaping SharpProspero around an untested
   adapter.

The document is therefore part of the implementation, not preliminary prose. A proposed API that
cannot be placed in its state, ownership and error tables is not ready to be coded.

## Authority and maintenance

Four durable records have different jobs:

| Record | Authority |
|---|---|
| `docs/engine-substrate.md` | Scope, priorities, endpoint and delivery order |
| This document | Current ownership, approved boundaries, lifetimes, API decisions and open design questions |
| GuitarHeroPs5 evidence graph and session records | Target observations and the claims they support |
| GuitarHeroPs5 `HANDOFF.md` | Exactly one current task, deployed artifact and immediate next action |

Update this document in the same commit as any SharpProspero change that alters a listed owner,
state transition, lifetime, public boundary or decision. Update the source inventory commit after the
change has landed. Do not silently rewrite an accepted decision: supersede it in the decision log and
retain the reason. Experimental history remains in the evidence graph, not here.

Terms used for design status are:

- **Observed:** present in the source inventory commit.
- **Accepted:** the intended architectural contract for new work.
- **Proposed:** concrete enough to review, but names or details may change before implementation.
- **Open:** a decision is required before the affected slice can start.
- **Implemented:** host-tested source exists.
- **Target-qualified:** a named target run supports the stated capability.

## Scope and non-goals

SharpProspero owns an external-host runtime, an engine-neutral graphics RHI, GPU resources and their
lifetimes, shader artifacts and reflection, presentation safety, input-device lifecycle, engine-neutral
audio primitives, and generic package/resource access.

SharpProspero does not own an ECS, scene graph, physics, animation, navigation, a material system, an
editor, an engine asset format, or MonoGame/Stride public APIs. Those remain above an engine-specific
adapter. The low-level `Graphics.Agc` and `Interop` layers remain available for SDK development and
diagnostics, but they are not the engine contract.

## Proven constraints and current limits

The following constraints are inputs to the design, not optional implementation details:

- GPU-CW established a target-visible structured-buffer triangle with stable yellow upper and green
  lower regions and graceful Circle exit using serialized read-only slot metadata.
- That result qualifies a fork diagnostic artifact and its host setup. It does not yet qualify the
  complete upstream `Renderer3D` path or a general RHI.
- A submitted graphics frame reaches a checked `AgcDevice.SuspendPoint()` before display retirement is
  awaited or the display frame is advanced.
- Exactly one component owns presentation for a frame.
- A shader container, microcode, stage linkage, reflection and resource binding form one indivisible
  contract.
- GPU-visible command, state, shader and resource memory remains alive and unmodified until its
  submission retires.
- Target execution and correct rendering are separate claims; a graceful exit proves neither pixels
  nor resource correctness by itself.

## Current source ownership inventory

The target owner column describes the endpoint. Migration occurs in the delivery slices later in this
document; it is not a claim that the endpoint already exists.

| ID | Current source and observed responsibility | Current coupling or risk | Target owner | Why resolving it accelerates later work |
|---|---|---|---|---|
| HOST-01 | `Application/ProsperoApp.cs`: initializes user/system services, opens display and one pad, owns timing, polling, dispatch, frame loop and teardown | An external engine already owns a loop; nesting loops would duplicate timing, input and presentation | `ProsperoRuntime` owns services/devices; `ProsperoApp` remains a convenience loop | MonoGame, Stride and test harnesses can host SharpProspero without adopting its application architecture |
| HOST-02 | `Application/AppConfig.cs`: display, pad, user and splash configuration | Configuration is tied to the convenience host | Runtime/display/input descriptions, with `AppConfig` adapting to them | Makes headless or externally hosted configurations explicit without breaking current apps |
| HOST-03 | `Application/FrameContext.cs`: CPU surface, timing, one input sample, dispatcher and exit flag | Assumes one reused CPU-drawing context and one pad | Remains a `ProsperoApp` compatibility type; substrate exposes independent runtime/input/frame objects | Avoids contaminating a general graphics frame with convenience-loop state |
| PRES-01 | `Graphics/DisplayDevice.cs`: opens VideoOut, allocates/registers scan-out buffers, exposes CPU and GPU addresses, submits CPU flips, waits, rotates and disposes | Raw handles/pointers leak upward; both CPU and GPU paths can initiate presentation | Internal display/swap-chain implementation behind `PresentationContext`; CPU `DisplayDevice` API remains compatible | Establishes one presentation owner and lets engines render without learning VideoOut rules |
| GPU-01 | `Graphics/Agc/AgcDevice.cs`: process-global AGC initialization, raw submit description and suspend point | Static global state has no device lifetime, ownership or fault state | Internal backend owned by `GraphicsDevice` | Gives callers an explicit device state and centralizes submit failure handling |
| CMD-01 | `Graphics/Agc/DrawCommandBuffer.cs`: owns or wraps direct memory and emits raw AGC packets/registers | Unsafe packet methods can bypass frame, pipeline and resource validation | Internal command encoder behind `GraphicsCommandList`; low-level API remains for SDK diagnostics | Engines receive legal operations while SharpProspero retains one packet implementation |
| SHD-01 | `Graphics/Agc/ShaderBinary.cs`: parses a whole `.sb`, exposes header/code and serialized resource slots | Reflection is partial and preparation exposes `AgcShader` | Public immutable `ShaderModule`; `PreparedShader` becomes backend-owned pipeline state | Prevents offset guessing and lets every adapter consume one validated shader contract |
| RES-01 | `Memory/DirectMemoryRegion.cs`: reserves/maps/releases GPU-visible memory and exposes pointer/physical offset | Immediate disposal is unsafe when GPU work still references the region | Internal allocation owned by device resources and submission retirement | Every buffer/texture inherits one correct alignment and deferred-destruction policy |
| RES-02 | `Graphics/Agc/MeshBuffer.cs`: converts indexed input to expanded vertices, uploads direct memory and exposes pointers | It is specialized to one `Vertex`, reports an index buffer it does not currently allocate, and has immediate disposal | Compatibility mesh wrapper over typed `GpuBuffer` resources | Removes fixed mesh layout from the platform API and makes indexed drawing honest |
| REN-01 | `Graphics/Renderer3D.cs`: owns built-in shaders, linkage, per-frame command/state/constants memory, render target, viewport, register assembly, descriptors, draw, submit, suspend, flip and rotation | A draw implicitly ends and presents a frame; it imports AGC, Interop and direct memory | Acceptance client implemented only with public RHI types | Its successful migration proves the abstraction is usable before an engine adapter begins |
| STATE-01 | `Cx*`, `Uc*`, `AgcViewport`, descriptors and register defaults under `Graphics.Agc` | Correct but firmware-facing representation leaks if used directly by adapters | Internal pipeline/render-pass/resource compiler | Static state is derived once and validated instead of rebuilt by each engine backend |

### Current frame path

The observed GPU path in `Renderer3D.SubmitPipeline` is:

```text
select per-frame slot
write constants and assemble render/shader state
reset command buffer
record wait-until-safe
record state, descriptors and one draw
record flip packet
submit
checked suspend point
wait/advance DisplayDevice
rotate renderer slot
```

The observed CPU path in `ProsperoApp.Run` is:

```text
select DisplayDevice.BackBuffer
poll input and dispatcher
call OnFrame
DisplayDevice.Present (submit flip, wait for exact flip, rotate)
```

These are two valid implementations sharing one display object, but they expose no common frame owner.
The RHI must unify ownership without making the CPU surface path submit AGC work it does not need.

## Target layers and dependency rule

```text
Game or engine
    -> engine-specific adapter
        -> SharpProspero public substrate
            Application.ProsperoRuntime
            Graphics.GraphicsDevice / GraphicsFrame / GraphicsCommandList
            Graphics.ShaderModule / GraphicsPipeline / GpuBuffer / Texture
            Input and Audio public devices
                -> SharpProspero internal graphics/platform backend
                    Graphics.Agc / Memory / Interop
```

Accepted dependency rules:

1. An engine adapter imports only public substrate namespaces. It does not import `Graphics.Agc`,
   `Interop.Agc`, `Interop.VideoOut`, or allocate `DirectMemoryRegion`.
2. Public substrate objects do not expose native pointers, register offsets, raw descriptors, output
   handles or command-buffer handles as their normal binding contract.
3. `Renderer3D` must obey rule 1 after migration. A source-level dependency test enforces it.
4. One escape hatch may eventually exist for SDK diagnostics, but it is explicitly unsafe, is not
   accepted by engine adapters, and does not weaken validation in the normal API.

Why this accelerates: dependency checks catch architectural drift in seconds on the host, before a
convenient low-level call grows into an engine-specific shadow renderer that requires target debugging.

## Proposed public object model

Names in this section are proposed. The responsibilities and ownership boundaries are accepted.

### Runtime host

```csharp
using ProsperoRuntime runtime = ProsperoRuntime.Initialize(runtimeDescription);
runtime.PollEvents();
IReadOnlyList<GamePadDevice> pads = runtime.GamePads;
DisplayDevice display = runtime.OpenDisplay(displayDescription);
```

`ProsperoRuntime` owns service initialization/termination and device discovery. It does not start a
loop, calculate an engine's time, or present. `ProsperoApp.Run()` becomes a compatibility composition
that constructs a runtime, opens its configured devices, and preserves its existing callbacks and
timing behavior.

Why this accelerates: the external engine can be introduced later without first untangling two nested
run loops, while existing SharpProspero applications retain their compact host.

### Graphics device and frame

```csharp
using GraphicsDevice graphics = GraphicsDevice.Create(display, description);
GraphicsFrame frame = graphics.BeginFrame();
GraphicsCommandList commands = frame.CreateCommandList();
commands.BeginRenderPass(renderPass);
commands.SetPipeline(pipeline);
commands.SetResources(bindings);
commands.Draw(vertexCount, instanceCount);
commands.EndRenderPass();
commands.Close();
Submission submission = graphics.Submit(frame);
graphics.Present(submission);
```

The device owns AGC initialization, frame slots, command/state/upload memory, submission tracking,
fault state and deferred destruction. A frame may contain multiple draws and, later, multiple command
lists and render passes. Draw never submits or presents.

For the first vertical slice, one frame owns one command list and one display render pass. The object
model deliberately leaves room to expand without exposing the backend.

### Resources and pipelines

- `GpuBuffer` is created from a typed description containing size, usage and update policy.
- `Texture` owns layout, direct memory, upload and its backend descriptor.
- `RenderTarget` and `DepthTarget` own compatible views over textures.
- `Sampler` is immutable state.
- `ShaderModule` owns one complete compiled container and immutable reflection.
- `GraphicsPipeline` owns prepared shaders, stage linkage and immutable raster, blend, depth, topology,
  vertex-layout and target-format state.
- `ResourceBindings` map declared shader resources to compatible resources by logical slot.
- `FrameUploadAllocator` owns transient CPU-written, GPU-read data until the frame retires.

Why this accelerates: an adapter translates engine concepts into descriptions once. It never recreates
tiling, descriptor or register logic, so each new engine feature builds on previously qualified code.

## Frame and presentation state machine

The API state is explicit even if the first implementation uses a single command buffer internally.

```text
Available
   | BeginFrame
   v
Recording ----BeginRenderPass----> InRenderPass
   ^                                  |
   |--------- EndRenderPass ----------|
   |
   +---- Close ----> Closed ---- Submit ----> InFlight ---- retire ----> Retired
                                                                  |          |
                                              display submission --+          |
                                                                  v          |
                                                               Presented -----+
```

| State | Legal operations | Owner and invariant | Illegal operation behavior |
|---|---|---|---|
| Available | `BeginFrame` | Device owns a retired frame slot and safe display buffer | Fail before recording if no slot becomes available within the bounded policy |
| Recording | Create/use command list, begin pass, write frame uploads, close | Frame exclusively owns its slot; no previous GPU use remains | Draw without a pass or submit before close throws validation error |
| InRenderPass | Bind compatible pipeline/resources, viewport/scissor, draw; end pass | Exactly one active pass on the command list | Nested pass, target mutation or missing required binding fails before unsafe packet emission |
| Closed | Submit once | Recording is immutable | Further recording or a second submit throws |
| InFlight | Query status; device may append/execute platform boundary operations | Submission owns command memory and referenced resources | Dispose or overwrite referenced storage is deferred or rejected |
| Retired | Recycle slot and release deferred resources | Checked GPU completion has occurred | Retirement must not be inferred from elapsed time or a build/test result |
| Presented | Return presented frame id; slot may become Available when both GPU and VideoOut are safe | Presentation owner observed the exact display boundary | A second present for the same frame throws |

### Physical order versus logical API

On the target-proven path the flip packet is recorded into the draw command buffer before submit;
after submit, `SuspendPoint` is checked before `DisplayDevice.AdvanceFrame`. Therefore a friendly
`Present(submission)` API does **not** imply that the native flip is only emitted at that call. The
device may encode the flip while closing/submitting the display frame and make `Present` the ownership
and exact-retirement boundary.

The accepted invariant is semantic: the caller requests presentation once, and the device preserves
the proven native order. API naming must not force an unsafe rearrangement of that order.

Offscreen-only frames retire without entering `Presented`. A frame containing a display pass has
exactly one presentation request and owner.

Why this accelerates: implementation reviews can compare code to a finite transition table. Black
screens caused by double flips, early reuse or missing suspension become host-testable state errors
instead of ambiguous hardware symptoms.

## Resource lifetime and in-flight ownership

Every GPU resource follows this conceptual lifecycle:

```text
Allocated -> Initialized -> ReferencedByRecording -> InFlight -> Retired -> Reusable/Disposed
```

Accepted rules:

1. The resource object owns its allocation; consumers never free `DirectMemoryRegion` directly.
2. Recording a resource adds it to the frame's usage set. Submission transfers that usage set to the
   returned submission until retirement.
3. `Dispose` is idempotent. If a resource is in flight, destruction is queued behind its latest use;
   public methods fail immediately after disposal is requested.
4. A dynamic update obtains fresh or retired storage. It never overwrites bytes referenced by an
   in-flight submission.
5. Prepared shader/header/code memory lives at least as long as every pipeline and submission that
   references it.
6. Display allocations outlive pending flips. Device shutdown drains or reports bounded failure before
   unregistering them.
7. The graphics device and recording objects are frame-thread owned in P0. Cross-thread resource
   creation may be added only with an explicit synchronization contract.

Why this accelerates: these rules turn scattered per-renderer rings into one reusable allocator and
make resource bugs deterministic. Textures, constants, UI vertices and skeletal data can later share
the same retirement mechanism.

## Shader, reflection and binding contract

`ShaderModule` treats the `.shader_header` and `.shader_text` sections as one artifact. Its immutable
identity includes a container hash, stage, format version and source/toolchain lineage. Reflection
records each input, output and resource by kind, logical slot, dword offset, width and small/full size
form.

Pipeline creation must:

1. validate every complete shader container;
2. validate supported stages and vertex inputs;
3. link stage outputs/inputs;
4. validate resource declarations against the selected binding layout;
5. prepare shaders and derive immutable static state; and
6. retain the module identity for diagnostics.

Command recording binds logical resources. Only the backend translates reflected offsets to shader
registers. A missing, wrong-kind, wrong-width or mismatched size-form resource fails before submit.
There is no public fallback to guessed offsets.

Why this accelerates: the GPU-CS through GPU-CV failure class becomes structurally unrepresentable in
the normal API. A shader experiment cannot accidentally graft resource-reading microcode into a
resource-less contract and still appear ready because a build passed.

## Error and fault model

| Failure class | Public behavior | Required diagnostic context |
|---|---|---|
| Invalid description or state transition | `ArgumentException`, `InvalidOperationException` or a dedicated validation exception before native submission | Object type/name, current state, requested operation and violated contract |
| Shader/pipeline incompatibility | Pipeline creation fails closed | Module hashes/stages, resource or linkage mismatch and expected/actual form |
| Native platform failure | `ProsperoException` or a graphics-specific wrapper preserving the native result | Native operation, code, frame/submission id and device state |
| Submit/suspend failure | Device enters `Faulted`; no frame memory is recycled automatically | Last completed stage, command size, frame id and native result |
| Bounded wait timeout | Explicit timeout/fault result; never infinite waiting | Wait kind, frame/flip id, elapsed bound and last observed status |
| Use after disposal | `ObjectDisposedException` | Resource/device identity |
| Deferred destruction | `Dispose` returns after scheduling safe release; diagnostics may report pending bytes | Latest submission retaining the object |

A faulted device rejects new recording and submission. Shutdown may release only resources proven safe;
it must not disguise an unknown in-flight state as successful teardown.

Why this accelerates: callers receive failures at the layer where they can act, and logs retain enough
identity to connect a target symptom to the pipeline, frame and artifact without repeating probes.

## Compatibility strategy

- `ProsperoApp` remains source-compatible while delegating service/device ownership to
  `ProsperoRuntime`. Its CPU surface loop continues to present once per callback.
- `DisplayDevice` remains usable for CPU surface applications. Its public raw GPU properties are
  treated as legacy/advanced API during migration; the RHI does not require consumers to use them.
- `Renderer3D` retains its public entry points until a deliberate versioning decision. Internally it
  becomes an RHI client.
- `Graphics.Agc` remains available to SDK maintainers and diagnostic probes. It is not removed merely
  because the RHI wraps it.
- The build/link/package toolchain is outside this refactor and must remain unchanged unless a slice
  demonstrates a specific need.

Compatibility is verified at every slice rather than after the migration. This keeps existing samples
as controls and avoids a long-lived branch where architecture and behavior change simultaneously.

## Smallest coherent delivery slices

### Slice 0 — Architecture enforcement

No runtime behavior changes. Add host checks that encode the dependency rules, state model fixtures and
shader-contract negative cases. This document and those checks are the review baseline.

Exit criteria:

- the listed source inventory is complete for the first two implementation slices;
- a test can reject a fixture that imports AGC from an engine-facing acceptance client;
- shader contract tests reject wrong kind, slot, width and size form, including a plausible near miss;
- open decisions that affect Slice 1 are resolved.

### Executable-check activation ledger

Architecture tests are activated only when they can be truthful and green. A check for a future API is
first proven against valid and deliberately invalid fixtures, then applied to live source in the same
slice that introduces the owning type. This avoids both unprotected implementation and a permanently
red suite that encourages stubs or waived assertions.

| Check | Current state | Activation gate |
|---|---|---|
| Engine-facing code has no AGC, Interop, VideoOut or direct-memory dependency | Active as a fixture-proven checker and a non-expanding `Renderer3D` legacy-debt ratchet; zero-dependency assertion pending | Make zero-dependency live when `Renderer3D` migrates to the RHI |
| `ProsperoApp` delegates lifecycle to `ProsperoRuntime` | Active as a non-expanding/non-duplicating direct-lifecycle-call ratchet | Require zero direct ownership plus ordered delegation when Slice 1 introduces `ProsperoRuntime` |
| Draw operations cannot submit or present | Backlog | Activate with `GraphicsCommandList`; fake encoder must observe recording events only |
| One presentation owner per display frame | Backlog | Activate with the frame/presentation token; duplicate present must fail before native interaction |
| In-flight resources cannot be destroyed or overwritten | Backlog | Activate with `Submission` and device resources; fake retirement must prove deferred release |
| Shader bindings derive from serialized metadata | Active for built-in resource lookup, wrong kind/slot and the offset-8 absolute-ISA near miss | Move the same fail-closed contract onto `ShaderModule`/pipeline binding in Slice 2 |
| Invalid frame transitions fail before submission | Backlog | Activate with the frame state owner; exercise every illegal edge and assert zero backend submits |
| Existing public API remains source-compatible | Active through a compile-only `ProsperoApp`/`DisplayDevice`/`Renderer3D` client and signature checks | Extend before each compatibility-bearing refactor; replace only after an approved versioning decision |

The active ratchets deliberately permit today's documented `Renderer3D` and `ProsperoApp` debt to
shrink but not spread. They do not claim that the endpoint already exists. Backlog entries are design
obligations, not optional tests, and become merge gates with their named owning slice.

### Slice 1 — External-host runtime

Extract service, display and input initialization/teardown from `ProsperoApp` into an explicit
`ProsperoRuntime`. `ProsperoApp` delegates to it but retains loop behavior. Do not introduce the RHI or
change presentation in this slice.

Exit criteria:

- host tests prove initialization/disposal ordering and idempotence;
- the existing `ProsperoApp` API compiles unchanged;
- a minimal external host can initialize, poll and shut down without entering `ProsperoApp.Run()`;
- no graphics target run is required unless observable target startup/order changes.

This is the first implementation slice because it has one ownership purpose and unblocks every engine
host without mixing in renderer risk.

### Slice 2 — Display graphics vertical slice

Introduce one device, frame, command list, display render pass, immutable shader modules/pipeline,
immutable structured vertex buffer and non-indexed draw. Preserve the proven native ordering. Exclude
textures, depth, index buffers, multiple command lists and dynamic updates.

The reference client records one structured-buffer triangle, submits once and presents once. It uses no
AGC, Interop or direct-memory APIs. Host validation precedes a target run; the GPU-CW artifact remains
the control.

Exit criteria:

- multiple draws can be recorded before the single close/submit/present boundary, even if the first
  target scene uses one draw;
- invalid state and binding transitions fail before submit;
- submission retains every referenced object through retirement;
- target output matches the declared reference scene and exits gracefully;
- logs separately identify artifact integrity, submit/suspend/retirement and pixel classification.

This is the smallest coherent graphics slice because omitting shader/resource/pipeline ownership would
merely rename the current raw command layer. Adding textures or depth would expand the first target
diagnosis without proving a stronger abstraction boundary.

### Later slices

1. Indexed buffers and general vertex layouts.
2. Texture/sampler resources and binding.
3. Owned depth/stencil and offscreen render targets.
4. Dynamic updates and frame upload allocation.
5. `Renderer3D` migration and multi-draw/single-present target qualification.
6. Multiple-controller lifecycle, audio voices and generic resource loaders.
7. Engine selection and adapter work.

Each capability crosses one target boundary at a time and keeps its previous control deployable.

## Validation architecture

Host validation must include:

- source/dependency tests for the public boundary;
- state-machine transition tests, including every illegal edge;
- submission retention and deferred-disposal tests with fake retirement;
- shader/reflection/binding valid, invalid and near-miss fixtures;
- pipeline compatibility tests for stage, layout and target formats;
- `ProsperoApp` compatibility tests; and
- deterministic diagnostics containing frame, submission, pipeline and shader identities.

Target validation must keep separate observations for:

1. artifact identity and provenance;
2. launch and initialization;
3. command recording and submit;
4. checked suspend return;
5. exact retirement/presentation;
6. visible pixel classification; and
7. graceful exit and teardown.

A host test cannot promote a hardware capability, and a visible control cannot validate an unrelated
resource contract. A repeated non-discriminating target result returns the work to the last qualified
boundary rather than spawning another symptom-only variant.

## Decision log

| ID | Status | Decision and reason |
|---|---|---|
| ADR-001 | Accepted | SharpProspero is an engine substrate, not a game engine. Engine systems and engine-specific APIs remain above adapters. |
| ADR-002 | Accepted | Draw, frame close, submit and presentation are distinct public operations. A draw never implicitly presents. |
| ADR-003 | Accepted | One device owns frame slots, command/state/upload memory, submission retirement and deferred destruction. |
| ADR-004 | Accepted | `ShaderModule` owns a complete container and immutable reflection; logical binding is resolved only from that metadata. |
| ADR-005 | Accepted | `Renderer3D` is the architectural acceptance client and must stop importing AGC, AGC interop and direct memory. |
| ADR-006 | Accepted | `ProsperoApp` remains a convenience wrapper; external engines use a runtime that does not own their loop. |
| ADR-007 | Accepted | Engine choice is deferred until the RHI boundary passes its acceptance client. |
| ADR-008 | Accepted | The friendly presentation API preserves the target-proven native order even when flip encoding occurs before the logical `Present` call. |
| ADR-009 | Proposed | P0 graphics recording is single-threaded and frame-thread owned. Revisit only with a concrete engine requirement and synchronization design. |
| ADR-010 | Open for Slice 2 | Decide whether `GraphicsDevice` owns `DisplayDevice` or borrows it with an explicit lifetime token. It must not be ambiguous. |
| ADR-011 | Open for Slice 2 | Define the bounded retirement mechanism and whether `Submission` exposes polling, waiting, or both. |
| ADR-012 | Open for Slice 2 | Decide whether the legacy raw GPU properties on `DisplayDevice` remain public long-term or move behind an explicitly unsafe advanced surface. |

## Delivery acceleration index

| Document section | Decision made early | Rework or delay avoided | Capability unlocked |
|---|---|---|---|
| Ownership inventory | Which class owns each platform behavior | Duplicate loops, flips, allocators and shader preparation | Independent runtime and RHI slices |
| Dependency rule | What an adapter may import | Engine-specific AGC code and a shadow SDK | Swappable MonoGame/Stride/custom adapters |
| State machine | Exact legal frame order | Hardware runs for invalid sequencing | Multi-draw and multi-pass recording |
| Resource lifecycle | When memory can be reused/freed | Intermittent corruption and overlarge per-renderer rings | Dynamic buffers, textures and uploads |
| Shader contract | Metadata is authoritative and indivisible | Repetition of the GPU-CS–CV blunder class | Safe generic resource binding |
| Error model | How failure changes device/resource state | Infinite waits and misleading graceful-exit claims | Actionable target telemetry |
| Compatibility plan | What stays stable each slice | Big-bang refactor and lost controls | Continuous upstream-reviewable commits |
| Slice exit criteria | What “done” means before advancing | Ten near-identical target tests | One-variable capability progression |

## Architecture acceptance criteria

The substrate boundary is established when all of the following are true:

- `Renderer3D` imports neither `SharpProspero.Graphics.Agc` nor `SharpProspero.Interop.Agc`, allocates no
  direct memory, emits no raw register/descriptor/packet and does not own presentation mechanics;
- its implementation composes public device, resource, shader, pipeline and command APIs;
- one frame records more than one draw before one submission and one presentation;
- resources cannot be overwritten or freed while referenced by an in-flight submission;
- shader linkage and resource mismatches fail on the host before submit;
- constant, interpolated and structured-buffer reference scenes remain reproducible on target;
- exact submit, suspension, retirement and exit telemetry remains available through engine-neutral
  diagnostics; and
- an engine adapter can use the same APIs without referencing `Renderer3D`.

Passing these criteria authorizes engine-adapter selection. It does not claim complete engine support;
shader authoring, content integration and P1 renderer features remain separately scoped work.

## Immediate next design action

Slice 1 has landed as a behavior-preserving extraction: `ProsperoRuntime` owns service, display and
input initialization/teardown without owning the engine loop, while `ProsperoApp` delegates and
retains its public API and CPU presentation behavior. Ordered-delegation tests are active in the
same change. No graphics implementation should begin until the Slice 2 ownership and retirement
choices (ADR-010 through ADR-012) are accepted against `DisplayDevice`, the target-proven
submit/suspend/advance order, and the expected external-host lifetime.
