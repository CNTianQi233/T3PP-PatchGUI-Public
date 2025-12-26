using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PatchGUI.Core
{
    internal static class PrivacyRedactor
    {
        public static string RedactForFile(string text) => Redact(text);

        public static string RedactForUi(string text) => Redact(text);

        private static string Redact(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var sb = new StringBuilder(text.Length);
            int i = 0;
            while (i < text.Length)
            {
                if (TryReadPathToken(text, i, out int consumed, out string replacement))
                {
                    sb.Append(replacement);
                    i += consumed;
                    continue;
                }

                sb.Append(text[i]);
                i++;
            }

            return sb.ToString();
        }

        private static bool TryReadPathToken(string text, int start, out int consumed, out string replacement)
        {
            consumed = 0;
            replacement = string.Empty;

            if (start >= text.Length)
                return false;

            if (!IsPathStart(text, start))
                return false;

            int end = start;
            while (end < text.Length && !IsDelimiter(text[end]))
                end++;

            int pathEnd = end;
            while (pathEnd > start && IsTrailingPunctuation(text[pathEnd - 1]))
                pathEnd--;

            string path = text.Substring(start, pathEnd - start);
            string suffix = text.Substring(pathEnd, end - pathEnd);

            // Preserve stack-trace suffix like ":line 123" / ":行 123"
            int lineIdx = path.IndexOf(":line ", StringComparison.OrdinalIgnoreCase);
            if (lineIdx >= 0)
            {
                suffix = path.Substring(lineIdx) + suffix;
                path = path.Substring(0, lineIdx);
            }
            else
            {
                int cnIdx = path.IndexOf(":行 ", StringComparison.OrdinalIgnoreCase);
                if (cnIdx >= 0)
                {
                    suffix = path.Substring(cnIdx) + suffix;
                    path = path.Substring(0, cnIdx);
                }
            }

            replacement = RedactPath(path) + suffix;
            consumed = end - start;
            return true;
        }

        private static bool IsPathStart(string text, int i)
        {
            // Drive path: C:\...
            if (i + 2 < text.Length
                && IsAsciiLetter(text[i])
                && text[i + 1] == ':'
                && (text[i + 2] == '\\' || text[i + 2] == '/'))
                return true;

            // UNC path: \\server\share\...
            if (i + 1 < text.Length && text[i] == '\\' && text[i + 1] == '\\')
                return true;

            return false;
        }

        private static bool IsDelimiter(char ch)
        {
            return ch == '\r'
                || ch == '\n'
                || ch == '\t'
                || ch == '"'
                || ch == '\''
                || ch == '<'
                || ch == '>'
                || ch == '|';
        }

        private static bool IsTrailingPunctuation(char ch)
        {
            return ch == '.'
                || ch == ','
                || ch == ';'
                || ch == ':'
                || ch == ')'
                || ch == ']'
                || ch == '}';
        }

        private static bool IsAsciiLetter(char ch)
            => (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z');

        private static string RedactPath(string path)
        {
            string trimmed = path.Trim();
            string hash = ShortHash(trimmed);

            string normalized = trimmed.Replace('/', '\\');
            bool endsWithSlash = normalized.EndsWith("\\", StringComparison.Ordinal) || normalized.EndsWith("/", StringComparison.Ordinal);
            if (endsWithSlash)
                normalized = normalized.TrimEnd('\\', '/');

            string fileName = string.Empty;
            string extension = string.Empty;

            try
            {
                fileName = Path.GetFileName(normalized);
                extension = Path.GetExtension(normalized).ToLowerInvariant();
            }
            catch
            {
                // ignore Path parsing failures
            }

            if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(extension))
                return $"<DIR#{hash}>";

            return extension switch
            {
                ".t3pp" => $"<PATCH:{fileName}#{hash}>",
                ".pak" => $"<BACKUP#{hash}:{fileName}>",
                ".log" => $"<LOG#{hash}:{fileName}>",
                ".dll" => $"<DLL:{fileName}>",
                ".exe" => $"<EXE:{fileName}>",
                ".json" => $"<JSON:{fileName}>",
                _ => $"<FILE#{hash}{extension}>"
            };
        }

        private static string ShortHash(string value)
        {
            try
            {
                byte[] data = Encoding.UTF8.GetBytes(value);
                byte[] hash = SHA256.HashData(data);
                return Convert.ToHexString(hash).Substring(0, 8);
            }
            catch
            {
                return "UNKNOWN";
            }
        }
    }
}
