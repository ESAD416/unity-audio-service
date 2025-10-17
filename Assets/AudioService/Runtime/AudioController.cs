using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace Controller.Audio
{
    public class AudioController : MonoBehaviour
    {
        private static readonly bool VerboseLogging = true;
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

        private readonly Dictionary<AudioSource, Coroutine> _activeFades = new();
        private Coroutine _bgmTransitionCoroutine;

        private static IAudioClipProvider _globalClipProvider;
        private static bool _globalClipProviderIsUnityObject;
        private static UnityEngine.Object _globalClipProviderUnityRef;

        private IAudioClipProvider _clipProvider;
        private bool _clipProviderIsUnityObject;
        private UnityEngine.Object _clipProviderUnityRef;
        public IAudioClipProvider ClipProvider => TryGetClipProvider(out var provider) ? provider : null;

        public static void RegisterClipProvider(IAudioClipProvider provider)
        {
            CacheGlobalClipProvider(provider);
            if (Instance != null)
            {
                Instance.SetClipProvider(provider);
            }
        }

        public void SetClipProvider(IAudioClipProvider provider)
        {
            _clipProvider = provider;
            if (provider is UnityEngine.Object unityObject)
            {
                _clipProviderIsUnityObject = true;
                _clipProviderUnityRef = unityObject;
            }
            else
            {
                _clipProviderIsUnityObject = false;
                _clipProviderUnityRef = null;
            }
        }

        private bool TryGetClipProvider(out IAudioClipProvider provider)
        {
            if (!EnsureClipProviderAlive())
            {
                provider = null;
                return false;
            }

            provider = _clipProvider;
            return provider != null;
        }

        private bool EnsureClipProviderAlive()
        {
            if (_clipProvider == null)
            {
                return TryAdoptGlobalClipProvider();
            }

            if (_clipProviderIsUnityObject && _clipProviderUnityRef == null)
            {
                Debug.LogWarning("[AudioController] Clip provider has been destroyed; clearing reference.");
                ClearClipProvider();
                return TryAdoptGlobalClipProvider();
            }

            return true;
        }

        private void ClearClipProvider()
        {
            _clipProvider = null;
            _clipProviderIsUnityObject = false;
            _clipProviderUnityRef = null;
        }

        private bool TryAdoptGlobalClipProvider()
        {
            if (!IsGlobalClipProviderAlive())
            {
                return false;
            }

            _clipProvider = _globalClipProvider;
            _clipProviderIsUnityObject = _globalClipProviderIsUnityObject;
            _clipProviderUnityRef = _globalClipProviderUnityRef;
            return _clipProvider != null;
        }

        private static void CacheGlobalClipProvider(IAudioClipProvider provider)
        {
            _globalClipProvider = provider;
            if (provider is UnityEngine.Object unityObject)
            {
                _globalClipProviderIsUnityObject = true;
                _globalClipProviderUnityRef = unityObject;
            }
            else
            {
                _globalClipProviderIsUnityObject = false;
                _globalClipProviderUnityRef = null;
            }
        }

        private static bool IsGlobalClipProviderAlive()
        {
            if (_globalClipProvider == null)
            {
                return false;
            }

            if (_globalClipProviderIsUnityObject && _globalClipProviderUnityRef == null)
            {
                Debug.LogWarning("[AudioController] Global clip provider has been destroyed; clearing registration.");
                ClearGlobalClipProvider();
                return false;
            }

            return true;
        }

        private static void ClearGlobalClipProvider()
        {
            _globalClipProvider = null;
            _globalClipProviderIsUnityObject = false;
            _globalClipProviderUnityRef = null;
        }

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

            ResolveClipProvider();
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

        private void ResolveClipProvider()
        {
            if (TryGetClipProvider(out _)) return;

            if (VerboseLogging)
            {
                Debug.Log("[AudioController] No clip provider assigned; key-based clip loading unavailable");
            }
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

        #region Fade Helpers

        private void StopFade(AudioSource source)
        {
            if (source == null) return;
            if (_activeFades.TryGetValue(source, out var routine) && routine != null)
            {
                StopCoroutine(routine);
            }
            _activeFades.Remove(source);
        }

        private void FadeSource(AudioSource source, float targetVolume, float seconds, bool stopAfter = false, bool clearClip = false)
        {
            if (source == null) return;

            targetVolume = Mathf.Clamp01(targetVolume);
            if (seconds <= 0f)
            {
                StopFade(source);
                source.volume = targetVolume;
                if (stopAfter)
                {
                    source.Stop();
                    if (clearClip)
                    {
                        source.clip = null;
                    }
                }
                return;
            }

            StopFade(source);
            var routine = StartCoroutine(FadeAndFinalizeRoutine(source, targetVolume, seconds, stopAfter, clearClip));
            _activeFades[source] = routine;
        }

        private IEnumerator FadeAndFinalizeRoutine(AudioSource source, float targetVolume, float seconds, bool stopAfter, bool clearClip)
        {
            yield return FadeSourceRoutine(source, targetVolume, seconds);

            if (source != null && stopAfter)
            {
                source.Stop();
                if (clearClip)
                {
                    source.clip = null;
                }
            }

            _activeFades.Remove(source);
        }

        private IEnumerator FadeSourceRoutine(AudioSource source, float targetVolume, float seconds)
        {
            if (source == null) yield break;

            var start = source.volume;
            targetVolume = Mathf.Clamp01(targetVolume);
            seconds = Mathf.Max(0f, seconds);

            if (seconds <= 0f)
            {
                source.volume = targetVolume;
                yield break;
            }

            // Wait until the next frame so the fade duration is not impacted by inspector interactions.
            yield return null;
            if (source == null) yield break;

            var startTime = Time.realtimeSinceStartup;
            var endTime = startTime + seconds;

            while (source != null)
            {
                var now = Time.realtimeSinceStartup;
                var t = Mathf.InverseLerp(startTime, endTime, now);
                source.volume = Mathf.Lerp(start, targetVolume, t);

                if (t >= 1f)
                {
                    break;
                }

                yield return null;
            }

            if (source != null)
            {
                source.volume = targetVolume;
            }
        }

        public void FadeBgmTo(float targetVolume, float seconds)
        {
            FadeSource(_bgmSource, targetVolume, seconds);
        }

        public void FadeBgmOut(float seconds, bool stopAfter = true)
        {
            FadeSource(_bgmSource, 0f, seconds, stopAfter, clearClip: stopAfter);
        }

        public void FadeSfxLoopTo(float targetVolume, float seconds)
        {
            FadeSource(_sfxLoopSource, targetVolume, seconds);
        }

        public void FadeVoiceLoopTo(float targetVolume, float seconds)
        {
            FadeSource(_voiceLoopSource, targetVolume, seconds);
        }

        public void FadeChannel(AudioChannel channel, float targetVolume, float seconds, bool stopAfter = false, bool useLoopSource = false)
        {
            AudioSource source = null;
            switch (channel)
            {
                case AudioChannel.Master:
                case AudioChannel.Bgm:
                    source = _bgmSource;
                    break;
                case AudioChannel.Sfx:
                    source = useLoopSource ? _sfxLoopSource : _sfxSource;
                    break;
                case AudioChannel.Voice:
                    source = useLoopSource ? _voiceLoopSource : _voiceSource;
                    break;
            }

            var clearClip = stopAfter && (channel == AudioChannel.Bgm || useLoopSource);
            FadeSource(source, targetVolume, seconds, stopAfter, clearClip);
        }

        private void StopBgmTransition()
        {
            if (_bgmTransitionCoroutine != null)
            {
                StopCoroutine(_bgmTransitionCoroutine);
                _bgmTransitionCoroutine = null;
            }
        }

        private IEnumerator TransitionBgmClip(AudioClip nextClip, float fadeOutSeconds, float fadeInSeconds)
        {
            if (_bgmSource == null)
            {
                yield break;
            }

            StopFade(_bgmSource);

            if (fadeOutSeconds > 0f && _bgmSource.isPlaying && _bgmSource.clip != null)
            {
                yield return FadeSourceRoutine(_bgmSource, 0f, fadeOutSeconds);
                _bgmSource.Stop();
                _bgmSource.clip = null;
            }
            else
            {
                if (_bgmSource.isPlaying)
                {
                    _bgmSource.Stop();
                }
                _bgmSource.clip = null;
            }

            if (nextClip == null)
            {
                _bgmTransitionCoroutine = null;
                yield break;
            }

            _bgmSource.clip = nextClip;
            _bgmSource.loop = true;
            _bgmSource.volume = fadeInSeconds > 0f ? 0f : 1f;
            _bgmSource.Play();

            if (fadeInSeconds > 0f)
            {
                yield return FadeSourceRoutine(_bgmSource, 1f, fadeInSeconds);
            }

            _bgmTransitionCoroutine = null;
        }

        #endregion
        #endregion

        #region Play Audio

        public void PlayBgm(AudioClip clip, float fadeOutSeconds = 0f, float fadeInSeconds = 0f)
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

            StopBgmTransition();

            if (fadeOutSeconds > 0f && _bgmSource.isPlaying && _bgmSource.clip != null)
            {
                _bgmTransitionCoroutine = StartCoroutine(TransitionBgmClip(clip, fadeOutSeconds, fadeInSeconds));
            }
            else
            {
                StopFade(_bgmSource);
                if (_bgmSource.isPlaying)
                {
                    _bgmSource.Stop();
                }
                _bgmSource.clip = clip;
                _bgmSource.loop = true;
                _bgmSource.volume = fadeInSeconds > 0f ? 0f : 1f;
                _bgmSource.Play();

                if (fadeInSeconds > 0f)
                {
                    FadeSource(_bgmSource, 1f, fadeInSeconds);
                }
            }

            if (VerboseLogging) Debug.Log($"[AudioController] Playing BGM clip: {clip.name}");
        }

        public void PlaySfx(AudioClip clip, bool loop = false, float fadeInSeconds = 0f)
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

                _sfxLoopSource.Stop();
                _sfxLoopSource.clip = clip;
                _sfxLoopSource.loop = true;
                _sfxLoopSource.volume = fadeInSeconds > 0f ? 0f : 1f;
                _sfxLoopSource.Play();

                if (fadeInSeconds > 0f)
                {
                    FadeSource(_sfxLoopSource, 1f, fadeInSeconds);
                }

                if (VerboseLogging) Debug.Log($"[AudioController] Playing looping SFX clip: {clip.name}");
            }
            else
            {
                if (_sfxSource == null)
                {
                    Debug.LogError("[AudioController] SFX source is not available");
                    return;
                }

                StopFade(_sfxSource);
                _sfxSource.PlayOneShot(clip);

                if (VerboseLogging) Debug.Log($"[AudioController] Playing one-shot SFX clip: {clip.name}");
            }
        }

        public void PlayVoice(AudioClip clip, bool loop = false, float fadeInSeconds = 0f)
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

                _voiceLoopSource.Stop();
                _voiceLoopSource.clip = clip;
                _voiceLoopSource.loop = true;
                _voiceLoopSource.volume = fadeInSeconds > 0f ? 0f : 1f;
                _voiceLoopSource.Play();

                if (fadeInSeconds > 0f)
                {
                    FadeSource(_voiceLoopSource, 1f, fadeInSeconds);
                }

                if (VerboseLogging) Debug.Log($"[AudioController] Playing looping voice clip: {clip.name}");
            }
            else
            {
                if (_voiceSource == null)
                {
                    Debug.LogError("[AudioController] Voice source is not available");
                    return;
                }

                StopFade(_voiceSource);
                _voiceSource.loop = false;
                _voiceSource.clip = clip;
                _voiceSource.volume = fadeInSeconds > 0f ? 0f : 1f;
                _voiceSource.Play();

                if (fadeInSeconds > 0f)
                {
                    FadeSource(_voiceSource, 1f, fadeInSeconds);
                }

                if (VerboseLogging) Debug.Log($"[AudioController] Playing voice clip: {clip.name}");
            }
        }

        #endregion

        #region Play Audio By Key

        public void PlayBgm(string key, bool allowAsyncLoad = true, float fadeOutSeconds = 0f, float fadeInSeconds = 0f)
        {
            PlayByKey(AudioCategory.Bgm, key, clip => PlayBgm(clip, fadeOutSeconds, fadeInSeconds), allowAsyncLoad);
        }

        public void PlaySfx(string key, bool loop = false, bool allowAsyncLoad = true, float fadeInSeconds = 0f)
        {
            PlayByKey(AudioCategory.Sfx, key, clip => PlaySfx(clip, loop, fadeInSeconds), allowAsyncLoad);
        }

        public void PlayVoice(string key, bool loop = false, bool allowAsyncLoad = true, float fadeInSeconds = 0f)
        {
            PlayByKey(AudioCategory.Voice, key, clip => PlayVoice(clip, loop, fadeInSeconds), allowAsyncLoad);
        }

        private void PlayByKey(AudioCategory category, string key, Action<AudioClip> playAction, bool allowAsyncLoad)
        {
            if (string.IsNullOrEmpty(key))
            {
                Debug.LogWarning($"[AudioController] Play {category} called with empty key");
                return;
            }

            if (!TryGetClipProvider(out var provider))
            {
                Debug.LogWarning($"[AudioController] No clip provider available, cannot play {category} with key {key}");
                return;
            }

            if (provider.TryGetClip(category, key, out var clip) && clip != null)
            {
                playAction?.Invoke(clip);
                return;
            }

            if (!allowAsyncLoad)
            {
                Debug.LogWarning($"[AudioController] Clip for key {key} not available synchronously");
                return;
            }

            if (provider is IAsyncAudioClipProvider asyncProvider)
            {
                StartCoroutine(LoadAndPlayAsync(asyncProvider, category, key, playAction));
                return;
            }

            Debug.LogWarning($"[AudioController] Clip provider does not support async loading for key {key}");
        }

        private IEnumerator LoadAndPlayAsync(IAsyncAudioClipProvider asyncProvider, AudioCategory category, string key, Action<AudioClip> playAction)
        {
            yield return asyncProvider.LoadClipAsync(category, key);

            if (!TryGetClipProvider(out var provider))
            {
                yield break;
            }

            if (provider.TryGetClip(category, key, out var clip) && clip != null)
            {
                playAction?.Invoke(clip);
            }
            else
            {
                Debug.LogWarning($"[AudioController] Failed to load clip asynchronously for key {key}");
            }
        }

        #endregion

        #region Stop Audio

        public void StopBgm(float fadeOutSeconds = 0f)
        {
            if (_bgmSource == null) return;
            StopBgmTransition();
            if (fadeOutSeconds > 0f)
            {
                FadeSource(_bgmSource, 0f, fadeOutSeconds, true, clearClip: true);
            }
            else
            {
                StopFade(_bgmSource);
                _bgmSource.Stop();
                _bgmSource.clip = null;
            }
            if (VerboseLogging) Debug.Log("[AudioController] BGM stopped");
        }

        public void StopSfx(bool stopLoopOnly = false, float fadeOutSeconds = 0f)
        {
            if (_sfxSource != null && !stopLoopOnly)
            {
                if (fadeOutSeconds > 0f)
                {
                    FadeSource(_sfxSource, 0f, fadeOutSeconds, true, clearClip: true);
                }
                else
                {
                    StopFade(_sfxSource);
                    _sfxSource.Stop();
                    _sfxSource.clip = null;
                }
            }

            if (_sfxLoopSource != null)
            {
                if (fadeOutSeconds > 0f)
                {
                    FadeSource(_sfxLoopSource, 0f, fadeOutSeconds, true, clearClip: true);
                }
                else
                {
                    StopFade(_sfxLoopSource);
                    _sfxLoopSource.Stop();
                    _sfxLoopSource.clip = null;
                }
            }

            if (VerboseLogging) Debug.Log("[AudioController] SFX stopped");
        }

        public void StopVoice(bool stopLoopOnly = false, float fadeOutSeconds = 0f)
        {
            if (_voiceSource != null && !stopLoopOnly)
            {
                if (fadeOutSeconds > 0f)
                {
                    FadeSource(_voiceSource, 0f, fadeOutSeconds, true, clearClip: true);
                }
                else
                {
                    StopFade(_voiceSource);
                    _voiceSource.Stop();
                    _voiceSource.clip = null;
                }
            }

            if (_voiceLoopSource != null)
            {
                if (fadeOutSeconds > 0f)
                {
                    FadeSource(_voiceLoopSource, 0f, fadeOutSeconds, true, clearClip: true);
                }
                else
                {
                    StopFade(_voiceLoopSource);
                    _voiceLoopSource.Stop();
                    _voiceLoopSource.clip = null;
                }
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
