using System;
using UnityEngine;
using UnityEngine.Audio;

namespace Controller
{
    public class AudioController : MonoBehaviour
    {
        private static readonly bool VerboseLogging = false;
        public static AudioController Instance { get; private set; }

        [Header("AudioMixer")]
        [SerializeField] private AudioMixer mixer; // Assigned via Inspector when available
        private const string MIXER_DEFAULT_RESOURCE_PATH = "Audio/MasterMixer";
        private const string PARAM_MASTER = "masterVolume";
        private const string PARAM_BGM = "bgmVolume";
        private const string PARAM_SOUND = "soundVolume";
        private const string PARAM_VOICE = "voiceVolume";

        private AudioMixerGroup _bgmGroup;
        private AudioMixerGroup _soundGroup;
        private AudioMixerGroup _voiceGroup;

        // Backing audio sources
        private AudioSource _bgmSource;
        private AudioSource _sfxSource;
        private AudioSource _voiceSource;
        // Dedicated looping SFX source to avoid conflicts with one-shots
        private AudioSource _sfxLoopSource;
        // Dedicated looping Voice source (like Sound_Loop)
        private AudioSource _voiceLoopSource;

        public AudioSource BgmSource => _bgmSource;
        public AudioSource SfxSource => _sfxSource;
        public AudioSource SfxLoopSource => _sfxLoopSource;
        public AudioSource VoiceSource => _voiceSource;
        public AudioSource VoiceLoopSource => _voiceLoopSource;

        private void Awake()
        {
            if (VerboseLogging) Debug.Log("[AudioController] Awake invoked");
            if (Instance != null && Instance != this)
            {
                if (VerboseLogging) Debug.Log("[AudioController] Duplicate instance detected, destroying this object");
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            if (VerboseLogging) Debug.Log("[AudioController] Instance established");

            Initialize();
        }

        #region Initialize
        private void Initialize()
        {
            if (VerboseLogging) Debug.Log("[AudioController] Initialize started");

            LoadAudioMixer();
            InitializeMixer();
            EnsureAudioSources();

            if (VerboseLogging) Debug.Log("[AudioController] Initialize completed");
        }

        private void LoadAudioMixer()
        {
            if (mixer != null) return;

            mixer = Resources.Load<AudioMixer>(MIXER_DEFAULT_RESOURCE_PATH);
            if (mixer == null)
            {
                Debug.LogWarning("[AudioController] AudioMixer not found at Resources/Audio/MasterMixer");
                return;
            }

            if (VerboseLogging) Debug.Log("[AudioController] AudioMixer loaded from Resources");
        }

        private void InitializeMixer()
        {
            if (mixer == null)
            {
                Debug.LogWarning("[AudioController] AudioMixer missing, mixer groups will not be assigned");
                return;
            }

            var masterGroups = mixer.FindMatchingGroups("Master");
            var masterGroup = masterGroups.Length > 0 ? masterGroups[0] : null;
            if (VerboseLogging)
            {
                if (masterGroup != null)
                {
                    Debug.Log($"[AudioController] Master group resolved: {masterGroup.name}");
                }
                else
                {
                    Debug.Log("[AudioController] Master group not found on AudioMixer");
                }
            }

            var groups = mixer.FindMatchingGroups(string.Empty);
            foreach (var group in groups)
            {
                switch (group.name)
                {
                    case "BGM":
                        _bgmGroup = group;
                        break;
                    case "Sound":
                        _soundGroup = group;
                        break;
                    case "Voice":
                        _voiceGroup = group;
                        break;
                }
            }

            if (VerboseLogging)
            {
                Debug.Log($"[AudioController] Mixer groups resolved - BGM: {_bgmGroup != null}, Sound: {_soundGroup != null}, Voice: {_voiceGroup != null}");
            }
        }

        private void EnsureAudioSources()
        {
            _bgmGroup = ResolveMixerGroup(_bgmGroup, "BGM");
            _soundGroup = ResolveMixerGroup(_soundGroup, "Sound");
            _voiceGroup = ResolveMixerGroup(_voiceGroup, "Voice");

            _bgmSource = EnsureAudioSource("BGM", true, _bgmGroup);
            _sfxSource = EnsureAudioSource("Sound", false, _soundGroup);
            _sfxLoopSource = EnsureAudioSource("Sound_Loop", true, _soundGroup);
            _voiceSource = EnsureAudioSource("Voice", false, _voiceGroup);
            _voiceLoopSource = EnsureAudioSource("Voice_Loop", true, _voiceGroup);

            if (VerboseLogging) Debug.Log("[AudioController] AudioSources ready");
        }

        private AudioMixerGroup ResolveMixerGroup(AudioMixerGroup cachedGroup, string groupName)
        {
            if (cachedGroup != null) return cachedGroup;
            if (mixer == null) return null;

            var matches = mixer.FindMatchingGroups(groupName);
            return matches.Length > 0 ? matches[0] : null;
        }

        private AudioSource EnsureAudioSource(string childName, bool loop, AudioMixerGroup mixerGroup)
        {
            var childTransform = transform.Find(childName);
            var go = childTransform != null ? childTransform.gameObject : new GameObject(childName);

            if (go.transform.parent != transform)
            {
                go.transform.SetParent(transform);
            }

            var source = go.GetComponent<AudioSource>();
            if (source == null) source = go.AddComponent<AudioSource>();

            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 0f; // Force 2D audio
            source.volume = 1f;
            source.pitch = 1f;
            source.outputAudioMixerGroup = mixerGroup;

            return source;
        }
        #endregion

        #region Play Audio

        public void PlayBgm(AudioClip clip)
        {
            if (clip == null)
            {
                Debug.LogWarning("[AudioController] PlayBgm called with null clip");
                return;
            }

            if (_bgmSource == null)
            {
                EnsureAudioSources();
                if (_bgmSource == null)
                {
                    Debug.LogError("[AudioController] BGM source is not available");
                    return;
                }
            }

            _bgmSource.clip = clip;
            _bgmSource.loop = true;
            _bgmSource.volume = 1f;
            _bgmSource.Play();

            if (VerboseLogging) Debug.Log($"[AudioController] Playing BGM clip: {clip.name}");
        }

        public void PlaySfx(AudioClip clip, bool loop = false)
        {
            if (clip == null)
            {
                Debug.LogWarning("[AudioController] PlaySfx called with null clip");
                return;
            }

            if (_sfxSource == null || _sfxLoopSource == null)
            {
                EnsureAudioSources();
            }

            if (loop)
            {
                if (_sfxLoopSource == null)
                {
                    Debug.LogError("[AudioController] Looping SFX source is not available");
                    return;
                }

                _sfxLoopSource.clip = clip;
                _sfxLoopSource.loop = true;
                _sfxLoopSource.volume = 1f;
                _sfxLoopSource.Play();

                if (VerboseLogging) Debug.Log($"[AudioController] Playing looping SFX clip: {clip.name}");
            }
            else
            {
                if (_sfxSource == null)
                {
                    Debug.LogError("[AudioController] SFX source is not available");
                    return;
                }

                _sfxSource.PlayOneShot(clip);

                if (VerboseLogging) Debug.Log($"[AudioController] Playing one-shot SFX clip: {clip.name}");
            }
        }

        public void PlayVoice(AudioClip clip, bool loop = false)
        {
            if (clip == null)
            {
                Debug.LogWarning("[AudioController] PlayVoice called with null clip");
                return;
            }

            if (_voiceSource == null || _voiceLoopSource == null)
            {
                EnsureAudioSources();
            }

            if (loop)
            {
                if (_voiceLoopSource == null)
                {
                    Debug.LogError("[AudioController] Looping voice source is not available");
                    return;
                }

                _voiceLoopSource.clip = clip;
                _voiceLoopSource.loop = true;
                _voiceLoopSource.volume = 1f;
                _voiceLoopSource.Play();

                if (VerboseLogging) Debug.Log($"[AudioController] Playing looping voice clip: {clip.name}");
            }
            else
            {
                if (_voiceSource == null)
                {
                    Debug.LogError("[AudioController] Voice source is not available");
                    return;
                }

                _voiceSource.loop = false;
                _voiceSource.clip = clip;
                _voiceSource.volume = 1f;
                _voiceSource.Play();

                if (VerboseLogging) Debug.Log($"[AudioController] Playing voice clip: {clip.name}");
            }
        }

        #endregion

        #region Stop Audio

        public void StopBgm()
        {
            if (_bgmSource == null) return;
            _bgmSource.Stop();
            _bgmSource.clip = null;
            if (VerboseLogging) Debug.Log("[AudioController] BGM stopped");
        }

        public void StopSfx(bool stopLoopOnly = false)
        {
            if (_sfxSource != null && !stopLoopOnly)
            {
                _sfxSource.Stop();
            }

            if (_sfxLoopSource != null)
            {
                _sfxLoopSource.Stop();
                _sfxLoopSource.clip = null;
            }

            if (VerboseLogging) Debug.Log("[AudioController] SFX stopped");
        }

        public void StopVoice(bool stopLoopOnly = false)
        {
            if (_voiceSource != null && !stopLoopOnly)
            {
                _voiceSource.Stop();
                _voiceSource.clip = null;
            }

            if (_voiceLoopSource != null)
            {
                _voiceLoopSource.Stop();
                _voiceLoopSource.clip = null;
            }

            if (VerboseLogging) Debug.Log("[AudioController] Voice stopped");
        }

        #endregion

        #region Volume Control

        public void SetMasterVolume(float normalizedVolume)
        {
            SetMixerVolume(PARAM_MASTER, normalizedVolume);
        }

        public void SetBgmVolume(float normalizedVolume)
        {
            SetMixerVolume(PARAM_BGM, normalizedVolume);
        }

        public void SetSfxVolume(float normalizedVolume)
        {
            SetMixerVolume(PARAM_SOUND, normalizedVolume);
        }

        public void SetVoiceVolume(float normalizedVolume)
        {
            SetMixerVolume(PARAM_VOICE, normalizedVolume);
        }

        private void SetMixerVolume(string parameterName, float normalizedVolume)
        {
            if (mixer == null)
            {
                Debug.LogWarning($"[AudioController] Mixer is null, cannot set volume for {parameterName}");
                return;
            }

            var volume = Mathf.Clamp01(normalizedVolume);
            var db = LinearToDb(volume);
            mixer.SetFloat(parameterName, db);

            if (VerboseLogging) Debug.Log($"[AudioController] Set {parameterName} volume to {volume:F2} ({db:F2} dB)");
        }

        private float LinearToDb(float linearVolume)
        {
            const float minDb = -80f;
            return linearVolume > 0f ? Mathf.Log10(linearVolume) * 20f : minDb;
        }

        #endregion
    }
}
