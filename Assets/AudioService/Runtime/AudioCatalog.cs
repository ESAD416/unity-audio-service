using System;
using System.Collections.Generic;
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
    public sealed class AudioCatalog : ScriptableObject, ISerializationCallbackReceiver
    {
        public AudioClipAddress[] Entries = Array.Empty<AudioClipAddress>();
        [NonSerialized] private Dictionary<(AudioCategory, string), AudioClipAddress> index;
        [NonSerialized] private AudioClipAddress[] indexedEntries;

        /// <summary>Rebuild after editing entries or aliases in place. When used by a
        /// controller, call RefreshClipProvider instead to refresh its clip cache too.</summary>
        public void RebuildIndex()
        {
            var entries = Entries;
            int capacity = 0;
            if (entries != null)
                foreach (var entry in entries) if (entry != null) capacity += 1 + (entry.Aliases?.Length ?? 0);
            var next = new Dictionary<(AudioCategory, string), AudioClipAddress>(capacity);
            if (entries != null)
                foreach (var entry in entries)
                {
                    if (entry == null) continue;
                    Add(entry.Id, entry);
                    if (entry.Aliases != null) foreach (var alias in entry.Aliases) Add(alias, entry);
                }
            indexedEntries = entries; index = next;

            void Add(string key, AudioClipAddress entry)
            {
                // Keep the first match, including an alias on an entry with an invalid
                // ID. Validation reports it; lookup must not silently choose another clip.
                if (!string.IsNullOrWhiteSpace(key)) next.TryAdd((entry.Category, key), entry);
            }
        }

        public bool TryResolve(AudioCategory category, AudioId id, out AudioClipAddress address)
        {
            address = null;
            if (string.IsNullOrWhiteSpace(id.Value)) return false;
            if (index == null || !ReferenceEquals(indexedEntries, Entries)) RebuildIndex();
            return index.TryGetValue((category, id.Value), out address) && !string.IsNullOrWhiteSpace(address.Id);
        }

        /// <summary>Check authoring data without loading assets or changing the catalog.
        /// Source key presence does not establish availability in a configured provider.</summary>
        public IReadOnlyList<string> GetValidationIssues()
        {
            var issues = new List<string>();
            var keys = new Dictionary<(AudioCategory, string), int>();
            if (Entries == null) { issues.Add("Entries is null; the catalog resolves no IDs."); return issues; }
            for (int i = 0; i < Entries.Length; i++)
            {
                var entry = Entries[i];
                if (entry == null) { issues.Add($"Entries[{i}]: entry is null."); continue; }
                if ((int)entry.Category < 0 || (int)entry.Category > 2) issues.Add($"Entries[{i}]: invalid Category.");
                CheckKey(entry.Id, "Id", i, entry.Category);
                if (entry.Aliases != null)
                    for (int a = 0; a < entry.Aliases.Length; a++) CheckKey(entry.Aliases[a], $"Aliases[{a}]", i, entry.Category);
                if (string.IsNullOrWhiteSpace(entry.ResourcesKey ?? entry.Id) && string.IsNullOrWhiteSpace(entry.AddressablesKey ?? entry.Id))
                    issues.Add($"Entries[{i}]: no source key for the built-in providers. Set ResourcesKey or AddressablesKey; null falls back to Id.");
                if (entry.MaxInstances < 0) issues.Add($"Entries[{i}]: MaxInstances must be 0 (unlimited) or positive.");
            }
            return issues;

            void CheckKey(string key, string field, int entryIndex, AudioCategory category)
            {
                string location = $"Entries[{entryIndex}].{field}";
                if (string.IsNullOrWhiteSpace(key)) { issues.Add(location + ": key is empty."); return; }
                if (key != key.Trim()) issues.Add(location + ": surrounding whitespace cannot be addressed directly by AudioId.");
                if (keys.TryGetValue((category, key), out var first))
                    issues.Add($"{location}: duplicate key '{key}' in {category}; Entries[{first}] has the first match.");
                else keys.Add((category, key), entryIndex);
            }
        }

        // Serialization/validation callbacks only invalidate this object's managed cache.
        private void InvalidateIndex() { index = null; indexedEntries = null; }
        private void OnEnable() => InvalidateIndex();
        private void OnValidate() => InvalidateIndex();
        void ISerializationCallbackReceiver.OnBeforeSerialize() { }
        void ISerializationCallbackReceiver.OnAfterDeserialize() => InvalidateIndex();
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
