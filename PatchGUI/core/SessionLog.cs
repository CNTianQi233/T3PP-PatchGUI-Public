using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PatchGUI.Core
{
    internal static class SessionLog
    {
        private static readonly object _gate = new();
        private static StreamWriter? _writer;
        private static Func<string, string> _redactor = static s => s;

        public static string? LogPath { get; private set; }

        public static void Initialize()
        {
            lock (_gate)
            {
                if (_writer != null)
                    return;

                InitializeLocked();

                WriteInternal("BOOT", $"LogPath={LogPath ?? "(disabled)"}");
                WriteInternal("BOOT", $"ProcessId={Environment.ProcessId}, Is64BitProcess={Environment.Is64BitProcess}");
                WriteInternal("BOOT", $"OS={RuntimeInformation.OSDescription}");
                WriteInternal("BOOT", $"Framework={RuntimeInformation.FrameworkDescription}");
                WriteInternal("BOOT", $"BaseDir={AppDomain.CurrentDomain.BaseDirectory}");
                WriteInternal("BOOT", $"Culture={CultureInfo.CurrentCulture.Name}, UICulture={CultureInfo.CurrentUICulture.Name}");
            }
        }

        public static void Shutdown()
        {
            lock (_gate)
            {
                if (_writer == null)
                    return;

                try
                {
                    WriteInternal("BOOT", "Shutdown");
                }
                catch
                {
                    // ignore
                }

                try { _writer.Dispose(); } catch { }
                _writer = null;
            }
        }

        public static void Write(string source, string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            lock (_gate)
            {
                if (_writer == null)
                    Initialize();

                string trimmed = message.TrimEnd('\r', '\n');
                if (trimmed.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                {
                    foreach (var line in trimmed.Replace("\r\n", "\n").Split('\n'))
                    {
                        if (string.IsNullOrWhiteSpace(line))
                            continue;
                        WriteInternal(source, line);
                    }
                }
                else
                {
                    WriteInternal(source, trimmed);
                }
            }
        }

        public static void WriteException(string source, string context, Exception ex)
        {
            if (ex == null)
                return;

            Write(source, $"{context}: {ex}");
        }

        private static void WriteInternal(string source, string message)
        {
            Debug.Assert(_writer != null);
            string safe = _redactor(message);
            _writer!.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{source}] {safe}");
        }

        private static void InitializeLocked()
        {
#if DEBUG
            _redactor = static s => s;
#else
            _redactor = PrivacyRedactor.RedactForFile;
#endif

            string fileName = $"PatchGUI_{DateTime.Now:yyyyMMdd_HHmmss}_{Environment.ProcessId}.log";

            foreach (var dir in GetCandidateLogDirectories())
            {
                try
                {
                    Directory.CreateDirectory(dir);
                    LogPath = Path.Combine(dir, fileName);

                    _writer = new StreamWriter(LogPath, append: false, encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
                    {
                        AutoFlush = true
                    };

                    return;
                }
                catch
                {
                    // try next directory
                }
            }

            LogPath = null;
            _writer = new StreamWriter(Stream.Null) { AutoFlush = true };
        }

        private static string[] GetCandidateLogDirectories()
        {
            string local = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PatchGUI",
                "logs");

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string nextToExe = Path.Combine(baseDir, "logs");

#if DEBUG
            return new[] { local, nextToExe };
#else
            return new[] { nextToExe, local };
#endif
        }
    }
}
