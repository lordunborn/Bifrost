using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Bifrost.Avalonia.Themes;
using Bifrost.Avalonia.Views.Dialogs;
using Bifrost.Avalonia.Views.Options;
using Bifrost.Core.ClientManagement;
using Bifrost.Core.Models;
using Bifrost.Core.News;
using Bifrost.Core.Updates;
using Bifrost.Updates.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Bifrost.Avalonia.Views
{
    public partial class MainWindow : Window
    {
        private readonly Dictionary<NewsFeedSourceCategories, IBrush> _newsCategoryBrushes;

        private enum PlayMode
        {
            Play,       // launch the game
            Update,     // the client has to be updated first
            Cancel,     // an update is running
            Blocked     // checking, or the client cannot be used (wrong version, launcher too old)
        }

        private ClientLauncher _clientLauncher;
        private UpdateManager _updateManager;
        private UpdateCheckResult _updateCheck;
        private CancellationTokenSource _updateCts;
        private PlayMode _playMode = PlayMode.Play;
        private bool _updateCheckSucceeded;
        private string _clientVersion;

        private int _pendingNewsSources = 0;

        public MainWindow()
        {
            InitializeComponent();

            _newsCategoryBrushes = new()
            {
                { NewsFeedSourceCategories.Default,     SolidColorBrush.Parse("#0088cc") },
                { NewsFeedSourceCategories.Server,      SolidColorBrush.Parse("#0f9c23") }
            };

            ThemeManager.Instance.Initialize(AppContext.BaseDirectory);

            if (MainGrid.Background is ImageBrush defaultBackgroundBrush && defaultBackgroundBrush.Source is Bitmap defaultBackground)
                ThemeManager.Instance.AddTheme(new(LauncherTheme.DefaultName, -1, defaultBackground));

            // Clear design news placeholders asap
            if (Design.IsDesignMode == false)
                NewsItemStackPanel.Children.Clear();
        }

        private async Task InitializeAsync()
        {
            _clientLauncher = new();

            string clientPath = ClientHelper.GetClientPath();
            ClientLauncherInitializationResult result = _clientLauncher.Initialize(clientPath);
            if (result != ClientLauncherInitializationResult.Success)
                result = await FindClientAsync();

            if (result != ClientLauncherInitializationResult.Success)
            {
                Close();
                Environment.Exit(0);
                return;
            }

            ApplyTheme(_clientLauncher.GuiConfig.ThemeOverride);
            LoadNews();
            RefreshClientData();
            RefreshServerComboBox();
            await StartUpdatesAsync();
        }

        /// <summary>Looks for the game in the saved folder and the Steam libraries, then asks the player.</summary>
        private async Task<ClientLauncherInitializationResult> FindClientAsync()
        {
            foreach (string path in ClientHelper.GetFallbackPaths(_clientLauncher.GuiConfig.ClientPath))
            {
                string root = ClientHelper.ResolveRoot(path);
                if (root != null && _clientLauncher.Initialize(root) == ClientLauncherInitializationResult.Success)
                {
                    _clientLauncher.GuiConfig.ClientPath = root;
                    return ClientLauncherInitializationResult.Success;
                }
            }

            DialogResult ask = await MessageBoxWindow.Show(this, "Marvel Heroes was not found. Select the folder that contains the game (the one with UnrealEngine3 and Data in it).",
                "Game folder", MessageBoxType.OKCancel);

            while (ask == DialogResult.OK)
            {
                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Select your Marvel Heroes folder", AllowMultiple = false });
                string picked = folders.FirstOrDefault()?.TryGetLocalPath();
                if (picked == null)
                    break;

                string root = ClientHelper.ResolveRoot(picked);
                if (root != null && _clientLauncher.Initialize(root) == ClientLauncherInitializationResult.Success)
                {
                    _clientLauncher.GuiConfig.ClientPath = root;
                    return ClientLauncherInitializationResult.Success;
                }

                ask = await MessageBoxWindow.Show(this, "That is not a Marvel Heroes folder. Try again?", "Game folder", MessageBoxType.OKCancel);
            }

            return ClientLauncherInitializationResult.ClientNotFound;
        }

        /// <summary>The player picked another game folder in the Updates window: start over with it.</summary>
        private async Task ReinitializeClientAsync()
        {
            string path = _clientLauncher.GuiConfig.ClientPath;
            ClientLauncherInitializationResult result = _clientLauncher.Initialize(path);
            if (result != ClientLauncherInitializationResult.Success)
            {
                await MessageBoxWindow.Show(this, ClientLauncher.GetInitializationResultText(result), "Game folder");
                return;
            }

            RefreshClientData();
            RefreshServerComboBox();
            await StartUpdatesAsync();
        }

        #region Updates

        private async Task StartUpdatesAsync()
        {
            _updateManager?.Dispose();
            _updateManager = null;
            _updateCheck = null;

            if (_clientLauncher.Deployment.UpdatesConfigured == false)
            {
                UpdateStatusTextBlock.Text = string.Empty;
                SetPlayMode(PlayMode.Play);
                return;
            }

            _updateManager = new(_clientLauncher.Deployment, _clientLauncher.ClientRoot);

            if (_clientLauncher.GuiConfig.AutoCheckUpdates || Program.AutoPlay)
                await CheckForUpdatesAsync();
            else
                UpdateStatusTextBlock.Text = "Updates not checked. Open Updates to check.";

            if (Program.AutoPlay && _updateCheckSucceeded)
            {
                if (_playMode == PlayMode.Update)
                    await RunUpdateAsync(launchAfter: true);
                else if (_playMode == PlayMode.Play)
                    await LaunchAsync();
            }
        }

        private void SetPlayMode(PlayMode mode)
        {
            _playMode = mode;
            PlayButton.IsEnabled = mode != PlayMode.Blocked;
            PlayButton.Content = mode switch
            {
                PlayMode.Update => "Update",
                PlayMode.Cancel => "Cancel",
                _ => "Play"
            };
        }

        private void SetControlsEnabled(bool enabled)
        {
            OptionsButton.IsEnabled = enabled;
            UpdatesButton.IsEnabled = enabled;
            ServerComboBox.IsEnabled = enabled;
            ManageServersButton.IsEnabled = enabled;
        }

        private async Task CheckForUpdatesAsync()
        {
            SetControlsEnabled(false);
            SetPlayMode(PlayMode.Blocked);
            UpdateStatusTextBlock.Text = "Checking for updates...";
            _updateCheckSucceeded = false;
            _updateCts = new();

            try
            {
                Progress<PlanProgress> progress = new(p => UpdateStatusTextBlock.Text = $"Checking game files ({p.FilesDone}/{p.FilesTotal})");
                _updateCheck = await _updateManager.CheckAsync(false, progress, _updateCts.Token);
                _updateCheckSucceeded = true;
                ApplyCheckResult(_updateCheck);
            }
            catch (OperationCanceledException)
            {
                UpdateStatusTextBlock.Text = "Update check cancelled.";
                SetPlayMode(PlayMode.Play);
            }
            catch (Exception ex)
            {
                // Could not tell whether the client is current (offline, host down, bad signature). The server decides whether this client may play.
                UpdateStatusTextBlock.Text = ex is UpdateException ? ex.Message : "Could not check for updates: " + ex.Message;
                SetPlayMode(PlayMode.Play);
            }
            finally
            {
                _updateCts.Dispose();
                _updateCts = null;
                SetControlsEnabled(true);
            }
        }

        private void ApplyCheckResult(UpdateCheckResult check)
        {
            string text = UpdateManager.DescribeShort(check);

            if (check.LauncherTooOld)
            {
                UpdateStatusTextBlock.Text = text;
                SetPlayMode(PlayMode.Blocked);
                return;
            }

            if (UpdateManager.HasWrongBaseVersion(check, _clientVersion))
            {
                UpdateStatusTextBlock.Text = $"Wrong game version {_clientVersion}: the server needs {check.Manifest.BaseClientVersion}. See Updates.";
                SetPlayMode(PlayMode.Blocked);
                return;
            }

            if (check.BaseCheck != null && check.BaseCheck.Ok == false)
                text += " (some stock game files look wrong, see Updates)";

            UpdateStatusTextBlock.Text = text;
            SetPlayMode(check.Required ? PlayMode.Update : PlayMode.Play);
        }

        private async Task RunUpdateAsync(bool launchAfter)
        {
            UpdateCheckResult check = _updateCheck;
            if (check == null || check.UpToDate)
                return;

            DialogResult confirm = await MessageBoxWindow.Show(this,
                $"Download {UpdateManager.FormatBytes(check.Plan.DownloadBytes)} and update {check.Plan.FileCount} game file(s)? The original files are backed up first.",
                "Update", MessageBoxType.OKCancel);

            if (confirm != DialogResult.OK)
                return;

            SetControlsEnabled(false);
            SetPlayMode(PlayMode.Cancel);
            UpdateProgressBar.Value = 0;
            UpdateProgressBar.IsVisible = true;
            _updateCts = new();

            try
            {
                Progress<UpdateProgress> progress = new(p =>
                {
                    UpdateProgressBar.Value = p.BytesToDownload > 0 ? 100.0 * p.BytesDownloaded / p.BytesToDownload : 0;
                    UpdateStatusTextBlock.Text = $"Updating {p.FilesDone}/{p.FilesTotal}: {UpdateManager.FormatBytes(p.BytesDownloaded)} of {UpdateManager.FormatBytes(p.BytesToDownload)}";
                });

                await _updateManager.ApplyAsync(progress, _updateCts.Token);
                _updateCheck = _updateManager.LastCheck;
                ApplyCheckResult(_updateCheck);
            }
            catch (OperationCanceledException)
            {
                UpdateStatusTextBlock.Text = "Update cancelled. You can continue it later.";
                SetPlayMode(PlayMode.Update);
                launchAfter = false;
            }
            catch (Exception ex)
            {
                UpdateStatusTextBlock.Text = ex is UpdateException ? ex.Message : "The update failed: " + ex.Message;
                SetPlayMode(PlayMode.Update);
                launchAfter = false;
            }
            finally
            {
                _updateCts.Dispose();
                _updateCts = null;
                UpdateProgressBar.IsVisible = false;
                SetControlsEnabled(true);
            }

            if (launchAfter && _playMode == PlayMode.Play)
                await LaunchAsync();
        }

        #endregion

        private async Task LaunchAsync()
        {
            SetControlsEnabled(false);
            PlayButton.IsEnabled = false;

            // Do the launch asynchronously because it may be delayed by an antivirus check.
            if (await Task.Run(_clientLauncher.Launch))
            {
                Close();
            }
            else
            {
                await MessageBoxWindow.Show(this, string.IsNullOrEmpty(_clientLauncher.LastLaunchError) ? "Failed to launch game client." : _clientLauncher.LastLaunchError, "Error");
                SetControlsEnabled(true);
                PlayButton.IsEnabled = true;
            }
        }

        private void ApplyTheme(string name)
        {
            LauncherTheme theme = ThemeManager.Instance.GetTheme(name);
            if (theme == null)
                return;

            ImageBrush imageBrush = new() { Source = theme.Background };
            MainGrid.Background = imageBrush;
        }

        private void LoadNews()
        {
            if (_clientLauncher.GuiConfig.NewsCategoryFilter == NewsFeedSourceCategories.None)
            {
                NewsGrid.IsVisible = false;
                return;
            }

            NewsGrid.IsVisible = true;

            _clientLauncher.RefreshNewsFeedSources();
            Interlocked.Add(ref _pendingNewsSources, _clientLauncher.NewsFeed.Sources.Count);

            foreach (NewsFeedSource source in _clientLauncher.NewsFeed.Sources.Values)
                Task.Run(() => source.Load(OnNewsFeedSourceLoaded));
        }

        private void OnNewsFeedSourceLoaded(NewsFeedSource source)
        {
            Interlocked.Decrement(ref _pendingNewsSources);

            if (_pendingNewsSources == 0)
                Dispatcher.UIThread.Invoke(RefreshNewsFeed);
        }

        private void RefreshNewsFeed()
        {
            const int MaxNewsFeedItems = 10;

            List<NewsFeedItem> newsList = new();
            _clientLauncher.NewsFeed.GetNews(newsList);

            Controls newsItems = NewsItemStackPanel.Children;
            newsItems.Clear();

            for (int i = 0; i < newsList.Count && i < MaxNewsFeedItems; i++)
            {
                NewsFeedItem newsItem = newsList[i];
                HyperlinkButton button = new() { NavigateUri = new Uri(newsItem.Url) };

                if (_newsCategoryBrushes.TryGetValue(newsItem.Source.Category, out IBrush borderBrush))
                    button.BorderBrush = borderBrush;

                button.Classes.Add("news-item");
                button.Content = new TextBlock() { Text = newsItem.Title };
                newsItems.Add(button);
            }
        }

        private void RefreshClientData()
        {
            _clientVersion = ClientInspector.Inspect(_clientLauncher.ClientRoot).Version;

            string version = _clientLauncher.ClientMetadata.Version;
            if (version == ClientMetadata.Unknown.Version && _clientVersion != null)
                version = _clientVersion;

            VersionTextBlock.Text = $"Game Version: {version}";
        }

        private void RefreshServerComboBox()
        {
            ServerComboBox.Items.Clear();
            foreach (ServerInfo serverInfo in _clientLauncher.ServerManager)
            {
                ComboBoxItem serverItem = new() { Content = serverInfo.Name };
                ServerComboBox.Items.Add(serverItem);
            }

            ServerComboBox.SelectedIndex = _clientLauncher.LaunchConfig.ServerIndex;
        }

        #region Event Handlers

        private async void Window_Opened(object sender, EventArgs e)
        {
            if (Design.IsDesignMode == false)
                await InitializeAsync();
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            _updateCts?.Cancel();
            _updateManager?.Dispose();
            _clientLauncher?.SaveData();
        }

        private async void OptionsButton_Click(object sender, RoutedEventArgs e)
        {
            GuiConfig oldConfig = _clientLauncher.GuiConfig.Clone();

            OptionsWindow optionsWindow = new(_clientLauncher);
            await optionsWindow.ShowDialog(this);

            if (_clientLauncher.GuiConfig.ThemeOverride != oldConfig.ThemeOverride)
                ApplyTheme(_clientLauncher.GuiConfig.ThemeOverride);

            if (_clientLauncher.GuiConfig.NewsCategoryFilter != oldConfig.NewsCategoryFilter ||
                _clientLauncher.GuiConfig.DefaultNewsFeedUrl != oldConfig.DefaultNewsFeedUrl)
            {
                LoadNews();
            }
        }

        private void ServerComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_clientLauncher == null)
                return;

            int selectedIndex = ServerComboBox.SelectedIndex;
            if (selectedIndex == -1)
                return;

            _clientLauncher.LaunchConfig.ServerIndex = selectedIndex;
        }

        private async void ManageServersButton_Click(object sender, RoutedEventArgs e)
        {
            ServerManagementWindow serverManagementWindow = new(_clientLauncher);
            await serverManagementWindow.ShowDialog(this);

            if (serverManagementWindow.ServerListChanged)
            {
                LoadNews();
                RefreshServerComboBox();
            }
        }

        private async void UpdatesButton_Click(object sender, RoutedEventArgs e)
        {
            UpdatesWindow updatesWindow = new(_clientLauncher, _updateManager);
            await updatesWindow.ShowDialog(this);

            if (updatesWindow.ClientFolderChanged)
            {
                await ReinitializeClientAsync();
            }
            else if (_updateManager?.LastCheck != null)
            {
                _updateCheck = _updateManager.LastCheck;
                ApplyCheckResult(_updateCheck);
            }
            else if (_updateManager != null)
            {
                // The files were restored to stock: look again.
                await CheckForUpdatesAsync();
            }
        }

        private async void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (Design.IsDesignMode)
                return;

            switch (_playMode)
            {
                case PlayMode.Cancel:
                    _updateCts?.Cancel();
                    break;

                case PlayMode.Update:
                    await RunUpdateAsync(launchAfter: false);
                    break;

                case PlayMode.Play:
                    await LaunchAsync();
                    break;
            }
        }

        #endregion
    }
}