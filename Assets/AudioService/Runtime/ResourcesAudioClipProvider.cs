using System;
using System.Collections.Generic;
using UnityEngine;

namespace Controller.Audio
{
    public class ResourcesAudioClipProvider : MonoBehaviour, IAudioClipProvider, IAudioClipLeaseProvider, IAudioSynchronousClipProvider
    {
        private sealed class Entry { public AudioClip Clip; public int Users; public bool Retained; }
        [SerializeField] private string rootPath = "Audio";
        [SerializeField] private string bgmPath = "BGM";
        [SerializeField] private string sfxPath = "SFX";
        [SerializeField] private string voicePath = "Voice";
        private readonly Dictionary<string, Entry> cache = new();
        public AudioClip GetClip(AudioCategory category, string key) { TryGetClip(category, key, out var clip); return clip; }
        private Entry Load(string path)
        {
            if (cache.TryGetValue(path, out var entry) && entry.Clip != null) return entry;
            var clip = Resources.Load<AudioClip>(path);
            if (clip == null) return null;
            entry = new Entry { Clip = clip }; cache[path] = entry; return entry;
        }
        // Legacy lookup retains its documented synchronous load and cache behavior.
        public bool TryGetClip(AudioCategory category, string key, out AudioClip clip)
        {
            clip = null; if (string.IsNullOrWhiteSpace(key)) return false;
            var entry = Load(BuildPath(category, key));
            if (entry == null) return false;
            entry.Retained = true; clip = entry.Clip; return true;
        }
        public bool TryGetCachedClip(AudioCategory category, string key, out AudioClip clip)
        {
            clip = null;
            if (!string.IsNullOrWhiteSpace(key) && cache.TryGetValue(BuildPath(category, key), out var entry)) clip = entry.Clip;
            return clip != null;
        }
        public void AcquireClip(AudioClipAddress address, Action<AudioClipLease> completed)
        { TryAcquireClip(address, out var lease); completed(lease); }
        public bool TryAcquireClip(AudioClipAddress address, out AudioClipLease lease)
        {
            lease = null; string key = address.ResourcesKey ?? address.Id;
            if (string.IsNullOrWhiteSpace(key)) return false;
            string path = BuildPath(address.Category, key); var entry = Load(path);
            if (entry == null) return false;
            entry.Users++;
            lease = new AudioClipLease(entry.Clip, () => { entry.Users--; ReleaseIfUnused(path, entry); });
            return true;
        }
        private void ReleaseIfUnused(string path, Entry entry)
        {
            if (entry.Users > 0 || entry.Retained) return;
            if (cache.TryGetValue(path, out var current) && ReferenceEquals(current, entry)) cache.Remove(path);
            entry.Clip = null;
            // Do not force UnloadAsset: an external clip owner may still be using it.
            // Unreferenced assets remain eligible for Unity's normal unused-asset cleanup.
        }
        public void ClearCache()
        {
            foreach (var pair in new List<KeyValuePair<string, Entry>>(cache))
            { pair.Value.Retained = false; ReleaseIfUnused(pair.Key, pair.Value); }
        }
        private void OnDestroy() => ClearCache();
        internal string DescribeLookup(AudioClipAddress address)
        {
            string key = address.ResourcesKey ?? address.Id;
            return string.IsNullOrWhiteSpace(key) ? "Resources disabled (empty key)"
                : $"Resources path='{BuildPath(address.Category, key)}'";
        }
        private string BuildPath(AudioCategory category, string key)
        {
            string folder = category == AudioCategory.Bgm ? bgmPath : category == AudioCategory.Sfx ? sfxPath : voicePath;
            return string.IsNullOrEmpty(folder) ? $"{rootPath}/{key}" : $"{rootPath}/{folder}/{key}";
        }
    }
}
