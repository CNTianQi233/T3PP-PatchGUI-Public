using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PatchGUI.Core
{
    internal static class SignedPatchCrypto
    {
        internal static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = null,
            WriteIndented = false,
            AllowTrailingCommas = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        internal static string ToHexSha256(byte[] bytes)
            => Convert.ToHexString(SHA256.HashData(bytes));

        internal static string ToHexSha256(ReadOnlySpan<byte> bytes)
            => Convert.ToHexString(SHA256.HashData(bytes));

        internal static byte[] ComputeFileSha256(string path, long length)
        {
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[1024 * 1024];

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            long remaining = Math.Max(0, Math.Min(length, fs.Length));
            while (remaining > 0)
            {
                int toRead = (int)Math.Min(buffer.Length, remaining);
                int read = fs.Read(buffer, 0, toRead);
                if (read <= 0)
                    break;
                sha.AppendData(buffer, 0, read);
                remaining -= read;
            }

            return sha.GetHashAndReset();
        }

        internal static string ComputePublicKeyFingerprintHex(SignedPatchEcdsaPublicKey publicKey)
        {
            byte[] x = Base64UrlDecode(publicKey.X);
            byte[] y = Base64UrlDecode(publicKey.Y);

            // Uncompressed point: 0x04 || X || Y
            byte[] point = new byte[1 + x.Length + y.Length];
            point[0] = 0x04;
            Buffer.BlockCopy(x, 0, point, 1, x.Length);
            Buffer.BlockCopy(y, 0, point, 1 + x.Length, y.Length);

            return ToHexSha256(point);
        }

        internal static ECDsa CreateEcdsaFromPublicKey(SignedPatchEcdsaPublicKey publicKey)
        {
            byte[] x = Base64UrlDecode(publicKey.X);
            byte[] y = Base64UrlDecode(publicKey.Y);

            var p = new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint
                {
                    X = x,
                    Y = y
                }
            };

            return ECDsa.Create(p);
        }

        internal static byte[] CanonicalizeJsonToUtf8Bytes<T>(T value)
        {
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
            using var doc = JsonDocument.Parse(json);
            using var ms = new MemoryStream(capacity: json.Length);
            using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false, SkipValidation = true }))
            {
                WriteCanonicalElement(writer, doc.RootElement);
            }

            return ms.ToArray();
        }

        private static void WriteCanonicalElement(Utf8JsonWriter writer, JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                {
                    writer.WriteStartObject();
                    foreach (var prop in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                    {
                        writer.WritePropertyName(prop.Name);
                        WriteCanonicalElement(writer, prop.Value);
                    }
                    writer.WriteEndObject();
                    break;
                }
                case JsonValueKind.Array:
                {
                    writer.WriteStartArray();
                    foreach (var item in element.EnumerateArray())
                    {
                        WriteCanonicalElement(writer, item);
                    }
                    writer.WriteEndArray();
                    break;
                }
                default:
                    element.WriteTo(writer);
                    break;
            }
        }

        internal static byte[] Base64UrlDecode(string base64Url)
        {
            if (string.IsNullOrWhiteSpace(base64Url))
                return Array.Empty<byte>();

            string s = base64Url.Replace('-', '+').Replace('_', '/');
            int pad = s.Length % 4;
            if (pad == 2) s += "==";
            else if (pad == 3) s += "=";
            else if (pad != 0) throw new FormatException("Invalid base64url length.");

            return Convert.FromBase64String(s);
        }

        internal static string Base64UrlEncode(ReadOnlySpan<byte> data)
        {
            string s = Convert.ToBase64String(data);
            s = s.TrimEnd('=').Replace('+', '-').Replace('/', '_');
            return s;
        }

        internal static (int blockLength, bool hasFooter) TryReadFooter(string patchPath)
        {
            using var fs = new FileStream(patchPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (fs.Length < 12)
                return (0, false);

            Span<byte> tail = stackalloc byte[12];
            fs.Seek(-12, SeekOrigin.End);
            int read = fs.Read(tail);
            if (read != 12)
                return (0, false);

            int blockLength = BinaryPrimitives.ReadInt32LittleEndian(tail[..4]);
            ReadOnlySpan<byte> magic = tail[4..];
            if (!magic.SequenceEqual(SignedPatchFormats.FooterMagicBytes.Span))
                return (0, false);

            return (blockLength, true);
        }
    }
}
