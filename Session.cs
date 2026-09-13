using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DvMod.RemoteDispatch
{
    public static class Sessions
    {
        private static readonly TimeSpan SessionTimeout = TimeSpan.FromMinutes(5);
        private const int MaximumSessions = 64;
        private static readonly object allSesssionsLock = new object();
        private static readonly Dictionary<string, Session> allSessions = new Dictionary<string, Session>();
        private static readonly HashSet<string> AllTags = new HashSet<string>() { "cars", "jobs", "junctions", "player" };

        public static event Action<string>? OnSessionStarted;
        public static event Action<string>? OnSessionEnded;

        private class Session
        {
            public readonly string username;
            public readonly AsyncSet<string> pendingTags = new AsyncSet<string>();
            public readonly Stopwatch timeSinceLastFetch = new Stopwatch();
            public readonly CancellationTokenSource cancellation = new CancellationTokenSource();

            public Session(string username)
            {
                this.username = username;
                foreach (var tag in AllTags)
                    pendingTags.Add(tag);
            }
        }

        public static HashSet<string> GetUsersWithActiveSessions()
        {
            lock (allSesssionsLock)
                return new HashSet<string>(allSessions.Values.Select(s => s.username));
        }

        public static void AddTag(string tag)
        {
            lock (allSesssionsLock)
            {
                RemoveExpiredSessionsLocked();
                foreach (var session in allSessions.Values) session.pendingTags.Add(tag);
            }
        }

        private static async Task<IEnumerable<string>> GetTags(string username, string sessionId, CancellationToken cancellationToken = default)
        {
            Session session;
            string? startedUser = null;
            lock (allSesssionsLock)
            {
                RemoveExpiredSessionsLocked();
                if (!allSessions.TryGetValue(sessionId, out var existingSession))
                {
                    if (allSessions.Count >= MaximumSessions) throw new InvalidOperationException("Too many active browser sessions.");
                    Main.DebugLog(() => $"Starting new session {sessionId} for user {username}");
                    session = new Session(username);
                    allSessions.Add(sessionId, session);
                    startedUser = username;
                }
                else
                {
                    session = existingSession;
                    if (!string.Equals(session.username, username, StringComparison.Ordinal))
                        throw new UnauthorizedAccessException("This browser session belongs to another identity.");
                }
            }

            if (startedUser != null)
                await Updater.RunOnMainThread(() => OnSessionStarted?.Invoke(startedUser)).ConfigureAwait(false);

            session.timeSinceLastFetch.Restart();

            var tags = new HashSet<string>(session.pendingTags.TakeAll());
            if (tags.Count > 0)
                return tags;

            // No data available
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(session.cancellation.Token, cancellationToken))
            {
                var (success, awaitedTag) = await session.pendingTags.TryTakeAsync(TimeSpan.FromSeconds(25), linked.Token).ConfigureAwait(false);
                return success ? new string[1] { awaitedTag } : new string[0];
            }
        }

        private static void RemoveExpiredSessionsLocked()
        {
            foreach (var item in allSessions.Where(item => item.Value.timeSinceLastFetch.Elapsed > SessionTimeout).ToArray())
            {
                allSessions.Remove(item.Key);
                item.Value.cancellation.Cancel();
                var username = item.Value.username;
                _ = Updater.RunOnMainThread(() => OnSessionEnded?.Invoke(username));
            }
        }

        public static void Reset()
        {
            Session[] sessions;
            lock (allSesssionsLock)
            {
                sessions = allSessions.Values.ToArray();
                allSessions.Clear();
            }
            foreach (var session in sessions) session.cancellation.Cancel();
        }

        private static Task<JObject?> GetUpdateForCarGuidAsync(string carGuid)
        {
            return CarData.GetCarGuidDataJsonAsync(carGuid);
        }

        private static async Task<JObject> GetUpdateForTrainsetAsync(string trainsetId)
        {
            return await CarData.GetTrainsetDataJsonAsync(int.Parse(trainsetId)).ConfigureAwait(false);
        }

        private static async Task<JToken?> GetUpdateForSplitTagAsync(string tag)
        {
            var index = tag.IndexOf('-');
            var tagType = tag.Substring(0, index);
            var tagId = tag.Substring(index + 1);
            return tagType switch
            {
                "carguid" => await GetUpdateForCarGuidAsync(tagId).ConfigureAwait(false),
                "trainset" => await GetUpdateForTrainsetAsync(tagId).ConfigureAwait(false),
                _ => throw new NotImplementedException($"Unexpected update tag {tag}"),
            };
        }

        private static async Task<JToken?> GetUpdateForTagAsync(string tag)
        {
            switch (tag)
            {
                case "cars": return await CarData.GetAllCarDataJsonAsync().ConfigureAwait(false);
                case "jobs": return JObject.FromObject(await JobData.GetAllJobDataAsync().ConfigureAwait(false));
                case "junctions": return new JArray(await Updater.RunOnMainThread(Junctions.GetAllJunctionStates).ConfigureAwait(false));
                case "player": return await PlayerData.GetPlayerDataAsync().ConfigureAwait(false);
                default: return tag.Contains('-')
                    ? await GetUpdateForSplitTagAsync(tag).ConfigureAwait(false)
                    : throw new NotImplementedException($"Unexpected update tag {tag}");
            }
        }

        public static async Task<string> GetUpdates(string username, string sessionId)
        {
            var tags = await GetTags(username, sessionId).ConfigureAwait(false);
            var updates = new Dictionary<string, JToken?>(StringComparer.Ordinal);
            foreach (var tag in tags)
                updates[tag] = await GetUpdateForTagAsync(tag).ConfigureAwait(false);
            return JsonConvert.SerializeObject(updates);
        }

        public static async Task<string> GetUpdateNotifications(string username, string sessionId, CancellationToken cancellationToken)
        {
            var tags = await GetTags(username, sessionId, cancellationToken).ConfigureAwait(false);
            return JsonConvert.SerializeObject(new { tags = tags.ToArray() });
        }
    }
}
