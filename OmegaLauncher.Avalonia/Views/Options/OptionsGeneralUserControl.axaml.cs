using Avalonia.Controls;
using Avalonia.Interactivity;
using OmegaLauncher.Avalonia.Views.Dialogs;
using OmegaLauncher.Core.ClientManagement;
using OmegaLauncher.Core.Models;
using System;

namespace OmegaLauncher.Avalonia.Views.Options;

public partial class OptionsGeneralUserControl : OptionsUserControl
{
    public OptionsGeneralUserControl()
    {
        InitializeComponent();
    }

    public override void Initialize(Window owner, ClientLauncher clientLauncher)
    {
        base.Initialize(owner, clientLauncher);

        LaunchConfig config = _clientLauncher.LaunchConfig;

        SkipStartupMoviesCheckBox.IsChecked = config.NoStartupMovies;
        NoSplashCheckBox.IsChecked = config.NoSplash;

        ForceCustomResolutionCheckBox.IsChecked = config.ForceCustomResolution;
        CustomResolutionXTextBox.Text = config.CustomResolutionX.ToString();
        CustomResolutionYTextBox.Text = config.CustomResolutionY.ToString();
        EnableCustomResolutionTextBoxes(config.ForceCustomResolution);

        NoHomeDirectoryCheckBox.IsChecked = config.NoHomeDirectory;

        EnableLocaleOverrideCheckBox.IsChecked = config.EnableLocaleOverride;

        LocaleOverrideComboBox.Items.Clear();
        for (int i = 0; i < clientLauncher.Locales.Length; i++)
        {
            string locale = clientLauncher.Locales[i];

            ComboBoxItem localeItem = new() { Content = locale, Tag = locale };
            LocaleOverrideComboBox.Items.Add(localeItem);

            if (string.Equals(locale, config.LocaleOverride, StringComparison.OrdinalIgnoreCase))
                LocaleOverrideComboBox.SelectedIndex = i;
        }
        LocaleOverrideComboBox.IsEnabled = config.EnableLocaleOverride;

        EnableAutoLoginCheckBox.IsChecked = config.EnableAutoLogin;
        AutoLoginEmailAddressTextBox.Text = config.AutoLoginEmailAddress;
        AutoLoginPasswordTextBox.Text = config.AutoLoginPassword;
        EnableAutoLoginTextboxes(config.EnableAutoLogin);

        CustomArgumentsTextBox.Text = config.CustomArguments;
    }

    public override bool ValidateInput(out string message)
    {
        if (base.ValidateInput(out message) == false)
            return false;

        string customResolutionX = CustomResolutionXTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(customResolutionX) || int.TryParse(customResolutionX, out _) == false)
        {
            message = "Invalid custom resolution width.";
            return false;
        }

        string customResolutionY = CustomResolutionYTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(customResolutionY) || int.TryParse(customResolutionY, out _) == false)
        {
            message = "Invalid custom resolution height.";
            return false;
        }

        return true;
    }

    public override void UpdateClientLauncher()
    {
        base.UpdateClientLauncher();

        LaunchConfig config = _clientLauncher.LaunchConfig;

        config.NoStartupMovies = SkipStartupMoviesCheckBox.IsChecked == true;
        config.NoSplash = NoSplashCheckBox.IsChecked == true;

        config.ForceCustomResolution = ForceCustomResolutionCheckBox.IsChecked == true;
        config.CustomResolutionX = int.Parse(CustomResolutionXTextBox.Text.Trim());
        config.CustomResolutionY = int.Parse(CustomResolutionYTextBox.Text.Trim());

        config.NoHomeDirectory = NoHomeDirectoryCheckBox.IsChecked == true;

        config.EnableLocaleOverride = EnableLocaleOverrideCheckBox.IsChecked == true;
        int localeOverrideIndex = LocaleOverrideComboBox.SelectedIndex;
        if (localeOverrideIndex != -1)
            config.LocaleOverride = (string)((ComboBoxItem)LocaleOverrideComboBox.Items[localeOverrideIndex]).Tag;

        config.EnableAutoLogin = EnableAutoLoginCheckBox.IsChecked == true;
        config.AutoLoginEmailAddress = AutoLoginEmailAddressTextBox.Text;
        config.AutoLoginPassword = AutoLoginPasswordTextBox.Text;

        config.CustomArguments = CustomArgumentsTextBox.Text;
    }

    private void EnableCustomResolutionTextBoxes(bool enable)
    {
        CustomResolutionXTextBox.IsEnabled = enable;
        CustomResolutionYTextBox.IsEnabled = enable;
    }

    private void EnableAutoLoginTextboxes(bool enable)
    {
        AutoLoginEmailAddressTextBox.IsEnabled = enable;
        AutoLoginPasswordTextBox.IsEnabled = enable;
    }

    #region Event Handlers

    private void ForceCustomResolutionCheckBox_IsCheckedChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox checkBox)
            return;

        EnableCustomResolutionTextBoxes(checkBox.IsChecked == true);
    }

    private void OverrideLocaleCheckBox_IsCheckedChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox checkBox)
            return;

        LocaleOverrideComboBox.IsEnabled = checkBox.IsChecked == true;
    }

    private async void EnableAutoLoginCheckBox_IsCheckedChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox checkBox)
            return;

        bool isChecked = checkBox.IsChecked == true;

        if (isChecked && _owner != null && _owner.IsVisible)
        {
            DialogResult result = await MessageBoxWindow.Show(_owner,
                "Auto-login does not store your credentials in a secure way. " +
                "You should use this option only with throwaway credentials on local servers. " +
                "Are you sure you want to enable auto-login?",
                "Confirm Enable Auto-Login", MessageBoxType.OKCancel);

            if (result != DialogResult.OK)
            {
                checkBox.IsChecked = false;
                return;
            }
        }

        EnableAutoLoginTextboxes(isChecked);
    }

    #endregion
}