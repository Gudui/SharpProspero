// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Ui;
using System;
using Xunit;

namespace SharpProspero.Tests;

// The banner takes itself down, so what matters is that its time runs out exactly once and that it
// fades over the last moment rather than vanishing.
public sealed class ToastTests
{
    [Fact]
    public void NothingShowsUntilAMessageIsGiven()
    {
        var toast = new Toast();
        Assert.False(toast.IsVisible);
        Assert.Equal("", toast.Message);
        Assert.Equal(0f, toast.Opacity);
    }

    [Fact]
    public void AMessageShowsAndThenGoes()
    {
        var toast = new Toast();
        toast.Show("Saved", 1f);
        Assert.True(toast.IsVisible);
        Assert.Equal("Saved", toast.Message);

        toast.Update(0.5f);
        Assert.True(toast.IsVisible);

        toast.Update(0.6f);
        Assert.False(toast.IsVisible);
        Assert.Equal("", toast.Message);
    }

    [Fact]
    public void ItIsFullySolidBeforeTheFadeAndThinsOverIt()
    {
        var toast = new Toast();
        toast.Show("Copied", 2f);
        Assert.Equal(1f, toast.Opacity);

        // Well past the start but before the last moment.
        toast.Update(1.0f);
        Assert.Equal(1f, toast.Opacity);

        // Into the fade: still showing, but thinner.
        toast.Update(0.8f);
        Assert.True(toast.Opacity is > 0f and < 1f);
    }

    [Fact]
    public void ShowingAgainReplacesWhatWasUp()
    {
        var toast = new Toast();
        toast.Show("First", 5f);
        toast.Update(4f);
        toast.Show("Second", 5f);

        Assert.Equal("Second", toast.Message);
        Assert.Equal(1f, toast.Opacity);
    }

    [Fact]
    public void HideTakesItDownStraightAway()
    {
        var toast = new Toast();
        toast.Show("Working", 10f);
        toast.Hide();
        Assert.False(toast.IsVisible);
    }

    [Fact]
    public void TimeOnlyMovesForward()
    {
        var toast = new Toast();
        toast.Show("Steady", 1f);
        toast.Update(0f);
        toast.Update(-5f);
        Assert.True(toast.IsVisible);
    }

    [Fact]
    public void AMessageMustLastSomeTime()
    {
        var toast = new Toast();
        Assert.Throws<ArgumentOutOfRangeException>(() => toast.Show("x", 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => toast.Show("x", -1f));
    }
}
