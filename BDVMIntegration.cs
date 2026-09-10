using System;
using System.Reflection;
using UnityModManagerNet;

namespace DvMod.RemoteDispatch
{
    internal static class BDVMIntegration
    {
        private const int MaximumPayloadBytes = 4096;

        public static string GetState(string authenticatedUser, bool isLoopbackRequest)
            => Invoke("GetState", authenticatedUser, null, isLoopbackRequest);

        public static string SubmitIntent(string authenticatedUser, string payload, bool isLoopbackRequest)
        {
            if (payload == null || System.Text.Encoding.UTF8.GetByteCount(payload) > MaximumPayloadBytes) throw new ArgumentException("BDVM intent payload is too large.");
            return Invoke("SubmitIntent", authenticatedUser, payload, isLoopbackRequest);
        }

        public static string GetWebShell(string authenticatedUser, bool isLoopbackRequest)
            => Invoke("GetWebShell", authenticatedUser, null, isLoopbackRequest);

        public static string GetManagementState(string authenticatedUser, bool isLoopbackRequest)
            => Invoke("GetManagementState", authenticatedUser, null, isLoopbackRequest);

        public static string SubmitManagementIntent(string authenticatedUser, string payload, bool isLoopbackRequest)
        {
            if (payload == null || System.Text.Encoding.UTF8.GetByteCount(payload) > MaximumPayloadBytes) throw new ArgumentException("BDVM management intent payload is too large.");
            return Invoke("SubmitManagementIntent", authenticatedUser, payload, isLoopbackRequest);
        }

        public static string GetWebAsset(string authenticatedUser, string assetKey, bool isLoopbackRequest)
            => Invoke("GetWebAsset", authenticatedUser, assetKey, isLoopbackRequest);

        private static string Invoke(string methodName, string authenticatedUser, string? payload, bool isLoopbackRequest)
        {
            if (string.IsNullOrWhiteSpace(authenticatedUser) || authenticatedUser.Length > 96) throw new UnauthorizedAccessException("Authenticated RemoteDispatch identity required.");
            var type = UnityModManager.FindMod("BDVM.Full")?.Assembly?.GetType("BDVM.RemoteDispatchBridge") ?? throw new InvalidOperationException("BDVM bridge is unavailable.");
            var parameterTypes = payload == null ? new[] { typeof(string), typeof(bool) } : new[] { typeof(string), typeof(string), typeof(bool) };
            var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static, null, parameterTypes, null) ?? throw new MissingMethodException(type.FullName, methodName);
            try { return (string)(method.Invoke(null, payload == null ? new object[] { authenticatedUser, isLoopbackRequest } : new object[] { authenticatedUser, payload, isLoopbackRequest }) ?? "{}"); }
            catch (TargetInvocationException exception) when (exception.InnerException != null) { throw exception.InnerException; }
        }
    }
}
