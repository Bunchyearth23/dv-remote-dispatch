using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DvMod.RemoteDispatch;

static class Program
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static IEnumerator Dispatcher() => (IEnumerator)typeof(Updater).GetMethod("DeferredEventsCoro", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(new Updater(), null)!;
    static IEnumerator Capture(TaskCompletionSource<int> result, Action step, Action disposed)
    {
        try { step(); yield return null; step(); result.TrySetResult(2); }
        finally { disposed(); }
    }
    static IEnumerator Fail(TaskCompletionSource<int> result) { yield return null; throw new InvalidOperationException("late-failure"); }
    static void PumpUntil(IEnumerator dispatcher, Func<bool> done)
    {
        var timeout = Stopwatch.StartNew();
        while (!done() && timeout.ElapsedMilliseconds < 3000) { dispatcher.MoveNext(); Thread.Yield(); }
        Check(done(), "operation failed to terminate");
    }
    static void Main()
    {
        Updater.Create();
        var dispatcher = Dispatcher();
        int steps = 0, disposed = 0;
        var result = UnityCapture.Run<int>(t => Capture(t, () => steps++, () => disposed++));
        PumpUntil(dispatcher, () => steps == 1);
        Check(!result.IsCompleted, "a capture must yield between units");
        PumpUntil(dispatcher, () => result.IsCompleted);
        dispatcher.MoveNext();
        Check(result.GetAwaiter().GetResult() == 2 && disposed == 1, "successful capture completes and disposes once");
        var failure = UnityCapture.Run<int>(Fail);
        PumpUntil(dispatcher, () => failure.IsCompleted);
        Check(failure.IsFaulted && failure.Exception!.InnerException!.Message == "late-failure", "late coroutine exception reaches caller");

        int fairSteps = 0;
        var fairness = UnityCapture.Run<int>(t => Capture(t, () => fairSteps++, () => { }));
        for (int i = 0; i < 10; i++) _ = Updater.RunOnMainThread(() => { var clock = Stopwatch.StartNew(); while (clock.ElapsedMilliseconds < 3) Thread.SpinWait(20); });
        for (int i = 0; i < 5; i++) dispatcher.MoveNext();
        Check(fairSteps > 0, "long callbacks must not starve captures across frames");
        Updater.Destroy();

        Updater.Create(); dispatcher = Dispatcher();
        int abandonedSteps = 0, abandonedDisposals = 0;
        var abandoned = UnityCapture.Run<int>(t => Capture(t, () => abandonedSteps++, () => abandonedDisposals++));
        PumpUntil(dispatcher, () => abandonedSteps == 1);
        var queued = Updater.RunOnMainThread(() => throw new Exception("must not execute"));
        Updater.Destroy();
        Check(abandoned.IsCanceled && queued.IsCanceled && abandonedDisposals == 1, "unload cancels queued and started captures");
        Check(Updater.RunOnMainThread(() => 1).IsCanceled, "stopped world refuses work");

        Updater.Create(); dispatcher = Dispatcher();
        int cancelledSteps = 0;
        using var cancel = new CancellationTokenSource();
        var cancelled = UnityCapture.Run<int>(t => Capture(t, () => cancelledSteps++, () => { }), cancel.Token);
        PumpUntil(dispatcher, () => cancelledSteps == 1);
        cancel.Cancel(); dispatcher.MoveNext();
        Check(cancelled.IsCanceled && cancelledSteps == 1, "timeout stops a launched capture before its next unit");
        Updater.Destroy();

        Updater.Create();
        var pending = new List<Task<int>>();
        for (int i = 0; i < 512; i++) pending.Add(Updater.RunOnMainThread(() => 1));
        var overflow = Updater.RunOnMainThread(() => 2);
        Check(overflow.IsFaulted, "queue overflow must refuse without executing inline");
        Updater.Destroy();
        Check(pending.TrueForAll(t => t.IsCanceled), "all admitted work terminates at shutdown");
        Console.WriteLine("PASS scheduler: shared budget fairness, frame yields, late failure, completion disposal, unload, timeout, queue bounds and restart.");
    }
}
namespace UnityEngine
{
    public class MonoBehaviour { protected void StartCoroutine(IEnumerator routine) { } }
    public class GameObject
    {
        public static void DontDestroyOnLoad(GameObject o) { }
        public static void DestroyImmediate(GameObject o) { }
        public T AddComponent<T>() where T : new() => new T();
    }
    public class WaitForSecondsRealtime { public WaitForSecondsRealtime(float seconds) { } }
}
public class TrainCar { public bool isStationary; }
public class Trainset { public static List<Trainset> allSets = new List<Trainset>(); public int id; public TrainCar? firstCar; }
namespace DvMod.RemoteDispatch
{
    static class Main { public static void DebugLog(Func<string> text) { } }
    static class Sessions { public static bool HasActiveSessions() => false; }
    static class PlayerData { public static void CheckTransform() { } }
    static class CarUpdater { public static void MarkTrainsetAsDirty(Trainset train, bool position) { } }
}
