using UnityEngine;
using Witch.Mod;

namespace WitchCtl
{
    public static class Entry
    {
        public const int DefaultPort = 47800;

        [ModInitialize]
        public static void Init(ModConfig modConfig)
        {
            // The CLI lives in another window: without this Unity pauses the player loop on focus loss.
            Application.runInBackground = true;
            // Silent only when asked (scripts/lab.sh start does): this DLL is also fine to leave in a game you actually play.
            Audio.Muted = System.Environment.GetEnvironmentVariable("WITCHCTL_MUTE") == "1";
            Audio.Apply();   // silent from the first frame, not from the first Update

            int port = DefaultPort;
            var env = System.Environment.GetEnvironmentVariable("WITCHCTL_PORT");
            if (!string.IsNullOrEmpty(env) && int.TryParse(env, out var parsed)) port = parsed;

            Bridge.Start(port);
        }
    }

    /// <summary>
    /// Optional global mute, for unattended background runs. Done with the listener volume on purpose: AudioListener.pause
    /// would also silence the game, but a paused clip still reports isPlaying, and dialogue waits for voice clips to end.
    /// The game's settings code writes the volume too, so it is re-asserted every frame (Update and LateUpdate).
    /// </summary>
    internal static class Audio
    {
        internal static volatile bool Muted;

        internal static void Apply() => AudioListener.volume = Muted ? 0f : 1f;

        internal static void Enforce()
        {
            if (Muted && AudioListener.volume != 0f) AudioListener.volume = 0f;
        }
    }
}
