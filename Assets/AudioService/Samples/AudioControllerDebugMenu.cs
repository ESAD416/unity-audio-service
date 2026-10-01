using UnityEngine;

namespace Controller.Audio
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

        private IAudioSettingsHandler _settingsHandler;
        private AudioController preparedController;
        private AudioHandle preparedBgm;
        private string preparedGroup;
        private int preparationVersion;

        [ContextMenu("Audio/Loading/Prepare and Play BGM")]
        private void ContextPrepareBgm()
        {
            if (!Application.isPlaying || !isActiveAndEnabled || !TryGetController(out var controller) || string.IsNullOrWhiteSpace(bgmKey)) return;
            ContextReleasePreparedBgm();
            controller.Initialize();
            if (!controller.Ready) return;
            preparedController = controller;
            preparedGroup = "AudioDebugMenu:" + GetEntityId().ToString();
            string key = bgmKey;
            int version = ++preparationVersion;
            controller.PrewarmSources(preparedSourceCapacity);
            controller.PrepareClip(AudioCategory.Bgm, key, preparedGroup, ready =>
            {
                if (this == null || version != preparationVersion || !isActiveAndEnabled) return;
                if (ready && controller != null && controller.Ready) preparedBgm = AudioService.PlayBgm(key);
                else Debug.LogWarning("[AudioControllerDebugMenu] BGM preparation did not complete. Retry after the service is ready.", this);
            });
        }

        [ContextMenu("Audio/Loading/Release Prepared BGM")]
        private void ContextReleasePreparedBgm()
        {
            preparationVersion++;
            var controller = preparedController; var handle = preparedBgm; var group = preparedGroup;
            preparedController = null; preparedBgm = null; preparedGroup = null;
            handle?.Stop();
            if (controller != null) controller.ReleaseGroup(group);
            // Ordinary playback also retains the cache. The scene owner chooses
            // when to call ReleaseUnusedClips for the whole service.
        }

        private void OnDisable() => ContextReleasePreparedBgm();

        private void Awake()
        {
            ResolveSettingsHandler();
        }

        private bool TryGetController(out AudioController controller)
        {
            controller = AudioController.Instance;
            if (controller == null)
            {
                Debug.LogWarning("[AudioControllerDebugMenu] AudioController.Instance is null, ensure controller is present in the scene.");
                return false;
            }

            return true;
        }

        private bool TryGetSettingsHandler(out IAudioSettingsHandler handler)
        {
            if (_settingsHandler != null)
            {
                handler = _settingsHandler;
                return true;
            }

            if (settingsHandlerSource != null)
            {
                handler = settingsHandlerSource as IAudioSettingsHandler;
                if (handler != null)
                {
                    _settingsHandler = handler;
                    return true;
                }

                Debug.LogWarning("[AudioControllerDebugMenu] Settings handler source does not implement IAudioSettingsHandler.");
            }

            handler = null;
            Debug.LogWarning("[AudioControllerDebugMenu] Assign Settings Handler Source on this component.");
            return false;
        }

        private void ResolveSettingsHandler()
        {
            if (settingsHandlerSource != null)
            {
                _settingsHandler = settingsHandlerSource as IAudioSettingsHandler;
                if (_settingsHandler == null)
                {
                    Debug.LogWarning("[AudioControllerDebugMenu] Settings handler source does not implement IAudioSettingsHandler.");
                }
            }
        }

        [ContextMenu("Audio/Play/BGM (Key)")]
        private void ContextPlayBgmByKey()
        {
            if (!TryGetController(out var controller)) return;
            if (string.IsNullOrEmpty(bgmKey))
            {
                Debug.LogWarning("[AudioControllerDebugMenu] BGM key is empty");
                return;
            }
            AudioService.PlayBgm(bgmKey, new PlayOptions { AllowAsyncLoad = keyAllowAsyncLoad });
        }
        [ContextMenu("Audio/Play/SFX (Key)")]
        private void ContextPlaySfxByKey()
        {
            if (!TryGetController(out var controller)) return;
            if (string.IsNullOrEmpty(sfxKey))
            {
                Debug.LogWarning("[AudioControllerDebugMenu] SFX key is empty");
                return;
            }
            AudioService.PlaySfx(sfxKey, new PlayOptions { Loop = sfxKeyLoop, AllowAsyncLoad = keyAllowAsyncLoad });
        }
        [ContextMenu("Audio/Play/Voice (Key)")]
        private void ContextPlayVoiceByKey()
        {
            if (!TryGetController(out var controller)) return;
            if (string.IsNullOrEmpty(voiceKey))
            {
                Debug.LogWarning("[AudioControllerDebugMenu] Voice key is empty");
                return;
            }
            AudioService.PlayVoice(voiceKey, new PlayOptions { Loop = voiceKeyLoop, AllowAsyncLoad = keyAllowAsyncLoad });
        }

        [ContextMenu("Audio/Stop/BGM")]
        private void ContextStopBgm()
        {
            if (!TryGetController(out var controller)) return;
            controller.StopBgm();
        }
        [ContextMenu("Audio/Stop/SFX")]
        private void ContextStopSfxLoop()
        {
            if (!TryGetController(out var controller)) return;
            controller.StopSfx();
        }
        [ContextMenu("Audio/Stop/Voice")]
        private void ContextStopVoice()
        {
            if (!TryGetController(out var controller)) return;
            controller.StopVoice();
        }

        [ContextMenu("Audio/Set Volume/Master")]
        private void ContextSetMasterVolume()
        {
            if (!TryGetController(out var controller)) return;
            controller.SetMasterVolume(masterVolume);
        }
        [ContextMenu("Audio/Set Volume/BGM")]
        private void ContextSetBgmVolume()
        {
            if (!TryGetController(out var controller)) return;
            controller.SetBgmVolume(bgmVolume);
        }
        [ContextMenu("Audio/Set Volume/SFX")]
        private void ContextSetSfxVolume()
        {
            if (!TryGetController(out var controller)) return;
            controller.SetSfxVolume(sfxVolume);
        }
        [ContextMenu("Audio/Set Volume/Voice")]
        private void ContextSetVoiceVolume()
        {
            if (!TryGetController(out var controller)) return;
            controller.SetVoiceVolume(voiceVolume);
        }

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
        private void ContextFadeOutBgm()
        {
            if (!TryGetController(out var controller)) return;
            controller.StopBgm(1f);
        }

        [ContextMenu("Audio/Fade/BGM Fade In 1s")]
        private void ContextFadeInBgm()
        {
            if (!TryGetController(out var controller)) return;
            if (string.IsNullOrEmpty(bgmKey))
            {
                Debug.LogWarning("[AudioControllerDebugMenu] BGM key is empty for fade in test");
                return;
            }
            AudioService.PlayBgm(bgmKey, new PlayOptions { AllowAsyncLoad = keyAllowAsyncLoad, FadeInSeconds = 1 });
        }

        [ContextMenu("Audio/Fade/BGM Transition 1s/1s")]
        private void ContextTransitionBgm()
        {
            if (!TryGetController(out var controller)) return;
            if (string.IsNullOrEmpty(bgmTransitionKey))
            {
                Debug.LogWarning("[AudioControllerDebugMenu] Transition BGM key is empty");
                return;
            }

            AudioService.PlayBgm(bgmTransitionKey, new PlayOptions { AllowAsyncLoad = keyAllowAsyncLoad, FadeInSeconds = 1, FadeOutSeconds = 1 });
        }

        [ContextMenu("Audio/Fade/SFX Loop Fade 1s")]
        private void ContextFadeSfxLoop()
        {
            if (!TryGetController(out var controller)) return;
            if (string.IsNullOrEmpty(sfxKey))
            {
                Debug.LogWarning("[AudioControllerDebugMenu] SFX key is empty for fade test");
                return;
            }
            AudioService.PlaySfx(sfxKey, new PlayOptions { Loop = true, AllowAsyncLoad = keyAllowAsyncLoad, FadeInSeconds = 1 });
        }

        [ContextMenu("Audio/Fade/SFX Loop Fade Out 1s")]
        private void ContextFadeOutSfxLoop()
        {
            if (!TryGetController(out var controller)) return;
            controller.StopSfx(fadeOutSeconds: 1f);
        }

        [ContextMenu("Audio/Fade/Voice Loop Fade 1s")]
        private void ContextFadeVoiceLoop()
        {
            if (!TryGetController(out var controller)) return;
            if (string.IsNullOrEmpty(voiceKey))
            {
                Debug.LogWarning("[AudioControllerDebugMenu] Voice key is empty for fade test");
                return;
            }
            AudioService.PlayVoice(voiceKey, new PlayOptions { Loop = true, AllowAsyncLoad = keyAllowAsyncLoad, FadeInSeconds = 1 });
        }

        [ContextMenu("Audio/Fade/Voice Loop Fade Out 1s")]
        private void ContextFadeOutVoiceLoop()
        {
            if (!TryGetController(out var controller)) return;
            controller.StopVoice(fadeOutSeconds: 1f);
        }
    }
}
