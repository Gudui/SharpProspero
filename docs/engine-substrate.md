---
title: Engine substrate roadmap
parent: Application Modules
nav_order: 3
---

# Engine substrate roadmap

SharpProspero's engine substrate is the stable boundary between an external game engine and the
console-specific application, graphics, memory, input and audio layers. It lets an engine backend
render and run without importing `SharpProspero.Interop.Agc`, constructing raw register packets,
allocating direct memory itself, or adopting `ProsperoApp` as its game architecture.

This is a roadmap, not a statement that the APIs below already exist. Names are conceptual until an
API design and tests approve them.

## Scope

SharpProspero owns:

- an externally hosted runtime for service initialization, display, input and shutdown;
- a graphics RHI over AGC device, command, memory and presentation primitives;
- explicit frame, command-list, render-pass, submit, retirement and present boundaries;
- buffers, textures, samplers, shader modules, pipelines, render and depth targets;
- resource creation, upload, visibility, in-flight ownership and deferred destruction;
- shader-container reflection and fail-closed resource binding;
- vertex layouts, dynamic uploads and frame-local allocation;
- controller enumeration and lifecycle; and
- engine-neutral audio voices and resource loaders where the lower layers already support them.

SharpProspero does not own an ECS, scene hierarchy, physics, animation, navigation, material system,
editor, or an engine's asset/effect format. MonoGame-, Stride- and game-specific adapters live outside
this repository.

## Existing foundation

The current SDK already supplies the platform-specific mechanisms beneath this boundary:

- `DirectMemoryRegion` and GPU-visible allocation;
- `DisplayDevice`, registered framebuffers and presentation;
- `AgcDevice`, `DrawCommandBuffer`, register blocks and descriptors;
- render-target, depth/stencil, blend, viewport and scissor state;
- shader loading, preparation, linkage and serialized resource-slot metadata;
- surface layout and tiling;
- `ProsperoApp`, pad input, timing, filesystem/package access and application lifecycle;
- queued PCM/audio facilities and media decoding; and
- NativeAOT compile, link and packaging tools.

The gap is orchestration and public ownership. `Renderer3D` currently owns its shaders, constant and
per-frame memory, render-target setup, register state, resource bindings, draw recording, submission
and presentation. `ProsperoApp.Run` owns service initialization, display/controller construction and
the application loop. An external engine needs those responsibilities decomposed without duplicating
their low-level safety rules.

## Architectural endpoint

```text
External engine
  scene, ECS, animation, physics, content and engine loop
                         |
Engine-specific platform adapter
                         |
SharpProspero engine substrate
  ProsperoRuntime
  GraphicsDevice / GraphicsCommandList / GraphicsPipeline
  GpuBuffer / Texture / Sampler / ShaderModule
  RenderTarget / DepthTarget / FrameUploadAllocator
                         |
Existing SharpProspero AGC, memory, display, input, audio and system layers
```

The existing `ProsperoApp` remains a convenience wrapper over the substrate. `Renderer3D` becomes a
consumer of the graphics RHI rather than an alternate owner of AGC and presentation.

## P0 — Required before an engine adapter

### External-host runtime

Factor service, display, input and shutdown ownership into a composable runtime. It must support
explicit initialization, polling/system events and teardown without starting a second game loop.
`ProsperoApp` delegates to it and preserves existing behavior.

### Frame, command and presentation separation

One frame can contain multiple draws and render passes before one submission and presentation:

```text
BeginFrame
  BeginRenderPass
    bind pipeline/resources; draw; draw; draw
  EndRenderPass
  BeginRenderPass; draw; EndRenderPass
EndFrame
Submit
Present
```

The device owns frame rotation, wait-until-safe recording, checked suspend, exact flip retirement and
bounded failure. A draw call does not implicitly end or present the frame.

### Generic GPU resources

Resource descriptions produce typed buffers, textures, samplers, render targets and depth targets.
Creation owns alignment, direct-memory allocation, layout/tiling, upload and descriptors. Disposal is
deferred or rejected while a resource is in flight. Callers never receive a mutable raw descriptor as
the binding contract.

### Shader module and reflection contract

A shader module is a complete compiled container plus immutable stage, input/output and resource
metadata. A pipeline validates stage linkage and the exact declaration/consumption/binding contract.
Unsupported stages, resource forms or metadata fail before command submission.

SharpProspero does not compile HLSL, GLSL, SDSL or engine effects. Engine toolchains may later produce
complete SharpProspero shader modules. A small versioned set of precompiled containers can qualify the
RHI, but it is not a general shader compiler.

### Immutable graphics pipelines

A pipeline combines shader modules, vertex layout, topology, blend, rasterizer, depth/stencil and
render-target formats. Creation performs shader preparation/linkage and derives static AGC state once;
per-draw recording binds the prepared result rather than reconstructing it ad hoc.

### Command-list binding and drawing

The command surface supports render passes, pipeline binding, vertex/index buffers, shader resources,
viewports/scissors and non-indexed/indexed drawing. It validates required bindings and target formats
before recording unsafe packets.

## P1 — Required for practical engine coverage

- vertex formats/layouts covering scalar/vector float, normalized integer, half and instance rates;
- offscreen colour targets and render-to-texture;
- owned depth/stencil resources, clear operations and format validation;
- dynamic buffer/texture updates and a frame upload allocator that cannot overwrite in-flight data;
- multiple-controller discovery, association, connect/disconnect and output features;
- engine-neutral audio clip/voice control over the existing queue and mixer; and
- generic package streams and texture/mesh/shader/audio resource loaders, not XNB or Stride assets.

## Safety and validation contracts

The RHI must preserve these target-proven rules:

1. A submit has a checked suspend point before waiting for or advancing presentation.
2. There is exactly one presentation owner per frame and every wait is bounded.
3. Shader container, microcode, reflection, stage linkage and host binding are one contract.
4. Resource binding derives from serialized metadata; callers do not supply AGC register offsets.
5. CPU/GPU visibility, frame ownership and destruction are explicit.
6. Unsupported state fails before submission, with the resource/pipeline/frame named in the error.
7. Artifact integrity, successful execution and correct rendering remain separate test claims.
8. Each target probe advances one capability boundary from a qualified control.

Host tests must include valid, deliberately invalid and plausible near-miss cases. A passing host suite
does not waive a target qualification for a new hardware capability.

## Migration test

The architectural acceptance client is the current `Renderer3D` behavior. Rewrite it on the RHI so
that its source imports neither `SharpProspero.Graphics.Agc` nor `SharpProspero.Interop.Agc`, performs
no direct-memory allocation and submits no raw registers or packets.

Acceptance requires:

- existing public `Renderer3D` behavior is either preserved or deliberately versioned;
- the new implementation is a small composition of device, resource, pipeline and command APIs;
- more than one mesh can be recorded in one frame before one present;
- the established constant, interpolated and structured-buffer reference scenes remain reproducible;
- exact submit/retirement/exit telemetry remains available through engine-neutral diagnostics; and
- an engine adapter can use the same public API without referencing `Renderer3D`.

Passing this test establishes the abstraction boundary. It does not establish complete engine support;
shader authoring, content and the selected engine adapter remain separate projects.

## Delivery order

1. Inventory current ownership and write the API/lifetime decision record. No source refactor.
2. Introduce the external-host runtime while keeping `ProsperoApp` behavior and tests green.
3. Introduce device/frame/command/presentation primitives over the proven lifecycle.
4. Introduce typed buffers and shader modules with immutable reflection.
5. Introduce immutable pipelines, resource bindings and a single render pass.
6. Move the structured reference triangle through the RHI and qualify it on target.
7. Rewrite `Renderer3D` as the acceptance client and prove multi-draw/single-present behavior.
8. Add P1 capabilities one boundary at a time.
9. Only then select and implement an external engine adapter.

Each step is independently reviewable and keeps the previous target control available. Architecture
work beyond a small fix should be discussed with the upstream maintainer before an upstream PR; local
fork design and validation do not imply upstream acceptance.

## Immediate design deliverable

Before implementation, produce a source inventory and decision record that assigns current
`Renderer3D`, `ProsperoApp`, `DisplayDevice`, `AgcDevice`, `DrawCommandBuffer`, `ShaderBinary`,
`PreparedShader`, `MeshBuffer` and direct-memory responsibilities to the proposed layers. It must define
frame state transitions, ownership/disposal, error behavior, compatibility and the smallest first
vertical API slice. No RHI source change is ready until that review passes.
