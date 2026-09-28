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

        [Header("Test Volumes")]
        [Range(0f, 1f)] [SerializeField] private float masterVolume = 1f;
        [Range(0f, 1f)] [SerializeField] private float bgmVolume = 1f;
        [Range(0f, 1f)] [SerializeField] private float sfxVolume = 1f;
        [Range(0f, 1f)] [SerializeField] private float voiceVolume = 1f;

        [Header("Settings Handler")]
        [SerializeField] private MonoBehaviour settingsHandlerSource;

        private IAudioSettingsHandler _settingsHandler;

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

            var bootstrap = FindAnyObjectByType<AudioBootstrap>(FindObjectsInactive.Include);
            if (bootstrap != null && bootstrap.SettingsHandler != null)
            {
                _settingsHandler = bootstrap.SettingsHandler;
                handler = _settingsHandler;
                return true;
            }

            handler = null;
            Debug.LogWarning("[AudioControllerDebugMenu] No settings handler available. Assign one on the component or ensure AudioBootstrap is active.");
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
            controller.PlayBgm(bgmKey, keyAllowAsyncLoad);
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
            controller.PlaySfx(sfxKey, sfxKeyLoop, keyAllowAsyncLoad);
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
            controller.PlayVoice(voiceKey, voiceKeyLoop, keyAllowAsyncLoad);
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
            if (_settingsHandler is AudioSettingsPlayerPrefs prefsHandler)
            {
                PlayerPrefs.DeleteKey("Audio.Master");
                PlayerPrefs.DeleteKey("Audio.Bgm");
                PlayerPrefs.DeleteKey("Audio.Sfx");
                PlayerPrefs.DeleteKey("Audio.Voice");
                Debug.Log("[AudioControllerDebugMenu] Cleared PlayerPrefs audio keys.");
                prefsHandler.BroadcastStoredVolumes();
                return;
            }

            PlayerPrefs.DeleteKey("Audio.Master");
            PlayerPrefs.DeleteKey("Audio.Bgm");
            PlayerPrefs.DeleteKey("Audio.Sfx");
            PlayerPrefs.DeleteKey("Audio.Voice");
            Debug.Log("[AudioControllerDebugMenu] Cleared PlayerPrefs audio keys (handler not cached).");
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
            controller.PlayBgm(bgmKey, keyAllowAsyncLoad, 0f, 1f);
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

            controller.PlayBgm(bgmTransitionKey, keyAllowAsyncLoad, 1f, 1f);
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
            controller.PlaySfx(sfxKey, true, keyAllowAsyncLoad, 1f);
        }

        [ContextMenu("Audio/Fade/SFX Loop Fade Out 1s")]
        private void ContextFadeOutSfxLoop()
        {
            if (!TryGetController(out var controller)) return;
            controller.StopSfx(stopLoopOnly: false, fadeOutSeconds: 1f);
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
            controller.PlayVoice(voiceKey, true, keyAllowAsyncLoad, 1f);
        }

        [ContextMenu("Audio/Fade/Voice Loop Fade Out 1s")]
        private void ContextFadeOutVoiceLoop()
        {
            if (!TryGetController(out var controller)) return;
            controller.StopVoice(stopLoopOnly: false, fadeOutSeconds: 1f);
        }
    }
}
