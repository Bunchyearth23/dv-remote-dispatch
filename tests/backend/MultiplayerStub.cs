using System.Collections.Generic;
namespace DvMod.RemoteDispatch
{
    // Multiplayer's complete adapter is exercised by tests/routes, independently of the signal fixtures.
    public static class MultiplayerData
    {
        public sealed class State { public string status="unavailable"; }
        public static IEnumerable<object?> ReadRows(State state) { yield break; }
    }
}
