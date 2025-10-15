using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Controller.Audio
{
    public class AddressablesAudioClipProvider : MonoBehaviour, IAsyncAudioClipProvider
    {
        private readonly Dictionary<string, AudioClip> _cache = new();
        private readonly Dictionary<string, AsyncOperationHandle<AudioClip>> _handles = new();

        [SerializeField] private bool preloadOnAwake = true;
        private void Awake()
        {
            if (!preloadOnAwake) return;

            StartCoroutine(PreloadAllAddressableAudio());
        }

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
                Debug.LogWarning("[AddressablesAudioClipProvider] Key is null or empty");
                return false;
            }

            if (_cache.TryGetValue(key, out clip) && clip != null)
            {
                return true;
            }

            var handle = Addressables.LoadAssetAsync<AudioClip>(key);
            if (!handle.IsDone)
            {
                handle.WaitForCompletion();
            }

            if (handle.Status == AsyncOperationStatus.Succeeded)
            {
                clip = handle.Result;
                _cache[key] = clip;
                if (!_handles.ContainsKey(key))
                {
                    _handles[key] = handle;
                }
                return true;
            }

            Debug.LogWarning($"[AddressablesAudioClipProvider] Failed to load clip for key: {key}");
            if (handle.IsValid())
            {
                Addressables.Release(handle);
            }
            return false;
        }

        public bool IsLoading(AudioCategory category, string key)
        {
            return _handles.TryGetValue(key, out var handle) && !handle.IsDone;
        }

        public bool IsCached(AudioCategory category, string key)
        {
            return _cache.ContainsKey(key);
        }

        public IEnumerator LoadClipAsync(AudioCategory category, string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                yield break;
            }

            if (_cache.TryGetValue(key, out var cached) && cached != null)
            {
                yield break;
            }

            if (_handles.TryGetValue(key, out var existingHandle))
            {
                if (!existingHandle.IsDone)
                {
                    yield return existingHandle;
                }

                if (existingHandle.Status == AsyncOperationStatus.Succeeded && existingHandle.Result != null)
                {
                    _cache[key] = existingHandle.Result;
                }
                yield break;
            }

            var handle = Addressables.LoadAssetAsync<AudioClip>(key);
            _handles[key] = handle;
            yield return handle;

            if (handle.Status == AsyncOperationStatus.Succeeded)
            {
                _cache[key] = handle.Result;
            }
            else
            {
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }
                _handles.Remove(key);
            }
        }

        public void ReleaseClip(AudioCategory category, string key)
        {
            if (_handles.TryGetValue(key, out var handle))
            {
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }
                _handles.Remove(key);
            }
            _cache.Remove(key);
        }

        private void OnDestroy()
        {
            foreach (var kv in _handles)
            {
                if (kv.Value.IsValid())
                {
                    Addressables.Release(kv.Value);
                }
            }
            _handles.Clear();
            _cache.Clear();
        }

        private IEnumerator PreloadAllAddressableAudio()
        {
            var initHandle = Addressables.InitializeAsync();
            if (!initHandle.IsDone)
            {
                yield return initHandle;
            }

            foreach (var locator in Addressables.ResourceLocators)
            {
                foreach (var key in locator.Keys)
                {
                    if (!(key is string keyString) || _cache.ContainsKey(keyString)) continue;

                    if (!locator.Locate(key, typeof(AudioClip), out var locations) || locations == null) continue;

                    var hasAudio = false;
                    foreach (var location in locations)
                    {
                        if (typeof(AudioClip).IsAssignableFrom(location.ResourceType))
                        {
                            hasAudio = true;
                            break;
                        }
                    }

                    if (!hasAudio) continue;

                    yield return LoadClipAsync(AudioCategory.Bgm, keyString);
                }
            }
        }
    }
}
