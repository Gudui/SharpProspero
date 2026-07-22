// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System;
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace SharpProspero.Threading;

/// <summary>
/// Runs jobs on a small pool of background threads, so slow work — reading a file, decoding an image,
/// a network request — happens off the frame loop and the screen keeps drawing. Hand it an action with
/// <see cref="Enqueue"/>; a worker picks it up. Dispose it at shutdown to finish the queued jobs and stop
/// the threads.
/// </summary>
/// <remarks>
/// A job runs on a worker thread, so anything it touches that the main thread also touches must be
/// guarded (a <c>lock</c>, an <see cref="Interlocked"/> counter). An exception a job throws is passed to
/// <see cref="ErrorHandler"/> if one is set, and otherwise swallowed so one bad job does not stop the
/// worker.
/// </remarks>
public sealed class WorkQueue : IDisposable
{
    private readonly BlockingCollection<Action> _jobs = new();
    private readonly Thread[] _workers;
    private readonly object _gate = new();
    private bool _disposed;

    /// <summary>Creates a queue served by <paramref name="workerCount"/> background threads.</summary>
    /// <param name="workerCount">How many worker threads run jobs at once.</param>
    /// <param name="name">A base name for the threads, or null to leave them unnamed.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="workerCount"/> is not positive.</exception>
    public WorkQueue(int workerCount = 2, string? name = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workerCount);
        _workers = new Thread[workerCount];
        for (int i = 0; i < workerCount; i++)
        {
            var thread = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = name is null ? null : $"{name} #{i}",
            };
            _workers[i] = thread;
            thread.Start();
        }
    }

    /// <summary>How many worker threads serve the queue.</summary>
    public int WorkerCount => _workers.Length;

    /// <summary>How many jobs are waiting to start.</summary>
    public int PendingCount => _jobs.Count;

    /// <summary>Called on a worker thread with the exception a job threw, or null to swallow it.</summary>
    public Action<Exception>? ErrorHandler { get; set; }

    /// <summary>Adds <paramref name="job"/> to the queue for a worker to run.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="job"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">The queue has been disposed.</exception>
    public void Enqueue(Action job)
    {
        ArgumentNullException.ThrowIfNull(job);
        // The check and the add are one step under the lock so a concurrent Dispose cannot slip between
        // them and turn the add into an InvalidOperationException the caller cannot anticipate.
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _jobs.Add(job);
        }
    }

    /// <summary>
    /// Adds a result-producing <paramref name="job"/> and returns a handle the frame loop can poll for
    /// the result. This runs on the shared pool rather than a thread of its own, so it is the pooled
    /// counterpart to <see cref="BackgroundOperation{T}"/> when many results are produced over a run.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="job"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">The queue has been disposed.</exception>
    public WorkItem<T> Enqueue<T>(Func<T> job)
    {
        ArgumentNullException.ThrowIfNull(job);
        var item = new WorkItem<T>();
        Enqueue(() => item.Run(job));
        return item;
    }

    /// <summary>Stops taking new jobs, waits for the queued and running ones to finish, and stops the threads.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _jobs.CompleteAdding();
        }

        // Join and dispose outside the lock: the workers never take it, and holding it across a Join is
        // needless. CompleteAdding already ends the consuming loops once the queue drains.
        foreach (Thread worker in _workers)
            worker.Join();
        _jobs.Dispose();
    }

    private void WorkerLoop()
    {
        foreach (Action job in _jobs.GetConsumingEnumerable())
        {
            try
            {
                job();
            }
            catch (Exception e)
            {
                // The handler is the last-resort error sink; a throw from it must not take the worker
                // thread down and leave the remaining jobs unconsumed.
                try { ErrorHandler?.Invoke(e); }
                catch { }
            }
        }
    }
}

/// <summary>
/// A handle to a result-producing job running on a <see cref="WorkQueue"/>. Poll <see cref="IsComplete"/>
/// from the frame loop, then read <see cref="Result"/>; reading the result before it is ready waits for
/// it, and if the job threw, reading the result throws that same exception.
/// </summary>
/// <typeparam name="T">The type of result the job produces.</typeparam>
public sealed class WorkItem<T>
{
    private readonly ManualResetEventSlim _done = new(false);
    private T? _result;
    private Exception? _error;
    private volatile bool _complete;

    /// <summary>Whether the job has finished (whether it succeeded or threw).</summary>
    public bool IsComplete => _complete;

    /// <summary>Whether the job finished by throwing.</summary>
    public bool Failed => _error is not null;

    /// <summary>The exception the job threw, or null.</summary>
    public Exception? Error => _error;

    /// <summary>The result, waiting for the job if it has not finished. Rethrows the exception the job threw.</summary>
    public T Result
    {
        get
        {
            _done.Wait();
            if (_error is not null)
                ExceptionDispatchInfo.Throw(_error);
            return _result!;
        }
    }

    /// <summary>Waits for the job to finish.</summary>
    public void Wait() => _done.Wait();

    /// <summary>Waits up to <paramref name="timeout"/> for the job to finish; returns whether it did.</summary>
    public bool Wait(TimeSpan timeout) => _done.Wait(timeout);

    // Run by a worker thread: capture the result or the exception, then release anyone waiting.
    internal void Run(Func<T> work)
    {
        try
        {
            _result = work();
        }
        catch (Exception e)
        {
            _error = e;
        }
        finally
        {
            _complete = true;
            _done.Set();
        }
    }
}
