using System;
using System.Threading;
using System.Threading.Tasks;
using DvMod.RemoteDispatch;

static class Program
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    static async Task Main()
    {
        Check(TransportSecurity.PasswordEquals("secret", "secret"), "Equal passwords rejected");
        Check(!TransportSecurity.PasswordEquals("secret", "Secret"), "Password comparison is not ordinal");
        Check(!TransportSecurity.PasswordEquals("secret", "secret-long"), "Password prefix accepted");
        Check(TransportSecurity.IsValidUsername("dispatcher"), "Normal username rejected");
        Check(!TransportSecurity.IsValidUsername("bad\nname"), "Control character accepted in username");
        Check(TransportSecurity.IsValidSessionId("550e8400-e29b-41d4-a716-446655440000"), "Browser UUID rejected");
        Check(!TransportSecurity.IsValidSessionId("../other"), "Unsafe session id accepted");

        var local = new Uri("http://localhost:7245/route/apply");
        Check(TransportSecurity.IsSameOrigin(null, local), "Native client without Origin rejected");
        Check(TransportSecurity.IsSameOrigin("http://LOCALHOST:7245", local), "Same origin rejected");
        Check(!TransportSecurity.IsSameOrigin("https://localhost:7245", local), "Scheme mismatch accepted");
        Check(!TransportSecurity.IsSameOrigin("http://localhost:7246", local), "Port mismatch accepted");
        Check(!TransportSecurity.IsSameOrigin("http://evil.example", local), "Cross origin accepted");

        var set = new AsyncSet<string>();
        using var cancellation = new CancellationTokenSource();
        var wait = set.TryTakeAsync(TimeSpan.FromSeconds(30), cancellation.Token);
        cancellation.Cancel();
        try { await wait; throw new Exception("Cancelled long poll completed normally"); }
        catch (OperationCanceledException) { }

        Console.WriteLine("PASS transport: credentials, identities, session IDs, same-origin policy and cancellable long polling.");
    }
}
