using System;
using System.Collections.Generic;
using System.Linq;

namespace DvMod.RemoteDispatch
{
    // Immutable after capture. This solver never touches Unity objects.
    public sealed class RouteGraph
    {
        public sealed class Track
        {
            public string id = "";
            public double length;
            public List<Link> forward = new List<Link>(), backward = new List<Link>();
        }
        public sealed class Link
        {
            public string track = "";
            public bool forward;
            public int junction = -1;
            public byte branch;
        }
        public sealed class Step
        {
            public string track = "";
            public bool forward;
            public int junction = -1;
            public byte branch;
        }
        public sealed class Path
        {
            public List<Step> steps = new List<Step>();
            public double distance;
        }
        public readonly Dictionary<string, Track> tracks = new Dictionary<string, Track>();

        public Path Find(string origin, string destination, string? via, bool? forward)
        {
            if (!tracks.ContainsKey(origin) || !tracks.ContainsKey(destination) ||
                (!string.IsNullOrEmpty(via) && !tracks.ContainsKey(via!)))
                throw new ArgumentException("Voie introuvable. Actualisez le catalogue.");
            var distances = new Dictionary<(string, bool, bool), double>();
            var previous = new Dictionary<(string, bool, bool), ((string, bool, bool) from, Link link)>();
            var queue = new SortedSet<(double distance, int serial, string track, bool direction, bool via)>();
            int serial = 0;
            foreach (bool direction in new[] { true, false })
            {
                if (forward.HasValue && forward.Value != direction) continue;
                var key = (origin, direction, string.IsNullOrEmpty(via) || origin == via);
                distances[key] = 0;
                queue.Add((0, serial++, origin, direction, key.Item3));
            }
            while (queue.Count > 0)
            {
                var item = queue.Min;
                queue.Remove(item);
                var key = (item.track, item.direction, item.via);
                if (item.distance != distances[key]) continue;
                if (item.track == destination && item.via)
                {
                    var result = new Path { distance = item.distance + tracks[destination].length };
                    while (previous.TryGetValue(key, out var prev))
                    {
                        result.steps.Add(new Step { track = key.Item1, forward = key.Item2,
                            junction = prev.link.junction, branch = prev.link.branch });
                        key = prev.from;
                    }
                    result.steps.Add(new Step { track = key.Item1, forward = key.Item2 });
                    result.steps.Reverse();
                    if (result.steps.Select(s => s.track).Distinct().Count() != result.steps.Count)
                        throw new ArgumentException("Ce parcours repasse par une voie : préparez plusieurs étapes de manœuvre.");
                    if (result.steps.Where(s => s.junction >= 0).GroupBy(s => s.junction).Any(g => g.Select(s => s.branch).Distinct().Count() > 1))
                        throw new ArgumentException("Ce parcours exige deux positions du même aiguillage. Préparez plusieurs étapes.");
                    return result;
                }
                foreach (var link in item.direction ? tracks[item.track].forward : tracks[item.track].backward)
                {
                    if (!tracks.ContainsKey(link.track)) continue;
                    var next = (link.track, link.forward, item.via || link.track == via);
                    double cost = item.distance + Math.Max(0.01, tracks[item.track].length);
                    if (distances.TryGetValue(next, out var old) && old <= cost) continue;
                    distances[next] = cost;
                    previous[next] = (key, link);
                    queue.Add((cost, serial++, next.Item1, next.Item2, next.Item3));
                }
            }
            throw new ArgumentException("Aucun parcours continu dans ce sens. Essayez l’autre sens ou une autre voie de passage.");
        }
    }
}
