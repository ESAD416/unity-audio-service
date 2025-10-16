using UnityEngine;

namespace Controller.Audio
{
    /// <summary>
    /// Wires together <see cref="AudioController"/>, clip providers, and settings handlers so the module can run standalone.
    /// </summary>
    [DisallowMultipleComponent]
    public class AudioBootstrap : MonoBehaviour
    {
        [Header("Controller")]
        [SerializeField] private AudioController controllerOverride;

        [Header("Clip Provider")]
        [SerializeField] private MonoBehaviour clipProviderSource;
        [SerializeField] private bool registerProviderGlobally = true;

        [Header("Settings Handler")]
        [SerializeField] private MonoBehaviour settingsHandlerSource;
        [SerializeField] private bool applyStoredVolumesOnStart = true;
        [SerializeField] private bool broadcastStoredVolumesOnStart = false;

        private AudioController _controller;
        private IAudioClipProvider _clipProvider;
        private IAudioSettingsHandler _settingsHandler;
        private bool _handlerSubscribed;
        private bool _suppressHandlerCallback;

        public AudioController Controller => _controller;
        public IAudioClipProvider ClipProvider => _clipProvider;
        public IAudioSettingsHandler SettingsHandler => _settingsHandler;

        private void Awake()
        {
            if (!ResolveController())
            {
                enabled = false;
                return;
            }

            ResolveClipProvider();
            ResolveSettingsHandler();
        }

        private void OnEnable()
        {
            AttachSettingsHandler();
        }

        private void Start()
        {
            if (_settingsHandler == null || _controller == null) return;

            if (applyStoredVolumesOnStart)
            {
                ApplyStoredVolumesToController();
            }

            if (broadcastStoredVolumesOnStart)
            {
                BroadcastHandlerVolumes();
            }
        }

        private void OnDisable()
        {
            DetachSettingsHandler();
        }

        private void OnDestroy()
        {
            DetachSettingsHandler();
        }

        /// <summary>
        /// Applies the specified volume both to the controller and (optionally) back to the handler for persistence.
        /// </summary>
        public void ApplyVolume(AudioChannel channel, float normalizedVolume, bool persist = true)
        {
            var clamped = Mathf.Clamp01(normalizedVolume);
            ApplyVolumeToController(channel, clamped);

            if (persist && _settingsHandler != null)
            {
                _settingsHandler.UpdateVolume(channel, clamped);
            }
        }

        private bool ResolveController()
        {
            if (controllerOverride != null)
            {
                _controller = controllerOverride;
            }
            else
            {
                _controller = AudioController.Instance;
                if (_controller == null)
                {
                    _controller = FindFirstObjectByType<AudioController>(FindObjectsInactive.Include);
                }
            }

            if (_controller == null)
            {
                Debug.LogWarning("[AudioBootstrap] AudioController instance not found; bootstrap disabled.");
                return false;
            }

            return true;
        }

        private void ResolveClipProvider()
        {
            if (clipProviderSource == null) return;

            _clipProvider = clipProviderSource as IAudioClipProvider;
            if (_clipProvider == null)
            {
                Debug.LogWarning("[AudioBootstrap] Clip provider source does not implement IAudioClipProvider.");
                return;
            }

            if (_controller != null)
            {
                _controller.SetClipProvider(_clipProvider);
            }

            if (registerProviderGlobally)
            {
                AudioController.RegisterClipProvider(_clipProvider);
            }
        }

        private void ResolveSettingsHandler()
        {
            if (settingsHandlerSource == null) return;

            _settingsHandler = settingsHandlerSource as IAudioSettingsHandler;
            if (_settingsHandler == null)
            {
                Debug.LogWarning("[AudioBootstrap] Settings handler source does not implement IAudioSettingsHandler.");
            }
        }

        private void AttachSettingsHandler()
        {
            if (_settingsHandler == null || _handlerSubscribed) return;

            _settingsHandler.VolumeChanged += HandleVolumeChanged;
            _handlerSubscribed = true;
        }

        private void DetachSettingsHandler()
        {
            if (_settingsHandler == null || !_handlerSubscribed) return;

            _settingsHandler.VolumeChanged -= HandleVolumeChanged;
            _handlerSubscribed = false;
        }

        private void ApplyStoredVolumesToController()
        {
            _suppressHandlerCallback = true;
            ApplyVolumeToController(AudioChannel.Master, _settingsHandler.MasterVolume);
            ApplyVolumeToController(AudioChannel.Bgm, _settingsHandler.BgmVolume);
            ApplyVolumeToController(AudioChannel.Sfx, _settingsHandler.SfxVolume);
            ApplyVolumeToController(AudioChannel.Voice, _settingsHandler.VoiceVolume);
            _suppressHandlerCallback = false;
        }

        private void BroadcastHandlerVolumes()
        {
            if (_settingsHandler == null) return;

            HandleVolumeChanged(AudioChannel.Master, _settingsHandler.MasterVolume);
            HandleVolumeChanged(AudioChannel.Bgm, _settingsHandler.BgmVolume);
            HandleVolumeChanged(AudioChannel.Sfx, _settingsHandler.SfxVolume);
            HandleVolumeChanged(AudioChannel.Voice, _settingsHandler.VoiceVolume);
        }

        private void HandleVolumeChanged(AudioChannel channel, float normalizedVolume)
        {
            if (_suppressHandlerCallback) return;
            ApplyVolumeToController(channel, normalizedVolume);
        }

        private void ApplyVolumeToController(AudioChannel channel, float normalizedVolume)
        {
            if (_controller == null) return;

            var clamped = Mathf.Clamp01(normalizedVolume);
            switch (channel)
            {
                case AudioChannel.Master:
                    _controller.SetMasterVolume(clamped);
                    break;
                case AudioChannel.Bgm:
                    _controller.SetBgmVolume(clamped);
                    break;
                case AudioChannel.Sfx:
                    _controller.SetSfxVolume(clamped);
                    break;
                case AudioChannel.Voice:
                    _controller.SetVoiceVolume(clamped);
                    break;
                default:
                    Debug.LogWarning($"[AudioBootstrap] Unsupported audio channel {channel}");
                    break;
            }
        }
    }
}
