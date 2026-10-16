using System.Collections.Generic;
using UnityEngine;
using Playback = Esad.Audio.AudioPlaybackEngine.Playback;

namespace Esad.Audio
{
    // Global and per-audio concurrency limits for new requests. Loading and paused
    // requests count; a replacement slot's own current and pending requests do not.
    internal sealed class AudioAdmission
    {
        private readonly List<Playback> active;
        // Built only when a per-audio limit is used. Ordinary unlimited sessions keep
        // no index, including after a busy scene ends.
        private Dictionary<(AudioCategory, AudioId), int> counts;
        private bool counting;

        public AudioAdmission(List<Playback> active) => this.active = active;

        public int MaxConcurrentSounds { get; set; }
        public AudioConcurrencyPolicy ConcurrencyPolicy { get; set; }

        public void Added(Playback playback)
        {
            if (counting)
                Increment(playback.Handle);
        }

        public void Removed(Playback playback)
        {
            if (!counting)
                return;
            var key = Key(playback.Handle);
            int remaining = counts[key] - 1;
            if (remaining == 0)
                counts.Remove(key);
            else
                counts[key] = remaining;
            if (active.Count == 0)
                counting = false;
        }

        /// <summary>Returns the rejection reason, or null when the request is admitted.
        /// An admitted request that must evict receives its victims in eviction order.</summary>
        public string Plan(AudioHandle incoming, PlaybackSlot slot, int maxInstances, AudioConcurrencyPolicy policy, out Playback[] victims)
        {
            victims = null;
            if (MaxConcurrentSounds <= 0 && maxInstances <= 0)
                return null;
            bool replacesSlot = slot != PlaybackSlot.None;
            int count = active.Count, same = 0;
            // Replacement slots exclude their current/pending requests; retain
            // their scan. Ordinary requests can count Loading/Paused via the index.
            if (replacesSlot)
            {
                count = 0;
                foreach (var p in active)
                {
                    if (p.Slot == slot)
                        continue;
                    count++;
                    if (maxInstances > 0 && SameAudio(p, incoming))
                        same++;
                }
            }
            else if (maxInstances > 0)
                same = Count(incoming);
            // Decide every limit before stopping anything. Per-audio evictions also
            // free global capacity; lowered limits may require more than one victim.
            int soundVictims = maxInstances > 0 ? Mathf.Max(0, same - maxInstances + 1) : 0;
            if (soundVictims > 0 && policy == AudioConcurrencyPolicy.RejectNew)
                return "Per-audio concurrency limit";
            int globalVictims = MaxConcurrentSounds > 0 ? Mathf.Max(0, count - soundVictims - MaxConcurrentSounds + 1) : 0;
            if (globalVictims > 0 && ConcurrencyPolicy == AudioConcurrencyPolicy.RejectNew)
                return "Global concurrency limit";
            int victimCount = soundVictims + globalVictims;
            if (victimCount > 0)
                victims = SelectVictims(incoming, slot, soundVictims, victimCount);
            return null;
        }

        // Preserve per-audio-first, then global-oldest order. Only accepted
        // requests that actually evict allocate a candidate snapshot.
        private Playback[] SelectVictims(AudioHandle incoming, PlaybackSlot slot, int soundVictims, int victimCount)
        {
            var victims = new Playback[victimCount];
            int soundIndex = 0, globalIndex = soundVictims;
            foreach (var p in active)
            {
                if (slot != PlaybackSlot.None && p.Slot == slot)
                    continue;
                if (soundIndex < soundVictims && SameAudio(p, incoming))
                    victims[soundIndex++] = p;
                else if (globalIndex < victimCount)
                    victims[globalIndex++] = p;
                if (soundIndex == soundVictims && globalIndex == victimCount)
                    break;
            }

            return victims;
        }

        private int Count(AudioHandle handle)
        {
            if (!counting)
            {
                counts ??= new Dictionary<(AudioCategory, AudioId), int>();
                counts.Clear();
                foreach (var p in active)
                    Increment(p.Handle);
                counting = true;
            }

            counts.TryGetValue(Key(handle), out int count);
            return count;
        }

        private void Increment(AudioHandle handle)
        {
            var key = Key(handle);
            counts.TryGetValue(key, out int count);
            counts[key] = count + 1;
        }

        private static (AudioCategory, AudioId) Key(AudioHandle handle) => (handle.Category, handle.AudioId);
        private static bool SameAudio(Playback playback, AudioHandle handle)
            => playback.Handle.Category == handle.Category && playback.Handle.AudioId.Equals(handle.AudioId);
    }
}
