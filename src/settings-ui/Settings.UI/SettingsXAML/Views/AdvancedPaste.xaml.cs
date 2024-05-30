// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Input;
using ManagedCommon;
using Microsoft.PowerToys.Settings.UI.Helpers;
using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.PowerToys.Settings.UI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Security.Credentials;

namespace Microsoft.PowerToys.Settings.UI.Views
{
    public sealed partial class AdvancedPastePage : Page, IRefreshablePage
    {
        private AdvancedPasteViewModel ViewModel { get; set; }

        public ICommand SaveOpenAIKeyCommand => new RelayCommand(SaveOpenAIKey);

        public AdvancedPastePage()
        {
            var settingsUtils = new SettingsUtils();
            ViewModel = new AdvancedPasteViewModel(
                settingsUtils,
                SettingsRepository<GeneralSettings>.GetInstance(settingsUtils),
                SettingsRepository<AdvancedPasteSettings>.GetInstance(settingsUtils),
                ShellPage.SendDefaultIPCMessage);
            DataContext = ViewModel;
            InitializeComponent();
        }

        public async void DeleteCustomShortcut(object sender, RoutedEventArgs e)
        {
            Button deleteRowButton = (Button)sender;

            if (deleteRowButton != null)
            {
                AdvancedPasteShortcut x = (AdvancedPasteShortcut)deleteRowButton.DataContext;
                var resourceLoader = Helpers.ResourceLoaderInstance.ResourceLoader;

                ContentDialog dialog = new ContentDialog();
                dialog.XamlRoot = XamlRoot;
                dialog.Title = x.Name;
                dialog.PrimaryButtonText = resourceLoader.GetString("Yes");
                dialog.CloseButtonText = resourceLoader.GetString("No");
                dialog.DefaultButton = ContentDialogButton.Primary;
                dialog.Content = new TextBlock() { Text = resourceLoader.GetString("Delete_Dialog_Description") };
                dialog.PrimaryButtonClick += (s, args) =>
                {
                    // Using InvariantCulture since this is internal and expected to be numerical
                    bool success = int.TryParse(deleteRowButton?.CommandParameter?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int rowNum);
                    if (success)
                    {
                        ViewModel.DeleteAdvancedPasteShortcut(rowNum);
                    }
                    else
                    {
                        Logger.LogError("Failed to delete custom image size.");
                    }
                };
                var result = await dialog.ShowAsync();
            }
        }

        public void RefreshEnabledState()
        {
            ViewModel.RefreshEnabledState();
        }

        private void AddShortcutButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ViewModel.AddRow("Test");
            }
            catch (Exception ex)
            {
                Logger.LogError("Exception encountered when adding a new image size.", ex);
            }
        }

        private void SaveOpenAIKey()
        {
            if (!string.IsNullOrEmpty(AdvancedPaste_EnableAIDialogOpenAIApiKey.Text))
            {
                ViewModel.EnableAI(AdvancedPaste_EnableAIDialogOpenAIApiKey.Text);
            }
        }

        private async void AdvancedPaste_EnableAIButton_Click(object sender, RoutedEventArgs e)
        {
            var resourceLoader = Helpers.ResourceLoaderInstance.ResourceLoader;
            EnableAIDialog.PrimaryButtonText = resourceLoader.GetString("EnableAIDialog_SaveBtnText");
            EnableAIDialog.SecondaryButtonText = resourceLoader.GetString("EnableAIDialog_CancelBtnText");
            EnableAIDialog.PrimaryButtonCommand = SaveOpenAIKeyCommand;

            AdvancedPaste_EnableAIDialogOpenAIApiKey.Text = string.Empty;

            await ShowEnableDialogAsync();
        }

        private async Task ShowEnableDialogAsync()
        {
            await EnableAIDialog.ShowAsync();
        }

        private void AdvancedPaste_DisableAIButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.DisableAI();
        }

        private void AdvancedPaste_EnableAIDialogOpenAIApiKey_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (AdvancedPaste_EnableAIDialogOpenAIApiKey.Text.Length > 0)
            {
                EnableAIDialog.IsPrimaryButtonEnabled = true;
            }
            else
            {
                EnableAIDialog.IsPrimaryButtonEnabled = false;
            }
        }

        private void ShortcutsListView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (ViewModel.IsListViewFocusRequested)
            {
                // Set focus to the last item in the ListView
                int size = ShortcutsListView.Items.Count;
                ((ListViewItem)ShortcutsListView.ContainerFromIndex(size - 1)).Focus(FocusState.Programmatic);

                // Reset the focus requested flag
                ViewModel.IsListViewFocusRequested = false;
            }
        }
    }
}
