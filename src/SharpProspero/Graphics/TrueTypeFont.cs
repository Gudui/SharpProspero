// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop;
using SharpProspero.Interop.Font;
using System;
using System.Runtime.InteropServices;

namespace SharpProspero.Graphics;

/// <summary>
/// A scalable TrueType or OpenType font, loaded from its file bytes, that draws antialiased text onto a
/// drawing surface in any color. It is the higher-quality alternative to the built-in bitmap font. Load
/// it once, set the pixel size, draw as often as needed, and dispose it. Load the font modules
/// (<c>SystemModule.Load(SystemModuleId.Font)</c> and <c>SystemModuleId.FontFt</c>) before loading a font.
/// </summary>
/// <remarks>
/// Each glyph is rendered to an 8-bit coverage image and composited onto the surface in the requested
/// color, so text blends smoothly over whatever is already drawn. The origin passed to
/// <see cref="DrawText"/> is the left end of the text baseline. Only characters in the Basic Multilingual
/// Plane are drawn.
/// </remarks>
public sealed unsafe class TrueTypeFont : IDisposable
{
    private readonly void* _memoryBlock;
    private readonly SceFontMemory* _memory;
    private readonly void* _fontData;
    private void* _library;
    private void* _renderer;
    private void* _fontHandle;

    // A scratch buffer the renderer draws each glyph into; its output is discarded, since the coverage
    // image the render returns is what gets composited.
    private uint* _scratch;
    private int _scratchDim;

    // A ceiling on the render size, so the scratch dimension and its allocation cannot overflow. It is
    // far above any on-screen text size.
    private const float MaxPixelSize = 1024f;

    private float _pixelSize;
    private bool _disposed;

    private TrueTypeFont(void* memoryBlock, SceFontMemory* memory, void* fontData)
    {
        _memoryBlock = memoryBlock;
        _memory = memory;
        _fontData = fontData;
    }

    /// <summary>The pixel size the font renders at.</summary>
    public float PixelSize
    {
        get => _pixelSize;
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            float size = Math.Clamp(value, 1f, MaxPixelSize);
            SceResult.ThrowIfFailed(
                SceFont.sceFontSetupRenderScalePixel(_fontHandle, size, size),
                nameof(SceFont.sceFontSetupRenderScalePixel));
            _pixelSize = size;
            EnsureScratch();
        }
    }

    /// <summary>
    /// Loads a font from the bytes of a <c>.ttf</c> or <c>.otf</c> file and sets its pixel size.
    /// </summary>
    /// <param name="fontFile">The font file bytes. A copy is kept for the font's lifetime.</param>
    /// <param name="pixelSize">The size to render at, in pixels.</param>
    /// <param name="memoryBudgetBytes">The block the engine manages; the default suits a UI font.</param>
    /// <exception cref="ProsperoException">The engine or the font could not be set up.</exception>
    public static TrueTypeFont Load(ReadOnlySpan<byte> fontFile, float pixelSize = 24f, int memoryBudgetBytes = 4 * 1024 * 1024)
    {
        if (fontFile.IsEmpty)
            throw new ArgumentException("The font file is empty.", nameof(fontFile));
        ArgumentOutOfRangeException.ThrowIfLessThan(memoryBudgetBytes, 256 * 1024);

        // Allocate the three native buffers up front. The block is allocated first; if either of the
        // others fails, everything allocated so far is freed here, since the object that would own them
        // in its Dispose has not been created yet.
        void* block = NativeMemory.Alloc((nuint)memoryBudgetBytes);
        SceFontMemory* memory = null;
        void* fontData = null;
        try
        {
            memory = (SceFontMemory*)NativeMemory.AllocZeroed((nuint)sizeof(SceFontMemory));
            fontData = NativeMemory.Alloc((nuint)fontFile.Length);
        }
        catch
        {
            if (fontData != null)
                NativeMemory.Free(fontData);
            if (memory != null)
                NativeMemory.Free(memory);
            NativeMemory.Free(block);
            throw;
        }
        fontFile.CopyTo(new Span<byte>(fontData, fontFile.Length));

        var font = new TrueTypeFont(block, memory, fontData);
        try
        {
            SceResult.ThrowIfFailed(
                SceFont.sceFontMemoryInit(memory, block, (uint)memoryBudgetBytes, null, null, null, null),
                nameof(SceFont.sceFontMemoryInit));

            void* rendererSelection = SceFontFt.sceFontSelectRendererFt(0);
            void* renderer;
            SceResult.ThrowIfFailed(
                SceFont.sceFontCreateRendererWithEdition(memory, rendererSelection, SceFont.Edition, &renderer),
                nameof(SceFont.sceFontCreateRendererWithEdition));
            font._renderer = renderer;

            void* librarySelection = SceFontFt.sceFontSelectLibraryFt(0);
            void* library;
            SceResult.ThrowIfFailed(
                SceFont.sceFontCreateLibraryWithEdition(memory, librarySelection, SceFont.Edition, &library),
                nameof(SceFont.sceFontCreateLibraryWithEdition));
            font._library = library;

            SceResult.ThrowIfFailed(
                SceFont.sceFontSupportExternalFonts(library, 4,
                    SceFont.FormatOpenType | SceFont.FormatOpenTypeTt | SceFont.FormatOpenTypeCff),
                nameof(SceFont.sceFontSupportExternalFonts));

            var detail = new SceFontOpenDetail { DetailId = 0x0FD2, UniqueId = -1 };
            void* handle;
            SceResult.ThrowIfFailed(
                SceFont.sceFontOpenFontMemory(library, fontData, (uint)fontFile.Length, &detail, &handle),
                nameof(SceFont.sceFontOpenFontMemory));
            font._fontHandle = handle;

            SceResult.ThrowIfFailed(SceFont.sceFontBindRenderer(handle, renderer), nameof(SceFont.sceFontBindRenderer));

            font.PixelSize = pixelSize;
            return font;
        }
        catch
        {
            font.Dispose();
            throw;
        }
    }

    /// <summary>The width, in pixels, that <paramref name="text"/> occupies at the current size.</summary>
    /// <exception cref="ProsperoException">A glyph's metrics could not be read.</exception>
    public int MeasureText(ReadOnlySpan<char> text)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        float pen = 0f;
        foreach (char c in text)
        {
            SceFontGlyphMetrics metrics = default;
            if (SceResult.Succeeded(SceFont.sceFontGetRenderCharGlyphMetrics(_fontHandle, c, &metrics)))
                pen += metrics.HorizontalAdvance;
        }
        return (int)(pen + 0.5f);
    }

    /// <summary>
    /// Draws <paramref name="text"/> onto <paramref name="surface"/> in <paramref name="color"/>, with
    /// (<paramref name="x"/>, <paramref name="y"/>) at the left end of the text baseline.
    /// </summary>
    /// <exception cref="ProsperoException">A glyph could not be rendered.</exception>
    public void DrawText(Surface surface, ReadOnlySpan<char> text, int x, int y, Color color)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        float penX = x;
        foreach (char c in text)
        {
            SceFontGlyphMetrics metrics = default;
            SceFontRenderResult result = default;
            SceFontRenderSurface renderSurface = default;
            SceFont.sceFontRenderSurfaceInit(&renderSurface, _scratch, _scratchDim * 4, 4, _scratchDim, _scratchDim);
            SceFont.sceFontRenderSurfaceSetScissor(&renderSurface, 0, 0, (uint)_scratchDim, (uint)_scratchDim);

            // The pen sits inside the scratch surface with room above the baseline for the ascent; the
            // coverage image the render returns is what is composited, so the scratch draw is discarded.
            int rc = SceFont.sceFontRenderCharGlyphImageHorizontal(
                _fontHandle, c, &renderSurface, _scratchDim * 0.25f, _scratchDim * 0.75f, &metrics, &result);
            if (SceResult.Failed(rc))
                continue;

            if (result.TransImage != null && result.TransImage->Address != null)
                CompositeCoverage(surface, result, color, (int)(penX + 0.5f), y);

            penX += result.ImageMetrics.Advance != 0f ? result.ImageMetrics.Advance : metrics.HorizontalAdvance;
        }
    }

    // Blends one glyph's 8-bit coverage over the surface in the chosen color, placed relative to the
    // pen and baseline by the glyph's bearings.
    private static void CompositeCoverage(Surface surface, SceFontRenderResult result, Color color, int penX, int baselineY)
    {
        SceFontTransImage* image = result.TransImage;
        int width = (int)image->ImageWidth;
        int height = (int)image->ImageHeight;
        if (width <= 0 || height <= 0)
            return;

        int pitch = (int)image->WidthByte;
        byte* coverage = image->Address;
        int left = penX + (int)(result.ImageMetrics.BearingX + 0.5f);
        int top = baselineY - (int)(result.ImageMetrics.BearingY + 0.5f);
        uint rgb = color.Value & 0x00FFFFFFu;

        for (int row = 0; row < height; row++)
        {
            int py = top + row;
            if ((uint)py >= (uint)surface.Height)
                continue;
            byte* line = coverage + (long)row * pitch;
            for (int col = 0; col < width; col++)
            {
                byte alpha = line[col];
                if (alpha == 0)
                    continue;
                surface.BlendPixel(left + col, py, rgb, alpha);
            }
        }
    }

    // Sizes the scratch render buffer to hold a glyph at the current pixel size, with room for the
    // ascent and any overhang.
    private void EnsureScratch()
    {
        // The pixel size is clamped to at most MaxPixelSize, so this stays positive and cannot overflow.
        int dim = (int)(_pixelSize * 3f + 0.5f) + 8;
        if (dim <= _scratchDim && _scratch != null)
            return;
        // Allocate the new buffer before freeing the old one, so a failed allocation leaves the current
        // buffer intact rather than dangling.
        var buffer = (uint*)NativeMemory.Alloc((nuint)((long)dim * dim * 4));
        if (_scratch != null)
            NativeMemory.Free(_scratch);
        _scratch = buffer;
        _scratchDim = dim;
    }

    /// <summary>Releases the font, the engine objects and their memory.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        if (_fontHandle != null)
        {
            SceFont.sceFontUnbindRenderer(_fontHandle);
            SceFont.sceFontCloseFont(_fontHandle);
            _fontHandle = null;
        }
        if (_library != null)
        {
            void* library = _library;
            SceFont.sceFontDestroyLibrary(&library);
            _library = null;
        }
        if (_renderer != null)
        {
            void* renderer = _renderer;
            SceFont.sceFontDestroyRenderer(&renderer);
            _renderer = null;
        }
        SceFont.sceFontMemoryTerm(_memory);

        if (_scratch != null)
            NativeMemory.Free(_scratch);
        NativeMemory.Free(_fontData);
        NativeMemory.Free(_memory);
        NativeMemory.Free(_memoryBlock);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases the resources if the font was dropped without a <see cref="Dispose"/> call.</summary>
    ~TrueTypeFont() => Dispose();
}
