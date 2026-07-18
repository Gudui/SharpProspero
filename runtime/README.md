# Runtime support pack

The link step needs a set of archives compiled for the device ABI: the ahead-of-time runtime and the
platform layer that ties it to the kernel. This folder holds the platform layer and the steps that
assemble the pack. The finished folder is what `PROSPERO_RUNTIME_PACK` points at.

## What the pack contains

| Piece | Source | Role |
|---|---|---|
| Platform layer | `pal/prospero_pal.c` (here) | Implements the operating-system primitives the runtime calls (memory, threads, thread-local storage, time) by forwarding to the kernel module. |
| Runtime core | The ahead-of-time runtime for an x86_64 target | The garbage collector, type system, exception handling and bootstrap the compiled object references. |
| C runtime forwarders | Small forwarding objects, as needed | Map any remaining C-library symbols the runtime imports onto the kernel and C runtime modules. |

## Building the platform layer

Compile `pal/prospero_pal.c` for the device ABI (freestanding, optimized for size) into an object,
wrap it in an archive named `libProsperoPal.a`, and place the archive in the pack folder. The source
is small and freestanding, so any compiler that targets the device ABI produces it.

## The runtime core

The runtime core is the one piece that is produced outside this repository. It is the ahead-of-time
runtime for an x86_64 target, built so its operating-system calls resolve against the platform layer
above rather than a host C library. Producing it is a build of the runtime sources for the target
followed by a validation run on a device; it is a mechanical build with the platform layer already in
hand, not open design work.

Once built, drop the runtime archives next to `libProsperoPal.a`. The order the linker expects can be
fixed with `ProsperoRuntimeLibraries` (see `build/Prospero.App.targets`); otherwise every archive in
the folder is linked inside one group.

## How the layer maps to the kernel

`pal/prospero_pal.c` is the record of the mapping. Each POSIX primitive the runtime uses forwards to
a kernel entry:

- Memory (`mmap`/`munmap`/`mprotect`) forward to the named-flexible-memory and protection calls.
- Threads and synchronization forward to the pthread family in the kernel module.
- Thread-local storage forwards to the pthread key calls.
- Time and sleep forward to the process-time counter and the microsecond sleep.

Graphics buffers do not come through this layer; they use the direct-memory path in the SDK, which is
separate from the managed heap the runtime manages here.

## Using the pack

```
setx PROSPERO_RUNTIME_PACK "<this folder once assembled>"
```

With the pack present, `src/SharpProspero.Sample/build.ps1` links a module end to end. Without it, the
solution still builds and the tests still run; only the link step needs the pack.
