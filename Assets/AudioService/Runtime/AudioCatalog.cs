using System;
using UnityEngine;

namespace Controller.Audio
{
    [Serializable]
    public sealed class AudioClipAddress
    {
        public string Id;
        public string ResourcesKey;
        public string AddressablesKey;
        public string[] Aliases = Array.Empty<string>();
        public AudioCategory Category;
        public int MaxInstances;
        public AudioClipAddress(string id, AudioCategory category)
        { Id = id; ResourcesKey = id; AddressablesKey = id; Category = category; }
    }

    [CreateAssetMenu(menuName = "Audio Service/Catalog")]
    public sealed class AudioCatalog : ScriptableObject
    {
        public AudioClipAddress[] Entries = Array.Empty<AudioClipAddress>();
        public bool TryResolve(AudioCategory category, AudioId id, out AudioClipAddress address)
        {
            address = null;
            if (string.IsNullOrWhiteSpace(id.Value)) return false;
            foreach (var entry in Entries)
            {
                if (entry == null || entry.Category != category) continue;
                if (entry.Id == id.Value || Array.IndexOf(entry.Aliases ?? Array.Empty<string>(), id.Value) >= 0)
                { address = entry; return !string.IsNullOrWhiteSpace(entry.Id); }
            }
            return false;
        }
    }

    public interface IAudioClipProviderChanges
    {
        event Action Changed;
    }

    // Explicit cache queries never cause Resources.Load or Addressables.LoadAssetAsync.
    public interface IAudioClipCache
    {
        bool TryGetCachedClip(AudioCategory category, string key, out AudioClip clip);
    }

    // Optional ownership contract. The provider keeps the clip valid until Dispose.
    // Implementations call completed exactly once, including failure (null).
    public interface IAudioClipLeaseProvider : IAudioClipCache
    {
        void AcquireClip(AudioClipAddress address, Action<AudioClipLease> completed);
    }

    // Explicit synchronous acquisition: Resources may load here; Addressables only
    // succeeds for an already resident asset. This is not a pure cache query.
    public interface IAudioSynchronousClipProvider
    {
        bool TryAcquireClip(AudioClipAddress address, out AudioClipLease lease);
    }

    public sealed class AudioClipLease : IDisposable
    {
        private Action release;
        public AudioClip Clip { get; private set; }
        public AudioClipLease(AudioClip clip, Action release = null) { Clip = clip; this.release = release; }
        public void Dispose() { var action = release; release = null; Clip = null; action?.Invoke(); }
    }
}
