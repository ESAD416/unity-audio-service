using System;

namespace Controller.Audio
{
    /// <summary>
    /// Defines a push-based contract for supplying and persisting normalized audio volumes.
    /// </summary>
    public interface IAudioSettingsHandler
    {
        /// <summary>Current persisted master channel volume (0-1).</summary>
        float MasterVolume { get; }

        /// <summary>Current persisted BGM channel volume (0-1).</summary>
        float BgmVolume { get; }

        /// <summary>Current persisted SFX channel volume (0-1).</summary>
        float SfxVolume { get; }

        /// <summary>Current persisted voice channel volume (0-1).</summary>
        float VoiceVolume { get; }

        /// <summary>
        /// Triggered when a volume changes. Implementations should only fire when the effective value differs.
        /// </summary>
        event Action<AudioChannel, float> VolumeChanged;

        /// <summary>
        /// Persists the specified normalized volume and emits <see cref="VolumeChanged"/> when appropriate.
        /// </summary>
        void UpdateVolume(AudioChannel channel, float normalizedVolume);
    }

    public enum AudioChannel
    {
        Master,
        Bgm,
        Sfx,
        Voice
    }
}
