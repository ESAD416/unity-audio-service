using System;
using UnityEngine;

namespace Controller.Audio
{
    public interface IAudioSettingsMaintenance
    {
        void BroadcastStoredVolumes();
        void ResetSettings();
        void Flush();
    }
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
        [Tooltip("Automatically flush after the slider has been idle for saveDelaySeconds.")]
        [SerializeField] private bool saveImmediately;
        [SerializeField] private float saveDelaySeconds = .25f;
        private readonly float[] values = new float[4];
        private bool loaded, dirty;
        private float flushAt;
        public event Action<AudioChannel, float> VolumeChanged;
        public float MasterVolume { get { EnsureLoaded(); return values[0]; } }
        public float BgmVolume { get { EnsureLoaded(); return values[1]; } }
        public float SfxVolume { get { EnsureLoaded(); return values[2]; } }
        public float VoiceVolume { get { EnsureLoaded(); return values[3]; } }
        public int SaveCount { get; private set; }
        private string Key(int index) => index == 0 ? masterKey : index == 1 ? bgmKey : index == 2 ? sfxKey : voiceKey;
        private float Default(int index) => AudioValues.Unit(index == 0 ? defaultMaster : index == 1 ? defaultBgm : index == 2 ? defaultSfx : defaultVoice, 1);
        private void Awake() => EnsureLoaded();
        private void EnsureLoaded() { if (!loaded) Reload(); }
        public void Reload()
        {
            for (int i = 0; i < 4; i++) values[i] = AudioValues.Unit(PlayerPrefs.GetFloat(Key(i), Default(i)), Default(i));
            loaded = true;
        }
        public void UpdateVolume(AudioChannel channel, float normalizedVolume)
        {
            EnsureLoaded(); int index = (int)channel; if (index < 0 || index > 3) return;
            var value = AudioValues.Unit(normalizedVolume);
            if (values[index] == value) return;
            values[index] = value; PlayerPrefs.SetFloat(Key(index), value);
            MarkDirty(); VolumeChanged?.Invoke(channel, value);
        }
        private void MarkDirty() { dirty = true; flushAt = Time.realtimeSinceStartup + AudioValues.Seconds(saveDelaySeconds); }
        private void Update() { if (dirty && saveImmediately && Time.realtimeSinceStartup >= flushAt) Flush(); }
        public void Flush() { if (!dirty) return; PlayerPrefs.Save(); dirty = false; SaveCount++; }
        private void OnDisable() => Flush();
        private void OnApplicationPause(bool paused) { if (paused) Flush(); }
        public void ResetSettings()
        {
            for (int i = 0; i < 4; i++) PlayerPrefs.DeleteKey(Key(i));
            loaded = false; EnsureLoaded(); MarkDirty(); BroadcastStoredVolumes();
        }
        public void BroadcastStoredVolumes()
        {
            EnsureLoaded(); for (int i = 0; i < 4; i++) VolumeChanged?.Invoke((AudioChannel)i, values[i]);
        }
    }
}
