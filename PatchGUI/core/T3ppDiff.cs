using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace PatchGUI.Core
{
    /// <summary>
    /// Managed wrapper for T3PP; algorithms/encryption/file ops live in T3ppNative.dll.
    /// </summary>
    public static class T3ppDiff
    {
        // ---------------------
        // P/Invoke declarations
        // ---------------------

        // 和 C++ t3pp_native.h 定义保持一致
        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
        private delegate void NativeLogCb(int level, string msg);

        private static class Native
        {
            // TODO: 如果你的 DLL 名不是 T3ppNative.dll，在这里改
            private const string DllName = "T3ppNative.dll";

            [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
            internal static extern int t3pp_apply_patch_from_file(
                string patch_file,
                string target_root,
                NativeLogCb? logger,
                int dry_run);

            [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
            internal static extern int t3pp_create_patch_from_dirs(
                string old_dir,
                string new_dir,
                string output_file,
                NativeLogCb? logger,
                int append_tenumuinoti);

            [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
            internal static extern int t3pp_create_patch_from_files(
                string old_file,
                string new_file,
                string output_file,
                NativeLogCb? logger,
                int append_tenumuinoti);

            // Resource access
            [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
            internal static extern int t3pp_get_resource(
                int resource_id,
                out IntPtr out_data,
                out int out_size);

            [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
            internal static extern int t3pp_get_lang_json(
                string lang_code,
                out IntPtr out_data,
                out int out_size);
        }

        // Resource IDs (must match t3pp_native.h)
        public const int RES_LMAO_PNG = 101;
        public const int RES_XDELTA3_EXE = 102;
        public const int RES_LANG_ZH_CN = 103;
        public const int RES_LANG_EN_US = 104;

        /// <summary>
        /// Get embedded resource data from T3ppNative.dll
        /// </summary>
        public static byte[]? GetResource(int resourceId)
        {
            int rc = Native.t3pp_get_resource(resourceId, out IntPtr dataPtr, out int size);
            if (rc != 0 || dataPtr == IntPtr.Zero || size <= 0)
                return null;

            byte[] data = new byte[size];
            Marshal.Copy(dataPtr, data, 0, size);
            return data;
        }

        /// <summary>
        /// Get language JSON string from T3ppNative.dll
        /// </summary>
        public static string? GetLangJson(string langCode)
        {
            int rc = Native.t3pp_get_lang_json(langCode, out IntPtr dataPtr, out int size);
            if (rc != 0 || dataPtr == IntPtr.Zero || size <= 0)
                return null;

            byte[] data = new byte[size];
            Marshal.Copy(dataPtr, data, 0, size);
            return System.Text.Encoding.UTF8.GetString(data);
        }

        // 这个仍然保留给 MainWindow 用
        public static Action<string>? DebugLog;

        // ---------------------
        // 对外 API：应用补丁
        // ---------------------
        public static void ApplyPatchToDirectory(
            string patchFile,
            string targetRoot,
            IPatchLogger? logger,
            bool dryRun)
        {
            if (string.IsNullOrWhiteSpace(patchFile))
                throw new ArgumentNullException(nameof(patchFile));
            if (string.IsNullOrWhiteSpace(targetRoot))
                throw new ArgumentNullException(nameof(targetRoot));

            try
            {
                logger?.Info("ApplyPatchToDirectory:");
                logger?.Info($"  patchFile={patchFile}");
                logger?.Info($"  targetRoot={targetRoot}");
                logger?.Info($"  dryRun={dryRun}");

                DebugLog?.Invoke("[ApplyPatchToDirectory]");
                DebugLog?.Invoke($"  patchFile={patchFile}");
                DebugLog?.Invoke($"  targetRoot={targetRoot}");
                DebugLog?.Invoke($"  dryRun={dryRun}");
            }
            catch { }

            // Capture native log to enrich error message (still write through to logger).
            string? lastNativeLine = null;
            string? lastNativeError = null;
            var lastErrors = new List<string>(capacity: 4);
            object gate = new();

            NativeLogCb cb = (level, msg) =>
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(msg))
                    {
                        lock (gate)
                        {
                            lastNativeLine = msg;
                            if (level >= 2)
                            {
                                lastNativeError = msg;
                                if (lastErrors.Count >= 4)
                                    lastErrors.RemoveAt(0);
                                lastErrors.Add(msg);
                            }
                        }
                    }
                }
                catch
                {
                    // ignore capture failure
                }

                try
                {
                    if (logger != null)
                    {
                        switch (level)
                        {
                            case 0: logger.Info(msg); break;
                            case 1: logger.Warn(msg); break;
                            default: logger.Error(msg); break;
                        }
                    }
                    else
                    {
                        DebugLog?.Invoke(msg);
                    }
                }
                catch
                {
                    // 日志失败直接吞掉，避免 native 回调崩溃
                }
            };

            int rc = Native.t3pp_apply_patch_from_file(
                patchFile,
                targetRoot,
                (logger != null || DebugLog != null) ? cb : null,
                dryRun ? 1 : 0);

            try
            {
                logger?.Info($"ApplyPatchToDirectory: native returned rc={rc}");
                DebugLog?.Invoke($"[ApplyPatchToDirectory] native returned rc={rc}");
            }
            catch { }

            if (rc != 0)
            {
                string? nativeReason;
                string? nativeDetails;
                lock (gate)
                {
                    nativeReason = lastNativeError ?? lastNativeLine;
                    nativeDetails = lastErrors.Count > 0 ? string.Join("\n", lastErrors) : null;
                }

                string desc = DescribeApplyPatchReturnCode(rc);

                if (!string.IsNullOrWhiteSpace(nativeDetails))
                {
                    try { logger?.Error($"原生补丁失败详情：\n{nativeDetails}"); } catch { }
                }
                else if (!string.IsNullOrWhiteSpace(nativeReason))
                {
                    try { logger?.Error($"原生补丁失败原因：{nativeReason}"); } catch { }
                }

                throw new NativePatchApplyException(rc, desc, nativeReason, nativeDetails);
            }
        }

        private static string DescribeApplyPatchReturnCode(int rc)
        {
            return rc switch
            {
                -1 => "参数无效",
                -2 => "补丁文件不存在",
                -3 => "目标目录不存在或不是目录",
                -4 => "无法打开补丁文件",
                -5 => "读取补丁文件失败（文件可能不完整）",
                -6 => "补丁文件格式不正确（magic 不匹配）",
                -7 => "读取补丁版本失败",
                -8 => "补丁版本不支持",
                -9 => "读取封面数据失败",
                -10 => "跳过封面数据失败",
                -11 => "读取条目数量失败",
                -12 => "读取条目数据失败（cipherLen）",
                -13 => "读取条目数据失败（nonce）",
                -14 => "读取条目数据失败（cipher）",
                -15 => "读取条目数据失败（tag）",
                -16 => "条目解密失败（补丁损坏或不匹配）",
                -17 => "条目应用失败（目标文件不匹配/被占用/权限不足等）",
                -100 => "原生异常（native exception）",
                -101 => "原生未知异常（native unknown exception）",
                _ => "未知错误"
            };
        }

        // ---------------------
        // 对外 API：生成补丁
        // ---------------------
        public static void CreateDirectoryDiff(
            string oldDir,
            string newDir,
            string outputFile,
            bool appendTenu = false,
            IPatchLogger? logger = null)
        {
            if (string.IsNullOrWhiteSpace(oldDir))
                throw new ArgumentNullException(nameof(oldDir));
            if (string.IsNullOrWhiteSpace(newDir))
                throw new ArgumentNullException(nameof(newDir));
            if (string.IsNullOrWhiteSpace(outputFile))
                throw new ArgumentNullException(nameof(outputFile));

            try
            {
                DebugLog?.Invoke("[CreateDirectoryDiff]");
                DebugLog?.Invoke($"  oldDir={oldDir}");
                DebugLog?.Invoke($"  newDir={newDir}");
                DebugLog?.Invoke($"  outputFile={outputFile}");
                DebugLog?.Invoke($"  appendTenu={appendTenu}");
            }
            catch { }

            var nativeLogs = new System.Collections.Generic.List<string>();
            NativeLogCb cb = (level, msg) =>
            {
                try
                {
                    // Forward native logs to the UI logger
                    if (logger != null && !string.IsNullOrWhiteSpace(msg))
                    {
                        // Remove [T3PP] prefix for cleaner display
                        string cleanMsg = msg;
                        if (cleanMsg.StartsWith("[T3PP] "))
                            cleanMsg = cleanMsg.Substring(7);

                        switch (level)
                        {
                            case 0: logger.Info(cleanMsg); break;
                            case 1: logger.Warn(cleanMsg); break;
                            default: logger.Error(cleanMsg); break;
                        }
                    }

                    string logLine = $"[NATIVE-{level}] {msg}";
                    lock (nativeLogs)
                        nativeLogs.Add(logLine);
                    DebugLog?.Invoke(logLine);
                }
                catch { }
            };

            int rc = Native.t3pp_create_patch_from_dirs(
                oldDir,
                newDir,
                outputFile,
                cb,
                appendTenu ? 1 : 0);

            try
            {
                DebugLog?.Invoke($"[CreateDirectoryDiff] native returned rc={rc}");
            }
            catch { }

            if (rc == 1)
            {
                // 1 = 没有差异（和你原来抛异常的语义相近）
                throw new InvalidOperationException("两个目录完全一致，没有需要打包的差分文件。");
            }

            if (rc != 0)
            {
                throw new InvalidOperationException($"原生补丁生成失败，错误码：{rc}");
            }
        }

        public static void CreateFileDiff(
            string oldFile,
            string newFile,
            string outputFile,
            bool appendTenu = false)
        {
            if (string.IsNullOrWhiteSpace(oldFile))
                throw new ArgumentNullException(nameof(oldFile));
            if (string.IsNullOrWhiteSpace(newFile))
                throw new ArgumentNullException(nameof(newFile));
            if (string.IsNullOrWhiteSpace(outputFile))
                throw new ArgumentNullException(nameof(outputFile));

            try
            {
                DebugLog?.Invoke("[CreateFileDiff]");
                DebugLog?.Invoke($"  oldFile={oldFile}");
                DebugLog?.Invoke($"  newFile={newFile}");
                DebugLog?.Invoke($"  outputFile={outputFile}");
                DebugLog?.Invoke($"  appendTenu={appendTenu}");
            }
            catch { }

            NativeLogCb? cb = null;
            if (DebugLog != null)
            {
                cb = (level, msg) =>
                {
                    try
                    {
                        DebugLog?.Invoke($"[NATIVE-{level}] {msg}");
                    }
                    catch { }
                };
            }

            int rc = Native.t3pp_create_patch_from_files(
                oldFile,
                newFile,
                outputFile,
                cb,
                appendTenu ? 1 : 0);

            try
            {
                DebugLog?.Invoke($"[CreateFileDiff] native returned rc={rc}");
            }
            catch { }

            if (rc == 1)
            {
                throw new InvalidOperationException("两个目录完全一致，没有需要打包的差分文件。");
            }

            if (rc != 0)
            {
                throw new InvalidOperationException($"原生补丁生成失败，错误码：{rc}");
            }
        }

    }
}
