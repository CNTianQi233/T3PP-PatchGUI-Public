using System;

namespace PatchGUI.Core
{
    public static class SignedPatchFormats
    {
        public const string FooterMagic = "T3PPSIG1"; // 8 bytes ASCII

        public const string EnvelopeFormat = "t3pp-signed-patch";
        public const string PublisherCertificateFormat = "t3pp-publisher-cert";
        public const string ManifestFormat = "t3pp-patch-manifest";
        public const string PrivateKeyFormat = "t3pp-private-key";

        public const string DefaultSignatureAlgorithm = "ECDSA-P256-SHA256";

        public const int MaxSignatureBlockBytes = 2 * 1024 * 1024; // 2 MiB safety cap

        public static readonly ReadOnlyMemory<byte> FooterMagicBytes
            = System.Text.Encoding.ASCII.GetBytes(FooterMagic);
    }
}
