// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Application;
using System;
using System.Collections.Generic;
using Xunit;

namespace SharpProspero.Tests;

public sealed class CommandStackTests
{
    [Fact]
    public void Execute_DoesTheCommandAndUndoRedoWalkTheHistory()
    {
        var log = new List<int>();
        var stack = new CommandStack();

        stack.Execute(() => log.Add(1), () => log.Remove(1));
        stack.Execute(() => log.Add(2), () => log.Remove(2));
        Assert.Equal([1, 2], log);
        Assert.Equal(2, stack.UndoCount);

        Assert.True(stack.Undo());
        Assert.Equal([1], log);
        Assert.True(stack.Undo());
        Assert.Empty(log);
        Assert.False(stack.Undo());

        Assert.True(stack.Redo());
        Assert.Equal([1], log);
        Assert.True(stack.Redo());
        Assert.Equal([1, 2], log);
        Assert.False(stack.Redo());
    }

    [Fact]
    public void ExecutingAfterAnUndo_DiscardsTheRedoBranch()
    {
        int value = 0;
        var stack = new CommandStack();
        stack.Execute(() => value = 1, () => value = 0);
        stack.Undo();
        Assert.True(stack.CanRedo);

        stack.Execute(() => value = 5, () => value = 0);
        Assert.False(stack.CanRedo); // the old redo is gone
        Assert.Equal(5, value);
    }

    [Fact]
    public void Limit_DropsTheOldestUndoSteps()
    {
        var stack = new CommandStack { Limit = 2 };
        for (int i = 0; i < 5; i++)
            stack.Execute(() => { }, () => { });
        Assert.Equal(2, stack.UndoCount);
    }

    [Fact]
    public void Execute_CoalescesConsecutiveCommandsThatOptIn()
    {
        var text = new System.Text.StringBuilder();
        var stack = new CommandStack();

        stack.Execute(new TypeCommand(text, 'a'));
        stack.Execute(new TypeCommand(text, 'b'));
        stack.Execute(new TypeCommand(text, 'c'));
        Assert.Equal("abc", text.ToString());
        Assert.Equal(1, stack.UndoCount); // the three typed characters folded into one step

        Assert.True(stack.Undo());
        Assert.Equal(string.Empty, text.ToString()); // undo removes all three at once
    }

    [Fact]
    public void ThrowingUndo_LeavesTheHistoryIntact()
    {
        var stack = new CommandStack();
        stack.Execute(new ThrowingUndoCommand());

        Assert.Throws<InvalidOperationException>(() => stack.Undo());
        Assert.Equal(1, stack.UndoCount); // the step is not lost
        Assert.Equal(0, stack.RedoCount);
    }

    [Fact]
    public void LoweringLimit_AlsoTrimsWhenRedoing()
    {
        var stack = new CommandStack();
        for (int i = 0; i < 4; i++)
            stack.Execute(() => { }, () => { });
        for (int i = 0; i < 4; i++)
            stack.Undo(); // now four steps on the redo branch

        stack.Limit = 2;
        stack.Redo();
        stack.Redo();
        stack.Redo();
        Assert.Equal(2, stack.UndoCount); // redo trims to the limit, not just Execute
    }

    // A command whose Undo throws, to check the history survives a failed undo.
    private sealed class ThrowingUndoCommand : ICommand
    {
        public void Do()
        {
        }

        public void Undo() => throw new InvalidOperationException();
    }

    // A command that appends a character and coalesces with later typing into one undo step.
    private sealed class TypeCommand(System.Text.StringBuilder target, char c) : ICoalescingCommand
    {
        private int _count = 1;

        public void Do() => target.Append(c);

        public void Undo() => target.Remove(target.Length - _count, _count);

        public bool TryCoalesceWith(ICommand next)
        {
            if (next is TypeCommand)
            {
                _count++;
                return true;
            }

            return false;
        }
    }
}
