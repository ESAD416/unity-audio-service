using UnityEngine;

namespace Controller.Audio
{
    public class FallbackAudioClipProvider : MonoBehaviour, IResultAudioClipProvider
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
            if (IsAlive(_main) && _main.TryGetClip(category, key, out clip) && clip != null)
            {
                return true;
            }

            if (IsAlive(_backup) && _backup.TryGetClip(category, key, out clip) && clip != null)
            {
                return true;
            }

            return false;
        }

        public bool IsLoading(AudioCategory category, string key)
        {
            if (IsAlive(_mainAsync) && _mainAsync.IsLoading(category, key)) return true;
            if (IsAlive(_backupAsync) && _backupAsync.IsLoading(category, key)) return true;
            return false;
        }

        public bool IsCached(AudioCategory category, string key)
        {
            if (IsAlive(_mainAsync) && _mainAsync.IsCached(category, key)) return true;
            if (IsAlive(_backupAsync) && _backupAsync.IsCached(category, key)) return true;
            return false;
        }

        private static bool IsAlive(IAudioClipProvider provider) =>
            provider != null && (!(provider is Object unityObject) || unityObject != null);

        public System.Collections.IEnumerator LoadClipAsync(AudioCategory category, string key)
        {
            yield return LoadClipAsync(category, key, null);
        }

        public System.Collections.IEnumerator LoadClipAsync(AudioCategory category, string key, System.Action<AudioClip> completed)
        {
            AudioClip result = null;
            if (IsAlive(_mainAsync))
                yield return LoadResult(_mainAsync, category, key, clip => result = clip);
            if (this == null) { completed?.Invoke(null); yield break; }
            if (result == null && IsAlive(_backupAsync))
                yield return LoadResult(_backupAsync, category, key, clip => result = clip);
            completed?.Invoke(this != null ? result : null);
        }

        private static System.Collections.IEnumerator LoadResult(IAsyncAudioClipProvider provider,
            AudioCategory category, string key, System.Action<AudioClip> completed)
        {
            if (provider is IResultAudioClipProvider resultProvider)
            {
                yield return resultProvider.LoadClipAsync(category, key, completed);
            }
            else
            {
                yield return provider.LoadClipAsync(category, key);
                AudioClip clip = null;
                if (IsAlive(provider)) provider.TryGetClip(category, key, out clip);
                completed(clip);
            }
        }

        public void ReleaseClip(AudioCategory category, string key)
        {
            if (IsAlive(_mainAsync)) _mainAsync.ReleaseClip(category, key);
            if (IsAlive(_backupAsync)) _backupAsync.ReleaseClip(category, key);
        }
    }
}
