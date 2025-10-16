using System;
using UnityEngine;

namespace Controller.Audio
{
    /// <summary>
    /// Simple PlayerPrefs-backed implementation of <see cref="IAudioSettingsHandler"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public class AudioSettingsPlayerPrefs : MonoBehaviour, IAudioSettingsHandler
    {
        private const float Epsilon = 0.0001f;

        [Header("PlayerPrefs Keys")]
        [SerializeField] private string masterKey = "Audio.Master";
        [SerializeField] private string bgmKey = "Audio.Bgm";
        [SerializeField] private string sfxKey = "Audio.Sfx";
        [SerializeField] private string voiceKey = "Audio.Voice";

        [Header("Default Volumes")]
        [Range(0f, 1f)] [SerializeField] private float defaultMaster = 1f;
        [Range(0f, 1f)] [SerializeField] private float defaultBgm = 1f;
        [Range(0f, 1f)] [SerializeField] private float defaultSfx = 1f;
        [Range(0f, 1f)] [SerializeField] private float defaultVoice = 1f;

        [Header("Persistence")]
        [SerializeField] private bool saveImmediately = true;

        private float _master;
        private float _bgm;
        private float _sfx;
        private float _voice;

        public event Action<AudioChannel, float> VolumeChanged;

        public float MasterVolume => _master;
        public float BgmVolume => _bgm;
        public float SfxVolume => _sfx;
        public float VoiceVolume => _voice;

        private void Awake()
        {
            LoadFromPrefs();
        }

        private void LoadFromPrefs()
        {
            _master = PlayerPrefs.GetFloat(masterKey, Mathf.Clamp01(defaultMaster));
            _bgm = PlayerPrefs.GetFloat(bgmKey, Mathf.Clamp01(defaultBgm));
            _sfx = PlayerPrefs.GetFloat(sfxKey, Mathf.Clamp01(defaultSfx));
            _voice = PlayerPrefs.GetFloat(voiceKey, Mathf.Clamp01(defaultVoice));
        }

        public void UpdateVolume(AudioChannel channel, float normalizedVolume)
        {
            var clamped = Mathf.Clamp01(normalizedVolume);

            switch (channel)
            {
                case AudioChannel.Master:
                    if (!ShouldChange(_master, clamped)) return;
                    _master = clamped;
                    PlayerPrefs.SetFloat(masterKey, _master);
                    break;
                case AudioChannel.Bgm:
                    if (!ShouldChange(_bgm, clamped)) return;
                    _bgm = clamped;
                    PlayerPrefs.SetFloat(bgmKey, _bgm);
                    break;
                case AudioChannel.Sfx:
                    if (!ShouldChange(_sfx, clamped)) return;
                    _sfx = clamped;
                    PlayerPrefs.SetFloat(sfxKey, _sfx);
                    break;
                case AudioChannel.Voice:
                    if (!ShouldChange(_voice, clamped)) return;
                    _voice = clamped;
                    PlayerPrefs.SetFloat(voiceKey, _voice);
                    break;
                default:
                    Debug.LogWarning($"[AudioSettingsPlayerPrefs] Unknown channel {channel}");
                    return;
            }

            if (saveImmediately)
            {
                PlayerPrefs.Save();
            }

            VolumeChanged?.Invoke(channel, clamped);
        }

        private static bool ShouldChange(float current, float incoming)
        {
            return Mathf.Abs(current - incoming) > Epsilon;
        }

        /// <summary>
        /// Emits current values through <see cref="VolumeChanged"/>. Useful when wiring listeners after Awake.
        /// </summary>
        public void BroadcastStoredVolumes()
        {
            VolumeChanged?.Invoke(AudioChannel.Master, _master);
            VolumeChanged?.Invoke(AudioChannel.Bgm, _bgm);
            VolumeChanged?.Invoke(AudioChannel.Sfx, _sfx);
            VolumeChanged?.Invoke(AudioChannel.Voice, _voice);
        }
    }
}
