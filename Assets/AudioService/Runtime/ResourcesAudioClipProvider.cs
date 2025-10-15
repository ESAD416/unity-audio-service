using UnityEngine;

namespace Controller.Audio
{
    public class ResourcesAudioClipProvider : MonoBehaviour, IAudioClipProvider
    {
        [SerializeField] private string rootPath = "Audio";
        [SerializeField] private string bgmPath = "BGM";
        [SerializeField] private string sfxPath = "SFX";
        [SerializeField] private string voicePath = "Voice";


        public AudioClip GetClip(AudioCategory category, string key)
        {
            TryGetClip(category, key, out var clip);
            return clip;
        }

        public bool TryGetClip(AudioCategory category, string key, out AudioClip clip)
        {
            clip = null;
            if (string.IsNullOrEmpty(key))
            {
                Debug.LogWarning("[ResourcesAudioClipProvider] Key is null or empty");
                return false;
            }

            var path = BuildPath(category, key);
            clip = Resources.Load<AudioClip>(path);
            if (clip == null)
            {
                Debug.LogWarning($"[ResourcesAudioClipProvider] AudioClip not found at path: {path}");
                return false;
            }

            return true;
        }

        private string BuildPath(AudioCategory category, string key)
        {
            string subFolder = category switch
            {
                AudioCategory.Bgm => bgmPath,
                AudioCategory.Sfx => sfxPath,
                AudioCategory.Voice => voicePath,
                _ => string.Empty
            };

            return string.IsNullOrEmpty(subFolder)
                ? $"{rootPath}/{key}"
                : $"{rootPath}/{subFolder}/{key}";
        }
    }
}
