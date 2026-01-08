#if DEBUG
// 该文件负责 Debug 设置页与测试工具窗口（仅 Debug 编译）。
using System;

namespace PatchGUI
{
    public partial class MainWindow
    {
        private DebugSettingsPanel? _debugSettingsPanel;
        private TaskbarTestWindow? _taskbarTestWindow;

        partial void InitializeDebugSettings()
        {
            try
            {
                if (DebugSettingsHost == null)
                    return;

                _debugSettingsPanel = new DebugSettingsPanel(
                    (context, ex) => ShowRepairErrorDialog(context, ex),
                    OpenTaskbarTestWindow);
                DebugSettingsHost.Content = _debugSettingsPanel;
            }
            catch
            {
                // ignore
            }
        }

        partial void ApplyDebugSettingsLocalization()
        {
            try
            {
                _debugSettingsPanel?.RefreshLocalization();
                _taskbarTestWindow?.RefreshLocalization();
            }
            catch
            {
                // ignore
            }
        }

        private void OpenTaskbarTestWindow()
        {
            try
            {
                if (MainTaskbarItemInfo == null)
                    return;

                if (_taskbarTestWindow != null)
                {
                    _taskbarTestWindow.Activate();
                    return;
                }

                var win = new TaskbarTestWindow(MainTaskbarItemInfo)
                {
                    Owner = this,
                    WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner
                };
                win.Closed += (_, _) => _taskbarTestWindow = null;

                _taskbarTestWindow = win;
                win.Show();
            }
            catch (Exception ex)
            {
                ShowRepairErrorDialog("Taskbar 测试", ex);
            }
        }
    }
}
#endif
