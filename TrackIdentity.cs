using System;
using System.Collections.Generic;
using System.Linq;

namespace DvMod.RemoteDispatch
{
    // Display IDs are not unique on the main line or on networks extended by mods.
    // Keep every native track and use the same session key in geometry and routing.
    public static class TrackIdentity
    {
        private static Dictionary<RailTrack, string>? ids;
        public static void Reset() => ids = null;
        public static IReadOnlyDictionary<RailTrack, string> All
        {
            get
            {
                if (ids != null) return ids;
                var result = new Dictionary<RailTrack, string>();
                var tracks = UnityEngine.Object.FindObjectsOfType<RailTrack>();
                foreach (var group in tracks.GroupBy(t => t.LogicTrack()?.ID?.ToString() ?? "#track"))
                {
                    var rows = group.ToArray();
                    foreach (var track in rows)
                        result.Add(track, rows.Length == 1 ? group.Key : group.Key + "@" + track.GetInstanceID());
                }
                return ids = result;
            }
        }
        public static string Id(RailTrack track)
        {
            if (All.TryGetValue(track, out var id)) return id;
            throw new InvalidOperationException("The network changed. Reload the map after the world has loaded.");
        }
    }
}
