using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Controller.Audio
{
    public class AddressablesAudioClipProvider : MonoBehaviour, IResultAudioClipProvider
    {
        // Only this record owns the handle. Waiters never inspect or release it.
        private sealed class LoadOperation
        {
            public AsyncOperationHandle<AudioClip> Handle;
            public AudioClip Result;
            public bool Completed;
            public bool Released;
        }

        private readonly Dictionary<string, LoadOperation> _operations = new();
        private AsyncOperationHandle<IResourceLocator> _initialization;
        private bool _destroyed;
        [SerializeField] private bool preloadOnAwake = true;

        private void Awake()
        {
            if (preloadOnAwake) StartCoroutine(PreloadAllAddressableAudio());
        }

        public AudioClip GetClip(AudioCategory category, string key)
        {
            TryGetClip(category, key, out var clip);
            return clip;
        }

        public bool TryGetClip(AudioCategory category, string key, out AudioClip clip)
        {
            clip = null;
            if (_destroyed || string.IsNullOrEmpty(key)) return false;
            if (!_operations.TryGetValue(key, out var operation)) return false;
            CompleteIfReady(key, operation);
            if (operation.Released || !operation.Completed) return false;
            clip = operation.Result;
            return clip != null;
        }

        public bool IsLoading(AudioCategory category, string key)
        {
            if (_destroyed || string.IsNullOrEmpty(key) || !_operations.TryGetValue(key, out var operation)) return false;
            CompleteIfReady(key, operation);
            return !operation.Completed && !operation.Released;
        }

        public bool IsCached(AudioCategory category, string key) => TryGetClip(category, key, out _);

        public IEnumerator LoadClipAsync(AudioCategory category, string key)
        {
            yield return LoadClipAsync(category, key, null);
        }

        public IEnumerator LoadClipAsync(AudioCategory category, string key, Action<AudioClip> completed)
        {
            if (_destroyed || string.IsNullOrEmpty(key))
            {
                completed?.Invoke(null);
                yield break;
            }

            if (!_operations.TryGetValue(key, out var operation))
            {
                operation = new LoadOperation { Handle = Addressables.LoadAssetAsync<AudioClip>(key) };
                _operations.Add(key, operation);
                var ownedOperation = operation;
                operation.Handle.Completed += _ => CompleteIfReady(key, ownedOperation);
            }

            // Poll the record, not an AsyncOperationHandle which another caller may release.
            while (!operation.Completed && !operation.Released)
            {
                CompleteIfReady(key, operation);
                if (!operation.Completed) yield return null;
            }
            completed?.Invoke(operation.Released ? null : operation.Result);
        }

        private void CompleteIfReady(string key, LoadOperation operation)
        {
            if (operation.Completed || operation.Released) return;
            if (!operation.Handle.IsValid())
            {
                ReleaseOperation(key, operation);
                return;
            }
            if (!operation.Handle.IsDone) return;
            operation.Completed = true;
            if (operation.Handle.Status == AsyncOperationStatus.Succeeded && operation.Handle.Result != null)
            {
                operation.Result = operation.Handle.Result;
            }
            else ReleaseOperation(key, operation);
        }

        private void ReleaseOperation(string key, LoadOperation operation)
        {
            if (operation.Released) return;
            operation.Released = true;
            operation.Completed = true;
            operation.Result = null;
            if (_operations.TryGetValue(key, out var current) && ReferenceEquals(current, operation))
                _operations.Remove(key);
            if (operation.Handle.IsValid()) Addressables.Release(operation.Handle);
        }

        public void ReleaseClip(AudioCategory category, string key)
        {
            if (!string.IsNullOrEmpty(key) && _operations.TryGetValue(key, out var operation))
                ReleaseOperation(key, operation);
        }

        private void OnDestroy()
        {
            _destroyed = true;
            foreach (var pair in new List<KeyValuePair<string, LoadOperation>>(_operations))
                ReleaseOperation(pair.Key, pair.Value);
            ReleaseInitialization();
        }

        private void ReleaseInitialization()
        {
            if (_initialization.IsValid()) Addressables.Release(_initialization);
            _initialization = default;
        }

        private IEnumerator PreloadAllAddressableAudio()
        {
            _initialization = Addressables.InitializeAsync(false);
            while (!_destroyed && _initialization.IsValid() && !_initialization.IsDone) yield return null;
            if (_destroyed || !_initialization.IsValid()) yield break;
            var succeeded = _initialization.Status == AsyncOperationStatus.Succeeded;
            ReleaseInitialization();
            if (!succeeded) yield break;

            foreach (var locator in Addressables.ResourceLocators)
            {
                foreach (var key in locator.Keys)
                {
                    if (!(key is string keyString) || IsCached(AudioCategory.Bgm, keyString)) continue;
                    if (!locator.Locate(key, typeof(AudioClip), out var locations) || locations == null) continue;
                    foreach (var location in locations)
                    {
                        if (!typeof(AudioClip).IsAssignableFrom(location.ResourceType)) continue;
                        yield return LoadClipAsync(AudioCategory.Bgm, keyString);
                        break;
                    }
                }
            }
        }
    }
}
