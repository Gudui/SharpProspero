// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Application;
using System;
using Xunit;

namespace SharpProspero.Tests;

public sealed class EventHubTests
{
    private readonly record struct Ping(int Value);
    private readonly record struct Pong(string Text);

    [Fact]
    public void Publish_DeliversToEverySubscriberInOrder()
    {
        var hub = new EventHub();
        int sum = 0;
        hub.Subscribe<Ping>(p => sum += p.Value);
        hub.Subscribe<Ping>(p => sum += p.Value * 10);

        hub.Publish(new Ping(3));

        Assert.Equal(33, sum);
        Assert.Equal(2, hub.SubscriberCount<Ping>());
    }

    [Fact]
    public void MessageTypesAreDeliveredIndependently()
    {
        var hub = new EventHub();
        int pings = 0, pongs = 0;
        hub.Subscribe<Ping>(_ => pings++);
        hub.Subscribe<Pong>(_ => pongs++);

        hub.Publish(new Ping(1));

        Assert.Equal(1, pings);
        Assert.Equal(0, pongs);
    }

    [Fact]
    public void DisposingASubscriptionStopsDelivery()
    {
        var hub = new EventHub();
        int count = 0;
        IDisposable subscription = hub.Subscribe<Ping>(_ => count++);

        hub.Publish(new Ping(1));
        subscription.Dispose();
        hub.Publish(new Ping(1));

        Assert.Equal(1, count);
        Assert.Equal(0, hub.SubscriberCount<Ping>());
        subscription.Dispose(); // disposing again is harmless
    }

    [Fact]
    public void Publish_WithNoSubscribers_DoesNothing()
    {
        var hub = new EventHub();
        hub.Publish(new Ping(1)); // must not throw
        Assert.Equal(0, hub.SubscriberCount<Ping>());
    }

    [Fact]
    public void UnsubscribingDuringDelivery_DoesNotDisturbTheMessageInFlight()
    {
        var hub = new EventHub();
        int a = 0, b = 0;
        IDisposable? second = null;
        hub.Subscribe<Ping>(_ => { a++; second!.Dispose(); }); // the first handler removes the second
        second = hub.Subscribe<Ping>(_ => b++);

        hub.Publish(new Ping(1)); // snapshot taken first, so both run this round
        Assert.Equal(1, a);
        Assert.Equal(1, b);

        hub.Publish(new Ping(1)); // the second is gone now
        Assert.Equal(2, a);
        Assert.Equal(1, b);
    }

    [Fact]
    public void DisposingATokenAfterClearDoesNotRemoveALaterSubscription()
    {
        var hub = new EventHub();
        int count = 0;
        void Handler(Ping _) => count++;

        IDisposable stale = hub.Subscribe<Ping>(Handler);
        hub.Clear();
        IDisposable live = hub.Subscribe<Ping>(Handler); // same handler, a new registration

        stale.Dispose(); // must not touch the live registration

        hub.Publish(new Ping(1));
        Assert.Equal(1, count);
        Assert.Equal(1, hub.SubscriberCount<Ping>());
        live.Dispose();
    }

    [Fact]
    public void Clear_RemovesEverySubscription()
    {
        var hub = new EventHub();
        hub.Subscribe<Ping>(_ => { });
        hub.Subscribe<Pong>(_ => { });
        hub.Clear();
        Assert.Equal(0, hub.SubscriberCount<Ping>());
        Assert.Equal(0, hub.SubscriberCount<Pong>());
    }

    [Fact]
    public void Subscribe_RejectsANullHandler()
    {
        var hub = new EventHub();
        Assert.Throws<ArgumentNullException>(() => hub.Subscribe<Ping>(null!));
    }
}
