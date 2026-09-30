using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

namespace Controller.Audio
{
    public class AddressablesAudioClipProvider : MonoBehaviour, IResultAudioClipProvider, IAudioClipLeaseProvider, IAudioSynchronousClipProvider, IAudioClipProviderRefresh
    {
        private sealed class Operation
        {
            public string Identity;
            public AsyncOperationHandle<AudioClip> Handle;
            public AudioClip Clip;
            public bool Complete, Released;
            public int Users, Deliveries;
            public readonly HashSet<string> CachedAliases = new();
            public readonly List<Request> Waiters = new();
        }
        private sealed class Request
        {
            public string Key;
            public bool Retain, Finished;
            public int Generation;
            public Action<AudioClipLease> Callback;
            public AsyncOperationHandle<IList<IResourceLocation>> Locations;
            public Operation Operation;
        }
        private readonly Dictionary<string, Operation> operations = new();
        private readonly Dictionary<string, Operation> aliases = new();
        private readonly List<Request> requests = new();
        private readonly Dictionary<string, IResourceLocation> knownLocations = new();
        private readonly AudioCallbackQueue callbacks = new();
        private int generation, lookupMutationDepth;
        [SerializeField] private bool preloadOnAwake;
        private bool destroyed;
        private AsyncOperationHandle<IResourceLocator> initialization;
        public int CachedOperationCount => operations.Count;
        private void Awake() { if (preloadOnAwake) StartCoroutine(PreloadAllAddressableAudio()); }
        public AudioClip GetClip(AudioCategory category, string key) { TryGetClip(category, key, out var clip); return clip; }
        public bool TryGetClip(AudioCategory category, string key, out AudioClip clip) => TryGetCachedClip(category, key, out clip);
        public bool TryGetCachedClip(AudioCategory category, string key, out AudioClip clip)
        {
            clip = null;
            if (!destroyed && !string.IsNullOrWhiteSpace(key) && aliases.TryGetValue(key, out var op) && op.Complete && !op.Released) clip = op.Clip;
            return clip != null;
        }
        public bool IsCached(AudioCategory category, string key) => TryGetCachedClip(category, key, out _);
        public bool IsLoading(AudioCategory category, string key) => requests.Exists(r => !r.Finished && r.Key == key);
        public void AcquireClip(AudioClipAddress address, Action<AudioClipLease> completed) => Acquire(address.AddressablesKey ?? address.Id, false, completed);
        public bool TryAcquireClip(AudioClipAddress address, out AudioClipLease lease)
        {
            lease = null; string key = address.AddressablesKey ?? address.Id;
            if (!TryGetCachedClip(address.Category, key, out var clip)) return false;
            var op = aliases[key]; op.Users++;
            lease = new AudioClipLease(clip, () => { op.Users--; ReleaseIfUnused(op); });
            return true;
        }
        private void Acquire(string key, bool retain, Action<AudioClipLease> completed)
        {
            using var mutation = callbacks.Begin();
            if (destroyed || string.IsNullOrWhiteSpace(key)) { AudioCallbacks.Deliver(completed, null); return; }
            var request = new Request { Key = key, Retain = retain, Callback = completed, Generation = generation }; requests.Add(request);
            if (aliases.TryGetValue(key, out var cached) && !cached.Released)
            { Join(request, cached); return; }
            if (knownLocations.TryGetValue(key, out var known)) { Resolve(request, known); return; }
            request.Locations = Addressables.LoadResourceLocationsAsync(key, typeof(AudioClip));
            request.Locations.Completed += handle =>
            {
                using var delivery = callbacks.Begin();
                IResourceLocation location = null;
                if (!request.Finished && !destroyed && request.Generation == generation && handle.IsValid() && handle.Status == AsyncOperationStatus.Succeeded && handle.Result.Count > 0) location = handle.Result[0];
                ReleaseLocations(request);
                if (request.Finished) return;
                if (location == null) { FinishRequest(request, null); return; }
                knownLocations[key] = location;
                Resolve(request, location);
            };
        }
        private void Resolve(Request request, IResourceLocation location)
        {
            // Address and GUID aliases resolve to the same location and asset handle.
            var identity = location.ProviderId + "\n" + location.InternalId + "\n" + location.ResourceType.FullName;
            if (!operations.TryGetValue(identity, out var op) || op.Released)
            {
                op = new Operation { Identity = identity, Handle = Addressables.LoadAssetAsync<AudioClip>(location) };
                operations[identity] = op;
                var owned = op; op.Handle.Completed += _ => Complete(owned);
            }
            aliases[request.Key] = op; Join(request, op);
        }
        private void Join(Request request, Operation op)
        {
            request.Operation = op;
            if (op.Complete) FinishRequest(request, op);
            else { op.Waiters.Add(request); if (op.Handle.IsValid() && op.Handle.IsDone) Complete(op); }
        }
        private void Complete(Operation op)
        {
            using var mutation = callbacks.Begin();
            if (op.Complete || op.Released) return;
            op.Complete = true;
            if (op.Handle.IsValid() && op.Handle.Status == AsyncOperationStatus.Succeeded) op.Clip = op.Handle.Result;
            var waiting = op.Waiters.ToArray(); op.Waiters.Clear();
            // A recipient may release its lease before later recipients are notified.
            // Keep the native operation alive for the entire delivery batch.
            op.Deliveries++;
            try { foreach (var request in waiting) FinishRequest(request, op); }
            finally { op.Deliveries--; ReleaseIfUnused(op); }
        }
        private void FinishRequest(Request request, Operation op)
        {
            if (request.Finished) return;
            request.Finished = true; requests.Remove(request);
            AudioClipLease lease = null;
            if (!destroyed && request.Generation == generation && op != null && !op.Released && op.Clip != null)
            {
                if (request.Retain) op.CachedAliases.Add(request.Key);
                op.Users++;
                lease = new AudioClipLease(op.Clip, () => { op.Users--; ReleaseIfUnused(op); });
            }
            var callback = request.Callback; request.Callback = null;
            // Ordinary delivery remains ordered and cancellable: an earlier
            // recipient may cancel a later waiter in this same native batch.
            if (lookupMutationDepth > 0) callbacks.Enqueue(() => AudioCallbacks.Deliver(callback, lease));
            else AudioCallbacks.Deliver(callback, lease);
        }
        private void ReleaseIfUnused(Operation op)
        {
            if (op.Released || op.Deliveries > 0 || op.Users > 0 || op.CachedAliases.Count > 0 || op.Waiters.Exists(r => !r.Finished)) return;
            op.Released = true; op.Clip = null;
            if (operations.TryGetValue(op.Identity, out var current) && ReferenceEquals(current, op)) operations.Remove(op.Identity);
            var keys = new List<string>(); foreach (var pair in aliases) if (ReferenceEquals(pair.Value, op)) keys.Add(pair.Key);
            foreach (var key in keys) aliases.Remove(key);
            if (op.Handle.IsValid())
            {
                try { Addressables.Release(op.Handle); }
                catch (Exception error) { Debug.LogException(error); }
            }
        }
        public IEnumerator LoadClipAsync(AudioCategory category, string key) { yield return LoadClipAsync(category, key, null); }
        public IEnumerator LoadClipAsync(AudioCategory category, string key, Action<AudioClip> completed)
        {
            bool done = false; AudioClip result = null;
            Acquire(key, true, lease => { result = lease?.Clip; lease?.Dispose(); done = true; });
            while (!done) yield return null;
            completed?.Invoke(result);
        }
        public void ReleaseClip(AudioCategory category, string key)
        {
            using var mutation = callbacks.Begin();
            foreach (var request in requests.ToArray()) if (request.Key == key)
                CancelRequest(request);
            if (key != null && aliases.TryGetValue(key, out var op)) { op.CachedAliases.Remove(key); ReleaseIfUnused(op); }
        }
        private static void ReleaseLocations(Request request)
        {
            var handle = request.Locations; request.Locations = default;
            if (handle.IsValid()) Addressables.Release(handle);
        }
        private void CancelRequest(Request request)
        {
            FinishRequest(request, null); ReleaseLocations(request);
            if (request.Operation != null) ReleaseIfUnused(request.Operation);
        }
        public void RefreshClipLookup()
        {
            using var mutation = callbacks.Begin();
            if (destroyed) return;
            lookupMutationDepth++;
            try
            {
                generation++;
                var oldRequests = requests.ToArray();
                var oldOperations = new List<Operation>(operations.Values);
                // Detach the generation before cancellation or native release can
                // reenter acquisition. Old users own their operations via leases.
                knownLocations.Clear(); aliases.Clear(); operations.Clear();
                foreach (var op in oldOperations) op.CachedAliases.Clear();
                foreach (var request in oldRequests) CancelRequest(request);
                foreach (var op in oldOperations) ReleaseIfUnused(op);
            }
            finally { lookupMutationDepth--; }
        }
        private void OnDestroy()
        {
            using var mutation = callbacks.Begin();
            destroyed = true;
            if (initialization.IsValid()) Addressables.Release(initialization);
            initialization = default;
            foreach (var request in requests.ToArray()) ReleaseClip(AudioCategory.Bgm, request.Key);
            foreach (var op in new List<Operation>(operations.Values)) { op.CachedAliases.Clear(); ReleaseIfUnused(op); }
            knownLocations.Clear(); aliases.Clear(); operations.Clear();
            // Outstanding leases deliberately retain the native handle after this component is destroyed.
        }
        private IEnumerator PreloadAllAddressableAudio()
        {
            int version = generation;
            initialization = Addressables.InitializeAsync(false);
            while (!destroyed && initialization.IsValid() && !initialization.IsDone) yield return null;
            bool success = initialization.IsValid() && initialization.Status == AsyncOperationStatus.Succeeded;
            if (initialization.IsValid()) Addressables.Release(initialization);
            initialization = default;
            if (!success || destroyed || version != generation) yield break;
            var keys = new HashSet<string>();
            foreach (var locator in Addressables.ResourceLocators)
                foreach (var key in locator.Keys)
                    if (key is string text && locator.Locate(key, typeof(AudioClip), out _)) keys.Add(text);
            foreach (var key in keys)
            {
                if (destroyed || version != generation) yield break;
                yield return LoadClipAsync(AudioCategory.Bgm, key);
            }
        }
    }
}
