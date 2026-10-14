using System;
using System.Collections.Generic;
using UnityEngine;

namespace Controller.Audio
{
    internal sealed class AudioPreparationQueue
    {
        private sealed class Preparation
        {
            public string Group;
            public Action<bool> Completed;
            public AudioClipLease Lease;
            public Action CancelLoad;
            public bool Finished;
        }

        private readonly List<Preparation> pending = new();
        private readonly AudioCallbackQueue callbacks;
        public int Count => pending.Count;

        public AudioPreparationQueue(AudioCallbackQueue callbacks) => this.callbacks = callbacks;

        // The caller supplies the current store; this queue never retains a stale generation.
        public void Prepare(AudioClipStore store, ResolvedAudioClip address, string group, Action<bool> completed)
        {
            using var mutation = callbacks.Begin();
            var preparation = new Preparation { Group = group, Completed = completed };
            pending.Add(preparation);
            var cancel = store.Request(address, true, lease => Receive(preparation, lease), group);
            if (!preparation.Finished && preparation.Lease == null)
                preparation.CancelLoad = cancel;
        }

        // Keep the delivered lease and ask Unity for audio data that is not loaded yet;
        // Check or a later Tick finishes once Unity reports the data loaded or failed.
        private void Receive(Preparation preparation, AudioClipLease lease)
        {
            using var delivery = callbacks.Begin();
            if (preparation.Finished)
            {
                AudioCallbacks.Dispose(lease);
                return;
            }

            preparation.CancelLoad = null;
            preparation.Lease = lease;
            try
            {
                if (lease?.Clip == null || !RequestAudioData(lease.Clip))
                    Finish(preparation, false);
                else
                    Check(preparation);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                Finish(preparation, false);
            }
        }

        private static bool RequestAudioData(AudioClip clip)
            => clip.loadState != AudioDataLoadState.Unloaded || clip.LoadAudioData();

        public void Tick()
        {
            using var mutation = callbacks.Begin();
            for (int i = pending.Count - 1; i >= 0; i--)
                Check(pending[i]);
        }

        private void Check(Preparation preparation)
        {
            if (preparation.Finished || preparation.Lease == null)
                return;
            var clip = preparation.Lease.Clip;
            if (clip == null || clip.loadState == AudioDataLoadState.Failed)
                Finish(preparation, false);
            else if (clip.loadState == AudioDataLoadState.Loaded)
                Finish(preparation, true);
        }

        private void Finish(Preparation preparation, bool success)
        {
            if (preparation.Finished)
                return;
            preparation.Finished = true;
            pending.Remove(preparation);
            var cancel = preparation.CancelLoad;
            preparation.CancelLoad = null;
            var lease = preparation.Lease;
            preparation.Lease = null;
            var completed = preparation.Completed;
            preparation.Completed = null;
            if (completed != null)
                callbacks.Enqueue(() => AudioCallbacks.Invoke(completed, success));
            AudioCallbacks.Invoke(cancel);
            AudioCallbacks.Dispose(lease);
        }

        public void CancelAll()
        {
            using var mutation = callbacks.Begin();
            foreach (var preparation in pending.ToArray())
                Finish(preparation, false);
        }

        public void CancelGroup(string group)
        {
            using var mutation = callbacks.Begin();
            foreach (var preparation in pending.ToArray())
                if (preparation.Group == group)
                    Finish(preparation, false);
        }
    }
}
