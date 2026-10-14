using System;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

namespace Controller.Audio
{
    public class AudioController : MonoBehaviour
    {
        [SerializeField]
        private MonoBehaviour clipProviderSource;
        [SerializeField]
        private MonoBehaviour settingsSource;
        [SerializeField]
        private AudioMixer mixer;
        [SerializeField]
        private bool loadDefaultMixer = true;
        [SerializeField]
        private AudioCatalog catalog;
        [SerializeField]
        private int maxVoices;
        [SerializeField]
        private AudioConcurrencyPolicy concurrencyPolicy;
        [SerializeField]
        private bool pauseOnBackground = true;
        [SerializeField]
        private bool verboseLogging;
        [SerializeField]
        private bool logPlaybackFailures = true;
        private AudioFailureLog failureLog;
        private IAudioClipProvider clipProvider;
        private AudioCatalog boundCatalog;
        private int catalogRevision;
        private bool providerConfigured, settingsConfigured, providerSubscribed, shuttingDown;
        private AudioPlaybackEngine engine;
        private AudioMixerState mixerState;
        private AudioMixerState MixerState => mixerState ??= new AudioMixerState(categories => engine?.RefreshGains(categories), (channel, value) => VolumeChanged?.Invoke(channel, value));
        public static AudioController Instance { get; private set; }
        public bool Ready { get; private set; }

        public event Action<AudioChannel, float> VolumeChanged;
        public IAudioClipProvider ClipProvider => AudioValues.Alive(clipProvider) ? clipProvider : null;

        public AudioCatalog Catalog
        {
            get => catalog;
            set
            {
                if (catalog == value)
                    return;
                catalog = value;
                RefreshCatalog();
            }
        }

        public AudioDiagnostics Diagnostics => engine != null ? engine.Diagnostics : default;
        public string LastMixerIssue => MixerState.LastIssue;

        public int MaxVoices
        {
            get => maxVoices;
            set
            {
                maxVoices = Mathf.Max(0, value);
                if (engine != null)
                    engine.MaxVoices = maxVoices;
            }
        }

        public AudioConcurrencyPolicy ConcurrencyPolicy
        {
            get => concurrencyPolicy;
            set
            {
                concurrencyPolicy = value;
                if (engine != null)
                    engine.ConcurrencyPolicy = value;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void ReconnectWithoutSceneReload()
        {
            foreach (var controller in FindObjectsByType<AudioController>(FindObjectsInactive.Exclude))
                if (controller.isActiveAndEnabled)
                    controller.Initialize();
        }

        private void Awake() => Initialize();
        private void OnEnable() => Initialize();
        public void Initialize()
        {
            if (!isActiveAndEnabled || shuttingDown)
                return;
            if (Instance != null && Instance != this)
            {
                ReportDiscardedDuplicate(Instance);
                Destroy(gameObject);
                return;
            }

            Instance = this;
            if (Ready)
            {
                AttachUnityEvents();
                return;
            }

            failureLog = null;
            if (transform.parent == null)
                DontDestroyOnLoad(gameObject);
            if (!providerConfigured)
                clipProvider = clipProviderSource as IAudioClipProvider;
            if (mixer == null && loadDefaultMixer)
                mixer = Resources.Load<AudioMixer>("Audio/MasterMixer");
            MixerState.Configure(mixer);
            Ready = true;
            engine = new AudioPlaybackEngine(this, ClipProvider)
            {
                MaxVoices = maxVoices,
                ConcurrencyPolicy = concurrencyPolicy
            };
            var current = engine;
            BindCatalog();
            if (!settingsConfigured)
                MixerState.BindSettings(settingsSource as IAudioSettingsHandler, true, true);
            else
                MixerState.AttachSettings();
            if (!IsCurrentEngine(current))
                return;
            AttachProviderNotifications();
            AttachUnityEvents();
        }

        // Copies of the same AudioCtrl in every scene are a common setup and are removed silently.
        // A different provider or catalog would be lost without notice, so name the active one.
        private void ReportDiscardedDuplicate(AudioController active)
        {
            if (!Application.isEditor && !Debug.isDebugBuild)
                return;
            static Type SourceType(MonoBehaviour source) => source != null ? source.GetType() : null;
            if (SourceType(clipProviderSource) == SourceType(active.clipProviderSource) && catalog == active.catalog)
                return;
            Debug.LogWarning($"[AudioService] Removed duplicate AudioController '{name}'. '{active.name}' is already the active service and keeps its own clip provider and catalog. Keep one AudioCtrl, or configure the active one with SetClipProvider / Catalog.", active);
        }

        public void Shutdown()
        {
            if (!Ready)
                return;
            Ready = false;
            shuttingDown = true;
            try
            {
                DetachUnityEvents();
                MixerState.DetachSettings();
                DetachProviderNotifications();
                UnbindCatalog();
                engine?.Shutdown();
                engine = null;
            }
            finally
            {
                shuttingDown = false;
            }
        }

        private void OnDisable() => Shutdown();
        private void OnDestroy()
        {
            Shutdown();
            if (Instance == this)
                Instance = null;
        }

        private void OnApplicationPause(bool paused)
        {
            if (pauseOnBackground)
                engine?.SetPaused(paused, true);
        }

        private void Update()
        {
            var current = engine;
            if (!Ready || current == null)
                return;
            if (!MixerState.Applied)
            {
                MixerState.Apply();
                ValidateMixer();
            }

            if (clipProvider != null && !AudioValues.Alive(clipProvider))
            {
                SetClipProvider(null);
                if (!IsCurrentEngine(current))
                    return;
            }
            SyncCatalog();
            if (IsCurrentEngine(current))
                current.Tick(Time.unscaledDeltaTime);
        }

        // A callback may shut down and initialize again before the old call returns.
        private bool IsCurrentEngine(AudioPlaybackEngine expected)
            => Ready && expected != null && ReferenceEquals(engine, expected) && this != null && isActiveAndEnabled;

        private void AttachUnityEvents()
        {
            DetachUnityEvents();
            AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void DetachUnityEvents()
        {
            AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        // Unity unloads unused assets on a non-additive load. Release the ordinary
        // playback cache at the same boundary; playing sounds and PrepareClip groups keep their clips.
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Single && Ready)
                engine?.ReleaseUnused();
        }

        private void OnAudioConfigurationChanged(bool deviceWasChanged)
        {
            if (!Ready || engine == null)
                return;
            using var mutation = engine.BeginMutation();
            MixerState.Configure(mixer);
            engine.AudioConfigurationChanged();
            MixerState.Apply();
            ValidateMixer();
        }

        public void SetClipProvider(IAudioClipProvider provider)
        {
            providerConfigured = true;
            if (ReferenceEquals(clipProvider, provider))
                return;
            failureLog = null;
            DetachProviderNotifications();
            clipProvider = provider;
            engine?.SetProvider(provider);
            AttachProviderNotifications();
        }

        public void RefreshClipProvider() => RefreshClipState(true);
        private void RefreshCatalog() => RefreshClipState(false);

        private void RefreshClipState(bool refreshLookup)
        {
            failureLog = null;
            BindCatalog();
            if (Ready)
                engine?.SetProvider(ClipProvider, force: true, refreshLookup: refreshLookup);
        }

        private void BindCatalog()
        {
            UnbindCatalog();
            boundCatalog = catalog;
            catalogRevision = catalog != null ? catalog.Revision : 0;
            if (Ready && boundCatalog != null)
                boundCatalog.Changed += RefreshCatalog;
        }

        private void UnbindCatalog()
        {
            if (boundCatalog != null)
                boundCatalog.Changed -= RefreshCatalog;
            boundCatalog = null;
        }

        private void SyncCatalog()
        {
            // Inspector/serialization callbacks can run off-thread. Observe their revision here.
            if (boundCatalog != catalog || (catalog != null && catalogRevision != catalog.Revision))
                RefreshCatalog();
        }

        private void AttachProviderNotifications()
        {
            if (Ready && !providerSubscribed && clipProvider is IAudioClipProviderChanges changes)
            {
                changes.Changed += RefreshClipProvider;
                providerSubscribed = true;
            }
        }

        private void DetachProviderNotifications()
        {
            if (providerSubscribed && clipProvider is IAudioClipProviderChanges changes)
                changes.Changed -= RefreshClipProvider;
            providerSubscribed = false;
        }

        private bool TryResolve(AudioCategory category, AudioId id, out AudioPlaybackEngine current, out ResolvedAudioClip address)
        {
            current = engine;
            address = default;
            if (!Ready || current == null)
                return false;
            SyncCatalog();
            if (!IsCurrentEngine(current))
                return false;
            if (catalog == null || !catalog.TryResolve(category, id, out address))
                address = new ResolvedAudioClip(id.Value, category);
            return true;
        }

        internal AudioHandle Request(AudioCategory category, AudioId id, AudioClip clip, PlayOptions options, PlaybackSlot slot)
        {
            if (clip != null)
            {
                if (Ready && engine != null)
                    return engine.Play(category, null, clip, options, slot, id);
            }
            else if (TryResolve(category, id, out var current, out var address))
                return current.Play(category, address, null, options, slot, id);

            var failed = new AudioHandle { AudioId = id, Category = category };
            failed.Finish(AudioCompletion.Failed, "Audio service is not ready");
            ReportPlaybackFailure(category, id, "Audio service is not ready. Enable AudioCtrl.prefab and call playback from Start or later");
            return failed;
        }

        /// <summary>Advanced: play in any category without the BGM or dialogue replacement slot.
        /// BGM loops only when PlayOptions.Loop is set. Daily playback uses AudioService.</summary>
        public AudioHandle PlayIndependent(AudioCategory category, AudioId id, PlayOptions options = null) => Request(category, id, null, options, PlaybackSlot.None);
        public AudioHandle PlayIndependent(AudioCategory category, AudioClip clip, PlayOptions options = null) => Request(category, default, clip, options, PlaybackSlot.None);
        public void StopBgm(float fadeOutSeconds = 0) => engine?.StopCategory(AudioCategory.Bgm, AudioValues.Seconds(fadeOutSeconds));
        public void StopSfx(float fadeOutSeconds = 0) => engine?.StopCategory(AudioCategory.Sfx, AudioValues.Seconds(fadeOutSeconds));
        public void StopVoice(float fadeOutSeconds = 0) => engine?.StopCategory(AudioCategory.Voice, AudioValues.Seconds(fadeOutSeconds));
        public void StopAll(float fadeOutSeconds = 0) => engine?.StopCategory(null, AudioValues.Seconds(fadeOutSeconds));
        public void FadeBus(AudioChannel channel, float targetVolume, float seconds)
        {
            if (Ready && AudioValues.IsValidChannel(channel))
                engine.FadeBus(channel, AudioValues.Unit(targetVolume), AudioValues.Seconds(seconds));
        }

        public void SetGamePaused(bool paused) => engine?.SetPaused(paused);
        public void SetBackgroundPaused(bool paused) => engine?.SetPaused(paused, true);
        public void SetMuted(AudioChannel channel, bool muted)
        {
            if (AudioValues.IsValidChannel(channel))
                engine?.SetMuted(channel, muted);
        }

        /// <summary>Grow total source capacity (active + idle). Call during loading; does not load clips.</summary>
        public int PrewarmSources(int targetCount) => Ready && engine != null ? engine.PrewarmSources(targetCount) : 0;
        /// <summary>Remove only idle sources, retaining minimumCapacity total sources.</summary>
        public int TrimIdleSources(int minimumCapacity = 0, int maxToRemove = 32) => Ready && engine != null ? engine.TrimIdleSources(minimumCapacity, maxToRemove) : 0;

        /// <summary>Retain a clip until group release, completing when Unity reports its audio data loaded.</summary>
        public void PrepareClip(AudioCategory category, AudioId id, string group, Action<bool> completed = null)
        {
            if (CanPrepare(category, id, group) && TryResolve(category, id, out var current, out var address))
                current.PrepareClip(address, group, completed);
            else
                AudioCallbacks.Invoke(completed, false);
        }

        private static bool CanPrepare(AudioCategory category, AudioId id, string group)
            => AudioValues.IsValidCategory(category) && !string.IsNullOrWhiteSpace(group) && !string.IsNullOrWhiteSpace(id.Value);
        public void ReleaseGroup(string group)
        {
            if (!string.IsNullOrWhiteSpace(group))
                engine?.ReleaseGroup(group);
        }

        public void ReleaseUnusedClips() => engine?.ReleaseUnused();
        public bool TryGetCachedClip(AudioCategory category, AudioId id, out AudioClip clip)
        {
            clip = null;
            return TryResolve(category, id, out var current, out var address) && current.TryGetCached(address, out clip);
        }

        public void BindSettings(IAudioSettingsHandler handler, bool applyStored = true)
        {
            settingsConfigured = true;
            MixerState.BindSettings(handler, Ready, applyStored);
        }

        public void UnbindSettings(IAudioSettingsHandler handler) => MixerState.UnbindSettings(handler);
        public float GetVolume(AudioChannel channel) => MixerState.GetVolume(channel);
        public AudioChannelDiagnostics GetChannelDiagnostics(AudioChannel channel)
        {
            if (!AudioValues.IsValidChannel(channel))
                return default;
            return engine != null ? engine.GetChannelDiagnostics(channel, GetVolume(channel)) : new AudioChannelDiagnostics(GetVolume(channel), 1, false);
        }

        public void SetMasterVolume(float value) => MixerState.SetVolume(AudioChannel.Master, value);
        public void SetBgmVolume(float value) => MixerState.SetVolume(AudioChannel.Bgm, value);
        public void SetSfxVolume(float value) => MixerState.SetVolume(AudioChannel.Sfx, value);
        public void SetVoiceVolume(float value) => MixerState.SetVolume(AudioChannel.Voice, value);
        public void ApplyVolume(AudioChannel channel, float value, bool persist = true) => MixerState.SetVolume(channel, value, persist);
        public void SetMixer(AudioMixer value)
        {
            mixer = value;
            loadDefaultMixer = false;
            MixerState.Configure(value);
            engine?.RefreshRouting();
            MixerState.Apply();
            ValidateMixer();
        }

        public bool ValidateMixer()
        {
            bool valid = MixerState.Validate();
            if (!valid)
                ReportFailure(LastMixerIssue);
            return valid;
        }

        internal void ConfigureSource(AudioSource source, AudioCategory category) => MixerState.ConfigureSource(source, category);
        internal float SourceSettingsGain(AudioCategory category) => MixerState.SourceGain(category);
        internal void ReportFailure(string message)
        {
            if (verboseLogging)
                Debug.LogWarning("[AudioController] " + message, this);
        }

        private bool LogsFailures => logPlaybackFailures && (Application.isEditor || Debug.isDebugBuild);
        internal void ReportPlaybackFailure(AudioCategory category, AudioId id, string reason, ResolvedAudioClip? address = null, bool allowAsync = true)
        {
            if (LogsFailures)
                (failureLog ??= new AudioFailureLog()).Report(this, category, id, reason, address, allowAsync);
        }

        internal void ReportControlFailure(string reason)
        {
            if (LogsFailures)
                (failureLog ??= new AudioFailureLog()).ReportControl(this, reason);
        }
    }
}
