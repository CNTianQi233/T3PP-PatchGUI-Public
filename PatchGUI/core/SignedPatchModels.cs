using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PatchGUI.Core
{
    public enum SignedPatchMode
    {
        Directory,
        File
    }

    public sealed class SignedPatchEcdsaPublicKey
    {
        [JsonPropertyName("crv")]
        public string Curve { get; init; } = "P-256";

        // Base64Url without padding.
        [JsonPropertyName("x")]
        public string X { get; init; } = string.Empty;

        // Base64Url without padding.
        [JsonPropertyName("y")]
        public string Y { get; init; } = string.Empty;
    }

    public sealed class SignedPatchPublisherCertificateV1
    {
        [JsonPropertyName("format")]
        public string Format { get; init; } = SignedPatchFormats.PublisherCertificateFormat;

        [JsonPropertyName("version")]
        public int Version { get; init; } = 1;

        [JsonPropertyName("source")]
        public string Source { get; init; } = string.Empty;

        [JsonPropertyName("distributor")]
        public string Distributor { get; init; } = string.Empty;

        [JsonPropertyName("serial")]
        public string SerialNumber { get; init; } = string.Empty;

        [JsonPropertyName("notBeforeUtc")]
        public DateTimeOffset NotBeforeUtc { get; init; }

        [JsonPropertyName("notAfterUtc")]
        public DateTimeOffset NotAfterUtc { get; init; }

        [JsonPropertyName("usage")]
        public string Usage { get; init; } = "PatchSigning";

        [JsonPropertyName("publicKey")]
        public SignedPatchEcdsaPublicKey PublicKey { get; init; } = new();
    }

    public sealed class SignedPatchTargetFileV1
    {
        [JsonPropertyName("sha256")]
        public string Sha256Hex { get; init; } = string.Empty;

        [JsonPropertyName("size")]
        public long? Size { get; init; }
    }

    public sealed class SignedPatchTargetDirectoryFileV1
    {
        [JsonPropertyName("path")]
        public string RelativePath { get; init; } = string.Empty;

        [JsonPropertyName("sha256")]
        public string Sha256Hex { get; init; } = string.Empty;

        [JsonPropertyName("size")]
        public long? Size { get; init; }
    }

    public sealed class SignedPatchTargetV1
    {
        [JsonPropertyName("type")]
        public SignedPatchMode Type { get; init; }

        // File mode
        [JsonPropertyName("file")]
        public SignedPatchTargetFileV1? File { get; init; }

        // Directory mode
        [JsonPropertyName("files")]
        public List<SignedPatchTargetDirectoryFileV1>? Files { get; init; }

        // Directory mode: files that did not exist before patch (record-only)
        [JsonPropertyName("newFiles")]
        public List<string>? NewFiles { get; init; }
    }

    public sealed class SignedPatchManifestV1
    {
        [JsonPropertyName("format")]
        public string Format { get; init; } = SignedPatchFormats.ManifestFormat;

        [JsonPropertyName("version")]
        public int Version { get; init; } = 1;

        [JsonPropertyName("patchMode")]
        public SignedPatchMode PatchMode { get; init; }

        // SHA256 hex of the patch payload bytes (the original patch data, excluding the signature footer).
        [JsonPropertyName("patchPayloadSha256")]
        public string PatchPayloadSha256Hex { get; init; } = string.Empty;

        [JsonPropertyName("createdAtUtc")]
        public DateTimeOffset CreatedAtUtc { get; init; }

        [JsonPropertyName("notes")]
        public string? Notes { get; init; }

        [JsonPropertyName("target")]
        public SignedPatchTargetV1 Target { get; init; } = new();
    }

    public sealed class SignedPatchEnvelopeV1
    {
        [JsonPropertyName("format")]
        public string Format { get; init; } = SignedPatchFormats.EnvelopeFormat;

        [JsonPropertyName("version")]
        public int Version { get; init; } = 1;

        [JsonPropertyName("publisher")]
        public SignedPatchPublisherCertificateV1 Publisher { get; init; } = new();

        [JsonPropertyName("manifest")]
        public SignedPatchManifestV1 Manifest { get; init; } = new();

        [JsonPropertyName("alg")]
        public string SignatureAlgorithm { get; init; } = SignedPatchFormats.DefaultSignatureAlgorithm;

        // Base64 of the signature bytes.
        [JsonPropertyName("sig")]
        public string SignatureBase64 { get; init; } = string.Empty;
    }

    public sealed class SignedPatchPrivateKeyV1
    {
        [JsonPropertyName("format")]
        public string Format { get; init; } = SignedPatchFormats.PrivateKeyFormat;

        [JsonPropertyName("version")]
        public int Version { get; init; } = 1;

        [JsonPropertyName("publicKey")]
        public SignedPatchEcdsaPublicKey PublicKey { get; init; } = new();

        // Base64Url without padding.
        [JsonPropertyName("d")]
        public string D { get; init; } = string.Empty;
    }
}
