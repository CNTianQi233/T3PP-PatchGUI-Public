using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Windows;

namespace PatchGUI
{
    public partial class DebugSettingsPanel : System.Windows.Controls.UserControl
    {
        private sealed class DebugSimErrorItem
        {
            public string Id { get; init; } = string.Empty;
            public string DisplayName { get; init; } = string.Empty;
            public string ActivateText { get; init; } = string.Empty;
        }

        private static readonly string[] DebugSimErrorOrder =
        [
            "testDialog",
            "dllNotFound",
            "entryPointNotFound",
            "badImageFormat",
            "unauthorized",
            "fileNotFound",
            "directoryNotFound",
            "pathTooLong",
            "fileInUse",
            "diskFull",
            "invalidJson",
            "cryptoFailed",
            "invalidArgument",
            "invalidFormat",
            "notSupported",
            "outOfMemory",
            "cancelled",
            "preflightFailed",
            "nativePatchFailed"
        ];

        private readonly Dictionary<string, Func<Exception>> _factories = new(StringComparer.OrdinalIgnoreCase);
        private readonly Action<string, Exception> _showError;
        private readonly Action _openTaskbarTest;

        public DebugSettingsPanel(Action<string, Exception> showError, Action openTaskbarTest)
        {
            InitializeComponent();

            _showError = showError ?? throw new ArgumentNullException(nameof(showError));
            _openTaskbarTest = openTaskbarTest ?? throw new ArgumentNullException(nameof(openTaskbarTest));
            InitFactories();
            RefreshLocalization();
        }

        public void RefreshLocalization()
        {
            TitleText.Text = LocalizationManager.Get("settings.debug.simulate.title", "模拟错误");
            string activate = LocalizationManager.Get("settings.debug.simulate.activate", "激活");

            var items = new List<DebugSimErrorItem>(DebugSimErrorOrder.Length);
            foreach (string id in DebugSimErrorOrder)
            {
                if (!_factories.ContainsKey(id))
                    continue;

                string name = LocalizationManager.Get($"settings.debug.simulate.{id}.name", id);
                items.Add(new DebugSimErrorItem
                {
                    Id = id,
                    DisplayName = name,
                    ActivateText = activate
                });
            }

            Items.ItemsSource = items;

            TaskbarTitleText.Text = LocalizationManager.Get("settings.debug.taskbar.title", "Taskbar");
            TaskbarTestLabel.Text = LocalizationManager.Get("settings.debug.taskbar.test.name", "Test Taskbar");
            TaskbarTestButtonText.Text = LocalizationManager.Get("settings.debug.taskbar.open", "Open");
        }

        private void ActivateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not Wpf.Ui.Controls.Button btn)
                    return;

                string? id = btn.Tag as string;
                if (string.IsNullOrWhiteSpace(id))
                    return;

                if (!_factories.TryGetValue(id, out var factory))
                    return;

                string name = LocalizationManager.Get($"settings.debug.simulate.{id}.name", id);
                string prefix = LocalizationManager.Get("settings.debug.simulate.prefix", "模拟错误");
                _showError($"{prefix}：{name}", factory());
            }
            catch (Exception ex)
            {
                _showError("测试错误弹窗", ex);
            }
        }

        private void OpenTaskbarTestButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _openTaskbarTest();
            }
            catch (Exception ex)
            {
                _showError("Taskbar", ex);
            }
        }

        private void InitFactories()
        {
            _factories["testDialog"] = () => new InvalidOperationException("TEST");
            _factories["dllNotFound"] = () => new DllNotFoundException("Simulated DllNotFoundException");
            _factories["entryPointNotFound"] = () => new EntryPointNotFoundException("Simulated EntryPointNotFoundException");
            _factories["badImageFormat"] = () => new BadImageFormatException("Simulated BadImageFormatException");
            _factories["unauthorized"] = () => new UnauthorizedAccessException("Simulated UnauthorizedAccessException");
            _factories["fileNotFound"] = () => new FileNotFoundException("Simulated FileNotFoundException", "missing.bin");
            _factories["directoryNotFound"] = () => new DirectoryNotFoundException("Simulated DirectoryNotFoundException");
            _factories["pathTooLong"] = () => new PathTooLongException("Simulated PathTooLongException");
            _factories["fileInUse"] = () => new IOException("Simulated sharing violation", unchecked((int)0x80070020));
            _factories["diskFull"] = () => new IOException("Simulated disk full", unchecked((int)0x80070070));
            _factories["invalidJson"] = () => new System.Text.Json.JsonException("Simulated JsonException");
            _factories["cryptoFailed"] = () => new CryptographicException("Simulated CryptographicException");
            _factories["invalidArgument"] = () => new ArgumentException("Simulated ArgumentException");
            _factories["invalidFormat"] = () => new FormatException("Simulated FormatException");
            _factories["notSupported"] = () => new NotSupportedException("Simulated NotSupportedException");
            _factories["outOfMemory"] = () => new OutOfMemoryException("Simulated OutOfMemoryException");
            _factories["cancelled"] = () => new OperationCanceledException("Simulated OperationCanceledException");
            _factories["preflightFailed"] = () => new InvalidOperationException("预检失败：目标目录中存在被占用或不可写入的文件，为避免半途失败已终止。");
            _factories["nativePatchFailed"] = () => new InvalidOperationException("原生补丁应用失败，错误码：-17");
        }
    }
}
