using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityModManagerNet;

namespace DvMod.RemoteDispatch
{
    public static class MultiplayerData
    {
        public sealed class State
        {
            public string status = "unavailable";
            public string? version;
            public bool canCommand;
            public List<Player> players = new List<Player>();
        }
        public sealed class Player
        {
            public string id = "", name = "", crew = "";
            public bool host;
            public float[] position = Array.Empty<float>();
            public float rotation;
            public string? car;
        }
        private sealed class Context
        {
            public State state = new State();
            public object? api, server, client;
            public Assembly? assembly;
        }
        private static Type? apiType;
        public static void Reset() => apiType = null;
        private static object? Read(object obj, string member) => AiTrafficData.Read(obj, member);
        private static Context Current()
        {
            var result = new Context();
            var mod = UnityModManager.FindMod("Multiplayer");
            if (mod?.Active != true || mod.Assembly == null) { result.state.canCommand = true; return result; }
            result.assembly = mod.Assembly;
            apiType ??= AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("MPAPI.MultiplayerAPI")).FirstOrDefault(t => t != null);
            if (apiType == null) throw new InvalidOperationException("API Multiplayer introuvable.");
            result.api = Read(apiType, "Instance");
            if (result.api == null) { result.state.status = "initializing"; return result; }
            result.state.version = Convert.ToString(Read(apiType, "MultiplayerVersion"));
            if (!Convert.ToBoolean(Read(result.api, "IsConnected"))) { result.state.status = "disconnected"; return result; }
            result.server = Read(apiType, "Server"); result.client = Read(apiType, "Client");
            bool host = Convert.ToBoolean(Read(result.api, "IsHost"));
            bool solo = Convert.ToBoolean(Read(result.api, "IsSinglePlayer"));
            bool dedicated = Convert.ToBoolean(Read(result.api, "IsDedicatedServer"));
            result.state.status = solo ? "singleplayer" : dedicated ? "dedicated" : host ? "host" : "client";
            // NetworkedJunction emits through the local client even on the host.
            result.state.canCommand = host && !dedicated && result.client != null && Convert.ToBoolean(Read(result.client, "IsConnected"));
            return result;
        }
        public static object? SessionIdentity()
        {
            var c = Current(); return c.server ?? c.client ?? c.api;
        }
        public static IEnumerable<object?> ReadRows(State target)
        {
            var context = Current();
            target.status = context.state.status; target.version = context.state.version; target.canCommand = context.state.canCommand;
            var provider = context.server ?? context.client;
            if (provider == null) yield break;
            int localId = context.client == null ? -1 : Convert.ToInt32(Read(context.client, "PlayerId"));
            var players = AiTrafficData.CopyList(Read(provider, "Players"));
            foreach (var player in players)
            {
                int id = Convert.ToInt32(Read(player, "PlayerId"));
                if (id == localId || !Convert.ToBoolean(Read(player, "IsLoaded"))) continue;
                var p = new World.Position((Vector3)Read(player, "Position")! - WorldMover.currentMove).ToLatLon();
                var car = Read(player, "OccupiedCar") as TrainCar;
                target.players.Add(new Player { id = "mp-" + id, name = Convert.ToString(Read(player, "Username")) ?? "",
                    crew = Convert.ToString(Read(player, "CrewName")) ?? "", host = Convert.ToBoolean(Read(player, "IsHost")),
                    position = new[] { p.latitude, p.longitude }, rotation = Convert.ToSingle(Read(player, "RotationY")), car = car?.ID });
                yield return null;
            }
        }
        public static string? CommandBlockReason(TrainCar? car = null, Junction? junction = null)
        {
            try
            {
                var c = Current();
                if (!c.state.canCommand) return "Multiplayer : utilisez la carte de l’hôte connecté pour envoyer les commandes (état : " + c.state.status + ").";
                if (c.state.status == "unavailable") return null;
                if (junction != null)
                {
                    var type = c.assembly!.GetType("Multiplayer.Components.Networking.World.NetworkedJunction", true)!;
                    var networked = junction.GetComponent(type);
                    if (networked == null || !Convert.ToBoolean(Read(networked, "initialised")) || Convert.ToUInt16(Read(networked, "NetId")) == 0)
                        return "Multiplayer : aiguillage non synchronisé. Attendez la fin du chargement.";
                }
                if (car != null)
                {
                    var cars = car.trainset?.cars ?? new List<TrainCar> { car };
                    foreach (var player in AiTrafficData.CopyList(Read(c.server!, "Players")))
                    {
                        var occupied = Read(player, "OccupiedCar") as TrainCar;
                        if (occupied != null && cars.Contains(occupied)) return "Multiplayer : un joueur est présent sur ce train. Libérez-le avant la commande distante.";
                    }
                    var type = c.assembly!.GetType("Multiplayer.Components.Networking.Train.NetworkedTrainCar", true)!;
                    foreach (var loco in cars.Where(t => t.IsLoco))
                    {
                        var networked = loco.GetComponent(type);
                        if (networked == null || Convert.ToUInt16(Read(networked, "NetId")) == 0 || Read(networked, "simulationFlow") == null)
                            return "Multiplayer : locomotive pas encore synchronisée.";
                        if (((IDictionary)Read(networked, "portAuthority")!).Count != 0)
                            return "Multiplayer : commandes de locomotive détenues par un joueur.";
                    }
                }
                return null;
            }
            catch { return "Multiplayer : état réseau ou autorité indisponible ; commande refusée."; }
        }
        public static void RequireCommand(TrainCar? car = null, Junction? junction = null)
        {
            var reason = CommandBlockReason(car, junction);
            if (reason != null) throw new InvalidOperationException(reason);
        }
    }
}
