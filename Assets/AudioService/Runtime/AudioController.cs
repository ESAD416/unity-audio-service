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

        // Request epochs are separate from transition versions: concurrent one-shots
        // share a cancellation epoch without invalidating each other's completion.
        private sealed class SourceState
        {
            public long RequestVersion;
            public long TransitionVersion;
            public Coroutine Transition;
            public float Gain = 1f;
            public float Envelope = 1f;
        }

        private readonly Dictionary<AudioSource, SourceState> _states = new();
        private long _providerVersion;
        private long _serviceVersion;
        private float _masterGain = 1f;
        private long _masterVersion;
        private Coroutine _masterFade;

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
            if (!ReferenceEquals(_clipProvider, provider)) _providerVersion++;
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
            _providerVersion++;
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

            _providerVersion++;
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

            foreach (var source in AllSources()) { State(source); ApplyGain(source); }
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

        #region Playback and transitions

        private SourceState State(AudioSource source)
        {
            if (!_states.TryGetValue(source, out var state))
            {
                state = new SourceState();
                _states.Add(source, state);
            }
            return state;
        }

        private void ApplyGain(AudioSource source)
        {
            if (source == null) return;
            var state = State(source);
            source.volume = Mathf.Clamp01(_masterGain * state.Gain * state.Envelope);
        }

        private void ApplyAllGains()
        {
            foreach (var pair in _states) ApplyGain(pair.Key);
        }

        private void CancelTransition(AudioSource source)
        {
            if (source == null) return;
            var state = State(source);
            state.TransitionVersion++;
            if (state.Transition != null) StopCoroutine(state.Transition);
            state.Transition = null;
        }

        private bool TransitionIsCurrent(AudioSource source, long version) =>
            this != null && isActiveAndEnabled && source != null && State(source).TransitionVersion == version;

        private long BeginRequest(AudioSource source, bool replace)
        {
            var state = State(source);
            if (replace)
            {
                state.RequestVersion++;
                CancelTransition(source);
                state.Envelope = 1f;
                ApplyGain(source);
            }
            return state.RequestVersion;
        }

        private void PreparePlayback(AudioSource source)
        {
            CancelTransition(source);
            State(source).Envelope = 1f;
            ApplyGain(source);
        }

        private IEnumerator Tween(AudioSource source, long version, float target, float seconds, bool envelope)
        {
            var state = State(source);
            var start = envelope ? state.Envelope : state.Gain;
            var startTime = Time.realtimeSinceStartup;
            while (TransitionIsCurrent(source, version))
            {
                var t = seconds <= 0f ? 1f : Mathf.Clamp01((Time.realtimeSinceStartup - startTime) / seconds);
                var value = Mathf.Lerp(start, target, t);
                if (envelope) state.Envelope = value;
                else state.Gain = value;
                ApplyGain(source);
                if (t >= 1f) yield break;
                yield return null;
            }
        }

        private void FadeSource(AudioSource source, float target, float seconds, bool stopAfter = false)
        {
            if (!isActiveAndEnabled || source == null) return;
            var state = State(source);
            CancelTransition(source);
            // A source fade supersedes a pending replacement as well as a BGM transition.
            state.RequestVersion++;
            target = Mathf.Clamp01(target);
            if (!stopAfter)
            {
                state.Gain *= state.Envelope;
                state.Envelope = 1f;
            }
            if (seconds <= 0f)
            {
                if (stopAfter) StopSourceNow(source);
                else { state.Gain = target; state.Envelope = 1f; ApplyGain(source); }
                return;
            }
            var version = state.TransitionVersion;
            state.Transition = StartCoroutine(FadeAndFinalize(source, version, target, seconds, stopAfter));
        }

        private IEnumerator FadeAndFinalize(AudioSource source, long version, float target, float seconds, bool stopAfter)
        {
            // Always suspend before completing so the coroutine slot cannot retain a completed routine.
            yield return null;
            yield return Tween(source, version, target, seconds, stopAfter);
            if (!TransitionIsCurrent(source, version)) yield break;
            if (stopAfter) StopSourceNow(source);
            State(source).Transition = null;
        }

        private void StopSourceNow(AudioSource source)
        {
            source.Stop();
            source.clip = null;
            State(source).Envelope = 1f;
            ApplyGain(source);
        }

        private void StartClip(AudioSource source, AudioClip clip, bool loop, float fadeInSeconds)
        {
            PreparePlayback(source);
            source.Stop();
            source.clip = clip;
            source.loop = loop;
            var state = State(source);
            state.Envelope = fadeInSeconds > 0f ? 0f : 1f;
            ApplyGain(source);
            source.Play();
            if (fadeInSeconds > 0f)
                state.Transition = StartCoroutine(FadeIn(source, state.TransitionVersion, fadeInSeconds));
        }

        private IEnumerator FadeIn(AudioSource source, long version, float seconds)
        {
            yield return null;
            yield return Tween(source, version, 1f, seconds, true);
            if (TransitionIsCurrent(source, version)) State(source).Transition = null;
        }

        private void PlayBgmCore(AudioClip clip, float fadeOutSeconds, float fadeInSeconds)
        {
            PreparePlayback(_bgmSource);
            if (fadeOutSeconds > 0f && _bgmSource.isPlaying && _bgmSource.clip != null)
            {
                var state = State(_bgmSource);
                state.Transition = StartCoroutine(TransitionBgmClip(clip, fadeOutSeconds, fadeInSeconds, state.TransitionVersion));
            }
            else StartClip(_bgmSource, clip, true, fadeInSeconds);
        }

        private IEnumerator TransitionBgmClip(AudioClip clip, float fadeOutSeconds, float fadeInSeconds, long version)
        {
            yield return null;
            yield return Tween(_bgmSource, version, 0f, fadeOutSeconds, true);
            if (!TransitionIsCurrent(_bgmSource, version)) yield break;
            _bgmSource.Stop();
            _bgmSource.clip = clip;
            _bgmSource.loop = true;
            State(_bgmSource).Envelope = fadeInSeconds > 0f ? 0f : 1f;
            ApplyGain(_bgmSource);
            _bgmSource.Play();
            if (fadeInSeconds > 0f) yield return Tween(_bgmSource, version, 1f, fadeInSeconds, true);
            if (TransitionIsCurrent(_bgmSource, version)) State(_bgmSource).Transition = null;
        }

        private void PlaySfxCore(AudioClip clip, bool loop, float fadeInSeconds)
        {
            if (loop) StartClip(_sfxLoopSource, clip, true, fadeInSeconds);
            else
            {
                PreparePlayback(_sfxSource);
                // One-shots share this source. Per-shot fade-in requires independent
                // emitters, which is deliberately reserved for stage two.
                _sfxSource.PlayOneShot(clip);
            }
        }

        public void PlayBgm(AudioClip clip, float fadeOutSeconds = 0f, float fadeInSeconds = 0f)
        {
            if (!CanPlay(clip)) return;
            BeginRequest(_bgmSource, true);
            PlayBgmCore(clip, fadeOutSeconds, fadeInSeconds);
        }

        public void PlaySfx(AudioClip clip, bool loop = false, float fadeInSeconds = 0f)
        {
            if (!CanPlay(clip)) return;
            BeginRequest(loop ? _sfxLoopSource : _sfxSource, loop);
            PlaySfxCore(clip, loop, fadeInSeconds);
        }

        public void PlayVoice(AudioClip clip, bool loop = false, float fadeInSeconds = 0f)
        {
            if (!CanPlay(clip)) return;
            var source = loop ? _voiceLoopSource : _voiceSource;
            BeginRequest(source, true);
            StartClip(source, clip, loop, fadeInSeconds);
        }

        private bool CanPlay(AudioClip clip)
        {
            if (!isActiveAndEnabled) return false;
            if (clip != null) return true;
            Debug.LogWarning("[AudioController] Play called with null clip");
            return false;
        }

        public void PlayBgm(string key, bool allowAsyncLoad = true, float fadeOutSeconds = 0f, float fadeInSeconds = 0f)
        {
            PlayByKey(AudioCategory.Bgm, _bgmSource, true, key,
                clip => PlayBgmCore(clip, fadeOutSeconds, fadeInSeconds), allowAsyncLoad);
        }

        public void PlaySfx(string key, bool loop = false, bool allowAsyncLoad = true, float fadeInSeconds = 0f)
        {
            PlayByKey(AudioCategory.Sfx, loop ? _sfxLoopSource : _sfxSource, loop, key,
                clip => PlaySfxCore(clip, loop, fadeInSeconds), allowAsyncLoad);
        }

        public void PlayVoice(string key, bool loop = false, bool allowAsyncLoad = true, float fadeInSeconds = 0f)
        {
            var source = loop ? _voiceLoopSource : _voiceSource;
            PlayByKey(AudioCategory.Voice, source, true, key,
                clip => StartClip(source, clip, loop, fadeInSeconds), allowAsyncLoad);
        }

        private void PlayByKey(AudioCategory category, AudioSource source, bool replace, string key, Action<AudioClip> play, bool allowAsyncLoad)
        {
            if (!isActiveAndEnabled || source == null) return;
            if (string.IsNullOrEmpty(key))
            {
                Debug.LogWarning($"[AudioController] Play {category} called with empty key");
                return;
            }
            var request = BeginRequest(source, replace);
            if (!TryGetClipProvider(out var provider))
            {
                Debug.LogWarning($"[AudioController] No clip provider available, cannot play {category} with key {key}");
                return;
            }
            var providerVersion = _providerVersion;
            var serviceVersion = _serviceVersion;
            if (provider.TryGetClip(category, key, out var clip) && clip != null)
            {
                if (RequestIsCurrent(source, request, provider, providerVersion, serviceVersion)) play(clip);
            }
            else if (allowAsyncLoad && provider is IAsyncAudioClipProvider asyncProvider)
            {
                StartCoroutine(LoadAndPlayAsync(asyncProvider, category, key, source, request, providerVersion, serviceVersion, play));
            }
            else Debug.LogWarning($"[AudioController] Clip for key {key} is unavailable; async loading disabled or unsupported");
        }

        private bool RequestIsCurrent(AudioSource source, long request, IAudioClipProvider provider, long providerVersion, long serviceVersion)
        {
            return this != null && isActiveAndEnabled && source != null &&
                _serviceVersion == serviceVersion && _providerVersion == providerVersion &&
                State(source).RequestVersion == request && ReferenceEquals(_clipProvider, provider) &&
                (!(provider is UnityEngine.Object unityObject) || unityObject != null);
        }

        private IEnumerator LoadAndPlayAsync(IAsyncAudioClipProvider provider, AudioCategory category, string key,
            AudioSource source, long request, long providerVersion, long serviceVersion, Action<AudioClip> play)
        {
            AudioClip clip = null;
            if (provider is IResultAudioClipProvider resultProvider)
                yield return resultProvider.LoadClipAsync(category, key, result => clip = result);
            else
            {
                yield return provider.LoadClipAsync(category, key);
                if (!RequestIsCurrent(source, request, provider, providerVersion, serviceVersion)) yield break;
                // Legacy providers retain their original lookup contract.
                provider.TryGetClip(category, key, out clip);
            }
            if (!RequestIsCurrent(source, request, provider, providerVersion, serviceVersion)) yield break;
            if (clip != null)
            {
                if (RequestIsCurrent(source, request, provider, providerVersion, serviceVersion)) play(clip);
            }
            else Debug.LogWarning($"[AudioController] Failed to load clip asynchronously for key {key}");
        }

        public void FadeBgmTo(float targetVolume, float seconds) => FadeSource(_bgmSource, targetVolume, seconds);
        public void FadeBgmOut(float seconds, bool stopAfter = true) => FadeSource(_bgmSource, 0f, seconds, stopAfter);
        public void FadeSfxLoopTo(float targetVolume, float seconds) => FadeSource(_sfxLoopSource, targetVolume, seconds);
        public void FadeVoiceLoopTo(float targetVolume, float seconds) => FadeSource(_voiceLoopSource, targetVolume, seconds);

        public void FadeChannel(AudioChannel channel, float targetVolume, float seconds, bool stopAfter = false, bool useLoopSource = false)
        {
            if (!isActiveAndEnabled) return;
            switch (channel)
            {
                case AudioChannel.Master:
                    CancelMasterFade();
                    if (stopAfter)
                    {
                        // Snapshot the stop as independent source transitions. Fresh playback
                        // cancels only its old stop; other sources still finish stopping.
                        foreach (var source in AllSources()) FadeSource(source, targetVolume, seconds, true);
                    }
                    else if (seconds <= 0f)
                    {
                        _masterGain = Mathf.Clamp01(targetVolume);
                        ApplyAllGains();
                    }
                    else _masterFade = StartCoroutine(FadeMaster(Mathf.Clamp01(targetVolume), seconds, _masterVersion));
                    break;
                case AudioChannel.Bgm: FadeSource(_bgmSource, targetVolume, seconds, stopAfter); break;
                case AudioChannel.Sfx: FadeSource(useLoopSource ? _sfxLoopSource : _sfxSource, targetVolume, seconds, stopAfter); break;
                case AudioChannel.Voice: FadeSource(useLoopSource ? _voiceLoopSource : _voiceSource, targetVolume, seconds, stopAfter); break;
            }
        }

        private IEnumerable<AudioSource> AllSources()
        {
            yield return _bgmSource;
            yield return _sfxSource;
            yield return _sfxLoopSource;
            yield return _voiceSource;
            yield return _voiceLoopSource;
        }

        private void CancelMasterFade()
        {
            _masterVersion++;
            if (_masterFade != null) StopCoroutine(_masterFade);
            _masterFade = null;
        }

        private IEnumerator FadeMaster(float target, float seconds, long version)
        {
            var start = _masterGain;
            yield return null;
            var startTime = Time.realtimeSinceStartup;
            while (version == _masterVersion && isActiveAndEnabled)
            {
                var t = Mathf.Clamp01((Time.realtimeSinceStartup - startTime) / seconds);
                _masterGain = Mathf.Lerp(start, target, t);
                ApplyAllGains();
                if (t >= 1f) break;
                yield return null;
            }
            if (version == _masterVersion) _masterFade = null;
        }

        public void StopBgm(float fadeOutSeconds = 0f) => FadeSource(_bgmSource, 0f, fadeOutSeconds, true);

        public void StopSfx(bool stopLoopOnly = false, float fadeOutSeconds = 0f)
        {
            if (!stopLoopOnly) FadeSource(_sfxSource, 0f, fadeOutSeconds, true);
            FadeSource(_sfxLoopSource, 0f, fadeOutSeconds, true);
        }

        public void StopVoice(bool stopLoopOnly = false, float fadeOutSeconds = 0f)
        {
            if (!stopLoopOnly) FadeSource(_voiceSource, 0f, fadeOutSeconds, true);
            FadeSource(_voiceLoopSource, 0f, fadeOutSeconds, true);
        }

        private void OnDisable()
        {
            _serviceVersion++;
            CancelMasterFade();
            StopAllCoroutines();
            foreach (var pair in _states)
            {
                pair.Value.RequestVersion++;
                pair.Value.TransitionVersion++;
                pair.Value.Transition = null;
                if (pair.Key != null) StopSourceNow(pair.Key);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
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
