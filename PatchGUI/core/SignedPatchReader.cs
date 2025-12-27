using System;
using System.Buffers.Binary;
using System.IO;
using System.Text.Json;

namespace PatchGUI.Core
{
    public sealed class SignedPatchReadResult
    {
        public long PayloadLength { get; init; }

        public long TotalLength { get; init; }

        public SignedPatchEnvelopeV1 Envelope { get; init; } = new();
    }

    public static class SignedPatchReader
    {
        public static bool TryRead(string patchPath, out SignedPatchReadResult? result, out string? error)
        {
            result = null;
            error = null;

            if (string.IsNullOrWhiteSpace(patchPath) || !File.Exists(patchPath))
            {
                error = "Patch file not found.";
                return false;
            }

            try
            {
                using var fs = new FileStream(patchPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                long fileLen = fs.Length;
                if (fileLen < 12)
                    return false;

                Span<byte> footer = stackalloc byte[12];
                fs.Seek(-12, SeekOrigin.End);
                int read = fs.Read(footer);
                if (read != 12)
                    return false;

                int blockLen = BinaryPrimitives.ReadInt32LittleEndian(footer[..4]);
                if (blockLen <= 0 || blockLen > SignedPatchFormats.MaxSignatureBlockBytes)
                {
                    error = "Invalid signature block length.";
                    return false;
                }

                ReadOnlySpan<byte> magic = footer[4..];
                if (!magic.SequenceEqual(SignedPatchFormats.FooterMagicBytes.Span))
                    return false;

                long blockStart = fileLen - 12 - blockLen;
                if (blockStart < 0)
                {
                    error = "Corrupted signature footer.";
                    return false;
                }

                fs.Seek(blockStart, SeekOrigin.Begin);
                byte[] block = new byte[blockLen];
                int got = 0;
                while (got < blockLen)
                {
                    int n = fs.Read(block, got, blockLen - got);
                    if (n <= 0)
                        break;
                    got += n;
                }
                if (got != blockLen)
                {
                    error = "Failed to read signature block.";
                    return false;
                }

                SignedPatchEnvelopeV1? envelope = JsonSerializer.Deserialize<SignedPatchEnvelopeV1>(block, SignedPatchCrypto.JsonOptions);
                if (envelope == null)
                {
                    error = "Invalid signature envelope.";
                    return false;
                }

                if (!string.Equals(envelope.Format, SignedPatchFormats.EnvelopeFormat, StringComparison.OrdinalIgnoreCase)
                    || envelope.Version != 1)
                {
                    error = "Unsupported signature envelope version.";
                    return false;
                }

                result = new SignedPatchReadResult
                {
                    PayloadLength = blockStart,
                    TotalLength = fileLen,
                    Envelope = envelope
                };
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}

