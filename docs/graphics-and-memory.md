---
title: Graphics and memory
nav_order: 7
---

# Graphics and memory

## The display

`DisplayDevice` opens the main output, allocates its framebuffers from direct memory, registers them,
and presents frames on the vertical blank.

```csharp
using var display = DisplayDevice.Open(width: 1920, height: 1080, bufferCount: 2);
while (running)
{
    Surface surface = display.BackBuffer;
    surface.Clear(Color.Black);
    surface.DrawTextCentered("Frame", 500, 6, Color.White);
    display.Present();
}
```

`BackBuffer` is the framebuffer to draw the next frame into. `Present` submits it, waits for the
vertical blank, and advances to the next framebuffer. The default is a two-buffer swap chain; pass a
higher `bufferCount` for triple buffering. The buffers are B8-G8-R8-A8 sRGB in linear layout, which
is the format the CPU renderer writes. The width must be a multiple of 64 so the row pitch matches
the allocated framebuffer; the standard 1920 and 1280 widths already are.

When an application uses `ProsperoApp`, the host owns the display and hands `BackBuffer` to each
frame through `FrameContext.Surface`; you do not open the display yourself.

## The surface

`Surface` is a lightweight view over a framebuffer. It holds a pointer and the geometry and allocates
nothing. Operations clip to the surface bounds.

| Method | Effect |
|---|---|
| `Clear(color)` | Fill the whole surface. |
| `SetPixel(x, y, color)` | Set one pixel; out-of-bounds is ignored. |
| `FillRect(x, y, w, h, color)` | Fill a rectangle, clipped. |
| `HLine(x, y, length, color)` | Draw a horizontal run, clipped. |
| `VLine(x, y, length, color)` | Draw a vertical run, clipped. |
| `DrawLine(x0, y0, x1, y1, color)` | Draw a one-pixel line between two points, clipped. |
| `DrawRect(x, y, w, h, color)` | Draw a one-pixel rectangle outline, clipped. |
| `FillCircle(cx, cy, radius, color)` | Fill a disc, clipped. |
| `DrawCircle(cx, cy, radius, color)` | Draw a one-pixel circle outline, clipped. |
| `Blit(source, x, y)` | Copy another surface onto this one, clipped. |
| `BlitBlended(source, x, y)` | Copy another surface, blending each pixel over the destination by its alpha. |
| `DrawGlyph(c, x, y, scale, color)` | Draw one glyph scaled by an integer factor. |
| `DrawText(text, x, y, scale, color)` | Draw a string left to right. |
| `DrawTextCentered(text, y, scale, color)` | Draw a string centered horizontally. |
| `DrawTextOutlined(text, x, y, scale, fill, outline)` | Draw a string with a one-pixel outline, so it stays readable over a photo or a video frame. |
| `MeasureText(text, scale)` | Width in pixels the string occupies. |

Fills use a single-pass span write per row, so `Clear` and `FillRect` are as fast as the memory
allows. When a framebuffer's row pitch is wider than the drawn width, construct the surface with a
`stride` (in pixels) so addressing skips the padding while drawing still clips to the width.

### More drawing

| Method | What it does |
|---|---|
| `Region(x, y, w, h)` | A view over a sub-rectangle; drawing on it clips to the region, so it acts as a clip rectangle or a panel to draw inside. |
| `FillVerticalGradient(x, y, w, h, top, bottom)` | Fill a rectangle with a top-to-bottom color gradient. |
| `FillHorizontalGradient(x, y, w, h, left, right)` | Fill a rectangle with a left-to-right color gradient. |
| `FillRadialGradient(x, y, w, h, center, edge)` | Fill a rectangle with a gradient from the middle out to the corners, for a soft background or a vignette. |
| `FillRoundedRect(x, y, w, h, radius, color)` | Fill a rectangle with rounded corners. |
| `DrawRoundedRect(x, y, w, h, radius, color)` | Draw a rounded-rectangle outline. |
| `DrawLine(x0, y0, x1, y1, color, thickness)` | Draw a line of a given pixel thickness. |
| `FillTriangle(x0, y0, x1, y1, x2, y2, color)` | Fill a triangle. |
| `FillPolygon(points, color)` | Fill a simple polygon (even-odd rule). |
| `BlitScaled(source, x, y, w, h)` | Copy a surface scaled to a destination rectangle. |
| `BlitScaledBlended(source, x, y, w, h)` | Scaled copy, blending by source alpha. |
| `BlitScaledSmooth(source, x, y, w, h)` | Scaled copy with bilinear sampling, so an enlarged photo stays smooth rather than blocky. |
| `BlitRotated(source, centerX, centerY, angleRadians)` | Copy a surface rotated about its center, blended. |
| `DrawRectThick(x, y, w, h, thickness, color)` | Draw a rectangle outline of a given thickness, drawn inside the rectangle. |
| `FillEllipse(cx, cy, radiusX, radiusY, color)` | Fill an ellipse. |
| `DrawEllipse(cx, cy, radiusX, radiusY, color)` | Draw a one-pixel ellipse outline. |
| `DrawTriangle(x0, y0, x1, y1, x2, y2, color)` | Draw a triangle outline. |
| `DrawPolyline(points, color, thickness)` | Draw a run of connected line segments; the run is open, so the ends are not joined. |
| `FillArcRing(cx, cy, innerRadius, outerRadius, start, sweep, color)` | Fill a slice of a ring, measured clockwise from the positive x-axis; a full turn is a complete ring. |
| `FillPie(cx, cy, radius, start, sweep, color)` | Fill a pie slice (a sector of a disc). |
| `DrawArc(cx, cy, radius, start, sweep, color)` | Draw a one-pixel arc; a full turn is a complete circle. |
| `DrawCircleThick(cx, cy, radius, thickness, color)` | Draw a ring: a circle outline of a given thickness, drawn inwards. |
| `FillRectBlended(x, y, w, h, color)` | Fill a rectangle, blending by the color's alpha (a translucent panel); opaque colors fill directly. |
| `FillCircleBlended(cx, cy, radius, color)` | Fill a disc, blending by the color's alpha (a soft dot or glow). |
| `BlitNineSlice(source, x, y, w, h, border)` | Draw a panel image at any size: corners keep their size, edges stretch along their run, the middle stretches both ways. |

### Text that fits

`TextLayout` fits text to a width: breaking a paragraph into lines, shortening a label that will not
fit, and drawing either one aligned. It measures through an `ITextFont`, so the same layout serves the
built-in text (`BitmapTextFont`) and a loaded outline font (`TrueTypeFont` implements it too).

| Call | What it does |
|---|---|
| `Wrap(font, text, maxWidth)` | Break into lines no wider than the width, splitting at spaces. A line break starts a new line; a word too wide is split rather than overflowing. |
| `MeasureWrapped(font, text, maxWidth)` | The widest line and the total height of the wrapped block. |
| `DrawWrapped(surface, font, text, x, y, width, color, alignment)` | Draw the wrapped block; returns the height it used. |
| `DrawAligned(surface, font, text, x, y, width, color, alignment)` | Draw one line placed left, centred or right within a width. |
| `Truncate(font, text, maxWidth, ellipsis)` | Shorten to fit, ending with the marker when anything was dropped. |

```csharp
ITextFont font = new BitmapTextFont(2);
int used = TextLayout.DrawWrapped(surface, font, description, 40, 100, 600, Color.White);
string name = TextLayout.Truncate(font, longFileName, columnWidth);
```

`Region` composes: build a themed panel by taking a sub-region and drawing into it with its own local
coordinates. Gradients and rounded rectangles give buttons and panels a finished look without an image,
and the scaled and rotated copies place thumbnails and sprites.

### Sprite sheets

`SpriteSheet` reads one image as a grid of equal-sized frames — a character's animation, a set of
icons, a tile set — and draws any frame as a sprite. It reads frames as views onto the shared image
and copies nothing, so it allocates nothing of its own. Pair it with a `Tween` or a frame counter to
animate.

```csharp
using var strip = PngImage.Decode(File.ReadAllBytes("/app0/run.png"));
var sheet = new SpriteSheet(strip.AsSurface(), frameWidth: 48, frameHeight: 48);

int frame = (int)(time * 12) % sheet.Count;     // 12 frames a second
sheet.Draw(display.BackBuffer, frame, x, y);    // blended, so a transparent background composites
```

| Member | What it is |
|---|---|
| `Columns`, `Rows`, `Count` | How the frames are arranged and how many there are. |
| `Frame(index)` | A view onto one frame (numbered left to right, then down), to draw however you like. |
| `Draw(dest, index, x, y)` | Draw a frame as a sprite, blended by alpha. |
| `DrawScaled(dest, index, x, y, w, h)` | Draw a frame scaled into a rectangle. |

`AnimatedSprite` plays a run of a sheet's frames over time, so you do not track the frame yourself.
Point it at all of a sheet or a range within one — several animations can share a sheet — set the rate,
advance it each frame, and draw it.

```csharp
var run = new AnimatedSprite(sheet, framesPerSecond: 12, firstFrame: 0, frameCount: 8);
// each frame:
run.Update((float)context.DeltaSeconds);
run.Draw(context.Surface, x, y);
```

The mode is `Loop` (the default), `Once` (stops on the last frame and reports `IsComplete`) or
`PingPong` (runs out and back). `CurrentFrame` is the sheet frame showing, and `Reset` returns to the
start.

### Image effects

A surface also transforms in place, over its own pixels, so the same calls apply to a decoded image, an
off-screen surface, or the back buffer. Each keeps the alpha channel.

| Method | What it does |
|---|---|
| `Invert()` | Invert the colours (a photo negative). |
| `ToGrayscale()` | Convert to grey by perceived luminance. |
| `AdjustBrightness(delta)` | Add to every channel (negative darkens), clamped. |
| `AdjustContrast(factor)` | Scale contrast around mid-grey (1 leaves it, above 1 raises it). |
| `Tint(color, amount)` | Blend towards a colour for a wash. |
| `FlipHorizontal()` / `FlipVertical()` | Mirror left-to-right or top-to-bottom. |
| `BoxBlur(radius)` | Blur with a box filter of the given radius. |

```csharp
using var photo = BmpImage.Load("/data/photo.bmp");
Surface s = photo.AsSurface();
s.AdjustBrightness(20);
s.BoxBlur(2);
display.BackBuffer.Blit(s, 0, 0);
```

## Images

`PngImage` decodes a PNG into B8-G8-R8-A8 pixels — the same layout the display surface uses — so a
decoded image blits straight onto a framebuffer. Load the decode module first.

```csharp
using var pngDec = SystemModule.Load(SystemModuleId.PngDec);
byte[] bytes = PackageFile.ReadAllBytes("/app0/assets/logo.png");
using var logo = PngImage.Decode(bytes);
display.BackBuffer.Blit(logo.AsSurface(), x: 100, y: 100);
```

`Decode` parses the header, sizes and creates a decoder, decodes into a fresh buffer, and releases the
decoder. `AsSurface` views the decoded pixels for drawing; disposing the image frees them.
`JpegImage` decodes JPEG the same way (load `SystemModuleId.JpegDec`). A decoded image carries its
alpha channel, so `BlitBlended` draws it over the background as a sprite while `Blit` copies it
opaquely.

`BmpImage` reads BMP, and `BmpEncoder` writes it, with no system module — the format is uncompressed,
so the SDK handles it on its own. It is a dependable interchange format for a file browser or an editor,
and a fallback when no decode module is loaded.

```csharp
using var picture = BmpImage.Load("/data/picture.bmp");   // 24- or 32-bit BMP
display.BackBuffer.Blit(picture.AsSurface(), 0, 0);

BmpEncoder.Save(display.BackBuffer, "/data/shot.bmp");     // export a 24-bit BMP
```

`TgaImage` and `TgaEncoder` read and write TGA, a simple lossless format editors and asset pipelines
export. Unlike BMP it keeps a proper alpha channel and reads run-length-compressed files, with no module.

`GifImage` decodes GIF — static or animated — with no system module, so an interface can show an animated
icon or a spinner. It handles the common forms (GIF87a and GIF89a, global and local colour tables,
interlacing, transparency, and the frame-disposal methods), returning fully-composed frames each with the
delay to show it, so an animation is just the frames drawn in order.

```csharp
using var gif = GifImage.Decode(PackageFile.ReadAllBytes("/app0/assets/spinner.gif"));
GifFrame frame = gif.Frames[frameIndex % gif.Frames.Count];
display.BackBuffer.BlitBlended(frame.AsSurface(), x, y);
```

Each `GifFrame` exposes `AsSurface` and `DelayMilliseconds`; the composed pixels carry transparency as an
alpha of zero, so `BlitBlended` overlays a frame while `Blit` copies it opaquely. A still GIF is one frame.

```csharp
using var texture = TgaImage.Load("/app0/texture.tga");     // 24- or 32-bit, plain or RLE
display.BackBuffer.BlitBlended(texture.AsSurface(), x, y);

TgaEncoder.Save(display.BackBuffer, "/data/shot.tga");      // export 32-bit BGRA
```

### Off-screen buffers

`PixelBuffer` is a drawing surface of its own, off the screen: its pixels live in memory it owns, in the
same layout as the display. Draw into it as you would the back buffer, then blit it onto the screen. Use
it to build an image once and draw it many times (a pre-rendered sprite, a cached panel), to compose a
picture before showing it, or to build something to encode to PNG or JPEG. It starts fully transparent,
so a frame drawn onto it with alpha composites cleanly.

```csharp
using var cache = new PixelBuffer(256, 64);
Surface s = cache.AsSurface();
s.FillRoundedRect(0, 0, 256, 64, 12, theme.Panel);
s.DrawText("Ready", 16, 20, 3, Color.White);

// later, every frame — no redrawing the panel:
display.BackBuffer.BlitBlended(cache.AsSurface(), x, y);
```

Its `AsSurface` view is valid only while the buffer is alive, so dispose it when you are done with it.

## A 2D scene: camera, tiles and particles

For a scrolling game or map, three pieces work together. `Camera2D` is a movable, zoomable view: game
logic stays in world coordinates and the camera converts them to the screen. `TileMap` is a grid of
tiles drawn from a sprite sheet through the camera, drawing only what is in view. `ParticleSystem` throws
and animates many small particles for an effect.

```csharp
var camera = new Camera2D(surface.Width, surface.Height);
var level = TileMap.FromCsv(PackageFile.ReadAllText("/app0/level.csv"), tileWidth: 32, tileHeight: 32);
var sparks = new ParticleSystem(512) { Gravity = new Vector2(0, 500) };

// each frame:
camera.MoveTo(player.Position);
camera.ClampToBounds(level.WorldBounds);       // do not scroll past the edges of the map

surface.Clear(sky);
level.Draw(surface, tileSheet, camera);
sparks.Update(dt);
sparks.Draw(surface, camera);

// collision against the level, and an effect on a hit:
bool blocked = level.Collides(player.NextBounds, tile => tile >= 16);   // tiles 16+ are solid
if (hit) sparks.Emit(hit.Position, 40, EmitParams.Burst(Color.FromRgb(255, 200, 80)));
```

`Camera2D` gives `WorldToScreen`/`ScreenToWorld`, `VisibleWorldBounds` (for culling), `Move`/`MoveTo`
and `ClampToBounds`. `TileMap` stores a frame index per cell (or `TileMap.Empty`), with `GetTile`,
`SetTile`, `TileBounds`, `WorldToTile`, `Collides` and `FromCsv`. `ParticleSystem` keeps a fixed pool,
so it allocates nothing after it is created; shape a burst with `EmitParams` or start from
`EmitParams.Burst`.

`GridPathfinder` in `SharpProspero.Ai` finds the shortest path across a grid with A*, for an enemy that
routes around walls or a cursor that steps to a target. It works with any grid through a walkable test,
so it reads a `TileMap` directly. Reuse one instance — it keeps its working buffers.

```csharp
var finder = new GridPathfinder(level.Columns, level.Rows) { AllowDiagonal = true };
var path = finder.FindPath((enemyCol, enemyRow), (playerCol, playerRow),
    (col, row) => level.GetTile(col, row) < 16);   // tiles below 16 are floor
```

The path runs from the start cell to the goal cell, or is empty when there is no way through. With
`AllowDiagonal`, a diagonal step is taken only when both cells beside it are open, so a path never cuts
through a wall corner.

## Color

`Color` packs a pixel for the display format. In memory the bytes run blue, green, red, alpha. The
`A`, `R`, `G` and `B` properties read the channels back.

```csharp
Color background = Color.FromRgb(0x0E, 0x11, 0x16);
Color translucent = Color.FromArgb(0x80, 0xFF, 0x00, 0x00);
Color midway = Color.Lerp(Color.Black, Color.White, 0.5f);   // blend between two colors
Color rainbow = Color.FromHsv(context.TotalSeconds * 60 % 360, 1f, 1f);   // hue over time
```

`Lerp` blends component-wise with the factor clamped to 0-1. `FromHsv` builds an opaque color from a
hue in degrees (wrapped) and a saturation and value in 0-1, and `ToHsv` reads them back for a color
picker or a hue shift. `Darken` and `Lighten` move a color toward black or white while keeping its
alpha, for a pressed or hovered shade. `WithAlpha` keeps the red, green and blue and sets a new alpha,
which `BlitBlended` then composites. `Black`, `White`, `Red`, `Green`, `Blue` and `Transparent` are
ready to use.

Where the surface's gradient fills blend two colors, a `Gradient` holds as many stops as you like and
returns the color at any point along it — a heat ramp, a UI theme, a spectrum. A `Palette` is a fixed
set of colors addressed by index, which a gradient can fill by even sampling.

```csharp
Color hot = Gradient.Heat.Sample(load);            // 0..1 along black-red-yellow-white
var ramp = Gradient.Rainbow.ToPalette(16);         // 16 evenly sampled colors
Color series = ramp.Cycle(seriesIndex);            // wraps for a color-per-series scheme
```

`Gradient` sorts its stops, clamps the sample to 0-1, and blends the surrounding stops; `TwoColor` makes
a simple start-to-end ramp. `Palette` offers an index (bounds-checked), `Cycle` (wraps any index), and
`Sample` (maps 0-1 to the nearest entry).

## The font

`BitmapFont` carries an 8x8 monospaced font for printable ASCII (0x20-0x7F) as read-only data.
`GetGlyph(c)` returns the eight rows for a character; anything outside the range maps to the blank
space glyph. Bit 0 of a row is the leftmost column. Scale glyphs with an integer factor when drawing.

## Scalable text

For smooth text at any size, `TrueTypeFont` loads a `.ttf` or `.otf` file and renders antialiased
glyphs in any color. Load the font modules first, load a font from its bytes, set the pixel size, and
draw. `(x, y)` is the left end of the text baseline. Dispose the font when done.

```csharp
using var module = SystemModule.Load(SystemModuleId.Font);
using var backend = SystemModule.Load(SystemModuleId.FontFt);

byte[] ttf = FileSystem.ReadAllBytes("/app0/assets/font.otf");
using var font = TrueTypeFont.Load(ttf, pixelSize: 32);

font.DrawText(surface, "Hello, world", 100, 200, Color.White);
int width = font.MeasureText("Hello, world");
```

## Direct memory

GPU-visible buffers come from direct memory, not the managed heap. `DirectMemoryRegion` reserves,
maps and releases a region in one disposable object.

```csharp
using var region = DirectMemoryRegion.Allocate(bytes: 8u * 1024 * 1024);
Surface surface = region.AsSurface(1920, 1080);
```

`Allocate` rounds the size up to the alignment (2 MiB by default), reserves cached memory shared
between the CPU and GPU, and maps it CPU-readable, CPU-writable and GPU-readable. Override the type,
protection and alignment for other uses. `Dispose` releases the reservation; the region is released
once and is safe to dispose more than once.

## Flexible memory

Working buffers that the GPU does not read come from flexible memory instead. It is drawn from a pool
the system manages, so it needs no physical reservation. `FlexibleMemoryRegion` maps and releases it in
one disposable object, and `Protect` changes the protection later.

```csharp
using var region = FlexibleMemoryRegion.Allocate(bytes: 1u * 1024 * 1024);
// region.Pointer is CPU read-write by default; region.Protect(...) to change it.
```

`SystemMemory` reports how much is left, so a build that streams or grows a cache can check an
allocation fits before it attempts it:

```csharp
nuint flexible = SystemMemory.AvailableFlexibleBytes();
nuint largestDirect = SystemMemory.LargestFreeDirectBytes();
```

## GPU texture and sampler descriptors

To sample a texture from a shader, describe it to the graphics processor. `AgcTextureDescriptor` builds
the eight-word image descriptor (base address, format, size, channel order, mip and array ranges, tiling)
and `AgcSamplerDescriptor` the four-word sampler (address modes, filters, level-of-detail range, border
color); write each with `WriteTo` into GPU-readable memory and point a shader slot at it.
`AgcViewport` maps clip space onto a pixel rectangle and sets the scissor, emitting the context-register
writes to record before a draw. These are advanced building blocks for shader-based rendering; the 2D
`Surface` path above needs none of them.

## The managed heap

The application runs with a small, non-concurrent collector and a hard ceiling baked into the image.
Set the ceiling per project with `ProsperoHeapHardLimitBytes`. Because memory maps are limited, keep
per-frame allocation flat:

- Draw into pre-allocated framebuffers rather than new buffers.
- Reuse arrays and objects across frames; the frame context is already reused for you.
- Prefer `stackalloc` and pointers for short-lived unmanaged buffers, as the bindings do.

`HeapMonitor` reads usage so a loop can react before it reaches the ceiling:

```csharp
if (HeapMonitor.ExceedsBudget(0.85))
    HeapMonitor.Collect();
```

`Capture` returns a `HeapSnapshot` with the committed heap size, total allocated bytes, the ceiling
and the collection count, plus a `Pressure` ratio. Run `Collect` sparingly, for example after loading
a scene rather than every frame.

When a hot loop needs a steady supply of short-lived objects — scratch lists, particles, projectiles —
an `ObjectPool<T>` reuses them instead of allocating each time, which keeps collection pressure down.
Borrow with `Rent`, give back with `Return`; a returned object is kept up to a retained limit and dropped
past it, so a burst does not grow the pool without bound.

```csharp
using SharpProspero.Memory;

var scratch = new ObjectPool<List<int>>(() => new List<int>(), onReturn: l => l.Clear());
List<int> work = scratch.Rent();
// ... use work ...
scratch.Return(work);
```

Pass `onRent` to prepare an object as it goes out and `onReturn` to reset it as it comes back, `prewarm`
to make some up front, and `maxRetained` to cap how many idle objects the pool keeps. Return each borrowed
object once.

## Timing

`GameClock` is a monotonic clock in the `SharpProspero.Timing` namespace. Construct one to measure
from a fixed origin, or use its static members for one-off readings and sleeping.

```csharp
var clock = new GameClock();
// ... work ...
double seconds = clock.ElapsedSeconds;
GameClock.Sleep(TimeSpan.FromMilliseconds(2));
```

`ElapsedMicroseconds` and `ElapsedSeconds` count from construction; `Restart` moves the origin to now.
`GameClock.ProcessMicroseconds` reads the microseconds since the process started. The readings never
move backward.

For the calendar date and time, `SystemClock` reads the real-time clock and returns a `DateTime`:

```csharp
DateTime utc = SystemClock.UtcNow;
DateTime local = SystemClock.LocalNow;
```

Use `GameClock` to pace and measure frames and `SystemClock` for the wall-clock time; unlike the game
clock, the system clock follows the calendar and can jump when the clock is set.

For game logic there are three small timers driven by the frame's delta, not the clock: `Cooldown` (a
gate that is ready, then cold for a set time — a weapon or an ability on a recharge), `Interval` (fires
on a steady beat — spawn something every few seconds), and `Countdown` (a one-shot that fires once when
it reaches zero — a delayed action or a respawn).

```csharp
var fireRate = new Cooldown(0.25f);
var spawner = new Interval(2f);
var respawn = new Countdown(3f);

// each frame, with dt = (float)context.DeltaSeconds:
fireRate.Advance(dt);
if (input.WasPressed("Fire") && fireRate.TryUse()) Shoot();

for (int i = 0; i < spawner.Advance(dt); i++) SpawnEnemy();

if (respawn.Advance(dt)) Respawn();
```

`Cooldown.Fraction` and `Countdown.Progress` give a 0..1 value for a recharge meter or a progress bar.

For a simulation that must advance in equal amounts regardless of the display rate — game physics, or an
emulated core — `FixedTimestep` turns the variable frame delta into a fixed number of steps. Feed it the
real delta; it returns how many steps are due and leaves an `Alpha` for interpolating the render between
the last two steps, and it clamps a slow frame so a hitch cannot trigger a catch-up burst.

```csharp
var sim = new FixedTimestep(1.0 / 60);
int steps = sim.Advance(context.DeltaSeconds);
for (int i = 0; i < steps; i++) Step(sim.Step);
Draw(sim.Alpha); // 0..1 between the previous and current step
```

## Files

`PackageFile` in `SharpProspero.Storage` reads files bundled with a module. Assets live under the
package root (`PackageFile.Root`, `/app0`).

```csharp
byte[] level = PackageFile.ReadAllBytes("/app0/assets/level.bin");
string config = PackageFile.ReadAllText("/app0/config.json");
```

`ReadAllBytes` opens, sizes, reads and closes the file in one call and throws a `ProsperoException` on
failure. For finer control, the `KernelFile` bindings expose open, read, write, seek and close
directly.

To browse or change files, `FileSystem` lists a directory and creates, moves and removes entries:

```csharp
foreach (DirectoryEntry entry in FileSystem.EnumerateDirectory("/app0/assets"))
{
    string kind = entry.IsDirectory ? "dir " : "file";
    long size = entry.IsFile ? FileSystem.GetFileSize($"/app0/assets/{entry.Name}") : 0;
    surface.DrawText($"{kind} {entry.Name} {size}", x, y, 2, Color.White);
}
```

`EnumerateDirectory` returns each entry's `Name` and `Type` (`IsDirectory` and `IsFile` cover the
common cases) and leaves out `.` and `..`. `GetFileSize`, `Exists`, `CreateDirectory`, `DeleteFile`,
`DeleteDirectory`, `Move`, `ReadAllBytes`, `WriteAllBytes` and `WriteAllText` round it out. For whole
trees there is `EnumerateRecursive` (every file beneath a folder), `CreateDirectoryRecursive` (a folder
and any missing parents), `CopyFile` and `CopyDirectory`, and `ReadAllText` for a text file. The package
root `/app0` is read-only; writes need a writable mount.

`PathUtil` works with paths as text — no files touched. `Combine` joins parts with a single separator,
and `GetFileName`, `GetFileNameWithoutExtension`, `GetExtension`, `GetDirectoryName`, `ChangeExtension`,
`HasExtension` and `IsAbsolute` pull a path apart. Paths use a forward slash, and an absolute path starts
with one.

```csharp
string name = PathUtil.GetFileName(path);            // "level.csv"
string save = PathUtil.Combine("/data/saves", name); // "/data/saves/level.csv"
string png = PathUtil.ChangeExtension(save, "png");  // "/data/saves/level.png"
```

## Random

`SharpProspero.Numerics` has two random sources. `GameRandom` is a fast, reproducible generator for
gameplay; `HardwareEntropy` draws unpredictable bytes from the system for seeds.

```csharp
var rng = new GameRandom(seed: 1234);   // same seed, same sequence
int roll = rng.Next(1, 7);              // 1..6
double t = rng.NextDouble();            // 0..1

var unpredictable = GameRandom.FromEntropy();       // seeded from the system
ulong token = HardwareEntropy.NextUInt64();         // straight from the entropy source
```

`GameRandom` gives `NextUInt64`, `NextUInt32`, `NextDouble` (0 to 1), `Next(min, max)` (max exclusive),
`NextSingle` (with an optional range), `NextBool`, `Pick` (one item from a set) and `Shuffle` (reorders a
span in place). It is for gameplay, not for keys or tokens; take those from `HardwareEntropy`.

## Vectors

`Vector2` in `SharpProspero.Numerics` is a small value type for a position, a velocity or a direction,
so movement and steering read as arithmetic rather than a pair of loose floats.

```csharp
var position = new Vector2(100f, 60f);
var velocity = Vector2.UnitX * 240f;                 // 240 px/sec to the right
position += velocity * (float)context.DeltaSeconds;  // advance by the frame time
float away = Vector2.Distance(position, target);
Vector2 toward = (target - position).Normalized();
```

It has the usual operators, `Length`/`LengthSquared`, `Normalized`, `Dot`, `Distance`, `Lerp` and
`Rotate`, and the `Zero`, `One`, `UnitX` and `UnitY` constants.

`RectF` is an axis-aligned rectangle for a bounding box or a hit area. It answers whether it holds a
point or another rectangle, where two rectangles overlap, and pulls a point to the nearest spot inside;
`Collision` adds the circle tests game code reaches for.

```csharp
var button = new RectF(x, y, w, h);
if (button.Contains(cursor)) { }                       // point in rectangle

var ball = new Vector2(bx, by);
if (Collision.CircleOverlapsRect(ball, radius, paddle)) Bounce();
if (Collision.CirclesOverlap(a, ra, b, rb)) Hit();
```

`RectF` has `Contains`, `Intersects`, `Intersection`, `Union`, `Inflate`, `Offset` and `Clamp`, plus
`FromEdges` and `FromCenter`; edges follow the half-open rule, so rectangles that share an edge do not
both claim it.

`MathUtil` holds the small floating-point helpers that go with them: `Lerp` and `LerpClamped`,
`InverseLerp` and `Remap` (mapping a value from one range to another), `SmoothStep`, `MoveTowards`,
`Clamp`/`Clamp01`, `Approximately`, `Repeat` and `PingPong`, and angle helpers (`DegreesToRadians`,
`RadiansToDegrees`, `WrapAngle`).

When a scene holds many things, testing every pair is wasteful. `Quadtree<T>` indexes items by rectangle
and answers "what is in this area" by visiting only the parts of the world near the query — collision
broad-phase, off-screen culling, or picking under the cursor.

```csharp
var tree = new Quadtree<Entity>(worldBounds);
foreach (Entity e in entities) tree.Insert(e, e.Bounds); // rebuild each frame for moving items
foreach (Entity near in tree.Query(camera.VisibleWorldBounds)) near.Draw(surface);
```

For curved motion, `Spline` in `SharpProspero.Animation` evaluates quadratic and cubic Bezier curves and
a Catmull-Rom spline that passes through a list of waypoints — a camera path, a projectile arc, a
Ken-Burns pan. Pass a 0..1 position along the curve.

```csharp
Vector2 p = Spline.CatmullRom(waypoints, t);          // through every waypoint
Vector2 q = Spline.Bezier(a, controlA, controlB, b, t); // cubic Bezier
```

## Playing media

`SharpProspero.Media.MediaPlayer` plays a media file. Open it for a path, start it, then pull decoded
audio frames to push at an audio port and video frames to draw to the display, while it stays active.

```csharp
using var player = MediaPlayer.Open("/app0/movie.mp4");
using var audio = AudioOutDevice.OpenStereo();
player.Start();
while (player.IsActive)
{
    if (player.TryGetAudioFrame(out AudioFrame audioFrame))
        audio.Output(audioFrame.Samples);
    if (player.TryGetVideoFrame(out VideoFrame videoFrame))
        videoFrame.RenderTo(display.BackBuffer, 0, 0, display.Width, display.Height);
}
```

`Start`, `Stop`, `Pause`, `Resume`, `SetLooping`, `JumpTo` and `Position` control playback.
`TryGetAudioFrame` and `TryGetVideoFrame` return false when nothing is decoded yet, which is normal;
each audio frame reports its samples, timestamp, channel count and sample rate, and each video frame is
a decoded picture. `VideoFrame.RenderTo` converts the frame to the surface color and scales it to the
destination rectangle, so a movie draws full-screen or in a window.

The player decodes on its own threads and calls back for every allocation, which the SDK answers from
the unmanaged heap. It plays a path itself, so no file callbacks are needed.

## Decoding compressed audio

Where `MediaPlayer` plays a whole file, `AudioDecoder` turns compressed audio into samples so an
application controls playback itself — a music player, a sound bank, or anything that needs the samples
rather than the picture. It reads MPEG-1/2 Audio Layer III and MPEG-4 Advanced Audio Coding.

Hand it a run of bytes and it finds the frame, reports how many bytes it used, and writes the sound.
Advance by the consumed count and call again:

```csharp
SystemModule.Load(SystemModuleId.AudioDec);

using var decoder = AudioDecoder.CreateMp3();
using var device = AudioOutDevice.OpenStereo();

byte[] pcm = new byte[decoder.SuggestedOutputSize];
int read = 0;
while (read < file.Length)
{
    AudioDecodeResult step = decoder.Decode(file.AsSpan(read), pcm);
    if (step.BytesConsumed == 0)
        break;                                   // needs more input than is left
    read += step.BytesConsumed;
    device.Output(MemoryMarshal.Cast<byte, short>(pcm.AsSpan(0, step.BytesProduced)));
}
```

| Call | What it does |
|---|---|
| `CreateMp3(wordSize)` | A decoder for Layer III, writing signed 16-bit samples by default. |
| `CreateAac(maxChannels, selfDescribingFrames, highEfficiency, wordSize)` | A decoder for Advanced Audio Coding; the default suits a file whose frames carry their own header. |
| `Decode(input, output)` | Decode one frame; returns the bytes consumed and produced. |
| `Reset()` | Drop what the decoder carried between frames, after seeking. |
| `SuggestedOutputSize` | A comfortable output buffer size for one call. |
| `SampleRate`, `ChannelCount` | What the decoder reported, once a frame has been read. |

Because the decoder finds the frame itself, no frame splitter is needed — feed it the file and follow
the consumed count. A consumed count of zero means the remaining input holds no complete frame.

## Decoding compressed video

`VideoDecoder` decodes H.264 a unit at a time and hands back each picture. Use it for a stream an
application receives itself, or anywhere the frames are wanted rather than playback (`MediaPlayer`
covers playing a file).

The service provides no memory of its own, so the decoder reserves every region it needs — each of the
kind the service expects — and offers a picture buffer per call:

```csharp
SystemModule.Load(SystemModuleId.AvPlayer);   // brings in the video decoder

using var decoder = VideoDecoder.CreateAvc();          // 1080p by default
using DirectMemoryRegion frame = decoder.AllocateFrameBuffer();

foreach (ReadOnlyMemory<byte> unit in units)
{
    DecodedPicture? picture = decoder.Decode(unit.Span, frame);
    if (picture is { } p)
        Present(p.Width, p.Height, p.PitchInBytes, p.AsSpan());
}

while (decoder.Flush(frame) is { } tail)                // pictures still held back
    Present(tail.Width, tail.Height, tail.PitchInBytes, tail.AsSpan());
```

| Call | What it does |
|---|---|
| `CreateAvc(maxWidth, maxHeight, profile, maxLevel)` | A decoder sized for the largest picture it must handle. |
| `AllocateFrameBuffer()` | Reserve a picture buffer of the size and alignment this decoder asks for. |
| `Decode(unit, frameBuffer, attachedData)` | Decode one unit; null while the decoder is still filling. |
| `Flush(frameBuffer)` | Push out a picture still held back; call until it returns null. |
| `Reset()` | Drop what the decoder carried, after seeking. |

Keep one buffer per picture in flight. `DecodedPicture` reports the size and row stride and `AsSpan()`
gives its bytes; the picture is written into the buffer that was offered for it, so hold that buffer
until the picture has been used.

`MediaPlayer.OpenUrl` plays a network stream instead of a file — pass an `http://` or `https://`
address and the player opens the stream over its own network source. The console needs a working
connection; everything after opening is the same as a file.

```csharp
using var player = MediaPlayer.OpenUrl("https://example.com/stream.m3u8");
player.Start();
```

## The web browser

`SharpProspero.Platform.WebBrowser` opens the system browser over the running application. Open it
for an address, then poll it once per frame until it closes.

```csharp
using var browser = WebBrowser.Open("https://example.com");
while (browser.Update() != WebBrowserState.Closed)
    display.Present();
int result = browser.Result();
```

Opening brings the shared dialog subsystem up, loads the browser module, and starts it, in that
order; disposing closes the browser if it is still open and shuts the subsystem down. The parameter
block the service takes carries a check value derived from its own address, so build it with
`WebBrowserDialog.InitializeParam` and hand it over from where it lives; do not copy it afterwards.

## Text input

`SharpProspero.Platform.TextInputDialog` shows the on-screen keyboard and hands back what the user
typed. Open it for a title, poll it once per frame until it closes, then read the text. This is what
lets a file explorer name a folder, a browser take an address, or any utility accept input.

```csharp
using var input = TextInputDialog.Open("Enter a name", maxLength: 64);
while (input.Update() == TextInputState.Running)
    display.Present();
if (input.EndStatus == ImeDialogEndStatus.Ok)
    Use(input.Text);
```

`Open` brings the dialog subsystem up, loads the keyboard module, and shows the keyboard centered on
screen. Pass a `placeholder`, an `initialText`, an `ImeType` (a number pad, an email or web-address
layout, and so on), or `ImeOption.Password` to mask the text. `Text` is the entered string once the
keyboard has finished, or empty when the user canceled. Disposing closes the keyboard, shuts it down,
unloads the module, and releases the text buffer.

## Keyboard and mouse

`SharpProspero.Input.Keyboard` and `SharpProspero.Input.Mouse` read a USB keyboard and mouse, the
input a file explorer or a browser wants beyond the controller. Open each for a user, read it each
frame, dispose it at shutdown.

```csharp
using var keyboard = Keyboard.Open();
using var mouse = Mouse.Open();

KeyboardState keys = keyboard.Read();
if (keys.Modifiers.HasFlag(KeyModifier.LeftControl)) { }

MouseState m = mouse.Read();
cursorX += m.DeltaX;              // the mouse reports movement, not a position
cursorY += m.DeltaY;
if (m.IsButtonDown(MouseButton.Primary)) { }
```

The keyboard reports the USB usage codes of the keys held and the modifier state; the mouse reports
the movement since the last read, the buttons, and the wheel.

A key code is a position on the keyboard, not a letter. `KeycodeConverter` turns one into the character
it produces, applying the layout and the held modifiers, so a build can read typed text from a USB
keyboard without the on-screen keyboard. It resolves from a system library at run time, so open it where
the module is available.

```csharp
using KeycodeConverter? converter = KeycodeConverter.TryOpen();   // null without the library
if (converter is not null)
{
    KeyboardLayout layout = converter.GetLayout();   // the user's chosen layout
    char c = converter.ToCharacter(keycode, keys.Modifiers, layout);
    if (c != '\0')
        typed += c;
}
```

`GetLayout` reads the user's layout so a build honors it; `ToCharacter` returns the null character for a
key that makes none, such as a modifier or a function key.

## Network information

`SharpProspero.Platform.NetworkInfo` reports the network connection, the panel a system-information
utility shows. Open it, read the fields, dispose it.

```csharp
using var net = NetworkInfo.Open();
if (net.IsConnected)
{
    Show(net.IpAddress);            // "192.168.1.20"
    Show(net.Ssid);                // wireless network name, empty when wired
    Show(net.MacAddress);          // "00:1a:2b:c0:ff:ee"
    Show(net.SignalStrength);      // 0 to 100 on wireless
}
```

`Device` reports wired or wireless; `State` reports where the connection is. Opening needs no socket
pool; the status service is the first and only network call it makes.

## Message and error dialogs

`SharpProspero.Platform.MessageDialog` shows a message with buttons, or a progress bar the application
drives — the progress bar is what a package installer shows while it works.

```csharp
// A progress bar, driven by the application:
using var progress = MessageDialog.ShowProgress("Installing...");
while (installing)
    progress.SetProgress(percentDone);

// A yes/no question:
using var ask = MessageDialog.ShowMessage("Delete this file?", MessageDialogButtons.YesNo);
while (ask.Update() == MessageDialogState.Running)
    display.Present();
bool yes = ask.ChosenButton == MsgDialogButtonId.Ok;   // OK and Yes share the first button
```

`SharpProspero.Platform.ErrorDialog` shows the console's own message for an error code, to report a
failure in the system's style. Show it, poll it until it closes.

```csharp
using var dialog = ErrorDialog.Show(errorCode);
while (dialog.Update() != ErrorDialogState.Closed)
    display.Present();
```

## Save data

`SharpProspero.Platform.SaveDataManager` lists and manages the console's saves for the signed-in
user, the surface a save-data manager or a backup tool is built on.

```csharp
using var saves = SaveDataManager.Open();
foreach (SaveDataInfo save in saves.Enumerate("CUSA00000"))
    Show(save.Title, save.SubTitle, save.ModifiedTime);

// Mount one to read its files, then unmount:
using MountedSave mounted = saves.Mount(saves.Enumerate("CUSA00000")[0].DirName);
byte[] data = FileSystem.ReadAllBytes(mounted.MountPoint + "/progress.dat");
```

`Enumerate` returns each save's directory name, title, subtitle, detail, user parameter, and modified
time. `Mount` mounts a save read-only by default and returns the path its files live under; `Delete`
removes a save.

## Downloading

`SharpProspero.Platform.HttpClient` downloads over HTTP and HTTPS, to fetch a file or a package from a
URL. Creating it brings up the network pool, the TLS context, and the HTTP service in order.

```csharp
using var http = HttpClient.Create();
HttpResponse response = http.Get("https://example.com/homebrew.pkg");
if (response.IsSuccess)
    FileSystem.WriteAllBytes("/data/homebrew.pkg", response.Body);
```

`Get` returns the status code and the body. Combined with the package installer, this downloads and
installs a package from the network.

## Notifications

`SharpProspero.Platform.Notification` shows the on-screen toast that slides in at the top of the
screen — to confirm a copy, report a finished install, or show a short message.

```csharp
Notification.Show("Installed successfully.");
```

It also drives the persistent banner shown next to the PS button through the notification service.
The banner stays up until you take it down, so it suits a background task that should stay visible:

```csharp
Notification.ShowPsButtonBanner();   // optional JSON config: ShowPsButtonBanner("{...}")
// ... work continues, banner stays on screen ...
Notification.HidePsButtonBanner();
```

## Trophies

`SharpProspero.Platform.TrophySet` reads a title's trophies and the signed-in player's progress, and
shows the system trophy list. Open it for a user, read the set-wide progress or the individual trophies,
and dispose it.

```csharp
using var trophies = TrophySet.Open(userId);
TrophyProgress progress = trophies.GetProgress();   // e.g. 12 of 34 unlocked, 41%
foreach (TrophyInfo t in trophies.GetTrophies())
    hud.AddRow(t.Name, t.Unlocked);
trophies.ShowList();                                 // the system trophy screen
```

`TrophyProgress` gives the set title and unlocked count; each `TrophyInfo` carries the trophy's grade,
name, description and whether the player has unlocked it. `TrophySet` is the read-and-display side, and it
needs a signed-in user whose title has a registered trophy set.

Unlocking a trophy — and reporting activities and statistics — is done through the universal-data-system,
the write side. `UniversalDataSystem.PostEvent` posts a named event with its properties; a trophy set
defines the event that unlocks each trophy.

```csharp
UniversalDataSystem.Initialize();
using var uds = UniversalDataSystem.Open(userId);
uds.PostEvent("_UnlockTrophy", e => e.Set("_trophy_id", trophyId));
UniversalDataSystem.Terminate();
```

The build callback sets the event's properties (`Set` takes a string, integer, long, double or boolean),
and the event is posted when the callback returns.

## Bluetooth HID

`SharpProspero.Platform.BluetoothHid` is the entry point to the Bluetooth human-interface-device driver.
`Initialize` opens the device (privileged) and must run first; `Version` reads the module's build number.

```csharp
BluetoothHid.Initialize();
```

This is a low-level driver surface. The device, report, and callback calls take structures whose layout
is device-specific, so they are exposed directly on `SharpProspero.Interop.Bluetooth.SceBluetoothHid`
(get and set input, feature, and output reports; read the report descriptor, device name, and device
info; register a device and a callback; interrupt output; disconnect) for advanced use. Each returns a
status code.

## Console feature flags

`SharpProspero.Platform.FeatureFlag` reads the console's feature flags by number: whether a feature is
enabled, and whether a change to it is waiting for a reboot.

```csharp
if (FeatureFlag.IsOn(featureId))
    Enable();
```

## GPU graphics (Agc)

The GPU graphics API is exposed in two layers. The lower layer, `SharpProspero.Interop.Agc`, is the
complete flat-C command interface: `SceAgc` (192 command builders - draws, dispatches, register writes,
synchronization, shader create and link) and `SceAgcDriver` (79 driver calls - submit, queue, display
flip, wait). Every builder takes the command buffer as its first argument and returns the address of the
packet it wrote.

The higher layer, `SharpProspero.Graphics.Agc`, wraps that for everyday use:

- `DrawCommandBuffer` records commands into GPU-readable memory. `Allocate` gives a ready buffer; the
  record calls cover register writes, index state, draws, synchronization, and the present calls
  (`WaitUntilSafeForDisplay`, `SetFlip`).
- `AgcShader` creates a shader from its compiled binary (a header plus a code section). Shaders are
  compiled ahead of time by the shader compiler; the runtime only loads them.
- `AgcDevice` does the one-time `Initialize`, submits a buffer with `Submit`, and closes the frame with
  `SuspendPoint`.
- `AgcFormats` holds the surface-format and channel-select enumerations.

A frame follows the shape of the graphics sample - wait for the display to release the buffer, record
state and draws, set the flip, submit, and close the frame:

```csharp
AgcDevice.Initialize();
using var dcb = DrawCommandBuffer.Allocate(1 << 20);
// ... per frame:
dcb.Reset();
dcb.WaitUntilSafeForDisplay(videoOutHandle, backBufferIndex);
// ... record register state and draws (see the render-target note below) ...
dcb.SetFlip(videoOutHandle, backBufferIndex);
AgcDevice.Submit(dcb);
AgcDevice.SuspendPoint();
```

### Surface layout

`AgcSurface.Compute` gives the memory layout of any surface - total size, base alignment, block
dimensions, and per-mip offsets - for every tile mode, computed the way the graphics address library
computes it. `LinearSurface.Compute` is the simpler linear-only helper (padded row pitch, size, 256-byte
alignment). Use them to size and align a framebuffer, a depth buffer, or a texture:

```csharp
AgcSurfaceLayout layout = AgcSurface.Compute(new AgcSurfaceDescription(
    AgcTileMode.RenderTarget, AgcSurfaceDimension.TwoD, width: 1920, height: 1080, bytesPerElement: 4));
using var mem = DirectMemoryRegion.Allocate(layout.TotalSizeBytes, layout.BaseAlignBytes);
```

### Render-target registers

`CxRenderTarget` is the sixteen-register color render-target block, with typed setters and getters for
every field. `AgcRenderTargetSetup.Initialize` fills the whole block from a `RenderTargetSpec` - the same
setup the graphics core performs, including the blend and rounding modes it derives from the channel type.
The register offsets and reset values come from the driver at runtime, so load them into the block first:

```csharp
// defaults: sixteen CxRegister values from the driver (sceAgcGetRegisterDefaults).
var rt = new CxRenderTarget().Init(defaults);
AgcRenderTargetSetup.Initialize(rt, new RenderTargetSpec(
    CxRenderTarget.Format.k8_8_8_8, CxRenderTarget.ChannelType.kUNorm, CxRenderTarget.ChannelOrder.kStandard,
    width: 1920, height: 1080, dataAddress: (ulong)mem.Address));
// rt.Registers is now ready to write into a command buffer.
```

`CxDepthRenderTarget` is the companion block for a depth and stencil buffer, with the same shape: typed
setters for the depth and stencil formats, dimensions, clear values, and addresses, loaded from the driver
defaults with `Init`.

### Pixel tiling

`AgcTiler` moves pixel bytes between plain row-major (linear) order and the hardware-tiled order the GPU
reads - to upload a texture built in memory, or to read a rendered surface back into a linear image. It
covers the render-target and depth tile modes; `LinearSizeBytes` sizes the linear buffer, and `Tile` /
`Detile` convert one mip level of one slice:

```csharp
var linear = new byte[AgcTiler.LinearSizeBytes(desc)];   // fill with your image
var tiled = new byte[AgcSurface.Compute(desc).TotalSizeBytes];
AgcTiler.Tile(tiled, linear, desc);                       // now upload `tiled` to GPU memory
```

Optimal *textures* (as opposed to render and depth targets) are still built ahead of time by the texture
tool - the same offline model as shaders.

## Install progress and app parameters

`SharpProspero.Platform.PlayGo` reads how much of the application's content has downloaded, for a
launcher or a title that streams its data.

```csharp
using var playGo = PlayGo.Open();
DownloadProgress p = playGo.GetProgress(new ushort[] { 0, 1, 2 });
Show(p.Fraction);
```

`SharpProspero.Platform.AppContent` reads the parameters a title was packaged with:

```csharp
int level = AppContent.GetIntParam(1);   // user-defined parameter 1
```

## Reading the system

`SharpProspero.Platform.SystemInfo` reports facts about the console. A diagnostics or settings
utility shows the system software version the way the console displays it:

```csharp
string firmware = SystemInfo.SystemSoftwareVersion;   // for example "11.020.000"
```

`SystemSoftwareVersionValue` returns the same version packed into a word (major byte then minor byte,
as it reads), which is the form a package's requirement is compared against. `ConsoleId` returns the
console's open identifier as a hex string, and `ProcessorCount` returns the number of cores available
to the application.

## Installing a package

`SharpProspero.Platform.PackageInstaller` installs a package file. The install service is not part of
the module set a title links against, so it is loaded at run time and its entry points resolved by
name.

```csharp
using var installer = PackageInstaller.Open();
installer.Install("/data/homebrew.pkg");
```

`Open` loads the service and starts it; `Install` hands the request over and the install continues in
the background; disposing shuts the service down. A missing module, a missing entry point, or a
rejected request all raise a `ProsperoException`. `AppExists("CUSA00000")` reports whether a title is
installed, and `AppGetSize("CUSA00000")` reads its installed size in bytes — an app manager lists and
inspects installed titles with these.

## System settings

`SharpProspero.Platform.SystemParameters` reads the user's console settings so a title can match them:

```csharp
if (SystemParameters.Language == SystemLanguage.French)
    LoadStrings("fr");
int minutesFromUtc = SystemParameters.TimeZoneMinutes;
```

`Language` is a `SystemLanguage`, `DateFormat` and `TimeFormat` describe how to present dates and
times, `TimeZoneMinutes` is the offset from UTC, and `IsSummerTime` reports daylight saving.

## Audio

`AudioOutDevice` in `SharpProspero.Audio` opens a stereo 16-bit output port. Fill a buffer of
`SamplesPerBlock` interleaved samples (left, right, left, right, …) and push it; each push blocks
until the block plays, which paces the caller to the audio clock.

```csharp
using var audio = AudioOutDevice.OpenStereo(grain: 256, sampleRate: 48000);
short[] block = new short[audio.SamplesPerBlock];
while (running)
{
    FillBlock(block);      // your synthesis or streaming
    audio.Output(block);
}
```

`OpenStereo` takes a grain (samples per block, 256 to 2048) and a sample rate. `SetVolume` sets both
channels from 0 to `AudioOut.Volume0Db`. Disposing closes the port.

`ToneGenerator` fills those blocks with a simple tone or effect — a beep, an alert, a coin, a hit —
with no audio file. Set the wave, the pitch and the loudness; the phase carries across blocks, so a held
tone is continuous.

```csharp
using var audio = AudioOutDevice.OpenStereo();
var tone = new ToneGenerator { Waveform = Waveform.Square, Frequency = 880, Amplitude = 0.3f };
short[] block = new short[audio.SamplesPerBlock];
for (int i = 0; i < beepBlocks; i++)
{
    tone.Fill(block);
    audio.Output(block);
}
```

The waves are `Sine`, `Square`, `Triangle`, `Sawtooth` and `Noise`. `Render(seconds)` returns a whole
short effect as one buffer instead of filling block by block, and `RenderClip(seconds)` returns it as a
`PcmAudio` ready for the mixer below.

`AudioMixer` plays several sounds at once — music under a run of effects, effects overlapping. Start a
clip with `Play`, then fill each block from `Mix` instead of the port directly. A mono clip is spread to
both channels and a clip recorded at another rate is retuned to the mixer's, so a sound from anywhere —
a WAV, a `ToneGenerator` effect — plays at the right pitch; finished sounds drop out on their own.

```csharp
using var audio = AudioOutDevice.OpenStereo();
var mixer = new AudioMixer();
mixer.Play(WavAudio.Load("/app0/music.wav"), volume: 0.6f, loop: true);
var coin = new ToneGenerator { Waveform = Waveform.Square, Frequency = 988 }.RenderClip(0.08);

short[] block = new short[audio.SamplesPerBlock];
while (running)
{
    mixer.Mix(block);      // overwrites the block with the mix of every playing sound
    audio.Output(block);
    if (collected)
        mixer.Play(coin);  // layered over the music
}
```

`MasterVolume` scales everything, `MaxVoices` caps how many play at once (the oldest drops to make room),
and `StopAll` clears them.

## Controller output

`GamePad` drives the controller's motors and light bar alongside reading input:

```csharp
gamePad.SetVibration(largeMotor: 200, smallMotor: 120);
gamePad.SetLightBar(0x00, 0x80, 0xFF);
gamePad.ResetLightBar();
```

Each returns false when the controller does not accept the request. Disposing the pad stops the
motors.

## Controller motion and touch

A controller sample carries more than buttons and sticks. `GamePadState` also decodes the motion and
touch fields, so a title can read the controller's orientation, motion and touch pad:

```csharp
GamePadState pad = context.Input;
Vector3 tilt = pad.AngularVelocity;       // radians per second
Quaternion facing = pad.Orientation;      // accumulated orientation
if (pad.Touch1.IsActive)
    DrawCursor(pad.Touch1.X, pad.Touch1.Y);
```

`Orientation` is a quaternion, `Acceleration` is in G and `AngularVelocity` is in radians per second,
each a `System.Numerics` value. `Touch1` and `Touch2` are the two touch-pad contacts, each with a
position and a tracking id, and `TouchCount` reports how many are live. `IsConnected` and
`TimestampMicroseconds` describe the sample itself.

## Mapping buttons to actions

`InputMap` names the controls, so game code asks whether the player jumped rather than whether cross is
pressed — and the buttons stay in one place, ready to rebind. Bind an action to a button (or a chord of
buttons, or several alternatives), feed it the controller sample each frame, then ask whether the action
was pressed, is held, or was released.

```csharp
var input = new InputMap()
    .Bind("Jump", ScePadButton.Cross)
    .Bind("Fire", ScePadButton.R2)
    .Bind("Special", ScePadButton.L1 | ScePadButton.R1)   // a chord: both held
    .Bind("Confirm", ScePadButton.Cross).Bind("Confirm", ScePadButton.Options); // alternatives

// each frame:
input.Update(context.Input);
if (input.WasPressed("Jump")) Jump();
if (input.IsHeld("Fire")) Fire();
```

`WasPressed` and `WasReleased` count the edge once; `IsHeld` is true for as long as the action is down.
`InputMap` keeps the previous sample itself, so it only needs this frame's.

## System modules

Some device libraries are not resident by default; load the ones a title needs at startup with
`SystemModule` and dispose them at shutdown.

```csharp
using var pngDec = SystemModule.Load(SystemModuleId.PngDec);
// ... use the module ...
```

`SystemModule.IsLoaded(id)` reports whether a module is present. Loading an already-loaded module
succeeds.

## System-version range

Bindings target the earliest supported system version, and later systems keep every earlier function,
so a module built with the SDK installs and runs across the whole version range. Nothing in the build
needs to state a version for that to hold.

The one place a version is declared is the package: put the lowest system the title needs in
`sce_sys/param.json` as `requiredSystemSoftwareVersion`. Raise it when the title calls a function a
later system added. The module itself records no system version, so this is what gates installation.

Versions do matter in one other place, and the toolchain handles it: a module records the module and
library **version** of everything it imports, and the loader binds an import only when that version
matches what the providing module publishes. See [modules.md](modules.md) for what that means when you
link against a library of your own.
