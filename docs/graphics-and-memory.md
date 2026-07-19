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
| `FillRoundedRect(x, y, w, h, radius, color)` | Fill a rectangle with rounded corners. |
| `DrawRoundedRect(x, y, w, h, radius, color)` | Draw a rounded-rectangle outline. |
| `DrawLine(x0, y0, x1, y1, color, thickness)` | Draw a line of a given pixel thickness. |
| `FillTriangle(x0, y0, x1, y1, x2, y2, color)` | Fill a triangle. |
| `FillPolygon(points, color)` | Fill a simple polygon (even-odd rule). |
| `BlitScaled(source, x, y, w, h)` | Copy a surface scaled to a destination rectangle. |
| `BlitScaledBlended(source, x, y, w, h)` | Scaled copy, blending by source alpha. |
| `BlitRotated(source, centerX, centerY, angleRadians)` | Copy a surface rotated about its center, blended. |

`Region` composes: build a themed panel by taking a sub-region and drawing into it with its own local
coordinates. Gradients and rounded rectangles give buttons and panels a finished look without an image,
and the scaled and rotated copies place thumbnails and sprites.

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
hue in degrees (wrapped) and a saturation and value in 0-1. `WithAlpha` keeps the red, green and blue
and sets a new alpha, which `BlitBlended` then composites. `Black`, `White`, `Red`, `Green`, `Blue`
and `Transparent` are ready to use.

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
`DeleteDirectory`, `Move`, `ReadAllBytes`, `WriteAllBytes` and `WriteAllText` round it out. The
package root `/app0` is read-only; writes need a writable mount.

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

`GameRandom` gives `NextUInt64`, `NextUInt32`, `NextDouble` (0 to 1) and `Next(min, max)` (max
exclusive). It is for gameplay, not for keys or tokens; take those from `HardwareEntropy`.

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
