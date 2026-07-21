// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Ui;
using System.Collections.Generic;
using Xunit;

namespace SharpProspero.Tests;

// While a panel is open it must be the only thing the controller can reach, so a confirmation cannot
// be walked past instead of answered.
public sealed class ModalHostTests
{
    private static readonly UiTheme Theme = UiTheme.Default;

    private static (ModalHost Host, Button Behind, Button Inside) Build()
    {
        var behind = new Button("behind");
        var inside = new Button("inside");
        var host = new ModalHost(new StackPanel().Add(behind));
        return (host, behind, inside);
    }

    // Focus order is what a screen walks, so it is the direct check for what is reachable.
    private static List<UiElement> Reachable(ModalHost host)
    {
        var found = new List<UiElement>();
        host.CollectFocusables(found);
        return found;
    }

    [Fact]
    public void StartsClosedWithTheContentReachable()
    {
        (ModalHost host, Button behind, _) = Build();
        Assert.False(host.IsOpen);
        Assert.Null(host.Modal);
        Assert.Contains(behind, Reachable(host));
    }

    [Fact]
    public void OpeningAPanelTakesOverTheController()
    {
        (ModalHost host, Button behind, Button inside) = Build();
        host.Show(new StackPanel().Add(inside));

        Assert.True(host.IsOpen);
        List<UiElement> reachable = Reachable(host);
        Assert.Contains(inside, reachable);
        Assert.DoesNotContain(behind, reachable);
    }

    [Fact]
    public void ClosingReturnsTheControllerToTheContent()
    {
        (ModalHost host, Button behind, Button inside) = Build();
        host.Show(new StackPanel().Add(inside));
        host.Close();

        Assert.False(host.IsOpen);
        List<UiElement> reachable = Reachable(host);
        Assert.Contains(behind, reachable);
        Assert.DoesNotContain(inside, reachable);
    }

    [Fact]
    public void ClosingAnnouncesItOnceAndIgnoresASecondClose()
    {
        (ModalHost host, _, Button inside) = Build();
        int closed = 0;
        host.Closed = () => closed++;

        host.Show(new StackPanel().Add(inside));
        host.Close();
        host.Close();

        Assert.Equal(1, closed);
    }

    [Fact]
    public void MeasureFollowsTheContent()
    {
        (ModalHost host, _, _) = Build();
        int contentHeight = new StackPanel().Add(new Button("behind")).Measure(400, Theme);
        Assert.Equal(contentHeight, host.Measure(400, Theme));
    }
}
