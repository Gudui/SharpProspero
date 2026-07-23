// A SharpProspero payload that runs once. A loader maps it into a running process and starts it; it
// connects out to a machine you run, sends a short report, and returns - a payload ends when Main
// returns. Set Host and Port to a machine listening for the line (for example, `nc -l 9060`). Replace
// Report with whatever the one-shot action should send back.

using System;
using System.Text;
using SharpProspero.Platform;

namespace SampleApp;

internal static class Program
{
    // The machine that receives the report. Change these to your listener.
    private const string Host = "192.168.1.10";
    private const int Port = 9060;

    private static void Main()
    {
        try
        {
            SocketAddress address = SocketAddress.Parse(Host, Port);
            using TcpConnection connection = TcpConnection.Connect(address);
            connection.SendAll(Encoding.UTF8.GetBytes(Report()));
            connection.Shutdown();
        }
        catch (Exception)
        {
            // Nothing is listening, or the send failed. A one-shot payload returns either way.
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
