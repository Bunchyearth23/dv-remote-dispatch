using DV.PointSet;
using System.Collections;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;
using UnityEngine;

namespace DvMod.RemoteDispatch
{
    public static class World
    {
        public readonly struct Position
        {
            public readonly float x;
            public readonly float z;

            public Position(float x, float z)
            {
                this.x = x;
                this.z = z;
            }

            public Position(Vector3 position) : this(position.x, position.z) { }
            public Position(Transform transform) : this(transform.position) { }

            public LatLon ToLatLon() => LatLon.From(this);
        }

        public readonly struct LatLon
        {
            private const int DECIMAL_PLACES = 8; // 1.11 mm
            private const float EARTH_CIRCUMFERENCE = 40e6f;
            private const float DEGREES_PER_METER = 360f / EARTH_CIRCUMFERENCE;

            public readonly float latitude;
            public readonly float longitude;

            public LatLon(float latitude, float longitude)
            {
                this.latitude = (float)Math.Round(latitude, DECIMAL_PLACES);
                this.longitude = (float)Math.Round(longitude, DECIMAL_PLACES);
            }

            public static LatLon From(Position p) => new LatLon(DEGREES_PER_METER * p.z, DEGREES_PER_METER * p.x);

            public JToken ToJson() => new JArray(latitude, longitude);
        }
    }

    public static class RailTracks
    {
        private const float SIMPLIFIED_RESOLUTION = 40f;

        private static IEnumerable<World.LatLon> NormalizeTrackPoints(IEnumerable<World.Position> positions) => positions.Select(p => p.ToLatLon());

        public static Dictionary<RailTrack, IEnumerable<World.LatLon>> GetNormalizedTrackCoordinates() =>
            GetAllTrackPoints().ToDictionary(kvp => kvp.Key, kvp => NormalizeTrackPoints(kvp.Value));

        public static Dictionary<RailTrack, IEnumerable<World.Position>> GetAllTrackPoints(float resolution = SIMPLIFIED_RESOLUTION)
        {
            if (!WorldStreamingInit.Instance || !WorldStreamingInit.IsLoaded)
                throw new Exception("World not yet loaded");
            var tracks = Component.FindObjectsOfType<RailTrack>();
            Main.DebugLog(() => $"Found {tracks.Length} RailTracks.");
            return tracks.ToDictionary(track => track, track => GetTrackPoints(track, resolution));
        }

        private static IEnumerable<World.Position> GetTrackPoints(RailTrack track, float resolution = SIMPLIFIED_RESOLUTION)
        {
            var pointSet = track.GetKinkedPointSet();
            EquiPointSet simplified = EquiPointSet.ResampleEquidistant(
                pointSet,
                Mathf.Min(resolution, (float)pointSet.span / 3));

            foreach (var pt in simplified.points)
                yield return new World.Position((float)pt.position.x, (float)pt.position.z);
        }

        private static readonly object trackGate = new object();
        private static Task<string>? trackPointTask;

        public static void Reset() { lock (trackGate) trackPointTask = null; TrackIdentity.Reset(); }

        private static async Task<string> BuildTrackPointJSON()
        {
            var captured = new Dictionary<string, World.LatLon[]>(StringComparer.Ordinal);
            await UnityCapture.Run<bool>(completion => CaptureTrackPoints(captured, completion)).ConfigureAwait(false);
            return await Task.Run(() => JsonConvert.SerializeObject(captured.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Select(point => new JArray(point.latitude, point.longitude)).ToArray()))).ConfigureAwait(false);
        }

        private static IEnumerator CaptureTrackPoints(Dictionary<string, World.LatLon[]> captured, TaskCompletionSource<bool> completion)
        {
            if (!WorldStreamingInit.Instance || !WorldStreamingInit.IsLoaded)
            {
                completion.TrySetException(new Exception("World not yet loaded"));
                yield break;
            }
            foreach (var track in Component.FindObjectsOfType<RailTrack>())
            {
                try
                {
                    var id = TrackIdentity.Id(track);
                    if (captured.ContainsKey(id)) throw new InvalidOperationException("Ambiguous track identifiers.");
                    captured[id] = GetTrackPoints(track).Select(position => position.ToLatLon()).ToArray();
                }
                catch (Exception exception) { completion.TrySetException(exception); yield break; }
                yield return null;
            }
            completion.TrySetResult(true);
        }

        public static Task<string> GetTrackPointJSON()
        {
            lock (trackGate)
            {
                if (trackPointTask == null || trackPointTask.IsFaulted || trackPointTask.IsCanceled)
                    trackPointTask = BuildTrackPointJSON();
                return trackPointTask;
            }
        }
    }

    public static class Junctions
    {
        private static readonly object pointGate = new object();
        private static Task<string>? junctionPointTask;

        public static void Reset() { lock (pointGate) junctionPointTask = null; }

        private sealed class JunctionPoint
        {
            public float[] position = Array.Empty<float>();
            public string[] branches = Array.Empty<string>();
        }

        private static async Task<string> BuildJunctionPointJSON()
        {
            var captured = await UnityCapture.Run<List<JunctionPoint>>(CaptureJunctionPoints).ConfigureAwait(false);
            return await Task.Run(() => JsonConvert.SerializeObject(captured)).ConfigureAwait(false);
        }

        private static IEnumerator CaptureJunctionPoints(TaskCompletionSource<List<JunctionPoint>> completion)
        {
            if (!WorldStreamingInit.Instance || !WorldStreamingInit.IsLoaded) throw new Exception("World not yet loaded");
            var result = new List<JunctionPoint>();
            var count = 0;
            foreach (var junction in RailTrackRegistry.Instance.OrderedJunctions)
            {
                result.Add(CaptureJunctionPoint(junction));
                if (++count >= 16) { count = 0; yield return null; }
            }
            completion.TrySetResult(result);
        }

        private static JunctionPoint CaptureJunctionPoint(Junction junction)
        {
            var moved = junction.position - WorldMover.currentMove;
            var point = new World.Position(moved.x, moved.z).ToLatLon();
            return new JunctionPoint { position = new[] { point.latitude, point.longitude },
                branches = junction.outBranches.Select(branch => TrackIdentity.Id(branch.track)).ToArray() };
        }

        public static Task<string> GetJunctionPointJSONAsync()
        {
            lock (pointGate)
            {
                if (junctionPointTask == null || junctionPointTask.IsFaulted || junctionPointTask.IsCanceled)
                    junctionPointTask = BuildJunctionPointJSON();
                return junctionPointTask;
            }
        }

        public static string GetJunctionPointJSON()
        {
            // Legacy synchronous callers must already own Unity. Never wait on
            // queued Unity work here: it cannot advance while this call blocks.
            if (!WorldStreamingInit.Instance || !WorldStreamingInit.IsLoaded) throw new Exception("World not yet loaded");
            return JsonConvert.SerializeObject(RailTrackRegistry.Instance.OrderedJunctions.Select(CaptureJunctionPoint).ToArray());
        }

        public static byte[] GetAllJunctionStates()
        {
            if (!WorldStreamingInit.Instance || !WorldStreamingInit.IsLoaded)
                throw new Exception("World not yet loaded");
            // Enumerate while the caller owns the Unity thread. Returning Select
            // would defer the native reads until the HTTP worker encodes them.
            return RailTrackRegistry.Instance.OrderedJunctions.Select(j => j.selectedBranch).ToArray();
        }

        public static string GetJunctionStateJSON()
        {
            return JsonConvert.SerializeObject(GetAllJunctionStates());
        }
    }
}
