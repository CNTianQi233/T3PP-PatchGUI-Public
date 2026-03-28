// 该文件负责：离线签名补丁（.t3pp）验证结果的缓存、UI 面板展示，以及在执行补丁前进行“证书式”安全确认/拦截。
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using PatchGUI.Core;

namespace PatchGUI
{
    public partial class MainWindow
    {
        private SignedPatchVerificationReport? _patchTrustBaseReport;
        private string? _patchTrustBaseReportPath;

        private SignedPatchVerificationReport? _patchTrustCurrentReport;
        private string? _patchTrustCurrentReportPath;
        private string? _patchTrustCurrentTargetPath;

        private CancellationTokenSource? _patchTrustRefreshCts;
        private long _patchTrustRefreshToken;

        private void RequestPatchTrustRefresh()
        {
            if (!_uiReady)
                return;

            var logger = new UiPatchLogger(AppendConsoleLine);
            _ = RefreshPatchTrustPanelAsync(logger);
        }

        private async Task RefreshPatchTrustPanelAsync(IPatchLogger logger)
        {
            string patchPath = _selectedPatchPath ?? string.Empty;
            string targetPath = DirectoryTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(patchPath) || !System.IO.File.Exists(patchPath))
            {
                _patchTrustCurrentReport = null;
                _patchTrustCurrentReportPath = null;
                _patchTrustCurrentTargetPath = null;
                Dispatcher.Invoke(() => ApplyPatchTrustReportToUi(null));
                return;
            }

            _patchTrustRefreshCts?.Cancel();
            _patchTrustRefreshCts?.Dispose();
            _patchTrustRefreshCts = new CancellationTokenSource();
            CancellationToken ct = _patchTrustRefreshCts.Token;

            long token = Interlocked.Increment(ref _patchTrustRefreshToken);

            SignedPatchVerificationReport? report = null;
            try
            {
                report = await BuildPatchTrustReportAsync(patchPath, targetPath, logger, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.Warn($"补丁签名验证失败（将显示为未验证）：{ex.GetType().Name}: {ex.Message}");
                report = new SignedPatchVerificationReport
                {
                    State = SignedPatchVerificationState.Unsigned,
                    HasSignatureBlock = false
                };
            }

            if (ct.IsCancellationRequested || token != Interlocked.Read(ref _patchTrustRefreshToken))
                return;

            _patchTrustCurrentReport = report;
            _patchTrustCurrentReportPath = patchPath;
            _patchTrustCurrentTargetPath = targetPath;

            Dispatcher.Invoke(() => ApplyPatchTrustReportToUi(report));
        }

        private async Task<SignedPatchVerificationReport> BuildPatchTrustReportAsync(
            string patchPath,
            string targetPath,
            IPatchLogger logger,
            CancellationToken ct)
        {
            SignedPatchVerificationReport baseReport;
            if (_patchTrustBaseReport != null
                && !string.IsNullOrWhiteSpace(_patchTrustBaseReportPath)
                && string.Equals(_patchTrustBaseReportPath, patchPath, StringComparison.OrdinalIgnoreCase))
            {
                baseReport = _patchTrustBaseReport;
            }
            else
            {
                baseReport = await Task.Run(() => SignedPatchVerifier.Verify(patchPath, targetPath: null, logger), ct);
                if (!ct.IsCancellationRequested)
                {
                    _patchTrustBaseReport = baseReport;
                    _patchTrustBaseReportPath = patchPath;
                }
            }

            if (string.IsNullOrWhiteSpace(targetPath) || baseReport.Manifest == null)
                return baseReport;

            var (check, issues) = await Task.Run(() => SignedPatchVerifier.VerifyTargetOnly(targetPath, baseReport.Manifest, logger), ct);
            return CloneWithTarget(baseReport, check, issues);
        }

        private static SignedPatchVerificationReport CloneWithTarget(
            SignedPatchVerificationReport report,
            SignedPatchTargetCheck check,
            System.Collections.Generic.IReadOnlyList<string>? issues)
        {
            return new SignedPatchVerificationReport
            {
                State = report.State,
                FailureReason = report.FailureReason,
                HasSignatureBlock = report.HasSignatureBlock,
                SignatureValid = report.SignatureValid,
                PatchPayloadHashValid = report.PatchPayloadHashValid,
                PublisherTrusted = report.PublisherTrusted,
                CertificateValidity = report.CertificateValidity,
                PublisherFingerprintSha256Hex = report.PublisherFingerprintSha256Hex,
                Publisher = report.Publisher,
                Manifest = report.Manifest,
                TargetCheck = check,
                TargetIssues = issues,
                HasLegacySecurityMark = report.HasLegacySecurityMark
            };
        }

        private void ApplyPatchTrustReportToUi(SignedPatchVerificationReport? report)
        {
            if (PatchTrustPanel == null)
                return;

            if (report == null || string.IsNullOrWhiteSpace(_selectedPatchPath))
            {
                PatchTrustPanel.Visibility = Visibility.Collapsed;
                return;
            }

            PatchTrustPanel.Visibility = Visibility.Visible;

            var verified = TryFindResource("TrustVerifiedBrush") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.ForestGreen;
            var warning = TryFindResource("TrustWarningBrush") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.DarkOrange;
            var invalid = TryFindResource("TrustInvalidBrush") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.IndianRed;

            System.Windows.Media.Brush brush;
            string status;
            string summary;

            switch (report.State)
            {
                case SignedPatchVerificationState.Verified:
                    brush = verified;
                    status = L("trust.status.verified", "Verified");
                    summary = L("trust.summary.verified", "Trusted publisher. Signature and payload verified.");
                    break;
                case SignedPatchVerificationState.Untrusted:
                    brush = warning;
                    status = L("trust.status.untrusted", "Untrusted");
                    summary = L("trust.summary.untrusted", "Signed, but the publisher is not trusted (or the certificate is not valid).");
                    break;
                case SignedPatchVerificationState.Invalid:
                    brush = invalid;
                    status = L("trust.status.invalid", "Invalid (blocked)");
                    summary = string.IsNullOrWhiteSpace(report.FailureReason)
                        ? L("trust.summary.invalid", "Signature/payload verification failed. The patch will be blocked.")
                        : $"{L("trust.summary.invalid", "Signature/payload verification failed. The patch will be blocked.")}\n{report.FailureReason}";
                    break;
                default:
                    brush = warning;
                    status = L("trust.status.unsigned", "Unsigned");
                    summary = report.HasLegacySecurityMark
                        ? L("trust.summary.unsigned.legacy", "Legacy mark detected, but it is not a cryptographic signature. Treat as unverified.")
                        : L("trust.summary.unsigned", "No signature. Cannot verify origin or integrity offline.");
                    break;
            }

            if (report.HasSignatureBlock && report.TargetCheck != SignedPatchTargetCheck.NotChecked)
            {
                summary += "\n" + BuildTargetSummary(report);
            }

            PatchTrustStatusDot.Fill = brush;
            PatchTrustStatusText.Text = status;
            PatchTrustStatusText.Foreground = brush;
            PatchTrustSummaryText.Text = summary;

            PatchTrustFieldSourceValue.Text = report.Publisher?.Source ?? "-";
            PatchTrustFieldDistributorValue.Text = report.Publisher?.Distributor ?? "-";
            PatchTrustFieldFingerprintValue.Text = report.PublisherFingerprintSha256Hex ?? "-";

            PatchTrustFieldValidityValue.Text = BuildValidityText(report);
            PatchTrustFieldTargetValue.Text = BuildTargetText(report);
        }

        private string BuildValidityText(SignedPatchVerificationReport report)
        {
            if (!report.HasSignatureBlock || report.Publisher == null)
                return "-";

            string validity = report.CertificateValidity switch
            {
                SignedPatchCertificateValidity.Valid => L("trust.cert.valid", "Valid"),
                SignedPatchCertificateValidity.NotYetValid => L("trust.cert.notYetValid", "Not yet valid"),
                SignedPatchCertificateValidity.Expired => L("trust.cert.expired", "Expired"),
                _ => L("trust.cert.unknown", "Unknown")
            };

            string from = report.Publisher.NotBeforeUtc == default ? "-" : report.Publisher.NotBeforeUtc.ToLocalTime().ToString("yyyy-MM-dd");
            string to = report.Publisher.NotAfterUtc == default ? "-" : report.Publisher.NotAfterUtc.ToLocalTime().ToString("yyyy-MM-dd");

            return $"{from} — {to} ({validity})";
        }

        private string BuildTargetText(SignedPatchVerificationReport report)
        {
            if (!report.HasSignatureBlock || report.Manifest == null)
                return "-";

            return report.TargetCheck switch
            {
                SignedPatchTargetCheck.NotChecked => L("trust.target.notChecked", "Not checked"),
                SignedPatchTargetCheck.Match => L("trust.target.match", "Match"),
                SignedPatchTargetCheck.Mismatch => L("trust.target.mismatch", "Mismatch"),
                SignedPatchTargetCheck.Error => L("trust.target.error", "Error"),
                _ => L("trust.target.notChecked", "Not checked")
            };
        }

        private string BuildTargetSummary(SignedPatchVerificationReport report)
        {
            if (report.TargetCheck == SignedPatchTargetCheck.Match)
                return L("trust.summary.target.match", "Target check: match.");

            if (report.TargetCheck == SignedPatchTargetCheck.Mismatch)
            {
                int count = report.TargetIssues?.Count ?? 0;
                if (count <= 0)
                    return L("trust.summary.target.mismatch", "Target check: mismatch.");

                string sample = string.Join(", ", report.TargetIssues!.Take(3));
                if (count > 3)
                    sample += ", ...";

                return string.Format(L("trust.summary.target.mismatchDetail", "Target check: mismatch ({0})."), sample);
            }

            if (report.TargetCheck == SignedPatchTargetCheck.Error)
                return L("trust.summary.target.error", "Target check: error.");

            return string.Empty;
        }

        private void RefreshPatchTrustPanelFromCache()
        {
            if (_patchTrustCurrentReport == null)
                return;

            ApplyPatchTrustReportToUi(_patchTrustCurrentReport);
        }

        private async Task<bool> ConfirmPatchTrustBeforeApplyAsync(string patchPath, string targetPath, IPatchLogger logger)
        {
            SignedPatchVerificationReport report;
            try
            {
                report = await BuildPatchTrustReportAsync(patchPath, targetPath, logger, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.Warn($"补丁签名验证失败（将按未验证处理）：{ex.Message}");
                report = new SignedPatchVerificationReport { State = SignedPatchVerificationState.Unsigned, HasSignatureBlock = false };
            }

            _patchTrustCurrentReport = report;
            _patchTrustCurrentReportPath = patchPath;
            _patchTrustCurrentTargetPath = targetPath;
            ApplyPatchTrustReportToUi(report);

            // Hard block: invalid signature or payload tampering.
            if (report.State == SignedPatchVerificationState.Invalid)
            {
                logger.Error($"补丁签名/主体校验失败，已拒绝执行：{report.FailureReason}");
                ShowError(LF("trust.block.invalid", "补丁签名或补丁主体校验失败，已拒绝执行：{0}", report.FailureReason ?? "-"));
                return false;
            }

            // Signed patches: target mismatch is dangerous; default is "No".
            if (report.HasSignatureBlock && (report.TargetCheck == SignedPatchTargetCheck.Mismatch || report.TargetCheck == SignedPatchTargetCheck.Error))
            {
                string details = report.TargetIssues is { Count: > 0 }
                    ? string.Join("\n", report.TargetIssues.Take(8))
                    : "-";

                var result = System.Windows.MessageBox.Show(
                    this,
                    LF("trust.warn.targetMismatch", "补丁与当前目标不匹配，继续可能导致文件损坏。\n\n问题示例：\n{0}\n\n仍要继续吗？", details),
                    L("title.error", "错误"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);

                if (result != MessageBoxResult.Yes)
                    return false;
            }

            // Unsigned / untrusted: warn and ask user to confirm (default "No").
            if (report.State != SignedPatchVerificationState.Verified)
            {
                string warning = report.HasSignatureBlock
                    ? L("trust.warn.untrusted", "该补丁已签名，但发布者不受信任（或证书无效）。继续前请确认来源可信。\n\n仍要继续吗？")
                    : L("trust.warn.unsigned", "该补丁未签名，无法离线验证来源与完整性。\n\n仍要继续吗？");

                var result = System.Windows.MessageBox.Show(
                    this,
                    warning,
                    L("title.info", "提示"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);

                if (result != MessageBoxResult.Yes)
                    return false;
            }

            return true;
        }
    }
}
