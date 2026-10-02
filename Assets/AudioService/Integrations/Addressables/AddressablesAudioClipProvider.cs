using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

namespace Controller.Audio
{
    public class AddressablesAudioClipProvider : MonoBehaviour, IAudioClipProvider, IAudioClipProviderRefresh, IAudioClipProviderDiagnostics
    {
        private sealed class Operation
        {
            public string Identity;
            public AsyncOperationHandle<AudioClip> Handle;
            public AudioClip Clip;
            public bool Complete, Released;
            public int Users, Deliveries;
            // Most operations have one key; only multiple aliases need a collection.
            public string LookupKey;
            public List<string> OtherLookupKeys;
            public readonly List<Request> Waiters = new();
        }

        private sealed class Request
        {
            public string Key;
            public bool Finished;
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
        private bool destroyed;
        public int CachedOperationCount => operations.Count;

        public bool TryGetCachedClip(ResolvedAudioClip address, out AudioClip clip)
        {
            clip = TryGetCompletedOperation(address.AddressablesKey, out var op) ? op.Clip : null;
            return clip != null;
        }

        private bool TryGetCompletedOperation(string key, out Operation op)
        {
            op = null;
            return !destroyed && !string.IsNullOrWhiteSpace(key) && aliases.TryGetValue(key, out op)
                && op.Complete && !op.Released && op.Clip != null;
        }

        public void AcquireClip(ResolvedAudioClip address, Action<AudioClipLease> completed) => Acquire(address.AddressablesKey, completed);
        public bool TryAcquireClip(ResolvedAudioClip address, out AudioClipLease lease)
        {
            lease = TryGetCompletedOperation(address.AddressablesKey, out var op) ? CreateLease(op) : null;
            return lease != null;
        }

        private AudioClipLease CreateLease(Operation op)
        {
            op.Users++;
            return new AudioClipLease(op.Clip, () =>
            {
                op.Users--;
                ReleaseIfUnused(op);
            });
        }

        private void Acquire(string key, Action<AudioClipLease> completed)
        {
            using var mutation = callbacks.Begin();
            if (destroyed || string.IsNullOrWhiteSpace(key))
            {
                AudioCallbacks.Deliver(completed, null);
                return;
            }

            if (TryGetCompletedOperation(key, out var completedOperation))
            {
                DeliverLease(completed, CreateLease(completedOperation));
                return;
            }

            var request = new Request
            {
                Key = key,
                Callback = completed,
                Generation = generation
            };
            requests.Add(request);
            if (aliases.TryGetValue(key, out var cached) && !cached.Released)
            {
                Join(request, cached);
                return;
            }

            if (knownLocations.TryGetValue(key, out var known))
            {
                Resolve(request, known);
                return;
            }

            LoadLocation(request);
        }

        // Keep the asynchronous closure off completed-cache and known-location paths.
        private void LoadLocation(Request request)
        {
            request.Locations = Addressables.LoadResourceLocationsAsync(request.Key, typeof(AudioClip));
            request.Locations.Completed += handle =>
            {
                using var delivery = callbacks.Begin();
                IResourceLocation location = null;
                if (!request.Finished && !destroyed && request.Generation == generation && handle.IsValid() && handle.Status == AsyncOperationStatus.Succeeded && handle.Result.Count > 0)
                    location = handle.Result[0];
                ReleaseLocations(request);
                if (request.Finished)
                    return;
                if (location == null)
                {
                    FinishRequest(request, null);
                    return;
                }

                knownLocations[request.Key] = location;
                Resolve(request, location);
            };
        }

        private void Resolve(Request request, IResourceLocation location)
        {
            // Address and GUID aliases resolve to the same location and asset handle.
            var identity = location.ProviderId + "\n" + location.InternalId + "\n" + location.ResourceType.FullName;
            if (!operations.TryGetValue(identity, out var op) || op.Released)
            {
                op = new Operation
                {
                    Identity = identity,
                    Handle = Addressables.LoadAssetAsync<AudioClip>(location)
                };
                operations[identity] = op;
                var owned = op;
                op.Handle.Completed += _ => Complete(owned);
            }

            BindAlias(request.Key, op);
            Join(request, op);
        }

        private void BindAlias(string key, Operation op)
        {
            if (aliases.TryGetValue(key, out var previous))
            {
                if (ReferenceEquals(previous, op))
                    return;
                if (previous.LookupKey == key)
                    previous.LookupKey = null;
                else
                    previous.OtherLookupKeys?.Remove(key);
            }

            aliases[key] = op;
            if (op.LookupKey == null)
                op.LookupKey = key;
            // The forward map already excludes duplicate bindings; an extra
            // hash index would add memory without helping the release scan.
            else
                (op.OtherLookupKeys ??= new List<string>()).Add(key);
        }

        private void RemoveAliases(Operation op)
        {
            RemoveAlias(op.LookupKey, op);
            if (op.OtherLookupKeys != null)
                foreach (var key in op.OtherLookupKeys)
                    RemoveAlias(key, op);
            ClearLookupKeys(op);
        }

        private void RemoveAlias(string key, Operation op)
        {
            // An old leased operation must not remove a freshly rebound key.
            if (key != null && aliases.TryGetValue(key, out var current) && ReferenceEquals(current, op))
                aliases.Remove(key);
        }

        private static void ClearLookupKeys(Operation op)
        {
            op.LookupKey = null;
            op.OtherLookupKeys = null;
        }

        private void Join(Request request, Operation op)
        {
            request.Operation = op;
            if (op.Complete)
                FinishRequest(request, op);
            else
            {
                op.Waiters.Add(request);
                if (op.Handle.IsValid() && op.Handle.IsDone)
                    Complete(op);
            }
        }

        private void Complete(Operation op)
        {
            using var mutation = callbacks.Begin();
            if (op.Complete || op.Released)
                return;
            op.Complete = true;
            if (op.Handle.IsValid() && op.Handle.Status == AsyncOperationStatus.Succeeded)
                op.Clip = op.Handle.Result;
            var waiting = op.Waiters.ToArray();
            op.Waiters.Clear();
            // A recipient may release its lease before later recipients are notified.
            // Keep the native operation alive for the entire delivery batch.
            op.Deliveries++;
            try
            {
                foreach (var request in waiting)
                    FinishRequest(request, op);
            }
            finally
            {
                op.Deliveries--;
                ReleaseIfUnused(op);
            }
        }

        private void FinishRequest(Request request, Operation op)
        {
            if (request.Finished)
                return;
            request.Finished = true;
            requests.Remove(request);
            AudioClipLease lease = null;
            if (!destroyed && request.Generation == generation && op != null && !op.Released && op.Clip != null)
                lease = CreateLease(op);

            var callback = request.Callback;
            request.Callback = null;
            DeliverLease(callback, lease);
        }

        private void DeliverLease(Action<AudioClipLease> callback, AudioClipLease lease)
        {
            // Ordinary delivery remains ordered and cancellable: an earlier
            // recipient may cancel a later waiter in this same native batch.
            if (lookupMutationDepth > 0)
                DeferDelivery(callback, lease);
            else
                AudioCallbacks.Deliver(callback, lease);
        }

        private void DeferDelivery(Action<AudioClipLease> callback, AudioClipLease lease)
            => callbacks.Enqueue(() => AudioCallbacks.Deliver(callback, lease));

        private void ReleaseIfUnused(Operation op)
        {
            if (op.Released || op.Deliveries > 0 || op.Users > 0 || op.Waiters.Exists(r => !r.Finished))
                return;
            op.Released = true;
            op.Clip = null;
            if (operations.TryGetValue(op.Identity, out var current) && ReferenceEquals(current, op))
                operations.Remove(op.Identity);
            RemoveAliases(op);
            if (op.Handle.IsValid())
            {
                try
                {
                    Addressables.Release(op.Handle);
                }
                catch (Exception error)
                {
                    Debug.LogException(error);
                }
            }
        }

        private static void ReleaseLocations(Request request)
        {
            var handle = request.Locations;
            request.Locations = default;
            if (handle.IsValid())
                Addressables.Release(handle);
        }

        private void CancelRequest(Request request)
        {
            FinishRequest(request, null);
            ReleaseLocations(request);
            if (request.Operation != null)
                ReleaseIfUnused(request.Operation);
        }

        public void RefreshClipLookup()
        {
            using var mutation = callbacks.Begin();
            if (destroyed)
                return;
            lookupMutationDepth++;
            try
            {
                generation++;
                var oldRequests = requests.ToArray();
                var oldOperations = new List<Operation>(operations.Values);
                // Detach the generation before cancellation or native release can
                // reenter acquisition. Old users own their operations via leases.
                knownLocations.Clear();
                aliases.Clear();
                operations.Clear();
                foreach (var op in oldOperations)
                    ClearLookupKeys(op);
                foreach (var request in oldRequests)
                    CancelRequest(request);
                foreach (var op in oldOperations)
                    ReleaseIfUnused(op);
            }
            finally
            {
                lookupMutationDepth--;
            }
        }

        private void OnDestroy()
        {
            using var mutation = callbacks.Begin();
            destroyed = true;
            foreach (var request in requests.ToArray())
                CancelRequest(request);
            foreach (var op in new List<Operation>(operations.Values))
            {
                RemoveAliases(op);
                ReleaseIfUnused(op);
            }

            knownLocations.Clear();
            aliases.Clear();
            operations.Clear();
        // Outstanding leases deliberately retain the native handle after this component is destroyed.
        }

        public string DescribeLookup(ResolvedAudioClip address) => string.IsNullOrWhiteSpace(address.AddressablesKey) ? "Addressables disabled (empty key)" : $"Addressables key='{address.AddressablesKey}'";
    }
}
