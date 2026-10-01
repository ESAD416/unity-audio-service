using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

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
        {
            Id = id;
            ResourcesKey = id;
            AddressablesKey = id;
            Category = category;
        }
    }

    public readonly struct ResolvedAudioClip
    {
        public string Id { get; }
        public AudioCategory Category { get; }
        public string ResourcesKey { get; }
        public string AddressablesKey { get; }
        public int MaxInstances { get; }

        public ResolvedAudioClip(string id, AudioCategory category, string resourcesKey = null, string addressablesKey = null, int maxInstances = 0)
        {
            Id = id;
            Category = category;
            ResourcesKey = resourcesKey ?? id;
            AddressablesKey = addressablesKey ?? id;
            MaxInstances = maxInstances;
        }
    }

    [CreateAssetMenu(menuName = "Audio Service/Catalog")]
    public sealed class AudioCatalog : ScriptableObject, ISerializationCallbackReceiver
    {
        private sealed class Lookup
        {
            public readonly ResolvedAudioClip[] Records;
            public readonly Dictionary<(AudioCategory, string), int> Keys;

            public Lookup(int records, int keys)
            {
                Records = new ResolvedAudioClip[records];
                Keys = new Dictionary<(AudioCategory, string), int>(keys);
            }
        }

        [SerializeField, FormerlySerializedAs("Entries")]
        private AudioClipAddress[] entries = Array.Empty<AudioClipAddress>();
        [NonSerialized]
        private Lookup index;
        public event Action Changed;
        internal int Revision { get; private set; }

        public AudioClipAddress[] GetEntriesCopy() => Copy(entries);
        public void ReplaceEntries(params AudioClipAddress[] value)
        {
            entries = Copy(value);
            RebuildIndex();
            Revision++;
            AudioCallbacks.Invoke(Changed);
        }

        private static AudioClipAddress[] Copy(AudioClipAddress[] source)
        {
            if (source == null)
                return Array.Empty<AudioClipAddress>();
            var copy = new AudioClipAddress[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                var entry = source[i];
                if (entry == null)
                    continue;
                copy[i] = new AudioClipAddress(entry.Id, entry.Category)
                {
                    ResourcesKey = entry.ResourcesKey,
                    AddressablesKey = entry.AddressablesKey,
                    Aliases = entry.Aliases == null ? Array.Empty<string>() : (string[])entry.Aliases.Clone(),
                    MaxInstances = entry.MaxInstances
                };
            }

            return copy;
        }

        internal void RebuildIndex()
        {
            int count = 0, capacity = 0;
            if (entries != null)
                foreach (var entry in entries)
                    if (entry != null)
                    {
                        count++;
                        capacity += 1 + (entry.Aliases?.Length ?? 0);
                    }
            var next = new Lookup(count, capacity);
            int row = 0;
            if (entries != null)
                foreach (var entry in entries)
                {
                    if (entry == null)
                        continue;
                    next.Records[row] = new ResolvedAudioClip(entry.Id, entry.Category, entry.ResourcesKey, entry.AddressablesKey, entry.MaxInstances);
                    Add(entry.Id, entry.Category, row);
                    if (entry.Aliases != null)
                        foreach (var alias in entry.Aliases)
                            Add(alias, entry.Category, row);
                    row++;
                }

            // Publish records and alias indices together only after the snapshot is complete.
            index = next;
            void Add(string key, AudioCategory category, int record)
            {
                // Keep the first match, including an alias on an entry with an invalid
                // ID. Validation reports it; lookup must not silently choose another clip.
                if (!string.IsNullOrWhiteSpace(key))
                    next.Keys.TryAdd((category, key), record);
            }
        }

        public bool TryResolve(AudioCategory category, AudioId id, out ResolvedAudioClip address)
        {
            address = default;
            if (string.IsNullOrWhiteSpace(id.Value))
                return false;
            var current = index;
            if (current == null)
            {
                RebuildIndex();
                current = index;
            }
            if (!current.Keys.TryGetValue((category, id.Value), out int row))
                return false;
            address = current.Records[row];
            return !string.IsNullOrWhiteSpace(address.Id);
        }

        /// <summary>Check authoring data without loading assets or changing the catalog.
        /// Source key presence does not establish availability in a configured provider.</summary>
        public IReadOnlyList<string> GetValidationIssues()
        {
            var issues = new List<string>();
            var keys = new Dictionary<(AudioCategory, string), int>();
            if (entries == null)
            {
                issues.Add("Entries is null; the catalog resolves no IDs.");
                return issues;
            }

            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (entry == null)
                {
                    issues.Add($"Entries[{i}]: entry is null.");
                    continue;
                }

                if ((int)entry.Category < 0 || (int)entry.Category > 2)
                    issues.Add($"Entries[{i}]: invalid Category.");
                CheckKey(entry.Id, "Id", i, entry.Category);
                if (entry.Aliases != null)
                    for (int a = 0; a < entry.Aliases.Length; a++)
                        CheckKey(entry.Aliases[a], $"Aliases[{a}]", i, entry.Category);
                if (string.IsNullOrWhiteSpace(entry.ResourcesKey ?? entry.Id) && string.IsNullOrWhiteSpace(entry.AddressablesKey ?? entry.Id))
                    issues.Add($"Entries[{i}]: no source key for the built-in providers. Set ResourcesKey or AddressablesKey; null falls back to Id.");
                if (entry.MaxInstances < 0)
                    issues.Add($"Entries[{i}]: MaxInstances must be 0 (unlimited) or positive.");
            }

            return issues;
            void CheckKey(string key, string field, int entryIndex, AudioCategory category)
            {
                string location = $"Entries[{entryIndex}].{field}";
                if (string.IsNullOrWhiteSpace(key))
                {
                    issues.Add(location + ": key is empty.");
                    return;
                }

                if (key != key.Trim())
                    issues.Add(location + ": surrounding whitespace cannot be addressed directly by AudioId.");
                if (keys.TryGetValue((category, key), out var first))
                    issues.Add($"{location}: duplicate key '{key}' in {category}; Entries[{first}] has the first match.");
                else
                    keys.Add((category, key), entryIndex);
            }
        }

        // Serialization/validation callbacks only invalidate this object's managed cache.
        private void InvalidateIndex()
        {
            index = null;
            Revision++;
        }

        private void OnEnable() => InvalidateIndex();
        private void OnValidate() => InvalidateIndex();
        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize() => InvalidateIndex();
    }
}
