---
title: Networking
nav_order: 12
---

# Networking

Beyond reading the connection status ([`NetworkInfo`](bindings.md)) and downloading over HTTP
([`HttpClient`](bindings.md)), the SDK exposes sockets, so a module can be a network client or a
server: a file transfer tool, a small web server, a remote-control listener, or a custom protocol.

Sockets live in `SharpProspero.Platform`. They are IPv4 and cover TCP and UDP, with a poller for
serving many connections from one thread and a resolver for connecting by host name.

## Addresses

A `SocketAddress` is an IPv4 endpoint: four address octets and a port.

```csharp
var any = SocketAddress.Any(8080);                 // every interface, port 8080
var local = SocketAddress.Loopback(9000);          // 127.0.0.1:9000
var server = SocketAddress.Parse("192.168.1.10", 21);
```

## A TCP client

Connect, send a request, read the reply, and dispose the connection.

```csharp
using var conn = TcpConnection.Connect(SocketAddress.Parse("192.168.1.10", 80));
conn.SendAll("GET / HTTP/1.0\r\n\r\n"u8);

Span<byte> buffer = stackalloc byte[2048];
int read = conn.Receive(buffer);                   // 0 means the peer closed the connection
```

`SendAll` repeats until every byte is accepted. `Send` sends once and reports how many bytes went, for
callers that manage their own buffering.

## A TCP server

Bind a listener, accept a client, serve it. This shape handles one client at a time.

```csharp
using var listener = TcpListener.Listen(SocketAddress.Any(8080));
while (running)
{
    using TcpConnection client = listener.Accept();
    Span<byte> request = stackalloc byte[1024];
    int read = client.Receive(request);
    client.SendAll(response);
}
```

## Serving many clients from one thread

To serve several connections at once without threads, set the sockets to non-blocking and drive them
from a `SocketPoller`. The poller reports which sockets are ready; register each with a token the
caller chooses, usually an index into its own table of connections.

```csharp
using var listener = TcpListener.Listen(SocketAddress.Any(8080));
listener.Blocking = false;

using var poller = SocketPoller.Create();
poller.Add(listener.Handle, PollEvents.Read, token: 0);

Span<PollReady> ready = stackalloc PollReady[32];
while (running)
{
    int count = poller.Wait(ready, timeoutMicroseconds: -1);   // -1 waits until something is ready
    for (int i = 0; i < count; i++)
    {
        if (ready[i].Token == 0)
        {
            TcpConnection client = listener.Accept();
            client.Blocking = false;
            // register client.Handle with its own token and keep it in a table
        }
        else if (ready[i].IsReadable)
        {
            // read from the connection the token maps to
        }
    }
}
```

`SocketPoller.Abort` unblocks a thread waiting in `Wait`, so a server loop can be told to stop.

## UDP

Datagrams need no connection. Bind to receive, send to an explicit destination.

```csharp
using var udp = UdpSocket.Bind(SocketAddress.Any(9000));
Span<byte> buffer = stackalloc byte[1500];
int read = udp.ReceiveFrom(buffer, out SocketAddress sender);
udp.SendTo(reply, sender);
```

## Connecting by host name

When the target is a name rather than an address, resolve it first. A `HostResolver` owns a small
network pool for its lifetime.

```csharp
using var dns = HostResolver.Create();
SocketAddress address = dns.Resolve("example.com", 80);
using var conn = TcpConnection.Connect(address);
```

## Errors

A failed socket call raises a `ProsperoException` whose `Code` carries the network error, so a caller
can branch on a specific failure such as a refused connection or a timeout.
