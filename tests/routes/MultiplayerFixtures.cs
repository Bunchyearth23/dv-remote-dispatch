using System;
using System.Collections.Generic;
using DvMod.RemoteDispatch;

public static class WorldMover { public static UnityEngine.Vector3 currentMove; }
namespace DvMod.RemoteDispatch
{
    public static class World
    {
        public struct Position
        {
            UnityEngine.Vector3 v; public Position(UnityEngine.Vector3 v){this.v=v;}
            public (float latitude,float longitude) ToLatLon()=>(v.z*0.000009f,v.x*0.000009f);
        }
    }
}
namespace MPAPI
{
    public static class MultiplayerAPI
    {
        public static object? Instance;
        public static object? Server,Client;
        public static string MultiplayerVersion="local-beta";
    }
    public class Api { public bool IsConnected=true,IsHost=true,IsSinglePlayer=false,IsDedicatedServer=false; }
    public class Peer
    {
        public bool IsConnected=true;public byte PlayerId;
        public List<Player> Players=new();
    }
    public class Player
    {
        public byte PlayerId;public string Username="Crew mate",CrewName="Team";
        public bool IsLoaded=true,IsHost=false;
        public UnityEngine.Vector3 Position;public float RotationY;
        public TrainCar? OccupiedCar;
    }
}
namespace Multiplayer.Components.Networking.World
{
    public class NetworkedJunction {public bool initialised=true; public ushort NetId=1;}
}
namespace Multiplayer.Components.Networking.Train
{
    public class NetworkedTrainCar {public ushort NetId=1;public object? simulationFlow=new();public Dictionary<uint,object> portAuthority=new();}
}
static class MultiplayerChecks
{
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    static MultiplayerData.State Snapshot(){var state=new MultiplayerData.State();foreach(var unused in MultiplayerData.ReadRows(state)){}return state;}
    public static void Run()
    {
        var mod=UnityModManagerNet.UnityModManager.MpMod;
        Check(MultiplayerData.CommandBlockReason()==null,"Missing optional mod blocked solo");
        mod.Active=true;Check(MultiplayerData.CommandBlockReason()!=null,"Uninitialized API allowed write");
        var api=new MPAPI.Api();MPAPI.MultiplayerAPI.Instance=api;
        var server=new MPAPI.Peer();var client=new MPAPI.Peer{PlayerId=0};MPAPI.MultiplayerAPI.Server=server;MPAPI.MultiplayerAPI.Client=client;
        var local=new MPAPI.Player{PlayerId=0};var remote=new MPAPI.Player{PlayerId=1,Position=new(){x=100,z=200}};
        server.Players.Add(local);server.Players.Add(remote);server.Players.Add(new(){PlayerId=2,IsLoaded=false});client.Players.Add(remote);
        WorldMover.currentMove=new(){x=10,z=20};
        var state=Snapshot();Check(state.status=="host"&&state.canCommand&&state.players.Count==1,"Host roster duplicated local or included loading player");
        Check(Math.Abs(state.players[0].position[0]-180*0.000009f)<0.000001,"World offset not normalized");
        Check(state.players[0].crew=="Team","Crew missing");
        var track=new RailTrack("mp");var car=new TrainCar("mp-car",track);var junction=new Junction();
        Check(MultiplayerData.CommandBlockReason(junction:junction)!=null,"Unnetworked junction allowed");
        var nj=new Multiplayer.Components.Networking.World.NetworkedJunction();junction.Components[nj.GetType()]=nj;
        Check(MultiplayerData.CommandBlockReason(junction:junction)==null,"Initialized host junction blocked");nj.initialised=false;
        Check(MultiplayerData.CommandBlockReason(junction:junction)!=null,"Initializing junction allowed");nj.initialised=true;
        var nc=new Multiplayer.Components.Networking.Train.NetworkedTrainCar();car.Components[nc.GetType()]=nc;
        Check(MultiplayerData.CommandBlockReason(car)==null,"Free locomotive blocked");
        nc.portAuthority[1]=remote;Check(MultiplayerData.CommandBlockReason(car)!=null,"Claimed control allowed");nc.portAuthority.Clear();
        remote.OccupiedCar=car;Check(MultiplayerData.CommandBlockReason(car)!=null,"Occupied consist allowed");remote.OccupiedCar=null;
        api.IsHost=false;MPAPI.MultiplayerAPI.Server=null;Check(Snapshot().status=="client"&&MultiplayerData.CommandBlockReason()!=null,"Client allowed write");
        bool rejected=false;try{MultiplayerData.RequireCommand(junction:junction);}catch(InvalidOperationException){rejected=true;}Check(rejected,"Client mutation guard failed");
        var session=MultiplayerData.SessionIdentity();MPAPI.MultiplayerAPI.Client=new MPAPI.Peer();Check(!ReferenceEquals(session,MultiplayerData.SessionIdentity()),"Session replacement invisible");
        MPAPI.MultiplayerAPI.Server=server;api.IsHost=true;api.IsDedicatedServer=true;Check(MultiplayerData.CommandBlockReason()!=null,"Unsupported dedicated write allowed");api.IsDedicatedServer=false;
        api.IsConnected=false;Check(MultiplayerData.CommandBlockReason()!=null,"Disconnected write allowed");
        MPAPI.MultiplayerAPI.Instance=new object();Check(MultiplayerData.CommandBlockReason()!=null,"Unknown API allowed write");
        MPAPI.MultiplayerAPI.Instance=api;api.IsConnected=true;server.Players.Clear();Check(Snapshot().players.Count==0,"Departed player retained");
        mod.Active=false;MultiplayerData.Reset();Check(MultiplayerData.CommandBlockReason()==null,"Return to solo blocked");
        Console.WriteLine("PASS Multiplayer: absent/initializing/host/client/dedicated/disconnected/incompatible, loaded roster and world offset, session identity, synchronized junctions, locomotive authority and occupied trains, disconnect cleanup.");
    }
}
