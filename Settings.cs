using System.Linq;
using UnityModManagerNet;
using UnityEngine;
using System.Collections.Generic;
using System;

namespace DvMod.RemoteDispatch
{
    public class Settings : UnityModManager.ModSettings
    {
        public int serverPort = 7245;
        public string serverPassword = "";
        public bool allowRemoteConnections = false;
        public Permissions permissions = new Permissions();
        public bool showUndiscoveredLocomotives = false;
        public bool enableLogging = false;

        public readonly string? version = Main.mod?.Info.Version;

        const char EnDash = '\u2013';
        private string uncommittedPort = "initial";
        private string message = "";

        public void Draw()
        {
            GUILayout.BeginVertical(GUILayout.ExpandWidth(false));

            if (uncommittedPort == "initial")
                uncommittedPort = serverPort.ToString();

            GUILayout.Label($"Network port (1024{EnDash}65535)");
            uncommittedPort = GUILayout.TextField(uncommittedPort, maxLength: 5);
            uncommittedPort = new string(uncommittedPort.Where(c => char.IsDigit(c)).ToArray());
            bool isValidPort = int.TryParse(uncommittedPort, out var parsed) && parsed >= 1024 && parsed <= 65535;
            if (isValidPort && parsed != serverPort)
            {
                serverPort = parsed;
                message = "Port saved; restart the mod to apply it.";
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label(message);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Password (blank for none)");
            serverPassword = GUILayout.TextField(serverPassword);
            GUILayout.EndHorizontal();

            allowRemoteConnections = GUILayout.Toggle(allowRemoteConnections,
                "Allow remote browser connections (requires a password and restart)");

            permissions.Draw();

            var newShowUndiscoveredLocomotives = GUILayout.Toggle(
                showUndiscoveredLocomotives,
                "Show undiscovered locomotives");
            if (newShowUndiscoveredLocomotives != showUndiscoveredLocomotives)
            {
                showUndiscoveredLocomotives = newShowUndiscoveredLocomotives;
                CarUpdater.ForceCarRefresh();
            }

            enableLogging = GUILayout.Toggle(enableLogging, "Enable logging");

            GUILayout.EndVertical();
        }

        override public void Save(UnityModManager.ModEntry entry)
        {
            Save<Settings>(this, entry);
        }
    }

    public class Permissions
    {
        private const int MaximumKnownUsers = 64;
        private readonly object permissionsLock = new object();
        private bool attached;
        public class PlayerPermissions
        {
            public string name;
            public bool canToggleJunctions;
            public bool canControlLocomotives;
            public bool canManageCompany;

            public PlayerPermissions()
            {
                name = "";
            }

            public PlayerPermissions(string name)
            {
                this.name = name;
            }
        }

        public readonly List<PlayerPermissions> permissions = new List<PlayerPermissions>();

        public Permissions()
        {
        }

        public void Attach()
        {
            if (attached) return;
            Sessions.OnSessionStarted += OnSessionStarted;
            attached = true;
        }

        public bool HasJunctionPermission(string username)
        {
            lock (permissionsLock) return permissions.Find(p => p.name == username)?.canToggleJunctions ?? false;
        }

        public bool HasLocoControlPermission(string username)
        {
            lock (permissionsLock) return permissions.Find(p => p.name == username)?.canControlLocomotives ?? false;
        }

        public bool HasCompanyPermission(string username)
        {
            lock (permissionsLock) return permissions.Find(p => p.name == username)?.canManageCompany ?? false;
        }

        private void OnSessionStarted(string username)
        {
            lock (permissionsLock)
            {
                if (!permissions.Any(p => p.name == username) && permissions.Count < MaximumKnownUsers)
                {
                    permissions.Add(new PlayerPermissions(username));
                    permissions.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.name, b.name));
                }
            }
        }

        public void Draw()
        {
            lock (permissionsLock)
            {
                GUILayout.Label("Dispatcher permissions:");
                GUILayout.BeginHorizontal("box", GUILayout.ExpandWidth(false));
                DrawNamesColumn();
                DrawConnectedColumn();
                DrawJunctionsColumn();
                DrawLocoControlColumn();
                DrawCompanyColumn();
                GUILayout.EndHorizontal();
            }
        }

        private void DrawColumn(string label, Action<PlayerPermissions> action)
        {
            GUILayout.BeginVertical();
            GUILayout.Label(label);
            foreach (var p in permissions)
                action(p);
            GUILayout.EndVertical();
        }

        private void DrawNamesColumn()
        {
            DrawColumn("Name", p => GUILayout.Label(p.name));
        }

        private void DrawConnectedColumn()
        {
            var connectedUsers = Sessions.GetUsersWithActiveSessions();
            DrawColumn("Connected", p => GUILayout.Toggle(connectedUsers.Contains(p.name), ""));
        }

        private void DrawJunctionsColumn()
        {
            DrawColumn("Junctions", p => p.canToggleJunctions = GUILayout.Toggle(p.canToggleJunctions, ""));
        }

        private void DrawLocoControlColumn()
        {
            DrawColumn("Locomotive Control", p => p.canControlLocomotives = GUILayout.Toggle(p.canControlLocomotives, ""));
        }

        private void DrawCompanyColumn()
        {
            DrawColumn("BDVM", p => p.canManageCompany = GUILayout.Toggle(p.canManageCompany, ""));
        }
    }
}
