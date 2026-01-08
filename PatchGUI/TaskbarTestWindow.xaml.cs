using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shell;

namespace PatchGUI
{
    public partial class TaskbarTestWindow : Wpf.Ui.Controls.FluentWindow
    {
        private sealed class TaskbarStateItem
        {
            public TaskbarItemProgressState State { get; init; }
            public string DisplayName { get; init; } = string.Empty;
        }

        private readonly TaskbarItemInfo _target;
        private bool _initialized;

        public TaskbarTestWindow(TaskbarItemInfo target)
        {
            InitializeComponent();

            _target = target ?? throw new ArgumentNullException(nameof(target));
            RefreshLocalization();

            var items = new List<TaskbarStateItem>
            {
                new()
                {
                    State = TaskbarItemProgressState.None,
                    DisplayName = LocalizationManager.Get("settings.debug.taskbar.state.none", "None (hidden)")
                },
                new()
                {
                    State = TaskbarItemProgressState.Indeterminate,
                    DisplayName = LocalizationManager.Get("settings.debug.taskbar.state.indeterminate", "Indeterminate")
                },
                new()
                {
                    State = TaskbarItemProgressState.Normal,
                    DisplayName = LocalizationManager.Get("settings.debug.taskbar.state.normal", "Normal")
                },
                new()
                {
                    State = TaskbarItemProgressState.Paused,
                    DisplayName = LocalizationManager.Get("settings.debug.taskbar.state.paused", "Paused")
                },
                new()
                {
                    State = TaskbarItemProgressState.Error,
                    DisplayName = LocalizationManager.Get("settings.debug.taskbar.state.error", "Error")
                }
            };

            StateComboBox.ItemsSource = items;

            ValueSlider.Value = Math.Clamp(_target.ProgressValue, 0.0, 1.0) * 100.0;
            PreviewProgressBar.Value = ValueSlider.Value;
            ValueText.Text = $"{Math.Round(ValueSlider.Value):0}%";

            foreach (var item in items)
            {
                if (item.State == _target.ProgressState)
                {
                    StateComboBox.SelectedItem = item;
                    break;
                }
            }

            _initialized = true;
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                ResetTarget();
            }
            catch
            {
                // ignore
            }
            base.OnClosed(e);
        }

        public void RefreshLocalization()
        {
            if (WindowTitleBar == null)
                return;

            Title = LocalizationManager.Get("settings.debug.taskbar.test.windowTitle", "Taskbar Test");
            WindowTitleBar.Title = Title;
            HeaderText.Text = LocalizationManager.Get("settings.debug.taskbar.test.header", "Taskbar Test");
            DescriptionText.Text = LocalizationManager.Get(
                "settings.debug.taskbar.test.desc",
                "Use this window to test taskbar progress states (None/Indeterminate/Normal/Error/Paused).");
            StateLabel.Text = LocalizationManager.Get("settings.debug.taskbar.test.state", "State");
            ValueLabel.Text = LocalizationManager.Get("settings.debug.taskbar.test.value", "Progress");
            ResetButtonText.Text = LocalizationManager.Get("settings.debug.taskbar.test.reset", "Reset");

            RefreshStateItems();
        }

        private void RefreshStateItems()
        {
            if (StateComboBox == null)
                return;

            var selectedState = _target.ProgressState;

            var items = new List<TaskbarStateItem>
            {
                new()
                {
                    State = TaskbarItemProgressState.None,
                    DisplayName = LocalizationManager.Get("settings.debug.taskbar.state.none", "None (hidden)")
                },
                new()
                {
                    State = TaskbarItemProgressState.Indeterminate,
                    DisplayName = LocalizationManager.Get("settings.debug.taskbar.state.indeterminate", "Indeterminate")
                },
                new()
                {
                    State = TaskbarItemProgressState.Normal,
                    DisplayName = LocalizationManager.Get("settings.debug.taskbar.state.normal", "Normal")
                },
                new()
                {
                    State = TaskbarItemProgressState.Paused,
                    DisplayName = LocalizationManager.Get("settings.debug.taskbar.state.paused", "Paused")
                },
                new()
                {
                    State = TaskbarItemProgressState.Error,
                    DisplayName = LocalizationManager.Get("settings.debug.taskbar.state.error", "Error")
                }
            };

            bool prevInit = _initialized;
            _initialized = false;
            StateComboBox.ItemsSource = items;
            foreach (var item in items)
            {
                if (item.State == selectedState)
                {
                    StateComboBox.SelectedItem = item;
                    break;
                }
            }
            _initialized = prevInit;
        }

        private void StateComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_initialized)
                return;

            if (StateComboBox.SelectedItem is not TaskbarStateItem item)
                return;

            _target.ProgressState = item.State;
        }

        private void ValueSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_initialized)
                return;

            double percent = e.NewValue;
            PreviewProgressBar.Value = percent;
            ValueText.Text = $"{Math.Round(percent):0}%";

            _target.ProgressValue = Math.Clamp(percent / 100.0, 0.0, 1.0);
        }

        private void ResetTarget()
        {
            _target.ProgressState = TaskbarItemProgressState.None;
            _target.ProgressValue = 0;
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            ResetTarget();

            ValueSlider.Value = 0;
            PreviewProgressBar.Value = 0;
            ValueText.Text = "0%";

            if (StateComboBox.ItemsSource is IEnumerable<TaskbarStateItem> items)
            {
                foreach (var item in items)
                {
                    if (item.State == TaskbarItemProgressState.None)
                    {
                        StateComboBox.SelectedItem = item;
                        break;
                    }
                }
            }
        }
    }
}
