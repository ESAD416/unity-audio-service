using System;
using UnityEngine;

namespace Controller.Audio
{
    [Serializable]
    public struct AudioId : IEquatable<AudioId>
    {
        [SerializeField]
        private string value;
        public string Value => value ?? string.Empty;

        public AudioId(string value)
        {
            this.value = value?.Trim() ?? string.Empty;
        }

        public bool Equals(AudioId other) => StringComparer.Ordinal.Equals(Value, other.Value);
        public override bool Equals(object obj) => obj is AudioId other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value;
        public static implicit operator AudioId(string value) => new AudioId(value);
    }

    public enum AudioPlaybackState
    {
        Loading,
        Playing,
        Paused,
        Finished
    }

    public enum AudioCompletion
    {
        None,
        Completed,
        Stopped,
        Cancelled,
        Failed,
        Rejected
    }

    public enum AudioConcurrencyPolicy
    {
        RejectNew,
        StealOldest
    }

    public enum AudioFallbackPolicy
    {
        PreferAvailable,
        PrimaryThenBackup
    }

    [Serializable]
    public sealed class PlayOptions
    {
        public bool Loop;
        public float Volume = 1f;
        public float Pitch = 1f;
        public float FadeInSeconds;
        public float FadeOutSeconds;
        public bool IgnoreGamePause;
        public bool AllowAsyncLoad = true;
        public int MaxInstances;
        public AudioConcurrencyPolicy ConcurrencyPolicy;
    }

    // An accepted request owns its settings, even when the caller later mutates
    // the public options or a provider release reenters the service.
    internal readonly struct PlaybackOptions
    {
        public readonly bool Loop, IgnoreGamePause, AllowAsyncLoad;
        public readonly float Volume, Pitch, FadeInSeconds, FadeOutSeconds;
        public PlaybackOptions(PlayOptions source, bool forceLoop = false)
        {
            Loop = forceLoop || (source?.Loop ?? false);
            Volume = AudioValues.Unit(source?.Volume ?? 1f);
            Pitch = AudioValues.Pitch(source?.Pitch ?? 1f);
            FadeInSeconds = AudioValues.Seconds(source?.FadeInSeconds ?? 0f);
            FadeOutSeconds = AudioValues.Seconds(source?.FadeOutSeconds ?? 0f);
            IgnoreGamePause = source?.IgnoreGamePause ?? false;
            AllowAsyncLoad = source?.AllowAsyncLoad ?? true;
        }
    }

    public sealed class AudioHandle
    {
        private Action<AudioHandle> completed;
        internal AudioPlaybackEngine Owner;
        internal AudioPlaybackEngine.Playback Playback;
        internal bool UserPaused;
        public long Id { get; internal set; }
        public AudioId AudioId { get; internal set; }
        public AudioCategory Category { get; internal set; }
        public AudioPlaybackState State { get; internal set; } = AudioPlaybackState.Loading;
        public AudioCompletion Result { get; private set; }
        public string FailureReason { get; private set; }
        public bool IsValid => Owner != null && State != AudioPlaybackState.Finished;
        public bool IsFinished => State == AudioPlaybackState.Finished;

        // A late subscription is immediately called with the retained terminal result.
        public event Action<AudioHandle> Completed
        {
            add
            {
                if (value == null)
                    return;
                if (IsFinished)
                    AudioCallbacks.Broadcast(value, this);
                else
                    completed += value;
            }

            remove
            {
                completed -= value;
            }
        }

        public bool Stop(float fadeOutSeconds = 0f) => Owner != null && Owner.Stop(this, AudioValues.Seconds(fadeOutSeconds));
        public bool Pause() => Owner != null && Owner.Pause(this, true);
        public bool Resume() => Owner != null && Owner.Pause(this, false);
        internal void Finish(AudioCompletion result, string reason = null)
        {
            if (IsFinished)
                return;
            var owner = Owner;
            State = AudioPlaybackState.Finished;
            Result = result;
            FailureReason = reason;
            Owner = null;
            Playback = null;
            var subscribers = completed;
            completed = null;
            if (subscribers == null)
                return;
            DispatchCompleted(owner, subscribers);
        }

        private void DispatchCompleted(AudioPlaybackEngine owner, Action<AudioHandle> subscribers)
        {
            // Keep the notification closure out of the common no-subscriber path.
            void Notify() => AudioCallbacks.Broadcast(subscribers, this);

            if (owner != null)
                owner.NotifyCompleted(Notify);
            else
                Notify();
        }

    }

    public static class AudioValues
    {
        public static float Unit(float value, float fallback = 0f) => float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp01(value);
        public static float Seconds(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Max(0f, value);
        public static float Pitch(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 1f : Mathf.Clamp(value, .01f, 3f);
        public static float Decibels(float value) => Mathf.Max(-80f, 20f * Mathf.Log10(Mathf.Max(.0001f, Unit(value))));
        internal static bool Alive(object value) => value != null && (!(value is UnityEngine.Object obj) || obj != null);
    }

    /// <summary>Read-only controls for one channel, before Master, listener and mixer effects are combined.</summary>
    public readonly struct AudioChannelDiagnostics
    {
        public readonly float Volume, FadeGain;
        public readonly bool Muted;
        internal AudioChannelDiagnostics(float volume, float fadeGain, bool muted)
        {
            Volume = volume;
            FadeGain = fadeGain;
            Muted = muted;
        }
    }

    public readonly struct AudioDiagnostics
    {
        public readonly int Playing, Paused, Loading, PooledSources, CreatedSources, CachedClips, ClipUsers, PendingLoads, PreparingClips;
        public readonly string LastFailure;
        public readonly bool GamePaused, BackgroundPaused;
        internal AudioDiagnostics(int playing, int paused, int loading, int pooled, int created, int cached, int users, int pending, string failure, int preparing = 0, bool gamePaused = false, bool backgroundPaused = false)
        {
            Playing = playing;
            Paused = paused;
            Loading = loading;
            PooledSources = pooled;
            CreatedSources = created;
            CachedClips = cached;
            ClipUsers = users;
            PendingLoads = pending;
            LastFailure = failure;
            PreparingClips = preparing;
            GamePaused = gamePaused;
            BackgroundPaused = backgroundPaused;
        }
    }
}
