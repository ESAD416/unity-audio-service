using System;
using System.Collections;
using UnityEngine;

namespace Controller.Audio
{
    /// <summary>
    /// Optional async extension: reports this load's result, rather than querying a
    /// cache that may now belong to a later load. Null means failed or released.
    /// The callback runs once when the enumerator finishes normally; abandoning
    /// the enumerator does not cancel or release a shared underlying load.
    /// </summary>
    public interface IResultAudioClipProvider : IAsyncAudioClipProvider
    {
        IEnumerator LoadClipAsync(AudioCategory category, string key, Action<AudioClip> completed);
    }
}
