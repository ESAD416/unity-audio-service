using System;
using UnityEngine;

namespace Controller.Audio
{
    public enum AudioCategory
    {
        Bgm,
        Sfx,
        Voice
    }

    /// <summary>Main-thread access. Cache queries never load; every successful acquisition
    /// owns a lease. Completion runs exactly once, including failure (null).</summary>
    public interface IAudioClipProvider
    {
        bool TryGetCachedClip(ResolvedAudioClip address, out AudioClip clip);
        bool TryAcquireClip(ResolvedAudioClip address, out AudioClipLease lease);
        void AcquireClip(ResolvedAudioClip address, Action<AudioClipLease> completed);
    }

    public interface IAudioClipProviderChanges
    {
        event Action Changed;
    }

    /// <summary>Cancel pending acquisitions and publish fresh lookups, keeping issued
    /// leases valid. Refresh must not raise Changed.</summary>
    public interface IAudioClipProviderRefresh
    {
        void RefreshClipLookup();
    }

    public interface IAudioClipProviderDiagnostics
    {
        string DescribeLookup(ResolvedAudioClip address);
    }

    public sealed class AudioClipLease : IDisposable
    {
        private Action release;
        public AudioClip Clip { get; private set; }

        public AudioClipLease(AudioClip clip, Action release = null)
        {
            Clip = clip;
            this.release = release;
        }

        public void Dispose()
        {
            var action = release;
            release = null;
            Clip = null;
            action?.Invoke();
        }
    }
}
