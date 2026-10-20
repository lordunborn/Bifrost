using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using OmegaLauncher.Avalonia.Views.Dialogs;
using OmegaLauncher.Core.Platform;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using OmegaLauncher.Avalonia.Themes;
using OmegaLauncher.Core.ClientManagement;
using OmegaLauncher.Core.Models;
using OmegaLauncher.Core.News;
using System.Linq;

namespace OmegaLauncher.Avalonia.Views.Options;

public partial class OptionsLauncherUserControl : OptionsUserControl
{
    private string _selectedRunnerPath = string.Empty;

    public OptionsLauncherUserControl()
    {
        InitializeComponent();
    }

    // Choosing a runner is a Linux / Steam Deck thing. SteamDeck.IsForced shows the section on any machine for dry runs and screenshots.
    private static bool ShowRunnerSection => OperatingSystem.IsLinux() || SteamDeck.IsForced;

    private void FillRunnerComboBox(string selectedPath)
    {
        RunnerComboBox.Items.Clear();
        RunnerComboBox.Items.Add(new ComboBoxItem { Content = "Automatic (best one found)", Tag = string.Empty });

        List<GameRunner> runners = _clientLauncher.DetectRunners();
        int selectedIndex = 0;

        foreach (GameRunner runner in runners)
        {
            RunnerComboBox.Items.Add(new ComboBoxItem { Content = $"{runner.Name} ({runner.Kind})", Tag = runner.Path });
            if (runner.Path == selectedPath)
                selectedIndex = RunnerComboBox.Items.Count - 1;
        }

        // A runner picked with Browse that is not in the detected list stays selectable.
        if (string.IsNullOrEmpty(selectedPath) == false && selectedIndex == 0)
        {
            GameRunner custom = LinuxRunner.FromPath(selectedPath);
            RunnerComboBox.Items.Add(new ComboBoxItem { Content = custom != null ? $"{custom.Name} (chosen)" : selectedPath, Tag = selectedPath });
            selectedIndex = RunnerComboBox.Items.Count - 1;
        }

        RunnerComboBox.SelectedIndex = selectedIndex;
        RunnerHintTextBlock.Text = runners.Count == 0
            ? "Nothing found. Install Proton-GE (ProtonUp-Qt), umu-launcher or Wine, then press Refresh, or use Browse."
            : $"{runners.Count} found. Automatic uses the first one.";
    }

    public override void Initialize(Window owner, ClientLauncher clientLauncher)
    {
        base.Initialize(owner, clientLauncher);

        GuiConfig config = _clientLauncher.GuiConfig;

        RunnerSection.IsVisible = ShowRunnerSection;
        _selectedRunnerPath = config.RunnerPath;
        FillRunnerComboBox(_selectedRunnerPath);
        PrefixTextBox.Watermark = LinuxRunner.DefaultPrefix(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        PrefixTextBox.Text = config.PrefixPath;
        DeckResolutionCheckBox.IsChecked = config.DeckResolution;

        int i = 0;
        int selectedIndex = -1;

        ThemeOverrideComboBox.Items.Clear();
        foreach (LauncherTheme theme in ThemeManager.Instance.Themes.OrderBy(theme => theme.SortOrder))
        {
            ComboBoxItem item = new() { Content = theme.Name, Tag = theme };
            ThemeOverrideComboBox.Items.Add(item);

            if (theme.Name == config.ThemeOverride)
                selectedIndex = i;
            i++;
        }

        ThemeOverrideComboBox.SelectedIndex = selectedIndex != -1 ? selectedIndex : 0;

        NewsFeedSourceCategories newsCategoryFilter = config.NewsCategoryFilter;
        EnableDefaultNews.IsChecked = newsCategoryFilter.HasFlag(NewsFeedSourceCategories.Default);
        EnableServerNews.IsChecked = newsCategoryFilter.HasFlag(NewsFeedSourceCategories.Server);
        DefaultNewsFeedUrlTextBox.Text = config.DefaultNewsFeedUrl;
    }

    public override bool ValidateInput(out string message)
    {
        message = string.Empty;

        string prefix = PrefixTextBox.Text?.Trim() ?? string.Empty;
        if (prefix.Length > 0 && Path.IsPathRooted(prefix) == false)
        {
            message = "The Wine prefix must be a full folder path (starting with /), or empty to use the default.";
            return false;
        }

        return true;
    }

    private async void RefreshRunnersButton_Click(object sender, RoutedEventArgs e)
    {
        FillRunnerComboBox(RunnerComboBox.SelectedItem is ComboBoxItem { Tag: string path } ? path : string.Empty);
        await Task.CompletedTask;
    }

    private async void BrowseRunnerButton_Click(object sender, RoutedEventArgs e)
    {
        var files = await TopLevel.GetTopLevel(this).StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select proton, umu-run or wine",
            AllowMultiple = false
        });

        string picked = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (picked == null)
            return;

        if (LinuxRunner.FromPath(picked) == null)
        {
            await MessageBoxWindow.Show(_owner, "That file is not proton, umu-run or wine.", "Runner");
            return;
        }

        FillRunnerComboBox(picked);
    }

    public override void UpdateClientLauncher()
    {
        base.UpdateClientLauncher();

        GuiConfig config = _clientLauncher.GuiConfig;

        if (ShowRunnerSection)
        {
            config.RunnerPath = RunnerComboBox.SelectedItem is ComboBoxItem { Tag: string runnerPath } ? runnerPath : string.Empty;
            config.PrefixPath = PrefixTextBox.Text?.Trim() ?? string.Empty;
            config.DeckResolution = DeckResolutionCheckBox.IsChecked == true;
        }

        if (ThemeOverrideComboBox.SelectedItem is ComboBoxItem themeOverrideItem && themeOverrideItem.Tag is LauncherTheme theme)
            config.ThemeOverride = theme.Name;

        NewsFeedSourceCategories newsCategoryFilter = NewsFeedSourceCategories.All;
        if (EnableDefaultNews.IsChecked != true)
            newsCategoryFilter &= ~NewsFeedSourceCategories.Default;

        if (EnableServerNews.IsChecked != true)
            newsCategoryFilter &= ~NewsFeedSourceCategories.Server;

        config.NewsCategoryFilter = newsCategoryFilter;
        config.DefaultNewsFeedUrl = DefaultNewsFeedUrlTextBox.Text.Trim();
    }
}