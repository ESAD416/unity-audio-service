using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Controller.Audio.Editor
{
    /// <summary>Explicit, optional import review and default profiles. Never runs on automatic import.</summary>
    public static class AudioImportTools
    {
        private const string Menu = "Tools/Audio Service/Audio Imports/";

        public static IReadOnlyList<string> GetIssues(AudioClip clip, AudioImporter importer, string platform = null)
        {
            var issues = new List<string>();
            if (clip == null || importer == null) return issues;
            bool overridden = !string.IsNullOrEmpty(platform) && importer.ContainsSampleSettingsOverride(platform);
            var settings = overridden ? importer.GetOverrideSampleSettings(platform) : importer.defaultSampleSettings;
            string scope = overridden ? platform : "Default";
            // A PCM estimate identifies candidates, not measured native audio memory.
            long pcmEstimate = (long)clip.samples * clip.channels * sizeof(float);
            bool large = clip.length >= 30f || pcmEstimate >= 8 * 1024 * 1024;
            if (large && settings.loadType == AudioClipLoadType.DecompressOnLoad)
            {
                issues.Add($"{scope}: long/large clip uses Decompress On Load. Estimated float PCM: {pcmEstimate / (1024f * 1024f):F1} MiB. Compare Streaming on the target platform.");
                if (!settings.preloadAudioData && !importer.loadInBackground)
                    issues.Add($"{scope}: the first Play or PrepareClip may synchronously load/decompress audio. Prepare during loading or compare Load In Background.");
            }
            if (overridden) issues.Add($"{platform} overrides Default. The optional profiles below change Default only; review this override separately.");
            return issues;
        }

        [MenuItem(Menu + "Review Selected Clips")]
        private static void ReviewSelected()
        {
            string platform = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget).ToString();
            foreach (var clip in Selection.GetFiltered<AudioClip>(SelectionMode.DeepAssets))
            {
                var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip)) as AudioImporter;
                var issues = GetIssues(clip, importer, platform);
                if (issues.Count == 0) Debug.Log($"[Audio Imports] {clip.name}: no loading-cost hints for {platform}. Validate playback on the target device.", clip);
                foreach (string issue in issues) Debug.LogWarning($"[Audio Imports] {clip.name}: {issue}", clip);
            }
        }

        public static void ApplyDefaultProfile(AudioImporter importer, AudioClipLoadType loadType)
        {
            var settings = importer.defaultSampleSettings;
            settings.loadType = loadType; settings.preloadAudioData = false;
            importer.defaultSampleSettings = settings; importer.loadInBackground = true;
        }

        private static void ApplySelected(AudioClipLoadType loadType)
        {
            foreach (var clip in Selection.GetFiltered<AudioClip>(SelectionMode.DeepAssets))
            {
                string path = AssetDatabase.GetAssetPath(clip);
                if (!path.StartsWith("Assets/", System.StringComparison.Ordinal)) continue;
                var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                if (importer == null) continue;
                Undo.RecordObject(importer, "Audio import profile");
                ApplyDefaultProfile(importer, loadType); importer.SaveAndReimport();
                Debug.Log($"[Audio Imports] {path}: Default = {loadType}, background enabled, preload disabled. Compression, quality, sample rate and platform overrides preserved; review overrides and call PrepareClip before playback.", clip);
            }
        }
        [MenuItem(Menu + "Apply Streaming to Selected (Default Only)")]
        private static void ApplyStreaming() => ApplySelected(AudioClipLoadType.Streaming);
        [MenuItem(Menu + "Apply Background Decompression to Selected (Default Only)")]
        private static void ApplyBackground() => ApplySelected(AudioClipLoadType.DecompressOnLoad);
        [MenuItem(Menu + "Review Selected Clips", true)]
        [MenuItem(Menu + "Apply Streaming to Selected (Default Only)", true)]
        [MenuItem(Menu + "Apply Background Decompression to Selected (Default Only)", true)]
        private static bool HasSelectedClips() => !EditorApplication.isPlayingOrWillChangePlaymode && Selection.GetFiltered<AudioClip>(SelectionMode.DeepAssets).Length > 0;
    }
}
