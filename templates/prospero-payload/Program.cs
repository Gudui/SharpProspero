// A SharpProspero payload: a headless network service a loader maps into a running process and starts.
// It listens on a port, greets each connection, echoes what it receives, and keeps serving. A payload
// has no screen and no controller - it runs inside another process - so this is a plain program, not a
// frame loop. Replace the handling in Serve with your own protocol.
//
// A payload reaches the network through SharpProspero.Payload.PayloadNetwork, the plain socket calls the
// operating-system library publishes by name. A payload has no dynamic linker, so the wrapped network
// types an application module uses do not resolve in one.

using System;
using System.Text;
using SharpProspero.Payload;

namespace SampleApp;

internal static class Program
{
    private const ushort Port = 9025;

    private static int Main()
    {
        int listener;
        try
        {
            listener = PayloadNetwork.Listen(Port);
        }
        catch (Exception)
        {
            // The port could not be opened; a payload ends by returning.
            return -1;
        }

        while (true)
        {
            int client = PayloadNetwork.Accept(listener);
            if (client < 0)
                continue;
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
                PayloadNetwork.Close(client);
            }
        }
    }

    private static void Serve(int client)
    {
        PayloadNetwork.SendAll(client, "SharpProspero payload ready\n"u8);
        Span<byte> buffer = stackalloc byte[512];
        while (true)
        {
            long read = PayloadNetwork.Receive(client, buffer);
            if (read <= 0)
                break;
            if (!PayloadNetwork.SendAll(client, buffer[..(int)read])) // echo the bytes back
                break;
        }
    }
}
