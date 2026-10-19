using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace WitchCtl
{
    /// <summary>
    /// Loopback HTTP server. A dedicated thread blocks in accept (no polling); handlers that touch the game
    /// hop to the Unity main thread through <see cref="Pump"/>, so the worst-case added latency is one frame.
    /// </summary>
    internal static class Bridge
    {
        private const int SlowRequestMs = 100;
        private const int MainThreadTimeoutMs = 10000;

        private static HttpListener _listener;
        private static long _served;
        private static long _failed;

        internal static void Start(int port)
        {
            var go = new GameObject("WitchCtl");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Pump>();

            _listener = new HttpListener();
            _listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
            _listener.Start();
            new Thread(AcceptLoop) { IsBackground = true, Name = "WitchCtl.http" }.Start();
            UnityEngine.Debug.Log("[WitchCtl] listening on 127.0.0.1:" + port);
        }

        internal static (long served, long failed) Counters => (Interlocked.Read(ref _served), Interlocked.Read(ref _failed));

        private static void AcceptLoop()
        {
            while (true)
            {
                HttpListenerContext ctx;
                try { ctx = _listener.GetContext(); }
                catch (Exception e) { UnityEngine.Debug.Log("[WitchCtl] accept loop ended: " + e.Message); return; }
                ThreadPool.QueueUserWorkItem(_ => Handle(ctx));
            }
        }

        private static void Handle(HttpListenerContext ctx)
        {
            var sw = Stopwatch.StartNew();
            var timing = new Timing();
            var req = ctx.Request;
            string path = req.Url.AbsolutePath;
            int code = 200;
            string body;
            try
            {
                string input = "";
                if (req.HasEntityBody)
                    using (var r = new StreamReader(req.InputStream, Encoding.UTF8)) input = r.ReadToEnd();
                var request = new Req(input, req.QueryString);

                var route = Routes.Find(req.HttpMethod, path);
                if (route == null)
                {
                    code = 404;
                    body = Json.Error("not_found", req.HttpMethod + " " + path);
                }
                else if (route.OnMainThread)
                {
                    body = RunOnMain(() => route.Handler(request), timing);
                }
                else
                {
                    body = route.Handler(request).GetAwaiter().GetResult();
                }
            }
            catch (Exception e)
            {
                code = e is Newtonsoft.Json.JsonException ? 400 : 500;
                var inner = e is AggregateException ae && ae.InnerException != null ? ae.InnerException : e;
                body = Json.Error(inner.GetType().Name, inner.Message);
                UnityEngine.Debug.LogError("[WitchCtl] " + path + " failed: " + inner);
            }

            Interlocked.Increment(ref _served);
            if (code >= 400) Interlocked.Increment(ref _failed);

            var bytes = Encoding.UTF8.GetBytes(body);
            try
            {
                ctx.Response.StatusCode = code;
                ctx.Response.ContentType = "application/json; charset=utf-8";
                ctx.Response.ContentLength64 = bytes.Length;
                ctx.Response.AddHeader("X-Hop-Us", timing.HopUs.ToString());
                ctx.Response.AddHeader("X-Exec-Us", timing.ExecUs.ToString());
                ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
                ctx.Response.Close();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning("[WitchCtl] client went away on " + path + ": " + e.Message);
            }

            if (sw.ElapsedMilliseconds >= SlowRequestMs)
                UnityEngine.Debug.LogWarning("[WitchCtl] slow request " + path + " " + sw.ElapsedMilliseconds + "ms (hop " + timing.HopUs / 1000 + "ms, exec " + timing.ExecUs / 1000 + "ms)");
        }

        private sealed class Timing
        {
            public long HopUs;
            public long ExecUs;
        }

        /// <summary>Runs an async handler on the main thread and blocks the HTTP worker until it completes.</summary>
        private static string RunOnMain(Func<UniTask<string>> work, Timing timing)
        {
            var tcs = new TaskCompletionSource<string>();
            long queued = Stopwatch.GetTimestamp();
            Pump.Post(() => Execute(work, tcs, timing, queued).Forget());
            if (!tcs.Task.Wait(MainThreadTimeoutMs))
            {
                // The client is told "timeout" and may retry: work that never started must not run later, or it runs twice.
                tcs.TrySetCanceled();
                throw new TimeoutException("main thread did not finish the request within " + MainThreadTimeoutMs + "ms");
            }
            return tcs.Task.Result;
        }

        private static async UniTaskVoid Execute(Func<UniTask<string>> work, TaskCompletionSource<string> tcs, Timing timing, long queued)
        {
            if (tcs.Task.IsCanceled)
            {
                UnityEngine.Debug.LogWarning("[WitchCtl] dropped a request that timed out before the main thread reached it");
                return;
            }
            long started = Stopwatch.GetTimestamp();
            timing.HopUs = (started - queued) * 1000000L / Stopwatch.Frequency;
            try
            {
                string result = await work();
                timing.ExecUs = (Stopwatch.GetTimestamp() - started) * 1000000L / Stopwatch.Frequency;
                tcs.TrySetResult(result);
            }
            catch (Exception e)
            {
                tcs.TrySetException(e);
            }
        }
    }
}
