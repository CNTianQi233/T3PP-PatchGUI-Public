using System;
using System.IO;
using System.Runtime.InteropServices;

namespace PatchGUI.Core
{
    /// <summary>
    /// 规则 .sha 加/解密（壳）：真实算法在 T3ppNative.dll 里。
    /// </summary>
    public static class RuleCrypto
    {
        /// <summary>
        /// 从 JSON 文本生成 .sha 文件内容（完整字节数组）。
        /// key1/key2 参数保留只是为了兼容旧调用，实际由 DLL 内部决定。
        /// </summary>
        public static byte[] EncryptRuleSha(string json, string key1, string key2)
        {
            if (json == null)
                throw new ArgumentNullException(nameof(json));

            var rc = NativeMethods.t3pp_rules_encrypt(json, out var ptr, out var len);
            if (rc != 0)
                throw new InvalidOperationException($"t3pp_rules_encrypt failed, rc={rc}");

            try
            {
                var buf = new byte[len];
                Marshal.Copy(ptr, buf, 0, len);
                return buf;
            }
            finally
            {
                NativeMethods.t3pp_rules_free(ptr);
            }
        }

        /// <summary>
        /// 从 .sha 文件解密出 JSON 字符串。
        /// </summary>
        public static string DecryptRuleShaFile(string path, string key1, string key2)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentNullException(nameof(path));

            var bytes = File.ReadAllBytes(path);
            return DecryptRuleShaBytes(bytes, key1, key2);
        }

        /// <summary>
        /// 从 .sha 字节解密出 JSON 字符串。
        /// </summary>
        public static string DecryptRuleShaBytes(byte[] bytes, string key1, string key2)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));

            var rc = NativeMethods.t3pp_rules_decrypt(bytes, bytes.Length, out var ptr, out var wlen);
            if (rc != 0)
                throw new InvalidOperationException($"t3pp_rules_decrypt failed, rc={rc}");

            try
            {
                // wlen 是 wchar_t 个数
                var chars = new char[wlen];
                Marshal.Copy(ptr, chars, 0, wlen);

                var s = new string(chars);
                var zero = s.IndexOf('\0');
                if (zero >= 0)
                    s = s[..zero];

                return s;
            }
            finally
            {
                NativeMethods.t3pp_rules_free(ptr);
            }
        }
    }
}
