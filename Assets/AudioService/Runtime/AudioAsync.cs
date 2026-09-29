using System;
using System.Collections;
using System.Collections.Generic;

namespace Controller.Audio
{
    internal static class AudioAsync
    {
        public static IEnumerator AcquireLegacy(IAudioClipProvider provider, AudioClipAddress address, Action<AudioClipLease> completed)
        {
            UnityEngine.AudioClip clip = null;
            bool failed = false;
            try { if (AudioValues.Alive(provider)) provider.TryGetClip(address.Category, address.Id, out clip); }
            catch (Exception) { failed = true; }
            if (!failed && clip == null && provider is IAsyncAudioClipProvider asyncProvider)
            {
                IEnumerator loading = null;
                try
                {
                    loading = provider is IResultAudioClipProvider results
                        ? results.LoadClipAsync(address.Category, address.Id, result => clip = result)
                        : asyncProvider.LoadClipAsync(address.Category, address.Id);
                }
                catch (Exception) { failed = true; }
                if (!failed) yield return Guard(loading, () => failed = true);
                if (!failed && !(provider is IResultAudioClipProvider))
                {
                    try { if (AudioValues.Alive(provider)) provider.TryGetClip(address.Category, address.Id, out clip); }
                    catch (Exception) { failed = true; }
                }
            }
            if (failed || !AudioValues.Alive(provider)) clip = null;
            completed(clip == null ? null : new AudioClipLease(clip, () =>
            { if (AudioValues.Alive(provider) && provider is IAsyncAudioClipProvider async) async.ReleaseClip(address.Category, address.Id); }));
        }

        // Observe exceptions from nested provider enumerators instead of leaving a
        // playback permanently in Loading after Unity abandons its coroutine.
        public static IEnumerator Guard(IEnumerator routine, Action failed)
        {
            if (routine == null) { failed(); yield break; }
            var stack = new Stack<IEnumerator>(); stack.Push(routine);
            try
            {
                while (stack.Count > 0)
                {
                    bool moved = false, error = false; object current = null;
                    try { moved = stack.Peek().MoveNext(); if (moved) current = stack.Peek().Current; }
                    catch (Exception) { error = true; }
                    if (error) { failed(); yield break; }
                    if (!moved) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                    if (current is IEnumerator nested) stack.Push(nested);
                    else yield return current;
                }
            }
            finally { while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose(); }
        }
    }
}
