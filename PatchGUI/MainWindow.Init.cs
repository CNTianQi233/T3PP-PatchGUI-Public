// 该文件负责 MainWindow 初始化、模式/导航初始化与游戏菜单刷新等逻辑。
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PatchGUI.Core;

namespace PatchGUI
{
    public partial class MainWindow
    {
        #region 初始化

        private async void InitOnlineRulesAsync()
        {
            bool enableOnline = false;
            if (!enableOnline)
                return;
            try
            {
                var svc = OnlinePatchService.Instance;

                // 把日志直接写到运行日志窗口
                await svc.EnsureInitializedAsync(
                    RuleKey1,
                    RuleKey2,
                    msg => AppendConsoleLine(msg)   // 你原来写日志的方法
                );

                // ★★ 在线规则加载完了，再刷新菜单（UI 线程）
                Dispatcher.Invoke(() =>
                {
                    RefreshGameMenu(svc.Games);
                });

                AppendConsoleLine("[ONLINE] 在线规则初始化完成。");
            }
            catch (Exception ex)
            {
                AppendConsoleLine("[ONLINE] 初始化失败：" + ex.Message);
            }
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            bool enableOnline = false;
            if (!enableOnline)
                return;
            try
            {
                await PatchGUI.Core.OnlinePatchService.Instance
                    .EnsureInitializedAsync(RuleKey1, RuleKey2, AppendConsoleLine);

                AppendConsoleLine("[ONLINE] 在线规则初始化完成。");
                // 如果你想自动根据 OnlinePatchService.Instance.Games 生成菜单，
                // 可以在这里遍历 Games，动态创建 MenuItem，Tag = game.Id，
                // Click 统一绑到 GameMenuItem_Click。
            }
            catch (Exception ex)
            {
                AppendConsoleLine($"[ONLINE] 初始化失败：{ex.Message}");
            }
        }

        private void InitModeMenu()
        {
            if (GameSelectButton.ContextMenu is not ContextMenu menu)
                return;

            menu.Items.Clear();

            var dirItem = new MenuItem
            {
                Tag = DirectoryModeTag
            };
            dirItem.Click += GameMenuItem_Click;
            menu.Items.Add(dirItem);

            var fileItem = new MenuItem
            {
                Tag = FileModeTag
            };
            fileItem.Click += GameMenuItem_Click;
            menu.Items.Add(fileItem);
        }

        private void InitMode()
        {
#if DEBUG
            DebugNavBar.Visibility = Visibility.Visible;

            PatchView.Visibility = Visibility.Visible; PatchView.Opacity = 1;
            GenerateView.Visibility = Visibility.Collapsed; GenerateView.Opacity = 0;
            SettingsView.Visibility = Visibility.Collapsed; SettingsView.Opacity = 0;
            GameSelectionPanel.Visibility = Visibility.Collapsed;
#else
            // 发行版：保留基础导航（补丁 + 设置），隐藏高级页
            DebugNavBar.Visibility = Visibility.Visible;
            GenerateTabRadio.Visibility = Visibility.Collapsed;
            KeysTabRadio.Visibility = Visibility.Collapsed;

            PatchView.Visibility = Visibility.Visible; PatchView.Opacity = 1;
            GenerateView.Visibility = Visibility.Collapsed; GenerateView.Opacity = 0;
            SettingsView.Visibility = Visibility.Collapsed; SettingsView.Opacity = 0;
            GameSelectionPanel.Visibility = Visibility.Collapsed;
#endif
        }

        private void RefreshGameMenu(IEnumerable<OnlineGameEntry> games)
        {
            if (GameSelectButton.ContextMenu is not ContextMenu menu)
                return;
            menu.Items.Clear();

            // 手动模式
            var manualItem = new MenuItem
            {
                Header = L("menu.manualMode", "手动模式"),
                Tag = ManualGameId
            };
            manualItem.Click += GameMenuItem_Click;
            menu.Items.Add(manualItem);

            // 分隔线
            menu.Items.Add(new Separator());

            // 从 _list.sha 解析出来的在线游戏
            foreach (var g in games)
            {
                var item = new MenuItem
                {
                    Header = g.DisplayName, // 显示名字
                    Tag = g.Id              // 内部 id，后面用来匹配
                };
                item.Click += GameMenuItem_Click;
                menu.Items.Add(item);
            }
        }

        #endregion
    }
}
