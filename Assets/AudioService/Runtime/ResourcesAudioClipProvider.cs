using System;
using System.Collections.Generic;
using UnityEngine;

namespace Esad.Audio
{
    public class ResourcesAudioClipProvider : MonoBehaviour, IAudioClipProvider, IAudioClipProviderRefresh, IAudioClipProviderDiagnostics
    {
        private sealed class Entry
        {
            public AudioClip Clip;
            public int Users;
        }

        [SerializeField]
        private string rootPath = "Audio";
        [SerializeField]
        private string bgmPath = "BGM";
        [SerializeField]
        private string sfxPath = "SFX";
        [SerializeField]
        private string voicePath = "Voice";
        private readonly Dictionary<string, Entry> cache = new();
        private Entry Load(string path)
        {
            if (cache.TryGetValue(path, out var entry) && entry.Clip != null)
                return entry;
            var clip = Resources.Load<AudioClip>(path);
            if (clip == null)
                return null;
            entry = new Entry
            {
                Clip = clip
            };
            cache[path] = entry;
            return entry;
        }

        // The Resources path of an address, or null when it has no key. A null Resources
        // key falls back to the ID; an empty key disables Resources for that clip.
        private string PathOf(ResolvedAudioClip address)
        {
            string key = address.ResourcesKey ?? address.Id;
            return string.IsNullOrWhiteSpace(key) ? null : BuildPath(address.Category, key);
        }

        public bool TryGetCachedClip(ResolvedAudioClip address, out AudioClip clip)
        {
            string path = PathOf(address);
            clip = path != null && cache.TryGetValue(path, out var entry) ? entry.Clip : null;
            return clip != null;
        }

        public void AcquireClip(ResolvedAudioClip address, Action<AudioClipLease> completed)
        {
            TryAcquireClip(address, out var lease);
            AudioCallbacks.Deliver(completed, lease);
        }

        public bool TryAcquireClip(ResolvedAudioClip address, out AudioClipLease lease)
        {
            lease = null;
            string path = PathOf(address);
            var entry = path != null ? Load(path) : null;
            if (entry == null)
                return false;
            entry.Users++;
            lease = new AudioClipLease(entry.Clip, () =>
            {
                entry.Users--;
                ReleaseIfUnused(path, entry);
            });
            return true;
        }

        private void ReleaseIfUnused(string path, Entry entry)
        {
            if (entry.Users > 0)
                return;
            if (cache.TryGetValue(path, out var current) && ReferenceEquals(current, entry))
                cache.Remove(path);
            // External owners may still use this clip; leave unloading to Unity.
            entry.Clip = null;
        }

        public void RefreshClipLookup() => cache.Clear();
        private void OnDestroy() => RefreshClipLookup();
        public string DescribeLookup(ResolvedAudioClip address)
            => PathOf(address) is { } path ? $"Resources path='{path}'" : "Resources disabled (empty key)";

        private string BuildPath(AudioCategory category, string key)
        {
            string folder = category switch
            {
                AudioCategory.Bgm => bgmPath,
                AudioCategory.Sfx => sfxPath,
                _ => voicePath
            };
            return string.IsNullOrEmpty(folder) ? $"{rootPath}/{key}" : $"{rootPath}/{folder}/{key}";
        }
    }
}
