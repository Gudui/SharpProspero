---
title: Utilities
nav_order: 9
---

# Utilities

Smaller building blocks for real applications and toolboxes: file integrity, screenshot and photo
export, microphone capture, and the app-loop control an application needs to behave well on the system.

## File integrity and checksums

`SharpProspero.Security` computes message digests and checksums with no system module, so a tool can
verify a downloaded file against a published checksum or compare two files. The algorithms are
`Sha256`, `Sha1`, `Md5` and `Crc32`.

```csharp
using SharpProspero.Security;

string sum = Sha256.HashFileHex("/data/game.pkg");   // lowercase hex
bool ok = sum == published;

uint crc = Crc32.ComputeFileValue("/data/archive.bin");
```

Each digest also works on a block of bytes (`Sha256.Hash`, `Sha256.HashHex`) or a stream of chunks:

```csharp
var hash = new Sha256();
hash.Update(part1);
hash.Update(part2);
byte[] digest = hash.Finish();
```

Prefer `Sha256` to guard against tampering; `Md5` and `Sha1` are for matching sums that other tools
publish, and `Crc32` is a fast check for accidental corruption.

## Screenshots and photo export

A drawing surface encodes to a file for a screenshot or an export. PNG is lossless and best for
interface captures; JPEG is far smaller and best for photographic content. Load the encode module
first.

```csharp
using var pngEnc = SystemModule.Load(SystemModuleId.PngEnc);
PngEncoder.Save(surface, "/data/screenshot.png");

using var jpegEnc = SystemModule.Load(SystemModuleId.JpegEnc);
JpegEncoder.Save(surface, "/data/photo.jpg", quality: 90);   // quality 1..100
```

Both also return the encoded bytes directly (`PngEncoder.Encode`, `JpegEncoder.Encode`) for sending
over the network or storing elsewhere.

## System capture

`ShareCapture` captures the whole finished screen, the application together with the system overlays,
and saves it to the console's capture gallery. It is the game-DVR capture the share button drives: use
it to grab a 2K or 4K screenshot, or to save the last several seconds of output as a video clip. This
differs from encoding a drawing surface, which captures only what the application itself drew.

```csharp
using var share = ShareCapture.Start();
share.CaptureScreenshot(ScreenshotFormat.Png4K);   // saved to the gallery in the background
share.CaptureRecentClip(secondsBack: 30);          // save the last 30 seconds as a clip
```

Captures are asynchronous: each call returns a request id and the image or clip is written in the
background. `Block(ShareFeature.Screenshot)` prevents capture while a sensitive screen is shown, and
`Allow` re-enables it; `SetScreenshotOverlay` adds a watermark to captured screenshots.

### Live capture of the finished screen (advanced)

`SystemAvCapture` reads the live system-composited audio and video — the finished screen the whole
system draws, together with its audio — for a recorder or a stream, rather than saving a gallery clip.
It is an advanced, privileged surface: the capture service runs behind a system channel and the process
must hold the authority to reach it, which a plain application sandbox does not, so opening it there
fails with a permission error. Treat it as best-effort.

```csharp
using SharpProspero.Interop.AvCapture;
using SharpProspero.Platform;

using var capture = SystemAvCapture.Open();
capture.OpenVideo(Avcap2VideoConfig.Create());
capture.Start();
while (recording)
{
    if (capture.TryReadVideo(out Avcap2VideoFrameInfo frame) && frame.IsValid)
        Encode(frame);          // a privileged consumer reads the frame planes
}
capture.Stop();
```

For a recording path that needs no elevated privilege, save a gallery clip with `ShareCapture` above,
or encode the application's own frames with the image encoders. Use `SystemAvCapture` only when the
whole finished screen, including other applications and the system overlays, must be captured live.

## Microphone capture

`AudioInDevice` captures 16-bit samples from the microphone for a voice recorder, a level meter, or
speech input. It mirrors audio output: open a port, then pull one block per call, each call blocking
until a block is captured.

```csharp
using SharpProspero.Audio;

using var mic = AudioInDevice.OpenMicrophone(userId);
short[] block = new short[mic.SamplesPerBlock];
while (recording)
{
    mic.Read(block);
    // append block to a buffer, measure its level, or feed it onward
}
```

`IsSilent` reports when the input is muted at the hardware or by the system.

## WAV audio files

`WavAudio` reads and writes 16-bit PCM WAV files with no system module, so a sound loads straight into
the shape the audio port plays and a microphone recording writes straight back out. A file becomes a
`PcmAudio` — the interleaved samples, the sample rate and the channel count.

```csharp
using SharpProspero.Audio;

PcmAudio clip = WavAudio.Load("/app0/assets/beep.wav");
using var audio = AudioOutDevice.OpenStereo();
audio.Output(clip.Samples);            // play it

// Save a recording:
WavAudio.Save("/data/recording.wav", new PcmAudio(recorded, 48000, 2));
```

`PcmAudio` reports its `FrameCount` and `DurationMilliseconds`. Only uncompressed 16-bit mono or stereo
is handled, which is the format the audio ports use, so what is read is always ready to play.

## Keeping the console awake and reacting to the system

`SystemControl` gives an application the app-loop control it needs. During a long operation with no
controller activity, call `KeepAwake` periodically so the console does not shut down on its idle timer.

```csharp
using SharpProspero.Platform;

while (installing)
{
    SystemControl.KeepAwake();
    // do a slice of work
}
```

Poll `TryReceiveEvent` each frame to react to system events, such as resuming from sleep, where time
and inputs may have moved on:

```csharp
while (SystemControl.TryReceiveEvent(out SystemEventType type))
{
    if (type == SystemEventType.Resume)
        ResyncClock();
}
```

Other members read whether the application is in the background (`IsInBackground`), the display's safe
area for laying out important content (`DisplaySafeAreaRatio`), and take the audio output for the
module alone (`SilenceBackgroundMedia` / `RestoreBackgroundMedia`). `LoadExecutable` replaces the
running module with another, for chain-loading. The console's name is available from
`SystemParameters.SystemName`.

## Logging

`SharpProspero.Diagnostics` gives a module a small logging facility: choose a minimum level, add one or
more sinks, and write leveled messages. Messages below the minimum, or when no sink is attached, cost
almost nothing, and a failing sink never throws back to the caller.

```csharp
using SharpProspero.Diagnostics;

Log.MinimumLevel = LogLevel.Debug;
Log.AddSink(FileLogSink.Open("/data/app.log"));   // appends lines to a file
Log.AddSink(new ConsoleLogSink());                // and to the development console

Log.Information("started");
Log.Error($"load failed: 0x{code:X8}");
```

Each line is written as `HH:mm:ss.fff LVL message`. `FileLogSink` appends to a file the user can read
back after a run and is disposed at shutdown; `ConsoleLogSink` writes to standard output, which appears
on the development console when one is attached. Implement `ILogSink` to send logs somewhere else, such
as over the network.

## Settings files

`IniFile` keeps a module's own configuration in a small INI-style file, with no system module. Values
live under named sections as `key = value` lines, and a leading `;` or `#` marks a comment — a format
the user can also read and edit. Load a file, read and write typed values, save it back.

```csharp
using SharpProspero.Storage;

IniFile settings = IniFile.Load("/data/app.ini");
int volume = settings.GetInt("audio", "volume", 80);
bool fullscreen = settings.GetBool("display", "fullscreen", true);

settings.Set("audio", "volume", 90);
settings.Save("/data/app.ini");
```

`GetString`, `GetInt` and `GetBool` each take a fallback for a missing value, so a first run with no
file still gets sensible defaults. `Load` returns an empty store when the file is absent.

## Optical disc

`DiscDrive` reaches the Blu-ray drive. There is no dedicated disc service, so it works through the file
system and the raw device node, and both need the module to run with enough privilege to reach the
drive, which a plain application sandbox does not have — treat the reads as best-effort.

When the system has recognised a disc it mounts its filesystem under `DiscDrive.MountPoint`
(`/mnt/disc`), which is browsed with the ordinary file APIs:

```csharp
if (DiscDrive.IsDiscMounted)
    foreach (DirectoryEntry entry in DiscDrive.EnumerateFiles())
        Show(entry.Name);
```

The raw block device is opened for a sector-level read or a full dump:

```csharp
using var disc = DiscDrive.OpenDevice();               // /dev/cd0
long total = disc.DumpTo("/data/disc.iso", onProgress: bytes => Report(bytes));
```

`DumpTo` reads the device to a file until the end; `Read` and `Seek` do positioned reads. What the
device returns is the drive's raw content, so for a commercial disc the readable files are the ones the
system has already mounted under `/mnt/disc`, not the raw sectors.
