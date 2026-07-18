---
title: Utilities
nav_order: 13
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
