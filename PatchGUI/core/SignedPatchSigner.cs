// This file is responsible for generating signing keys/certificates and appending an offline signature footer to .t3pp patch files.
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace PatchGUI.Core
{
    public static class SignedPatchSigner
    {
        public static (SignedPatchPrivateKeyV1 PrivateKey, SignedPatchEcdsaPublicKey PublicKey, string FingerprintSha256Hex) GenerateKeyPair()
        {
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var p = ecdsa.ExportParameters(includePrivateParameters: true);
            if (p.D == null)
                throw new CryptographicException("Failed to export private key parameters.");

            var pub = new SignedPatchEcdsaPublicKey
            {
                Curve = "P-256",
                X = SignedPatchCrypto.Base64UrlEncode(p.Q.X),
                Y = SignedPatchCrypto.Base64UrlEncode(p.Q.Y)
            };

            var priv = new SignedPatchPrivateKeyV1
            {
                PublicKey = pub,
                D = SignedPatchCrypto.Base64UrlEncode(p.D)
            };

            string fp = SignedPatchCrypto.ComputePublicKeyFingerprintHex(pub);
            return (priv, pub, fp);
        }

        public static SignedPatchPrivateKeyV1 LoadPrivateKey(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentNullException(nameof(path));

            byte[] data = File.ReadAllBytes(path);
            var key = JsonSerializer.Deserialize<SignedPatchPrivateKeyV1>(data, SignedPatchCrypto.JsonOptions);
            if (key == null || !string.Equals(key.Format, SignedPatchFormats.PrivateKeyFormat, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Invalid private key format.");

            return key;
        }

        public static void SavePrivateKey(string path, SignedPatchPrivateKeyV1 key)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentNullException(nameof(path));
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            byte[] data = JsonSerializer.SerializeToUtf8Bytes(key, SignedPatchCrypto.JsonOptions);
            File.WriteAllBytes(path, data);
        }

        public static SignedPatchPublisherCertificateV1 LoadPublisherCertificate(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentNullException(nameof(path));

            byte[] data = File.ReadAllBytes(path);
            var cert = JsonSerializer.Deserialize<SignedPatchPublisherCertificateV1>(data, SignedPatchCrypto.JsonOptions);
            if (cert == null || !string.Equals(cert.Format, SignedPatchFormats.PublisherCertificateFormat, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Invalid publisher certificate format.");

            return cert;
        }

        public static void SavePublisherCertificate(string path, SignedPatchPublisherCertificateV1 cert)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentNullException(nameof(path));
            if (cert == null)
                throw new ArgumentNullException(nameof(cert));

            byte[] data = JsonSerializer.SerializeToUtf8Bytes(cert, SignedPatchCrypto.JsonOptions);
            File.WriteAllBytes(path, data);
        }

        public static void SignPatchFile(
            string inputPatchPath,
            string outputPatchPath,
            SignedPatchPublisherCertificateV1 publisherCertificate,
            SignedPatchPrivateKeyV1 privateKey,
            SignedPatchManifestV1 manifestTemplate,
            IPatchLogger? logger)
        {
            if (string.IsNullOrWhiteSpace(inputPatchPath))
                throw new ArgumentNullException(nameof(inputPatchPath));
            if (string.IsNullOrWhiteSpace(outputPatchPath))
                throw new ArgumentNullException(nameof(outputPatchPath));
            if (publisherCertificate == null)
                throw new ArgumentNullException(nameof(publisherCertificate));
            if (privateKey == null)
                throw new ArgumentNullException(nameof(privateKey));
            if (manifestTemplate == null)
                throw new ArgumentNullException(nameof(manifestTemplate));

            inputPatchPath = Path.GetFullPath(inputPatchPath);
            outputPatchPath = Path.GetFullPath(outputPatchPath);

            if (!File.Exists(inputPatchPath))
                throw new FileNotFoundException("Patch file not found.", inputPatchPath);

            if (string.Equals(inputPatchPath, outputPatchPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Output file path must be different from input patch file.");

            if (SignedPatchReader.TryRead(inputPatchPath, out _, out _))
                throw new InvalidOperationException("Patch already contains a signature block.");

            string pubFp = SignedPatchCrypto.ComputePublicKeyFingerprintHex(publisherCertificate.PublicKey);
            string privFp = SignedPatchCrypto.ComputePublicKeyFingerprintHex(privateKey.PublicKey);
            if (!string.Equals(pubFp, privFp, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Private key does not match the publisher certificate public key.");

            var fi = new FileInfo(inputPatchPath);
            byte[] payloadHash = SignedPatchCrypto.ComputeFileSha256(inputPatchPath, fi.Length);
            string payloadHex = Convert.ToHexString(payloadHash);

            var manifest = new SignedPatchManifestV1
            {
                PatchMode = manifestTemplate.PatchMode,
                PatchPayloadSha256Hex = payloadHex,
                CreatedAtUtc = manifestTemplate.CreatedAtUtc == default ? DateTimeOffset.UtcNow : manifestTemplate.CreatedAtUtc,
                Notes = manifestTemplate.Notes,
                Target = manifestTemplate.Target
            };

            byte[] canonicalManifest = SignedPatchCrypto.CanonicalizeJsonToUtf8Bytes(manifest);
            byte[] sig = SignManifest(privateKey, canonicalManifest);

            var envelope = new SignedPatchEnvelopeV1
            {
                Publisher = publisherCertificate,
                Manifest = manifest,
                SignatureAlgorithm = SignedPatchFormats.DefaultSignatureAlgorithm,
                SignatureBase64 = Convert.ToBase64String(sig)
            };

            byte[] block = JsonSerializer.SerializeToUtf8Bytes(envelope, SignedPatchCrypto.JsonOptions);
            if (block.Length <= 0 || block.Length > SignedPatchFormats.MaxSignatureBlockBytes)
                throw new InvalidOperationException("Signature block too large.");

            logger?.Info($"签名：patchPayloadSha256={payloadHex}");
            logger?.Info($"签名：publisherFingerprintSha256={pubFp}");
            logger?.Info($"签名：signatureBlockBytes={block.Length}");

            Directory.CreateDirectory(Path.GetDirectoryName(outputPatchPath)!);
            using var input = new FileStream(inputPatchPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var output = new FileStream(outputPatchPath, FileMode.Create, FileAccess.Write, FileShare.None);

            input.CopyTo(output);
            output.Write(block, 0, block.Length);

            Span<byte> footer = stackalloc byte[12];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(footer[..4], block.Length);
            SignedPatchFormats.FooterMagicBytes.Span.CopyTo(footer[4..]);
            output.Write(footer);

            output.Flush(flushToDisk: true);
        }

        private static byte[] SignManifest(SignedPatchPrivateKeyV1 privateKey, byte[] canonicalManifest)
        {
            byte[] x = SignedPatchCrypto.Base64UrlDecode(privateKey.PublicKey.X);
            byte[] y = SignedPatchCrypto.Base64UrlDecode(privateKey.PublicKey.Y);
            byte[] d = SignedPatchCrypto.Base64UrlDecode(privateKey.D);

            var p = new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = x, Y = y },
                D = d
            };

            using var ecdsa = ECDsa.Create(p);
            return ecdsa.SignData(canonicalManifest, HashAlgorithmName.SHA256);
        }
    }
}

