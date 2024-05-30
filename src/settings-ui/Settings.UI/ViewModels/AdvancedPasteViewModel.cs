// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Timers;
using global::PowerToys.GPOWrapper;
using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.PowerToys.Settings.UI.Library.Helpers;
using Microsoft.PowerToys.Settings.UI.Library.Interfaces;
using Microsoft.Win32;
using Windows.Security.Credentials;

namespace Microsoft.PowerToys.Settings.UI.ViewModels
{
    public class AdvancedPasteViewModel : Observable, IDisposable
    {
        private bool disposedValue;

        // Delay saving of settings in order to avoid calling save multiple times and hitting file in use exception. If there is no other request to save settings in given interval, we proceed to save it, otherwise we schedule saving it after this interval
        private const int SaveSettingsDelayInMs = 500;

        private GeneralSettings GeneralSettingsConfig { get; set; }

        private readonly ISettingsUtils _settingsUtils;
        private readonly object _delayedActionLock = new object();

        private AdvancedPasteSettings AdvancedPasteSettings { get; set; }

        private Timer _delayedTimer;

        private const string ModuleName = AdvancedPasteSettings.ModuleName;

        private GpoRuleConfigured _enabledGpoRuleConfiguration;
        private bool _enabledStateIsGPOConfigured;
        private GpoRuleConfigured _onlineAIModelsGpoRuleConfiguration;
        private bool _onlineAIModelsDisallowedByGPO;
        private bool _isEnabled;
        private ObservableCollection<AdvancedPasteShortcut> _advancedPasteShortcuts = new ObservableCollection<AdvancedPasteShortcut>();

        private Func<string, int> SendConfigMSG { get; }

        public bool IsListViewFocusRequested { get; set; }

        public AdvancedPasteViewModel(
            ISettingsUtils settingsUtils,
            ISettingsRepository<GeneralSettings> settingsRepository,
            ISettingsRepository<AdvancedPasteSettings> advancedPasteSettingsRepository,
            Func<string, int> ipcMSGCallBackFunc)
        {
            // To obtain the general settings configurations of PowerToys Settings.
            ArgumentNullException.ThrowIfNull(settingsRepository);

            GeneralSettingsConfig = settingsRepository.SettingsConfig;

            // To obtain the settings configurations of Fancy zones.
            ArgumentNullException.ThrowIfNull(settingsRepository);

            _settingsUtils = settingsUtils ?? throw new ArgumentNullException(nameof(settingsUtils));

            ArgumentNullException.ThrowIfNull(advancedPasteSettingsRepository);

            AdvancedPasteSettings = advancedPasteSettingsRepository.SettingsConfig;

            InitializeEnabledValue();

            // set the callback functions value to handle outgoing IPC message.
            SendConfigMSG = ipcMSGCallBackFunc;

            _delayedTimer = new Timer();
            _delayedTimer.Interval = SaveSettingsDelayInMs;
            _delayedTimer.Elapsed += DelayedTimer_Tick;
            _delayedTimer.AutoReset = false;
        }

        public ObservableCollection<AdvancedPasteShortcut> Shortcuts
        {
            get
            {
                return _advancedPasteShortcuts;
            }

            set
            {
                SaveAdvancedPasteShortcuts(value);
                _advancedPasteShortcuts = value;
                OnPropertyChanged(nameof(Shortcuts));
            }
        }

        public void DeleteAdvancedPasteShortcut(int id)
        {
            AdvancedPasteShortcut shortcut = _advancedPasteShortcuts.First(x => x.Id == id);
            ObservableCollection<AdvancedPasteShortcut> shortcuts = Shortcuts;
            shortcuts.Remove(shortcut);

            _advancedPasteShortcuts = shortcuts;
            SaveAdvancedPasteShortcuts(shortcuts);
        }

        private void InitializeEnabledValue()
        {
            _enabledGpoRuleConfiguration = GPOWrapper.GetConfiguredAdvancedPasteEnabledValue();
            if (_enabledGpoRuleConfiguration == GpoRuleConfigured.Disabled || _enabledGpoRuleConfiguration == GpoRuleConfigured.Enabled)
            {
                // Get the enabled state from GPO.
                _enabledStateIsGPOConfigured = true;
                _isEnabled = _enabledGpoRuleConfiguration == GpoRuleConfigured.Enabled;
            }
            else
            {
                _isEnabled = GeneralSettingsConfig.Enabled.AdvancedPaste;
            }

            _onlineAIModelsGpoRuleConfiguration = GPOWrapper.GetAllowedAdvancedPasteOnlineAIModelsValue();
            if (_onlineAIModelsGpoRuleConfiguration == GpoRuleConfigured.Disabled)
            {
                _onlineAIModelsDisallowedByGPO = true;

                // disable AI if it was enabled
                DisableAI();
            }
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_enabledStateIsGPOConfigured)
                {
                    // If it's GPO configured, shouldn't be able to change this state.
                    return;
                }

                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    OnPropertyChanged(nameof(IsEnabled));

                    // Set the status of AdvancedPaste in the general settings
                    GeneralSettingsConfig.Enabled.AdvancedPaste = value;
                    var outgoing = new OutGoingGeneralSettings(GeneralSettingsConfig);

                    SendConfigMSG(outgoing.ToString());
                }
            }
        }

        public void SaveAdvancedPasteShortcuts(ObservableCollection<AdvancedPasteShortcut> shortcuts)
        {
            _settingsUtils.SaveSettings(AdvancedPasteSettings.Properties.Shortcuts.ToJsonString(), ModuleName, "paste.json");
            AdvancedPasteSettings.Properties.Shortcuts = new AdvancedPasteShortcuts(shortcuts);
            _settingsUtils.SaveSettings(AdvancedPasteSettings.ToJsonString(), ModuleName);
        }

        private bool OpenAIKeyExists()
        {
            PasswordVault vault = new PasswordVault();
            PasswordCredential cred = null;

            try
            {
                cred = vault.Retrieve("https://platform.openai.com/api-keys", "PowerToys_AdvancedPaste_OpenAIKey");
            }
            catch (Exception)
            {
                return false;
            }

            return cred is not null;
        }

        public bool IsOpenAIEnabled => OpenAIKeyExists() && !IsOnlineAIModelsDisallowedByGPO;

        public bool IsEnabledGpoConfigured
        {
            get => _enabledStateIsGPOConfigured;
        }

        public bool IsOnlineAIModelsDisallowedByGPO
        {
            get => _onlineAIModelsDisallowedByGPO || _enabledGpoRuleConfiguration == GpoRuleConfigured.Disabled;
        }

        public bool ShowOnlineAIModelsGpoConfiguredInfoBar
        {
            get => _onlineAIModelsDisallowedByGPO && _enabledGpoRuleConfiguration != GpoRuleConfigured.Disabled;
        }

        public void AddRow(string shortcutNamePrefix)
        {
            /// This is a fallback validation to eliminate the warning "CA1062:Validate arguments of public methods" when using the parameter (variable) "sizeNamePrefix" in the code.
            /// If the parameter is unexpectedly empty or null, we fill the parameter with a non-localized string.
            /// Normally the parameter "sizeNamePrefix" can't be null or empty because it is filled with a localized string when we call this method from <see cref="UI.Views.ImageResizerPage.AddSizeButton_Click"/>.
            shortcutNamePrefix = string.IsNullOrEmpty(shortcutNamePrefix) ? "New Shortcut" : shortcutNamePrefix;

            ObservableCollection<AdvancedPasteShortcut> shortcuts = Shortcuts;
            int maxId = shortcuts.Count > 0 ? shortcuts.OrderBy(x => x.Id).Last().Id : -1;
            string shortcutName = GenerateNameForNewShortcut(shortcuts, shortcutNamePrefix);

            AdvancedPasteShortcut newShortcut = new AdvancedPasteShortcut(maxId + 1, shortcutName, string.Empty, string.Empty);
            newShortcut.PropertyChanged += ShortcutPropertyChanged;
            shortcuts.Add(newShortcut);
            _advancedPasteShortcuts = shortcuts;
            SaveAdvancedPasteShortcuts(shortcuts);

            // Set the focus requested flag to indicate that an add operation has occurred during the ContainerContentChanging event
            IsListViewFocusRequested = true;
        }

        public void ShortcutPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            AdvancedPasteShortcut modifiedShortcut = (AdvancedPasteShortcut)sender;
            ObservableCollection<AdvancedPasteShortcut> shortcuts = Shortcuts;
            shortcuts.First(x => x.Id == modifiedShortcut.Id).Update(modifiedShortcut);
            _advancedPasteShortcuts = shortcuts;
            SaveAdvancedPasteShortcuts(shortcuts);
        }

        private bool IsClipboardHistoryEnabled()
        {
            string registryKey = @"HKEY_CURRENT_USER\Software\Microsoft\Clipboard\";
            try
            {
                int enableClipboardHistory = (int)Registry.GetValue(registryKey, "EnableClipboardHistory", false);
                return enableClipboardHistory != 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string GenerateNameForNewShortcut(in ObservableCollection<AdvancedPasteShortcut> shortcutsList, in string namePrefix)
        {
            int newShortcutCounter = 0;

            foreach (AdvancedPasteShortcut shortcut in shortcutsList)
            {
                string name = shortcut.Name;

                if (name.StartsWith(namePrefix, StringComparison.InvariantCulture))
                {
                    if (int.TryParse(name.AsSpan(namePrefix.Length), out int number))
                    {
                        if (newShortcutCounter < number)
                        {
                            newShortcutCounter = number;
                        }
                    }
                }
            }

            return $"{namePrefix} {++newShortcutCounter}";
        }

        private bool IsClipboardHistoryDisabledByGPO()
        {
            string registryKey = @"HKEY_LOCAL_MACHINE\Software\Policies\Microsoft\Windows\System\";
            try
            {
                object allowClipboardHistory = Registry.GetValue(registryKey, "AllowClipboardHistory", null);
                if (allowClipboardHistory != null)
                {
                    return (int)allowClipboardHistory == 0;
                }
                else
                {
                    return false;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void SetClipboardHistoryEnabled(bool value)
        {
            string registryKey = @"HKEY_CURRENT_USER\Software\Microsoft\Clipboard\";
            try
            {
                Registry.SetValue(registryKey, "EnableClipboardHistory", value ? 1 : 0);
            }
            catch (Exception)
            {
            }
        }

        public bool ClipboardHistoryEnabled
        {
            get => IsClipboardHistoryEnabled();
            set
            {
                if (IsClipboardHistoryEnabled() != value)
                {
                    SetClipboardHistoryEnabled(value);
                }
            }
        }

        public bool ClipboardHistoryDisabledByGPO
        {
            get => IsClipboardHistoryDisabledByGPO();
        }

        public HotkeySettings AdvancedPasteUIShortcut
        {
            get => AdvancedPasteSettings.Properties.AdvancedPasteUIShortcut;
            set
            {
                if (AdvancedPasteSettings.Properties.AdvancedPasteUIShortcut != value)
                {
                    AdvancedPasteSettings.Properties.AdvancedPasteUIShortcut = value ?? AdvancedPasteProperties.DefaultAdvancedPasteUIShortcut;
                    OnPropertyChanged(nameof(AdvancedPasteUIShortcut));
                    OnPropertyChanged(nameof(IsConflictingCopyShortcut));

                    _settingsUtils.SaveSettings(AdvancedPasteSettings.ToJsonString(), AdvancedPasteSettings.ModuleName);
                    NotifySettingsChanged();
                }
            }
        }

        public HotkeySettings PasteAsPlainTextShortcut
        {
            get => AdvancedPasteSettings.Properties.PasteAsPlainTextShortcut;
            set
            {
                if (AdvancedPasteSettings.Properties.PasteAsPlainTextShortcut != value)
                {
                    AdvancedPasteSettings.Properties.PasteAsPlainTextShortcut = value ?? AdvancedPasteProperties.DefaultPasteAsPlainTextShortcut;
                    OnPropertyChanged(nameof(PasteAsPlainTextShortcut));
                    OnPropertyChanged(nameof(IsConflictingCopyShortcut));

                    _settingsUtils.SaveSettings(AdvancedPasteSettings.ToJsonString(), AdvancedPasteSettings.ModuleName);
                    NotifySettingsChanged();
                }
            }
        }

        public HotkeySettings PasteAsMarkdownShortcut
        {
            get => AdvancedPasteSettings.Properties.PasteAsMarkdownShortcut;
            set
            {
                if (AdvancedPasteSettings.Properties.PasteAsMarkdownShortcut != value)
                {
                    AdvancedPasteSettings.Properties.PasteAsMarkdownShortcut = value ?? new HotkeySettings();
                    OnPropertyChanged(nameof(PasteAsMarkdownShortcut));
                    OnPropertyChanged(nameof(IsConflictingCopyShortcut));

                    _settingsUtils.SaveSettings(AdvancedPasteSettings.ToJsonString(), AdvancedPasteSettings.ModuleName);
                    NotifySettingsChanged();
                }
            }
        }

        public HotkeySettings PasteAsJsonShortcut
        {
            get => AdvancedPasteSettings.Properties.PasteAsJsonShortcut;
            set
            {
                if (AdvancedPasteSettings.Properties.PasteAsJsonShortcut != value)
                {
                    AdvancedPasteSettings.Properties.PasteAsJsonShortcut = value ?? new HotkeySettings();
                    OnPropertyChanged(nameof(PasteAsJsonShortcut));
                    OnPropertyChanged(nameof(IsConflictingCopyShortcut));

                    _settingsUtils.SaveSettings(AdvancedPasteSettings.ToJsonString(), AdvancedPasteSettings.ModuleName);
                    NotifySettingsChanged();
                }
            }
        }

        public bool ShowCustomPreview
        {
            get => AdvancedPasteSettings.Properties.ShowCustomPreview;
            set
            {
                if (value != AdvancedPasteSettings.Properties.ShowCustomPreview)
                {
                    AdvancedPasteSettings.Properties.ShowCustomPreview = value;
                    NotifySettingsChanged();
                }
            }
        }

        public bool IsConflictingCopyShortcut
        {
            get
            {
                return PasteAsPlainTextShortcut.ToString() == "Ctrl + V" || PasteAsPlainTextShortcut.ToString() == "Ctrl + Shift + V" ||
                    AdvancedPasteUIShortcut.ToString() == "Ctrl + V" || AdvancedPasteUIShortcut.ToString() == "Ctrl + Shift + V" ||
                    PasteAsMarkdownShortcut.ToString() == "Ctrl + V" || PasteAsMarkdownShortcut.ToString() == "Ctrl + Shift + V" ||
                    PasteAsJsonShortcut.ToString() == "Ctrl + V" || PasteAsJsonShortcut.ToString() == "Ctrl + Shift + V";
            }
        }

        private void DelayedTimer_Tick(object sender, EventArgs e)
        {
            lock (_delayedActionLock)
            {
                _delayedTimer.Stop();
                NotifySettingsChanged();
            }
        }

        private void NotifySettingsChanged()
        {
            // Using InvariantCulture as this is an IPC message
            SendConfigMSG(
                   string.Format(
                       CultureInfo.InvariantCulture,
                       "{{ \"powertoys\": {{ \"{0}\": {1} }} }}",
                       AdvancedPasteSettings.ModuleName,
                       JsonSerializer.Serialize(AdvancedPasteSettings)));
        }

        public void RefreshEnabledState()
        {
            InitializeEnabledValue();
            OnPropertyChanged(nameof(IsEnabled));
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    _delayedTimer.Dispose();
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        internal void DisableAI()
        {
            try
            {
                PasswordVault vault = new PasswordVault();
                PasswordCredential cred = vault.Retrieve("https://platform.openai.com/api-keys", "PowerToys_AdvancedPaste_OpenAIKey");
                vault.Remove(cred);
                OnPropertyChanged(nameof(IsOpenAIEnabled));
            }
            catch (Exception)
            {
            }
        }

        internal void EnableAI(string password)
        {
            try
            {
                PasswordVault vault = new PasswordVault();
                PasswordCredential cred = new PasswordCredential("https://platform.openai.com/api-keys", "PowerToys_AdvancedPaste_OpenAIKey", password);
                vault.Add(cred);
                OnPropertyChanged(nameof(IsOpenAIEnabled));
            }
            catch (Exception)
            {
            }
        }
    }
}
