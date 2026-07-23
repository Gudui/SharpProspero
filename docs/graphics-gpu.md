---
title: GPU command layer
parent: Graphics
nav_order: 2
---

# GPU command layer

The Agc layer drives the graphics processor directly: you describe shader resources and pipeline state as small register blocks, record them into a command buffer, and submit the buffer for the GPU to run. Everything on this page lives in `SharpProspero.Graphics.Agc`.

{: .note }
> This is the shader-based path, for meshes, custom shaders, depth buffering, and blending. Most applications draw with the CPU [Surface](graphics.md) instead and never touch a single type here. Reach for this layer only when you are writing a renderer.

## The two layers

The GPU is exposed as two layers. The lower one is the complete command interface: `SceAgc` holds the command builders (draws, dispatches, register writes, synchronization, shader create and link) and `SceAgcDriver` holds the driver calls (submit, queue, flip, wait). Every builder takes the command buffer first and returns the address of the packet it wrote.

The higher layer wraps that for everyday use:

- `DrawCommandBuffer` records commands into GPU-readable memory. `Allocate` hands back a ready buffer; the record calls cover register writes, index state, draws, synchronization, and the present calls.
- `AgcShader` builds a shader from its compiled binary. Shaders are compiled ahead of time; the runtime only loads them.
- `AgcDevice` does the one-time `Initialize`, submits a buffer with `Submit`, and closes the frame with `SuspendPoint`.
- `AgcFormats` holds the surface-format and channel-select enumerations.

A frame waits for the display to release a buffer, records state and draws, queues the flip, submits, and closes:

```csharp
int videoOutHandle = 0;   // from the display device
uint backBuffer = 0;      // the buffer index being rendered this frame

AgcDevice.Initialize();
using var dcb = DrawCommandBuffer.Allocate(1 << 20);

// ... per frame:
dcb.Reset();
dcb.WaitUntilSafeForDisplay(videoOutHandle, backBuffer);
// record register blocks and draws (see below)
dcb.SetFlip(videoOutHandle, (int)backBuffer);
AgcDevice.Submit(dcb);
AgcDevice.SuspendPoint();
```

## Register blocks

Pipeline state reaches the GPU as context registers. A single register is a `CxRegister` - an offset and a 32-bit value:

```csharp
public struct CxRegister
{
    public ushort Offset;
    public uint Value;
}
```

The typed blocks (`CxRenderTarget`, `CxBlendControl`, and the rest) group the registers for one piece of state and give you a setter per field, so you never pack bits by hand. They all follow the same rhythm. The register offsets and reset values are not baked into the SDK - they come from the graphics driver at runtime - so you load them into the block with `Init` first, then apply setters, then write the block's `Registers` into the command buffer, one `SetContextRegister` call per entry.

```mermaid
flowchart LR
  D["driver register<br/>defaults"] --> I["block.Init(defaults)"]
  I --> S["typed setters<br/>(or AgcRenderTargetSetup.Initialize)"]
  S --> R["block.Registers"]
  R --> W["dcb.SetContextRegister<br/>per entry"]
  W --> Sub["AgcDevice.Submit(dcb)"]
```

Writing a block into the buffer is always the same loop:

```csharp
foreach (CxRegister reg in block.Registers)
    dcb.SetContextRegister(reg.Offset, reg.Value);
```

Blocks that address one of the eight hardware render-target slots expose `SetSlot`, which shifts the offsets to target that slot; call it after `Init`.

## Render-target registers

`CxRenderTarget` is the sixteen-register color-target block, with typed setters and getters for every field - format, channel type and order, dimensions, tiling, the color and metadata addresses, and the blend and rounding behavior. You rarely set those one at a time. `AgcRenderTargetSetup.Initialize` fills the whole block from a `RenderTargetSpec`, applying the same setup the graphics core does, including the blend, clamp, and rounding modes it derives from the channel type:

```csharp
// defaults: sixteen CxRegister values loaded from the driver for a color target.
var rt = new CxRenderTarget().Init(defaults);
AgcRenderTargetSetup.Initialize(rt, new RenderTargetSpec(
    CxRenderTarget.Format.k8_8_8_8,
    CxRenderTarget.ChannelType.kUNorm,
    CxRenderTarget.ChannelOrder.kStandard,
    width: 1920, height: 1080,
    dataAddress: gpuColorAddress));

foreach (CxRegister reg in rt.Registers)
    dcb.SetContextRegister(reg.Offset, reg.Value);
```

`CxDepthRenderTarget` is the companion sixteen-register block for a depth and stencil buffer: the same shape, with setters for the depth and stencil formats, dimensions, clear values, HTILE acceleration, and the read, write, and HTILE addresses, loaded from the driver defaults with `Init`.

## Blending

`CxBlendControl` is the one-register blend-control block for a render-target slot: whether blending is on, the source and destination multipliers and the combine function for color and alpha, and whether alpha uses its own equation. Standard alpha blending is source-alpha over one-minus-source-alpha:

```csharp
var blend = new CxBlendControl().Init(defaults);
blend.SetSlot(0)
     .SetBlend(CxBlendControl.Blend.kEnable)
     .SetColorSourceMultiplier(CxBlendControl.ColorSourceMultiplier.kSrcAlpha)
     .SetColorDestMultiplier(CxBlendControl.ColorDestMultiplier.kOneMinusSrcAlpha)
     .SetColorBlendFunc(CxBlendControl.ColorBlendFunc.kAdd);

foreach (CxRegister reg in blend.Registers)
    dcb.SetContextRegister(reg.Offset, reg.Value);
```

`CxBlendColor` is the constant blend color the `kConstantColor` and `kConstantAlpha` multipliers reference - four floating-point components set with `SetRed`, `SetGreen`, `SetBlue`, and `SetAlpha`.

## Depth and stencil

`CxDepthStencilControl` is the one-register test-control block: whether the depth test and depth write are on and the comparison to use, and whether the stencil test is on with its front- and back-face comparisons.

```csharp
var ds = new CxDepthStencilControl().Init(defaults);
ds.SetDepth(CxDepthStencilControl.Depth.kEnable)
  .SetDepthWrite(CxDepthStencilControl.DepthWrite.kEnable)
  .SetDepthFunction(CxDepthStencilControl.DepthFunction.kLessEqual);

foreach (CxRegister reg in ds.Registers)
    dcb.SetContextRegister(reg.Offset, reg.Value);
```

The stencil state fans out into three more one-register blocks: `CxStencilControl` carries the test value, compare mask, write mask, and operation value for front-facing primitives, `CxStencilControlBackFace` the same for back faces, and `CxStencilOpControl` the operations applied on stencil fail, depth-stencil pass, and depth fail for both faces. Each follows the same `Init`-then-set-then-record pattern.

## Viewport and scissor

`AgcViewport` maps the clip-space cube the vertex shader outputs onto a pixel rectangle of the render target and clips anything outside a pixel rectangle. Without it, nothing maps clip space to pixels and nothing is clipped. Set the rectangle, depth range, and scissor, then record the fourteen context registers it produces:

```csharp
var vp = new AgcViewport();
vp.SetViewport(0, 0, 1920, 1080);
vp.SetScissor(0, 0, 1920, 1080);

Span<CxRegister> regs = stackalloc CxRegister[AgcViewport.RegisterCount];
int count = vp.WriteTo(regs);
for (int i = 0; i < count; i++)
    dcb.SetContextRegister(regs[i].Offset, regs[i].Value);
```

The vertical scale is negated for you, so a positive height maps clip space with y pointing up onto a target whose rows count downward from the top. This covers the single-viewport case that scan-out rendering uses.

## Texture and sampler descriptors

To sample a texture in a shader you describe it to the GPU as two small descriptors and point a shader slot at each.

`AgcTextureDescriptor` builds the eight-word image descriptor (a "T#"): where the pixels are, the surface size and format, how its channels map to red-green-blue-alpha, and the mip and array ranges. Build one for an image already laid out in tiled memory, then write its words into GPU-readable memory:

```csharp
var tex = new AgcTextureDescriptor();
tex.SetType(AgcImageType.Texture2D);
tex.SetBaseAddress(gpuTextureAddress);
tex.SetDataFormat((uint)AgcFormats.ChannelLayout.k8_8_8_8);
tex.SetChannelType(AgcTextureChannelType.UNorm);
tex.SetDimensions(1024, 1024);
tex.SetChannelOrder(AgcChannelSource.Red, AgcChannelSource.Green, AgcChannelSource.Blue, AgcChannelSource.Alpha);
tex.SetTilingIndex((int)AgcTileMode.RenderTarget);

Span<uint> imageWords = stackalloc uint[AgcTextureDescriptor.WordCount];
tex.WriteTo(imageWords);
```

`AgcSamplerDescriptor` builds the four-word sampler descriptor (an "S#"): how coordinates wrap, which filters apply for magnification, minification, and between mip levels, the level-of-detail range and bias, anisotropy, an optional depth comparison for shadows, and the border color. A default (all-zero) descriptor wraps, points, and samples the base level, so you only set what you need:

```csharp
var samp = new AgcSamplerDescriptor();
samp.SetAddressModes(AgcAddressMode.ClampToEdge, AgcAddressMode.ClampToEdge, AgcAddressMode.ClampToEdge);
samp.SetFilter(AgcFilter.Bilinear, AgcFilter.Bilinear, AgcMipFilter.Linear);
samp.SetLodRange(0f, 15f);

Span<uint> samplerWords = stackalloc uint[AgcSamplerDescriptor.WordCount];
samp.WriteTo(samplerWords);
```

Both descriptors address memory in 256-byte units, and the setters pack each field into the exact bits the hardware reads. They are value types - copy the written words to where the shader can reach them.

## Surface layout

Before a render target, depth buffer, or texture exists, you size and align its memory. `AgcSurface.Compute` gives the full layout for any tile mode - total size, base alignment, block dimensions, and per-mip offsets - computed the way the graphics address library computes it:

```csharp
AgcSurfaceLayout layout = AgcSurface.Compute(new AgcSurfaceDescription(
    AgcTileMode.RenderTarget, AgcSurfaceDimension.TwoD,
    width: 1920, height: 1080, bytesPerElement: 4));

using var mem = DirectMemoryRegion.Allocate((nuint)layout.TotalSizeBytes, layout.BaseAlignBytes);
```

`AgcTileMode.RenderTarget` is the tiling the display accepts and the one to give a color target; `AgcTileMode.Depth` is the depth-target tiling; `AgcTileMode.Linear` is plain row-major storage. Direct memory is the only source of GPU-visible buffers - see [Memory](memory.md). For a linear surface where you want just the padded row pitch and size, `LinearSurface.Compute` is the simpler arithmetic-only helper.

## Pixel tiling

The GPU does not read pixels in row-major order; it reads them swizzled into blocks. `AgcTiler` moves pixel bytes between plain linear order and that hardware-tiled order - to upload a texture you built in memory, or to read a rendered surface back into a linear image. `LinearSizeBytes` sizes the linear side, and `Tile` / `Detile` convert one mip level of one slice:

```csharp
var desc = new AgcSurfaceDescription(
    AgcTileMode.RenderTarget, AgcSurfaceDimension.TwoD,
    width: 512, height: 512, bytesPerElement: 4);

var linear = new byte[AgcTiler.LinearSizeBytes(desc)];   // fill with your image
var tiled = new byte[AgcSurface.Compute(desc).TotalSizeBytes];
AgcTiler.Tile(tiled, linear, desc);                      // now upload `tiled` to GPU memory
```

The tiled byte offset of element $(x, y)$ in a two-dimensional target is the block it falls in, times the block size, plus its swizzled position within that block:

$$
\text{offset} = \big(b_y \cdot W_b + b_x\big)\,S_{\text{block}} + e(x, y),
\qquad b_x = \left\lfloor \frac{x}{w_b} \right\rfloor,\quad b_y = \left\lfloor \frac{y}{h_b} \right\rfloor
$$

where $w_b \times h_b$ is the block in elements (`BlockWidth`, `BlockHeight`), $W_b$ is the target width in blocks, $S_{\text{block}}$ is the block size in bytes, and $e(x, y)$ is the within-block element offset the address equation produces. `Detile` runs the same mapping in reverse. Optimal textures - as opposed to render and depth targets - are built ahead of time by the texture tool, the same offline model as shaders.

## Formats

`AgcFormats` is the reference for the format and swizzle enumerations. `ChannelLayout` gives the bit widths per element, `ChannelType` gives how those bits are read (unorm, snorm, uint, sint, srgb, float), `TypedFormat` pairs a layout with an interpretation, and `ChannelSelect` names which source channel or constant a destination channel reads. The render-target block carries its own `CxRenderTarget.Format`, `CxRenderTarget.ChannelType`, and `CxRenderTarget.ChannelOrder` enums for the color-target register fields.

## Related pages

- [2D scenes](graphics-scene.md) - cameras, tile maps, and particles on the CPU surface.
- [Graphics](graphics.md) - the CPU `Surface` most applications draw with.
- [Memory](memory.md) - `DirectMemoryRegion`, the source of every GPU-visible buffer.
