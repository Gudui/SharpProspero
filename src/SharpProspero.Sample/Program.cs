// SharpProspero.Sample - a minimal on-device module built with the SharpProspero SDK.
// Copyright (C) 2026 SvenGDK
//
// Renders three centered text lines through the display path with a CPU-written framebuffer, the
// C# counterpart of the reference module. Press Options on the controller to exit.

using SharpProspero.Application;
using SharpProspero.Graphics;
using SharpProspero.Interop.Pad;

namespace SharpProspero.Sample;

internal sealed class SampleApp : ProsperoApp
{
    private static readonly Color Background = Color.FromRgb(0x0E, 0x11, 0x16);
    private static readonly Color Header = Color.FromRgb(0xFF, 0xFF, 0xFF);
    private static readonly Color Body = Color.FromRgb(0xC8, 0xD2, 0xDC);
    private static readonly Color Footer = Color.FromRgb(0x8A, 0x94, 0xA0);

    protected override void OnFrame(FrameContext context)
    {
        Surface surface = context.Surface;

        surface.Clear(Background);
        surface.DrawTextCentered("SharpProspero", 300, 7, Header);

        // The accent bar cycles its hue over time, paced by the frame clock.
        Color accent = Color.FromHsv((float)(context.TotalSeconds * 40 % 360), 0.7f, 0.9f);
        const int barWidth = 784;
        int barY = 300 + BitmapFont.GlyphSize * 7 + 20;
        surface.FillRect((surface.Width - barWidth) / 2, barY, barWidth, 4, accent);

        surface.DrawTextCentered("Welcome to the first C# homebrew!", 520, 3, Body);
        surface.DrawTextCentered("Copyright (C) 2026", 760, 3, Footer);

        // Leave on the frame Options is pressed.
        if (context.Pressed(ScePadButton.Options))
            context.RequestExit();
    }
}

internal static class Program
{
    private static void Main()
    {
        using (var app = new SampleApp())
            app.Run();

        // Returning from here is reported to the platform as a fault and the user is shown the
        // box that says the application closed unexpectedly, even when everything went as
        // intended. The process is ended through the C library instead.
        ProcessExit.Exit();
    }
}
