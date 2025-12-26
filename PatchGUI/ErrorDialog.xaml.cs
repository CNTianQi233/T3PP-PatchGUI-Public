using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace PatchGUI
{
    public partial class ErrorDialog : Wpf.Ui.Controls.FluentWindow
    {
        private readonly string? _logFilePath;

        public ErrorDialog(string title, string message, string? logFilePath)
        {
            InitializeComponent();

            _logFilePath = logFilePath;

            string localizedTitle = LocalizationManager.Get("errorDialog.title", title);
            Title = localizedTitle;
            TitleText.Text = localizedTitle;
            MessageText.Text = message;

            OpenLogButtonText.Text = LocalizationManager.Get("errorDialog.openLog", "在文件管理器中打开这个文件");
            CloseButtonText.Text = LocalizationManager.Get("errorDialog.close", "关闭");

            OpenLogButton.IsEnabled = !string.IsNullOrWhiteSpace(_logFilePath) && File.Exists(_logFilePath);
        }

        public static void Show(Window owner, string title, string message, string? logFilePath)
        {
            var dlg = new ErrorDialog(title, message, logFilePath)
            {
                Owner = owner
            };
            dlg.ShowDialog();
        }

        private void OpenLogButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_logFilePath) || !File.Exists(_logFilePath))
                    return;

                var psi = new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{_logFilePath}\"",
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch
            {
                // ignore
            }
            finally
            {
                Close();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
