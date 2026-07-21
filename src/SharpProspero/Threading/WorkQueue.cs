// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System;
using System.Collections.Concurrent;
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
