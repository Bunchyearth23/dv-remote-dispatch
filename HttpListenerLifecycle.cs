namespace DvMod.RemoteDispatch
{
    internal static class HttpListenerLifecycle
    {
        internal const bool CaptureUnityContextForAccept = false;

        internal static bool IsExpectedShutdown(bool isListening) => !isListening;
    }
}
