---
title: Payload API
parent: Payloads
nav_order: 7
---

# Payload API

Everything a payload calls from managed code lives under the `SharpProspero.Payload` namespace.
This page groups the surface by what it does, names the wrapper class, and shows one call for
each.

## Entry point and arguments

| Type | What it holds |
|---|---|
| `PayloadEntryPoint` | The loader-supplied arguments the CRT captured. `PayloadEntryPoint.Args` returns the `PayloadArgs*` — null when the loader did not supply one. |
| `PayloadArgs` | The struct the loader hands the payload: the resolver entry, the pipe primitive descriptors, and the kernel data base. |

```csharp
PayloadArgs* args = PayloadEntryPoint.Args;
if (args == null) { /* loader did not supply args */ }
```

## Kernel

| Class | What it does |
|---|---|
| `PayloadKernel` | Process-oriented helpers: walk the process list, promote a running application (credentials, capabilities, filesystem view), cache the root vnode. See [Kernel access](payload-kernel.md). |
| `PayloadKernelIo` | The raw read/write surface: `ReadU64`, `WriteU64`, `Read`, `Write`, and `TryRead*`/`TryWrite*` for a soft failure. |

## Networking

```csharp
int listener = PayloadNetwork.Listen(9069, backlog: 8);
int client   = PayloadNetwork.Accept(listener);
Span<byte> buf = stackalloc byte[1024];
long n = PayloadNetwork.Receive(client, buf);
PayloadNetwork.SendAll(client, buf.Slice(0, (int)n));
PayloadNetwork.Close(client);
PayloadNetwork.Close(listener);

// Outbound
int s = PayloadNetwork.Connect(192, 168, 1, 10, 9069);
```

The socket API routes through the POSIX interop layer, which binds to `libSceNet.sprx` at run
time via the payload's `DT_NEEDED` list. `libSceNet.sprx` is one of the default entries, so a
payload calls the socket surface without any extra project declaration.

## Filesystem

`PayloadFileSystem` wraps `opendir`, `readdir`, `closedir`, and `stat` from libc, with a small
`IsDirectory(mode)` helper:

```csharp
bool isDir = PayloadFileSystem.IsDirectory(mode);
```

For any other filesystem call the payload needs (`open`, `read`, `write`, `mkdir`, `unlink`, and
so on), declare it with `[LibraryImport]` in the payload's own `Program.cs` — every FreeBSD libc
call is available through the resolver cascade. Every path is applied in the host process's
filesystem view, which is the per-title sandbox by default. A companion daemon promotes another
process to lift that view — see [Promoting a running application](payload-promotion.md).

## Process

```csharp
int rc = PayloadProcess.GetAppInfo(pid, out PayloadAppInfo info);
// info.AppId, info.AppType, info.TitleId (10-byte identifier)
```

`PayloadAppInfo` exposes `AppId`, `AppType`, and the ten-byte `TitleId` identifier for the
running process. Use `PayloadKernel.FindProcessByComm(...)` or `FindProcessByTitleId(...)` to walk
the process list and locate the target of a credential-write.

## Notifications

```csharp
PayloadNotification.SendKernelNotification("unjail: daemon ready"u8);
PayloadNotification.SendNotification(userId: 1, isLogged: true, jsonPayload: "..."u8);
PayloadNotification.SendNotificationById(userId: 1, isLogged: true,
                                         id: "myEventId"u8, jsonData: "{...}"u8);
```

The kernel variant is what the CRT uses for daemon bring-up messages. The user-visible variants
open on top of the system notification service; the `id` argument names a notification identifier
the system recognises, and `jsonData` carries the notification body.

## Dynamic library loading

```csharp
void* handle = PayloadDlfcn.Dlopen(nameUtf8, PayloadDlfcn.RtldLazy);
void* sym    = PayloadDlfcn.Dlsym(handle, "someSymbol"u8);
PayloadDlfcn.Dlclose(handle);
```

`PayloadDlfcn` covers `dlopen`, `dlsym`, `dlclose`, and `dlerror` for a module the payload has to
load itself instead of declaring in the project. The mode constants are `PayloadDlfcn.RtldLazy`
and `PayloadDlfcn.RtldNow`.

## Random bytes

```csharp
Span<byte> buffer = stackalloc byte[32];
int rc = PayloadRandom.GetRandomBytes(buffer);
```

`GetRandomBytes` fills the buffer through the system random library (`libSceRandom.sprx`); the
call is capped at 64 bytes. `GetRandomBytesFull` loops for larger requests until every byte is
filled.

## sysctl

`PayloadSysctl` wraps `sysctl` and `getmntinfo` for reading kernel-published state. Use it to
read a numeric sysctl node, or to list every mounted filesystem the host process can see.

## Hardware information

```csharp
int rc = PayloadHardwareInfo.GetModelName(out string model);
int rc = PayloadHardwareInfo.GetSerialNumber(out string serial);
int rc = PayloadHardwareInfo.GetCpuTemperature(out int celsius);
int rc = PayloadHardwareInfo.GetSocSensorTemperature(sensor: 0, out int celsius);
```

The hardware-info reads are behind `libkernel_sys.sprx`, so a payload that uses them sets
`<ProsperoKernelSprx>libkernel_sys.sprx</ProsperoKernelSprx>`.

## User service

```csharp
PayloadUserService.Initialize();
int rc = PayloadUserService.GetInitialUser(out int userId);
PayloadUserService.Terminate();
```

`PayloadUserService` is the read-side of the user-account service. Add
`<ProsperoSprx Include="libSceUserService.sprx" />` in the project.

## Browser

```csharp
int rc = PayloadBrowser.LaunchWebBrowser("https://example.local/"u8);
```

Launches the system browser at the URL. Add both `libSceSystemService.sprx` and
`libSceUserService.sprx` to the project.

## HTTP/2

```csharp
int rc = PayloadHttp2.InitStack("prospero"u8,
                                netPoolSize: 0x40000,
                                sslPoolSize: 0x40000,
                                httpPoolSize: 0x40000,
                                out int netMem,
                                out int sslCtx,
                                out int httpCtx);
// ... request setup, send, receive ...
PayloadHttp2.Cleanup(reqId: 0, tmplId: 0, httpCtx, sslCtx, netMem);
```

The HTTP/2 stack is bigger than the raw socket API but handles TLS through `libSceSsl`. Add both
`libSceHttp2.sprx` and `libSceSsl.sprx` in the project.

## Package install

```csharp
int rc = PayloadAppInstaller.InstallFromDirectory("PPSA99099\0"u8, "/user/app/\0"u8);
```

`InstallFromDirectory` hands a staged folder to the package installer for the named title id. The
title id and path are passed as null-terminated UTF-8. The installer takes over from there.

## Debug

`PayloadDebug` collects the CRT breadcrumb emitters, the `sp:*` string helpers, and small
diagnostic hooks. A production payload leaves them out; a diagnostic build (see
`-DiagnosticBreadcrumbs` in [Building a payload](payload-build.md)) uses them to walk the
bring-up sequence.

## Layout

Every wrapper class is a plain static class with `[LibraryImport]` interop declarations. The
source lives under `src/SharpProspero/Payload/`. A missing wrapper for a symbol the payload needs
is usually one of two things: add a `LibraryImport` in the payload's `Program.cs` (fast path), or
add the wrapper to the SDK (permanent path, with the matching SPRX declaration).
