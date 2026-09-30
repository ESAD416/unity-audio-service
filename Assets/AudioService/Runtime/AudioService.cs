using UnityEngine;

namespace Controller.Audio
{
    /// <summary>Replace the dialogue slot, or play a voice independently of that slot.</summary>
    public enum VoicePlaybackMode { Replace, Overlap }

    /// <summary>
    /// Recommended main-thread API. Add AudioCtrl.prefab to the entry scene first.
    /// Every Play method returns a handle; ignoring it never changes playback behavior.
    /// AudioController retains the original APIs and advanced configuration.
    /// </summary>
    public static class AudioService
    {
        private static AudioFailureLog missingControllerLog;
        public static bool Ready => AudioController.Instance != null && AudioController.Instance.Ready;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => missingControllerLog = null;

        /// <summary>Loop music in the shared BGM slot. A successful replacement stops the previous music.</summary>
        public static AudioHandle PlayBgm(AudioId id, PlayOptions options = null)
            => Request(AudioCategory.Bgm, id, null, options, 0);
        /// <summary>Loop an externally owned clip in the shared BGM slot. The service never unloads this clip.</summary>
        public static AudioHandle PlayBgm(AudioClip clip, PlayOptions options = null)
            => Request(AudioCategory.Bgm, default, clip, options, 0);
        /// <summary>Play an independent sound, including when Loop is enabled. Keep the handle to stop only this sound.</summary>
        public static AudioHandle PlaySfx(AudioId id, PlayOptions options = null)
            => Request(AudioCategory.Sfx, id, null, options, -1);
        /// <summary>Play an independent, externally owned sound. The service never unloads this clip.</summary>
        public static AudioHandle PlaySfx(AudioClip clip, PlayOptions options = null)
            => Request(AudioCategory.Sfx, default, clip, options, -1);
        /// <summary>
        /// Replace the dialogue slot by default, regardless of Loop or whether the caller keeps the handle.
        /// Overlap plays independently; replacing the dialogue slot does not stop overlapping voices.
        /// </summary>
        public static AudioHandle PlayVoice(AudioId id, PlayOptions options = null, VoicePlaybackMode mode = VoicePlaybackMode.Replace)
            => Voice(id, null, options, mode);
        /// <summary>Play an externally owned voice clip, using the same replacement policy as the ID overload.</summary>
        public static AudioHandle PlayVoice(AudioClip clip, PlayOptions options = null, VoicePlaybackMode mode = VoicePlaybackMode.Replace)
            => Voice(default, clip, options, mode);

        private static AudioHandle Voice(AudioId id, AudioClip clip, PlayOptions options, VoicePlaybackMode mode)
        {
            if (mode != VoicePlaybackMode.Replace && mode != VoicePlaybackMode.Overlap)
                return Failed(AudioCategory.Voice, id, "Invalid VoicePlaybackMode. Use Replace or Overlap.");
            // Share the existing non-loop dialogue slot. Loop remains a playback
            // option, so changing it does not select a different replacement slot.
            return Request(AudioCategory.Voice, id, clip, options, mode == VoicePlaybackMode.Replace ? 3 : -1);
        }
        private static AudioHandle Request(AudioCategory category, AudioId id, AudioClip clip, PlayOptions options, int bank)
        {
            var controller = AudioController.Instance;
            return controller != null ? controller.Request(category, id, clip, options, bank)
                : Failed(category, id, "AudioController is missing. Add AudioCtrl.prefab to the entry scene and call playback from Start or later.");
        }
        private static AudioHandle Failed(AudioCategory category, AudioId id, string reason)
        {
            var handle = new AudioHandle { Category = category, AudioId = id };
            handle.Finish(AudioCompletion.Failed, reason);
            var controller = AudioController.Instance;
            if (controller != null) controller.ReportPlaybackFailure(handle, reason);
            else if (Application.isEditor || Debug.isDebugBuild)
                (missingControllerLog ??= new AudioFailureLog()).Report(null, handle, reason);
            return handle;
        }
        private static AudioController Controller
        {
            get
            {
                var controller = AudioController.Instance;
                if (controller != null && controller.Ready) return controller;
                Failed(AudioCategory.Sfx, default, "Audio service is not ready. Enable AudioCtrl.prefab and call controls from Start or later.");
                return null;
            }
        }

        /// <summary>Stop all music. New playback after this call is not stopped by the old fade.</summary>
        public static void StopBgm(float fadeOutSeconds = 0f) => Controller?.StopBgm(fadeOutSeconds);
        /// <summary>Stop ALL SFX, including loops and sounds started through legacy APIs. For one sound use its handle.</summary>
        public static void StopSfx(float fadeOutSeconds = 0f) => Controller?.StopSfx(false, fadeOutSeconds);
        /// <summary>Stop ALL voices, including dialogue, overlapping voices and legacy loops.</summary>
        public static void StopVoice(float fadeOutSeconds = 0f) => Controller?.StopVoice(false, fadeOutSeconds);
        /// <summary>
        /// Fade a whole category (or Master), including old and new APIs. Does not change saved player volume.
        /// The gain persists for future playback; fade back to 1 to restore it. stopAfter stops only existing playback.
        /// </summary>
        public static void FadeBus(AudioChannel channel, float targetVolume, float seconds, bool stopAfter = false)
            => Controller?.FadeBus(channel, targetVolume, seconds, stopAfter);
        /// <summary>Set saved player volume in 0..1 when the prefab's settings handler is connected.</summary>
        public static void SetMasterVolume(float volume) => Controller?.SetMasterVolume(volume);
        public static void SetBgmVolume(float volume) => Controller?.SetBgmVolume(volume);
        public static void SetSfxVolume(float volume) => Controller?.SetSfxVolume(volume);
        public static void SetVoiceVolume(float volume) => Controller?.SetVoiceVolume(volume);
        public static float GetVolume(AudioChannel channel) => Controller?.GetVolume(channel) ?? 0f;
        /// <summary>Temporarily mute a channel without stopping playback or changing saved volume.</summary>
        public static void SetMuted(AudioChannel channel, bool muted) => Controller?.SetMuted(channel, muted);
        /// <summary>Pause ordinary playback; sounds with IgnoreGamePause remain active.</summary>
        public static void SetGamePaused(bool paused) => Controller?.SetGamePaused(paused);
    }
}
