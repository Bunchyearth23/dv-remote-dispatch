using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace DvMod.RemoteDispatch
{
    public class Updater : MonoBehaviour
    {
        private const float TelemetryIntervalSeconds = 0.2f;
        private const float TrainsetTelemetryIntervalSeconds = 0.5f;
        private const float IdleIntervalSeconds = 1.0f;
        private const double MainThreadBudgetMilliseconds = 1.5d;
        private const double MainThreadWorkWarningMilliseconds = 4d;
        private const int MaximumQueuedWork = 512;
        private static readonly WaitForSecondsRealtime idleInterval = new WaitForSecondsRealtime(IdleIntervalSeconds);
        private static readonly WaitForSecondsRealtime telemetryInterval = new WaitForSecondsRealtime(TelemetryIntervalSeconds);
        private static readonly WaitForSecondsRealtime trainsetTelemetryInterval = new WaitForSecondsRealtime(TrainsetTelemetryIntervalSeconds);

        public void Start()
        {
            StartCoroutine(CheckPlayerTransformCoro());
            StartCoroutine(CheckTrainsetsCoro());
            StartCoroutine(DeferredEventsCoro());
        }

        private static GameObject? rootObject;

        private static readonly object lifetimeGate = new object();
        private static CancellationTokenSource lifetime = new CancellationTokenSource();
        private static bool running;
        private static readonly Queue<IEnumerator> captures = new Queue<IEnumerator>();
        public static CancellationToken Lifetime { get { lock (lifetimeGate) return running ? lifetime.Token : new CancellationToken(true); } }

        // Captures share the dispatcher budget instead of each owning a Unity coroutine.
        public static void RunCoroutine(IEnumerator routine)
        {
            if (!running) throw new OperationCanceledException("Unity world is stopping.");
            if (captures.Count >= 128) throw new InvalidOperationException("Too many Unity captures; retry shortly.");
            captures.Enqueue(routine);
        }

        public static void Create()
        {
            if (rootObject == null)
            {
                rootObject = new GameObject();
                GameObject.DontDestroyOnLoad(rootObject);
                rootObject.AddComponent<Updater>();
                lock (lifetimeGate) { lifetime = new CancellationTokenSource(); running = true; }
            }
        }

        public static void Destroy()
        {
            lock (lifetimeGate)
            {
                running = false;
                lifetime.Cancel();
                while (taskQueue.TryDequeue(out var pending)) pending.cancel();
            }
            while (captures.Count > 0) DisposeCapture(captures.Dequeue());
            if (rootObject != null)
            {
                GameObject.DestroyImmediate(rootObject);
                rootObject = null;
            }
        }

        private IEnumerator CheckPlayerTransformCoro()
        {
            while (true)
            {
                if (!Sessions.HasActiveSessions())
                {
                    yield return idleInterval;
                    continue;
                }
                yield return telemetryInterval;
                if (!Sessions.HasActiveSessions()) continue;
                PlayerData.CheckTransform();
            }
        }

        private IEnumerator CheckTrainsetsCoro()
        {
            var moving = new HashSet<int>();
            var cursor = 0;
            while (true)
            {
                if (!Sessions.HasActiveSessions())
                {
                    moving.Clear();
                    cursor = 0;
                    yield return idleInterval;
                    continue;
                }
                var trainsets = Trainset.allSets;
                if (trainsets == null || trainsets.Count == 0)
                {
                    cursor = 0;
                    yield return trainsetTelemetryInterval;
                    continue;
                }
                // A large consist list is spread across frames.  A single web
                // refresh must never turn into one long Unity callback.
                var end = Math.Min(trainsets.Count, cursor + 16);
                for (; cursor < end; cursor++)
                {
                    var trainset = trainsets[cursor];
                    if (trainset == null) continue;
                    if (trainset.firstCar != null && !trainset.firstCar.isStationary)
                    {
                        moving.Add(trainset.id);
                        CarUpdater.MarkTrainsetAsDirty(trainset, true);
                    }
                    else if (moving.Remove(trainset.id))
                        CarUpdater.MarkTrainsetAsDirty(trainset, true); // Publish the final stopping position too.
                }
                if (cursor >= trainsets.Count)
                {
                    cursor = 0;
                    yield return trainsetTelemetryInterval;
                }
                else yield return null;
            }
        }

        private IEnumerator DeferredEventsCoro()
        {
            var frameBudget = new System.Diagnostics.Stopwatch();
            var preferCapture = false;
            while (true)
            {
                frameBudget.Restart();
                var remainingCaptures = captures.Count;
                while (frameBudget.Elapsed.TotalMilliseconds < MainThreadBudgetMilliseconds)
                {
                    // Alternate across frames too, so neither work class starves.
                    if (remainingCaptures > 0 && captures.Count > 0 && (preferCapture || taskQueue.IsEmpty))
                    {
                        remainingCaptures--;
                        preferCapture = false;
                        var capture = captures.Dequeue();
                        try
                        {
                            if (capture.MoveNext()) captures.Enqueue(capture);
                            else DisposeCapture(capture);
                        }
                        catch (Exception error)
                        {
                            DisposeCapture(capture);
                            Main.DebugLog(() => "Unity capture failed: " + error.Message);
                        }
                    }
                    else if (taskQueue.TryDequeue(out var work))
                    {
                        preferCapture = true;
                        var workStarted = frameBudget.Elapsed;
                        work.execute();
                        var workElapsed = frameBudget.Elapsed - workStarted;
                        if (workElapsed.TotalMilliseconds >= MainThreadWorkWarningMilliseconds)
                            Main.DebugLog(() => $"Unity main-thread work took {workElapsed.TotalMilliseconds:F2} ms.");
                    }
                    else break;
                }
                yield return null;
            }
        }

        private static void DisposeCapture(IEnumerator capture)
        {
            try { (capture as IDisposable)?.Dispose(); }
            catch (Exception error) { Main.DebugLog(() => "Unity capture disposal failed: " + error.Message); }
        }

        private sealed class QueuedWork
        {
            public readonly Action execute;
            public readonly Action cancel;
            public QueuedWork(Action execute, Action cancel) { this.execute = execute; this.cancel = cancel; }
        }

        private static readonly ConcurrentQueue<QueuedWork> taskQueue = new ConcurrentQueue<QueuedWork>();
        private static int queuedWorkCount;

        public static Task RunOnMainThread(Action action)
        {
            return RunOnMainThread(() => { action(); return true; });
        }

        public static Task<T> RunOnMainThread<T>(Func<T> func)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            var admittedAt = System.Diagnostics.Stopwatch.GetTimestamp();
            lock (lifetimeGate)
            {
                if (!running) { tcs.SetCanceled(); return tcs.Task; }
                if (Interlocked.Increment(ref queuedWorkCount) > MaximumQueuedWork)
                {
                    Interlocked.Decrement(ref queuedWorkCount);
                    tcs.SetException(new InvalidOperationException("Unity main-thread queue is busy; retry after the current frame."));
                    return tcs.Task;
                }
                taskQueue.Enqueue(new QueuedWork(() =>
                {
                    Interlocked.Decrement(ref queuedWorkCount);
                    try
                    {
                        if ((System.Diagnostics.Stopwatch.GetTimestamp() - admittedAt) / (double)System.Diagnostics.Stopwatch.Frequency > 10d)
                            throw new TimeoutException("Unity main-thread request expired before execution; retry.");
                        tcs.TrySetResult(func());
                    }
                    catch (Exception e)
                    {
                        tcs.TrySetException(e);
                    }
                }, () => { Interlocked.Decrement(ref queuedWorkCount); tcs.TrySetCanceled(); }));
                return tcs.Task;
            }
        }
    }
}
