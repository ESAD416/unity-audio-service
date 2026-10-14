using System;
using UnityEngine;

namespace Controller.Audio.Samples
{
    /// <summary>Playable sample and optional UnityEvent adapter. Assign clips in the Inspector.</summary>
    public sealed class AudioQuickStart : MonoBehaviour
    {
        // A replaceable sound this sample started: its handle, and a request number
        // that tells an outdated Play call apart from the latest command.
        private sealed class OwnedSound
        {
            public readonly string Name;
            public AudioHandle Handle;
            public int Request;
            public OwnedSound(string name) => Name = name;

            // Invalidate any Play call still in progress, then stop the owned sound.
            public void Stop(float fadeOutSeconds)
            {
                Request++;
                Handle?.Stop(fadeOutSeconds);
            }

            // On disable: stop at once, even mid-fade, and let go of the handle.
            public void Release()
            {
                Request++;
                var current = Handle;
                Handle = null;
                current?.Stop();
            }
        }

        [SerializeField] private AudioClip music;
        [SerializeField] private AudioClip sound;
        [SerializeField] private AudioClip voice;
        [Tooltip("Show the demonstration controls during Play Mode. Disable when using these methods from UI Buttons.")]
        [SerializeField] private bool showControls = true;
        private readonly OwnedSound ownedMusic = new("Music"), ownedVoice = new("Voice");
        private string playbackMessage;
        private GUIStyle messageStyle;

        // Void adapters remain bindable to Unity UI Button events.
        public void PlayMusic() => PlayOwned(ownedMusic, () => AudioService.PlayBgm(music));
        public void PlaySound() { if (isActiveAndEnabled) AudioService.PlaySfx(sound); }
        public void PlayVoice() => PlayOwned(ownedVoice, () => AudioService.PlayVoice(voice));
        private void PlayOwned(OwnedSound owned, Func<AudioHandle> play)
        {
            if (this == null || !isActiveAndEnabled) return;
            int request = ++owned.Request;
            // These direct-clip requests replace immediately. Failed/rejected
            // requests leave the old sound playing, so keep its control handle.
            var next = play();
            // Completion callbacks can stop, disable, or start this sample again
            // before Play returns. The most recent command owns the result.
            if (this == null || !isActiveAndEnabled || request != owned.Request)
            {
                next.Stop();
                return;
            }
            if (next.IsFinished)
            {
                playbackMessage = $"{owned.Name} did not start: {next.FailureReason ?? next.Result.ToString()}";
                return;
            }
            owned.Handle = next;
            playbackMessage = null;
        }
        public void StopMusic() => ownedMusic.Stop(.5f);
        public void StopVoice() => ownedVoice.Stop(.1f);
        public void SetMasterVolume(float volume) => AudioService.SetMasterVolume(volume);
        private void OnDisable()
        {
            ownedMusic.Release();
            ownedVoice.Release();
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
