using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;

namespace WitchCtl
{
    /// <summary>Runs work queued by the HTTP threads once per frame, on the Unity main thread.</summary>
    internal sealed class Pump : MonoBehaviour
    {
        private static readonly ConcurrentQueue<Action> Queue = new ConcurrentQueue<Action>();
        internal static int MainThreadId;

        internal static void Post(Action work) => Queue.Enqueue(work);

        private void LateUpdate() => Audio.Enforce();

        private void Update()
        {
            MainThreadId = Thread.CurrentThread.ManagedThreadId;
            Audio.Enforce();
            while (Queue.TryDequeue(out var work))
            {
                try { work(); }
                catch (Exception e) { UnityEngine.Debug.LogError("[WitchCtl] work item failed: " + e); }
            }
        }
    }
}
