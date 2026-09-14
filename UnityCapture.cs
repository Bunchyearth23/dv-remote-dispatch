using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;

namespace DvMod.RemoteDispatch
{
    /// <summary>Owns a capture from admission through completion and world shutdown.</summary>
    internal static class UnityCapture
    {
        public static Task<T> Run<T>(Func<TaskCompletionSource<T>, IEnumerator> factory, CancellationToken cancellation = default)
        {
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            var lifetime = Updater.Lifetime;
            var registration = lifetime.Register(() => completion.TrySetCanceled());
            var callerRegistration = cancellation.Register(() => completion.TrySetCanceled());
            _ = completion.Task.ContinueWith(_ => { registration.Dispose(); callerRegistration.Dispose(); }, CancellationToken.None,
                TaskContinuationOptions.None, TaskScheduler.Default);
            var admission = Updater.RunOnMainThread(() =>
            {
                if (!completion.Task.IsCompleted)
                    Updater.RunCoroutine(Guard(factory, completion));
            });
            _ = admission.ContinueWith(task =>
            {
                if (task.IsCanceled) completion.TrySetCanceled();
                else if (task.IsFaulted) completion.TrySetException(task.Exception!.InnerExceptions);
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            return completion.Task;
        }

        private static IEnumerator Guard<T>(Func<TaskCompletionSource<T>, IEnumerator> factory, TaskCompletionSource<T> completion)
        {
            IEnumerator? routine = null;
            try
            {
                try { routine = factory(completion); }
                catch (Exception error) { completion.TrySetException(error); }
                while (routine != null && !completion.Task.IsCompleted)
                {
                    bool more = false;
                    object? current = null;
                    try { more = routine.MoveNext(); if (more) current = routine.Current; }
                    catch (Exception error) { completion.TrySetException(error); }
                    if (!more) break;
                    yield return current;
                }
            }
            finally
            {
                try { (routine as IDisposable)?.Dispose(); }
                catch (Exception error) { completion.TrySetException(error); }
                if (!completion.Task.IsCompleted)
                    completion.TrySetException(new InvalidOperationException("Unity capture ended without a result."));
            }
        }
    }
}
