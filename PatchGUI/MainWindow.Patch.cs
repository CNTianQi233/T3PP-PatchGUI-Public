// 该文件负责“应用补丁”页：选择补丁/目标、备份、应用补丁与错误处理等逻辑。
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Shell;
using PatchGUI.Core;
using WinForms = System.Windows.Forms;

namespace PatchGUI
{
    public partial class MainWindow
    {
        #region 补丁页：选择目录 + 应用 t3pp 补丁
        /// <summary>
        /// 根据当前游戏 ID，优先尝试云端补丁；失败则回退到手动选择对话框。
        /// 返回 null 表示用户取消。
        /// 特殊约定：菜单 Tag = "manual" 时，强制走手动选择。
        /// </summary>
        /// 
        private async Task<string?> ResolvePatchFilePathAsync(IPatchLogger logger)
        {
            using var _ = logger.Step("解析补丁路径（在线/手动/已选）");
            bool useOnline = false;
            if (useOnline)
            {
                if (!string.IsNullOrWhiteSpace(_currentGameId) &&
                    !string.Equals(_currentGameId, "manual", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        await PatchGUI.Core.OnlinePatchService.Instance
                            .EnsureInitializedAsync(RuleKey1, RuleKey2, AppendConsoleLine);

                        string? onlinePatch =
                            await PatchGUI.Core.OnlinePatchService.Instance
                                .GetOrDownloadPatchAsync(_currentGameId!, logger);

                        if (!string.IsNullOrWhiteSpace(onlinePatch) && File.Exists(onlinePatch))
                        {
                            logger.Info($"在线补丁路径：{onlinePatch}");
                            return onlinePatch;
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Warn($"在线补丁下载失败：{ex.Message}");
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(_selectedPatchPath) && File.Exists(_selectedPatchPath))
            {
                logger.Info($"使用已选择补丁：{_selectedPatchPath}");
                return _selectedPatchPath;
            }

            using var patchDialog = new WinForms.OpenFileDialog
            {
                Title = L("dialog.selectPatch.title", "选择 T3PP 补丁文件"),
                Filter = L("filter.patchFile", "Touhou 3rd-party Patch (*.t3pp)|*.t3pp|All files (*.*)|*.*")
            };

            if (ShowWinFormsDialog(patchDialog) != WinForms.DialogResult.OK)
            {
                logger.Info("用户取消了补丁文件选择。");
                return null;
            }

            _selectedPatchPath = patchDialog.FileName;
            PatchFileTextBox.Text = _selectedPatchPath;
            UpdateTargetSelectionState();
            logger.Info($"已选择补丁：{_selectedPatchPath}");
            RequestPatchTrustRefresh();
            return _selectedPatchPath;
        }

        private void SelectPatchFileButton_Click(object sender, RoutedEventArgs e)
        {
            var logger = new UiPatchLogger(AppendConsoleLine);
            using var dialog = new WinForms.OpenFileDialog
            {
                Title = L("dialog.selectPatch.title", "选择 T3PP 补丁文件"),
                Filter = L("filter.patchFile", "Touhou 3rd-party Patch (*.t3pp)|*.t3pp|All files (*.*)|*.*")
            };

            if (!string.IsNullOrWhiteSpace(_selectedPatchPath) && File.Exists(_selectedPatchPath))
            {
                dialog.InitialDirectory = Path.GetDirectoryName(_selectedPatchPath);
                dialog.FileName = _selectedPatchPath;
            }

            if (ShowWinFormsDialog(dialog) != WinForms.DialogResult.OK)
            {
                logger.Info("用户取消了补丁文件选择。");
                return;
            }

            _selectedPatchPath = dialog.FileName;
            PatchFileTextBox.Text = _selectedPatchPath;
            UpdateTargetSelectionState();
            logger.Info($"已选择补丁：{_selectedPatchPath}");
            ApplyPatchModeFromFile(_selectedPatchPath);
            if (!_useDirectoryMode && File.Exists(DirectoryTextBox.Text))
            {
                UpdateHashDisplay(DirectoryTextBox.Text);
            }

            RequestPatchTrustRefresh();
        }

        private void SelectDirButton_Click(object sender, RoutedEventArgs e)
        {
            var logger = new UiPatchLogger(AppendConsoleLine);
            try
            {
                if (!_useDirectoryMode)
                {
                    using var fileDialog = new WinForms.OpenFileDialog
                    {
                        Title = L("dialog.selectTargetFile.title", "选择目标文件"),
                        Filter = L("filter.allFiles", "所有文件 (*.*)|*.*")
                    };

                    if (!string.IsNullOrWhiteSpace(DirectoryTextBox.Text) &&
                        File.Exists(DirectoryTextBox.Text))
                    {
                        fileDialog.FileName = DirectoryTextBox.Text;
                        fileDialog.InitialDirectory = Path.GetDirectoryName(DirectoryTextBox.Text);
                    }

                    if (ShowWinFormsDialog(fileDialog) == WinForms.DialogResult.OK)
                    {
                        DirectoryTextBox.Text = fileDialog.FileName;
                        logger.Info($"已选择目标文件：{fileDialog.FileName}");
                        UpdateHashDisplay(fileDialog.FileName);
                        RequestPatchTrustRefresh();
                    }
                    return;
                }
                using var folderDialog = new WinForms.FolderBrowserDialog
                {
                    Description = L("dialog.selectGameRoot.desc", "选择游戏根目录")
                };

                if (!string.IsNullOrWhiteSpace(DirectoryTextBox.Text) &&
                    Directory.Exists(DirectoryTextBox.Text))
                {
                    folderDialog.SelectedPath = DirectoryTextBox.Text;
                }

                if (ShowWinFormsDialog(folderDialog) == WinForms.DialogResult.OK)
                {
                    DirectoryTextBox.Text = folderDialog.SelectedPath;
                    logger.Info($"已选择目录：{folderDialog.SelectedPath}");
                    RequestPatchTrustRefresh();
                }
            }
            catch (Exception ex)
            {
                logger.LogException("选择目标失败", ex);
                ShowRepairErrorDialog("选择目标失败", ex, logger);
            }
        }

        private WinForms.DialogResult ShowWinFormsDialog(WinForms.CommonDialog dialog)
        {
            if (dialog == null)
                throw new ArgumentNullException(nameof(dialog));

            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                var owner = hwnd == IntPtr.Zero ? null : new Win32Window(hwnd);
                return owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
            }
            finally
            {
                // WinForms.ShowDialog() 会启动嵌套消息循环，某些情况下会让 WPF 的鼠标/按压状态不同步。
                // 这里尽量把输入状态同步回来，避免悬停类动画“失效”。
                try { System.Windows.Input.Mouse.Capture(null); } catch { }
                try { System.Windows.Input.Mouse.Synchronize(); } catch { }
                try { Activate(); } catch { }
            }
        }

        private sealed class Win32Window : WinForms.IWin32Window
        {
            public IntPtr Handle { get; }

            public Win32Window(IntPtr handle) => Handle = handle;
        }
        /// <summary>
        /// “恢复备份”按钮：从 .pak 里还原游戏目录。
        /// </summary>
        private async void RestoreBackupButton_Click(object sender, RoutedEventArgs e)
        {
            var logger = new UiPatchLogger(AppendConsoleLine);
            try
            {
                using var _ = logger.Step("恢复备份");

                string gameRoot = DirectoryTextBox.Text.Trim();
                if (string.IsNullOrEmpty(gameRoot) || !Directory.Exists(gameRoot))
                {
                    logger.Warn("目标目录无效，已取消恢复备份。");
                    ShowInfo(L("msg.selectValidTarget", "请先选择正确的目标。"));
                    return;
                }

                using var dlg = new WinForms.OpenFileDialog
                {
                    Title = L("dialog.selectBackup.title", "选择备份 .pak 文件"),
                    Filter = L("filter.backupPak", "T3PP 备份包 (*.pak)|*.pak|所有文件 (*.*)|*.*"),
                    InitialDirectory = gameRoot
                };

                if (ShowWinFormsDialog(dlg) != WinForms.DialogResult.OK)
                {
                    logger.Info("用户取消了备份文件选择。");
                    return;
                }

                string backupPath = dlg.FileName;
                logger.Info($"目标目录：{gameRoot}");
                logger.Info($"备份文件：{backupPath}");

                using var __ = BeginUiLock();
                await Task.Run(() => PatchGUI.Core.BackupHelper.RestoreDirectoryBackup(backupPath, gameRoot, logger));

                logger.Info("备份恢复完成。");
                ShowDone(L("msg.restoreBackupDone", "备份恢复完成。"));
            }
            catch (Exception ex)
            {
                logger.LogException("恢复备份失败", ex);
                ShowRepairErrorDialog("恢复备份失败", ex, logger);
            }
        }

        /// <summary>
        /// “执行补丁”按钮：让玩家选一个 .t3pp，然后对当前目录应用补丁。
        /// </summary>
        /// <summary>
        /// “执行补丁”按钮：
        /// - 优先尝试根据当前游戏 ID 从云端拉 .t3pp；
        /// - 失败则弹出对话框让用户手动选 .t3pp；
        /// - 在真正打补丁前，对整个游戏目录做一次 .pak 备份。
        /// </summary>
        private async void RunPatchButton_Click(object sender, RoutedEventArgs e)
        {
            var logger = new UiPatchLogger(AppendConsoleLine);
            string runId = Guid.NewGuid().ToString("N")[..8];
            using var runScope = logger.Step($"执行补丁 RunId={runId}");

            if (_patchCts != null)
            {
                logger.Warn("补丁正在执行中，已忽略重复点击。");
                ShowInfo(L("msg.patchInProgress", "补丁正在执行中。"));
                return;
            }

            string targetPath = DirectoryTextBox.Text.Trim();
            string gameRoot = targetPath;
            if (string.IsNullOrEmpty(targetPath) ||
                (!Directory.Exists(targetPath) && !File.Exists(targetPath)))
            {
                logger.Warn($"目标无效：{targetPath}");
                ShowInfo(L("msg.selectValidTarget", "请先选择正确的目标。"));
                return;
            }

            logger.Info($"初始目标：{targetPath}");
            logger.Info($"模式：{(_useDirectoryMode ? "目录模式" : "文件模式")}");
            if (!string.IsNullOrWhiteSpace(_currentGameId))
                logger.Info($"当前游戏：{_currentGameName} (ID: {_currentGameId})");

            // 1. 决定要用哪个 .t3pp（云端 or 手动选择）
            string? patchPath;
            using (logger.Step("选择补丁文件"))
            {
                patchPath = await ResolvePatchFilePathAsync(logger);
            }
            if (!string.IsNullOrWhiteSpace(patchPath))
            {
                ApplyPatchModeFromFile(patchPath);

                targetPath = DirectoryTextBox.Text.Trim();
                if (_useDirectoryMode)
                {
                    if (string.IsNullOrEmpty(targetPath) || !Directory.Exists(targetPath))
                    {
                        logger.Warn($"目录模式下目标无效：{targetPath}");
                        ShowInfo(L("msg.selectValidGameDirectory", "请选择有效的游戏目录。"));
                        return;
                    }
                    gameRoot = targetPath;
                }
                else
                {
                    if (string.IsNullOrEmpty(targetPath) || !File.Exists(targetPath))
                    {
                        logger.Warn($"文件模式下目标无效：{targetPath}");
                        ShowInfo(L("msg.selectValidTargetFile", "请选择有效的目标文件。"));
                        return;
                    }

                    gameRoot = Path.GetDirectoryName(targetPath) ?? targetPath;
                    if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot))
                    {
                        logger.Warn($"无法解析文件所在目录：{targetPath}");
                        ShowInfo(L("msg.cannotResolveTargetDir", "无法解析所选文件所在的目录。"));
                        return;
                    }

                    if (string.IsNullOrWhiteSpace(_crc32))
                    {
                        logger.Warn("哈希信息为空（可能未计算/未选择目标文件）。");
                        ShowInfo(L("msg.hashInfoMissing", "哈希信息获取失败，请重新选择目标文件。"));
                        return;
                    }

                    if (!VerifyHashes(targetPath))
                    {
                        logger.Error("哈希校验失败（可能文件被修改或选择错误）。");
                        ShowError(L("msg.hashMismatchOrCalcFailed", "哈希不匹配或计算失败，请检查目标文件。"));
                        return;
                    }
                }
            }
            if (string.IsNullOrWhiteSpace(patchPath))
            {
                logger.Info("用户取消了补丁选择。");
                return;
            }

            logger.Info($"最终目标目录：{gameRoot}");
            logger.Info($"选择补丁：{patchPath}");

            if (!await ConfirmPatchTrustBeforeApplyAsync(patchPath, targetPath, logger))
                return;

            _patchCts = new CancellationTokenSource();
            using var uiLock = BeginUiLock();
            BeginTaskbarProgress();
            SetMainProgress(0, animate: false);

            try
            {
                SelectiveBackupPlan? dryRunPlan = null;
                using (logger.Step("预扫描补丁（dry-run）以确定备份清单"))
                {
                    dryRunPlan = await Task.Run(
                        () => TryBuildSelectiveBackupPlanByDryRun(gameRoot, patchPath, logger),
                        _patchCts.Token);
                }
                SetMainProgress(15);

                try
                {
                    using (logger.Step("预检目标文件占用情况"))
                    {
                        if (dryRunPlan is { } plan)
                        {
                            await Task.Run(
                                () => PreflightCheckPatchTargets(gameRoot, plan, logger),
                                _patchCts.Token);
                        }
                        else
                        {
                            logger.Warn("预检已跳过：未能获取 dry-run 文件清单。");
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogException("预检失败，已终止", ex);
                    ShowRepairErrorDialog("预检失败", ex, logger);
                    SetTaskbarState(TaskbarItemProgressState.Error);
                    return;
                }
                SetMainProgress(25);

                string? backupPath = null;
                try
                {
                    using (logger.Step("创建备份"))
                    {
                        backupPath = await Task.Run(
                            () => CreateBackupForPatch(gameRoot, patchPath, dryRunPlan, logger),
                            _patchCts.Token);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogException("创建备份失败，已终止", ex);
                    ShowRepairErrorDialog("创建备份失败", ex, logger);
                    SetTaskbarState(TaskbarItemProgressState.Error);
                    return;
                }
                SetMainProgress(55);

                try
                {
                    string patchFileLocal = patchPath; // 避免闭包捕获 UI 变量

                    using (logger.Step("应用补丁"))
                    {
                        await Task.Run(() =>
                        {
                            _patchCts.Token.ThrowIfCancellationRequested();

                            T3ppDiff.ApplyPatchToDirectory(
                                patchFile: patchFileLocal,
                                targetRoot: gameRoot,
                                logger: logger,
                                dryRun: false);
                        }, _patchCts.Token);
                    }

                    logger.Info("补丁应用完成。");

                    if (!string.IsNullOrEmpty(backupPath))
                    {
                        logger.Info($"如需恢复补丁前状态，可在游戏目录下找到备份：{backupPath}");
                    }

                    SetMainProgress(100);
                }
                catch (Exception ex)
                {
                    SetTaskbarState(TaskbarItemProgressState.Error);
                    logger.LogException("补丁应用失败", ex);
                    ShowRepairErrorDialog("补丁应用失败", ex, logger);
                }
            }
            finally
            {
                _patchCts?.Dispose();
                _patchCts = null;
                if (MainProgressBar.Value < 100)
                    SetMainProgress(100);

                ClearTaskbarProgress();
            }
        }

        private static string? CreateBackupForPatch(string gameRoot, string patchPath, SelectiveBackupPlan? dryRunPlan, IPatchLogger logger)
        {
            if (string.IsNullOrWhiteSpace(gameRoot))
                throw new ArgumentException("gameRoot is empty.", nameof(gameRoot));
            if (string.IsNullOrWhiteSpace(patchPath))
                throw new ArgumentException("patchPath is empty.", nameof(patchPath));

            if (dryRunPlan is { } p && p.BackedUpFiles.Length == 0 && p.MissingBeforeFiles.Length == 0)
            {
                logger.Info("文件一致，无可备份的文件。");
                return null;
            }

            string backupName = $"T3PP_BACKUP_{DateTime.Now:yyyyMMdd_HHmmss}.pak";

            string tempBackupDir = Path.Combine(Path.GetTempPath(), "PatchGUI_Backups");
            Directory.CreateDirectory(tempBackupDir);
            string tempBackupPath = Path.Combine(tempBackupDir, backupName);
            string desiredFinalPath = Path.Combine(gameRoot, backupName);

            logger.Info($"备份临时路径：{tempBackupPath}");
            logger.Info($"备份目标路径：{desiredFinalPath}");

            if (dryRunPlan is { } plan)
            {
                if (plan.BackedUpFiles.Length == 0 && plan.MissingBeforeFiles.Length > 0)
                {
                    logger.Info($"无需备份既存文件，将仅记录补丁新增文件清单（新增文件数={plan.MissingBeforeFiles.Length}）。");
                }

                try
                {
                    PatchGUI.Core.BackupHelper.CreateSelectiveBackupFromLists(
                        sourceDir: gameRoot,
                        patchFilePath: patchPath,
                        backupFilePath: tempBackupPath,
                        backedUpFiles: plan.BackedUpFiles,
                        missingBeforeFiles: plan.MissingBeforeFiles,
                        logger: logger);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("选择性备份列表为空", StringComparison.OrdinalIgnoreCase)
                                                          || ex.Message.Contains("选择性备份未写入任何文件", StringComparison.OrdinalIgnoreCase))
                {
                    // 如果补丁只新增文件，但我们没能可靠解析新增文件清单，则跳过备份（不影响安全性：不存在“修补前文件”需要恢复）。
                    // 或者：manifest 里有文件，但经过 BackupHelper 里的 IsSafeRelativePath 过滤后变为空，也会抛出此异常，此时也应安全跳过。
                    // 或者：所有待备份文件在磁盘上均不存在（BackupHelper 会抛出“选择性备份未写入任何文件”），此时也无需备份。
                    logger.Warn($"选择性备份失败（将跳过备份）：{ex.Message}");
                    return null;
                }
            }
            else
            {
                logger.Warn("未能获取 dry-run 文件清单，已跳过创建备份。");
                return null;
            }

            string backupPath = tempBackupPath;
            try
            {
                if (File.Exists(desiredFinalPath))
                    File.Delete(desiredFinalPath);

                File.Move(tempBackupPath, desiredFinalPath);
                backupPath = desiredFinalPath;
                logger.Info($"备份已移动到游戏目录：{backupPath}");
            }
            catch (Exception ex)
            {
                logger.Warn($"无法将备份移动到游戏目录，将保留在临时目录：{ex.Message}");
                logger.Info($"备份位置：{backupPath}");
            }

            return backupPath;
        }


        #endregion
    }
}

