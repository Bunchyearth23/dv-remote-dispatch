using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityModManagerNet;

namespace DvMod.RemoteDispatch
{
    // Read-only adapter for the installed AITraffic 0.2.1. Never calls pathfinding,
    // reservation methods, or singleton getters that create game objects.
    public static class AiTrafficData
    {
        public sealed class State
        {
            public string status = "world-unavailable";
            public string? version;
            public List<Train> trains = new List<Train>();
            public List<object> reservations = new List<object>();
            public List<object> junctionLocks = new List<object>();
        }
        public sealed class Train
        {
            public string id = "", carId = "", state = "";
            public string? origin, destination, destinationTrack;
            public float[] position = Array.Empty<float>();
            public float rotation;
            public float? speedKmh, targetSpeedKmh, distanceToSignal, distanceToDestination;
            public int? signalId;
            public bool worker;
            public List<string> routeTracks = new List<string>();
        }

        private static readonly Dictionary<(Type, string), MemberInfo> members = new Dictionary<(Type, string), MemberInfo>();
        public static void Reset() => members.Clear();

        // Main-thread command preflight, independent of the telemetry cache.
        // Fail closed if the installed adapter cannot expose authoritative ownership.
        public static Dictionary<RailTrack, string> RouteReservations(out HashSet<string> engineers)
        {
            engineers = new HashSet<string>();
            var result = new Dictionary<RailTrack, string>();
            var mod = UnityModManager.FindMod("AITraffic");
            if (mod?.Active != true || mod.Assembly == null) return result;
            var manager = Read(mod.Assembly.GetType("AITraffic.Core.TrafficManager", true)!, "s_instance");
            var owners = new Dictionary<object, string>();
            if (manager != null)
                foreach (var engineer in CopyList(Read(manager, "ActiveEngineers")))
                {
                    if (!(Read(engineer, "TrainCar") is TrainCar car) || car == null) continue;
                    engineers.Add(car.CarGUID);
                    owners[engineer] = car.CarGUID;
                    owners[car] = car.CarGUID;
                }
            var graph = Read(mod.Assembly.GetType("AITraffic.Navigation.RailGraph", true)!, "s_instance");
            if (graph == null) throw new InvalidOperationException("AITraffic initialise ses réservations. Réessayez ensuite.");
            foreach (var entry in CopyRegistry(graph, "_trackReservations"))
                if (entry.Key is RailTrack track)
                    result[track] = entry.Value != null && owners.TryGetValue(entry.Value, out var owner) ? owner : "AI inconnue";
            return result;
        }

        internal static object? Read(object target, string name)
        {
            var type = target as Type ?? target.GetType();
            var key = (type, name);
            if (!members.TryGetValue(key, out var member))
            {
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
                member = (MemberInfo?)type.GetProperty(name, flags) ?? type.GetField(name, flags)
                    ?? throw new MissingMemberException(type.FullName, name);
                members[key] = member;
            }
            var instance = target is Type ? null : target;
            return member is PropertyInfo p ? p.GetValue(instance) : ((FieldInfo)member).GetValue(instance);
        }
        private static float? Finite(object? value)
        {
            if (value == null) return null;
            var number = Convert.ToSingle(value);
            return float.IsInfinity(number) || float.IsNaN(number) ? (float?)null : number;
        }
        internal static List<object> CopyList(object? source)
        {
            var result = new List<object>();
            if (source is IEnumerable items) foreach (var item in items) result.Add(item);
            return result;
        }
        internal static List<DictionaryEntry> CopyRegistry(object instance, string field)
        {
            var result = new List<DictionaryEntry>();
            lock (Read(instance, "_lock")!)
                foreach (DictionaryEntry entry in (IDictionary)Read(instance, field)!) result.Add(entry);
            return result;
        }

        public static IEnumerable<object?> ReadRows(State result)
        {
            result.status = "unavailable";
            var mod = UnityModManager.FindMod("AITraffic");
            if (mod?.Active != true || mod.Assembly == null) yield break;
            result.version = mod.Info.Version;
            result.status = "initializing";
            var managerType = mod.Assembly.GetType("AITraffic.Core.TrafficManager", true)!;
            var manager = Read(managerType, "s_instance");
            if (manager == null || !Convert.ToBoolean(Read(managerType, "IsRunning"))) yield break;
            var owners = new Dictionary<object, string>();
            foreach (var engineer in CopyList(Read(manager, "ActiveEngineers")))
            {
                var car = Read(engineer, "TrainCar") as TrainCar;
                if (car == null) continue;
                var p = new World.Position(car.transform.position - WorldMover.currentMove).ToLatLon();
                var signal = Read(engineer, "ApproachingSignal");
                var train = new Train {
                    id = car.CarGUID, carId = car.ID, state = Convert.ToString(Read(engineer, "State")) ?? "",
                    position = new[] { p.latitude, p.longitude }, rotation = car.transform.eulerAngles.y,
                    origin = Convert.ToString(Read(engineer, "OriginStationName")),
                    destination = Convert.ToString(Read(engineer, "DestinationStationName")),
                    destinationTrack = Convert.ToString(Read(engineer, "DestinationTrackName")),
                    speedKmh = Finite(Read(engineer, "CurrentSpeedKmh")), targetSpeedKmh = Finite(Read(engineer, "TargetSpeedKmh")),
                    distanceToSignal = Finite(Read(engineer, "DistanceToSignal")),
                    distanceToDestination = Finite(Read(engineer, "DistanceToDestination")),
                    signalId = signal == null ? (int?)null : Convert.ToInt32(Read(signal, "Id")),
                    worker = Convert.ToBoolean(Read(engineer, "IsWorkerDriven"))
                };
                owners[engineer] = train.id;
                owners[car] = train.id;
                // Copy references before yielding: AITraffic rebuilds this list during Update.
                var route = CopyList(Read(engineer, "UpcomingTracks"));
                yield return null;
                foreach (var item in route)
                {
                    if (item is RailTrack track && track != null) train.routeTracks.Add(TrackIdentity.Id(track));
                    yield return null;
                }
                result.trains.Add(train);
            }

            // The engineer's _reservedTracks is not authoritative: 0.2.1 can add
            // a track even if TryReserveTrack failed. Read the graph registry instead.
            var graph = Read(mod.Assembly.GetType("AITraffic.Navigation.RailGraph", true)!, "s_instance");
            if (graph != null && Convert.ToBoolean(Read(graph, "IsInitialized")))
            {
                foreach (var entry in CopyRegistry(graph, "_trackReservations"))
                {
                    if (entry.Key is RailTrack track && track != null)
                        result.reservations.Add(new { trackId = TrackIdentity.Id(track),
                            ownerId = entry.Value != null && owners.TryGetValue(entry.Value, out var owner) ? owner : null });
                    yield return null;
                }
            }

            var junctionIds = new Dictionary<Junction, int>();
            var ordered = RailTrackRegistry.Instance.OrderedJunctions;
            for (int id = 0; id < ordered.Length; id++) junctionIds[ordered[id]] = id;
            var locks = Read(mod.Assembly.GetType("AITraffic.Navigation.JunctionController", true)!, "_instance");
            if (locks != null)
            {
                foreach (var entry in CopyRegistry(locks, "_activeLocks"))
                {
                    if (entry.Value != null && entry.Key is Junction junction &&
                        junctionIds.TryGetValue(junction, out var id) && !Convert.ToBoolean(Read(entry.Value, "IsExpired")))
                    {
                        var requester = Read(entry.Value, "Requester");
                        result.junctionLocks.Add(new { junctionId = id,
                            ownerId = requester != null && owners.TryGetValue(requester, out var owner) ? owner : null });
                    }
                    yield return null;
                }
            }
            result.status = "ready";
        }

        // Recheck the live lock in the same Unity action as the switch, never trust
        // an HTTP client's snapshot. Reading IsJunctionLocked() would release expired locks.
        public static string? JunctionBlockReason(Junction junction, HashSet<string>? allowedOwners = null)
        {
            var mod = UnityModManager.FindMod("AITraffic");
            if (mod?.Active != true || mod.Assembly == null) return null;
            try
            {
                var controller = Read(mod.Assembly.GetType("AITraffic.Navigation.JunctionController", true)!, "_instance");
                if (controller == null) return null;
                lock (Read(controller, "_lock")!)
                {
                    var locks = (IDictionary)Read(controller, "_activeLocks")!;
                    var info = locks.Contains(junction) ? locks[junction] : null;
                    if (info != null && allowedOwners != null)
                    {
                        var requester = Read(info, "Requester");
                        var car = requester as TrainCar;
                        if (car == null && requester != null) car = Read(requester, "TrainCar") as TrainCar;
                        if (car != null && allowedOwners.Contains(car.CarGUID)) return null;
                    }
                    return info != null && !Convert.ToBoolean(Read(info, "IsExpired")) ? "Aiguillage verrouillé par AITraffic" : null;
                }
            }
            catch { return "État du verrou AITraffic indisponible"; }
        }
    }
}
