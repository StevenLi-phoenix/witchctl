using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Witch.UI.Automation;

namespace WitchCtl
{
    /// <summary>One HTTP request as the handlers see it.</summary>
    internal sealed class Req
    {
        public readonly string Body;
        private readonly NameValueCollection _query;

        public Req(string body, NameValueCollection query)
        {
            Body = body;
            _query = query;
        }

        /// <summary>?name=value, or null.</summary>
        public string Value(string name) => _query?[name];

        /// <summary>?name=1 / ?name=true / bare ?name.</summary>
        public bool Flag(string name)
        {
            var value = _query?[name];
            return value != null && value != "0" && !value.Equals("false", StringComparison.OrdinalIgnoreCase);
        }
    }

    internal sealed class Route
    {
        public string Method;
        public string Path;
        public bool OnMainThread;
        public Func<Req, UniTask<string>> Handler;
    }

    internal static class Routes
    {
        internal const string Version = "0.2.0";

        private static readonly List<Route> All = new List<Route>
        {
            // Background-thread route: proves the transport, never touches Unity.
            Bg("GET", "/ping", _ => UniTask.FromResult(Json.Write(new
            {
                ok = true,
                version = Version,
                audioMuted = Audio.Muted,
                served = Bridge.Counters.served,
                failed = Bridge.Counters.failed,
                thread = Thread.CurrentThread.ManagedThreadId,
                lastError = Battle.LastError,
            }))),

            // Everything below reads or drives the game, so it runs on the Unity main thread.
            Main("GET", "/state", r => UniTask.FromResult(Json.Write(StateReader.Read(r.Flag("full"))))),
            Main("GET", "/actions", _ => UniTask.FromResult(Json.Write(StateReader.ReadActions()))),

            // Read-only diagnostics for "why is the game in this state" questions.
            Main("GET", "/debug", _ => UniTask.FromResult(Json.Write(Diagnostics.Read()))),
            Main("GET", "/world", r =>
            {
                var depth = int.TryParse(r.Value("depth"), out var d) ? d : 4;
                var window = r.Value("window");
                return UniTask.FromResult(Json.Write(string.IsNullOrEmpty(window) ? World.Dump(r.Value("root"), depth) : World.DumpWindow(window, depth)));
            }),

            // Post-fight screens and battle go through our own code (see Screens.cs, Battle.cs): the game's service would play the hand
            // and does not know the reward screens. Everything else is the game's own action service.
            Main("POST", "/act", async r =>
            {
                var req = Json.Read<RuntimePerformActionRequest>(r.Body);
                if (Tutorial.Owns(req.ActionId)) return Json.Write(Tutorial.Perform(req.ActionId));
                if (Hub.Owns(req.ActionId))
                {
                    var again = SingleShot.Check(req.ActionId);
                    if (again != null) return Json.Write(new RuntimePerformActionResult { Success = false, ActionId = req.ActionId, Message = again });
                    var started = Hub.Perform(req.ActionId);
                    if (started.Success) SingleShot.Record(req.ActionId);
                    return Json.Write(started);
                }
                if (Rest.Owns(req.ActionId)) return Json.Write(Rest.Perform(req.ActionId));
                if (SlotMode.Owns(req.ActionId)) return Json.Write(SlotMode.Perform(req.ActionId));
                if (MapNodes.Owns(req.ActionId)) return Json.Write(MapNodes.Perform(req.ActionId));
                if (GameEntry.Owns(req.ActionId)) return Json.Write(GameEntry.Perform(req.ActionId));
                if (Screens.Owns(req.ActionId)) return Json.Write(Screens.Perform(req.ActionId));
                if (Battle.IsActive()) return Json.Write(await Battle.PerformAsync(req.ActionId));
                var refusal = MapGate.Check(req.ActionId) ?? SingleShot.Check(req.ActionId);
                if (refusal != null) return Json.Write(new RuntimePerformActionResult { Success = false, ActionId = req.ActionId, Message = refusal });
                var result = await RuntimeGameplayAutomationService.PerformActionAsync(req);
                if (result.Success) SingleShot.Record(req.ActionId);
                return Json.Write(result);
            }),

            Main("POST", "/play", async r => Json.Write(await Battle.PlayAsync(Json.Read<Battle.PlayRequest>(r.Body)))),

            // Fast UI path (a few ms): active windows only, direct pointer click.
            Main("GET", "/ui", _ => UniTask.FromResult(Json.Write(UiLite.Dump()))),
            Main("POST", "/click", r =>
            {
                var req = Json.Read<ClickRequest>(r.Body);
                return UniTask.FromResult(Json.Write(UiLite.Click(req.Id, req.Path, req.Text, req.Right)));
            }),

            // The game's own UI service, passed through as-is. Thorough but slow (about 0.8 s and 2.4 s per call).
            Main("POST", "/ui/full", r =>
                UniTask.FromResult(Json.Write(RuntimeUiAutomationService.CaptureSnapshot(Json.Read<RuntimeUiSnapshotRequest>(r.Body))))),

            Main("POST", "/ui/do", async r =>
                Json.Write(await RuntimeUiAutomationService.InteractAsync(Json.Read<RuntimeUiInteractionRequest>(r.Body)))),

            Main("POST", "/scene", r =>
                UniTask.FromResult(Json.Write(RuntimeSceneAutomationService.CaptureSnapshot(Json.Read<RuntimeSceneSnapshotRequest>(r.Body))))),

            Main("POST", "/scene/do", async r =>
                Json.Write(await RuntimeSceneAutomationService.InteractAsync(Json.Read<RuntimeSceneInteractionRequest>(r.Body)))),

            // The game's config tables as data (whole rows, filtered by a keyword): `witchctl raw GET '/config?table=Enemy&match=魔王'`.
            Main("GET", "/config", r => UniTask.FromResult(Json.Write(ConfigDump.Dump(r.Value("table"), r.Value("match"), int.TryParse(r.Value("limit"), out var n) ? n : 20)))),

            // The game's clock, for the lab: animations, tweens and waits run `scale` times faster (a classic run takes minutes, not half an hour).
            Main("GET", "/speed", _ => UniTask.FromResult(Json.Write(new { timeScale = Time.timeScale }))),
            Main("POST", "/speed", r =>
            {
                var req = Json.Read<SpeedRequest>(r.Body);
                Time.timeScale = Mathf.Clamp(req.Scale, 0.25f, 4f);
                UnityEngine.Debug.Log("[WitchCtl] timeScale " + Time.timeScale);
                return UniTask.FromResult(Json.Write(new { timeScale = Time.timeScale }));
            }),

            Main("POST", "/audio", r =>
            {
                var req = Json.Read<AudioRequest>(r.Body);
                Audio.Muted = !req.On;
                Audio.Apply();
                return UniTask.FromResult(Json.Write(new { audioMuted = Audio.Muted }));
            }),
        };

        internal static Route Find(string method, string path)
        {
            foreach (var route in All)
                if (route.Method == method && route.Path == path) return route;
            return null;
        }

        private static Route Bg(string method, string path, Func<Req, UniTask<string>> handler) =>
            new Route { Method = method, Path = path, OnMainThread = false, Handler = handler };

        private static Route Main(string method, string path, Func<Req, UniTask<string>> handler) =>
            new Route { Method = method, Path = path, OnMainThread = true, Handler = handler };

        private sealed class ClickRequest
        {
            public int? Id { get; set; }
            public string Path { get; set; }
            public string Text { get; set; }
            public bool Right { get; set; }
        }

        private sealed class AudioRequest
        {
            public bool On { get; set; }
        }

        private sealed class SpeedRequest
        {
            public float Scale { get; set; }
        }
    }
}
