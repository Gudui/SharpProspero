// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Application;
using System;
using System.Collections.Generic;
using Xunit;

namespace SharpProspero.Tests;

public sealed class StateMachineTests
{
    private enum S { A, B, C }

    [Fact]
    public void Start_EntersTheInitialState()
    {
        var log = new List<string>();
        var machine = new StateMachine<S>()
            .Configure(S.A, onEnter: () => log.Add("enterA"))
            .Configure(S.B, onEnter: () => log.Add("enterB"));

        machine.Start(S.A);

        Assert.True(machine.IsRunning);
        Assert.Equal(S.A, machine.Current);
        Assert.Equal(["enterA"], log);
    }

    [Fact]
    public void Update_RunsTheActiveStateAndDoesNothingBeforeStart()
    {
        double total = 0;
        var machine = new StateMachine<S>().Configure(S.A, onUpdate: dt => total += dt);

        machine.Update(0.5); // before Start: ignored
        Assert.Equal(0, total);

        machine.Start(S.A);
        machine.Update(0.5);
        machine.Update(0.25);
        Assert.Equal(0.75, total, 5);
    }

    [Fact]
    public void TransitionTo_RunsExitThenEnterAndRaisesTheEvent()
    {
        var log = new List<string>();
        var machine = new StateMachine<S>()
            .Configure(S.A, onExit: () => log.Add("exitA"))
            .Configure(S.B, onEnter: () => log.Add("enterB"));
        (S From, S To) fired = default;
        machine.Transitioned += (from, to) => fired = (from, to);

        machine.Start(S.A);
        machine.TransitionTo(S.B);

        Assert.Equal(S.B, machine.Current);
        Assert.Equal(["exitA", "enterB"], log);
        Assert.Equal((S.A, S.B), fired);
    }

    [Fact]
    public void TransitionTo_TheActiveState_IsANoOp()
    {
        int enters = 0, exits = 0;
        var machine = new StateMachine<S>().Configure(S.A, onEnter: () => enters++, onExit: () => exits++);

        machine.Start(S.A);
        Assert.Equal(1, enters);

        machine.TransitionTo(S.A);
        Assert.Equal(1, enters); // not re-entered
        Assert.Equal(0, exits);  // not exited
    }

    [Fact]
    public void TransitionTo_FromInsideAnUpdate_Works()
    {
        var log = new List<string>();
        StateMachine<S> machine = null!;
        machine = new StateMachine<S>()
            .Configure(S.A, onUpdate: _ => machine.TransitionTo(S.B), onExit: () => log.Add("exitA"))
            .Configure(S.B, onEnter: () => log.Add("enterB"));

        machine.Start(S.A);
        machine.Update(0.1);

        Assert.Equal(S.B, machine.Current);
        Assert.Equal(["exitA", "enterB"], log);
    }

    [Fact]
    public void TransitionTo_FromInsideAnExitCallback_IsRefusedAndLeavesTheStateIntact()
    {
        var machine = new StateMachine<S>();
        machine.Configure(S.A, onExit: () => machine.TransitionTo(S.C));
        machine.Configure(S.B);
        machine.Configure(S.C);
        machine.Start(S.A);

        // Leaving A runs its exit, which tries to start a second transition: that must be refused rather
        // than clobber the outer one and leave the machine half-moved.
        Assert.Throws<InvalidOperationException>(() => machine.TransitionTo(S.B));
        Assert.Equal(S.A, machine.Current);
    }

    [Fact]
    public void Guards_RejectMisuse()
    {
        var machine = new StateMachine<S>().Configure(S.A);

        Assert.Throws<InvalidOperationException>(() => _ = machine.Current);      // before Start
        Assert.Throws<InvalidOperationException>(() => machine.TransitionTo(S.A)); // before Start
        Assert.Throws<ArgumentException>(() => machine.Start(S.C));               // not configured

        machine.Start(S.A);
        Assert.Throws<InvalidOperationException>(() => machine.Start(S.A));       // already started
        Assert.Throws<ArgumentException>(() => machine.TransitionTo(S.C));        // not configured
    }
}
