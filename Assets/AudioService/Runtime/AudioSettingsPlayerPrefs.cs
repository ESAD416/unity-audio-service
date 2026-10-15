using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace Controller.Audio
{
    [DisallowMultipleComponent]
    public class AudioSettingsPlayerPrefs : MonoBehaviour, IAudioSettingsHandler, IAudioSettingsMaintenance
    {
        [SerializeField] private string masterKey = "Audio.Master";
        [SerializeField] private string bgmKey = "Audio.Bgm";
        [SerializeField] private string sfxKey = "Audio.Sfx";
        [SerializeField] private string voiceKey = "Audio.Voice";
        [Range(0, 1)] [SerializeField] private float defaultMaster = 1;
        [Range(0, 1)] [SerializeField] private float defaultBgm = 1;
        [Range(0, 1)] [SerializeField] private float defaultSfx = 1;
        [Range(0, 1)] [SerializeField] private float defaultVoice = 1;
        [Tooltip("Save after volume changes have been idle for the save delay. Volume changes take effect immediately.")]
        [SerializeField, FormerlySerializedAs("saveImmediately")] private bool autoSave;
        [SerializeField] private float saveDelaySeconds = .25f;
        private readonly float[] values = new float[AudioMixerLayout.ChannelCount];
        private bool loaded, dirty;
        private float flushAt;
        public event Action<AudioChannel, float> VolumeChanged;
        public float MasterVolume => Stored(AudioChannel.Master);
        public float BgmVolume => Stored(AudioChannel.Bgm);
        public float SfxVolume => Stored(AudioChannel.Sfx);
        public float VoiceVolume => Stored(AudioChannel.Voice);
        // Number of PlayerPrefs.Save calls, for verifying that writes are batched.
        internal int SaveCount { get; private set; }
        private string Key(AudioChannel channel) => channel switch
        {
            AudioChannel.Master => masterKey,
            AudioChannel.Bgm => bgmKey,
            AudioChannel.Sfx => sfxKey,
            _ => voiceKey
        };
        private float Default(AudioChannel channel) => AudioValues.Unit(channel switch
        {
            AudioChannel.Master => defaultMaster,
            AudioChannel.Bgm => defaultBgm,
            AudioChannel.Sfx => defaultSfx,
            _ => defaultVoice
        }, 1);

        private float Stored(AudioChannel channel)
        {
            EnsureLoaded();
            return values[(int)channel];
        }

        private void Awake() => EnsureLoaded();
        private void EnsureLoaded()
        {
            if (!loaded)
                Reload();
        }

        public void Reload()
        {
            foreach (var channel in AudioMixerLayout.Channels)
                values[(int)channel] = AudioValues.Unit(PlayerPrefs.GetFloat(Key(channel), Default(channel)), Default(channel));
            loaded = true;
        }

        public void UpdateVolume(AudioChannel channel, float normalizedVolume)
        {
            EnsureLoaded();
            if (!AudioValues.IsValidChannel(channel))
                return;
            int index = (int)channel;
            var value = AudioValues.Unit(normalizedVolume);
            if (values[index] == value)
                return;
            values[index] = value;
            PlayerPrefs.SetFloat(Key(channel), value);
            MarkDirty();
            VolumeChanged?.Invoke(channel, value);
        }

        private void MarkDirty()
        {
            dirty = true;
            flushAt = Time.realtimeSinceStartup + AudioValues.Seconds(saveDelaySeconds);
        }

        private void Update()
        {
            if (dirty && autoSave && Time.realtimeSinceStartup >= flushAt)
                Flush();
        }

        public void Flush()
        {
            if (!dirty)
                return;
            PlayerPrefs.Save();
            dirty = false;
            SaveCount++;
        }

        private void OnDisable() => Flush();
        private void OnApplicationPause(bool paused)
        {
            if (paused)
                Flush();
        }

        public void ResetSettings()
        {
            foreach (var channel in AudioMixerLayout.Channels)
                PlayerPrefs.DeleteKey(Key(channel));
            loaded = false;
            EnsureLoaded();
            MarkDirty();
            BroadcastStoredVolumes();
        }

        public void BroadcastStoredVolumes()
        {
            EnsureLoaded();
            foreach (var channel in AudioMixerLayout.Channels)
                VolumeChanged?.Invoke(channel, values[(int)channel]);
        }
    }
}
