using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace PatchGUI.Core
{
    internal sealed class CrashCaptureResult
    {
        public CrashCaptureResult(string crashDirectory, string logPath, string? dumpPath, string? dumpError)
        {
            CrashDirectory = crashDirectory;
            LogPath = logPath;
            DumpPath = dumpPath;
            DumpError = dumpError;
        }

        public string CrashDirectory { get; }

        public string LogPath { get; }

        public string? DumpPath { get; }

        public string? DumpError { get; }

        public string GetUiPathHint(bool verbose)
        {
            if (verbose)
            {
                if (!string.IsNullOrWhiteSpace(DumpPath))
                    return $"{LogPath}{Environment.NewLine}{DumpPath}";

                return LogPath;
            }

            string logName = Path.GetFileName(LogPath);
            if (!string.IsNullOrWhiteSpace(DumpPath))
            {
                string dumpName = Path.GetFileName(DumpPath);
                return $"Crash\\{logName}{Environment.NewLine}Crash\\{dumpName}";
            }

            return $"Crash\\{logName}";
        }
    }

    internal static class CrashReportWriter
    {
        private static int _captureSequence;
        private static int _unhandledCaptured;

        public static CrashCaptureResult? TryCaptureForHandledErrorCode(string context, Exception ex)
        {
            if (ex == null)
                return null;

            Exception root = UnwrapException(ex);
            if (!TryDescribeErrorCode(root, out string fileToken, out string errorCodeLine))
                return null;

            return TryCapture(context, ex, root, fileToken, errorCodeLine);
        }

        public static CrashCaptureResult? TryCaptureForUnhandledException(string context, Exception? ex)
        {
            if (Interlocked.Exchange(ref _unhandledCaptured, 1) != 0)
                return null;

            Exception effective = ex ?? new InvalidOperationException("Unhandled non-Exception object.");
            Exception root = UnwrapException(effective);
            return TryCapture(context, effective, root, "unhandled", null);
        }

        private static CrashCaptureResult? TryCapture(
            string context,
            Exception fullException,
            Exception rootException,
            string fileToken,
            string? errorCodeLine)
        {
            try
            {
                string crashDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Crash");
                Directory.CreateDirectory(crashDirectory);

                int seq = Interlocked.Increment(ref _captureSequence);
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
                string baseName = $"PatchGUI_{timestamp}_{Environment.ProcessId}_{fileToken}_{seq:000}";
                string logPath = Path.Combine(crashDirectory, baseName + ".log");
                string dumpPath = Path.Combine(crashDirectory, baseName + ".dmp");

                string? dumpError = TryWriteMiniDump(dumpPath);
                string? finalDumpPath = dumpError == null ? dumpPath : null;

                WriteCrashLog(logPath, context, fullException, rootException, errorCodeLine, finalDumpPath, dumpError);

                try
                {
                    SessionLog.Write("EX", $"Crash log created: {logPath}");
                    if (finalDumpPath != null)
                        SessionLog.Write("EX", $"Crash dump created: {finalDumpPath}");
                    else if (!string.IsNullOrWhiteSpace(dumpError))
                        SessionLog.Write("EX", $"Crash dump creation failed: {dumpError}");
                }
                catch
                {
                    // ignore session log failure
                }

                return new CrashCaptureResult(crashDirectory, logPath, finalDumpPath, dumpError);
            }
            catch (Exception captureEx)
            {
                try
                {
                    SessionLog.Write("EX", $"Crash capture failed: {captureEx}");
                }
                catch
                {
                    // ignore nested failure
                }

                return null;
            }
        }

        private static void WriteCrashLog(
            string logPath,
            string context,
            Exception fullException,
            Exception rootException,
            string? errorCodeLine,
            string? dumpPath,
            string? dumpError)
        {
            using var writer = new StreamWriter(logPath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true
            };

            writer.WriteLine($"Timestamp={DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            writer.WriteLine($"Context={context}");
            writer.WriteLine($"ProcessId={Environment.ProcessId}");
            writer.WriteLine($"BaseDir={AppDomain.CurrentDomain.BaseDirectory}");
            writer.WriteLine($"ProcessPath={Environment.ProcessPath ?? "(unknown)"}");
            writer.WriteLine($"Version={GetVersionString()}");

            if (!string.IsNullOrWhiteSpace(errorCodeLine))
                writer.WriteLine(errorCodeLine);

            writer.WriteLine($"RootExceptionType={rootException.GetType().FullName ?? rootException.GetType().Name}");
            writer.WriteLine($"RootExceptionMessage={rootException.Message}");
            writer.WriteLine($"SessionLogPath={SessionLog.LogPath ?? "(disabled)"}");

            if (!string.IsNullOrWhiteSpace(dumpPath))
                writer.WriteLine($"DumpPath={dumpPath}");
            else if (!string.IsNullOrWhiteSpace(dumpError))
                writer.WriteLine($"DumpError={dumpError}");

            writer.WriteLine();
            writer.WriteLine("=== Exception ===");
            writer.WriteLine(fullException.ToString());

            if (!ReferenceEquals(fullException, rootException))
            {
                writer.WriteLine();
                writer.WriteLine("=== Root Exception ===");
                writer.WriteLine(rootException.ToString());
            }

            string? sessionLogPath = SessionLog.LogPath;
            if (!string.IsNullOrWhiteSpace(sessionLogPath) && File.Exists(sessionLogPath))
            {
                writer.WriteLine();
                writer.WriteLine("=== Session Log ===");
                try
                {
                    using var stream = new FileStream(sessionLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                    writer.Write(reader.ReadToEnd());
                }
                catch (Exception ex)
                {
                    writer.WriteLine($"<Failed to append session log: {ex.Message}>");
                }
            }
        }

        private static string? TryWriteMiniDump(string dumpPath)
        {
            try
            {
                using Process process = Process.GetCurrentProcess();
                using var stream = new FileStream(dumpPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);

                bool ok = MiniDumpWriteDump(
                    process.Handle,
                    process.Id,
                    stream.SafeFileHandle,
                    MiniDumpType.MiniDumpWithDataSegs
                    | MiniDumpType.MiniDumpWithHandleData
                    | MiniDumpType.MiniDumpWithUnloadedModules
                    | MiniDumpType.MiniDumpWithIndirectlyReferencedMemory
                    | MiniDumpType.MiniDumpWithProcessThreadData
                    | MiniDumpType.MiniDumpWithThreadInfo,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero);

                if (ok)
                    return null;

                int error = Marshal.GetLastWin32Error();
                return $"MiniDumpWriteDump failed with Win32 error {error}.";
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        private static bool TryDescribeErrorCode(Exception root, out string fileToken, out string errorCodeLine)
        {
            if (root is NativePatchApplyException native)
            {
                fileToken = native.ReturnCode < 0
                    ? $"rc-{Math.Abs(native.ReturnCode)}"
                    : $"rc{native.ReturnCode}";

                if (string.IsNullOrWhiteSpace(native.ReturnCodeDescription))
                    errorCodeLine = $"ReturnCode={native.ReturnCode}";
                else
                    errorCodeLine = $"ReturnCode={native.ReturnCode} ({native.ReturnCodeDescription})";

                return true;
            }

            fileToken = string.Empty;
            errorCodeLine = string.Empty;
            return false;
        }

        private static Exception UnwrapException(Exception ex)
        {
            Exception current = ex;
            while (true)
            {
                if (current is AggregateException ae)
                {
                    AggregateException flat = ae.Flatten();
                    if (flat.InnerExceptions.Count == 1)
                    {
                        current = flat.InnerExceptions[0];
                        continue;
                    }
                }

                if (current is TargetInvocationException tie && tie.InnerException != null)
                {
                    current = tie.InnerException;
                    continue;
                }

                return current;
            }
        }

        private static string GetVersionString()
        {
            try
            {
                string? processPath = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath))
                {
                    FileVersionInfo info = FileVersionInfo.GetVersionInfo(processPath);
                    if (!string.IsNullOrWhiteSpace(info.FileVersion))
                        return info.FileVersion;
                }
            }
            catch
            {
                // ignore version lookup failure
            }

            return typeof(CrashReportWriter).Assembly.GetName().Version?.ToString() ?? "(unknown)";
        }

        [Flags]
        private enum MiniDumpType : uint
        {
            MiniDumpWithDataSegs = 0x00000001,
            MiniDumpWithHandleData = 0x00000004,
            MiniDumpWithUnloadedModules = 0x00000020,
            MiniDumpWithIndirectlyReferencedMemory = 0x00000040,
            MiniDumpWithProcessThreadData = 0x00000100,
            MiniDumpWithThreadInfo = 0x00001000,
        }

        [DllImport("Dbghelp.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool MiniDumpWriteDump(
            IntPtr hProcess,
            int processId,
            SafeFileHandle hFile,
            MiniDumpType dumpType,
            IntPtr exceptionParam,
            IntPtr userStreamParam,
            IntPtr callbackParam);
    }
}
