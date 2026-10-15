using UnityEngine;

namespace Controller.Audio.Samples
{
    /// <summary>
    /// 提供 ContextMenu 便於在編輯器測試 AudioController 的播放功能。
    /// </summary>
    public class AudioControllerDebugMenu : MonoBehaviour
    {
        [Header("Test Clip Keys")]
        [SerializeField] private string bgmKey;
        [SerializeField] private string bgmTransitionKey;
        [SerializeField] private string sfxKey;
        [SerializeField] private bool sfxKeyLoop;
        [SerializeField] private string voiceKey;
        [SerializeField] private bool voiceKeyLoop;
        [SerializeField] private bool keyAllowAsyncLoad = true;
        [Min(0)] [SerializeField] private int preparedSourceCapacity = 24;

        [Header("Test Volumes")]
        [Range(0f, 1f)] [SerializeField] private float masterVolume = 1f;
        [Range(0f, 1f)] [SerializeField] private float bgmVolume = 1f;
        [Range(0f, 1f)] [SerializeField] private float sfxVolume = 1f;
        [Range(0f, 1f)] [SerializeField] private float voiceVolume = 1f;

        [Header("Settings Handler")]
        [SerializeField] private MonoBehaviour settingsHandlerSource;

        private AudioController preparedController;
        private AudioHandle preparedBgm;
        private string preparedGroup;
        private int preparationVersion;

        [ContextMenu("Audio/Loading/Prepare and Play BGM")]
        private void ContextPrepareBgm()
        {
            if (!Application.isPlaying || !isActiveAndEnabled || !HasKey(bgmKey, "BGM")) return;
            ContextReleasePreparedBgm();
            var controller = AudioController.Instance;
            if (controller == null || !controller.Ready)
            {
                Debug.LogWarning("[AudioControllerDebugMenu] Enable AudioCtrl.prefab before preparing BGM.", this);
                return;
            }
            preparedController = controller;
            preparedGroup = "AudioDebugMenu:" + GetEntityId().ToString();
            string key = bgmKey;
            int version = ++preparationVersion;
            // A late completion after release, disable or a newer request must not play.
            bool IsCurrent() => this != null && version == preparationVersion && isActiveAndEnabled;
            controller.PrewarmSources(preparedSourceCapacity);
            controller.PrepareClip(AudioCategory.Bgm, key, preparedGroup, ready =>
            {
                if (!IsCurrent()) return;
                if (ready && controller != null && controller.Ready) preparedBgm = AudioService.PlayBgm(key);
                else Debug.LogWarning("[AudioControllerDebugMenu] BGM preparation did not complete. Retry after the service is ready.", this);
            });
        }

        [ContextMenu("Audio/Loading/Release Prepared BGM")]
        private void ContextReleasePreparedBgm()
        {
            preparationVersion++;
            var controller = preparedController;
            var handle = preparedBgm;
            var group = preparedGroup;
            preparedController = null;
            preparedBgm = null;
            preparedGroup = null;
            handle?.Stop();
            if (controller != null) controller.ReleaseGroup(group);
            // Ordinary playback also retains the cache until the next non-additive
            // scene load; additive flows may call ReleaseUnusedClips themselves.
        }

        private void OnDisable() => ContextReleasePreparedBgm();

        private void Awake()
        {
            ResolveSettingsHandler();
        }

        // Playback and controls go through AudioService, which reports a missing or
        // not-ready AudioCtrl itself; this menu only reports its own empty keys.
        private bool HasKey(string key, string label)
        {
            if (!string.IsNullOrWhiteSpace(key))
                return true;

            Debug.LogWarning($"[AudioControllerDebugMenu] {label} key is empty.", this);
            return false;
        }

        private bool TryGetSettingsHandler(out IAudioSettingsHandler handler)
        {
            handler = ResolveSettingsHandler();
            if (handler != null)
                return true;

            Debug.LogWarning("[AudioControllerDebugMenu] Assign Settings Handler Source on this component.");
            return false;
        }

        private IAudioSettingsHandler ResolveSettingsHandler()
        {
            if (settingsHandlerSource == null)
                return null;
            if (settingsHandlerSource is IAudioSettingsHandler handler)
                return handler;

            Debug.LogWarning("[AudioControllerDebugMenu] Settings handler source does not implement IAudioSettingsHandler.");
            return null;
        }

        [ContextMenu("Audio/Play/BGM (Key)")]
        private void ContextPlayBgmByKey()
        {
            if (HasKey(bgmKey, "BGM"))
                AudioService.PlayBgm(bgmKey, new PlayOptions { AllowAsyncLoad = keyAllowAsyncLoad });
        }
        [ContextMenu("Audio/Play/SFX (Key)")]
        private void ContextPlaySfxByKey()
        {
            if (HasKey(sfxKey, "SFX"))
                AudioService.PlaySfx(sfxKey, new PlayOptions { Loop = sfxKeyLoop, AllowAsyncLoad = keyAllowAsyncLoad });
        }
        [ContextMenu("Audio/Play/Voice (Key)")]
        private void ContextPlayVoiceByKey()
        {
            if (HasKey(voiceKey, "Voice"))
                AudioService.PlayVoice(voiceKey, new PlayOptions { Loop = voiceKeyLoop, AllowAsyncLoad = keyAllowAsyncLoad });
        }

        [ContextMenu("Audio/Stop/BGM")]
        private void ContextStopBgm() => AudioService.StopBgm();
        [ContextMenu("Audio/Stop/SFX")]
        private void ContextStopSfxLoop() => AudioService.StopSfx();
        [ContextMenu("Audio/Stop/Voice")]
        private void ContextStopVoice() => AudioService.StopVoice();

        [ContextMenu("Audio/Set Volume/Master")]
        private void ContextSetMasterVolume() => AudioService.SetMasterVolume(masterVolume);
        [ContextMenu("Audio/Set Volume/BGM")]
        private void ContextSetBgmVolume() => AudioService.SetBgmVolume(bgmVolume);
        [ContextMenu("Audio/Set Volume/SFX")]
        private void ContextSetSfxVolume() => AudioService.SetSfxVolume(sfxVolume);
        [ContextMenu("Audio/Set Volume/Voice")]
        private void ContextSetVoiceVolume() => AudioService.SetVoiceVolume(voiceVolume);

        [ContextMenu("Audio/Settings/Set Master (Handler)")]
        private void ContextSetMasterViaHandler()
        {
            if (!TryGetSettingsHandler(out var handler)) return;
            handler.UpdateVolume(AudioChannel.Master, masterVolume);
        }

        [ContextMenu("Audio/Settings/Set BGM (Handler)")]
        private void ContextSetBgmViaHandler()
        {
            if (!TryGetSettingsHandler(out var handler)) return;
            handler.UpdateVolume(AudioChannel.Bgm, bgmVolume);
        }

        [ContextMenu("Audio/Settings/Set SFX (Handler)")]
        private void ContextSetSfxViaHandler()
        {
            if (!TryGetSettingsHandler(out var handler)) return;
            handler.UpdateVolume(AudioChannel.Sfx, sfxVolume);
        }

        [ContextMenu("Audio/Settings/Set Voice (Handler)")]
        private void ContextSetVoiceViaHandler()
        {
            if (!TryGetSettingsHandler(out var handler)) return;
            handler.UpdateVolume(AudioChannel.Voice, voiceVolume);
        }

        [ContextMenu("Audio/Settings/Clear PlayerPrefs")]
        private void ContextClearPlayerPrefs()
        {
            if (!TryGetSettingsHandler(out var handler)) return;
            if (handler is IAudioSettingsMaintenance maintenance)
            {
                maintenance.ResetSettings();
                Debug.Log("[AudioControllerDebugMenu] Reset actual handler keys and in-memory volumes.");
            }
            else Debug.LogWarning("[AudioControllerDebugMenu] Settings handler does not support reset.");
        }

        [ContextMenu("Audio/Fade/BGM Fade Out 1s")]
        private void ContextFadeOutBgm() => AudioService.StopBgm(1f);

        [ContextMenu("Audio/Fade/BGM Fade In 1s")]
        private void ContextFadeInBgm()
        {
            if (HasKey(bgmKey, "BGM"))
                AudioService.PlayBgm(bgmKey, new PlayOptions { AllowAsyncLoad = keyAllowAsyncLoad, FadeInSeconds = 1 });
        }

        [ContextMenu("Audio/Fade/BGM Transition 1s/1s")]
        private void ContextTransitionBgm()
        {
            if (HasKey(bgmTransitionKey, "Transition BGM"))
                AudioService.PlayBgm(bgmTransitionKey, new PlayOptions { AllowAsyncLoad = keyAllowAsyncLoad, FadeInSeconds = 1, FadeOutSeconds = 1 });
        }

        [ContextMenu("Audio/Fade/SFX Loop Fade 1s")]
        private void ContextFadeSfxLoop()
        {
            if (HasKey(sfxKey, "SFX"))
                AudioService.PlaySfx(sfxKey, new PlayOptions { Loop = true, AllowAsyncLoad = keyAllowAsyncLoad, FadeInSeconds = 1 });
        }

        [ContextMenu("Audio/Fade/SFX Loop Fade Out 1s")]
        private void ContextFadeOutSfxLoop() => AudioService.StopSfx(1f);

        [ContextMenu("Audio/Fade/Voice Loop Fade 1s")]
        private void ContextFadeVoiceLoop()
        {
            if (HasKey(voiceKey, "Voice"))
                AudioService.PlayVoice(voiceKey, new PlayOptions { Loop = true, AllowAsyncLoad = keyAllowAsyncLoad, FadeInSeconds = 1 });
        }

        [ContextMenu("Audio/Fade/Voice Loop Fade Out 1s")]
        private void ContextFadeOutVoiceLoop() => AudioService.StopVoice(1f);
    }
}
