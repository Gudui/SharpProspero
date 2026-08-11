// A SharpProspero payload that runs once. A loader maps it into a running process and starts it; it
// connects out to a machine you run, sends a short report, and returns - a payload ends when Main
// returns. Set the four address bytes and Port to a machine listening for the line (for example,
// `nc -l 9060`). Replace Report with whatever the one-shot action should send back.
//
// A payload reaches the network through SharpProspero.Payload.PayloadNetwork, the plain socket calls the
// operating-system library publishes by name, because a payload has no dynamic linker to bind the
// wrapped network types an application module uses.

using System;
using System.Text;
using SharpProspero.Payload;

namespace SampleApp;

internal static class Program
{
    // The machine that receives the report, as four address bytes and a port. Change these to your
    // listener - for 192.168.1.10 the bytes are 192, 168, 1, 10.
    private const byte A = 192, B = 168, C = 1, D = 10;
    private const ushort Port = 9060;

    private static int Main()
    {
        int connection = -1;
        try
        {
            connection = PayloadNetwork.Connect(A, B, C, D, Port);
            PayloadNetwork.SendAll(connection, Encoding.UTF8.GetBytes(Report()));
            return 0;
        }
        catch (Exception)
        {
            // Nothing is listening, or the send failed. A one-shot payload returns either way.
            return -1;
        }
        finally
        {
            if (connection >= 0)
                PayloadNetwork.Close(connection);
        }
    }

    private static string Report()
    {
        var text = new StringBuilder();
        text.Append("SharpProspero payload reporting in\n");
        text.Append($"processor count: {Environment.ProcessorCount}\n");
        text.Append($"milliseconds since the host started: {Environment.TickCount64}\n");
        return text.ToString();
    }
}
