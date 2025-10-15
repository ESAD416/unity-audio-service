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
    }
}
