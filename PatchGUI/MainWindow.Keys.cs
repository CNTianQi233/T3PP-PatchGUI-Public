using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Runtime.InteropServices;
using PatchGUI.Core;

namespace PatchGUI
{
    public partial class MainWindow
    {
        partial void InitializeKeysPage()
        {
#if DEBUG
            // Set default validity period (today to 1 year from now)
            CertValidFromPicker.SelectedDate = DateTime.Today;
            CertValidToPicker.SelectedDate = DateTime.Today.AddYears(1);
#endif
        }

#if DEBUG
        // ---------------------------------------------------------
        // Keypair / Certificate / Signing
        // ---------------------------------------------------------

        private SignedPatchPrivateKeyV1? _currentPrivateKey;

        private void KeysGenerateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var (priv, pub, fp) = SignedPatchSigner.GenerateKeyPair();
                _currentPrivateKey = priv;
                KeysFingerprintBox.Text = fp;

                // Auto-generate serial number
                string serial = Guid.NewGuid().ToString("N")[..16].ToUpperInvariant();
                CertSerialBox.Text = serial;

                AppendConsoleLine($"[Keys] Keypair generated. Fingerprint: {fp}, Serial: {serial}");
            }
            catch (Exception ex)
            {
                AppendConsoleLine($"[Keys] Error generating keypair: {ex.Message}");
            }
        }

        private void KeysLoadKeyButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = L("keys.dialog.loadKey.title", "Load private key"),
                Filter = L("keys.dialog.loadKey.filter", "JSON (*.json)|*.json|All files (*.*)|*.*")
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    _currentPrivateKey = SignedPatchSigner.LoadPrivateKey(dialog.FileName);
                    string fp = SignedPatchCrypto.ComputePublicKeyFingerprintHex(_currentPrivateKey.PublicKey);
                    KeysFingerprintBox.Text = fp;
                    AppendConsoleLine($"[Keys] Loaded private key. Fingerprint: {fp}");
                    System.Windows.MessageBox.Show(
                        L("keys.msg.loadKeySuccess", "私钥载入成功。"),
                        L("title.done", "完成"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    AppendConsoleLine($"[Keys] Failed to load key: {ex.Message}");
                    System.Windows.MessageBox.Show(
                        $"{L("keys.msg.loadKeyFailed", "私钥载入失败")}: {ex.Message}",
                        L("title.error", "错误"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }

        private void KeysSaveKeyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPrivateKey == null)
            {
                string msg = L("keys.err.noPrivateKey", "请先生成或载入私钥。");
                AppendConsoleLine($"[Keys] {msg}");
                System.Windows.MessageBox.Show(
                    msg,
                    L("title.error", "错误"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // 安全检查：确保指纹文本框有内容
            string fingerprintText = KeysFingerprintBox.Text?.Trim() ?? "";
            if (fingerprintText.Length < 8)
            {
                string msg = L("keys.err.invalidFingerprint", "私钥指纹无效。");
                AppendConsoleLine($"[Keys] {msg}");
                System.Windows.MessageBox.Show(msg, L("title.error", "错误"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = L("keys.dialog.saveKey.title", "Save private key"),
                Filter = L("keys.dialog.saveKey.filter", "JSON (*.json)|*.json|All files (*.*)|*.*"),
                FileName = $"private_key_{fingerprintText[..8]}.json"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    SignedPatchSigner.SavePrivateKey(dialog.FileName, _currentPrivateKey);
                    AppendConsoleLine($"[Keys] Saved private key to: {dialog.FileName}");
                    System.Windows.MessageBox.Show(
                        L("keys.msg.saveKeySuccess", "私钥保存成功。"),
                        L("title.done", "完成"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    AppendConsoleLine($"[Keys] Failed to save key: {ex.Message}");
                    System.Windows.MessageBox.Show(
                        $"{L("keys.msg.saveKeyFailed", "私钥保存失败")}: {ex.Message}",
                        L("title.error", "错误"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }

        private void KeysSaveCertButton_Click(object sender, RoutedEventArgs e)
        {
            // 验证私钥
            if (_currentPrivateKey == null)
            {
                string msg = L("keys.err.noPrivateKey", "请先生成或载入私钥。");
                AppendConsoleLine($"[Keys] {msg}");
                System.Windows.MessageBox.Show(
                    msg,
                    L("title.error", "错误"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // 验证必填字段
            string source = CertSourceBox.Text.Trim();
            string dist = CertDistributorBox.Text.Trim();
            string serial = CertSerialBox.Text.Trim();

            if (string.IsNullOrEmpty(source))
            {
                string msg = L("keys.err.noSource", "请填写来源（Source）。");
                AppendConsoleLine($"[Keys] {msg}");
                System.Windows.MessageBox.Show(msg, L("title.error", "错误"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(dist))
            {
                string msg = L("keys.err.noDistributor", "请填写分发者（Distributor）。");
                AppendConsoleLine($"[Keys] {msg}");
                System.Windows.MessageBox.Show(msg, L("title.error", "错误"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(serial))
            {
                string msg = L("keys.err.noSerial", "请填写序列号（Serial）。");
                AppendConsoleLine($"[Keys] {msg}");
                System.Windows.MessageBox.Show(msg, L("title.error", "错误"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 验证日期
            DateTime notBefore = CertValidFromPicker.SelectedDate ?? DateTime.Today;
            DateTime notAfter = CertValidToPicker.SelectedDate ?? DateTime.Today.AddYears(10);

            if (notAfter < notBefore)
            {
                string msg = L("keys.err.invalidDateRange", "失效日期不能早于生效日期。");
                AppendConsoleLine($"[Keys] {msg}");
                System.Windows.MessageBox.Show(msg, L("title.error", "错误"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // 直接使用选中的日期，不做任何转换
                var notBeforeUtc = new DateTimeOffset(notBefore.Date, TimeSpan.Zero);
                var notAfterUtc = new DateTimeOffset(notAfter.Date.AddDays(1).AddSeconds(-1), TimeSpan.Zero); // 当天 23:59:59 UTC

                AppendConsoleLine($"[Keys] 日期选择: notBefore={notBefore:yyyy-MM-dd}, notAfter={notAfter:yyyy-MM-dd}");
                AppendConsoleLine($"[Keys] 保存为 UTC: notBeforeUtc={notBeforeUtc:O}, notAfterUtc={notAfterUtc:O}");

                var cert = new SignedPatchPublisherCertificateV1
                {
                    PublicKey = _currentPrivateKey.PublicKey,
                    Source = source,
                    Distributor = dist,
                    SerialNumber = serial,
                    NotBeforeUtc = notBeforeUtc,
                    NotAfterUtc = notAfterUtc
                };

                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = L("keys.dialog.saveCert.title", "Save publisher certificate"),
                    Filter = L("keys.dialog.saveCert.filter", "JSON (*.json)|*.json|All files (*.*)|*.*"),
                    FileName = $"cert_{serial}.json"
                };

                if (dialog.ShowDialog() == true)
                {
                    SignedPatchSigner.SavePublisherCertificate(dialog.FileName, cert);
                    _loadedCert = cert; // 保存后设置为当前证书
                    AppendConsoleLine($"[Keys] Saved certificate to: {dialog.FileName}");
                    System.Windows.MessageBox.Show(
                        L("keys.msg.createCertSuccess", "证书创建成功。"),
                        L("title.done", "完成"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                AppendConsoleLine($"[Keys] Failed to create/save cert: {ex.Message}");
                System.Windows.MessageBox.Show(
                    $"{L("keys.msg.createCertFailed", "证书创建失败")}: {ex.Message}",
                    L("title.error", "错误"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void SignBrowsePatch_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = L("dialog.selectPatch.title", "Select T3PP patch file"),
                Filter = L("filter.patchFile", "Touhou 3rd-party Patch (*.t3pp)|*.t3pp|All files (*.*)|*.*")
            };
            if (dialog.ShowDialog() == true)
            {
                SignPatchInputBox.Text = dialog.FileName;
            }
        }

        private void SignBrowseOutput_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = L("keys.dialog.sign.output.title", "Select output file for signed patch"),
                Filter = L("filter.patchFile", "Touhou 3rd-party Patch (*.t3pp)|*.t3pp|All files (*.*)|*.*"),
                FileName = "signed_patch.t3pp"
            };
            if (dialog.ShowDialog() == true)
            {
                SignOutputBox.Text = dialog.FileName;
            }
        }

        private string? _signTargetPath;
        private SignedPatchPublisherCertificateV1? _loadedCert;

        private void SignBrowseTarget_Click(object sender, RoutedEventArgs e)
        {
            // Determine mode: directory or file based on checkbox or a simple heuristic
            bool isDirectoryMode = PackDirectoryCheckBox?.IsChecked == true;

            if (isDirectoryMode)
            {
                var dialog = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = L("keys.sign.target.dir.title", "Select target directory (original files)"),
                    UseDescriptionForTitle = true
                };
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    _signTargetPath = dialog.SelectedPath;
                    SignTargetBox.Text = dialog.SelectedPath;
                }
            }
            else
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = L("keys.sign.target.file.title", "Select target file (original)"),
                    Filter = L("filter.allFiles", "All files (*.*)|*.*")
                };
                if (dialog.ShowDialog() == true)
                {
                    _signTargetPath = dialog.FileName;
                    SignTargetBox.Text = dialog.FileName;
                }
            }
        }

        private void KeysLoadCertButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = L("keys.dialog.loadCert.title", "Load publisher certificate"),
                Filter = L("keys.dialog.loadCert.filter", "JSON (*.json)|*.json|All files (*.*)|*.*")
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    _loadedCert = SignedPatchSigner.LoadPublisherCertificate(dialog.FileName);
                    CertSourceBox.Text = _loadedCert.Source;
                    CertDistributorBox.Text = _loadedCert.Distributor;
                    CertSerialBox.Text = _loadedCert.SerialNumber;

                    // 设置有效期日期选择器（使用 UTC 日期，避免时区转换导致日期偏移）
                    AppendConsoleLine($"[Keys] 证书 UTC 日期: NotBeforeUtc={_loadedCert.NotBeforeUtc:O}, NotAfterUtc={_loadedCert.NotAfterUtc:O}");
                    if (_loadedCert.NotBeforeUtc != default)
                    {
                        var fromDate = _loadedCert.NotBeforeUtc.UtcDateTime.Date;
                        AppendConsoleLine($"[Keys] 显示生效日期: {fromDate:yyyy-MM-dd}");
                        CertValidFromPicker.SelectedDate = fromDate;
                    }
                    if (_loadedCert.NotAfterUtc != default)
                    {
                        var toDate = _loadedCert.NotAfterUtc.UtcDateTime.Date;
                        AppendConsoleLine($"[Keys] 显示失效日期: {toDate:yyyy-MM-dd}");
                        CertValidToPicker.SelectedDate = toDate;
                    }

                    string fp = SignedPatchCrypto.ComputePublicKeyFingerprintHex(_loadedCert.PublicKey);
                    KeysFingerprintBox.Text = fp;
                    AppendConsoleLine($"[Keys] Loaded certificate: {_loadedCert.Source} / {_loadedCert.Distributor} (fingerprint: {fp})");
                    System.Windows.MessageBox.Show(
                        L("keys.msg.importCertSuccess", "证书导入成功。"),
                        L("title.done", "完成"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    AppendConsoleLine($"[Keys] Failed to load certificate: {ex.Message}");
                    System.Windows.MessageBox.Show(
                        $"{L("keys.msg.importCertFailed", "证书导入失败")}: {ex.Message}",
                        L("title.error", "错误"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }

        private void KeysExportCertButton_Click(object sender, RoutedEventArgs e)
        {
            if (_loadedCert == null)
            {
                AppendConsoleLine(L("keys.err.noCertLoaded", "请先载入证书。"));
                System.Windows.MessageBox.Show(
                    L("keys.err.noCertLoaded", "请先载入证书。"),
                    L("title.error", "错误"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = L("keys.dialog.exportCert.title", "导出证书"),
                Filter = L("keys.dialog.exportCert.filter", "JSON (*.json)|*.json|All files (*.*)|*.*"),
                FileName = $"cert_{_loadedCert.SerialNumber}.json"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    SignedPatchSigner.SavePublisherCertificate(dialog.FileName, _loadedCert);
                    AppendConsoleLine($"[Keys] {L("keys.status.certExported", "证书已导出")}: {dialog.FileName}");
                    System.Windows.MessageBox.Show(
                        L("keys.msg.exportCertSuccess", "证书导出成功。"),
                        L("title.done", "完成"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    AppendConsoleLine($"[Keys] {L("keys.err.exportCertFailed", "导出证书失败")}: {ex.Message}");
                    System.Windows.MessageBox.Show(
                        $"{L("keys.msg.exportCertFailed", "证书导出失败")}: {ex.Message}",
                        L("title.error", "错误"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }

        private async void SignExecuteButton_Click(object sender, RoutedEventArgs e)
        {
            // Collect all UI values on UI thread first
            string inputPatch = SignPatchInputBox?.Text?.Trim() ?? "";
            string outputPatch = SignOutputBox?.Text?.Trim() ?? "";
            string? targetPath = _signTargetPath;
            string source = CertSourceBox?.Text?.Trim() ?? "";
            string dist = CertDistributorBox?.Text?.Trim() ?? "";
            string serial = CertSerialBox?.Text?.Trim() ?? "";
            DateTime notBefore = CertValidFromPicker?.SelectedDate ?? DateTime.Today;
            DateTime notAfter = CertValidToPicker?.SelectedDate ?? DateTime.Today.AddYears(1);
            bool isDirectoryMode = PackDirectoryCheckBox?.IsChecked == true;
            string? notes = SignNotesBox?.Text?.Trim();

            // Validation
            if (string.IsNullOrWhiteSpace(inputPatch))
            {
                string msg = L("keys.err.noPatchInput", "请选择要签名的补丁文件。");
                AppendConsoleLine($"[Keys] {msg}");
                System.Windows.MessageBox.Show(msg, L("title.error", "错误"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!System.IO.File.Exists(inputPatch))
            {
                string msg = L("keys.err.patchFileNotFound", "补丁文件不存在。");
                AppendConsoleLine($"[Keys] {msg}: {inputPatch}");
                System.Windows.MessageBox.Show($"{msg}\n{inputPatch}", L("title.error", "错误"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(outputPatch))
            {
                string msg = L("keys.err.noOutputPath", "请指定签名后补丁的输出路径。");
                AppendConsoleLine($"[Keys] {msg}");
                System.Windows.MessageBox.Show(msg, L("title.error", "错误"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_currentPrivateKey == null)
            {
                string msg = L("keys.err.noPrivateKey", "请先生成或载入私钥。");
                AppendConsoleLine($"[Keys] {msg}");
                System.Windows.MessageBox.Show(msg, L("title.error", "错误"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Build or use loaded certificate
            SignedPatchPublisherCertificateV1 cert;
            if (_loadedCert != null)
            {
                string certFp = SignedPatchCrypto.ComputePublicKeyFingerprintHex(_loadedCert.PublicKey);
                string keyFp = SignedPatchCrypto.ComputePublicKeyFingerprintHex(_currentPrivateKey.PublicKey);
                if (!string.Equals(certFp, keyFp, StringComparison.OrdinalIgnoreCase))
                {
                    string msg = L("keys.err.certKeyMismatch", "已载入的证书与当前私钥不匹配。");
                    AppendConsoleLine($"[Keys] {msg}");
                    System.Windows.MessageBox.Show(msg, L("title.error", "错误"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                cert = _loadedCert;
            }
            else
            {
                if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(dist))
                {
                    string msg = L("keys.err.noCertOrInfo", "请填写来源和分发者，或载入证书。");
                    AppendConsoleLine($"[Keys] {msg}");
                    System.Windows.MessageBox.Show(msg, L("title.error", "错误"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (string.IsNullOrEmpty(serial))
                    serial = Guid.NewGuid().ToString("N")[..16].ToUpperInvariant();

                cert = new SignedPatchPublisherCertificateV1
                {
                    PublicKey = _currentPrivateKey.PublicKey,
                    Source = source,
                    Distributor = dist,
                    SerialNumber = serial,
                    NotBeforeUtc = new DateTimeOffset(notBefore.Year, notBefore.Month, notBefore.Day, 0, 0, 0, TimeSpan.Zero),
                    NotAfterUtc = new DateTimeOffset(notAfter.Year, notAfter.Month, notAfter.Day, 23, 59, 59, TimeSpan.Zero)
                };
            }

            var patchMode = isDirectoryMode ? SignedPatchMode.Directory : SignedPatchMode.File;
            var privateKey = _currentPrivateKey;

            // Lock UI and start progress
            SetUiLocked(true);
            BeginTaskbarProgress();
            SetMainProgress(5);

            bool signSuccess = false;
            string? signError = null;

            try
            {
                AppendConsoleLine($"[Keys] Signing patch: {inputPatch}");

                // Build target info in background
                SignedPatchTargetV1 target;
                if (!string.IsNullOrWhiteSpace(targetPath))
                {
                    AppendConsoleLine($"[Keys] Computing target hashes...");
                    SetMainProgress(10);
                    target = await Task.Run(() => BuildTargetInfo(targetPath, patchMode));
                    SetMainProgress(30);
                }
                else
                {
                    target = new SignedPatchTargetV1 { Type = patchMode };
                    SetMainProgress(30);
                }

                var manifestTemplate = new SignedPatchManifestV1
                {
                    PatchMode = patchMode,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    Notes = string.IsNullOrWhiteSpace(notes) ? null : notes,
                    Target = target
                };

                var logger = new UiPatchLogger(AppendConsoleLine);

                SetMainProgress(40);
                AppendConsoleLine($"[Keys] Creating signature...");

                await Task.Run(() => SignedPatchSigner.SignPatchFile(inputPatch, outputPatch, cert, privateKey, manifestTemplate, logger));

                SetMainProgress(100);
                AppendConsoleLine($"[Keys] Signed patch saved to: {outputPatch}");
                signSuccess = true;
            }
            catch (Exception ex)
            {
                SetTaskbarState(System.Windows.Shell.TaskbarItemProgressState.Error);
                AppendConsoleLine($"[Keys] Signing failed: {ex.Message}");
                signError = ex.Message;
            }
            finally
            {
                SetUiLocked(false);
                ClearTaskbarProgress();
            }

            // Show result message after UI is unlocked
            if (signSuccess)
            {
                System.Windows.MessageBox.Show(
                    L("keys.msg.signSuccess", "补丁签名成功。"),
                    L("title.done", "完成"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else if (signError != null)
            {
                System.Windows.MessageBox.Show(
                    $"{L("keys.msg.signFailed", "补丁签名失败")}: {signError}",
                    L("title.error", "错误"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private SignedPatchTargetV1 BuildTargetInfo(string targetPath, SignedPatchMode mode)
        {
            if (mode == SignedPatchMode.File)
            {
                if (!System.IO.File.Exists(targetPath))
                    throw new System.IO.FileNotFoundException("Target file not found.", targetPath);

                var fi = new System.IO.FileInfo(targetPath);
                byte[] hash = SignedPatchCrypto.ComputeFileSha256(targetPath, fi.Length);

                return new SignedPatchTargetV1
                {
                    Type = SignedPatchMode.File,
                    File = new SignedPatchTargetFileV1
                    {
                        Sha256Hex = Convert.ToHexString(hash),
                        Size = fi.Length
                    }
                };
            }

            // Directory mode
            if (!System.IO.Directory.Exists(targetPath))
                throw new System.IO.DirectoryNotFoundException("Target directory not found.");

            var files = new System.Collections.Generic.List<SignedPatchTargetDirectoryFileV1>();
            foreach (string file in System.IO.Directory.EnumerateFiles(targetPath, "*", System.IO.SearchOption.AllDirectories))
            {
                var fi = new System.IO.FileInfo(file);
                string relativePath = System.IO.Path.GetRelativePath(targetPath, file).Replace('\\', '/');
                byte[] hash = SignedPatchCrypto.ComputeFileSha256(file, fi.Length);

                files.Add(new SignedPatchTargetDirectoryFileV1
                {
                    RelativePath = relativePath,
                    Sha256Hex = Convert.ToHexString(hash),
                    Size = fi.Length
                });
            }

            return new SignedPatchTargetV1
            {
                Type = SignedPatchMode.Directory,
                Files = files
            };
        }
#else
        // Stubs for Release build where Keys UI is hidden but XAML still references these handlers
        private void KeysGenerateButton_Click(object sender, RoutedEventArgs e) { }
        private void KeysLoadKeyButton_Click(object sender, RoutedEventArgs e) { }
        private void KeysSaveKeyButton_Click(object sender, RoutedEventArgs e) { }
        private void KeysSaveCertButton_Click(object sender, RoutedEventArgs e) { }
        private void KeysExportCertButton_Click(object sender, RoutedEventArgs e) { }
        private void KeysLoadCertButton_Click(object sender, RoutedEventArgs e) { }
        private void SignBrowsePatch_Click(object sender, RoutedEventArgs e) { }
        private void SignBrowseOutput_Click(object sender, RoutedEventArgs e) { }
        private void SignBrowseTarget_Click(object sender, RoutedEventArgs e) { }
        private void SignExecuteButton_Click(object sender, RoutedEventArgs e) { }
#endif
    }
}

