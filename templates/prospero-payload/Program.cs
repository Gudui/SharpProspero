// A SharpProspero payload: a headless network service a loader maps into a running process and starts.
// It listens on a port, greets each connection, echoes what it receives, and keeps serving. A payload
// has no screen and no controller - it runs inside another process - so this is a plain program, not a
// frame loop. Replace the handling in Serve with your own protocol.

using System;
using System.Text;
using SharpProspero.Platform;

namespace SampleApp;

internal static class Program
{
    private const int Port = 9025;

    private static void Main()
    {
        using var listener = TcpListener.Listen(SocketAddress.Any(Port));
        while (true)
        {
            TcpConnection client = listener.Accept();
            try
            {
                Serve(client);
            }
            catch (Exception)
            {
                // Drop a client that failed and keep serving the next one.
            }
            finally
            {
                client.Dispose();
            }
        }
    }

    private static void Serve(TcpConnection client)
    {
        client.SendAll(Encoding.ASCII.GetBytes("SharpProspero payload ready\n"));
        Span<byte> buffer = stackalloc byte[512];
        while (true)
        {
            int read = client.Receive(buffer);
            if (read <= 0)
                break;
            client.SendAll(buffer[..read]); // echo the bytes back
        }
    }
}
