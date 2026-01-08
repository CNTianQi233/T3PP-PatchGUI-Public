// 该文件负责“生成补丁”页：选择源/目标、生成补丁与相关日志/进度逻辑。
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Shell;
using PatchGUI.Core;
using WinForms = System.Windows.Forms;

namespace PatchGUI
{
    public partial class MainWindow
    {
        #region 生成页：选择目录 + 生成 t3pp

        private void GenSelectSourceButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_useDirectoryMode)
                {
                    using var fileDialog = new WinForms.OpenFileDialog
                    {
                        Title = L("dialog.selectSourceFile.title", "选择源文件"),
                        Filter = L("filter.allFiles", "所有文件 (*.*)|*.*")
                    };

                    if (!string.IsNullOrWhiteSpace(GenSourceBox.Text) &&
                        File.Exists(GenSourceBox.Text))
                    {
                        fileDialog.FileName = GenSourceBox.Text;
                        fileDialog.InitialDirectory = Path.GetDirectoryName(GenSourceBox.Text);
                    }

                    if (ShowWinFormsDialog(fileDialog) == WinForms.DialogResult.OK)
                    {
                        GenSourceBox.Text = fileDialog.FileName;
                        AppendGenConsoleLine($"[INFO] Source path: {fileDialog.FileName}");
                    }
                    return;
                }
                using var folderDialog = new WinForms.FolderBrowserDialog
                {
                    Description = L("dialog.selectOriginalDir.desc", "选择修改前（原始）目录")
                };

                if (!string.IsNullOrWhiteSpace(GenSourceBox.Text) &&
                    Directory.Exists(GenSourceBox.Text))
                {
                    folderDialog.SelectedPath = GenSourceBox.Text;
                }

                if (ShowWinFormsDialog(folderDialog) == WinForms.DialogResult.OK)
                {
                    GenSourceBox.Text = folderDialog.SelectedPath;
                    AppendGenConsoleLine($"[INFO] 已选择原始目录：{folderDialog.SelectedPath}");
                }
            }
            catch (Exception ex)
            {
                ShowRepairErrorDialog("选择源路径失败", ex);
            }
        }

        private void GenSelectTargetButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_useDirectoryMode)
                {
                    using var fileDialog = new WinForms.OpenFileDialog
                    {
                        Title = L("dialog.selectTargetFile.title", "选择目标文件"),
                        Filter = L("filter.allFiles", "所有文件 (*.*)|*.*")
                    };

                    if (!string.IsNullOrWhiteSpace(GenTargetBox.Text) &&
                        File.Exists(GenTargetBox.Text))
                    {
                        fileDialog.FileName = GenTargetBox.Text;
                        fileDialog.InitialDirectory = Path.GetDirectoryName(GenTargetBox.Text);
                    }

                    if (ShowWinFormsDialog(fileDialog) == WinForms.DialogResult.OK)
                    {
                        GenTargetBox.Text = fileDialog.FileName;
                        AppendGenConsoleLine($"[INFO] Target path: {fileDialog.FileName}");
                    }
                    return;
                }
                using var folderDialog = new WinForms.FolderBrowserDialog
                {
                    Description = L("dialog.selectModifiedDir.desc", "选择修改后（目标）目录")
                };

                if (!string.IsNullOrWhiteSpace(GenTargetBox.Text) &&
                    Directory.Exists(GenTargetBox.Text))
                {
                    folderDialog.SelectedPath = GenTargetBox.Text;
                }

                if (ShowWinFormsDialog(folderDialog) == WinForms.DialogResult.OK)
                {
                    GenTargetBox.Text = folderDialog.SelectedPath;
                    AppendGenConsoleLine($"[INFO] 已选择目标目录：{folderDialog.SelectedPath}");
                }
            }
            catch (Exception ex)
            {
                ShowRepairErrorDialog("选择目标失败", ex);
            }
        }

        private async void GenStartDiffButton_Click(object sender, RoutedEventArgs e)
        {
            SetGenProgress(0, animate: false);
            GenConsoleBox.Clear();

            var logger = new UiPatchLogger(AppendGenConsoleLine);
            string runId = Guid.NewGuid().ToString("N")[..8];
            using var runScope = logger.Step($"生成差分 RunId={runId}");

            string oldDir = GenSourceBox.Text.Trim();
            string newDir = GenTargetBox.Text.Trim();

            if (!_useDirectoryMode)
            {
                logger.Info("模式：文件差分");
                if (string.IsNullOrEmpty(oldDir) || !File.Exists(oldDir))
                {
                    logger.Warn($"源文件无效：{oldDir}");
                    ShowInfo(L("msg.selectSourceFileFirst", "请先选择源文件。"));
                    return;
                }

                if (string.IsNullOrEmpty(newDir) || !File.Exists(newDir))
                {
                    logger.Warn($"目标文件无效：{newDir}");
                    ShowInfo(L("msg.selectTargetFileFirst", "请先选择目标文件。"));
                    return;
                }

                bool fileAddWatermark = AddWatermarkCheckBox.IsChecked == true;
                string fileBaseDir = Path.GetDirectoryName(newDir)
                                 ?? Path.GetDirectoryName(oldDir)
                                 ?? Path.GetTempPath();
                string fileDefaultName = $"patch_{DateTime.Now:yyyyMMdd_HHmmss}.t3pp";

                string fileOutFile;
                using (var fileDialog = new WinForms.SaveFileDialog
                {
                    Title = L("dialog.savePatch.title", "选择补丁输出文件"),
                    Filter = L("filter.patchFile", "Touhou 3rd-party Patch (*.t3pp)|*.t3pp|All files (*.*)|*.*"),
                    FileName = fileDefaultName,
                    InitialDirectory = fileBaseDir
                })
                {
                    if (ShowWinFormsDialog(fileDialog) != WinForms.DialogResult.OK)
                    {
                        logger.Info("用户取消了输出文件选择。");
                        return;
                    }

                    fileOutFile = fileDialog.FileName;
                }

                logger.Info($"Source file: {oldDir}");
                logger.Info($"Target file: {newDir}");
                logger.Info($"Output file: {fileOutFile}");

                BeginTaskbarProgress();
                using var uiLock = BeginUiLock();
                try
                {
                    using (logger.Step("生成差分（文件）"))
                    {
                        await Task.Run(() =>
                        {
                            string tempOldDir = Path.Combine(Path.GetTempPath(), "t3pp_old_" + Guid.NewGuid().ToString("N"));
                            string tempNewDir = Path.Combine(Path.GetTempPath(), "t3pp_new_" + Guid.NewGuid().ToString("N"));

                            string relativeName = Path.GetFileName(newDir);
                            if (string.IsNullOrWhiteSpace(relativeName))
                            {
                                relativeName = Path.GetFileName(oldDir);
                            }
                            if (string.IsNullOrWhiteSpace(relativeName))
                            {
                                relativeName = "file.bin";
                            }

                            Directory.CreateDirectory(tempOldDir);
                            Directory.CreateDirectory(tempNewDir);

                            string oldCopy = Path.Combine(tempOldDir, relativeName);
                            string newCopy = Path.Combine(tempNewDir, relativeName);

                            try
                            {
                                logger.Info($"TempOldDir: {tempOldDir}");
                                logger.Info($"TempNewDir: {tempNewDir}");

                                File.Copy(oldDir, oldCopy, true);
                                File.Copy(newDir, newCopy, true);

                                T3ppDiff.CreateDirectoryDiff(tempOldDir, tempNewDir, fileOutFile, fileAddWatermark);
                                File.AppendAllText(fileOutFile, FilePatchTag);
                            }
                            finally
                            {
                                TryDeleteDirectory(tempOldDir);
                                TryDeleteDirectory(tempNewDir);
                                logger.Info("已清理临时目录。");
                            }
                        });
                    }
                    SetGenProgress(100);
                    logger.Info("Diff generation completed.");
                }
                catch (Exception ex)
                {
                    logger.LogException("Diff generation failed", ex);
                    ShowRepairErrorDialog("生成差分失败", ex, logger);
                }
                finally
                {
                    ClearTaskbarProgress();
                }
                return;
            }

            if (string.IsNullOrEmpty(oldDir) || !Directory.Exists(oldDir))
            {
                logger.Warn($"原始目录无效：{oldDir}");
                ShowInfo(L("msg.selectOriginalDirFirst", "请先选择“修改前”的原始目录。"));
                return;
            }

            if (string.IsNullOrEmpty(newDir) || !Directory.Exists(newDir))
            {
                logger.Warn($"目标目录无效：{newDir}");
                ShowInfo(L("msg.selectModifiedDirFirst", "请先选择“修改后”的目标目录。"));
                return;
            }

            // 目前只是占位，后端还是按目录做
            bool packDirectory = PackDirectoryCheckBox.IsChecked == true;
            bool addWatermark = AddWatermarkCheckBox.IsChecked == true;
            logger.Info($"模式：目录差分，水印={(addWatermark ? "开" : "关")}");

            // 默认输出目录：原始目录的父目录
            string baseDir = Directory.GetParent(oldDir)?.FullName ?? oldDir;
            string defaultName = $"patch_{DateTime.Now:yyyyMMdd_HHmmss}.t3pp";

            string outFile;
            using (var dialog = new WinForms.SaveFileDialog
            {
                Title = L("dialog.savePatch.title", "选择补丁输出文件"),
                Filter = L("filter.patchFile.zh", "Touhou 3rd-party Patch (*.t3pp)|*.t3pp|所有文件 (*.*)|*.*"),
                FileName = defaultName,
                InitialDirectory = baseDir
            })
            {
                if (ShowWinFormsDialog(dialog) != WinForms.DialogResult.OK)
                {
                    // 用户取消
                    logger.Info("用户取消了输出文件选择。");
                    return;
                }

                outFile = dialog.FileName;
            }

            logger.Info($"原始目录：{oldDir}");
            logger.Info($"目标目录：{newDir}");
            logger.Info($"输出文件：{outFile}");
            logger.Info($"打包模式：{(packDirectory ? "按目录差分" : "预留多文件模式（当前仍按目录处理）")}");

            try
            {
                BeginTaskbarProgress();
                using var uiLock = BeginUiLock();
                using (logger.Step("生成差分（目录）"))
                {
                    var uiLogger = logger; // Capture for lambda
                    await Task.Run(() =>
                    {
                        T3ppDiff.CreateDirectoryDiff(oldDir, newDir, outFile, addWatermark, uiLogger);
                        var modeTag = packDirectory ? DirectoryPatchTag : FilePatchTag;
                        File.AppendAllText(outFile, modeTag);
                    });
                }
                SetGenProgress(100);
                logger.Info("差分生成完成。");
            }
            catch (Exception ex)
            {
                SetTaskbarState(TaskbarItemProgressState.Error);
                logger.LogException("生成差分失败", ex);
                ShowRepairErrorDialog("生成差分失败", ex, logger);
            }
            finally
            {
                ClearTaskbarProgress();
            }
        }

        #endregion
    }
}

