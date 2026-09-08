using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System;
using UnityEngine;

namespace DvMod.RemoteDispatch
{
    public class HttpServer : MonoBehaviour
    {
        private static GameObject? rootObject;
        private readonly HttpListener listener = new HttpListener();

        public async void Start()
        {
            if (!listener.IsListening)
            {
                listener.Prefixes.Add($"http://*:{Main.settings.serverPort}/");
                listener.AuthenticationSchemes = AuthenticationSchemes.Anonymous | AuthenticationSchemes.Basic;
                listener.Realm = "DV Remote Dispatch";
                Main.DebugLog(() => $"Starting HTTP server on port {Main.settings.serverPort}");
                listener.Start();
            }

            while (listener.IsListening)
            {
                try
                {
                    var context = await listener.GetContextAsync().ConfigureAwait(true);
                    if (CheckAuthentication(context))
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await HandleRequest(context).ConfigureAwait(false);
                            }
                            catch (Exception e)
                            {
                                Main.DebugLog(() => $"Exception while handling HTTP request ({context.Request.Url}): {e}");
                                try { RenderEmpty(context, 503); } catch { /* Client may have disconnected. */ }
                            }
                        });
                    }
                    else
                    {
                        context.Response.Headers.Add("WWW-Authenticate", "Basic");
                        RenderEmpty(context, 401);
                    }
                }
                catch (ObjectDisposedException e) when (e.ObjectName == "listener")
                {
                    // ignore when OnDestroy() is called to shutdown the server
                }
            }
        }

        public void OnDestroy()
        {
            if (listener.IsListening)
            {
                Main.DebugLog(() => "Stopping HTTP server");
                listener.Stop();
                listener.Prefixes.Clear();
            }
        }

        private static bool CheckAuthentication(HttpListenerContext context)
        {
            string serverPassword = Main.settings.serverPassword;
            return context.User?.Identity is HttpListenerBasicIdentity identity && (string.IsNullOrEmpty(serverPassword) || identity.Password == serverPassword);
        }

        private static async Task HandleRequest(HttpListenerContext context)
        {
            var request = context.Request;
            if (request.Url.Segments.Length < 2)
            {
                context.Response.ContentType = ContentTypes.Html;
                RenderResource(context, "index.html");
                return;
            }

            switch (request.Url.Segments[1].TrimEnd('/'))
            {
            case "route":
                await HandleRouteRequest(context).ConfigureAwait(false);
                break;
            case "bdvm":
                await HandleBDVMRequest(context).ConfigureAwait(false);
                break;
            case "infrastructure":
                context.Response.Headers["Cache-Control"] = "no-store";
                Render200(context, ContentTypes.Json, await InfrastructureData.GetJson().ConfigureAwait(false));
                break;
            case "car":
                await HandleCarRequest(context).ConfigureAwait(false);
                break;
            case "job":
                Render200(context, ContentTypes.Json, JobData.GetAllJobDataJson());
                break;
            case "junction":
                await HandleJunctionRequest(context).ConfigureAwait(false);
                break;
            case "junctionState":
                Render200(context, ContentTypes.Json, await Updater.RunOnMainThread(Junctions.GetJunctionStateJSON).ConfigureAwait(false));
                break;
            case "player":
                var playerJson = PlayerData.GetPlayerDataJson();
                if (playerJson != null)
                    Render200(context, ContentTypes.Json, playerJson);
                else
                    RenderEmpty(context, 500);
                break;
            case "res":
                RenderResource(context);
                break;
            case "track":
                Render200(context, ContentTypes.Json, await RailTracks.GetTrackPointJSON().ConfigureAwait(false));
                break;
            case "trainset":
                HandleTrainsetRequest(context);
                break;
            case "updates":
                await HandleUpdatesRequest(context).ConfigureAwait(false);
                break;
            default:
                RenderEmpty(context, 404);
                break;
            }
        }

        private static async Task HandleBDVMRequest(HttpListenerContext context)
        {
            context.Response.Headers["Cache-Control"] = "no-store";
            try
            {
                var request = context.Request; var user = context.User?.Identity?.Name ?? "";
                if (!Main.settings.permissions.HasCompanyPermission(user)) { RenderEmpty(context, 403); return; }
                string result;
                if (request.HttpMethod == "GET") result = await Updater.RunOnMainThread(() => BDVMIntegration.GetState(user)).ConfigureAwait(false);
                else if (request.HttpMethod == "POST")
                {
                    if (!(request.ContentType ?? "").StartsWith("application/json", StringComparison.OrdinalIgnoreCase)) { RenderEmpty(context, 415); return; }
                    var origin = request.Headers["Origin"]; if (origin != null && (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri) || originUri.Authority != request.Url.Authority)) { RenderEmpty(context, 403); return; }
                    if (request.ContentLength64 < 0 || request.ContentLength64 > 4096) { RenderEmpty(context, 413); return; }
                    using var reader = new StreamReader(request.InputStream, Encoding.UTF8); var payload = await reader.ReadToEndAsync().ConfigureAwait(false); JObject.Parse(payload);
                    result = await Updater.RunOnMainThread(() => BDVMIntegration.SubmitIntent(user, payload)).ConfigureAwait(false);
                }
                else { RenderEmpty(context, 405); return; }
                Render200(context, ContentTypes.Json, result);
            }
            catch (Exception exception)
            {
                context.Response.StatusCode = exception is UnauthorizedAccessException ? 403 : exception is ArgumentException || exception is JsonException ? 400 : 409;
                context.Response.ContentType = ContentTypes.Json; var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { error = exception.Message })); context.Response.ContentLength64 = bytes.Length; await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false); context.Response.Close();
            }
        }

        private static async Task HandleRouteRequest(HttpListenerContext context)
        {
            context.Response.Headers["Cache-Control"] = "no-store";
            try
            {
                var request = context.Request;
                var action = request.Url.AbsolutePath;
                object result;
                if (action == "/route/catalog" && request.HttpMethod == "GET")
                    result = await RoutePlanner.Catalog().ConfigureAwait(false);
                else if ((action == "/route/preview" || action == "/route/apply" || action == "/route/assign" || action == "/route/control-ai") && request.HttpMethod == "POST")
                {
                    if (!(request.ContentType ?? "").StartsWith("application/json", StringComparison.OrdinalIgnoreCase)) { RenderEmpty(context, 415); return; }
                    var origin = request.Headers["Origin"];
                    if (origin != null && (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri) || originUri.Authority != request.Url.Authority)) { RenderEmpty(context, 403); return; }
                    if (request.ContentLength64 < 0 || request.ContentLength64 > 4096) { RenderEmpty(context, 413); return; }
                    using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
                    var body = JObject.Parse(await reader.ReadToEndAsync().ConfigureAwait(false));
                    var owner = context.User.Identity.Name;
                    if (action == "/route/preview")
                        result = await RoutePlanner.Preview(owner, (string?)body["train"] ?? "", (string?)body["destination"] ?? "", (string?)body["via"]).ConfigureAwait(false);
                    else if (action == "/route/assign")
                        result = await RoutePlanner.AssignAi(owner, (string?)body["token"] ?? "").ConfigureAwait(false);
                    else if (action == "/route/control-ai")
                        result = await RoutePlanner.ControlAi(owner, (string?)body["train"] ?? "", (string?)body["action"] ?? "").ConfigureAwait(false);
                    else result = await RoutePlanner.Apply(owner, (string?)body["token"] ?? "").ConfigureAwait(false);
                }
                else { RenderEmpty(context, 405); return; }
                Render200(context, ContentTypes.Json, JsonConvert.SerializeObject(result));
            }
            catch (Exception e)
            {
                context.Response.StatusCode = e is UnauthorizedAccessException ? 403 : e is ArgumentException || e is JsonException ? 400 : 409;
                context.Response.ContentType = ContentTypes.Json;
                var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { error = e.Message }));
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                context.Response.Close();
            }
        }

        private static async Task HandleCarRequest(HttpListenerContext context)
        {
            var segments = context.Request.Url.Segments;
            if (segments.Length == 2 && context.Request.HttpMethod == "GET")
            {
                var allCarDataJson = CarData.GetAllCarDataJson();
                Render200(context, allCarDataJson);
                return;
            }

            if (segments.Length == 3 && context.Request.HttpMethod == "GET")
            {
                var carGuid = segments[2].TrimEnd('/');
                var carDataJson = CarData.GetCarGuidDataJson(carGuid);
                if (carDataJson == null)
                    RenderEmpty(context, 404);
                else
                    Render200(context, carDataJson);
                return;
            }

            if (segments.Length == 4 && segments[3] == "control" && context.Request.HttpMethod == "POST")
            {
                var carGuid = segments[2].TrimEnd('/');
                if (!Main.settings.permissions.HasLocoControlPermission(context.User.Identity.Name))
                {
                    RenderEmpty(context, 403);
                    return;
                }
                var status = await Updater.RunOnMainThread(() => {
                    var controller = LocoControl.GetLocoController(carGuid);
                    if (controller == null) return 404;
                    var car = TrainCarRegistry.Instance.GetTrainCarByCarGuid(carGuid);
                    if (MultiplayerData.CommandBlockReason(car) != null) return 409;
                    return LocoControl.RunCommand(controller, context.Request.QueryString) ? 204 : 400;
                }).ConfigureAwait(false);
                RenderEmpty(context, status);
                return;
            }
            RenderEmpty(context, 404);
        }

        private static async Task HandleUpdatesRequest(HttpListenerContext context)
        {
            if (context.Request.Url.Segments.Length < 3)
            {
                RenderEmpty(context, 404);
                return;
            }

            var username = context.User?.Identity?.Name ?? "";
            var sessionId = context.Request.Url.Segments[2];
            Render200(context, ContentTypes.Json, await Sessions.GetUpdates(username, sessionId).ConfigureAwait(false));
        }

        private static bool IsValidJunctionId(int junctionId)
        {
            return junctionId >= 0 && junctionId < RailTrackRegistry.Instance.OrderedJunctions.Length;
        }

        private static async Task HandleJunctionRequest(HttpListenerContext context)
        {
            var url = context.Request.Url;
            switch (url.Segments.Length)
            {
            case 2:
                Render200(context, ContentTypes.Json, await Updater.RunOnMainThread(Junctions.GetJunctionPointJSON).ConfigureAwait(false));
                break;
            case 4:
                var junctionIdString = url.Segments[2].TrimEnd('/');
                if (context.Request.HttpMethod == "POST" && int.TryParse(junctionIdString, out var junctionId) && url.Segments[3] == "toggle" && IsValidJunctionId(junctionId))
                {
                    if (!Main.settings.permissions.HasJunctionPermission(context.User.Identity.Name))
                    {
                        RenderEmpty(context, 403);
                        return;
                    }
                    var newSelectedBranch = await Updater.RunOnMainThread(() =>
                    {
                        Main.DebugLog(() => $"Toggling J-{junctionId}.");
                        var junction = RailTrackRegistry.Instance.OrderedJunctions[junctionId];
                        if (MultiplayerData.CommandBlockReason(junction: junction) != null) return -1;
                        if (AiTrafficData.JunctionBlockReason(junction) != null) return -1;
                        junction.Switch(Junction.SwitchMode.REGULAR);
                        return (int)junction.selectedBranch;
                    }).ConfigureAwait(false);
                    if (newSelectedBranch < 0) RenderEmpty(context, 409);
                    else Render200(context, new JValue(newSelectedBranch));
                    return;
                }
                RenderEmpty(context, 404);
                break;
            default:
                RenderEmpty(context, 404);
                break;
            }
        }

        public static void HandleTrainsetRequest(HttpListenerContext context)
        {
            var request = context.Request;
            if (request.Url.Segments.Length < 3)
            {
                RenderEmpty(context, 404);
                return;
            }
            var trainsetId = int.Parse(request.Url.Segments[2]);
            Render200(context, CarData.GetTrainsetDataJson(trainsetId));
        }

        public static void Create()
        {
            if (rootObject == null)
            {
                rootObject = new GameObject();
                GameObject.DontDestroyOnLoad(rootObject);
                rootObject.AddComponent<HttpServer>();
            }
        }

        public static void Destroy()
        {
            if (rootObject == null)
                return;
            // ensure server shuts down immediately, not at the end of the frame
            DestroyImmediate(rootObject);
            rootObject = null;
        }

        private static void RenderResource(HttpListenerContext context)
        {
            var resourceName = context.Request.Url.Segments[2];
            var extension = Path.GetExtension(resourceName);
            context.Response.ContentType = ContentTypes.ForExtension(extension);
            RenderResource(context, resourceName);
        }

        private static void RenderResource(HttpListenerContext context, string resourceName)
        {
            var assembly = typeof(HttpServer).Assembly;
            using var stream = assembly.GetManifestResourceStream(typeof(HttpServer), resourceName);
            if (stream == null)
            {
                RenderEmpty(context, 404);
            }
            else
            {
                stream.CopyTo(context.Response.OutputStream);
                context.Response.Close();
            }
        }

        private static class ContentTypes
        {
            public const string Css = "text/css";
            public const string Html = "text/html; charset=UTF-8";
            public const string Json = "application/json";
            public const string Javascript = "application/javascript";
            public const string Png = "image/png";
            public const string Svg = "image/svg+xml";

            public static string ForExtension(string extension)
            {
                return extension switch
                {
                    ".css" => Css,
                    ".js" => Javascript,
                    ".json" => Json,
                    ".png" => Png,
                    ".svg" => Svg,
                    _ => "",
                };
            }
        }

        private static void Render200(HttpListenerContext context, JToken json)
        {
            Render200(context, ContentTypes.Json, JsonConvert.SerializeObject(json));
        }

        private static void Render200(HttpListenerContext context, string contentType, string s)
        {
            context.Response.ContentType = contentType;
            var bytes = Encoding.UTF8.GetBytes(s);
            if (bytes.Length > 128 && (context.Request.Headers.GetValues("Accept-Encoding")?.Contains("gzip") ?? false))
            {
                context.Response.Headers.Add("Content-Encoding", "gzip");
                var mem = new MemoryStream(bytes);
                using var gzip = new GZipStream(context.Response.OutputStream, CompressionMode.Compress);
                mem.CopyTo(gzip);
            }
            else
            {
                context.Response.Close(bytes, false);
            }
        }

        private static void RenderEmpty(HttpListenerContext context, int statusCode)
        {
            context.Response.StatusCode = statusCode;
            context.Response.Close();
        }
    }
}
