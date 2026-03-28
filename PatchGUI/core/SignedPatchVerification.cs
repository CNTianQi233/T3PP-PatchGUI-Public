using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace PatchGUI.Core
{
    public enum SignedPatchVerificationState
    {
        Unsigned,
        Verified,
        Untrusted,
        Invalid
    }

    public enum SignedPatchCertificateValidity
    {
        Unknown,
        Valid,
        NotYetValid,
        Expired
    }

    public enum SignedPatchTargetCheck
    {
        NotChecked,
        Match,
        Mismatch,
        Error
    }

    public sealed class SignedPatchVerificationReport
    {
        public SignedPatchVerificationState State { get; init; }

        public string? FailureReason { get; init; }

        public bool HasSignatureBlock { get; init; }

        public bool SignatureValid { get; init; }

        public bool PatchPayloadHashValid { get; init; }

        public bool PublisherTrusted { get; init; }

        public SignedPatchCertificateValidity CertificateValidity { get; init; }

        public string? PublisherFingerprintSha256Hex { get; init; }

        public SignedPatchPublisherCertificateV1? Publisher { get; init; }

        public SignedPatchManifestV1? Manifest { get; init; }

        public SignedPatchTargetCheck TargetCheck { get; init; }

        public IReadOnlyList<string>? TargetIssues { get; init; }

        public bool HasLegacySecurityMark { get; init; }
    }

    public static class SignedPatchVerifier
    {
        public static (SignedPatchTargetCheck check, IReadOnlyList<string>? issues) VerifyTargetOnly(
            string? targetPath,
            SignedPatchManifestV1 manifest,
            IPatchLogger? logger)
            => VerifyTarget(targetPath, manifest, logger);

        public static SignedPatchVerificationReport Verify(
            string patchPath,
            string? targetPath,
            IPatchLogger? logger)
        {
            if (string.IsNullOrWhiteSpace(patchPath) || !File.Exists(patchPath))
            {
                return new SignedPatchVerificationReport
                {
                    State = SignedPatchVerificationState.Invalid,
                    FailureReason = "Patch file not found."
                };
            }

            var legacyMeta = PatchMetadataReader.Read(patchPath);
            bool legacyMark = legacyMeta?.IsSecurityPatch == true;

            if (!SignedPatchReader.TryRead(patchPath, out var read, out var readError) || read == null)
            {
                // No signature block (legacy patch).
                return new SignedPatchVerificationReport
                {
                    State = SignedPatchVerificationState.Unsigned,
                    HasSignatureBlock = false,
                    HasLegacySecurityMark = legacyMark
                };
            }

            var env = read.Envelope;
            var cert = env.Publisher;
            var manifest = env.Manifest;

            string? fingerprint;
            try
            {
                fingerprint = SignedPatchCrypto.ComputePublicKeyFingerprintHex(cert.PublicKey);
            }
            catch (Exception ex)
            {
                logger?.Error($"证书公钥解析失败：{ex.Message}");
                return new SignedPatchVerificationReport
                {
                    State = SignedPatchVerificationState.Invalid,
                    FailureReason = "Invalid publisher public key.",
                    HasSignatureBlock = true,
                    HasLegacySecurityMark = legacyMark
                };
            }

            bool trusted = SignedPatchTrustStore.IsTrustedPublisherFingerprint(fingerprint);

            SignedPatchCertificateValidity validity = GetValidity(cert);

            if (!string.Equals(env.SignatureAlgorithm, SignedPatchFormats.DefaultSignatureAlgorithm, StringComparison.OrdinalIgnoreCase))
            {
                return new SignedPatchVerificationReport
                {
                    State = SignedPatchVerificationState.Invalid,
                    FailureReason = $"Unsupported signature algorithm: {env.SignatureAlgorithm}",
                    HasSignatureBlock = true,
                    PublisherFingerprintSha256Hex = fingerprint,
                    PublisherTrusted = trusted,
                    CertificateValidity = validity,
                    Publisher = cert,
                    Manifest = manifest,
                    HasLegacySecurityMark = legacyMark
                };
            }

            byte[] sigBytes;
            try
            {
                sigBytes = Convert.FromBase64String(env.SignatureBase64);
            }
            catch
            {
                return new SignedPatchVerificationReport
                {
                    State = SignedPatchVerificationState.Invalid,
                    FailureReason = "Signature is not valid base64.",
                    HasSignatureBlock = true,
                    PublisherFingerprintSha256Hex = fingerprint,
                    PublisherTrusted = trusted,
                    CertificateValidity = validity,
                    Publisher = cert,
                    Manifest = manifest,
                    HasLegacySecurityMark = legacyMark
                };
            }

            bool sigValid;
            try
            {
                byte[] canonicalManifest = SignedPatchCrypto.CanonicalizeJsonToUtf8Bytes(manifest);
                using var ecdsa = SignedPatchCrypto.CreateEcdsaFromPublicKey(cert.PublicKey);
                sigValid = ecdsa.VerifyData(canonicalManifest, sigBytes, HashAlgorithmName.SHA256);
            }
            catch (Exception ex)
            {
                logger?.Error($"签名验证异常：{ex.Message}");
                sigValid = false;
            }

            if (!sigValid)
            {
                return new SignedPatchVerificationReport
                {
                    State = SignedPatchVerificationState.Invalid,
                    FailureReason = "Signature verification failed.",
                    HasSignatureBlock = true,
                    SignatureValid = false,
                    PublisherFingerprintSha256Hex = fingerprint,
                    PublisherTrusted = trusted,
                    CertificateValidity = validity,
                    Publisher = cert,
                    Manifest = manifest,
                    HasLegacySecurityMark = legacyMark
                };
            }

            bool payloadOk = false;
            try
            {
                byte[] payloadHash = SignedPatchCrypto.ComputeFileSha256(patchPath, read.PayloadLength);
                string payloadHex = Convert.ToHexString(payloadHash);
                payloadOk = string.Equals(payloadHex, manifest.PatchPayloadSha256Hex, StringComparison.OrdinalIgnoreCase);
                if (!payloadOk)
                {
                    logger?.Error($"补丁主体哈希不匹配：manifest={manifest.PatchPayloadSha256Hex} actual={payloadHex}");
                }
            }
            catch (Exception ex)
            {
                logger?.Error($"补丁主体哈希计算失败：{ex.Message}");
                payloadOk = false;
            }

            if (!payloadOk)
            {
                return new SignedPatchVerificationReport
                {
                    State = SignedPatchVerificationState.Invalid,
                    FailureReason = "Patch payload hash mismatch.",
                    HasSignatureBlock = true,
                    SignatureValid = true,
                    PatchPayloadHashValid = false,
                    PublisherFingerprintSha256Hex = fingerprint,
                    PublisherTrusted = trusted,
                    CertificateValidity = validity,
                    Publisher = cert,
                    Manifest = manifest,
                    HasLegacySecurityMark = legacyMark
                };
            }

            var (targetCheck, targetIssues) = VerifyTarget(targetPath, manifest, logger);

            SignedPatchVerificationState state = trusted && validity == SignedPatchCertificateValidity.Valid
                ? SignedPatchVerificationState.Verified
                : SignedPatchVerificationState.Untrusted;

            return new SignedPatchVerificationReport
            {
                State = state,
                HasSignatureBlock = true,
                SignatureValid = true,
                PatchPayloadHashValid = true,
                PublisherTrusted = trusted,
                CertificateValidity = validity,
                PublisherFingerprintSha256Hex = fingerprint,
                Publisher = cert,
                Manifest = manifest,
                TargetCheck = targetCheck,
                TargetIssues = targetIssues,
                HasLegacySecurityMark = legacyMark
            };
        }

        private static SignedPatchCertificateValidity GetValidity(SignedPatchPublisherCertificateV1 cert)
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                if (cert.NotBeforeUtc != default && now < cert.NotBeforeUtc)
                    return SignedPatchCertificateValidity.NotYetValid;
                if (cert.NotAfterUtc != default && now > cert.NotAfterUtc)
                    return SignedPatchCertificateValidity.Expired;
                return SignedPatchCertificateValidity.Valid;
            }
            catch
            {
                return SignedPatchCertificateValidity.Unknown;
            }
        }

        private static (SignedPatchTargetCheck check, IReadOnlyList<string>? issues) VerifyTarget(
            string? targetPath,
            SignedPatchManifestV1 manifest,
            IPatchLogger? logger)
        {
            if (string.IsNullOrWhiteSpace(targetPath))
                return (SignedPatchTargetCheck.NotChecked, null);

            try
            {
                if (manifest.Target.Type == SignedPatchMode.File)
                {
                    if (!File.Exists(targetPath))
                        return (SignedPatchTargetCheck.Error, new[] { "Target file not found." });

                    if (manifest.Target.File == null || string.IsNullOrWhiteSpace(manifest.Target.File.Sha256Hex))
                        return (SignedPatchTargetCheck.Error, new[] { "Manifest missing expected file hash." });

                    string expected = manifest.Target.File.Sha256Hex;
                    byte[] actualHash = SignedPatchCrypto.ComputeFileSha256(targetPath, new FileInfo(targetPath).Length);
                    string actual = Convert.ToHexString(actualHash);

                    if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                    {
                        logger?.Error($"目标文件哈希不匹配：expected={expected} actual={actual}");
                        return (SignedPatchTargetCheck.Mismatch, new[] { "Target file hash mismatch." });
                    }

                    return (SignedPatchTargetCheck.Match, null);
                }

                // Directory
                if (!Directory.Exists(targetPath))
                    return (SignedPatchTargetCheck.Error, new[] { "Target directory not found." });

                bool hasFileHashes = manifest.Target.Files is { Count: > 0 };
                bool hasNewFiles = manifest.Target.NewFiles is { Count: > 0 };
                if (!hasFileHashes && !hasNewFiles)
                    return (SignedPatchTargetCheck.Error, new[] { "Manifest missing expected directory checks." });

                var issues = new List<string>(capacity: 16);

                if (hasFileHashes)
                {
                    foreach (var item in manifest.Target.Files!)
                    {
                        if (string.IsNullOrWhiteSpace(item.RelativePath) || string.IsNullOrWhiteSpace(item.Sha256Hex))
                            continue;

                        string full = Path.Combine(targetPath, item.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                        if (!File.Exists(full))
                        {
                            issues.Add($"Missing: {item.RelativePath}");
                            continue;
                        }

                        byte[] actualHash = SignedPatchCrypto.ComputeFileSha256(full, new FileInfo(full).Length);
                        string actual = Convert.ToHexString(actualHash);
                        if (!string.Equals(item.Sha256Hex, actual, StringComparison.OrdinalIgnoreCase))
                        {
                            issues.Add($"Mismatch: {item.RelativePath}");
                        }
                    }
                }

                if (manifest.Target.NewFiles is { Count: > 0 })
                {
                    foreach (var rel in manifest.Target.NewFiles)
                    {
                        if (string.IsNullOrWhiteSpace(rel))
                            continue;

                        string full = Path.Combine(targetPath, rel.Replace('/', Path.DirectorySeparatorChar));
                        if (File.Exists(full))
                        {
                            issues.Add($"Unexpected existing (should be new): {rel}");
                        }
                    }
                }

                if (issues.Count > 0)
                    return (SignedPatchTargetCheck.Mismatch, issues);

                return (SignedPatchTargetCheck.Match, null);
            }
            catch (Exception ex)
            {
                logger?.Error($"目标校验失败：{ex.Message}");
                return (SignedPatchTargetCheck.Error, new[] { ex.Message });
            }
        }
    }
}
