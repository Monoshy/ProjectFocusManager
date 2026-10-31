using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using FocusManager.App.Views.Dialogs;
using FocusManager.Core.Services;

namespace FocusManager.App.Views;

public partial class SettingsView : UserControl
{
    private static readonly string[] ThemeOptions = ["浅色模式", "深色模式"];

    /// <summary>暂停提醒的四个选项，数字和 PauseReminderRule.AllowedMinutes 一一对应。</summary>
    private static readonly int[] ReminderMinutes = PauseReminderRule.AllowedMinutes;

    private static readonly CloseBehavior[] CloseBehaviors =
        [CloseBehavior.Ask, CloseBehavior.MinimizeToTray, CloseBehavior.Exit];

    private bool _syncingCloseBehavior;

    /// <summary>程序自己改下拉框选中项时，避免又被当成"用户点了"。</summary>
    private bool _syncingTheme;

    private bool _syncingReminder;

    public SettingsView()
    {
        InitializeComponent();

        var services = AppServices.Current;

        if (services is not null)
        {
            DataDirectoryBox.Text = services.DataDirectory;
        }

        VersionText.Text = "版本 " + (typeof(SettingsView).Assembly.GetName().Version?.ToString(3) ?? "0.1.0");

        ThemeComboBox.ItemsSource = ThemeOptions;
        SyncThemeComboBox();

        PauseReminderComboBox.ItemsSource = ReminderMinutes.Select(ToReminderLabel).ToArray();
        SyncPauseReminderComboBox();

        CloseBehaviorComboBox.ItemsSource = CloseBehaviors
            .Select(CloseBehaviorRule.ToDisplayName)
            .ToArray();
        SyncCloseBehaviorComboBox();

        // 在别处（左侧栏按钮）切换主题时，这里的下拉框也要跟着变。
        ThemeManager.ThemeChanged += OnThemeChanged;
        Unloaded += (_, _) => ThemeManager.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, EventArgs e) => SyncThemeComboBox();

    private void SyncThemeComboBox()
    {
        _syncingTheme = true;

        try
        {
            ThemeComboBox.SelectedIndex = ThemeManager.IsDark ? 1 : 0;
        }
        finally
        {
            _syncingTheme = false;
        }
    }

    private void OnThemeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingTheme) return;

        var services = AppServices.Current;

        if (services is null) return;

        ThemeManager.Set(services.Settings, ThemeComboBox.SelectedIndex == 1);
    }

    private static string ToReminderLabel(int minutes)
        => minutes == 0 ? "不提醒" : minutes + " 分钟";

    private void SyncPauseReminderComboBox()
    {
        _syncingReminder = true;

        try
        {
            var services = AppServices.Current;
            var stored = services?.Settings.Get(PauseReminderRule.SettingKey);
            var threshold = PauseReminderRule.ResolveThreshold(stored);
            var minutes = threshold is null ? 0 : (int)threshold.Value.TotalMinutes;
            var index = Array.IndexOf(ReminderMinutes, minutes);

            PauseReminderComboBox.SelectedIndex = index >= 0 ? index : 0;
        }
        finally
        {
            _syncingReminder = false;
        }
    }

    /// <summary>把数据库里存的行为显示到下拉框上。</summary>
    private void SyncCloseBehaviorComboBox()
    {
        var current = CloseBehaviorRule.Resolve(AppServices.Current?.Settings.Get(CloseBehaviorRule.SettingKey));

        _syncingCloseBehavior = true;

        try
        {
            var index = Array.IndexOf(CloseBehaviors, current);

            CloseBehaviorComboBox.SelectedIndex = index < 0 ? 0 : index;
        }
        finally
        {
            _syncingCloseBehavior = false;
        }
    }

    private void OnCloseBehaviorSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingCloseBehavior) return;

        var services = AppServices.Current;

        if (services is null) return;

        var index = CloseBehaviorComboBox.SelectedIndex;

        if (index < 0 || index >= CloseBehaviors.Length) return;

        services.Settings.Set(
            CloseBehaviorRule.SettingKey,
            CloseBehaviorRule.ToSettingValue(CloseBehaviors[index]));
    }

    private void OnPauseReminderSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingReminder) return;

        var services = AppServices.Current;

        if (services is null) return;

        var index = PauseReminderComboBox.SelectedIndex;

        if (index < 0 || index >= ReminderMinutes.Length) return;

        services.Settings.Set(PauseReminderRule.SettingKey, ReminderMinutes[index].ToString(CultureInfo.InvariantCulture));
    }

    private void OnOpenDataFolderClick(object sender, RoutedEventArgs e)
    {
        var services = AppServices.Current;

        if (services is null) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = services.DataDirectory,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            AppMessage.Show(Window.GetWindow(this), "打不开文件夹：" + ex.Message, "提示", MessageKind.Warning);
        }
    }

    private void OnBackupClick(object sender, RoutedEventArgs e)
    {
        var services = AppServices.Current;

        if (services is null) return;

        try
        {
            var path = services.Database.Backup("手动备份");

            BackupResultText.Text = "备份好了：" + Path.GetFileName(path);
            BackupResultText.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            BackupResultText.Text = "备份失败：" + ex.Message;
            BackupResultText.Visibility = Visibility.Visible;
        }
    }
}
