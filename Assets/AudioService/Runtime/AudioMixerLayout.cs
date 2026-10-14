using System;

namespace Controller.Audio
{
    /// <summary>Categories whose sources need a gain refresh.</summary>
    [Flags]
    internal enum CategoryMask
    {
        None = 0,
        Bgm = 1 << (int)AudioCategory.Bgm,
        Sfx = 1 << (int)AudioCategory.Sfx,
        Voice = 1 << (int)AudioCategory.Voice,
        All = Bgm | Sfx | Voice
    }

    // The bus layout is Master followed by one bus per AudioCategory, in enum order.
    // AudioChannel mirrors that order; this class is the only place that relies on it.
    internal static class AudioMixerLayout
    {
        private static readonly string[] Parameters = { "masterVolume", "bgmVolume", "soundVolume", "voiceVolume" };
        private static readonly string[] Groups = { "BGM", "Sound", "Voice" };

        public static readonly AudioChannel[] Channels = { AudioChannel.Master, AudioChannel.Bgm, AudioChannel.Sfx, AudioChannel.Voice };
        public static int ChannelCount => Parameters.Length;
        public static int CategoryCount => Groups.Length;
        public static string Parameter(int channel) => Parameters[channel];
        public static string Group(int category) => Groups[category];
        public static AudioChannel ChannelOf(AudioCategory category) => (AudioChannel)((int)category + 1);
        public static CategoryMask MaskOf(AudioCategory category) => (CategoryMask)(1 << (int)category);
        public static CategoryMask MaskOf(AudioChannel channel)
            => channel == AudioChannel.Master ? CategoryMask.All : MaskOf((AudioCategory)((int)channel - 1));
    }
}
