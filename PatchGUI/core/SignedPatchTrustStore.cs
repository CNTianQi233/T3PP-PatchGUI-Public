using System;
using System.Collections.Generic;

namespace PatchGUI.Core
{
    public static class SignedPatchTrustStore
    {
        // SHA256 fingerprints (hex) of trusted publisher public keys.
        // Add official publisher fingerprints here for stable releases.
        private static readonly HashSet<string> TrustedPublisherFingerprints = new(StringComparer.OrdinalIgnoreCase)
        {
            // Example:
            // "0123456789ABCDEF...".
            "30BE046EE05B4F37452E9D813FD5A1D33C2440E8AB6AA52D31D0DAED5BBA8326"
        };

#if DEBUG
        // Debug builds can optionally trust additional dev/test publisher keys.
        private static readonly HashSet<string> DevTrustedPublisherFingerprints = new(StringComparer.OrdinalIgnoreCase)
        {
            // Put your dev publisher fingerprint(s) here when needed.
        };

        public static bool DevRootsEnabled { get; set; } = true;
#endif

        public static bool IsTrustedPublisherFingerprint(string? fingerprintHex)
        {
            if (string.IsNullOrWhiteSpace(fingerprintHex))
                return false;

            if (TrustedPublisherFingerprints.Contains(fingerprintHex))
                return true;

#if DEBUG
            if (DevRootsEnabled && DevTrustedPublisherFingerprints.Contains(fingerprintHex))
                return true;
#endif

            return false;
        }
    }
}
