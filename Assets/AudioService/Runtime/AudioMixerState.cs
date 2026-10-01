using System;
using UnityEngine;
using UnityEngine.Audio;

namespace Controller.Audio
{
    internal sealed class AudioMixerState
    {
        private readonly float[] volumes =
        {
            1,
            1,
            1,
            1
        };
        private readonly bool[] parameters = new bool[AudioMixerLayout.ChannelCount];
        private readonly AudioMixerGroup[] groups = new AudioMixerGroup[AudioMixerLayout.CategoryCount];
        private readonly Action<int> refreshGains;
        private readonly Action<AudioChannel, float> volumeChanged;
        private AudioMixer mixer;
        private IAudioSettingsHandler settings;
        private bool subscribed, applyingSettings;
        public bool Applied { get; private set; }
        public string LastIssue { get; private set; }

        public AudioMixerState(Action<int> refreshGains, Action<AudioChannel, float> volumeChanged)
        {
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

        public void BindSettings(IAudioSettingsHandler handler, bool active, bool applyStored)
        {
            DetachSettings();
            settings = handler;
            if (active)
                AttachSettings(applyStored);
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
        public float GetVolume(AudioChannel channel) => (int)channel >= 0 && (int)channel < 4 ? volumes[(int)channel] : 0;
        public void SetVolume(AudioChannel channel, float value, bool persist = true)
        {
            int index = (int)channel;
            if (index < 0 || index > 3)
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
                    int categories = 0;
                    if (bgm != SourceGain(AudioCategory.Bgm))
                        categories |= 1;
                    if (sfx != SourceGain(AudioCategory.Sfx))
                        categories |= 2;
                    if (voice != SourceGain(AudioCategory.Voice))
                        categories |= 4;
                    if (categories != 0)
                        refreshGains(categories);
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
            refreshGains(7);
        }

        public bool Validate()
        {
            LastIssue = null;
            if (mixer == null)
                return true;
            for (int i = 0; i < parameters.Length; i++)
                if (!mixer.GetFloat(AudioMixerLayout.Parameter(i), out _))
                    LastIssue = "Missing exposed parameter: " + AudioMixerLayout.Parameter(i);
            if (groups[0] == null || groups[1] == null || groups[2] == null)
                LastIssue = "Missing BGM, Sound or Voice mixer group";
            return LastIssue == null;
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
            int channel = (int)category + 1;
            bool routed = mixer != null && groups[(int)category] != null;
            return (routed && parameters[0] ? 1 : volumes[0]) * (routed && parameters[channel] ? 1 : volumes[channel]);
        }
    }
}
