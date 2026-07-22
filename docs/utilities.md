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
`Sha256`, `Sha512`, `Sha1`, `Md5`, `Crc32` and `Sha3` (SHA3-256/384/512, the Keccak family).

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

Prefer `Sha256` (or `Sha512` for a wider digest) to guard against tampering; `Md5` and `Sha1` are for
matching sums that other tools publish, and `Crc32` is a fast check for accidental corruption. `Sha3` is
the newer Keccak-based standard for a tool that needs it — pick the width with `Sha3Variant`:

```csharp
string sha3 = Sha3.HashHex(bytes, Sha3Variant.Bits512);
```

To prove a message was not changed by anyone without a shared key, use a keyed digest (HMAC) over any of
those hashes:

```csharp
string tag = Hmac.Sha256Hex(key, message);   // also Sha512, Sha1, Md5
bool authentic = tag == expected;
```

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

## Encoding audio to AAC

`AacEncoder` compresses 16-bit or floating-point PCM into AAC-LC, one 1024-sample frame at a time, for a
recording or an export. Create it for a channel count and bit rate, feed it frames, and flush at the end.

```csharp
using SharpProspero.Platform;

using var encoder = AacEncoder.Create(channels: 2, bitRate: 128000);
var block = new byte[Interop.Audio.M4aacEnc.MaxOutputBufferSize];
foreach (ReadOnlyMemory<byte> frame in frames)            // 1024 samples per channel each
{
    int written = encoder.Encode(frame.Span, block);
    output.Write(block, 0, written);
}
output.Write(block, 0, encoder.Flush(block));             // any trailing block
```

The lower-level bindings for the AAC and ATRAC9 encoders (`SharpProspero.Interop.Audio.M4aacEnc` and
`At9Enc`) are there for finer control; the existing `Audiodec` covers the decode side.

## Audio synthesis and mixing (Ngs2)

`SharpProspero.Interop.Audio.Ngs2` binds the audio synthesis and mixing engine: a system owns racks
(sampler, submixer, reverb, mastering), a rack owns voices that play waveforms, and a render pass mixes
them into an output buffer. It is the full flat interface - system, rack, voice, stream, and waveform
calls - for building a multi-voice mixer with effects. The engine works in a buffer the caller sizes with
the query-buffer-size calls; handles are opaque, and the sized option and command structures are built
against the reset-option and query-info calls.

## Camera depth

`SharpProspero.Interop.Vision.Depth2` binds the stereo-camera depth-generation library: it turns the
camera's two images into a 16-bit depth map. Query the working memory a configuration needs, initialize
the library into that buffer to get a handle, then per frame set the source images, submit, wait, and read
back the depth image. Its parameter structures, enums, and error codes come from the vision header.

## Watching for device changes

`DeviceMonitor` notices when the set of connected devices changes, through the message bus. Start it, then
compare `Generation` against the last value you saw (it advances on any change), or read the pending event
bitmask with `PeekEvents` / `ConsumeEvents`. The lower-level `SharpProspero.Interop.Device.DeviceService`
also exposes the device-info query.

```csharp
using SharpProspero.Platform;

using var devices = DeviceMonitor.Start();
int seen = devices.Generation;
// each frame:
if (devices.Generation != seen)
{
    seen = devices.Generation;
    OnDevicesChanged();
}
```

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

## Frame timing

`FrameStats` tracks how long recent frames took and reports the frame rate and the frame time, so a
build can show whether it is holding its pace. Feed it the time since the last frame, then read the
figures or draw the readout and a small graph over the screen.

```csharp
using SharpProspero.Diagnostics;

var stats = new FrameStats();          // averages over the most recent frames

// each frame:
stats.Record((float)context.DeltaSeconds);
// after drawing the screen:
stats.Draw(surface, 20, 20, scale: 2, Color.White);
stats.DrawGraph(surface, 20, 60, 240, 60, Color.FromRgb(90, 160, 255));
```

`Fps` comes from the mean frame time, and `LastMs`, `AvgMs`, `MinMs` and `MaxMs` report the window in
milliseconds. The graph draws the recent frame times oldest-first, scaled so a slow frame stands out
against a flat low line. A mean hides the occasional stutter, so `PercentileMs(95f)` gives the time all
but the slowest five percent of frames came in under, and `OnePercentLowFps` reports the rate a player
feels during the worst frames.

## Scheduling callbacks

Where the game-logic timers (`Cooldown`, `Interval`, `Countdown`) are values a frame polls, a
`FrameScheduler` runs a callback for you when its time comes. Call `Update` once a frame with the seconds
elapsed; it fires the callbacks that came due, in order, on that thread.

```csharp
using SharpProspero.Timing;

var scheduler = new FrameScheduler();
scheduler.After(1.5, () => ShowHint());        // once, in 1.5 s
int spawn = scheduler.Every(0.75, SpawnEnemy); // repeating, every 0.75 s

// each frame:
scheduler.Update(context.DeltaSeconds);
// later:
scheduler.Cancel(spawn);
```

`After` returns a handle for `Cancel`; `Every` repeats until cancelled and, after a long pause, fires once
and re-arms rather than firing once per missed interval. Work scheduled from inside a callback waits for
the next `Update`, so a callback that re-schedules itself cannot spin. `Clear` cancels everything.

## Background work

`SharpProspero.Threading` keeps slow work off the frame loop so the screen never freezes. A
`BackgroundOperation` runs one piece of work on a background thread; poll it each frame and act when it
finishes. A `WorkQueue` is a small pool of worker threads that run whatever jobs you hand it.

```csharp
using SharpProspero.Threading;

// One-shot: load and decode a file without blocking the loop.
var loading = new BackgroundOperation<PngImage>(() => PngImage.Decode(FileSystem.ReadAllBytes(path)));
// each frame:
if (loading.IsComplete && !loading.Failed)
    ShowImage(loading.Result);

// A pool for a stream of jobs.
using var queue = new WorkQueue(workerCount: 2);
queue.Enqueue(() => WriteCache(entry));
```

A job runs on another thread, so anything it shares with the main thread must be guarded (a `lock` or an
`Interlocked` counter). `BackgroundOperation` reports `IsComplete`, `Failed` and `Error`, and reading
`Result` waits and rethrows the work's exception. `WorkQueue.Dispose` finishes the queued jobs and stops
the threads; set its `ErrorHandler` to see a job's exception. To get a result back from a pooled job
rather than spawn a thread per call, `queue.Enqueue(() => Decode(bytes))` returns a `WorkItem<T>` you poll
for `IsComplete` and read `Result` from, the pooled counterpart to `BackgroundOperation<T>`.

A drawing surface and most game state are not safe to touch from a worker thread, so a job should not
apply its own result. Hand it back to the frame thread with a `Dispatcher`. `ProsperoApp` owns one and
drains it once per frame before `OnFrame`, reached as `Dispatcher` in the app or `context.Dispatcher` in
the frame; `Post` is safe to call from any thread.

```csharp
// On a worker thread, once the work is done:
context.Dispatcher.Post(() => _texture = decoded.AsSurface()); // runs on the next frame, on the frame thread
```

`Post` queues a callback; `RunPending` runs the ones queued at that moment and returns how many ran, so a
callback that posts more work cannot stall the loop — the new work waits for the next drain. `Clear` drops
what is queued, and an `ErrorHandler` catches a throwing callback so the rest still run.

## States and events

`StateMachine<TState>` runs an application as a set of named states, one active at a time, each with work
to do on entry, every frame, and exit — a menu, a level, a pause screen, a results page. Configure the
states, `Start` in one, `Update` each frame, and `TransitionTo` another when something happens; the enter
and exit callbacks stay paired so a state cleans up after itself.

```csharp
using SharpProspero.Application;

var game = new StateMachine<Screen>()
    .Configure(Screen.Menu, onUpdate: dt => { if (start) game.TransitionTo(Screen.Play); })
    .Configure(Screen.Play, onEnter: LoadLevel, onUpdate: Step, onExit: UnloadLevel);
game.Start(Screen.Menu);

// each frame:
game.Update(context.DeltaSeconds);
```

Transitioning to the active state does nothing; a `Transitioned` event reports the state left and entered.

`EventHub` lets parts of an application talk without holding references to each other. A subscriber asks
for a message type; a publisher sends one and every subscriber for that type receives it, synchronously,
in order.

```csharp
var events = new EventHub();
using IDisposable subscription = events.Subscribe<ScoreChanged>(m => hud.SetScore(m.Total));
events.Publish(new ScoreChanged(1200));
```

The message type is the channel, so unrelated messages stay separate. Dispose the token from `Subscribe`
to stop receiving; a subscriber may subscribe or unsubscribe while a message is being delivered without
disturbing the one in flight.

`CommandStack` gives an editor-style undo and redo history. Run a change through `Execute` — either an
`ICommand` or a do/undo pair of delegates — and it is performed and remembered; `Undo` and `Redo` walk
the history, and a new change after an undo discards the redo branch.

```csharp
var history = new CommandStack();
history.Execute(() => item.Rename("new"), () => item.Rename("old"));
history.Undo(); // back to "old"
history.Redo(); // "new" again
```

A command that implements `ICoalescingCommand` folds a run of small changes (typing, dragging) into one
undo step, and `Limit` caps how many steps are kept.

`StringTable` in `SharpProspero.Platform` keeps user-facing text in data rather than code, keyed by a
stable identifier and looked up per language with a fallback table for missing keys.

```csharp
var en = new StringTable("en").Set("greeting", "Hello, {0}");
var fr = new StringTable("fr", fallback: en).Set("greeting", "Bonjour, {0}");
string text = fr.Format("greeting", name); // "Bonjour, Sven", or the English text if fr lacks the key
```

Load the entries from the INI or JSON readers; a missing key returns the key itself so it is visible. The
current user language comes from `SystemParameters`.

## Loading assets

`AssetManager` gives one path space over several sources, so a build asks for an asset by a logical name
and does not care whether it comes from a folder in the package, a tar archive bundled with the title, or
bytes built at runtime. Mount the sources once, then read by name; the bytes are read on first use and
kept, and a decoded asset is decoded once and kept too.

```csharp
using SharpProspero.Storage;

var assets = new AssetManager();
assets.MountDirectory("/app0/assets");                       // the package's assets folder, at the root
assets.MountArchive(FileSystem.ReadAllBytes("/app0/levels.tar"), prefix: "levels");

Image title = assets.Load("ui/title.bmp", BmpImage.Load);    // decoded once, then served from the cache
byte[] level = assets.ReadBytes("levels/world1.dat");
```

A later mount covers an earlier one for the same name, so a patch or a user folder can override the base
content. `Unload` forgets one asset and `ClearCache` forgets them all, keeping the mounts.

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

## JSON

`JsonValue` reads and writes JSON with no system module — for a configuration file, a manifest, or a
reply from a service. Reading a field that is missing, or reading it as the wrong kind, returns the
fallback you give rather than throwing, so a missing field is easy to handle. Objects keep their keys in
the order they were added, so a file read and written back keeps its shape.

```csharp
using SharpProspero.Storage;

JsonValue config = JsonValue.Load("/data/config.json");   // Null when the file is absent
int volume = config.GetInt("volume", 80);
bool music = config["audio"].GetBool("music", true);
string first = config["profiles"][0].GetString("name", "Player 1");

var reply = JsonValue.NewObject();
reply["ok"] = true;
reply["count"] = 3;
reply["items"] = JsonValue.NewArray().Add("a").Add("b");
reply.Save("/data/out.json");                             // indented by default
string compact = reply.Write();                           // or a compact string
```

`Parse` reads text and throws `JsonException` on bad input; `TryParse` returns false instead. `AsString`,
`AsNumber`, `AsInt`, `AsBool` read a value with a fallback, and `GetString`, `GetInt`, `GetNumber` and
`GetBool` read a named value from an object in one step. `Write(indented: true)` lays the output out over
several lines.

## XML

`SharpProspero.Xml` reads and writes XML with no system module, for a configuration or data file. It never
loads an external DTD or entity, so it is safe on the console. `XmlDocument.Parse` builds a small tree that
is checked for well-formedness (matching tags, a single root) and has its entity references resolved;
`ToXml` writes it back, optionally indented.

```csharp
using SharpProspero.Xml;

var doc = XmlDocument.Parse(PackageFile.ReadAllText("/app0/level.xml"));
string name = doc.Root.Attribute("name") ?? "untitled";
foreach (XmlElement enemy in doc.Root.Element("enemies")!.Elements("enemy"))
    Spawn(enemy.Attribute("type")!, int.Parse(enemy.Attribute("x")!));

var save = new XmlElement("save").SetAttribute("slot", "1");
save.AddElement("score").Text = "1200";
string xml = new XmlDocument(save).ToXml(indent: true);
```

For streaming, `XmlReader` is a forward-only pull parser: call `Read` and inspect `NodeType`, `Name`,
`Value`, and `Attributes`. `XmlWriter` builds output directly, escaping text and attributes and
self-closing empty elements. A malformed document throws an `XmlException` that carries the line and column.

## CSV

`Csv` reads and writes comma-separated values — a table exported from a tool, a list the user can open in
a spreadsheet. A field holding the separator, a quote or a line break is wrapped in quotes on writing and
unwrapped on reading, so the round trip keeps the data intact. Pass a tab for tab-separated values.

```csharp
using SharpProspero.Storage;

List<string[]> rows = Csv.Load("/data/scores.csv");   // empty list when the file is absent
foreach (string[] row in rows)
    Log.Info($"{row[0]} = {row[1]}");

Csv.Save("/data/out.csv", new[]
{
    new[] { "name", "score" },
    new[] { "Ada", "42" },
});
```

`Parse` and `Write` work on a string; rows are separated by a line break and fields by the separator.

## Tabular data

`DataTable` turns the raw rows the CSV and JSON readers produce into something a list or grid interface can
bind to: named columns of text cells that you can sort, filter and group. Each of those returns a new
table, so the original is untouched.

```csharp
DataTable scores = DataTable.FromCsv(FileSystem.ReadAllText("/data/scores.csv"));
DataTable top = scores
    .Where(r => r["mode"] == "ranked")
    .SortBy("score", descending: true, comparer: TextFormat.NaturalComparer);
foreach (DataRow row in top.Rows)
    Draw(row["name"], row["score"]);
```

`SortBy` is stable (equal keys keep their order) and takes a comparer, so a natural-order sort puts "9"
before "20"; `GroupBy` splits the rows into a table per value; `ToCsv` writes it back out.

## Versioned saves

`SaveState` standardizes a save file as a schema version paired with a JSON payload, so a later build can
load a save written by an earlier one. `Write` wraps the payload with its version and `Read` pulls both
back; `MigrateTo` walks an old save up to the current version through per-version upgrade steps.

```csharp
FileSystem.WriteAllText(path, new SaveState(3, data).Write(indented: true));

// Loading a possibly-older save and bringing it current:
var migrations = new Dictionary<int, Func<JsonValue, JsonValue>>
{
    [1] = d => /* v1 -> v2 */ d,
    [2] = d => /* v2 -> v3 */ d,
};
SaveState save = SaveState.Read(FileSystem.ReadAllText(path)).MigrateTo(3, migrations);
```

A missing upgrade step or a target older than the save is an error, so a broken chain fails loudly rather
than loading half-converted data.

## Binary buffers

`SpanReader` and `ByteWriter` in `SharpProspero.Buffers` read and write numbers, text and raw bytes in a
byte buffer, choosing the byte order per value — for a file header, a save file, or a message on a
socket, with no stream. `SpanReader` keeps a cursor into a buffer and throws rather than reading past the
end; `ByteWriter` grows its buffer as it goes.

```csharp
using SharpProspero.Buffers;

var writer = new ByteWriter();
writer.WriteUInt32BigEndian(0x53415645);   // a "SAVE" tag
writer.WriteInt32LittleEndian(level);
writer.WriteUtf8(playerName);
byte[] bytes = writer.ToArray();

var reader = new SpanReader(bytes);
uint tag = reader.ReadUInt32BigEndian();
int savedLevel = reader.ReadInt32LittleEndian();
```

Both cover the 8-, 16-, 32- and 64-bit integers, 32- and 64-bit floats (little- and big-endian), raw
byte spans and UTF-8 text. `SpanReader` reports `Position`, `Remaining` and `End`; `ByteWriter` exposes
`WrittenSpan` and `ToArray`.

## Ring buffers

`RingBuffer<T>` is a fixed-capacity queue over one array. When it is full, adding another item overwrites
the oldest — exactly what a rolling history wants: the last N frame times, recent log lines, an input
trail. The oldest item is at index 0, and it enumerates oldest to newest.

```csharp
using SharpProspero.Buffers;

var recent = new RingBuffer<float>(120); // last 120 frame times
recent.Add((float)context.DeltaSeconds);
float newest = recent[recent.Count - 1];
```

`ByteRing` is the byte equivalent for staging a stream — audio samples, bytes off a socket, a decoder's
output. Unlike `RingBuffer<T>` it does not overwrite: `Write` stores only what fits and returns how much
it took, and `Read` copies out the oldest bytes and removes them, so a producer and consumer can run at
different rates with back-pressure. Both wrap around the end of the buffer without copying.

## Base-N encodings

`BaseN` turns bytes into text and back — hexadecimal, Base32 (RFC 4648), and Base64 (standard or the
URL-safe alphabet, with or without padding) — for an HTTP header, a token or a `data:` URL, or a stored
blob. Decoding ignores whitespace and padding.

```csharp
using SharpProspero.Buffers;

string token = BaseN.ToBase64(bytes, urlSafe: true, padding: false);
byte[] back = BaseN.FromBase64(token);
string hex = BaseN.ToHex(digest); // lower-case; upperCase: true for upper
```

## Formatting text

`TextFormat` in `SharpProspero.Text` produces the human-readable strings an application shows: a file
size, a playback duration, a filename-friendly sort, aligned columns, and a byte dump.

```csharp
using SharpProspero.Text;

string size = TextFormat.ByteSize(1_572_864);   // "1.5 MiB"  (binary: false for MB steps)
string time = TextFormat.Duration(track.Seconds); // "3:45", or "1:02:03" past an hour
files.Sort(TextFormat.NaturalComparer);           // "file2" before "file10"
string dump = TextFormat.HexDump(header);          // offset, hex, and ASCII columns
```

`Columns` lays ragged rows out as aligned columns for a control panel or a tool, and `CompareNatural`
is the comparison behind `NaturalComparer`.

`FuzzyMatcher` matches a short pattern against text the way a type-to-find box does: the pattern
characters must appear in order but need not be adjacent, matching is case-insensitive, and a match is
scored so runs and word starts rank higher. Use it to filter and rank a list and to highlight the hits.

```csharp
List<(string Item, FuzzyMatch Match)> hits = FuzzyMatcher.Rank(query, titles, t => t);
// hits[0].Item is the best match; hits[i].Match.MatchedIndices are the characters to bold.
```

## Archive files

`TarArchive` reads a tar file — the common way to bundle many assets into one — into its members, with no
system module. It handles the widely used forms (the original layout, the ustar long-path prefix, and GNU
long names) and returns each regular file and directory; other record kinds, such as links, are skipped. A
tar is not compressed, so a member's bytes come back as stored.

```csharp
using SharpProspero.Storage;

foreach (TarEntry entry in TarArchive.Read(FileSystem.ReadAllBytes("/data/assets.tar")))
{
    if (!entry.IsDirectory)
        Install(entry.Name, entry.Data);   // entry.Text decodes the bytes as UTF-8
}
```

Each `TarEntry` carries its `Name` (with any directory prefix already joined on), `IsDirectory`, and
`Data`. A malformed archive — a bad header checksum, a bad size, or an entry running past the end — raises
a `ProsperoException` rather than returning partial results.

## System settings

`SystemSettings` reads and writes the values the system itself keeps, which no other service exposes.
Each entry is addressed by a numeric identifier the system defines, so a tool supplies the identifier
it is interested in.

```csharp
using SharpProspero.Platform;

if (SystemSettings.TryOpen(out SystemSettings? settings))
{
    using (settings)
    {
        if (settings!.TryGetInt32(id, out int value))
            Log.Info($"setting {id} = {value}");

        settings.SetInt32(id, value + 1);
        string text = settings.GetString(otherId);
    }
}
```

| Call | What it does |
|---|---|
| `TryOpen(out settings)` | Load the service, reporting whether it could be reached. |
| `Open()` | Load it, raising when it could not be reached. |
| `GetInt32(id)` / `TryGetInt32(id, out value)` | Read a whole-number setting. |
| `SetInt32(id, value)` | Write a whole-number setting. |
| `GetString(id, maxLength)` / `TryGetString(id, out value, maxLength)` | Read a text setting. |
| `SetString(id, value)` | Write a text setting. |

Reaching this service depends on what the running build is permitted to do. `TryOpen` and the `Try`
forms report a refusal rather than raising, so a tool can offer the feature only where it works and
carry on where it does not. Dispose the object to unload the service.

## Background transfers

`DownloadService` controls the transfers the system is running: find the task carrying a piece of
content, then hold it back, let it carry on, or stop it. A tool that reports what the console is
downloading, or that pauses a transfer while something else runs, works through this.

```csharp
using SharpProspero.Platform;

if (DownloadService.TryOpen(out DownloadService? transfers))
{
    using (transfers)
    {
        if (transfers!.TryFindTaskByContentId(contentId, kind, out int task))
        {
            transfers.Pause(task);
            // ... later
            transfers.Resume(task);
        }
    }
}
```

| Call | What it does |
|---|---|
| `TryOpen(out service, memorySize)` | Load and start the service, reporting whether it could be reached. |
| `TryFindTaskByContentId(contentId, kind, out taskId)` | Turn a content identifier into a task identifier. `kind` is one of the accepted values in `FindKinds`. |
| `Start` / `Stop` / `Pause` / `Resume` (taskId) | Control one transfer. |
| `TryGetProgress(taskId, out progress)` | Read how far a transfer has gone as named fields. |
| `TryGetProgressRecord(taskId, destination)` | Read the whole 88-byte record for the fields that are not named. |

`TryGetProgress` returns a `TransferProgress`: `TotalBytes`, `TransferredBytes`, a `PercentComplete`
derived from them, an `ErrorCode` (negative on failure, exposed as `HasError`), and `IsComplete`. These
four fields of the service's record were recovered from the callers that read it; the rest of the
record is left to `TryGetProgressRecord`.

The service needs a block of memory, which the object reserves and releases; the default is the
smallest it accepts. Reaching it depends on what the running build is permitted to do.

**Creating** a transfer is deliberately absent, for a settled reason: the register-by-storage call was
traced and its block is mapped, but it is a stub on a retail system, and the live call that takes a
network address uses a different block that no reachable caller builds — so it stays out rather than
being bound as a call that always fails or a block filled by guesswork.

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
