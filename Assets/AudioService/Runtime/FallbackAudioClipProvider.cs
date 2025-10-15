using UnityEngine;

namespace Controller.Audio
{
    public class FallbackAudioClipProvider : MonoBehaviour, IAsyncAudioClipProvider
    {
        [SerializeField] private MonoBehaviour mainProvider;
        [SerializeField] private MonoBehaviour backupProvider;

        private IAudioClipProvider _main;
        private IAudioClipProvider _backup;
        private IAsyncAudioClipProvider _mainAsync;
        private IAsyncAudioClipProvider _backupAsync;

        private void Awake()
        {
            if (mainProvider == null)
            {
                mainProvider = EnsureChildProvider<AddressablesAudioClipProvider>("AddressablesProvider");
            }
            if (backupProvider == null)
            {
                backupProvider = EnsureChildProvider<ResourcesAudioClipProvider>("ResourcesProvider");
            }

            _main = mainProvider as IAudioClipProvider;
            _backup = backupProvider as IAudioClipProvider;
            _mainAsync = mainProvider as IAsyncAudioClipProvider;
            _backupAsync = backupProvider as IAsyncAudioClipProvider;

            if (mainProvider != null && _main == null)
            {
                Debug.LogWarning("[FallbackAudioClipProvider] Main Provider does not implement IAudioClipProvider");
            }
            if (backupProvider != null && _backup == null)
            {
                Debug.LogWarning("[FallbackAudioClipProvider] Backup Provider does not implement IAudioClipProvider");
            }
            if (mainProvider != null && _mainAsync == null)
            {
                Debug.Log("[FallbackAudioClipProvider] Main Provider does not implement IAsyncAudioClipProvider");
            }
            if (backupProvider != null && _backupAsync == null)
            {
                Debug.Log("[FallbackAudioClipProvider] Backup Provider does not implement IAsyncAudioClipProvider");
            }
        }

        private T EnsureChildProvider<T>(string childName) where T : MonoBehaviour
        {
            T provider = null;
            var child = transform.Find(childName);
            if (child != null)
            {
                provider = child.GetComponent<T>();
                if (provider != null) return provider;
            }

            var go = child != null ? child.gameObject : new GameObject(childName);
            go.transform.SetParent(transform);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            provider = go.GetComponent<T>();
            if (provider == null)
            {
                provider = go.AddComponent<T>();
            }
            return provider;
        }

        public AudioClip GetClip(AudioCategory category, string key)
        {
            TryGetClip(category, key, out var clip);
            return clip;
        }

        public bool TryGetClip(AudioCategory category, string key, out AudioClip clip)
        {
            clip = null;
            if (_main != null && _main.TryGetClip(category, key, out clip) && clip != null)
            {
                return true;
            }

            if (_backup != null && _backup.TryGetClip(category, key, out clip) && clip != null)
            {
                return true;
            }

            return false;
        }

        public bool IsLoading(AudioCategory category, string key)
        {
            if (_mainAsync != null && _mainAsync.IsLoading(category, key)) return true;
            if (_backupAsync != null && _backupAsync.IsLoading(category, key)) return true;
            return false;
        }

        public bool IsCached(AudioCategory category, string key)
        {
            if (_mainAsync != null && _mainAsync.IsCached(category, key)) return true;
            if (_backupAsync != null && _backupAsync.IsCached(category, key)) return true;
            return false;
        }

        public System.Collections.IEnumerator LoadClipAsync(AudioCategory category, string key)
        {
            if (_mainAsync != null)
            {
                yield return _mainAsync.LoadClipAsync(category, key);
                if (_main != null && _main.TryGetClip(category, key, out var clip) && clip != null)
                {
                    yield break;
                }
            }

            if (_backupAsync != null)
            {
                yield return _backupAsync.LoadClipAsync(category, key);
                if (_backup != null && _backup.TryGetClip(category, key, out var clip) && clip != null)
                {
                    yield break;
                }
            }
        }

        public void ReleaseClip(AudioCategory category, string key)
        {
            _mainAsync?.ReleaseClip(category, key);
            _backupAsync?.ReleaseClip(category, key);
        }
    }
}
