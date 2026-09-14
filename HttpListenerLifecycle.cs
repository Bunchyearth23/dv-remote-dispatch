namespace DvMod.RemoteDispatch
{
    internal static class HttpListenerLifecycle
    {
        internal const bool CaptureUnityContextForAccept = false;

        internal static bool IsExpectedShutdown(bool isListening) => !isListening;

        internal static bool TryStart(System.Action start, System.Action<System.Exception> failed)
        {
            try { start(); return true; }
            catch (System.Exception exception) { failed(exception); return false; }
        }
    }
}
