using System;
using System.Collections.Generic;
using UnityEngine;

namespace Controller.Audio
{
    public class FallbackAudioClipProvider : MonoBehaviour, IAudioClipProvider, IAudioClipProviderChanges, IAudioClipProviderRefresh, IAudioClipProviderDiagnostics
    {
        [SerializeField]
        private MonoBehaviour mainProvider;
        [SerializeField]
        private MonoBehaviour backupProvider;
        [SerializeField]
        private AudioFallbackPolicy policy;
        private IAudioClipProvider main, backup;
        private bool configured;
        private int generation;
        private readonly List<Action> cancellations = new();
        private readonly AudioCallbackQueue callbacks = new();
        private IAudioClipProvider Primary => configured ? main : mainProvider as IAudioClipProvider;
        private IAudioClipProvider Secondary => configured ? backup : backupProvider as IAudioClipProvider;

        public event Action Changed;
        public AudioFallbackPolicy Policy
        {
            get => policy;
            set
            {
                if (policy == value)
                    return;
                policy = value;
                NotifyChanged();
            }
        }

        public void Configure(IAudioClipProvider primary, IAudioClipProvider secondary, AudioFallbackPolicy selection)
        {
            if (Contains(primary, this, new HashSet<IAudioClipProvider>()) || Contains(secondary, this, new HashSet<IAudioClipProvider>()))
                throw new ArgumentException("Fallback providers must not form a cycle.");
            Unsubscribe();
            main = primary;
            backup = secondary;
            policy = selection;
            configured = true;
            if (isActiveAndEnabled)
                Subscribe();
            NotifyChanged();
        }

        private static bool Contains(IAudioClipProvider provider, IAudioClipProvider target, HashSet<IAudioClipProvider> visited)
        {
            if (!AudioValues.Alive(provider))
                return false;
            if (ReferenceEquals(provider, target))
                return true;
            return visited.Add(provider) && provider is FallbackAudioClipProvider fallback && (Contains(fallback.Primary, target, visited) || Contains(fallback.Secondary, target, visited));
        }

        private void OnEnable()
        {
            if (Contains(Primary, this, new HashSet<IAudioClipProvider>()) || Contains(Secondary, this, new HashSet<IAudioClipProvider>()))
            {
                Debug.LogError("[AudioService] Cyclic fallback configuration.", this);
                main = backup = null;
                configured = true;
            }

            Subscribe();
        }

        private void Subscribe()
        {
            if (Primary is IAudioClipProviderChanges primary)
                primary.Changed += NotifyChanged;
            if (!ReferenceEquals(Primary, Secondary) && Secondary is IAudioClipProviderChanges secondary)
                secondary.Changed += NotifyChanged;
        }

        private void Unsubscribe()
        {
            if (Primary is IAudioClipProviderChanges primary)
                primary.Changed -= NotifyChanged;
            if (!ReferenceEquals(Primary, Secondary) && Secondary is IAudioClipProviderChanges secondary)
                secondary.Changed -= NotifyChanged;
        }

        private void NotifyChanged()
        {
            using var mutation = callbacks.Begin();
            CancelPending();
            AudioCallbacks.Broadcast(Changed);
        }

        public bool TryGetCachedClip(ResolvedAudioClip address, out AudioClip clip)
        {
            clip = null;
            if (AudioValues.Alive(Primary) && Primary.TryGetCachedClip(address, out clip))
                return true;
            return policy == AudioFallbackPolicy.PreferAvailable && AudioValues.Alive(Secondary) && Secondary.TryGetCachedClip(address, out clip);
        }

        public bool TryAcquireClip(ResolvedAudioClip address, out AudioClipLease lease)
        {
            if (TryAcquire(Primary, address, out lease))
                return true;
            return policy == AudioFallbackPolicy.PreferAvailable && TryAcquire(Secondary, address, out lease);
        }

        private static bool TryAcquire(IAudioClipProvider provider, ResolvedAudioClip address, out AudioClipLease lease)
        {
            lease = null;
            if (!AudioValues.Alive(provider))
                return false;
            if (provider.TryAcquireClip(address, out lease) && lease?.Clip != null)
                return true;
            AudioCallbacks.Dispose(lease);
            lease = null;
            return false;
        }

        public void AcquireClip(ResolvedAudioClip address, Action<AudioClipLease> completed)
        {
            using var mutation = callbacks.Begin();
            int version = generation;
            if (policy == AudioFallbackPolicy.PreferAvailable)
            {
                AudioClipLease immediate = null;
                bool acquired;
                try
                {
                    acquired = TryAcquireClip(address, out immediate);
                }
                catch (Exception)
                {
                    callbacks.Release(immediate);
                    Deliver(completed, null);
                    return;
                }

                // A synchronous provider can reconfigure or disable this owner.
                if (!IsCurrent(version))
                {
                    callbacks.Release(immediate);
                    Deliver(completed, null);
                    return;
                }
                if (acquired)
                {
                    Deliver(completed, immediate);
                    return;
                }
            }

            AcquirePending(address, completed, version);
        }

        private bool IsCurrent(int version) => this != null && generation == version;
        private void Deliver(Action<AudioClipLease> completed, AudioClipLease lease)
            => callbacks.Enqueue(() => AudioCallbacks.Deliver(completed, lease));

        private void AcquirePending(ResolvedAudioClip address, Action<AudioClipLease> completed, int version)
        {
            bool finished = false;
            Action cancel = null;
            bool Valid() => !finished && IsCurrent(version);
            void Finish(AudioClipLease lease)
            {
                using var delivery = callbacks.Begin();
                if (finished)
                {
                    callbacks.Release(lease);
                    return;
                }

                bool valid = Valid();
                finished = true;
                cancellations.Remove(cancel);
                if (!valid)
                {
                    callbacks.Release(lease);
                    lease = null;
                }

                Deliver(completed, lease);
            }

            cancel = () => Finish(null);
            cancellations.Add(cancel);
            try
            {
                var primary = Primary;
                var secondary = Secondary;
                Acquire(primary, address, lease =>
                {
                    if (!AudioValues.Alive(primary))
                    {
                        AudioCallbacks.Dispose(lease);
                        lease = null;
                    }

                    if (!Valid() || lease?.Clip != null)
                        Finish(lease);
                    else
                    {
                        AudioCallbacks.Dispose(lease);
                        Acquire(secondary, address, Finish);
                    }
                });
            }
            catch (Exception)
            {
                Finish(null);
            }
        }

        private static void Acquire(IAudioClipProvider provider, ResolvedAudioClip address, Action<AudioClipLease> completed)
        {
            if (!AudioValues.Alive(provider))
            {
                completed(null);
                return;
            }

            try
            {
                provider.AcquireClip(address, completed);
            }
            catch (Exception)
            {
                completed(null);
            }
        }

        public void RefreshClipLookup()
        {
            using var mutation = callbacks.Begin();
            CancelPending();
            if (AudioValues.Alive(Primary) && Primary is IAudioClipProviderRefresh primary)
                AudioCallbacks.Invoke(primary.RefreshClipLookup);
            if (!ReferenceEquals(Primary, Secondary) && AudioValues.Alive(Secondary) && Secondary is IAudioClipProviderRefresh secondary)
                AudioCallbacks.Invoke(secondary.RefreshClipLookup);
        }

        private void CancelPending()
        {
            generation++;
            var pending = cancellations.ToArray();
            cancellations.Clear();
            foreach (var cancel in pending)
                AudioCallbacks.Invoke(cancel);
        }

        private void OnDisable()
        {
            using var mutation = callbacks.Begin();
            Unsubscribe();
            CancelPending();
        }

        public string DescribeLookup(ResolvedAudioClip address) => $"Fallback {policy}; primary [{AudioFailureLog.DescribeProvider(Primary, address)}]; backup [{AudioFailureLog.DescribeProvider(Secondary, address)}]";
    }
}
