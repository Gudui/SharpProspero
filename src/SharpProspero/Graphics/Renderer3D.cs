// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using SharpProspero.Graphics.Agc;
using SharpProspero.Interop.Agc;
using SharpProspero.Memory;
using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace SharpProspero.Graphics;

/// <summary>
/// Draws 3D meshes with the graphics processor. It renders straight into the display's framebuffers with
/// the built-in mesh shaders: give it a <see cref="MeshBuffer"/> and a model-view-projection matrix, and
/// it transforms and lights the mesh. Create one for a display, call <see cref="DrawMesh"/> each frame,
/// dispose it at shutdown.
/// </summary>
/// <remarks>
/// The renderer records one draw per frame into a command buffer and presents through the display. It
/// binds the mesh as a structured buffer the vertex program reads by index, and passes the matrices in a
/// constant buffer; both descriptors and the combined register state live in graphics-readable memory
/// because the processor reads them while it runs. The render target is linear, matching the display's
/// framebuffers, so no separate scan-out set-up is needed.
/// </remarks>
public sealed unsafe class Renderer3D : IDisposable
{
    // Two 4x4 matrices: model-view-projection, then model (for transforming normals).
    [StructLayout(LayoutKind.Sequential)]
    private struct Constants
    {
        public Matrix4x4 Mvp;
        public Matrix4x4 Model;
    }

    private const uint TargetMaskOffset = 0x008E;          // CB_TARGET_MASK
    private const uint GsOutPrimTypeOffset = 0x029B;       // VGT_GS_OUT_PRIM_TYPE
    private const uint PrimitiveTypeOffset = 0x0242;       // VGT_PRIMITIVE_TYPE (user-config)
    private const uint PrimitiveTriangleList = 4;          // DI_PT_TRILIST
    private const uint GsOutTriangles = 2;                 // CxGsOutputPrimitiveType::kTriangles
    private const byte Index32Bit = 1;

    private readonly DisplayDevice _display;
    private readonly ShaderBinary _vsBinary, _psBinary;
    private PreparedShader _vs, _ps;
    // One set of command buffer and register/constant memory per frame in flight, so a frame does not
    // overwrite memory a still-running earlier frame is reading. The set rotates with the display's
    // framebuffers.
    private readonly DrawCommandBuffer[] _dcb;
    private readonly DirectMemoryRegion[] _contextState;   // combined Cx registers the GPU loads
    private readonly DirectMemoryRegion[] _shaderState;    // combined Sh registers
    private readonly DirectMemoryRegion[] _constants;      // the two matrices
    private readonly int _maxContext, _maxShader, _framesInFlight;
    private int _slot;
    private bool _disposed;

    /// <summary>
    /// Builds a renderer for a display, using the built-in mesh shaders. The display's framebuffers are
    /// the render targets.
    /// </summary>
    /// <exception cref="Interop.ProsperoException">The graphics device or a shader could not be prepared.</exception>
    public Renderer3D(DisplayDevice display, int commandBufferBytes = 256 * 1024)
    {
        ArgumentNullException.ThrowIfNull(display);
        _display = display;
        AgcDevice.Initialize();

        _vsBinary = BuiltInShaders.MeshVertex();
        _psBinary = BuiltInShaders.MeshPixel();
        _vs = _vsBinary.Prepare();
        _ps = _psBinary.Prepare();

        // The combined register state: the target, the viewport, the write mask, the primitive type, and
        // each shader's own registers. Sized for the block registers plus both shaders' registers.
        _maxContext = CxRenderTarget.RegisterCount + AgcViewport.RegisterCount + 4
                      + _vs.Shader.ContextRegisters.Length + _ps.Shader.ContextRegisters.Length;
        _maxShader = _vs.Shader.ShaderRegisters.Length + _ps.Shader.ShaderRegisters.Length;

        // One set per frame in flight, matching the display's framebuffer count, so recording a frame
        // never touches memory an earlier frame's draw is still reading.
        _framesInFlight = Math.Max(2, _display.BufferCount);
        _dcb = new DrawCommandBuffer[_framesInFlight];
        _contextState = new DirectMemoryRegion[_framesInFlight];
        _shaderState = new DirectMemoryRegion[_framesInFlight];
        _constants = new DirectMemoryRegion[_framesInFlight];
        for (int i = 0; i < _framesInFlight; i++)
        {
            _dcb[i] = DrawCommandBuffer.Allocate((uint)commandBufferBytes);
            _contextState[i] = DirectMemoryRegion.Allocate((nuint)(_maxContext * sizeof(CxRegister)));
            _shaderState[i] = DirectMemoryRegion.Allocate((nuint)(_maxShader * sizeof(CxRegister)));
            _constants[i] = DirectMemoryRegion.Allocate((nuint)sizeof(Constants));
        }
    }

    /// <summary>
    /// Draws a mesh this frame, transformed by <paramref name="mvp"/> (world to clip) with
    /// <paramref name="model"/> used to orient its normals, and presents the frame. Call once per frame.
    /// </summary>
    public void DrawMesh(MeshBuffer mesh, in Matrix4x4 mvp, in Matrix4x4 model)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // The set of graphics-readable memory this frame records into. It rotates with the framebuffers so
        // recording never overwrites memory an earlier frame's draw is still reading.
        DrawCommandBuffer dcbObj = _dcb[_slot];
        DirectMemoryRegion contextRegion = _contextState[_slot];
        DirectMemoryRegion shaderRegion = _shaderState[_slot];
        DirectMemoryRegion constantsRegion = _constants[_slot];

        // The matrices the vertex program reads.
        Constants* constants = (Constants*)constantsRegion.Pointer;
        constants->Mvp = mvp;
        constants->Model = model;

        // The colour target points at the framebuffer being drawn; it is linear to match the display.
        var target = new CxRenderTarget().Init(RegisterDefaults.RenderTargetBlock());
        var spec = new RenderTargetSpec(
            CxRenderTarget.Format.k8_8_8_8, CxRenderTarget.ChannelType.kUNorm, CxRenderTarget.ChannelOrder.kStandard,
            (uint)_display.Width, (uint)_display.Height, (ulong)_display.BackBufferAddress, CxRenderTarget.TileMode.kLinear);
        AgcRenderTargetSetup.Initialize(target, spec);

        var viewport = new AgcViewport();
        viewport.SetViewport(0, 0, _display.Width, _display.Height);

        // Assemble the combined context register state into graphics-readable memory.
        int cx = 0;
        var context = new Span<CxRegister>(contextRegion.Pointer, _maxContext);
        target.Registers.CopyTo(context[cx..]); cx += CxRenderTarget.RegisterCount;
        cx += viewport.WriteTo(context[cx..]);
        context[cx++] = new CxRegister((ushort)TargetMaskOffset, 0xF);            // write all four channels of target 0
        context[cx++] = new CxRegister((ushort)GsOutPrimTypeOffset, GsOutTriangles);
        _vs.Shader.ContextRegisters.CopyTo(context[cx..]); cx += _vs.Shader.ContextRegisters.Length;
        _ps.Shader.ContextRegisters.CopyTo(context[cx..]); cx += _ps.Shader.ContextRegisters.Length;

        int sh = 0;
        var shader = new Span<CxRegister>(shaderRegion.Pointer, _maxShader);
        _vs.Shader.ShaderRegisters.CopyTo(shader[sh..]); sh += _vs.Shader.ShaderRegisters.Length;
        _ps.Shader.ShaderRegisters.CopyTo(shader[sh..]); sh += _ps.Shader.ShaderRegisters.Length;

        // The descriptors the vertex program reads its constants and vertices through.
        AgcBufferDescriptor cbDescriptor = AgcBufferDescriptor.Constant((ulong)constantsRegion.Pointer, (uint)sizeof(Constants));
        AgcBufferDescriptor vbDescriptor = AgcBufferDescriptor.Structured((ulong)mesh.VertexAddress, (uint)MeshBuffer.VertexStride, (uint)mesh.VertexCount);

        void* dcb = dcbObj.Handle;
        dcbObj.Reset();
        SceAgcDriver.sceAgcDriverWaitUntilSafeForRendering(dcb, _display.OutputHandle, (uint)_display.CurrentBufferIndex, (uint)VideoOutFlipModeVSync, 0);

        SceAgc.sceAgcDcbSetCxRegistersIndirect(dcb, contextRegion.Pointer, (uint)cx);
        SceAgc.sceAgcDcbSetShRegistersIndirect(dcb, shaderRegion.Pointer, (uint)sh);
        SceAgc.sceAgcDcbSetUcRegisterDirect(dcb, Pack(PrimitiveTypeOffset, PrimitiveTriangleList));

        BindResource(dcb, ShaderResourceKind.ConstantBuffer, cbDescriptor);
        BindResource(dcb, ShaderResourceKind.ReadOnly, vbDescriptor);

        dcbObj.SetIndexSize(Index32Bit);
        dcbObj.SetIndexBuffer(mesh.IndexAddress);
        dcbObj.SetIndexCount((uint)mesh.IndexCount);
        dcbObj.DrawIndex((uint)mesh.IndexCount, mesh.IndexAddress);

        // Record the flip on the graphics timeline so it runs after the draw completes (not immediately on
        // the processor as a display-side flip would), then pace to the vertical blank and rotate the set.
        SceAgc.sceAgcDcbSetFlip(dcb, (uint)_display.OutputHandle, (uint)_display.CurrentBufferIndex, (uint)VideoOutFlipModeVSync, (ulong)_display.FrameIndex);
        AgcDevice.Submit(dcbObj);
        _display.AdvanceFrame();
        _slot = (_slot + 1) % _framesInFlight;
    }

    // Writes a resource descriptor into the vertex program's user data at the slot the program declares.
    private void BindResource(void* dcb, ShaderResourceKind kind, in AgcBufferDescriptor descriptor)
    {
        if (!_vs.Shader.TryGetResourceSlot(kind, 0, out int dwordOffset, out _))
            return; // the program does not read a resource of this kind
        uint* words = stackalloc uint[4];
        descriptor.WriteTo(new Span<uint>(words, 4));
        SceAgc.sceAgcCbSetShRegisterRangeDirect(dcb, AgcShader.GsUserDataBaseOffset + (uint)dwordOffset, words, 4);
    }

    private static ulong Pack(uint offset, uint value) => offset | ((ulong)value << 32);

    private const uint VideoOutFlipModeVSync = 1;

    /// <summary>Releases the shaders, command buffer, and state memory.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _vs.Dispose();
        _ps.Dispose();
        for (int i = 0; i < _framesInFlight; i++)
        {
            _dcb[i].Dispose();
            _contextState[i].Dispose();
            _shaderState[i].Dispose();
            _constants[i].Dispose();
        }
    }
}
