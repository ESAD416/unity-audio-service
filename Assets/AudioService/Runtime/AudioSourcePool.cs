using System.Collections.Generic;
using UnityEngine;
using Emitter = Esad.Audio.AudioPlaybackEngine.Emitter;

namespace Esad.Audio
{
    // Native AudioSources behind playback: creation, idle reuse, the active update
    // list and trimming. An emitter is in use while it has a current or pending playback.
    internal sealed class AudioSourcePool
    {
        private readonly Transform parent;
        private readonly List<Emitter> all = new();
        private readonly Stack<Emitter> idle = new();

        public AudioSourcePool(Transform parent) => this.parent = parent;

        /// <summary>Emitters the engine updates each tick. Recycling swaps the last one into the freed index.</summary>
        public List<Emitter> Active { get; } = new();
        /// <summary>Total sources currently held, active and idle.</summary>
        public int Count => all.Count;
        public int IdleCount => idle.Count;

        public Emitter Rent()
        {
            var emitter = idle.Count > 0 ? idle.Pop() : Create();
            emitter.InPool = false;
            Activate(emitter);
            return emitter;
        }

        /// <summary>Reset an emitter that no playback uses and return it to the idle pool.</summary>
        public void Recycle(Emitter emitter)
        {
            if (emitter.InUse)
                return;
            Deactivate(emitter);
            emitter.Transition = default;
            emitter.Envelope = 1;
            var source = emitter.Source;
            source.Stop();
            source.clip = null;
            source.loop = false;
            source.pitch = 1;
            source.mute = false;
            source.ignoreListenerPause = false;
            source.outputAudioMixerGroup = null;
            source.spatialBlend = 0;
            source.transform.localPosition = Vector3.zero;
            if (emitter.InPool)
                return;
            emitter.InPool = true;
            idle.Push(emitter);
        }

        /// <summary>Grow total capacity to targetCount and return the number created.</summary>
        public int Prewarm(int targetCount)
        {
            int missing = targetCount - all.Count;
            if (missing <= 0)
                return 0;
            for (int i = 0; i < missing; i++)
            {
                var emitter = Create();
                emitter.InPool = true;
                idle.Push(emitter);
            }

            // Idle emitters stay out of Active and acquire current gains on Start.
            if (Active.Capacity < all.Count)
                Active.Capacity = all.Count;
            return missing;
        }

        /// <summary>Destroy idle sources while keeping minimumCapacity in total; Unity finishes at frame end.</summary>
        public int Trim(int minimumCapacity, int maxToRemove)
        {
            int removed = 0;
            while (removed < maxToRemove && idle.Count > 0 && all.Count > minimumCapacity)
            {
                var emitter = idle.Pop();
                emitter.InPool = false;
                all.Remove(emitter);
                removed++;
                Object.Destroy(emitter.Source.gameObject);
            }

            return removed;
        }

        /// <summary>After an audio reset the cached gains no longer describe the native sources.</summary>
        public void ForgetAppliedGains()
        {
            foreach (var emitter in all)
                emitter.AppliedGain = float.NaN;
        }

        public void DestroyAll()
        {
            foreach (var emitter in all)
                if (emitter.Source != null)
                    Object.Destroy(emitter.Source.gameObject);
            idle.Clear();
        }

        private Emitter Create()
        {
            var go = new GameObject("AudioEmitter");
            go.transform.SetParent(parent, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0;
            source.Stop();
            var emitter = new Emitter { Source = source };
            all.Add(emitter);
            return emitter;
        }

        private void Activate(Emitter emitter)
        {
            if (emitter.UpdateIndex >= 0)
                return;
            emitter.UpdateIndex = Active.Count;
            Active.Add(emitter);
        }

        private void Deactivate(Emitter emitter)
        {
            int index = emitter.UpdateIndex;
            if (index < 0)
                return;
            int last = Active.Count - 1;
            Active[index] = Active[last];
            Active[index].UpdateIndex = index;
            Active.RemoveAt(last);
            emitter.UpdateIndex = -1;
        }
    }
}
