using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using DvMod.RemoteDispatch;

static class Program
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    static async Task Main()
    {
        await Sessions.GetUpdates("alice", "session-1");
        try
        {
            await Sessions.GetUpdates("bob", "session-1");
            throw new Exception("A second identity reused an existing session");
        }
        catch (UnauthorizedAccessException) { }

        var waiting = Sessions.GetUpdates("alice", "session-1");
        await Task.Delay(20);
        Check(!waiting.IsCompleted, "Long poll did not wait when no tags were pending");
        Sessions.Reset();
        try { await waiting; throw new Exception("Reset did not cancel the long poll"); }
        catch (OperationCanceledException) { }

        Check(Sessions.GetUsersWithActiveSessions().Count == 0, "Reset retained active sessions");
        Console.WriteLine("PASS sessions: identity ownership, long-poll wait and unload cancellation.");
    }
}

namespace DvMod.RemoteDispatch
{
    static class Main { public static void DebugLog(Func<string> message) { } }
    static class Updater
    {
        public static Task<T> RunOnMainThread<T>(Func<T> action) => Task.FromResult(action());
        public static Task RunOnMainThread(Action action) { action(); return Task.CompletedTask; }
    }
    static class CarData
    {
        public sealed class Row { public JObject ToJson() => new JObject(); }
        public static JObject? GetCarGuidDataJson(string id) => new JObject();
        public static object GetTrainsetData(int id) => new JObject();
        public static Dictionary<string, Row> GetAllCarData() => new Dictionary<string, Row>();
    }
    static class JobData { public static object GetAllJobData() => new JObject(); }
    static class Junctions { public static object[] GetAllJunctionStates() => Array.Empty<object>(); }
    static class PlayerData { public static JToken GetPlayerData() => new JObject(); }
}
