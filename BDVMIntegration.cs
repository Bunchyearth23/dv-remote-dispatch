using System;
using System.Reflection;
using UnityModManagerNet;

namespace DvMod.RemoteDispatch
{
    internal static class BDVMIntegration
    {
        private const int MaximumPayloadBytes = 4096;

        public static string GetState(string authenticatedUser)
            => Invoke("GetState", authenticatedUser, null);

        public static string SubmitIntent(string authenticatedUser, string payload)
        {
            if (payload == null || System.Text.Encoding.UTF8.GetByteCount(payload) > MaximumPayloadBytes) throw new ArgumentException("BDVM intent payload is too large.");
            return Invoke("SubmitIntent", authenticatedUser, payload);
        }

        private static string Invoke(string methodName, string authenticatedUser, string? payload)
        {
            if (string.IsNullOrWhiteSpace(authenticatedUser) || authenticatedUser.Length > 96) throw new UnauthorizedAccessException("Authenticated RemoteDispatch identity required.");
            var type = UnityModManager.FindMod("BDVM")?.Assembly?.GetType("BDVM.RemoteDispatchBridge") ?? throw new InvalidOperationException("BDVM bridge is unavailable.");
            var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static) ?? throw new MissingMethodException(type.FullName, methodName);
            try { return (string)(method.Invoke(null, payload == null ? new object[] { authenticatedUser } : new object[] { authenticatedUser, payload }) ?? "{}"); }
            catch (TargetInvocationException exception) when (exception.InnerException != null) { throw exception.InnerException; }
        }
    }
}
