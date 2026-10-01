using System;
using UnityEngine;
using UnityEngine.Audio;

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
                RefreshClipProvider();
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
                Destroy(gameObject);
                return;
            }

            Instance = this;
            if (Ready)
            {
                AttachAudioConfiguration();
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
            BindCatalog();
            if (!settingsConfigured)
                MixerState.BindSettings(settingsSource as IAudioSettingsHandler, true, true);
            else
                MixerState.AttachSettings();
            AttachProviderNotifications();
            AttachAudioConfiguration();
        }

        public void Shutdown()
        {
            if (!Ready)
                return;
            Ready = false;
            shuttingDown = true;
            try
            {
                AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
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
            if (!Ready)
                return;
            if (!MixerState.Applied)
            {
                MixerState.Apply();
                ValidateMixer();
            }

            if (clipProvider != null && !AudioValues.Alive(clipProvider))
                SetClipProvider(null);
            SyncCatalog();
            engine.Tick(Time.unscaledDeltaTime);
        }

        private void AttachAudioConfiguration()
        {
            AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
            AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
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

        public void RefreshClipProvider()
        {
            failureLog = null;
            BindCatalog();
            if (Ready)
                engine?.SetProvider(ClipProvider, true, true);
        }

        private void BindCatalog()
        {
            UnbindCatalog();
            boundCatalog = catalog;
            catalogRevision = catalog != null ? catalog.Revision : 0;
            if (Ready && boundCatalog != null)
                boundCatalog.Changed += RefreshClipProvider;
        }

        private void UnbindCatalog()
        {
            if (boundCatalog != null)
                boundCatalog.Changed -= RefreshClipProvider;
            boundCatalog = null;
        }

        private void SyncCatalog()
        {
            // Inspector/serialization callbacks can run off-thread. Observe their revision here.
            if (boundCatalog != catalog || (catalog != null && catalogRevision != catalog.Revision))
                RefreshClipProvider();
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

        private ResolvedAudioClip Resolve(AudioCategory category, AudioId id)
        {
            SyncCatalog();
            return catalog != null && catalog.TryResolve(category, id, out var address) ? address : new ResolvedAudioClip(id.Value, category);
        }

        internal AudioHandle Request(AudioCategory category, AudioId id, AudioClip clip, PlayOptions options, PlaybackSlot slot)
        {
            if (!Ready || engine == null)
            {
                var failed = new AudioHandle
                {
                    AudioId = id,
                    Category = category
                };
                failed.Finish(AudioCompletion.Failed, "Audio service is not ready");
                ReportPlaybackFailure(failed, "Audio service is not ready. Enable AudioCtrl.prefab and call playback from Start or later");
                return failed;
            }

            return engine.Play(category, clip == null ? Resolve(category, id) : null, clip, options, slot, id);
        }

        public AudioHandle Play(AudioCategory category, AudioId id, PlayOptions options = null) => Request(category, id, null, options, PlaybackSlot.None);
        public AudioHandle Play(AudioCategory category, AudioClip clip, PlayOptions options = null) => Request(category, default, clip, options, PlaybackSlot.None);
        public void StopBgm(float fadeOutSeconds = 0) => engine?.StopCategory(AudioCategory.Bgm, AudioValues.Seconds(fadeOutSeconds));
        public void StopSfx(float fadeOutSeconds = 0) => engine?.StopCategory(AudioCategory.Sfx, AudioValues.Seconds(fadeOutSeconds));
        public void StopVoice(float fadeOutSeconds = 0) => engine?.StopCategory(AudioCategory.Voice, AudioValues.Seconds(fadeOutSeconds));
        public void FadeBus(AudioChannel channel, float targetVolume, float seconds, bool stopAfter = false)
        {
            if (Ready && (int)channel >= 0 && (int)channel < 4)
                engine.FadeBus(channel, AudioValues.Unit(targetVolume), AudioValues.Seconds(seconds), stopAfter);
        }

        public void SetGamePaused(bool paused) => engine?.SetPaused(paused);
        public void SetBackgroundPaused(bool paused) => engine?.SetPaused(paused, true);
        public void SetMuted(AudioChannel channel, bool muted)
        {
            if ((int)channel >= 0 && (int)channel < 4)
                engine?.SetMuted(channel, muted);
        }

        /// <summary>Grow total source capacity (active + idle). Call during loading; does not load clips.</summary>
        public int PrewarmSources(int targetCount) => Ready && engine != null ? engine.PrewarmSources(targetCount) : 0;
        /// <summary>Remove only idle sources, retaining minimumCapacity total sources.</summary>
        public int TrimIdleSources(int minimumCapacity = 0, int maxToRemove = 32) => Ready && engine != null ? engine.TrimIdleSources(minimumCapacity, maxToRemove) : 0;
        public void Preload(AudioCategory category, AudioId id, string group, Action<bool> completed = null)
        {
            if (!CanPrepare(category, id, group))
            {
                AudioCallbacks.Invoke(completed, false);
                return;
            }

            engine.Preload(Resolve(category, id), group, completed);
        }

        /// <summary>Retain a clip until group release, completing when Unity reports its audio data loaded.</summary>
        public void PrepareClip(AudioCategory category, AudioId id, string group, Action<bool> completed = null)
        {
            if (!CanPrepare(category, id, group))
            {
                AudioCallbacks.Invoke(completed, false);
                return;
            }

            engine.PrepareClip(Resolve(category, id), group, completed);
        }

        private bool CanPrepare(AudioCategory category, AudioId id, string group) => Ready && (int)category >= 0 && (int)category <= 2 && !string.IsNullOrWhiteSpace(group) && !string.IsNullOrWhiteSpace(id.Value);
        public void ReleaseGroup(string group)
        {
            if (!string.IsNullOrWhiteSpace(group))
                engine?.ReleaseGroup(group);
        }

        public void ReleaseUnusedClips() => engine?.ReleaseUnused();
        public bool TryGetCachedClip(AudioCategory category, AudioId id, out AudioClip clip)
        {
            clip = null;
            return Ready && engine.TryGetCached(Resolve(category, id), out clip);
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
            if ((int)channel < 0 || (int)channel >= 4)
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

        internal void ReportPlaybackFailure(AudioHandle handle, string reason, ResolvedAudioClip? address = null, bool allowAsync = true)
        {
            if (logPlaybackFailures && (Application.isEditor || Debug.isDebugBuild))
                (failureLog ??= new AudioFailureLog()).Report(this, handle, reason, address, allowAsync);
        }
    }
}
