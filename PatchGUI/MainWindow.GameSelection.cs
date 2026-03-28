// 该文件负责“游戏/模式选择”菜单与相关点击处理逻辑。
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace PatchGUI
{
    public partial class MainWindow
    {
        #region 游戏选择

        private void GameSelectButton_Click(object sender, RoutedEventArgs e)
        {
            if (GameSelectButton.ContextMenu is ContextMenu menu)
            {
                menu.PlacementTarget = GameSelectButton;
                menu.Placement = PlacementMode.Bottom;
                menu.IsOpen = true;
            }
        }

        private void GameMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem item)
                return;

            string tag = item.Tag?.ToString() ?? string.Empty;
            bool dirMode = string.Equals(tag, DirectoryModeTag, StringComparison.OrdinalIgnoreCase);
            bool fileMode = string.Equals(tag, FileModeTag, StringComparison.OrdinalIgnoreCase);
            if (dirMode || fileMode)
            {
                _useDirectoryMode = dirMode;
                _selectedPatchPath = null;
                GenSourceBox.Text = string.Empty;
                GenTargetBox.Text = string.Empty;
                PatchFileTextBox.Text = string.Empty;
                DirectoryTextBox.Text = string.Empty;
                UpdateHashDisplay(null);
                PackDirectoryCheckBox.IsChecked = dirMode;
                ApplyLocalization();
                UpdateTargetSelectionState();
                return;
            }

            if (tag == ManualGameId)
            {
                // 选中“手动模式”
                _currentGameId = null;
                _currentGameName = null;
                _isManualMode = true;
                SelectedGameText.Text = L("label.currentGameManual", "当前游戏：手动模式");
                AppendConsoleLine($"[INFO] {L("log.switchedManual", "已切换到手动模式，本地选择 .t3pp 文件。")}");
                return;
            }

            string gameName = item.Header?.ToString() ?? L("label.unknownGame", "未知游戏");
            string gameTag = tag;

            _currentGameId = gameTag;
            _currentGameName = gameName;
            _isManualMode = false;
            SelectedGameText.Text = $"{L("label.currentGamePrefix", "当前游戏：")}{gameName}";

            AppendConsoleLine($"[INFO] 已选择游戏：{gameName} (ID: {gameTag})");
        }

        #endregion
    }
}
