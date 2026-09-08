using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace DvMod.RemoteDispatch
{
    public class Updater : MonoBehaviour
    {
        public void Start()
        {
            StartCoroutine(CheckPlayerTransformCoro());
            StartCoroutine(CheckTrainsetsCoro());
            StartCoroutine(DeferredEventsCoro());
        }

        private static GameObject? rootObject;

        public static void RunCoroutine(IEnumerator routine) => rootObject!.GetComponent<Updater>().StartCoroutine(routine);

        public static void Create()
        {
            if (rootObject == null)
            {
                rootObject = new GameObject();
                GameObject.DontDestroyOnLoad(rootObject);
                rootObject.AddComponent<Updater>();
            }
        }

        public static void Destroy()
        {
            if (rootObject != null)
            {
                GameObject.DestroyImmediate(rootObject);
                rootObject = null;
            }
            while (taskQueue.TryDequeue(out var pending)) pending.cancel();
        }

        private IEnumerator CheckPlayerTransformCoro()
        {
            while (true)
            {
                yield return WaitFor.Seconds(0.1f);
                PlayerData.CheckTransform();
            }
        }

        private IEnumerator CheckTrainsetsCoro()
        {
            var interval = new WaitForSecondsRealtime(0.1f);
            var moving = new HashSet<int>();
            while (true)
            {
                foreach (var trainset in Trainset.allSets)
                {
                    if (trainset.firstCar != null && !trainset.firstCar.isStationary)
                    {
                        moving.Add(trainset.id);
                        CarUpdater.MarkTrainsetAsDirty(trainset);
                    }
                    else if (moving.Remove(trainset.id))
                        CarUpdater.MarkTrainsetAsDirty(trainset); // Publish the final stopping position too.
                }
                yield return interval;
            }
        }

        private IEnumerator DeferredEventsCoro()
        {
            var frameBudget = new System.Diagnostics.Stopwatch();
            while (true)
            {
                frameBudget.Restart();
                while (taskQueue.TryDequeue(out var work))
                {
                    work.execute();
                    if (frameBudget.Elapsed.TotalMilliseconds >= 2) break;
                }
                yield return null;
            }
        }

        private sealed class QueuedWork
        {
            public readonly Action execute;
            public readonly Action cancel;
            public QueuedWork(Action execute, Action cancel) { this.execute = execute; this.cancel = cancel; }
        }

        private static readonly ConcurrentQueue<QueuedWork> taskQueue = new ConcurrentQueue<QueuedWork>();

        public static Task RunOnMainThread(Action action)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (rootObject == null) { tcs.SetCanceled(); return tcs.Task; }
            taskQueue.Enqueue(new QueuedWork(() =>
            {
                try
                {
                    action();
                    tcs.TrySetResult(true);
                }
                catch (Exception e)
                {
                    tcs.TrySetException(e);
                }
            }, () => tcs.TrySetCanceled()));
            return tcs.Task;
        }

        public static Task<T> RunOnMainThread<T>(Func<T> func)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (rootObject == null) { tcs.SetCanceled(); return tcs.Task; }
            taskQueue.Enqueue(new QueuedWork(() =>
            {
                try
                {
                    tcs.TrySetResult(func());
                }
                catch (Exception e)
                {
                    tcs.TrySetException(e);
                }
            }, () => tcs.TrySetCanceled()));
            return tcs.Task;
        }
    }
}
