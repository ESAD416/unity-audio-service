namespace Controller.Audio
{
    internal static class AudioMixerLayout
    {
        private static readonly string[] Parameters = { "masterVolume", "bgmVolume", "soundVolume", "voiceVolume" };
        private static readonly string[] Groups = { "BGM", "Sound", "Voice" };

        public static int ChannelCount => Parameters.Length;
        public static int CategoryCount => Groups.Length;
        public static string Parameter(int channel) => Parameters[channel];
        public static string Group(int category) => Groups[category];
    }
}
