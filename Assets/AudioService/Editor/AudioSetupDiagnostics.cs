using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;

namespace Controller.Audio.Editor
{
    public readonly struct AudioSetupIssue
    {
        public readonly string Message;
        public readonly MessageType Severity;
        public readonly Object Context;
        public AudioSetupIssue(string message, MessageType severity, Object context = null)
        { Message = message; Severity = severity; Context = context; }
    }

    /// <summary>On-demand, read-only Editor checks. Does not load clips, repair settings, or test audible output.</summary>
    public static class AudioSetupDiagnostics
    {
        private static readonly string[] ChannelNames = { "Master", "BGM", "SFX", "Voice" };
        private static readonly string[] ChannelValues = { "Master", "Bgm", "Sfx", "Voice" };
        private static readonly string[] MixerParameters = { "masterVolume", "bgmVolume", "soundVolume", "voiceVolume" };
        private static readonly string[] MixerGroups = { "BGM", "Sound", "Voice" };

        public static IReadOnlyList<AudioSetupIssue> GetIssues(AudioController controller)
        {
            var issues = new List<AudioSetupIssue>();
            if (controller == null)
            { issues.Add(new AudioSetupIssue("Add and enable AudioCtrl.prefab in the entry scene.", MessageType.Error)); return issues; }

            using (var data = new SerializedObject(controller))
                CheckMixer(data.FindProperty("mixer").objectReferenceValue as AudioMixer, issues);

            // A prefab asset/stage is not the loaded game's listening environment.
            if (EditorUtility.IsPersistent(controller) || !controller.gameObject.scene.IsValid()
                || StageUtility.GetStageHandle(controller.gameObject) != StageUtility.GetMainStageHandle())
            {
                issues.Add(new AudioSetupIssue("Scene output checks are available on an AudioCtrl instance in an open scene. A prefab does not need its own AudioListener.", MessageType.Info));
                return issues;
            }
            if (!controller.isActiveAndEnabled)
                issues.Add(new AudioSetupIssue("AudioCtrl is disabled. Enable its GameObject and AudioController component before playback.", MessageType.Warning, controller));
            if (controller.transform.parent != null)
                issues.Add(new AudioSetupIssue("Move AudioCtrl to the scene root if it should persist across scenes.", MessageType.Warning, controller.transform));

            int listeners = 0; AudioListener extra = null;
            foreach (var listener in Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude))
                if (listener.isActiveAndEnabled && listener.gameObject.scene.isLoaded
                    && StageUtility.GetStageHandle(listener.gameObject) == StageUtility.GetMainStageHandle())
                { listeners++; extra = listener; }
            if (listeners == 0)
                issues.Add(new AudioSetupIssue("No active AudioListener in the loaded scenes. Enable one on the gameplay camera (or another listening object).", MessageType.Warning));
            else if (listeners > 1)
                issues.Add(new AudioSetupIssue($"{listeners} active AudioListeners in the loaded scenes. Keep one active listener, including when loading scenes additively.", MessageType.Warning, extra));

            if (!Application.isPlaying) return issues;
            if (!controller.Ready)
            { issues.Add(new AudioSetupIssue("Service is not ready. Enable AudioCtrl and call playback from Start or later.", MessageType.Warning, controller)); return issues; }
            if (AudioListener.volume <= 0)
                issues.Add(new AudioSetupIssue("AudioListener.volume is 0. Restore the listener volume in the code that changed it.", MessageType.Info));
            if (AudioListener.pause)
                issues.Add(new AudioSetupIssue("AudioListener.pause is on. Resume it in the code that paused it; IgnoreGamePause sounds can still play.", MessageType.Info));
            var state = controller.Diagnostics;
            if (state.GamePaused)
                issues.Add(new AudioSetupIssue("Game audio is paused. Resume with AudioService.SetGamePaused(false); IgnoreGamePause sounds can still play.", MessageType.Info));
            if (state.BackgroundPaused)
                issues.Add(new AudioSetupIssue("Background audio is paused. Return to the app, or review the owner of SetBackgroundPaused and Pause On Background.", MessageType.Info));
            for (int i = 0; i < ChannelNames.Length; i++)
            {
                var channel = controller.GetChannelDiagnostics((AudioChannel)i);
                string name = ChannelNames[i], argument = "AudioChannel." + ChannelValues[i];
                if (channel.Muted)
                    issues.Add(new AudioSetupIssue($"{name} is muted. To unmute, call AudioService.SetMuted({argument}, false).", MessageType.Info, controller));
                if (channel.Volume <= 0)
                    issues.Add(new AudioSetupIssue($"{name} player volume is 0. Raise it through your volume UI or AudioService.Set{ChannelValues[i]}Volume(value).", MessageType.Info, controller));
                if (channel.FadeGain <= 0)
                    issues.Add(new AudioSetupIssue($"{name} FadeBus gain is 0. To restore it, call AudioService.FadeBus({argument}, 1f, seconds). This does not change saved volume.", MessageType.Info, controller));
            }
            return issues;
        }

        private static void CheckMixer(AudioMixer mixer, List<AudioSetupIssue> issues)
        {
            if (mixer == null) return; // Mixer-free playback uses source gains.
            foreach (string parameter in MixerParameters)
                if (!mixer.GetFloat(parameter, out _))
                    issues.Add(new AudioSetupIssue($"Mixer is missing exposed parameter '{parameter}'. Expose that volume parameter, or use the supplied MasterMixer. The service uses source-volume fallback where needed.", MessageType.Warning, mixer));
            var groups = mixer.FindMatchingGroups(string.Empty);
            foreach (string name in MixerGroups)
            {
                bool found = false;
                foreach (var group in groups) if (group.name == name) { found = true; break; }
                if (!found)
                    issues.Add(new AudioSetupIssue($"Mixer is missing group '{name}'. Add that group, or use the supplied MasterMixer. That category uses source-volume fallback.", MessageType.Warning, mixer));
            }
        }
    }
}
