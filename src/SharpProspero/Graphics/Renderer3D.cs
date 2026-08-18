// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using SharpProspero.Graphics.Agc;
using SharpProspero.Interop;
using SharpProspero.Interop.Agc;
using SharpProspero.Interop.VideoOut;
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
    private readonly Action<string>? _trace;
    private bool _firstDraw = true;
    private int _slot;
    private bool _disposed;

    /// <summary>
    /// Which colour channels of render target 0 the pixel program is allowed to write, as the four-bit
    /// mask <c>CB_TARGET_MASK</c> takes: <c>0xF</c> writes all four and is the default, <c>0</c> writes
    /// none.
    /// </summary>
    /// <remarks>
    /// Setting this to zero keeps the whole pipeline running - vertices are still fetched, transformed and
    /// rasterised, and the pixel program still runs - while nothing reaches the framebuffer. That makes it
    /// the way to ask whether a pipeline that does not complete is failing at the colour target or before
    /// it, which is otherwise hard to separate: both look like a frame that never appears.
    /// </remarks>
    public uint TargetWriteMask { get; set; } = 0xF;

    /// <summary>
    /// Builds a renderer for a display, using the built-in mesh shaders. The display's framebuffers are
    /// the render targets.
    /// </summary>
    /// <param name="display">The display whose framebuffers are drawn into.</param>
    /// <param name="commandBufferBytes">Size of each per-frame command buffer.</param>
    /// <param name="trace">
    /// Optional diagnostic sink. When set, the first <see cref="DrawMesh"/> reports each stage it reaches
    /// as it reaches it. Bringing a renderer up on a new firmware is largely a matter of finding which
    /// stage is the last one to complete, and a stage that never reports is far easier to act on than a
    /// picture that never appears.
    /// </param>
    /// <exception cref="Interop.ProsperoException">The graphics device or a shader could not be prepared.</exception>
    public Renderer3D(DisplayDevice display, int commandBufferBytes = 256 * 1024, Action<string>? trace = null)
    {
        ArgumentNullException.ThrowIfNull(display);
        _display = display;
        _trace = trace;
        AgcDevice.Initialize();

        _vsBinary = BuiltInShaders.MeshVertex();
        _psBinary = BuiltInShaders.MeshPixel();
        _vs = _vsBinary.Prepare();
        _ps = _psBinary.Prepare();
        _interpolants = CxInterpolantMapping.Create(_vs.Shader, _ps.Shader, _trace);

        // The combined register state: the target, the viewport, the write mask, the primitive type,
        // each shader's own registers, the interpolant mapping registers, and 8 user data descriptor registers.
        _maxContext = CxRenderTarget.RegisterCount + AgcViewport.RegisterCount + 4
                      + _vs.Shader.ContextRegisters.Length + _ps.Shader.ContextRegisters.Length
                      + _interpolants.Registers.Length;
        _maxShader = _vs.Shader.ShaderRegisters.Length + _ps.Shader.ShaderRegisters.Length + 8;

        // Ring buffer of frames in flight so recording a frame never touches memory an earlier in-flight frame is still reading.
        _framesInFlight = 8;
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

    private readonly CxInterpolantMapping _interpolants;

    private enum DrawMode
    {
        None,
        Auto,
        Indexed
    }

    /// <summary>
    /// Submits shader and context register state without binding resource descriptors or issuing a draw command,
    /// and presents the frame. Used to isolate register loading from descriptor/draw execution.
    /// </summary>
    public void SubmitShaderStateOnly()
    {
        SubmitPipeline(bindDescriptors: false, drawMode: DrawMode.None, null, Matrix4x4.Identity, Matrix4x4.Identity);
    }

    /// <summary>
    /// Submits shader and context register state and binds resource descriptors (constant and structured vertex buffers)
    /// without issuing a draw command, and presents the frame. Used to isolate descriptor binding from draw execution.
    /// </summary>
    public void SubmitDescriptorsOnly(MeshBuffer mesh, in Matrix4x4 mvp, in Matrix4x4 model)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        SubmitPipeline(bindDescriptors: true, drawMode: DrawMode.None, mesh, mvp, model);
    }

    /// <summary>
    /// Draws a mesh non-indexed using DrawIndexAuto (vertex IDs generated automatically without index buffer DMA),
    /// transformed by <paramref name="mvp"/> and <paramref name="model"/>, and presents the frame.
    /// </summary>
    public void DrawAuto(MeshBuffer mesh, in Matrix4x4 mvp, in Matrix4x4 model)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        SubmitPipeline(bindDescriptors: true, drawMode: DrawMode.Auto, mesh, mvp, model);
    }

    /// <summary>
    /// Draws a mesh this frame, transformed by <paramref name="mvp"/> (world to clip) with
    /// <paramref name="model"/> used to orient its normals, and presents the frame. Call once per frame.
    /// </summary>
    public void DrawMesh(MeshBuffer mesh, in Matrix4x4 mvp, in Matrix4x4 model)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        SubmitPipeline(bindDescriptors: true, drawMode: DrawMode.Auto, mesh, mvp, model);
    }

    private void SubmitPipeline(bool bindDescriptors, DrawMode drawMode, MeshBuffer? mesh, in Matrix4x4 mvp, in Matrix4x4 model)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        bool trace = _trace is not null;

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

        // The colour target points at the framebuffer being drawn.
        var target = new CxRenderTarget().Init(RegisterDefaults.RenderTargetBlock(_firstDraw && trace ? _trace : null));
        var spec = new RenderTargetSpec(
            CxRenderTarget.Format.k8_8_8_8, CxRenderTarget.ChannelType.kUNorm, CxRenderTarget.ChannelOrder.kAlt,
            (uint)_display.Width, (uint)_display.Height, (ulong)_display.BackBufferAddress,
            _display.Tiling == VideoOutTilingMode.Tiled
                ? CxRenderTarget.TileMode.kRenderTarget
                : CxRenderTarget.TileMode.kLinear);
        AgcRenderTargetSetup.Initialize(target, spec);
        if (_firstDraw && trace) _trace?.Invoke("AGC_STAGE_TARGET_OK");

        var viewport = new AgcViewport();
        viewport.SetViewport(0, 0, _display.Width, _display.Height);

        // Assemble the combined context register state into graphics-readable memory.
        int cx = 0;
        var context = new Span<CxRegister>(contextRegion.Pointer, _maxContext);
        target.Registers.CopyTo(context[cx..]); cx += CxRenderTarget.RegisterCount;
        cx += viewport.WriteTo(context[cx..]);
        context[cx++] = new CxRegister((ushort)TargetMaskOffset, TargetWriteMask);
        context[cx++] = new CxRegister((ushort)GsOutPrimTypeOffset, GsOutTriangles);
        context[cx++] = new CxRegister((ushort)PrimitiveTypeOffset, PrimitiveTriangleList);
        _vs.Shader.ContextRegisters.CopyTo(context[cx..]); cx += _vs.Shader.ContextRegisters.Length;
        _ps.Shader.ContextRegisters.CopyTo(context[cx..]); cx += _ps.Shader.ContextRegisters.Length;
        _interpolants.Registers.CopyTo(context[cx..]); cx += _interpolants.Registers.Length;
        context[cx++] = new CxRegister(0x01C2, 0); // PA_SU_SC_MODE_CNTL: Disable face culling so geometry is visible from all angles

        int sh = 0;
        var shader = new Span<CxRegister>(shaderRegion.Pointer, _maxShader);
        _vs.Shader.ShaderRegisters.CopyTo(shader[sh..]); sh += _vs.Shader.ShaderRegisters.Length;
        _ps.Shader.ShaderRegisters.CopyTo(shader[sh..]); sh += _ps.Shader.ShaderRegisters.Length;

        if (bindDescriptors && mesh is not null)
        {
            AgcBufferDescriptor cbDescriptor = AgcBufferDescriptor.Constant((ulong)constantsRegion.Pointer, (uint)sizeof(Constants));
            AgcBufferDescriptor vbDescriptor = AgcBufferDescriptor.Structured((ulong)mesh.VertexAddress, (uint)MeshBuffer.VertexStride, (uint)mesh.VertexCount);

            int cbOff = 0, vbOff = 4;
            if (_vs.Shader.TryGetResourceSlot(ShaderResourceKind.ConstantBuffer, 0, out int cOff, out _)) cbOff = cOff;
            if (_vs.Shader.TryGetResourceSlot(ShaderResourceKind.ReadOnly, 0, out int vOff, out _)) vbOff = vOff;
            if (_firstDraw && trace) _trace?.Invoke("AGC_SLOTS cbOff=" + cbOff + " vbOff=" + vbOff);

            shader[sh++] = new CxRegister((ushort)(AgcShader.GsUserDataBaseOffset + cbOff + 0), cbDescriptor.Word0);
            shader[sh++] = new CxRegister((ushort)(AgcShader.GsUserDataBaseOffset + cbOff + 1), cbDescriptor.Word1);
            shader[sh++] = new CxRegister((ushort)(AgcShader.GsUserDataBaseOffset + cbOff + 2), cbDescriptor.Word2);
            shader[sh++] = new CxRegister((ushort)(AgcShader.GsUserDataBaseOffset + cbOff + 3), cbDescriptor.Word3);

            shader[sh++] = new CxRegister((ushort)(AgcShader.GsUserDataBaseOffset + vbOff + 0), vbDescriptor.Word0);
            shader[sh++] = new CxRegister((ushort)(AgcShader.GsUserDataBaseOffset + vbOff + 1), vbDescriptor.Word1);
            shader[sh++] = new CxRegister((ushort)(AgcShader.GsUserDataBaseOffset + vbOff + 2), vbDescriptor.Word2);
            shader[sh++] = new CxRegister((ushort)(AgcShader.GsUserDataBaseOffset + vbOff + 3), vbDescriptor.Word3);
        }

        if (_firstDraw && trace)
        {
            _trace?.Invoke("AGC_STAGE_STATE_OK cx=" + cx + " sh=" + sh);
            DumpRegisters(context[..cx], shader[..sh]);
        }

        void* dcb = dcbObj.Handle;
        dcbObj.Reset();

        SceAgc.sceAgcDcbSetCxRegistersIndirect(dcb, contextRegion.Pointer, (uint)cx);
        SceAgc.sceAgcDcbSetShRegistersIndirect(dcb, shaderRegion.Pointer, (uint)sh);

        if (drawMode == DrawMode.Auto && mesh is not null)
        {
            dcbObj.DrawIndexAuto((uint)mesh.VertexCount);
        }
        else if (drawMode == DrawMode.Indexed && mesh is not null)
        {
            dcbObj.SetIndexSize(Index32Bit);
            dcbObj.SetIndexBuffer(mesh.IndexAddress);
            dcbObj.DrawIndexOffset(0, (uint)mesh.IndexCount);
        }
        if (_firstDraw && trace) _trace?.Invoke("AGC_STAGE_COMMAND_OK cx=" + cx + " sh=" + sh + " descriptors=" + bindDescriptors + " draw=" + drawMode);

        // Record the flip on the graphics timeline
        SceAgc.sceAgcDcbSetFlip(dcb, (uint)_display.OutputHandle, _display.CurrentBufferIndex, VideoOutFlipModeVSync, (long)_display.FrameIndex);
        if (_firstDraw && trace) _trace?.Invoke("AGC_STAGE_SUBMIT_BEGIN");
        AgcDevice.Submit(dcbObj);
        if (_firstDraw && trace) _trace?.Invoke("AGC_STAGE_SUBMIT_OK");

        if (_firstDraw && trace) _trace?.Invoke("AGC_STAGE_SUSPEND_BEGIN");
        int suspendResult = AgcDevice.SuspendPoint();
        if (_firstDraw && trace) _trace?.Invoke("AGC_STAGE_SUSPEND_RETURN result=0x" + suspendResult.ToString("X8"));
        SceResult.ThrowIfFailed(suspendResult, nameof(AgcDevice.SuspendPoint));
        if (_firstDraw && trace) _trace?.Invoke("AGC_STAGE_SUSPEND_OK");

        if (_firstDraw && trace) _trace?.Invoke("AGC_STAGE_FLIP_BEGIN frame=" + _display.FrameIndex);
        _display.AdvanceFrame();
        if (_firstDraw && trace) _trace?.Invoke("AGC_STAGE_FLIP_OK");
        _firstDraw = false;
        _slot = (_slot + 1) % _framesInFlight;
    }

    // Reports the assembled register state, one line per register, before it is handed to the processor.
    // A count of how many registers were written says nothing about what is in them, and the state is the
    // first thing to suspect when a pipeline that builds cleanly does not complete: a register the driver
    // listed no reset value for arrives here as a zero that is indistinguishable from a deliberate one.
    // Only the first draw reports, so this costs one burst of lines at start-up rather than a flood.
    private void DumpRegisters(ReadOnlySpan<CxRegister> context, ReadOnlySpan<CxRegister> shader)
    {
        if (_trace is null) return;
        for (int i = 0; i < context.Length; i++)
            _trace("AGC_REG_CX index=" + i +
                   " offset=0x" + context[i].Offset.ToString("X4") +
                   " value=0x" + context[i].Value.ToString("X8"));
        for (int i = 0; i < shader.Length; i++)
            _trace("AGC_REG_SH index=" + i +
                   " offset=0x" + shader[i].Offset.ToString("X4") +
                   " value=0x" + shader[i].Value.ToString("X8"));
        _trace("AGC_REG_DUMP_OK cx=" + context.Length + " sh=" + shader.Length +
               " target_write_mask=0x" + TargetWriteMask.ToString("X"));
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
