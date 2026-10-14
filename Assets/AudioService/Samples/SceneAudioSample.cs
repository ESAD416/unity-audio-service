using UnityEngine;

namespace Controller.Audio.Samples
{
    /// <summary>
    /// Optional whole-scene lifecycle example: prepare one looping SFX, retain its
    /// handle, cancel pending work on exit, then release the scene's retention.
    /// Keep persistent music under a separate persistent owner.
    /// </summary>
    public sealed class SceneAudioSample : MonoBehaviour
    {
        [Tooltip("An SFX key in the configured provider, for example an ambient wind loop. Empty means no automatic playback.")]
        [SerializeField] private string loopingSfxKey;
        [Range(0, 1)] [SerializeField] private float volume = .3f;
        private AudioController controller;
        private AudioHandle ownedSound;
        private string group;
        private int version;
        private bool started;

        private void Start() { if (!started) PrepareAndPlay(); }
        private void OnEnable() { if (started) PrepareAndPlay(); }
        /// <summary>May also be called by a loading screen. Repeated calls replace this sample's own session.</summary>
        public void PrepareAndPlay()
        {
            started = true;
            ReleaseSceneAudio();
            if (!isActiveAndEnabled || string.IsNullOrWhiteSpace(loopingSfxKey)) return;
            controller = AudioController.Instance;
            if (controller == null || !controller.Ready)
            {
                Debug.LogWarning("[SceneAudioSample] Enable AudioCtrl.prefab before preparing scene audio.", this);
                return;
            }
            var owner = controller;
            string key = loopingSfxKey;
            float requestedVolume = volume;
            int request = ++version;
            group = "SceneAudioSample:" + GetEntityId().ToString();
            owner.PrepareClip(AudioCategory.Sfx, key, group, ready =>
            {
                // A late completion after disable, cancellation, or a new request
                // must never restart audio. Capture the key before asynchronous work.
                if (this == null || request != version || !isActiveAndEnabled || owner != controller) return;
                if (!ready)
                {
                    Debug.LogWarning($"[SceneAudioSample] Could not prepare SFX '{key}'. Check the provider/key, or retry after cancellation.", this);
                    return;
                }
                if (owner != AudioController.Instance || !owner.Ready) return;
                ownedSound = AudioService.PlaySfx(key, new PlayOptions { Loop = true, Volume = requestedVolume });
            });
        }
        /// <summary>
        /// Scene exit: stop only this sample's sound, cancel preparation and remove
        /// its group. The service clears ordinary playback cache on the next
        /// non-additive scene load; other groups and active playback stay valid.
        /// </summary>
        public void ReleaseSceneAudio()
        {
            version++;
            var owner = controller;
            var handle = ownedSound;
            string retainedGroup = group;
            controller = null;
            ownedSound = null;
            group = null;
            handle?.Stop();
            if (owner != null)
                owner.ReleaseGroup(retainedGroup);
        }
        private void OnDisable() => ReleaseSceneAudio();
    }
}
