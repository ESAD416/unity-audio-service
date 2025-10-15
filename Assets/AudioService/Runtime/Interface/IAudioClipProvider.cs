using System.Collections;
using UnityEngine;

namespace Controller.Audio
{
    public enum AudioCategory
    {
        Bgm,
        Sfx,
        Voice
    }

    public interface IAudioClipProvider
    {
        AudioClip GetClip(AudioCategory category, string key);
        bool TryGetClip(AudioCategory category, string key, out AudioClip clip);
    }

    public interface IAsyncAudioClipProvider : IAudioClipProvider
    {
        bool IsLoading(AudioCategory category, string key);
        bool IsCached(AudioCategory category, string key);
        IEnumerator LoadClipAsync(AudioCategory category, string key);
        void ReleaseClip(AudioCategory category, string key);
    }
}
