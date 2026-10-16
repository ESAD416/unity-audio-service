using System.Collections.Generic;
using UnityEngine;

namespace Esad.Audio
{
    // Failure-only, bounded development logging. Successful playback does not
    // build diagnostic strings or query providers for diagnostic information.
    internal sealed class AudioFailureLog
    {
        /// <summary>Setup and failure warnings are development aids: the Editor and development builds only.</summary>
        public static bool Enabled => Application.isEditor || Debug.isDebugBuild;

        private const int Limit = 64;
        private readonly HashSet<(AudioCategory, string, string)> reported = new();
        private readonly HashSet<string> controlReasons = new();
        private bool limitReported;

        // Category controls have no sound ID; report each reason once without naming a category.
        public void ReportControl(AudioController controller, string reason)
        {
            if (controlReasons.Add(reason))
                Debug.LogWarning($"[AudioService] Control call ignored: {reason}.", controller);
        }

        public void Report(AudioController controller, AudioCategory category, AudioId id, string reason,
            ResolvedAudioClip? address = null, bool allowAsync = true)
        {
            var key = (category, id.Value, reason);
            if (reported.Contains(key)) return;
            if (reported.Count == Limit)
            {
                if (!limitReported)
                {
                    limitReported = true;
                    Debug.LogWarning("[AudioService] Further playback warnings are suppressed for this configuration. Inspect handles or Diagnostics.LastFailure; refresh the provider or re-enable the controller to reset warnings.", controller);
                }
                return;
            }
            reported.Add(key);
            string lookup = address.HasValue ? LookupHint(controller, address.Value, allowAsync) : string.Empty;
            Debug.LogWarning($"[AudioService] {category} '{id.Value}' failed: {reason}.{lookup}", controller);
        }

        // Only load failures carry the resolved address; describe where the configured provider looked.
        private static string LookupHint(AudioController controller, ResolvedAudioClip address, bool allowAsync)
        {
            var provider = controller != null ? controller.ClipProvider : null;
            string hint = " Configured lookup: " + DescribeProvider(provider, address)
                + ". Check the category, case-sensitive key and Catalog mapping. Resources keys omit the extension; Addressables keys must exist in the active catalog.";
            return allowAsync ? hint : hint + " AllowAsyncLoad=false: Addressables must already be resident; Resources may load synchronously.";
        }
        internal static string DescribeProvider(IAudioClipProvider provider, ResolvedAudioClip address)
        {
            if (!AudioValues.Alive(provider)) return "no live provider; connect AudioController's Clip Provider Source";
            if (provider is IAudioClipProviderDiagnostics diagnostics) return diagnostics.DescribeLookup(address);
            return provider.GetType().Name + " (custom provider; inspect its own lookup diagnostics)";
        }
    }
}
