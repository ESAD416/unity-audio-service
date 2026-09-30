using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace Controller.Audio
{
    public class AudioController : MonoBehaviour
    {
        [SerializeField] private bool verboseLogging;
        private bool VerboseLogging => verboseLogging;
        public static AudioController Instance { get; private set; }
        [SerializeField] private AudioMixer mixer;
        [SerializeField] private bool loadDefaultMixer = true;
        [SerializeField] private AudioCatalog catalog;
        [SerializeField] private int maxVoices;
        [SerializeField] private AudioConcurrencyPolicy concurrencyPolicy;
        [SerializeField] private bool pauseOnBackground = true;
        private const string MIXER_DEFAULT_RESOURCE_PATH = "Audio/MasterMixer";
        private static readonly string[] Parameters = { "masterVolume", "bgmVolume", "soundVolume", "voiceVolume" };
        private AudioMixerGroup _bgmGroup, _soundGroup, _voiceGroup;
        private AudioSource _bgmSource, _sfxSource, _sfxLoopSource, _voiceSource, _voiceLoopSource;
        private static IAudioClipProvider globalProvider;
        private static object registrationOwner;
        private IAudioClipProvider clipProvider;
        private IAudioSettingsHandler settingsHandler;
        private bool shuttingDown;
        private bool settingsSubscribed, applyingSettings, mixerApplied, providerSubscribed;
        private readonly float[] volumes = { 1, 1, 1, 1 };
        private readonly bool[] mixerParameters = new bool[4];
        private AudioPlaybackEngine engine;
        public bool Ready { get; private set; }
        public event Action<AudioChannel, float> VolumeChanged;
        public IAudioClipProvider ClipProvider => AudioValues.Alive(clipProvider) ? clipProvider : null;
        public AudioCatalog Catalog { get => catalog; set { if (catalog == value) return; catalog = value; RefreshClipProvider(); } }
        public AudioDiagnostics Diagnostics => engine != null ? engine.Diagnostics : default;
        public string LastMixerIssue { get; private set; }
        public int MaxVoices { get => maxVoices; set { maxVoices = Mathf.Max(0, value); if (engine != null) engine.MaxVoices = maxVoices; } }
        public AudioConcurrencyPolicy ConcurrencyPolicy { get => concurrencyPolicy; set { concurrencyPolicy = value; if (engine != null) engine.ConcurrencyPolicy = value; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { Instance = null; globalProvider = null; registrationOwner = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void ReconnectWithoutSceneReload()
        {
            foreach (var controller in FindObjectsByType<AudioController>(FindObjectsInactive.Exclude))
                if (controller.isActiveAndEnabled) controller.Initialize();
            foreach (var bootstrap in FindObjectsByType<AudioBootstrap>(FindObjectsInactive.Exclude))
                if (bootstrap.isActiveAndEnabled) bootstrap.Reconnect();
        }
        public static void RegisterClipProvider(IAudioClipProvider provider) => RegisterClipProvider(provider, provider);
        public static void RegisterClipProvider(IAudioClipProvider provider, object owner)
        {
            globalProvider = provider; registrationOwner = owner;
            if (Instance != null) Instance.SetClipProvider(provider);
        }
        public static void UnregisterClipProvider(object owner)
        {
            if (!ReferenceEquals(registrationOwner, owner)) return;
            var old = globalProvider; globalProvider = null; registrationOwner = null;
            if (Instance != null && ReferenceEquals(Instance.clipProvider, old)) Instance.SetClipProvider(null);
        }
        public void SetClipProvider(IAudioClipProvider provider)
        { if (ReferenceEquals(clipProvider, provider)) return; DetachProviderNotifications(); clipProvider = provider; engine?.SetProvider(provider); AttachProviderNotifications(); }
        public void RefreshClipProvider()
        {
            // Publish new lookup data before cancellation callbacks can request playback.
            if (catalog != null) catalog.RebuildIndex();
            if (Ready) engine?.SetProvider(clipProvider, true, true);
        }
        private void AttachProviderNotifications()
        { if (Ready && !providerSubscribed && clipProvider is IAudioClipProviderChanges changes) { changes.Changed += RefreshClipProvider; providerSubscribed = true; } }
        private void DetachProviderNotifications()
        { if (providerSubscribed && clipProvider is IAudioClipProviderChanges changes) changes.Changed -= RefreshClipProvider; providerSubscribed = false; }
        private void Awake() { Initialize(); }
        private void OnEnable() { Initialize(); }
        public void Initialize()
        {
            if (!isActiveAndEnabled || shuttingDown) return;
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            if (Ready) { AttachAudioConfiguration(); return; }
            if (transform.parent == null) DontDestroyOnLoad(gameObject);
            if (catalog != null) catalog.RebuildIndex();
            if (!AudioValues.Alive(clipProvider)) clipProvider = AudioValues.Alive(globalProvider) ? globalProvider : null;
            LoadAudioMixer(); InitializeMixer(); EnsureAudioSources();
            Ready = true;
            engine = new AudioPlaybackEngine(this, new[] { _bgmSource, _sfxSource, _sfxLoopSource, _voiceSource, _voiceLoopSource }, clipProvider)
            { MaxVoices = maxVoices, ConcurrencyPolicy = concurrencyPolicy };
            mixerApplied = false; AttachSettings(); AttachProviderNotifications(); AttachAudioConfiguration();
        }
        private void AttachAudioConfiguration()
        {
            AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
            AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
        }
        private void OnAudioConfigurationChanged(bool deviceWasChanged)
        {
            if (!Ready || engine == null) return;
            using var mutation = engine.BeginMutation();
            InitializeMixer();
            engine.AudioConfigurationChanged();
            ApplyMixerSettings(); ValidateMixer();
        }
        public void Shutdown()
        {
            if (!Ready) return;
            Ready = false; shuttingDown = true;
            try
            {
                AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
                DetachSettings(); DetachProviderNotifications(); engine?.Shutdown();
            }
            finally { StopAllCoroutines(); shuttingDown = false; }
        }
        private void OnDisable() { Shutdown(); }
        private void OnDestroy() { Shutdown(); if (Instance == this) Instance = null; }
        private void OnApplicationPause(bool paused) { if (pauseOnBackground) engine?.SetPaused(paused, true); }
        private void Update()
        {
            if (!Ready) return;
            if (!mixerApplied) { ApplyMixerSettings(); ValidateMixer(); }
            if (clipProvider != null && !AudioValues.Alive(clipProvider)) SetClipProvider(null);
            engine.Tick(Time.unscaledDeltaTime);
        }
        internal void ReportFailure(string message) { if (verboseLogging) Debug.LogWarning("[AudioController] " + message, this); }

        private void LoadAudioMixer()
        {
            if (mixer != null || !loadDefaultMixer) return;

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

        private AudioClipAddress Resolve(AudioCategory category, AudioId id)
        {
            if (catalog != null && catalog.TryResolve(category, id, out var address)) return address;
            return new AudioClipAddress(id.Value, category);
        }
        private AudioHandle Request(AudioCategory category, AudioId id, AudioClip clip, PlayOptions options, int bank)
        {
            if (!Ready || engine == null)
            {
                var failed = new AudioHandle { AudioId = id, Category = category };
                failed.Finish(AudioCompletion.Failed, "Audio service is not ready"); return failed;
            }
            return engine.Play(category, clip == null ? Resolve(category, id) : null, clip, options, bank);
        }
        public AudioHandle Play(AudioCategory category, AudioId id, PlayOptions options = null) => Request(category, id, null, options, -1);
        public AudioHandle Play(AudioCategory category, AudioClip clip, PlayOptions options = null) => Request(category, default, clip, options, -1);
        public AudioHandle PlayBgmHandle(AudioId id, PlayOptions options = null)
            => Request(AudioCategory.Bgm, id, null, options, 0);
        public AudioHandle PlaySfxHandle(AudioId id, PlayOptions options = null) => Play(AudioCategory.Sfx, id, options);
        public AudioHandle PlayVoiceHandle(AudioId id, PlayOptions options = null) => Play(AudioCategory.Voice, id, options);

        public void PlayBgm(AudioClip clip, float fadeOutSeconds = 0f, float fadeInSeconds = 0f)
        { Request(AudioCategory.Bgm, default, clip, new PlayOptions { Loop = true, FadeOutSeconds = fadeOutSeconds, FadeInSeconds = fadeInSeconds }, 0); }
        public void PlayBgm(string key, bool allowAsyncLoad = true, float fadeOutSeconds = 0f, float fadeInSeconds = 0f)
        { Request(AudioCategory.Bgm, new AudioId(key), null, new PlayOptions { Loop = true, AllowAsyncLoad = allowAsyncLoad, FadeOutSeconds = fadeOutSeconds, FadeInSeconds = fadeInSeconds }, 0); }
        public void PlaySfx(AudioClip clip, bool loop = false, float fadeInSeconds = 0f)
        { Request(AudioCategory.Sfx, default, clip, new PlayOptions { Loop = loop, FadeInSeconds = fadeInSeconds }, loop ? 2 : 1); }
        public void PlaySfx(string key, bool loop = false, bool allowAsyncLoad = true, float fadeInSeconds = 0f)
        { Request(AudioCategory.Sfx, new AudioId(key), null, new PlayOptions { Loop = loop, AllowAsyncLoad = allowAsyncLoad, FadeInSeconds = fadeInSeconds }, loop ? 2 : 1); }
        public void PlayVoice(AudioClip clip, bool loop = false, float fadeInSeconds = 0f)
        { Request(AudioCategory.Voice, default, clip, new PlayOptions { Loop = loop, FadeInSeconds = fadeInSeconds }, loop ? 4 : 3); }
        public void PlayVoice(string key, bool loop = false, bool allowAsyncLoad = true, float fadeInSeconds = 0f)
        { Request(AudioCategory.Voice, new AudioId(key), null, new PlayOptions { Loop = loop, AllowAsyncLoad = allowAsyncLoad, FadeInSeconds = fadeInSeconds }, loop ? 4 : 3); }
        public void StopBgm(float fadeOutSeconds = 0f) => engine?.StopCategory(AudioCategory.Bgm, false, AudioValues.Seconds(fadeOutSeconds));
        public void StopSfx(bool stopLoopOnly = false, float fadeOutSeconds = 0f) => engine?.StopCategory(AudioCategory.Sfx, stopLoopOnly, AudioValues.Seconds(fadeOutSeconds));
        public void StopVoice(bool stopLoopOnly = false, float fadeOutSeconds = 0f) => engine?.StopCategory(AudioCategory.Voice, stopLoopOnly, AudioValues.Seconds(fadeOutSeconds));
        public void FadeBgmTo(float targetVolume, float seconds) => FadeChannel(AudioChannel.Bgm, targetVolume, seconds);
        public void FadeBgmOut(float seconds, bool stopAfter = true) => FadeChannel(AudioChannel.Bgm, 0, seconds, stopAfter);
        public void FadeSfxLoopTo(float targetVolume, float seconds) => FadeChannel(AudioChannel.Sfx, targetVolume, seconds, false, true);
        public void FadeVoiceLoopTo(float targetVolume, float seconds) => FadeChannel(AudioChannel.Voice, targetVolume, seconds, false, true);
        public void FadeChannel(AudioChannel channel, float targetVolume, float seconds, bool stopAfter = false, bool useLoopSource = false)
        {
            if (!Ready || (int)channel < 0 || (int)channel > 3) return;
            if (channel == AudioChannel.Master) FadeBus(channel, targetVolume, seconds, stopAfter);
            else engine.FadeLegacy(channel == AudioChannel.Bgm ? 0 : channel == AudioChannel.Sfx ? (useLoopSource ? 2 : 1) : (useLoopSource ? 4 : 3), AudioValues.Unit(targetVolume), AudioValues.Seconds(seconds), stopAfter);
        }
        public void FadeBus(AudioChannel channel, float targetVolume, float seconds, bool stopAfter = false)
        { if (Ready && (int)channel >= 0 && (int)channel < 4) engine.FadeBus(channel, AudioValues.Unit(targetVolume), AudioValues.Seconds(seconds), stopAfter); }
        public void SetGamePaused(bool paused) => engine?.SetPaused(paused);
        public void SetBackgroundPaused(bool paused) => engine?.SetPaused(paused, true);
        public void SetMuted(AudioChannel channel, bool muted) { if ((int)channel >= 0 && (int)channel < 4) engine?.SetMuted(channel, muted); }
        /// <summary>On the main thread, grow total dynamic source capacity (active + idle) to targetCount.
        /// Excludes the five legacy sources. Returns sources created; nonpositive targets or an unready service return zero.
        /// Call during loading: this creates objects synchronously, without loading clips or changing voice limits.</summary>
        public int PrewarmSources(int targetCount) => Ready && engine != null ? engine.PrewarmSources(targetCount) : 0;
        /// <summary>Remove at most maxToRemove idle dynamic sources while retaining
        /// minimumCapacity total dynamic sources (active + idle). Never removes active,
        /// loading or legacy sources. Native destruction completes at the end of the frame.</summary>
        public int TrimIdleSources(int minimumCapacity = 0, int maxToRemove = 32)
            => Ready && engine != null ? engine.TrimIdleSources(minimumCapacity, maxToRemove) : 0;
        public void Preload(AudioCategory category, AudioId id, string group, Action<bool> completed = null)
        { if (!Ready || string.IsNullOrWhiteSpace(group) || string.IsNullOrWhiteSpace(id.Value)) { AudioCallbacks.Invoke(completed, false); return; } engine.Preload(Resolve(category, id), group, completed); }
        /// <summary>Retain a clip in a group and complete when Unity reports its audio data loaded.
        /// Call on the main thread during a loading phase: LoadAudioData may block depending on import settings.</summary>
        public void PrepareClip(AudioCategory category, AudioId id, string group, Action<bool> completed = null)
        {
            if (!Ready || (int)category < 0 || (int)category > 2 || string.IsNullOrWhiteSpace(group) || string.IsNullOrWhiteSpace(id.Value))
            { AudioCallbacks.Invoke(completed, false); return; }
            engine.PrepareClip(Resolve(category, id), group, completed);
        }
        public void ReleaseGroup(string group) { if (!string.IsNullOrWhiteSpace(group)) engine?.ReleaseGroup(group); }
        public void ReleaseUnusedClips() => engine?.ReleaseUnused();
        public bool TryGetCachedClip(AudioCategory category, AudioId id, out AudioClip clip)
        { clip = null; return Ready && engine.TryGetCached(Resolve(category, id), out clip); }

        public void BindSettings(IAudioSettingsHandler handler, bool applyStored = true)
        { DetachSettings(); settingsHandler = handler; AttachSettings(applyStored); }
        public void UnbindSettings(IAudioSettingsHandler handler)
        { if (ReferenceEquals(settingsHandler, handler)) { DetachSettings(); settingsHandler = null; } }
        private void AttachSettings(bool applyStored = true)
        {
            if (!Ready || !AudioValues.Alive(settingsHandler) || settingsSubscribed) return;
            settingsHandler.VolumeChanged += SettingsChanged; settingsSubscribed = true;
            if (applyStored) ApplySettings();
        }
        private void DetachSettings()
        { if (settingsHandler != null && settingsSubscribed) settingsHandler.VolumeChanged -= SettingsChanged; settingsSubscribed = false; }
        private void ApplySettings()
        {
            if (!AudioValues.Alive(settingsHandler)) return;
            SettingsChanged(AudioChannel.Master, settingsHandler.MasterVolume); SettingsChanged(AudioChannel.Bgm, settingsHandler.BgmVolume);
            SettingsChanged(AudioChannel.Sfx, settingsHandler.SfxVolume); SettingsChanged(AudioChannel.Voice, settingsHandler.VoiceVolume);
        }
        private void SettingsChanged(AudioChannel channel, float value)
        {
            bool previous = applyingSettings; applyingSettings = true;
            try { SetVolume(channel, value); } finally { applyingSettings = previous; }
        }
        public float GetVolume(AudioChannel channel) => (int)channel >= 0 && (int)channel < 4 ? volumes[(int)channel] : 0;
        public void SetMasterVolume(float normalizedVolume) => SetVolume(AudioChannel.Master, normalizedVolume);
        public void SetBgmVolume(float normalizedVolume) => SetVolume(AudioChannel.Bgm, normalizedVolume);
        public void SetSfxVolume(float normalizedVolume) => SetVolume(AudioChannel.Sfx, normalizedVolume);
        public void SetVoiceVolume(float normalizedVolume) => SetVolume(AudioChannel.Voice, normalizedVolume);
        public void ApplyVolume(AudioChannel channel, float value, bool persist = true)
        {
            bool previous = applyingSettings; applyingSettings = !persist;
            try { SetVolume(channel, value); } finally { applyingSettings = previous; }
        }
        private void SetVolume(AudioChannel channel, float value)
        {
            int index = (int)channel; if (index < 0 || index > 3) return;
            value = AudioValues.Unit(value); bool changed = volumes[index] != value;
            if (changed)
            {
                float bgmGain = SourceSettingsGain(AudioCategory.Bgm);
                float sfxGain = SourceSettingsGain(AudioCategory.Sfx);
                float voiceGain = SourceSettingsGain(AudioCategory.Voice);
                volumes[index] = value;
                if (mixerApplied) ApplyMixerParameter(index);
                // A working mixer applies player volume itself. Refresh sources only
                // when their fallback gain changes, including a failed mixer write.
                if (bgmGain != SourceSettingsGain(AudioCategory.Bgm) ||
                    sfxGain != SourceSettingsGain(AudioCategory.Sfx) ||
                    voiceGain != SourceSettingsGain(AudioCategory.Voice)) engine?.RefreshGains();
            }
            // An unchanged effective volume may still need to persist a temporary
            // ApplyVolume(..., persist: false) value or synchronize a newly bound handler.
            if (!applyingSettings && AudioValues.Alive(settingsHandler)) settingsHandler.UpdateVolume(channel, value);
            if (changed) VolumeChanged?.Invoke(channel, value);
        }
        private void ApplyMixerParameter(int index)
        { mixerParameters[index] = mixer != null && mixer.SetFloat(Parameters[index], AudioValues.Decibels(volumes[index])); }
        private void ApplyMixerSettings()
        {
            mixerApplied = true;
            for (int i = 0; i < 4; i++) ApplyMixerParameter(i);
            engine?.RefreshGains();
        }
        public void SetMixer(AudioMixer value)
        {
            mixer = value; loadDefaultMixer = false; _bgmGroup = null; _soundGroup = null; _voiceGroup = null;
            InitializeMixer(); engine?.RefreshRouting(); ApplyMixerSettings(); ValidateMixer();
        }
        public bool ValidateMixer()
        {
            LastMixerIssue = null;
            if (mixer == null) return true;
            for (int i = 0; i < 4; i++) if (!mixer.GetFloat(Parameters[i], out _)) LastMixerIssue = "Missing exposed parameter: " + Parameters[i];
            if (_bgmGroup == null || _soundGroup == null || _voiceGroup == null) LastMixerIssue = "Missing BGM, Sound or Voice mixer group";
            if (LastMixerIssue != null) ReportFailure(LastMixerIssue);
            return LastMixerIssue == null;
        }
        internal void ConfigureSource(AudioSource source, AudioCategory category)
        {
            source.playOnAwake = false; source.spatialBlend = 0; source.mute = false;
            source.outputAudioMixerGroup = category == AudioCategory.Bgm ? _bgmGroup : category == AudioCategory.Sfx ? _soundGroup : _voiceGroup;
        }
        internal float SourceSettingsGain(AudioCategory category)
        {
            int channel = (int)category + 1;
            var group = category == AudioCategory.Bgm ? _bgmGroup : category == AudioCategory.Sfx ? _soundGroup : _voiceGroup;
            bool routed = mixer != null && group != null;
            return (routed && mixerParameters[0] ? 1 : volumes[0]) * (routed && mixerParameters[channel] ? 1 : volumes[channel]);
        }
    }
}
