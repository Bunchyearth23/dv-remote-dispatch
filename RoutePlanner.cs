using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace DvMod.RemoteDispatch
{
    public static class RoutePlanner
    {
        private sealed class Topology
        {
            public RouteGraph graph = new RouteGraph();
            public Dictionary<string, RailTrack> native = new Dictionary<string, RailTrack>();
            public Junction[] junctions = Array.Empty<Junction>();
        }
        private sealed class Plan
        {
            public string token = "", owner = "", train = "";
            public DateTime expires;
            public Topology topology = null!;
            public RouteGraph.Path path = null!;
            public object? engineer;
            public object? multiplayerSession;
        }
        private static readonly SemaphoreSlim requestGate = new SemaphoreSlim(1, 1);
        private static Topology? topology;
        private static readonly Dictionary<string, Plan> plans = new Dictionary<string, Plan>();
        private static int generation;
        public static void Reset() { generation++; topology = null; plans.Clear(); }
        private static InvalidOperationException ChangedNetwork(string message)
        {
            Reset();
            return new InvalidOperationException(message + " Recalculez le parcours.");
        }

        private static void RequireWorld()
        {
            if (!WorldStreamingInit.Instance || !WorldStreamingInit.IsLoaded)
                throw new InvalidOperationException("La partie n’est pas chargée.");
        }
        private static string Id(RailTrack track) => TrackIdentity.Id(track);
        private static RailTrack? CurrentTrack(TrainCar car) => car.FrontBogie?.track ?? car.RearBogie?.track;
        private static TrainCar GetCar(string guid)
        {
            RequireWorld();
            var car = TrainCarRegistry.Instance.GetTrainCarByCarGuid(guid);
            if (car == null || !car.IsLoco) throw new ArgumentException("Locomotive indisponible.");
            return AiTrafficCommands.DrivingCar(car);
        }
        private static IEnumerable<object?> CaptureRows(Topology result)
        {
            RequireWorld();
            result.junctions = RailTrackRegistry.Instance.OrderedJunctions;
            var indices = result.junctions.Select((j, i) => (j, i)).ToDictionary(x => x.j, x => x.i);
            var native = UnityEngine.Object.FindObjectsOfType<RailTrack>();
            yield return null;
            foreach (var track in native)
            {
                var id = Id(track);
                if (result.native.ContainsKey(id)) throw new InvalidOperationException("Identifiants de voies ambigus.");
                result.native.Add(id, track);
                var row = new RouteGraph.Track { id = id, length = track.curve.length };
                foreach (bool forward in new[] { true, false })
                {
                    var junction = forward ? track.outJunction : track.inJunction;
                    var branches = forward ? track.GetAllOutBranches() : track.GetAllInBranches();
                    if (branches == null) continue;
                    foreach (var next in branches)
                    {
                        if (next.track == null) continue;
                        int junctionId = -1;
                        byte branch = 0;
                        if (junction != null)
                        {
                            if (!indices.TryGetValue(junction, out junctionId)) continue;
                            // Only stem-to-leg or leg-to-stem transitions are physically possible.
                            var leg = junction.inBranch.track == track ? next.track : track;
                            if (junction.inBranch.track != track && junction.inBranch.track != next.track) continue;
                            var branchIndex = junction.outBranches.FindIndex(b => b.track == leg);
                            if (branchIndex < 0 || branchIndex > byte.MaxValue) continue;
                            branch = (byte)branchIndex;
                        }
                        (forward ? row.forward : row.backward).Add(new RouteGraph.Link {
                            track = Id(next.track), forward = next.first, junction = junctionId, branch = branch });
                    }
                }
                result.graph.tracks.Add(id, row);
                yield return null;
            }
        }
        private static IEnumerator Capture(Topology result, TaskCompletionSource<bool> completion, int epoch)
        {
            using var rows = CaptureRows(result).GetEnumerator();
            var slice = Stopwatch.StartNew();
            while (true)
            {
                bool more;
                try
                {
                    if (epoch != generation) throw new InvalidOperationException("La partie a changé.");
                    more = rows.MoveNext();
                }
                catch (Exception e) { completion.TrySetException(e); yield break; }
                if (!more) break;
                if (slice.Elapsed.TotalMilliseconds >= 1) { yield return null; slice.Restart(); }
            }
            completion.TrySetResult(true);
        }
        private static async Task<Topology> GetTopology()
        {
            var result = await Updater.RunOnMainThread(() => { RequireWorld(); return topology; });
            if (result != null) return result;
            result = new Topology();
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int epoch = await Updater.RunOnMainThread(() => { int e = generation; Updater.RunCoroutine(Capture(result, completion, e)); return e; });
            if (await Task.WhenAny(completion.Task, Task.Delay(15000)) != completion.Task)
                throw new InvalidOperationException("Le chargement du réseau a expiré. Réessayez.");
            await completion.Task;
            return await Updater.RunOnMainThread(() => {
                if (epoch != generation) throw new InvalidOperationException("La partie a changé.");
                return topology = result;
            });
        }
        public static async Task<object> Catalog()
        {
            if (!await requestGate.WaitAsync(0)) throw new InvalidOperationException("Une préparation est en cours. Réessayez.");
            try
            {
                return await Updater.RunOnMainThread<object>(() => {
                    RequireWorld();
                    var engineers = new HashSet<string>();
                    string? warning = null;
                    try { AiTrafficData.RouteReservations(out engineers); }
                    catch (Exception e) { warning = "État AI indisponible : " + e.Message; }
                    var trains = TrainCarRegistry.Instance.logicCarToTrainCar.Values
                        .Where(c => c != null && c.IsLoco && CurrentTrack(c) != null)
                        .Select(c => new { id = c.CarGUID, name = c.ID, origin = Id(CurrentTrack(c)!), ai = engineers.Contains(c.CarGUID) ||
                            (c.trainset != null && c.trainset.cars.Any(member => engineers.Contains(member.CarGUID))) }).ToArray();
                    return new { trains, warning, tracks = TrackIdentity.All.Select(v => new { id = v.Value, length = Math.Round(v.Key.curve.length) }).OrderBy(v => v.id).ToArray() };
                });
            }
            finally { requestGate.Release(); }
        }
        public static async Task<object> Preview(string owner, string train, string destination, string? via)
        {
            if (!await requestGate.WaitAsync(0)) throw new InvalidOperationException("Une préparation est en cours. Réessayez.");
            try
            {
                var t = await GetTopology();
                var origin = await Updater.RunOnMainThread(() => {
                    var track = CurrentTrack(GetCar(train));
                    if (track == null) throw new InvalidOperationException("Le train n’est pas sur une voie.");
                    return Id(track);
                });
                var path = await Task.Run(() => t.graph.Find(origin, destination, via, null));
                return await Updater.RunOnMainThread<object>(() => {
                    if (topology != t) throw new InvalidOperationException("Le réseau a changé.");
                    var lead = GetCar(train);
                    var plan = new Plan { token = Guid.NewGuid().ToString("N"), owner = owner, train = lead.CarGUID,
                        engineer = AiTrafficCommands.FindEngineer(lead),
                        multiplayerSession = MultiplayerData.SessionIdentity(),
                        topology = t, path = path, expires = DateTime.UtcNow.AddSeconds(60) };
                    foreach (var key in plans.Where(p => p.Value.expires < DateTime.UtcNow || p.Value.owner == owner).Select(p => p.Key).ToArray()) plans.Remove(key);
                    if (plans.Count >= 32) throw new InvalidOperationException("Trop de préparations actives.");
                    plans[plan.token] = plan;
                    var conflicts = Check(plan, out var isAi);
                    return new { token = plan.token, tracks = path.steps.Select(s => s.track).ToArray(),
                        distance = Math.Round(path.distance), expiresAt = new DateTimeOffset(plan.expires).ToUnixTimeMilliseconds(),
                        origin, destination, via, ai = isAi, conflicts,
                        switches = path.steps.Where(s => s.junction >= 0).Select(s => new {
                            id = s.junction, branch = s.branch, change = t.junctions[s.junction].selectedBranch != s.branch }).ToArray() };
                });
            }
            finally { requestGate.Release(); }
        }
        private static List<string> Check(Plan plan, out bool isAi)
        {
            RequireWorld();
            var t = plan.topology;
            if (topology != t || RailTrackRegistry.Instance.OrderedJunctions != t.junctions)
                throw ChangedNetwork("Le réseau a changé.");
            var car = GetCar(plan.train);
            var conflicts = new List<string>();
            if (!ReferenceEquals(plan.multiplayerSession, MultiplayerData.SessionIdentity()))
                throw new InvalidOperationException("La session multiplayer a changé. Recalculez le parcours.");
            var networkReason = MultiplayerData.CommandBlockReason();
            if (networkReason != null) conflicts.Add(networkReason);
            for (int i = 1; i < plan.path.steps.Count; i++)
            {
                var previous = plan.path.steps[i - 1];
                var step = plan.path.steps[i];
                var from = t.native[previous.track];
                var to = t.native[step.track];
                if (from == null || to == null) throw ChangedNetwork("Une voie a disparu.");
                var next = previous.forward ? from.GetAllOutBranches() : from.GetAllInBranches();
                var junction = previous.forward ? from.outJunction : from.inJunction;
                if (next == null || !next.Any(b => b.track == to && b.first == step.forward) ||
                    (step.junction < 0 ? junction != null : junction != t.junctions[step.junction]))
                    throw ChangedNetwork("Les connexions du réseau ont changé.");
                if (step.junction >= 0 && (step.branch >= junction!.outBranches.Count ||
                    junction.outBranches[step.branch].track != (junction.inBranch.track == from ? to : from)))
                    throw ChangedNetwork("Les branches d’un aiguillage ont changé.");
            }
            var reservations = AiTrafficData.RouteReservations(out var engineers);
            var ownCars = new HashSet<string>(car.trainset?.cars.Select(c => c.CarGUID) ?? new[] { car.CarGUID });
            isAi = engineers.Overlaps(ownCars);
            if (isAi)
            {
                var authorityReason = MultiplayerData.CommandBlockReason(car);
                if (authorityReason != null) conflicts.Add(authorityReason);
                var aiReason = AiTrafficCommands.BlockReason(car, plan.path.steps.Select(s => t.native[s.track]).ToList());
                if (aiReason != null) conflicts.Add(aiReason);
            }
            if (CurrentTrack(car) != t.native[plan.path.steps[0].track]) conflicts.Add("Le train a changé de voie : recalculez le parcours.");
            if (Math.Abs(car.GetForwardSpeed()) > 0.1f) conflicts.Add("Immobilisez le train avant d’établir le parcours.");
            var occupied = new Dictionary<RailTrack, List<TrainCar>>();
            var bodies = new List<(Vector3 center, float radius)>();
            foreach (var other in TrainCarRegistry.Instance.logicCarToTrainCar.Values)
            {
                if (other == null) continue;
                bodies.Add((other.transform.TransformPoint(other.Bounds.center), other.InterCouplerDistance * 0.5f + 10));
                foreach (var track in new[] { other.FrontBogie?.track, other.RearBogie?.track })
                {
                    if (track == null) continue;
                    if (!occupied.TryGetValue(track, out var cars)) occupied[track] = cars = new List<TrainCar>();
                    cars.Add(other);
                }
            }
            foreach (var step in plan.path.steps)
            {
                var track = t.native[step.track];
                if (track == null) throw ChangedNetwork("Une voie a disparu.");
                if (occupied.TryGetValue(track, out var cars) && cars.Any(c => c != car && (car.trainset == null || c.trainset != car.trainset)))
                    conflicts.Add("Voie occupée : " + step.track);
                if (reservations.TryGetValue(track, out var owner) && !ownCars.Contains(owner)) conflicts.Add("Voie réservée par AITraffic : " + step.track);
                if (step.junction < 0) continue;
                var junction = t.junctions[step.junction];
                var junctionReason = MultiplayerData.CommandBlockReason(junction: junction);
                if (junctionReason != null) conflicts.Add("J" + step.junction + " : " + junctionReason);
                var lockReason = AiTrafficData.JunctionBlockReason(junction, isAi ? ownCars : null);
                if (lockReason != null) conflicts.Add("J" + step.junction + " : " + lockReason);
                if (junction.selectedBranch == step.branch) continue;
                if (occupied.ContainsKey(junction.inBranch.track) || junction.outBranches.Any(b => occupied.ContainsKey(b.track)) ||
                    bodies.Any(b => (b.center - junction.position).sqrMagnitude < b.radius * b.radius))
                    conflicts.Add("J" + step.junction + " : une voie adjacente est occupée (protection conservatrice).");
            }
            return conflicts.Distinct().ToList();
        }
        public static Task<object> ControlAi(string owner, string train, string action) => Updater.RunOnMainThread<object>(() => {
            if (!Main.settings.permissions.HasLocoControlPermission(owner)) throw new UnauthorizedAccessException("Permission de commande des locomotives requise.");
            MultiplayerData.RequireCommand(GetCar(train));
            return AiTrafficCommands.Control(GetCar(train), action);
        });
        public static Task<object> AssignAi(string owner, string token) => Updater.RunOnMainThread<object>(() => {
            if (!Main.settings.permissions.HasLocoControlPermission(owner) || !Main.settings.permissions.HasJunctionPermission(owner))
                throw new UnauthorizedAccessException("Permissions de commande des locomotives et aiguillages requises.");
            if (!plans.TryGetValue(token, out var plan) || plan.owner != owner || plan.expires <= DateTime.UtcNow)
                throw new InvalidOperationException("Préparation expirée ou remplacée. Recalculez le parcours.");
            plans.Remove(token);
            var conflicts = Check(plan, out var isAi);
            if (!isAi || plan.engineer == null) throw new InvalidOperationException("Le conducteur a changé. Recalculez le parcours.");
            if (conflicts.Count > 0) throw new InvalidOperationException(string.Join("\n", conflicts));
            return AiTrafficCommands.Assign(GetCar(plan.train), plan.path.steps.Select(s => plan.topology.native[s.track]).ToList(), plan.path, plan.engineer);
        });
        public static Task<object> Apply(string owner, string token) => Updater.RunOnMainThread<object>(() => {
            if (!Main.settings.permissions.HasJunctionPermission(owner)) throw new UnauthorizedAccessException("Permission de commande des aiguillages requise.");
            if (!plans.TryGetValue(token, out var plan) || plan.owner != owner || plan.expires <= DateTime.UtcNow)
                throw new InvalidOperationException("Préparation expirée ou remplacée. Recalculez le parcours.");
            plans.Remove(token); // Single use, including a refused or partially failed attempt.
            var conflicts = Check(plan, out var isAi);
            if (isAi) throw new InvalidOperationException("Utilisez l’affectation au conducteur pour ce train AI.");
            if (conflicts.Count > 0) throw new InvalidOperationException(string.Join("\n", conflicts));
            int changed = 0;
            try
            {
                // Preflight and switching run in one main-thread callback, without yielding.
                foreach (var step in plan.path.steps.Where(s => s.junction >= 0))
                {
                    var junction = plan.topology.junctions[step.junction];
                    if (junction.selectedBranch == step.branch) continue;
                    MultiplayerData.RequireCommand(junction: junction);
                    if (AiTrafficData.JunctionBlockReason(junction) != null) throw new InvalidOperationException("Un verrou AI a changé.");
                    junction.Switch(Junction.SwitchMode.REGULAR, step.branch);
                    if (junction.selectedBranch != step.branch) throw new InvalidOperationException("Un aiguillage a refusé la commande.");
                    changed++;
                }
            }
            catch (Exception e) { throw new InvalidOperationException($"Commande interrompue après {changed} changements ; vérifiez la carte. {e.Message}"); }
            return new { changed, message = "Aiguillages positionnés. Le parcours n’est pas réservé ; respectez les signaux et surveillez le trafic." };
        });
    }
}
