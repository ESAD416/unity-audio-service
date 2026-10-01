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
            ResolvedAudioClip? address = null, bool allowAsync = true)
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
                : " Configured lookup: " + DescribeProvider(controller != null ? controller.ClipProvider : null, address.Value)
                  + ". Check the category, case-sensitive key and Catalog mapping. Resources keys omit the extension; Addressables keys must exist in the active catalog."
                  + (allowAsync ? string.Empty : " AllowAsyncLoad=false: Addressables must already be resident; Resources may load synchronously.");
            Debug.LogWarning($"[AudioService] {handle.Category} '{handle.AudioId.Value}' failed: {reason}.{lookup}", controller);
        }
        internal static string DescribeProvider(IAudioClipProvider provider, ResolvedAudioClip address)
        {
            if (!AudioValues.Alive(provider)) return "no live provider; connect AudioController's Clip Provider Source";
            if (provider is IAudioClipProviderDiagnostics diagnostics) return diagnostics.DescribeLookup(address);
            return provider.GetType().Name + " (custom provider; inspect its own lookup diagnostics)";
        }
    }
}
