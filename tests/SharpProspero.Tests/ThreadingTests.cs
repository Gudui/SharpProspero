// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Threading;
using System;
using System.Threading;
using Xunit;

namespace SharpProspero.Tests;

public sealed class ThreadingTests
{
    [Fact]
    public void WorkQueue_RunsEveryJob()
    {
        int counter = 0;
        var queue = new WorkQueue(workerCount: 4);
        for (int i = 0; i < 200; i++)
            queue.Enqueue(() => Interlocked.Increment(ref counter));
        queue.Dispose(); // waits for the queued jobs to finish
        Assert.Equal(200, counter);
    }

    [Fact]
    public void WorkQueue_ReportsJobErrorsAndRejectsAfterDispose()
    {
        Exception? captured = null;
        using var done = new ManualResetEventSlim();
        var queue = new WorkQueue(1) { ErrorHandler = e => { captured = e; done.Set(); } };
        queue.Enqueue(() => throw new InvalidOperationException("boom"));
        Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
        queue.Dispose();

        Assert.IsType<InvalidOperationException>(captured);
        Assert.Throws<ObjectDisposedException>(() => queue.Enqueue(() => { }));
    }

    [Fact]
    public void BackgroundOperation_Void_CompletesAndReportsSuccess()
    {
        int flag = 0;
        var operation = new BackgroundOperation(() => Interlocked.Exchange(ref flag, 1));
        operation.Wait();
        Assert.True(operation.IsComplete);
        Assert.False(operation.Failed);
        Assert.Equal(1, flag);
    }

    [Fact]
    public void BackgroundOperation_Result_ReturnsTheValue()
    {
        var operation = new BackgroundOperation<int>(() =>
        {
            Thread.Sleep(10);
            return 42;
        });
        Assert.Equal(42, operation.Result); // waits for the work
        Assert.True(operation.IsComplete);
    }

    [Fact]
    public void BackgroundOperation_CapturesAndRethrowsAnError()
    {
        var operation = new BackgroundOperation<int>(() => throw new InvalidOperationException("no"));
        operation.Wait();
        Assert.True(operation.Failed);
        Assert.IsType<InvalidOperationException>(operation.Error);
        Assert.Throws<InvalidOperationException>(() => _ = operation.Result); // reading the result rethrows
    }

    [Fact]
    public void WorkQueue_ThrowingErrorHandlerDoesNotKillTheWorker()
    {
        int done = 0;
        using var finished = new ManualResetEventSlim();
        // A single worker: if a throwing handler took it down, the follow-up job would never run.
        var queue = new WorkQueue(1) { ErrorHandler = _ => throw new InvalidOperationException("handler") };
        queue.Enqueue(() => throw new InvalidOperationException("job"));
        queue.Enqueue(() => { Interlocked.Exchange(ref done, 1); finished.Set(); });

        Assert.True(finished.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, done);
        queue.Dispose();
    }

    [Fact]
    public void Dispatcher_RunsPostedWorkOnTheCallingThread()
    {
        var dispatcher = new Dispatcher();
        int value = 0;
        dispatcher.Post(() => value = 7);
        dispatcher.Post(() => value += 1);
        Assert.Equal(2, dispatcher.PendingCount);

        int ran = dispatcher.RunPending();
        Assert.Equal(2, ran);
        Assert.Equal(8, value);
        Assert.Equal(0, dispatcher.PendingCount);
    }

    [Fact]
    public void Dispatcher_WorkPostedByACallbackRunsOnTheNextPass()
    {
        var dispatcher = new Dispatcher();
        int reentrant = 0;
        dispatcher.Post(() => dispatcher.Post(() => reentrant = 1));

        Assert.Equal(1, dispatcher.RunPending()); // ran the outer callback only
        Assert.Equal(0, reentrant);               // the re-post did not run this pass
        Assert.Equal(1, dispatcher.PendingCount);

        Assert.Equal(1, dispatcher.RunPending()); // now the re-posted callback runs
        Assert.Equal(1, reentrant);
    }

    [Fact]
    public void Dispatcher_ErrorHandlerCatchesAndRemainingWorkRuns()
    {
        Exception? captured = null;
        var dispatcher = new Dispatcher { ErrorHandler = e => captured = e };
        int after = 0;
        dispatcher.Post(() => throw new InvalidOperationException("boom"));
        dispatcher.Post(() => after = 1);

        Assert.Equal(2, dispatcher.RunPending());
        Assert.IsType<InvalidOperationException>(captured);
        Assert.Equal(1, after); // the throwing callback did not stop the rest
    }

    [Fact]
    public void Dispatcher_ClearDropsPendingWork()
    {
        var dispatcher = new Dispatcher();
        int ran = 0;
        dispatcher.Post(() => ran = 1);
        dispatcher.Clear();
        Assert.Equal(0, dispatcher.PendingCount);
        Assert.Equal(0, dispatcher.RunPending());
        Assert.Equal(0, ran);
    }
}
