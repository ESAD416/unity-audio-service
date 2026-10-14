using System;
using UnityEngine;
using UnityEngine.Audio;

namespace Controller.Audio
{
    internal sealed class AudioMixerState
    {
        private readonly float[] volumes = new float[AudioMixerLayout.ChannelCount];
        private readonly bool[] parameters = new bool[AudioMixerLayout.ChannelCount];
        private readonly AudioMixerGroup[] groups = new AudioMixerGroup[AudioMixerLayout.CategoryCount];
        private readonly Action<CategoryMask> refreshGains;
        private readonly Action<AudioChannel, float> volumeChanged;
        private AudioMixer mixer;
        private IAudioSettingsHandler settings;
        private bool subscribed, applyingSettings;
        public bool Applied { get; private set; }
        public string LastIssue { get; private set; }

        public AudioMixerState(Action<CategoryMask> refreshGains, Action<AudioChannel, float> volumeChanged)
        {
            Array.Fill(volumes, 1f);
            this.refreshGains = refreshGains;
            this.volumeChanged = volumeChanged;
        }

        public void Configure(AudioMixer value)
        {
            mixer = value;
            Applied = false;
            Array.Clear(parameters, 0, parameters.Length);
            Array.Clear(groups, 0, groups.Length);
            if (mixer == null)
                return;
            foreach (var group in mixer.FindMatchingGroups(string.Empty))
                for (int i = 0; i < groups.Length; i++)
                    if (group.name == AudioMixerLayout.Group(i))
                        groups[i] = group;
        }

        /// <summary>Stop following the current settings source and use another; AttachSettings starts following it.</summary>
        public void ReplaceSettings(IAudioSettingsHandler handler)
        {
            DetachSettings();
            settings = handler;
        }

        public void UnbindSettings(IAudioSettingsHandler handler)
        {
            if (ReferenceEquals(settings, handler))
            {
                DetachSettings();
                settings = null;
            }
        }

        public void AttachSettings(bool applyStored = true)
        {
            if (subscribed || !AudioValues.Alive(settings))
                return;
            settings.VolumeChanged += SettingsChanged;
            subscribed = true;
            if (!applyStored)
                return;
            SettingsChanged(AudioChannel.Master, settings.MasterVolume);
            SettingsChanged(AudioChannel.Bgm, settings.BgmVolume);
            SettingsChanged(AudioChannel.Sfx, settings.SfxVolume);
            SettingsChanged(AudioChannel.Voice, settings.VoiceVolume);
        }

        public void DetachSettings()
        {
            if (subscribed)
                settings.VolumeChanged -= SettingsChanged;
            subscribed = false;
        }

        private void SettingsChanged(AudioChannel channel, float value) => SetVolume(channel, value, false);
        public float GetVolume(AudioChannel channel) => AudioValues.IsValidChannel(channel) ? volumes[(int)channel] : 0;
        public void SetVolume(AudioChannel channel, float value, bool persist = true)
        {
            int index = (int)channel;
            if (!AudioValues.IsValidChannel(channel))
                return;
            bool previous = applyingSettings;
            applyingSettings |= !persist;
            try
            {
                value = AudioValues.Unit(value);
                bool changed = volumes[index] != value;
                if (changed)
                {
                    float bgm = SourceGain(AudioCategory.Bgm);
                    float sfx = SourceGain(AudioCategory.Sfx);
                    float voice = SourceGain(AudioCategory.Voice);
                    volumes[index] = value;
                    if (Applied)
                        ApplyParameter(index);
                    // A mixer-applied volume leaves source gains unchanged; refresh only moved categories.
                    var moved = CategoryMask.None;
                    if (bgm != SourceGain(AudioCategory.Bgm))
                        moved |= CategoryMask.Bgm;
                    if (sfx != SourceGain(AudioCategory.Sfx))
                        moved |= CategoryMask.Sfx;
                    if (voice != SourceGain(AudioCategory.Voice))
                        moved |= CategoryMask.Voice;
                    if (moved != CategoryMask.None)
                        refreshGains(moved);
                }

                // Persist even an unchanged value after a temporary, nonpersistent override.
                if (!applyingSettings && AudioValues.Alive(settings))
                    settings.UpdateVolume(channel, value);
                if (changed)
                    volumeChanged(channel, value);
            }
            finally
            {
                applyingSettings = previous;
            }
        }

        private void ApplyParameter(int index) => parameters[index] = mixer != null && mixer.SetFloat(AudioMixerLayout.Parameter(index), AudioValues.Decibels(volumes[index]));
        public void Apply()
        {
            Applied = true;
            for (int i = 0; i < parameters.Length; i++)
                ApplyParameter(i);
            refreshGains(CategoryMask.All);
        }

        public bool Validate()
        {
            LastIssue = FindIssue();
            return LastIssue == null;
        }

        // Report the first problem; the Editor audio checks list every missing item.
        private string FindIssue()
        {
            if (mixer == null)
                return null;
            for (int i = 0; i < parameters.Length; i++)
                if (!mixer.GetFloat(AudioMixerLayout.Parameter(i), out _))
                    return "Missing exposed parameter: " + AudioMixerLayout.Parameter(i);
            return Array.Exists(groups, group => group == null) ? "Missing BGM, Sound or Voice mixer group" : null;
        }

        public void ConfigureSource(AudioSource source, AudioCategory category)
        {
            source.playOnAwake = false;
            source.spatialBlend = 0;
            source.mute = false;
            source.outputAudioMixerGroup = groups[(int)category];
        }

        public float SourceGain(AudioCategory category)
        {
            bool routed = mixer != null && groups[(int)category] != null;
            // A routed source leaves each volume the mixer already applies through its exposed parameter.
            float Volume(AudioChannel channel) => routed && parameters[(int)channel] ? 1 : volumes[(int)channel];
            return Volume(AudioChannel.Master) * Volume(AudioMixerLayout.ChannelOf(category));
        }
    }
}
