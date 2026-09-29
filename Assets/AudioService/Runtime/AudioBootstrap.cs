using UnityEngine;

namespace Controller.Audio
{
    [DisallowMultipleComponent]
    public class AudioBootstrap : MonoBehaviour
    {
        [SerializeField] private AudioController controllerOverride;
        [SerializeField] private MonoBehaviour clipProviderSource;
        [SerializeField] private bool registerProviderGlobally = true;
        [SerializeField] private MonoBehaviour settingsHandlerSource;
        [SerializeField] private bool applyStoredVolumesOnStart = true;
        [SerializeField] private bool broadcastStoredVolumesOnStart;
        private AudioController controller;
        private IAudioClipProvider clipProvider;
        private IAudioSettingsHandler settingsHandler;
        public AudioController Controller => controller;
        public IAudioClipProvider ClipProvider => clipProvider;
        public IAudioSettingsHandler SettingsHandler => settingsHandler;
        private void Awake() => Reconnect();
        private void OnEnable() => Reconnect();
        private void Start()
        {
            if (applyStoredVolumesOnStart && controller != null) controller.BindSettings(settingsHandler);
            if (broadcastStoredVolumesOnStart && settingsHandler is IAudioSettingsMaintenance maintenance) maintenance.BroadcastStoredVolumes();
        }
        public void Reconnect()
        {
            var parent = GetComponentInParent<AudioController>();
            if (parent != null && AudioController.Instance != null && parent != AudioController.Instance) return;
            controller = controllerOverride != null ? controllerOverride : AudioController.Instance;
            if (controller == null) controller = FindAnyObjectByType<AudioController>(FindObjectsInactive.Include);
            if (controller == null || !controller.isActiveAndEnabled) return;
            controller.Initialize();
            clipProvider = clipProviderSource as IAudioClipProvider;
            settingsHandler = settingsHandlerSource as IAudioSettingsHandler;
            if (AudioValues.Alive(clipProvider))
            {
                if (registerProviderGlobally) AudioController.RegisterClipProvider(clipProvider, this);
                else controller.SetClipProvider(clipProvider);
            }
            if (AudioValues.Alive(settingsHandler)) controller.BindSettings(settingsHandler, applyStoredVolumesOnStart);
        }
        private void OnDisable()
        {
            if (registerProviderGlobally) AudioController.UnregisterClipProvider(this);
            if (controller != null) controller.UnbindSettings(settingsHandler);
        }
        private void OnDestroy() => OnDisable();
        public void ApplyVolume(AudioChannel channel, float normalizedVolume, bool persist = true)
        {
            if (controller == null) return;
            controller.ApplyVolume(channel, normalizedVolume, persist);
        }
    }
}
