using UnityEngine;

namespace Controller.Audio.Samples
{
    /// <summary>Playable sample and optional UnityEvent adapter. Assign clips in the Inspector.</summary>
    public sealed class AudioQuickStart : MonoBehaviour
    {
        [SerializeField] private AudioClip music;
        [SerializeField] private AudioClip sound;
        [SerializeField] private AudioClip voice;
        [Tooltip("Show the demonstration controls during Play Mode. Disable when using these methods from UI Buttons.")]
        [SerializeField] private bool showControls = true;
        private AudioHandle musicHandle, voiceHandle;
        private int musicRequest, voiceRequest;
        private string playbackMessage;
        private GUIStyle messageStyle;

        // Void adapters remain bindable to Unity UI Button events.
        public void PlayMusic() => PlayOwned(true);
        public void PlaySound() { if (isActiveAndEnabled) AudioService.PlaySfx(sound); }
        public void PlayVoice() => PlayOwned(false);
        private void PlayOwned(bool isMusic)
        {
            if (this == null || !isActiveAndEnabled) return;
            int request = isMusic ? ++musicRequest : ++voiceRequest;
            // These direct-clip requests replace immediately. Failed/rejected
            // requests leave the old sound playing, so keep its control handle.
            var next = isMusic ? AudioService.PlayBgm(music) : AudioService.PlayVoice(voice);
            // Completion callbacks can stop, disable, or start this sample again
            // before Play returns. The most recent command owns the result.
            if (this == null || !isActiveAndEnabled || request != (isMusic ? musicRequest : voiceRequest))
            { next.Stop(); return; }
            if (next.IsFinished)
            {
                playbackMessage = $"{(isMusic ? "Music" : "Voice")} did not start: {next.FailureReason ?? next.Result.ToString()}";
                return;
            }
            if (isMusic) musicHandle = next; else voiceHandle = next;
            playbackMessage = null;
        }
        public void StopMusic() { musicRequest++; musicHandle?.Stop(.5f); }
        public void StopVoice() { voiceRequest++; voiceHandle?.Stop(.1f); }
        public void SetMasterVolume(float volume) => AudioService.SetMasterVolume(volume);
        private void OnDisable()
        {
            musicRequest++; voiceRequest++;
            var oldMusic = musicHandle; var oldVoice = voiceHandle;
            musicHandle = voiceHandle = null;
            oldMusic?.Stop(); oldVoice?.Stop();
        }
        private void OnGUI()
        {
            if (!showControls) return;
            messageStyle ??= new GUIStyle(GUI.skin.label) { wordWrap = true };
            GUILayout.BeginArea(new Rect(24, 24, Mathf.Min(440, Screen.width - 48), Screen.height - 48), GUI.skin.box);
            GUILayout.Label("Audio Service - Quick Start");
            GUILayout.Label(AudioService.Ready ? "Ready. Assign your own clips in the Inspector." : "Add and enable AudioCtrl.prefab in this scene.");
            bool previous = GUI.enabled;
            GUI.enabled = previous && AudioService.Ready;
            if (GUILayout.Button("Play sound (each click is independent)")) PlaySound();
            if (GUILayout.Button("Play music (loop / replace previous)")) PlayMusic();
            if (GUILayout.Button("Stop this music with a fade")) StopMusic();
            if (GUILayout.Button("Play voice (replace previous dialogue)")) PlayVoice();
            if (GUILayout.Button("Stop this voice")) StopVoice();
            if (AudioService.Ready)
            {
                float volume = AudioService.GetVolume(AudioChannel.Master);
                GUILayout.Label($"Master volume: {volume:P0} (saved)");
                float next = GUILayout.HorizontalSlider(volume, 0, 1);
                if (next != volume) SetMasterVolume(next);
            }
            GUI.enabled = previous;
            GUILayout.Label("Keep the returned handle to pause or stop one sound.");
            GUILayout.Label("For ID playback and scene cleanup, see README.");
            if (!string.IsNullOrEmpty(playbackMessage)) GUILayout.Label(playbackMessage, messageStyle);
            GUILayout.EndArea();
        }
    }
}
