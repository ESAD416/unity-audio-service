using System.Collections.Generic;
using UnityEngine;

namespace Controller.Audio
{
    // Failure-only, bounded development logging. Successful playback does not
    // build diagnostic strings or query providers for diagnostic information.
    internal sealed class AudioFailureLog
    {
        private const int Limit = 64;
        private readonly HashSet<(AudioCategory, string, string)> reported = new();
        private bool limitReported;
        public void Report(AudioController controller, AudioHandle handle, string reason,
            AudioClipAddress address = null, bool allowAsync = true)
        {
            var key = (handle.Category, handle.AudioId.Value, reason);
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
            string lookup = address == null ? string.Empty
                : " Configured lookup: " + DescribeProvider(controller != null ? controller.ClipProvider : null, address)
                  + ". Check the category, case-sensitive key and Catalog mapping. Resources keys omit the extension; Addressables keys must exist in the active catalog."
                  + (allowAsync ? string.Empty : " AllowAsyncLoad=false: Addressables must already be resident; Resources may load synchronously.");
            Debug.LogWarning($"[AudioService] {handle.Category} '{handle.AudioId.Value}' failed: {reason}.{lookup}", controller);
        }
        internal static string DescribeProvider(IAudioClipProvider provider, AudioClipAddress address, int depth = 0)
        {
            if (!AudioValues.Alive(provider)) return "no live provider; connect AudioBootstrap's Clip Provider Source";
            if (depth >= 4) return "nested fallback (inspect provider configuration)";
            if (provider is ResourcesAudioClipProvider resources) return resources.DescribeLookup(address);
            if (provider is AddressablesAudioClipProvider)
            {
                string key = address.AddressablesKey ?? address.Id;
                return string.IsNullOrWhiteSpace(key) ? "Addressables disabled (empty key)" : $"Addressables key='{key}'";
            }
            if (provider is FallbackAudioClipProvider fallback) return fallback.DescribeLookup(address, depth);
            return provider.GetType().Name + " (custom provider; inspect its own lookup diagnostics)";
        }
    }
}
