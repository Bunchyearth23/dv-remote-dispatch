using System;
using System.Text;

namespace DvMod.RemoteDispatch
{
    internal static class TransportSecurity
    {
        public const int MaximumUsernameLength = 96;
        public const int MaximumSessionIdLength = 64;

        public static bool PasswordEquals(string expected, string supplied)
        {
            var left = Encoding.UTF8.GetBytes(expected ?? "");
            var right = Encoding.UTF8.GetBytes(supplied ?? "");
            var difference = left.Length ^ right.Length;
            var count = Math.Max(left.Length, right.Length);
            for (var i = 0; i < count; i++)
                difference |= (i < left.Length ? left[i] : 0) ^ (i < right.Length ? right[i] : 0);
            return difference == 0;
        }

        public static bool IsValidUsername(string username)
        {
            if (string.IsNullOrWhiteSpace(username) || username.Length > MaximumUsernameLength) return false;
            foreach (var character in username)
                if (char.IsControl(character)) return false;
            return true;
        }

        public static bool IsValidSessionId(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId) || sessionId.Length > MaximumSessionIdLength) return false;
            foreach (var character in sessionId)
                if (!(char.IsLetterOrDigit(character) || character == '-')) return false;
            return true;
        }

        public static bool IsSameOrigin(string? origin, Uri requestUrl)
        {
            if (string.IsNullOrEmpty(origin)) return true; // Native clients do not send Origin.
            return Uri.TryCreate(origin, UriKind.Absolute, out var parsed)
                && (parsed.Scheme == "http" || parsed.Scheme == "https")
                && string.Equals(parsed.Scheme, requestUrl.Scheme, StringComparison.OrdinalIgnoreCase)
                && string.Equals(parsed.Host, requestUrl.Host, StringComparison.OrdinalIgnoreCase)
                && parsed.Port == requestUrl.Port;
        }
    }
}
