// 该文件负责 UI 辅助：日志、进度条/任务栏进度、导航指示条与主题切换等通用逻辑。
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using PatchGUI.Core;
using Wpf.Ui.Appearance;

namespace PatchGUI
{
    public partial class MainWindow
    {
        #region UI 日志/进度辅助

        private const int ProgressAnimationMs = 200;

        private void SetMainProgress(double percent, bool animate = true)
        {
            SetSmoothProgress(MainProgressBar, percent, animate);

            // 更新百分比文本
            if (ProgressPercentText != null)
            {
                int displayPercent = (int)Math.Round(Math.Clamp(percent, 0, 100));
                ProgressPercentText.Text = $"{displayPercent}%";
                ProgressPercentText.Visibility = displayPercent > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void SetGenProgress(double percent, bool animate = true)
            => SetSmoothProgress(GenProgressBar, percent, animate);

        private void SetSmoothProgress(System.Windows.Controls.ProgressBar bar, double percent, bool animate)
        {
            if (bar == null)
                return;

            percent = Math.Clamp(percent, bar.Minimum, bar.Maximum);

            if (!animate)
            {
                bar.BeginAnimation(System.Windows.Controls.ProgressBar.ValueProperty, null);
                bar.Value = percent;
            }
            else
            {
                var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
                var animation = new DoubleAnimation
                {
                    To = percent,
                    Duration = TimeSpan.FromMilliseconds(ProgressAnimationMs),
                    EasingFunction = easing,
                    FillBehavior = FillBehavior.HoldEnd
                };
                bar.BeginAnimation(System.Windows.Controls.ProgressBar.ValueProperty, animation, HandoffBehavior.SnapshotAndReplace);
            }

            if (_taskbarProgressActive)
            {
                SetTaskbarProgressPercent(percent, animate);
            }
        }

        private void BeginTaskbarProgress()
        {
            _taskbarProgressActive = true;

            if (MainTaskbarItemInfo == null)
                return;

            MainTaskbarItemInfo.ProgressState = TaskbarItemProgressState.Normal;
            _taskbarProgressTarget = 0;
            StopTaskbarProgressTimer();
            MainTaskbarItemInfo.ProgressValue = 0;
        }

        private void SetTaskbarProgressPercent(double percent, bool animate)
        {
            if (MainTaskbarItemInfo == null)
                return;

            double value = Math.Clamp(percent / 100.0, 0.0, 1.0);
            _taskbarProgressTarget = value;

            if (!animate)
            {
                StopTaskbarProgressTimer();
                MainTaskbarItemInfo.ProgressValue = value;
                return;
            }

            EnsureTaskbarProgressTimer();
            if (_taskbarProgressTimer?.IsEnabled != true)
                _taskbarProgressTimer?.Start();
        }

        private void ClearTaskbarProgress()
        {
            StopTaskbarProgressTimer();
            if (MainTaskbarItemInfo != null)
            {
                MainTaskbarItemInfo.ProgressState = TaskbarItemProgressState.None;
                MainTaskbarItemInfo.ProgressValue = 0;
            }

            _taskbarProgressTarget = 0;
            _taskbarProgressActive = false;

            // 隐藏百分比文本
            if (ProgressPercentText != null)
            {
                ProgressPercentText.Visibility = Visibility.Collapsed;
            }
        }

        private void SetTaskbarState(TaskbarItemProgressState state)
        {
            if (MainTaskbarItemInfo == null)
                return;

            MainTaskbarItemInfo.ProgressState = state;
        }

        private void EnsureTaskbarProgressTimer()
        {
            _taskbarProgressTimer ??= new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _taskbarProgressTimer.Tick -= TaskbarProgressTimer_Tick;
            _taskbarProgressTimer.Tick += TaskbarProgressTimer_Tick;
        }

        private void StopTaskbarProgressTimer()
        {
            _taskbarProgressTimer?.Stop();
        }

        private void TaskbarProgressTimer_Tick(object? sender, EventArgs e)
        {
            if (!_taskbarProgressActive || MainTaskbarItemInfo == null)
            {
                StopTaskbarProgressTimer();
                return;
            }

            const double factor = 0.28;
            const double epsilon = 0.002;

            double current = Math.Clamp(MainTaskbarItemInfo.ProgressValue, 0.0, 1.0);
            double target = Math.Clamp(_taskbarProgressTarget, 0.0, 1.0);
            double delta = target - current;

            if (Math.Abs(delta) <= epsilon)
            {
                MainTaskbarItemInfo.ProgressValue = target;
                StopTaskbarProgressTimer();
                return;
            }

            MainTaskbarItemInfo.ProgressValue = Math.Clamp(current + (delta * factor), 0.0, 1.0);
        }

        private string L(string key, string fallback) => LocalizationManager.Get(key, fallback);

        private string LF(string key, string fallback, params object[] args)
            => string.Format(CultureInfo.CurrentCulture, L(key, fallback), args);

        private void ShowInfo(string message)
            => System.Windows.MessageBox.Show(this, message, L("title.info", "提示"),
                MessageBoxButton.OK, MessageBoxImage.Information);

        private void ShowDone(string message)
            => System.Windows.MessageBox.Show(this, message, L("title.done", "完成"),
                MessageBoxButton.OK, MessageBoxImage.Information);

        private void ShowError(string message)
            => System.Windows.MessageBox.Show(this, message, L("title.error", "错误"),
                MessageBoxButton.OK, MessageBoxImage.Error);


        private void AppendConsoleLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            Core.SessionLog.Write("PATCH", line);

            string raw = line.TrimEnd('\r', '\n');
            bool showInUi = ShouldShowInUi(raw);
            string uiRaw = VerboseUiLog ? raw : Core.PrivacyRedactor.RedactForUi(raw);
            string stamped = $"[{DateTime.Now:HH:mm:ss.fff}] {uiRaw}";
            Dispatcher.Invoke(() =>
            {
                if (showInUi)
                {
                    ConsoleTextBox.AppendText(stamped + Environment.NewLine);
                    TrimLog(ConsoleTextBox, 400);
                    ConsoleTextBox.CaretIndex = ConsoleTextBox.Text.Length;
                    ConsoleTextBox.ScrollToEnd();
                }

                UpdateStatusFromLog(stamped);
                ConsoleTextBox.CaretIndex = ConsoleTextBox.Text.Length;
                ConsoleTextBox.ScrollToEnd();
            });

            string errorLine = VerboseUiLog ? stamped : Core.PrivacyRedactor.RedactForFile(stamped);
            TryWriteErrorLog(errorLine);
        }

        private void AppendGenConsoleLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            Core.SessionLog.Write("GEN", line);

            string raw = line.TrimEnd('\r', '\n');
            bool showInUi = ShouldShowInUi(raw);
            string uiRaw = VerboseUiLog ? raw : Core.PrivacyRedactor.RedactForUi(raw);
            string stamped = $"[{DateTime.Now:HH:mm:ss.fff}] {uiRaw}";
            Dispatcher.Invoke(() =>
            {
                if (showInUi)
                {
                    GenConsoleBox.AppendText(stamped + Environment.NewLine);
                    TrimLog(GenConsoleBox, 400);
                    GenConsoleBox.CaretIndex = GenConsoleBox.Text.Length;
                    GenConsoleBox.ScrollToEnd();
                }
                GenConsoleBox.CaretIndex = GenConsoleBox.Text.Length;
                GenConsoleBox.ScrollToEnd();
            });
        }

        private static bool ShouldShowInUi(string rawLine)
        {
            if (VerboseUiLog)
                return true;

            var text = rawLine?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(text))
                return false;

            if (text.Contains("==>", StringComparison.Ordinal) || text.Contains("<==", StringComparison.Ordinal))
                return true;

            if (text.StartsWith("[ERROR]", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("[WARN]", StringComparison.OrdinalIgnoreCase))
                return true;

            if (!text.StartsWith("[INFO]", StringComparison.OrdinalIgnoreCase))
                return false;

            // Release: only show high-level info lines.
            return text.Contains("日志文件", StringComparison.OrdinalIgnoreCase)
                || text.Contains("已选择补丁", StringComparison.OrdinalIgnoreCase)
                || text.Contains("已选择目录", StringComparison.OrdinalIgnoreCase)
                || text.Contains("已选择目标", StringComparison.OrdinalIgnoreCase)
                || text.Contains("最终目标", StringComparison.OrdinalIgnoreCase)
                || text.Contains("备份已", StringComparison.OrdinalIgnoreCase)
                || text.Contains("备份位置", StringComparison.OrdinalIgnoreCase)
                || text.Contains("无可备份", StringComparison.OrdinalIgnoreCase)
                || text.Contains("无需备份", StringComparison.OrdinalIgnoreCase)
                || text.Contains("补丁应用完成", StringComparison.OrdinalIgnoreCase)
                || text.Contains("差分生成完成", StringComparison.OrdinalIgnoreCase)
                || text.Contains("用户取消", StringComparison.OrdinalIgnoreCase);
        }

        private IDisposable BeginUiLock()
        {
            SetUiLocked(true);
            return new ActionDisposable(() => SetUiLocked(false));
        }

        private void SetUiLocked(bool locked)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => SetUiLocked(locked));
                return;
            }

            if (locked)
            {
                _uiLockDepth++;
                if (_uiLockDepth != 1)
                    return;
            }
            else
            {
                if (_uiLockDepth == 0)
                    return;

                _uiLockDepth--;
                if (_uiLockDepth != 0)
                    return;
            }

            bool enabled = _uiLockDepth == 0;

            DebugNavBar.IsEnabled = enabled;
            PatchControlsPanel.IsEnabled = enabled;
            GameSelectionPanel.IsEnabled = enabled;
            GenerateControlsPanel.IsEnabled = enabled;
            SettingsView.IsEnabled = enabled;

            System.Windows.Input.Mouse.OverrideCursor = enabled ? null : System.Windows.Input.Cursors.Wait;
        }

        private sealed class ActionDisposable : IDisposable
        {
            private Action? _dispose;

            public ActionDisposable(Action dispose)
            {
                _dispose = dispose ?? throw new ArgumentNullException(nameof(dispose));
            }

            public void Dispose()
            {
                var d = Interlocked.Exchange(ref _dispose, null);
                d?.Invoke();
            }
        }

        private string GetLogPathHint()
        {
            string? path = Core.SessionLog.LogPath;
            if (string.IsNullOrWhiteSpace(path))
                return L("log.file.disabled", "日志文件：未启用");

            if (VerboseUiLog)
                return string.Format(CultureInfo.CurrentCulture, L("log.file.path", "日志文件：{0}"), path);

            string fileName = Path.GetFileName(path);
            return string.Format(CultureInfo.CurrentCulture, L("log.file.name", "日志文件：logs\\{0}（已脱敏）"), fileName);
        }

        private void TryWriteErrorLog(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            if (line.IndexOf("[ERROR]", StringComparison.OrdinalIgnoreCase) < 0)
                return;

            try
            {
                string sanitized = line.TrimEnd('\r', '\n');
                string logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {sanitized}{Environment.NewLine}";
                File.AppendAllText(ErrorLogPath, logLine);
            }
            catch
            {
                // ignore file write failure
            }
        }

        private void ShowRepairErrorDialog(string context, Exception ex, IPatchLogger? logger = null)
        {
            try
            {
                logger?.LogException(context, ex);
            }
            catch { }

            try
            {
                Core.SessionLog.WriteException("EX", context, ex);
            }
            catch { }

            string title = L("errorDialog.title", "错误");
            string? logPath = Core.SessionLog.LogPath;

            string errorTypeLine = BuildFriendlyErrorTypeLine(ex);
            string? detailLine = BuildFriendlyErrorDetailLine(ex);

            string logHint;
            if (string.IsNullOrWhiteSpace(logPath))
            {
                logHint = L("errorDialog.logDisabled", "(未生成日志文件)");
            }
            else
            {
                string fileName = Path.GetFileName(logPath);
#if DEBUG
                logHint = logPath;
#else
                logHint = $"logs\\{fileName}";
#endif
            }

            string message = string.Format(
                CultureInfo.CurrentCulture,
                L("errorDialog.generic", "修补游戏时产生了个错误。\n\n请不要发送此界面的截图。\n请给开发者提供错误日志：{0}"),
                logHint);

            if (!string.IsNullOrWhiteSpace(errorTypeLine))
            {
                message = message + "\n\n" + string.Format(
                    CultureInfo.CurrentCulture,
                    L("errorDialog.typeLine", "错误类型：{0}"),
                    errorTypeLine);
            }

            if (!string.IsNullOrWhiteSpace(detailLine))
            {
                message = message + "\n\n" + string.Format(
                    CultureInfo.CurrentCulture,
                    L("errorDialog.detailLine", "原因：{0}"),
                    detailLine);
            }

            try
            {
                ErrorDialog.Show(this, title, message, logPath);
            }
            catch
            {
                System.Windows.MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string BuildFriendlyErrorTypeLine(Exception ex)
        {
            Exception root = UnwrapException(ex);

            string typeName = root.GetType().FullName ?? root.GetType().Name;
            string? reasonKey = TryGetKnownErrorReasonKey(root);

            if (string.IsNullOrWhiteSpace(reasonKey))
                return typeName;

            string friendly = LocalizationManager.Get(reasonKey, typeName);
            if (string.IsNullOrWhiteSpace(friendly) || friendly.Equals(typeName, StringComparison.Ordinal))
                return typeName;

            return string.Format(CultureInfo.CurrentCulture, "{0}（{1}）", friendly, typeName);
        }

        private string? BuildFriendlyErrorDetailLine(Exception ex)
        {
            Exception root = UnwrapException(ex);

            if (root is NativePatchApplyException native)
            {
                // 优先使用 NativeDetails（包含所有错误消息，可能有具体的文件名）
                string? detail = native.NativeDetails;

                // 如果没有详细信息，回退到 NativeReason
                if (string.IsNullOrWhiteSpace(detail))
                    detail = native.NativeReason;

                if (string.IsNullOrWhiteSpace(detail))
                {
                    if (!string.IsNullOrWhiteSpace(native.ReturnCodeDescription))
                        detail = $"{native.ReturnCodeDescription}（错误码：{native.ReturnCode}）";
                    else
                        detail = $"错误码：{native.ReturnCode}";
                }

                if (string.IsNullOrWhiteSpace(detail))
                    return null;

                return VerboseUiLog ? detail : Core.PrivacyRedactor.RedactForUi(detail);
            }

            return null;
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

                if (current is System.Reflection.TargetInvocationException tie && tie.InnerException != null)
                {
                    current = tie.InnerException;
                    continue;
                }

                return current;
            }
        }

        private static string? TryGetKnownErrorReasonKey(Exception ex)
        {
            if (ex is NativePatchApplyException)
                return "errorDialog.reason.nativePatchFailed";

            if (ex is DllNotFoundException)
                return "errorDialog.reason.dllNotFound";

            if (ex is EntryPointNotFoundException)
                return "errorDialog.reason.entryPointNotFound";

            if (ex is BadImageFormatException)
                return "errorDialog.reason.badImageFormat";

            if (ex is UnauthorizedAccessException)
                return "errorDialog.reason.unauthorized";

            if (ex is FileLoadException)
                return "errorDialog.reason.fileLoadFailed";

            if (ex is FileNotFoundException)
                return "errorDialog.reason.fileNotFound";

            if (ex is DirectoryNotFoundException)
                return "errorDialog.reason.directoryNotFound";

            if (ex is PathTooLongException)
                return "errorDialog.reason.pathTooLong";

            if (ex is NotSupportedException)
                return "errorDialog.reason.notSupported";

            if (ex is ArgumentException)
                return "errorDialog.reason.invalidArgument";

            if (ex is FormatException)
                return "errorDialog.reason.invalidFormat";

            if (ex is System.Text.Json.JsonException)
                return "errorDialog.reason.invalidJson";

            if (ex is CryptographicException)
                return "errorDialog.reason.cryptoFailed";

            if (ex is OutOfMemoryException)
                return "errorDialog.reason.outOfMemory";

            if (ex is OperationCanceledException)
                return "errorDialog.reason.cancelled";

            if (ex is Win32Exception w32)
            {
                return w32.NativeErrorCode switch
                {
                    5 => "errorDialog.reason.unauthorized",
                    2 => "errorDialog.reason.fileNotFound",
                    3 => "errorDialog.reason.directoryNotFound",
                    32 => "errorDialog.reason.fileInUse",
                    33 => "errorDialog.reason.fileInUse",
                    112 => "errorDialog.reason.diskFull",
                    206 => "errorDialog.reason.pathTooLong",
                    _ => null
                };
            }

            if (ex is IOException io)
            {
                int hr = Marshal.GetHRForException(io);
                return hr switch
                {
                    unchecked((int)0x80070002) => "errorDialog.reason.fileNotFound",
                    unchecked((int)0x80070003) => "errorDialog.reason.directoryNotFound",
                    unchecked((int)0x80070005) => "errorDialog.reason.unauthorized",
                    unchecked((int)0x80070020) => "errorDialog.reason.fileInUse",   // sharing violation
                    unchecked((int)0x80070021) => "errorDialog.reason.fileInUse",   // lock violation
                    unchecked((int)0x80070070) => "errorDialog.reason.diskFull",    // disk full
                    _ => null
                };
            }

            if (ex is InvalidOperationException inv)
            {
                string msg = inv.Message ?? string.Empty;
                if (msg.Contains("预检失败", StringComparison.OrdinalIgnoreCase) ||
                    msg.Contains("preflight", StringComparison.OrdinalIgnoreCase))
                {
                    return "errorDialog.reason.preflightFailed";
                }

                if (msg.Contains("原生补丁", StringComparison.OrdinalIgnoreCase) ||
                    msg.Contains("错误码", StringComparison.OrdinalIgnoreCase) ||
                    msg.Contains("native", StringComparison.OrdinalIgnoreCase))
                {
                    return "errorDialog.reason.nativePatchFailed";
                }
            }

            return null;
        }

        private void TrimLog(System.Windows.Controls.TextBox box, int maxLines)
        {
            if (box.LineCount <= maxLines)
                return;

            int removeCount = box.GetCharacterIndexFromLineIndex(box.LineCount - maxLines);
            box.Text = box.Text[removeCount..];
        }

        private void UpdateStatusFromLog(string line)
        {
            var text = StripLeadingTimestamp(line?.Trim() ?? string.Empty);
            if (string.IsNullOrEmpty(text))
                return;

            System.Windows.Media.Brush color = System.Windows.Media.Brushes.Gray;
            string message = text;

            if (text.StartsWith("[ERROR]", StringComparison.OrdinalIgnoreCase))
            {
                color = System.Windows.Media.Brushes.IndianRed;
                message = text;
            }
            else if (text.StartsWith("[WARN]", StringComparison.OrdinalIgnoreCase))
            {
                color = System.Windows.Media.Brushes.DarkGoldenrod;
                message = text;
            }
            else if (text.StartsWith("[INFO]", StringComparison.OrdinalIgnoreCase))
            {
                color = System.Windows.Media.Brushes.DimGray;
                message = text;
            }

            StatusText.Text = message;
            StatusText.Foreground = color;
        }

        private static string StripLeadingTimestamp(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            if (!text.StartsWith("[", StringComparison.Ordinal))
                return text;

            int end = text.IndexOf(']');
            if (end <= 0 || end > 32)
                return text;

            string ts = text.Substring(1, end - 1);
            if (DateTime.TryParseExact(ts, "HH:mm:ss.fff", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out _)
                || DateTime.TryParseExact(ts, "HH:mm:ss", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out _))
            {
                return text[(end + 1)..].TrimStart();
            }

            return text;
        }

        private void ApplyPatchModeFromFile(string patchPath)
        {
            bool? dirModeHint = null;

            try
            {
                if (SignedPatchReader.TryRead(patchPath, out var signed, out _) && signed != null)
                {
                    dirModeHint = signed.Envelope.Manifest.PatchMode == Core.SignedPatchMode.Directory;

                    AppendConsoleLine(L("log.patchMetaHeader", "[INFO] 补丁元信息："));
                    AppendConsoleLine($"[INFO]   Path={patchPath}");
                    AppendConsoleLine($"[INFO]   Security=Signed");
                    AppendConsoleLine($"[INFO]   Mode={signed.Envelope.Manifest.PatchMode}");
                }
            }
            catch
            {
                // Fall back to legacy metadata reader.
            }

            if (dirModeHint == null)
            {
                var metadata = PatchMetadataReader.Read(patchPath);
                if (metadata == null)
                {
                    AppendConsoleLine($"[WARN] 无法读取补丁元信息：{patchPath}");
                    return;
                }

                AppendConsoleLine(L("log.patchMetaHeader", "[INFO] 补丁元信息："));
                AppendConsoleLine($"[INFO]   Path={patchPath}");
                AppendConsoleLine($"[INFO]   Security={(metadata.IsSecurityPatch ? "Verified" : "Unverified")}");
                AppendConsoleLine($"[INFO]   Mode={(metadata.Mode?.ToString() ?? "Unknown")}");

                if (metadata.Mode == null)
                    return;

                dirModeHint = metadata.Mode == PatchModeHint.Directory;
            }

            bool dirMode = dirModeHint.Value;
            string message = dirMode
                ? L("msg.patchModeDirectory", "检测到目录补丁，已切换到目录模式。")
                : L("msg.patchModeFile", "检测到文件补丁，已切换到文件模式。");

            string previousTarget = DirectoryTextBox.Text.Trim();

            _useDirectoryMode = dirMode;
            PackDirectoryCheckBox.IsChecked = dirMode;

            if (_useDirectoryMode)
            {
                if (Directory.Exists(previousTarget))
                {
                    AppendConsoleLine($"[INFO] 保留已选择目录：{previousTarget}");
                }
                else if (File.Exists(previousTarget))
                {
                    string? dir = Path.GetDirectoryName(previousTarget);
                    if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                    {
                        DirectoryTextBox.Text = dir;
                        AppendConsoleLine($"[INFO] 目录模式：将已选择文件转换为目录：{dir}");
                    }
                    else
                    {
                        DirectoryTextBox.Text = string.Empty;
                        AppendConsoleLine("[WARN] 目录模式：无法从已选择文件解析目录，已清空目标。");
                    }
                }
                else if (!string.IsNullOrWhiteSpace(previousTarget))
                {
                    DirectoryTextBox.Text = string.Empty;
                    AppendConsoleLine("[WARN] 目录模式：已选择目标不存在，已清空目标。");
                }

                UpdateHashDisplay(null);
            }
            else
            {
                if (File.Exists(previousTarget))
                {
                    AppendConsoleLine($"[INFO] 保留已选择文件：{previousTarget}");
                    UpdateHashDisplay(previousTarget);
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(previousTarget))
                        AppendConsoleLine("[WARN] 文件模式：已选择目标不是有效文件，已清空目标。");

                    DirectoryTextBox.Text = string.Empty;
                    UpdateHashDisplay(null);
                }
            }

            ApplyLocalization();
            AppendConsoleLine($"[INFO] {message}");
        }

        private void UpdateHashDisplay(string? path)
        {
            if (_useDirectoryMode)
            {
                _crc32 = _md5 = _sha1 = null;
                SetHashTexts("N/A", "N/A", "N/A");
                return;
            }

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                _crc32 = _md5 = _sha1 = null;
                SetHashTexts("N/A", "N/A", "N/A");
                return;
            }

            var hashes = ComputeHashes(path, new UiPatchLogger(AppendConsoleLine));
            if (hashes == null)
            {
                _crc32 = _md5 = _sha1 = null;
                SetHashTexts("N/A", "N/A", "N/A");
                ShowInfo(L("msg.hashComputeFailed", "计算哈希失败，请重试。"));
                return;
            }

            (_crc32, _md5, _sha1) = hashes.Value;
            SetHashTexts(_crc32!, _md5!, _sha1!);
        }

        private void SetHashTexts(string crc, string md5, string sha1)
        {
            _hashCrcText ??= FindName("HashCrcText") as TextBlock;
            _hashMd5Text ??= FindName("HashMd5Text") as TextBlock;
            _hashSha1Text ??= FindName("HashSha1Text") as TextBlock;

            if (_hashCrcText != null) _hashCrcText.Text = $"CRC32: {crc}";
            if (_hashMd5Text != null) _hashMd5Text.Text = $"MD5: {md5}";
            if (_hashSha1Text != null) _hashSha1Text.Text = $"SHA-1: {sha1}";
        }

        private bool VerifyHashes(string path)
        {
            string? prevCrc = _crc32;
            string? prevMd5 = _md5;
            string? prevSha1 = _sha1;

            var hashes = ComputeHashes(path, new UiPatchLogger(AppendConsoleLine));
            if (hashes == null)
                return false;

            var (crc, md5, sha1) = hashes.Value;
            bool match = string.Equals(crc, prevCrc, StringComparison.OrdinalIgnoreCase)
                      && string.Equals(md5, prevMd5, StringComparison.OrdinalIgnoreCase)
                      && string.Equals(sha1, prevSha1, StringComparison.OrdinalIgnoreCase);

            _crc32 = crc;
            _md5 = md5;
            _sha1 = sha1;
            SetHashTexts(crc, md5, sha1);

            if (!match)
            {
                AppendConsoleLine($"[ERROR] Hash mismatch. Previous: CRC={prevCrc}, MD5={prevMd5}, SHA1={prevSha1}. Current: CRC={crc}, MD5={md5}, SHA1={sha1}");
            }

            return match;
        }

        private static (string crc, string md5, string sha1)? ComputeHashes(string path, IPatchLogger? logger)
        {
            try
            {
                var fi = new FileInfo(path);
                logger?.Info($"计算哈希：{path}（{fi.Length} bytes）");

                uint crc = 0xFFFFFFFF;

                using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
                using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);

                byte[] buffer = new byte[1024 * 1024];
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                int read;
                while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
                {
                    md5.AppendData(buffer, 0, read);
                    sha1.AppendData(buffer, 0, read);

                    for (int i = 0; i < read; i++)
                    {
                        crc = Crc32Table[(crc ^ buffer[i]) & 0xFF] ^ (crc >> 8);
                    }
                }

                crc ^= 0xFFFFFFFF;
                string crcHex = crc.ToString("X8");

                string md5Hex = Convert.ToHexString(md5.GetHashAndReset());
                string sha1Hex = Convert.ToHexString(sha1.GetHashAndReset());

                logger?.Info($"哈希完成：CRC32={crcHex} MD5={md5Hex} SHA1={sha1Hex}");
                return (crcHex, md5Hex, sha1Hex);
            }
            catch (Exception ex)
            {
                logger?.Error($"计算哈希失败：{ex.Message}");
                return null;
            }
        }

        private readonly record struct SelectiveBackupPlan(string[] BackedUpFiles, string[] MissingBeforeFiles);

        private void PreflightCheckPatchTargets(string gameRoot, SelectiveBackupPlan plan, IPatchLogger logger)
        {
            if (string.IsNullOrWhiteSpace(gameRoot))
                throw new ArgumentException("gameRoot is empty.", nameof(gameRoot));

            string[] backedUpFiles = plan.BackedUpFiles ?? Array.Empty<string>();
            int total = backedUpFiles.Length;
            if (total == 0)
            {
                logger.Info("预检：无需修改既存文件，跳过文件占用检查。");
                return;
            }

            var blocked = new System.Collections.Generic.List<string>(capacity: Math.Min(total, 32));

            foreach (string rel in backedUpFiles)
            {
                if (string.IsNullOrWhiteSpace(rel))
                    continue;

                string fullPath = Path.Combine(gameRoot, rel);
                if (!File.Exists(fullPath))
                {
                    logger.Warn($"预检：文件不存在（可能已被删除/移动）：{fullPath}");
                    continue;
                }

                try
                {
                    var attrs = File.GetAttributes(fullPath);
                    if ((attrs & FileAttributes.ReadOnly) != 0)
                    {
                        blocked.Add(rel);
                        logger.Error($"预检：文件为只读，无法写入：{fullPath}");
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    blocked.Add(rel);
                    logger.Error($"预检：读取文件属性失败：{fullPath} ({ex.GetType().Name}: {ex.Message})");
                    continue;
                }

                if (!FileAccessProbe.CanOpenForPatch(fullPath, out int win32Error))
                {
                    blocked.Add(rel);
                    logger.Error($"预检：无法打开目标文件以进行修改（可能被占用/无权限）：{fullPath} (Win32Error={win32Error}: {FileAccessProbe.DescribeWin32Error(win32Error)})");
                }
            }

            if (blocked.Count == 0)
            {
                logger.Info($"预检通过：{total} 个既存文件可写入。");
                return;
            }

            const int sampleLimit = 10;
            int take = Math.Min(sampleLimit, blocked.Count);
            var sb = new StringBuilder(capacity: 256);
            for (int i = 0; i < take; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                sb.Append(blocked[i]);
            }
            if (blocked.Count > sampleLimit)
                sb.Append(", ...");
            string sample = sb.ToString();

            logger.Error($"预检失败：{blocked.Count}/{total} 个目标文件不可写入（被占用/只读/无权限）。示例：{sample}");
            throw new InvalidOperationException("预检失败：目标目录中存在被占用或不可写入的文件，为避免半途失败已终止。");
        }

        private SelectiveBackupPlan? TryBuildSelectiveBackupPlanByDryRun(string gameRoot, string patchPath, IPatchLogger logger)
        {
            if (string.IsNullOrWhiteSpace(gameRoot) || string.IsNullOrWhiteSpace(patchPath))
                return null;

            try
            {
                var capture = new System.Collections.Generic.List<string>(capacity: 1024);
                var captureLogger = new CaptureOnlyLogger(capture);

                try
                {
                    T3ppDiff.ApplyPatchToDirectory(
                        patchFile: patchPath,
                        targetRoot: gameRoot,
                        logger: captureLogger,
                        dryRun: true);
                }
                catch (Exception ex)
                {
                    logger.Warn($"Dry-run 预扫描失败（将回退其他备份方案）：{ex.Message}");
                    return null;
                }

                var backedUp = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var missing = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int xdeltaTargets = 0;
                int fullTargets = 0;
                int deleteTargets = 0;
                int? entryCount = null;
                int processedEntries = 0;

                foreach (var line in capture)
                {
                    if (TryParseNativeEntryCountLine(line, out int c))
                    {
                        entryCount = c;
                    }

                    if (line.Contains("[T3PP] 处理 ", StringComparison.OrdinalIgnoreCase))
                    {
                        processedEntries++;
                    }

                    if (TryParseNativeDryRunLine(line, out var relPath, out var payloadMode, out var isDelete))
                    {
                        if (isDelete)
                        {
                            deleteTargets++;
                        }
                        else if (payloadMode == 2)
                        {
                            xdeltaTargets++;
                        }
                        else if (payloadMode == 1)
                        {
                            fullTargets++;
                        }

                        AddPlanItem(gameRoot, backedUp, missing, relPath, allowMissing: true);
                        continue;
                    }

                    foreach (var rel in ExtractRelativeFileCandidates(gameRoot, patchPath, line))
                    {
                        // Fallback: only trust paths that already exist in the selected target.
                        // (Avoid polluting the manifest with random "path-like" substrings from logs.)
                        AddPlanItem(gameRoot, backedUp, missing, rel, allowMissing: false);
                    }
                }

                if (backedUp.Count == 0 && missing.Count == 0)
                {
                    if (entryCount == 0)
                    {
                        logger.Info("Dry-run 结果：补丁条目数为 0。");
                        return new SelectiveBackupPlan(Array.Empty<string>(), Array.Empty<string>());
                    }

                    if (entryCount is > 0 && processedEntries > 0)
                    {
                        // 说明原生库已解析并遍历过条目，但没有任何需要实际执行的修改（可能已是新版本/无需应用/删除跳过等）。
                        logger.Info("Dry-run 结果：没有需要修改的文件。");
                        return new SelectiveBackupPlan(Array.Empty<string>(), Array.Empty<string>());
                    }

                    logger.Warn("Dry-run 未解析到可用的文件清单（可能原生库未输出路径），将回退其他备份方案。");
                    return null;
                }

                logger.Info($"Dry-run 清单：将备份文件数={backedUp.Count}，将标记新增文件数={missing.Count}，xdelta3目标={xdeltaTargets}，整文件替换目标={fullTargets}，删除目标={deleteTargets}");
                return new SelectiveBackupPlan(backedUp.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray(),
                    missing.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray());
            }
            catch (Exception ex)
            {
                logger.Warn($"Dry-run 预扫描异常（将回退其他备份方案）：{ex.Message}");
                return null;
            }
        }

        internal (string[] BackedUpFiles, string[] MissingBeforeFiles)? TryBuildSelectiveBackupPlanByDryRunForTools(
            string gameRoot,
            string patchPath,
            IPatchLogger logger)
        {
            SelectiveBackupPlan? plan = TryBuildSelectiveBackupPlanByDryRun(gameRoot, patchPath, logger);
            if (plan == null)
                return null;

            return (plan.Value.BackedUpFiles ?? Array.Empty<string>(), plan.Value.MissingBeforeFiles ?? Array.Empty<string>());
        }

        private static void AddPlanItem(
            string gameRoot,
            System.Collections.Generic.HashSet<string> backedUp,
            System.Collections.Generic.HashSet<string> missing,
            string relPath,
            bool allowMissing)
        {
            if (string.IsNullOrWhiteSpace(relPath))
                return;

            string normalized = relPath.Replace('/', '\\').Trim();
            if (string.IsNullOrWhiteSpace(normalized))
                return;

            string rootFull;
            try
            {
                rootFull = Path.GetFullPath(gameRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                           + Path.DirectorySeparatorChar;
            }
            catch
            {
                return;
            }

            // If native logs absolute paths, normalize them back to relative paths under gameRoot.
            if (Path.IsPathRooted(normalized))
            {
                string fullPath;
                try
                {
                    fullPath = Path.GetFullPath(normalized);
                }
                catch
                {
                    return;
                }

                if (!fullPath.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                    return;

                try
                {
                    normalized = Path.GetRelativePath(rootFull, fullPath).Replace('/', '\\').TrimStart('\\');
                }
                catch
                {
                    return;
                }
            }

            normalized = normalized.TrimStart('\\', '/');
            if (normalized.StartsWith(".\\", StringComparison.Ordinal) || normalized.StartsWith("./", StringComparison.Ordinal))
                normalized = normalized.Substring(2);

            if (string.IsNullOrWhiteSpace(normalized))
                return;

            if (normalized.Contains("..", StringComparison.Ordinal))
                return;

            if (!Path.HasExtension(normalized))
                return;

            if (normalized.Length > 260)
                return;

            if (normalized.Contains(':'))
                return;

            // Filter invalid filename characters (per segment); keep path separators.
            var invalidNameChars = Path.GetInvalidFileNameChars().Where(c => c != '\\' && c != '/').ToArray();
            foreach (var segment in normalized.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (segment.IndexOfAny(invalidNameChars) >= 0)
                    return;
            }

            foreach (char ch in normalized)
            {
                if (ch < 0x20)
                    return;
            }

            string full = Path.Combine(gameRoot, normalized);
            if (File.Exists(full))
                backedUp.Add(normalized);
            else if (allowMissing)
                missing.Add(normalized);
        }

        private static bool TryParseNativeDryRunLine(string line, out string relPath, out int payloadMode, out bool isDelete)
        {
            relPath = string.Empty;
            payloadMode = 0;
            isDelete = false;

            if (string.IsNullOrWhiteSpace(line))
                return false;

            // Examples (from T3ppNative.dll):
            // [T3PP] [DryRun] 将对 xxx\yyy.bin 应用 payloadMode=2
            // [T3PP] [DryRun] apply to xxx\yyy.bin payloadMode=2
            // [T3PP] [DryRun] 将删除：xxx\old.dat
            // [T3PP] [DryRun] delete: xxx\old.dat

            int modeIdx = line.IndexOf("payloadMode=", StringComparison.OrdinalIgnoreCase);
            if (modeIdx >= 0)
            {
                int pathStart = -1;
                int prefixIdx = line.LastIndexOf("将对 ", modeIdx, StringComparison.Ordinal);
                if (prefixIdx >= 0)
                    pathStart = prefixIdx + "将对 ".Length;
                else
                {
                    prefixIdx = line.LastIndexOf("apply to ", modeIdx, StringComparison.OrdinalIgnoreCase);
                    if (prefixIdx >= 0)
                        pathStart = prefixIdx + "apply to ".Length;
                }

                if (pathStart >= 0 && pathStart < modeIdx)
                {
                    string between = line.Substring(pathStart, modeIdx - pathStart);
                    // Trim "应用"/"with" words if present.
                    int cut = between.LastIndexOf(" 应用 ", StringComparison.Ordinal);
                    if (cut >= 0)
                        between = between.Substring(0, cut);
                    cut = between.LastIndexOf(" with ", StringComparison.OrdinalIgnoreCase);
                    if (cut >= 0)
                        between = between.Substring(0, cut);

                    relPath = between.Trim();

                    string modePart = line.Substring(modeIdx + "payloadMode=".Length).Trim();
                    int end = 0;
                    while (end < modePart.Length && char.IsDigit(modePart[end]))
                        end++;
                    if (end > 0 && int.TryParse(modePart.Substring(0, end), out int m))
                        payloadMode = m;

                    return !string.IsNullOrWhiteSpace(relPath);
                }
            }

            int delIdx = line.IndexOf("将删除：", StringComparison.Ordinal);
            if (delIdx >= 0)
            {
                relPath = line.Substring(delIdx + "将删除：".Length).Trim();
                isDelete = true;
                return !string.IsNullOrWhiteSpace(relPath);
            }

            delIdx = line.IndexOf("delete:", StringComparison.OrdinalIgnoreCase);
            if (delIdx >= 0)
            {
                relPath = line.Substring(delIdx + "delete:".Length).Trim();
                isDelete = true;
                return !string.IsNullOrWhiteSpace(relPath);
            }

            return false;
        }

        private static bool TryParseNativeEntryCountLine(string line, out int entryCount)
        {
            entryCount = 0;

            if (string.IsNullOrWhiteSpace(line))
                return false;

            // Examples (from T3ppNative.dll):
            // [T3PP] entry 数量：4
            // [T3PP] entry count: 4

            int idx = line.IndexOf("entry 数量", StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                idx = line.IndexOf("entry count", StringComparison.OrdinalIgnoreCase);

            if (idx < 0)
                return false;

            int i = idx;
            while (i < line.Length && !char.IsDigit(line[i]))
                i++;

            if (i >= line.Length)
                return false;

            int j = i;
            while (j < line.Length && char.IsDigit(line[j]))
                j++;

            string num = line.Substring(i, j - i);
            return int.TryParse(num, NumberStyles.Integer, CultureInfo.InvariantCulture, out entryCount);
        }

        private static System.Collections.Generic.IEnumerable<string> ExtractRelativeFileCandidates(string gameRoot, string patchPath, string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                yield break;

            string root = Path.GetFullPath(gameRoot);
            string patchFull = Path.GetFullPath(patchPath);

            foreach (var candidate in ExtractPathLikeSubstrings(line))
            {
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;

                string cleaned = candidate.Trim().TrimEnd('.', ',', ';', ':', ')', ']', '}');

                if (cleaned.Equals(patchFull, StringComparison.OrdinalIgnoreCase))
                    continue;

                string full;
                try
                {
                    if (Path.IsPathRooted(cleaned))
                    {
                        full = Path.GetFullPath(cleaned);
                    }
                    else
                    {
                        cleaned = cleaned.Replace('/', '\\').TrimStart('\\');
                        full = Path.GetFullPath(Path.Combine(root, cleaned));
                    }
                }
                catch
                {
                    continue;
                }

                if (!full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    continue;

                string rel;
                try
                {
                    rel = Path.GetRelativePath(root, full).Replace('/', '\\').TrimStart('\\');
                }
                catch
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(rel))
                    continue;

                if (!Path.HasExtension(rel))
                    continue;

                if (rel.Contains("..", StringComparison.Ordinal))
                    continue;

                yield return rel;
            }
        }

        private static System.Collections.Generic.IEnumerable<string> ExtractPathLikeSubstrings(string line)
        {
            // 1) If a full absolute path under root is included, capture it with spaces.
            // 2) Otherwise, token-scan common separators/quotes and return path-ish tokens.

            foreach (var token in line.Split(new[] { '"', '\'', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (var part in token.Split(new[] { ' ', '\u3000' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (part.Length < 4 || part.Length > 520)
                        continue;

                    if (part.IndexOf('.') < 0)
                        continue;

                    if (!part.Contains('\\') && !part.Contains('/'))
                        continue;

                    yield return part;
                }
            }

            // Absolute path that may contain spaces: attempt to shrink from any drive-root match.
            for (int i = 0; i + 2 < line.Length; i++)
            {
                if (!char.IsLetter(line[i]) || line[i + 1] != ':' || (line[i + 2] != '\\' && line[i + 2] != '/'))
                    continue;

                string suffix = line.Substring(i).Trim();
                string? best = TryShrinkToValidPath(suffix);
                if (!string.IsNullOrWhiteSpace(best))
                    yield return best!;
            }
        }

        private static string? TryShrinkToValidPath(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            string s = text.Trim().Trim('"', '\'');
            if (s.Length == 0)
                return null;

            // Cap to avoid pathological long lines.
            if (s.Length > 520)
                s = s.Substring(0, 520);

            // Try direct.
            if (LooksLikePath(s))
                return s;

            // Shrink by last whitespace/punctuation until it looks like a path.
            for (int iter = 0; iter < 50; iter++)
            {
                int cut = s.LastIndexOfAny(new[] { ' ', '\t', '\u3000', ')', ']', '}', '"', '\'' });
                if (cut <= 3)
                    break;

                s = s.Substring(0, cut).TrimEnd('.', ',', ';', ':', ')', ']', '}');
                if (LooksLikePath(s))
                    return s;
            }

            return null;
        }

        private static bool LooksLikePath(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return false;

            if (s.Length < 4)
                return false;

            if (s.IndexOf('.') < 0)
                return false;

            if (!Path.IsPathRooted(s))
                return false;

            return true;
        }

        private sealed class CaptureOnlyLogger : IPatchLogger
        {
            private readonly System.Collections.Generic.List<string> _lines;

            public CaptureOnlyLogger(System.Collections.Generic.List<string> lines)
            {
                _lines = lines ?? throw new ArgumentNullException(nameof(lines));
            }

            public void Info(string message) => Add(message);
            public void Warn(string message) => Add(message);
            public void Error(string message) => Add(message);

            private void Add(string message)
            {
                if (string.IsNullOrWhiteSpace(message))
                    return;
                _lines.Add(message);
            }
        }

        private string BuildNativeDllLoadErrorMessage(Exception ex)
        {
            try
            {
                string? exeDir = Path.GetDirectoryName(Environment.ProcessPath ?? string.Empty);
                if (string.IsNullOrWhiteSpace(exeDir))
                    exeDir = AppDomain.CurrentDomain.BaseDirectory;

                string dllPath = Path.Combine(exeDir, "T3ppNative.dll");
                if (!File.Exists(dllPath))
                {
                    return LF(
                        "msg.nativeDllMissing",
                        "缺少必要组件：T3ppNative.dll。\n\n请确认：\n1) 解压后的目录里同时存在 PatchGUI.exe 和 T3ppNative.dll（不要只拷贝 exe 单独运行）\n2) 你下载的是 win-x64 版本，并在 64 位 Windows 上运行。\n\n详细日志：{0}",
                        GetLogPathHint());
                }

                if (IsLikelyDebugNativeDll(dllPath))
                {
                return LF(
                    "msg.nativeDllDebugBuild",
                    "修补游戏时产生了个错误。\n\n请不要发送此界面的截图。\n请给开发者提供错误日志：{0}",
                    GetLogPathHint());
            }

                int? win32 = TryLoadLibraryAndGetLastError(dllPath);
                if (win32 == 193)
                {
                return LF(
                    "msg.nativeDllBadFormat",
                    "修补游戏时产生了个错误。\n\n请不要发送此界面的截图。\n请给开发者提供错误日志：{0}",
                    GetLogPathHint());
            }

            return LF(
                "msg.nativeDllLoadFailed",
                "修补游戏时产生了个错误。\n\n请不要发送此界面的截图。\n错误码：{0}\n请给开发者提供错误日志：{1}",
                (win32?.ToString(CultureInfo.CurrentCulture) ?? "unknown"),
                GetLogPathHint());
            }
            catch
            {
                return LF("msg.applyPatchFailed", "补丁应用过程中发生错误：{0}", ex.Message);
            }
        }

        private string BuildNativeDllBadFormatMessage(Exception ex)
        {
            return LF(
                "msg.nativeDllBadFormat",
                "修补游戏时产生了个错误。\n\n请不要发送此界面的截图。\n请给开发者提供错误日志：{0}",
                GetLogPathHint());
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryW(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        private static int? TryLoadLibraryAndGetLastError(string fullPath)
        {
            try
            {
                IntPtr h = LoadLibraryW(fullPath);
                if (h != IntPtr.Zero)
                {
                    try { FreeLibrary(h); } catch { }
                    return 0;
                }

                return Marshal.GetLastWin32Error();
            }
            catch
            {
                return null;
            }
        }

        private static bool IsLikelyDebugNativeDll(string dllPath)
        {
            try
            {
                byte[] data = File.ReadAllBytes(dllPath);
                string ascii = Encoding.ASCII.GetString(data);

                return ascii.Contains("ucrtbased.dll", StringComparison.OrdinalIgnoreCase)
                    || ascii.Contains("MSVCP140D.dll", StringComparison.OrdinalIgnoreCase)
                    || ascii.Contains("VCRUNTIME140D.dll", StringComparison.OrdinalIgnoreCase)
                    || ascii.Contains("VCRUNTIME140_1D.dll", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static uint[] BuildCrc32Table()
        {
            const uint poly = 0xEDB88320;
            var table = new uint[256];
            for (uint i = 0; i < table.Length; i++)
            {
                uint entry = i;
                for (int j = 0; j < 8; j++)
                {
                    if ((entry & 1) == 1)
                        entry = (entry >> 1) ^ poly;
                    else
                        entry >>= 1;
                }
                table[i] = entry;
            }
            return table;
        }

        private static void TryDeleteDirectory(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
                // Swallow cleanup failures; temp folders will be purged by the OS.
            }
        }

        private void ApplyLocalization()
        {
            string localizedTitle = L("window.title", "汉化补丁器");
            Title = localizedTitle;

            PatchTabRadio.Content = L("nav.patch", "补丁");
            GenerateTabRadio.Content = L("nav.generate", "生成");
            SettingsTabRadio.Content = L("nav.settings", "设置");

            GameSelectButton.Content = L("button.gameSelect", "游戏选择");
            if (_isManualMode)
            {
                SelectedGameText.Text = L("label.currentGameManual", "当前游戏：手动模式");
            }
            else if (!string.IsNullOrWhiteSpace(_currentGameName))
            {
                SelectedGameText.Text = $"{L("label.currentGamePrefix", "当前游戏：")}{_currentGameName}";
            }
            else
            {
                SelectedGameText.Text = L("label.currentGame", "当前游戏：未选择");
            }

            SelectDirButton.Content = L("button.selectDir", "选择目录");
            SelectDirButton.ToolTip = L("tooltip.selectDir", "浏览游戏安装目录");
            DirectoryTextBox.PlaceholderText = L("placeholder.gameDir", "请选择游戏根目录...");
            RunPatchButton.Content = L("button.runPatch", "执行补丁");
            RestoreBackupButton.Content = L("button.restoreBackup", "恢复备份");

            RunLogLabel.Text = L("label.runLog", "运行日志");
            StatusText.Text = L("status.ready", "就绪");

            GenSelectSourceButton.Content = L("gen.button.source", "选择目录");
            GenSelectSourceButton.ToolTip = L("gen.tooltip.source", "选择修改前的原始目录");
            GenSourceBox.PlaceholderText = L("gen.placeholder.source", "选择修改前的目录...");

            GenSelectTargetButton.Content = L("gen.button.target", "选择目录");
            GenSelectTargetButton.ToolTip = L("gen.tooltip.target", "选择修改后的输出目录");
            GenTargetBox.PlaceholderText = L("gen.placeholder.target", "选择修改后的目录...");

            PackDirectoryCheckBox.Content = L("gen.packDirectory", "对目录进行打包");
            PackDirectoryCheckBox.ToolTip = L("gen.packDirectory.tooltip", "勾选：按目录进行差分打包；取消：按文件列表模式生成差分。");
            AddWatermarkCheckBox.Content = L("gen.addWatermark", "添加水印");
            AddWatermarkCheckBox.ToolTip = L("gen.addWatermark.tooltip", "在生成的补丁末尾追加水印字符串。");

            GenStartDiffButton.Content = L("gen.button.start", "开始生成");
            GenLogLabel.Text = L("gen.log.title", "生成日志");
            GenStatusText.Text = L("gen.status.waiting", "等待操作");


            SettingsTitleText.Text = L("settings.title", "设置");
            LanguageSectionTitle.Text = L("settings.subtitle", "基础设置");

            AppearanceSectionTitleText.Text = L("settings.appearanceSection", "外观");
            ThemeSectionLabel.Text = L("settings.theme.label", "切换主题");
            ThemeSectionDescriptionText.Text = L("settings.theme.desc", "切换深色/浅色主题");
            ThemeToggleButton.ToolTip = L("settings.theme.tooltip", "切换深色/浅色主题");

            LanguageSectionHeaderText.Text = L("settings.languageSection", "语言切换");
            LanguageLabel.Text = L("settings.languageLabel", "界面语言");
            LanguageDescriptionText.Text = L("settings.language.desc", "切换界面显示语言");

            ApplyDebugSettingsLocalization();
            ApplyKeysLocalization();

            foreach (var item in LanguageSelector.Items)
            {
                if (item is ComboBoxItem cbItem)
                {
                    string tag = cbItem.Tag?.ToString() ?? string.Empty;
                    bool isZh = tag.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
                    cbItem.Content = isZh
                        ? L("settings.language.zh", "中文")
                        : L("settings.language.en", "English");
                }
            }

            // Keep selector aligned with current language
            for (int i = 0; i < LanguageSelector.Items.Count; i++)
            {
                if (LanguageSelector.Items[i] is ComboBoxItem cbItem &&
                    string.Equals(cbItem.Tag?.ToString(), LocalizationManager.Instance.CurrentLanguage, StringComparison.OrdinalIgnoreCase))
                {
                    LanguageSelector.SelectedIndex = i;
                    break;
                }
            }

            GameSelectButton.Content = L("button.gameSelect", "Select Mode");
            SelectedGameText.Text = _useDirectoryMode
                ? L("label.currentModeDirectory", "Current mode: Directory")
                : L("label.currentModeFile", "Current mode: File");

            if (GameSelectButton.ContextMenu is ContextMenu menu)
            {
                foreach (var item in menu.Items)
                {
                    if (item is MenuItem cbItem)
                    {
                        string tag = cbItem.Tag?.ToString() ?? string.Empty;
                        cbItem.Header = string.Equals(tag, DirectoryModeTag, StringComparison.OrdinalIgnoreCase)
                            ? L("menu.mode.directory", "Directory")
                            : L("menu.mode.file", "File");
                    }
                }
            }

            SelectPatchButton.Content = L("button.selectPatch", "Select Patch");
            SelectPatchButton.ToolTip = L("tooltip.selectPatch", "Choose a .t3pp patch file");
            PatchFileTextBox.PlaceholderText = L("placeholder.patchFile", "Select a patch file...");

            PatchTrustHeaderText.Text = L("trust.title", "Patch verification");
            PatchTrustFieldSourceLabel.Text = L("trust.field.source", "Source");
            PatchTrustFieldDistributorLabel.Text = L("trust.field.distributor", "Distributor");
            PatchTrustFieldFingerprintLabel.Text = L("trust.field.fingerprint", "Fingerprint");
            PatchTrustFieldValidityLabel.Text = L("trust.field.validity", "Validity");
            PatchTrustFieldTargetLabel.Text = L("trust.field.target", "Target");

            SelectDirButton.Content = _useDirectoryMode
                ? L("button.selectDir", "Select Target")
                : L("button.selectFile", "Select File");
            SelectDirButton.ToolTip = _useDirectoryMode
                ? L("tooltip.selectDir", "Browse target path")
                : L("tooltip.selectFile", "Browse target file");
            DirectoryTextBox.PlaceholderText = _useDirectoryMode
                ? L("placeholder.gameDir", "Select a target directory...")
                : L("placeholder.gameFile", "Select a target file...");

            string genSourceLabel = _useDirectoryMode
                ? L("gen.button.source", "Select Folder")
                : L("gen.button.source.file", "Select File");
            string genSourceTip = _useDirectoryMode
                ? L("gen.tooltip.source", "Choose the original folder (before changes)")
                : L("gen.tooltip.source.file", "Choose the original file (before changes)");
            string genSourcePlaceholder = _useDirectoryMode
                ? L("gen.placeholder.source", "Pick the original folder...")
                : L("gen.placeholder.source.file", "Pick the original file...");
            GenSelectSourceButton.Content = genSourceLabel;
            GenSelectSourceButton.ToolTip = genSourceTip;
            GenSourceBox.PlaceholderText = genSourcePlaceholder;

            string genTargetLabel = _useDirectoryMode
                ? L("gen.button.target", "Select Folder")
                : L("gen.button.target.file", "Select File");
            string genTargetTip = _useDirectoryMode
                ? L("gen.tooltip.target", "Choose the modified/output folder")
                : L("gen.tooltip.target.file", "Choose the modified file (after changes)");
            string genTargetPlaceholder = _useDirectoryMode
                ? L("gen.placeholder.target", "Pick the modified folder...")
                : L("gen.placeholder.target.file", "Pick the modified file...");
            GenSelectTargetButton.Content = genTargetLabel;
            GenSelectTargetButton.ToolTip = genTargetTip;
            GenTargetBox.PlaceholderText = genTargetPlaceholder;

            PackDirectoryCheckBox.Visibility = _useDirectoryMode ? Visibility.Visible : Visibility.Collapsed;
            SetHashTexts(_crc32 ?? "N/A", _md5 ?? "N/A", _sha1 ?? "N/A");

            WindowTitleBar.Title = localizedTitle;
            RefreshPatchTrustPanelFromCache();
            UpdateTargetSelectionState();
        }

        private void LanguageSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_uiReady)
                return;

            if (LanguageSelector.SelectedItem is ComboBoxItem item)
            {
                string lang = item.Tag?.ToString() ?? "zh";
                if (!string.Equals(LocalizationManager.Instance.CurrentLanguage, lang, StringComparison.OrdinalIgnoreCase))
                {
                    LocalizationManager.LoadLanguage(lang);
                }
                ApplyLocalization();

                // 语言切换后，等待布局更新完成再刷新指示条位置
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                {
                    RefreshNavIndicatorPosition();
                }));
            }
        }

        #region 导航指示条动画

        private const int NavIndicatorAnimationMs = 280;

        /// <summary>
        /// 导航选项卡切换时触发，更新指示条位置
        /// </summary>
        private void NavTab_Checked(object sender, RoutedEventArgs e)
        {
            if (!_uiReady || sender is not System.Windows.Controls.RadioButton selectedTab)
                return;

            // 延迟到布局完成后计算位置
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                AnimateNavIndicatorTo(selectedTab);
            }));
        }

        /// <summary>
        /// 计算目标选项卡的中心位置并动画移动指示条
        /// </summary>
        private void AnimateNavIndicatorTo(System.Windows.Controls.RadioButton targetTab)
        {
            if (NavTabPanel is null || NavIndicator is null || NavIndicatorTranslate is null)
                return;

            // 获取目标选项卡相对于 NavTabPanel 的位置
            System.Windows.Point tabPosition = targetTab.TranslatePoint(new System.Windows.Point(0, 0), NavTabPanel);
            double tabWidth = targetTab.ActualWidth;
            double indicatorWidth = NavIndicator.ActualWidth;

            // 计算指示条应该移动到的 X 位置（居中于选项卡）
            double targetX = tabPosition.X + (tabWidth - indicatorWidth) / 2;

            // 获取当前指示条的 X 位置（支持打断：从动画的当前值开始）
            double currentX = NavIndicatorTranslate.X;

            // 如果位置相同，无需动画
            if (Math.Abs(currentX - targetX) < 0.5)
                return;

            // 创建平滑的位置动画，支持打断（SnapshotAndReplace 会从当前值开始）
            var animation = new DoubleAnimation
            {
                From = currentX,
                To = targetX,
                Duration = TimeSpan.FromMilliseconds(NavIndicatorAnimationMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
                FillBehavior = FillBehavior.HoldEnd
            };

            // 使用 SnapshotAndReplace 支持打断动画
            NavIndicatorTranslate.BeginAnimation(TranslateTransform.XProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }

        /// <summary>
        /// 初始化导航指示条位置（窗口加载完成后调用）
        /// </summary>
        private void InitializeNavIndicator()
        {
            var selectedTab = GetSelectedNavTab();
            if (selectedTab is null)
                return;

            // 计算并设置初始位置（无动画）
            System.Windows.Point tabPosition = selectedTab.TranslatePoint(new System.Windows.Point(0, 0), NavTabPanel);
            double tabWidth = selectedTab.ActualWidth;
            double indicatorWidth = NavIndicator.ActualWidth;
            double targetX = tabPosition.X + (tabWidth - indicatorWidth) / 2;

            NavIndicatorTranslate.X = targetX;
        }

        /// <summary>
        /// 刷新导航指示条位置（语言切换等场景使用，带动画）
        /// </summary>
        private void RefreshNavIndicatorPosition()
        {
            var selectedTab = GetSelectedNavTab();
            if (selectedTab is not null)
            {
                AnimateNavIndicatorTo(selectedTab);
            }
        }

        /// <summary>
        /// 获取当前选中的导航选项卡
        /// </summary>
        private System.Windows.Controls.RadioButton? GetSelectedNavTab()
        {
            if (NavTabPanel is null || NavIndicator is null || NavIndicatorTranslate is null)
                return null;

            foreach (var child in NavTabPanel.Children)
            {
                if (child is System.Windows.Controls.RadioButton rb && rb.IsChecked == true)
                {
                    return rb;
                }
            }
            return null;
        }

        #endregion

        #region 主题切换

        private bool _isDarkTheme = false;
        private long _themeTransitionId = 0;
        private const int ThemeTransitionFadeMs = 260;

        private BitmapSource? CaptureThemeTransitionFrame()
        {
            if (RootGrid is not FrameworkElement target)
                return null;

            if (target.ActualWidth <= 0 || target.ActualHeight <= 0)
                return null;

            var dpi = VisualTreeHelper.GetDpi(target);
            int pixelWidth = Math.Max(1, (int)Math.Ceiling(target.ActualWidth * dpi.DpiScaleX));
            int pixelHeight = Math.Max(1, (int)Math.Ceiling(target.ActualHeight * dpi.DpiScaleY));

            var rtb = new RenderTargetBitmap(pixelWidth, pixelHeight, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            rtb.Render(target);
            rtb.Freeze();
            return rtb;
        }

        private BitmapSource? CaptureFrameWithoutTransitionOverlay()
        {
            if (RootGrid is not FrameworkElement target || ThemeTransitionOverlay is null)
                return CaptureThemeTransitionFrame();

            var previousVisibility = ThemeTransitionOverlay.Visibility;
            ThemeTransitionOverlay.Visibility = Visibility.Collapsed;

            target.UpdateLayout();
            BitmapSource? captured = CaptureThemeTransitionFrame();

            ThemeTransitionOverlay.Visibility = previousVisibility;
            target.UpdateLayout();

            return captured;
        }

        private static BitmapSource? ComposeFrame(BitmapSource baseFrame, BitmapSource overlayFrame, double overlayOpacity)
        {
            overlayOpacity = Math.Clamp(overlayOpacity, 0.0, 1.0);
            if (overlayOpacity <= 0.0)
                return baseFrame;

            var dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                var rect = new Rect(0, 0, baseFrame.PixelWidth, baseFrame.PixelHeight);
                dc.DrawImage(baseFrame, rect);
                dc.PushOpacity(overlayOpacity);
                dc.DrawImage(overlayFrame, rect);
                dc.Pop();
            }

            double dpiX = baseFrame.DpiX > 0 ? baseFrame.DpiX : 96.0;
            double dpiY = baseFrame.DpiY > 0 ? baseFrame.DpiY : 96.0;
            var composed = new RenderTargetBitmap(baseFrame.PixelWidth, baseFrame.PixelHeight, dpiX, dpiY, PixelFormats.Pbgra32);
            composed.Render(dv);
            composed.Freeze();
            return composed;
        }

        private void BeginThemeTransition(Action applyTheme)
        {
            if (!IsLoaded || ThemeTransitionOverlay is null || ThemeTransitionImage is null)
            {
                applyTheme?.Invoke();
                return;
            }

            // 立即停止任何正在进行的过渡动画，避免快速点击时的重影问题
            ThemeTransitionOverlay.BeginAnimation(UIElement.OpacityProperty, null);
            ThemeTransitionOverlay.Visibility = Visibility.Collapsed;
            ThemeTransitionOverlay.Opacity = 0;
            ThemeTransitionImage.Source = null;

            // 强制完成布局更新，确保旧的过渡层完全清除
            Dispatcher.Invoke(() => { }, DispatcherPriority.Render);

            // 捕获当前干净的画面
            BitmapSource? snapshot = CaptureThemeTransitionFrame();

            if (snapshot is null)
            {
                applyTheme?.Invoke();
                return;
            }

            ThemeTransitionImage.Source = snapshot;
            ThemeTransitionOverlay.Visibility = Visibility.Visible;
            ThemeTransitionOverlay.Opacity = 1.0;

            Dispatcher.Invoke(() => { }, DispatcherPriority.Render);

            applyTheme?.Invoke();

            long token = Interlocked.Increment(ref _themeTransitionId);

            var fade = new DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(ThemeTransitionFadeMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
                FillBehavior = FillBehavior.HoldEnd,
            };

            fade.Completed += (_, _) =>
            {
                if (token != Interlocked.Read(ref _themeTransitionId))
                    return;

                ThemeTransitionOverlay.Visibility = Visibility.Collapsed;
                ThemeTransitionOverlay.Opacity = 0.0;
                ThemeTransitionOverlay.BeginAnimation(UIElement.OpacityProperty, null);
                ThemeTransitionImage.Source = null;
            };

            ThemeTransitionOverlay.BeginAnimation(UIElement.OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
        }

        private void ThemeToggleButton_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (!_uiReady)
                return;

            if (sender is ToggleButton btn)
            {
                bool isDark = btn.IsChecked == true;
                _isDarkTheme = isDark;

                if (isDark)
                {
                    SwitchToDarkTheme();
                }
                else
                {
                    SwitchToLightTheme();
                }
            }
        }

        private void SwitchToDarkTheme()
        {
            const int duration = 500;

            BeginThemeTransition(() =>
            {
                ApplicationThemeManager.Apply(ApplicationTheme.Dark, Wpf.Ui.Controls.WindowBackdropType.Mica, true);

                // 背景色（深色模式使用较深的灰色，保持层次感）
                AnimationHelper.ResBrushBeginAnimation("WindowBackgroundBrush", (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1C1C1C"), duration);
                AnimationHelper.ResBrushBeginAnimation("ApplicationBackgroundBrush", (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1C1C1C"), duration);

                // 文字颜色
                AnimationHelper.ResBrushBeginAnimation("TextFillColorPrimaryBrush", (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F5F5F5"), duration);
                AnimationHelper.ResBrushBeginAnimation("TextFillColorSecondaryBrush", (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#B0B0B0"), duration);
                AnimationHelper.ResBrushBeginAnimation("TextFillColorTertiaryBrush", (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#8A8A8A"), duration);
                AnimationHelper.ResBrushBeginAnimation("TextFillColorDisabledBrush", (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#606060"), duration);
                AnimationHelper.ResBrushBeginAnimation("PrimaryTextBrush", (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F5F5F5"), duration);
                AnimationHelper.ResBrushBeginAnimation("SecondaryTextBrush", (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#B0B0B0"), duration);

                // 卡片样式（微妙的层次差异）
                AnimationHelper.ResBrushBeginAnimation("CardBackgroundBrush", (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#252525"), duration);
                AnimationHelper.ResBrushBeginAnimation("CardBorderBrush", (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3D3D3D"), duration);
                AnimationHelper.ResBrushBeginAnimation("CardBrush", (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2D2D2D"), duration);

                // 导航栏
                AnimationHelper.ResBrushBeginAnimation("NavTextBrush", (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#D4D4D4"), duration);
                AnimationHelper.ResBrushBeginAnimation("NavHoverBackgroundBrush", (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#15FFFFFF"), duration);

                // 分隔线
                AnimationHelper.ResBrushBeginAnimation("DividerBrush", (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#404040"), duration);

                try
                {
                    var hwnd = new WindowInteropHelper(this).Handle;
                    NativeThemeMethods.SetWindowFrameTheme(hwnd, true);
                }
                catch { }
            });
        }

        private void SwitchToLightTheme()
        {
            const int duration = 500;

            BeginThemeTransition(() =>
            {
                ApplicationThemeManager.Apply(ApplicationTheme.Light, Wpf.Ui.Controls.WindowBackdropType.Mica, true);

                // 背景色（浅色模式使用柔和的灰白色）
                AnimationHelper.ResBrushBeginAnimation("WindowBackgroundBrush", (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F8F9FA"), duration);
                AnimationHelper.ResBrushBeginAnimation("ApplicationBackgroundBrush", (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F8F9FA"), duration);

                // 文字颜色
                AnimationHelper.ResBrushBeginAnimation("TextFillColorPrimaryBrush", (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1A1A1A"), duration);
                AnimationHelper.ResBrushBeginAnimation("TextFillColorSecondaryBrush", (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#5C5C5C"), duration);
                AnimationHelper.ResBrushBeginAnimation("TextFillColorTertiaryBrush", (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#7A7A7A"), duration);
                AnimationHelper.ResBrushBeginAnimation("TextFillColorDisabledBrush", (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#9E9E9E"), duration);
                AnimationHelper.ResBrushBeginAnimation("PrimaryTextBrush", (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1A1A1A"), duration);
                AnimationHelper.ResBrushBeginAnimation("SecondaryTextBrush", (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#5C5C5C"), duration);

                // 卡片样式
                AnimationHelper.ResBrushBeginAnimation("CardBackgroundBrush", (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FCFCFC"), duration);
                AnimationHelper.ResBrushBeginAnimation("CardBorderBrush", (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E8E8E8"), duration);
                AnimationHelper.ResBrushBeginAnimation("CardBrush", (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFFFFF"), duration);

                // 导航栏
                AnimationHelper.ResBrushBeginAnimation("NavTextBrush", (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2D2D2D"), duration);
                AnimationHelper.ResBrushBeginAnimation("NavHoverBackgroundBrush", (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#0A000000"), duration);

                // 分隔线
                AnimationHelper.ResBrushBeginAnimation("DividerBrush", (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E5E5E5"), duration);

                try
                {
                    var hwnd = new WindowInteropHelper(this).Handle;
                    NativeThemeMethods.SetWindowFrameTheme(hwnd, false);
                }
                catch { }
            });
        }

        #endregion

        private void UpdateTargetSelectionState()
        {
            bool hasPatch = !string.IsNullOrWhiteSpace(_selectedPatchPath);
            SelectDirButton.IsEnabled = hasPatch;
        }

        private sealed class UiPatchLogger : IPatchLogger
        {
            private readonly Action<string> _appendLine;

            public UiPatchLogger(Action<string> appendLine)
            {
                _appendLine = appendLine ?? throw new ArgumentNullException(nameof(appendLine));
            }

            public void Info(string message) => _appendLine($"[INFO] {message}");
            public void Warn(string message) => _appendLine($"[WARN] {message}");
            public void Error(string message) => _appendLine($"[ERROR] {message}");
        }

        #endregion
    }
}

