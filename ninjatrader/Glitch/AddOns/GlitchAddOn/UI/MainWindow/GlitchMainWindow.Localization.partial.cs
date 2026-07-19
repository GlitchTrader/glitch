using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Glitch.Services;

namespace Glitch.UI
{
    public partial class GlitchMainWindow
    {
        private readonly List<Action> _localizedUiBindings = new List<Action>();
        private GlitchLocalizationService _localizationService;
        private Border _languageSwitcherBorder;
        private ComboBox _languageSwitcherCombo;
        private bool _isApplyingUiLanguage;
        private bool _isGlobalNewsLockoutActive;
        private string _globalNewsLockoutRawText;

        private void EnsureLocalizationInitialized()
        {
            if (_localizationService != null)
                return;

            _localizationService = new GlitchLocalizationService(
                GlitchLocalizationService.GetDefaultLocalizationPath(),
                GlitchLocalizationService.GetDefaultSettingsPath());
        }

        private string L(string key, string fallback)
        {
            EnsureLocalizationInitialized();
            return _localizationService.Translate(key, fallback);
        }

        private string Lf(string key, string fallback, params object[] args)
        {
            string format = L(key, fallback);
            if (args == null || args.Length == 0)
                return format;

            try
            {
                return string.Format(CultureInfo.CurrentCulture, format, args);
            }
            catch
            {
                return format;
            }
        }

        private void RegisterLocalizationBinding(Action action)
        {
            if (action == null)
                return;

            _localizedUiBindings.Add(action);
            action();
        }

        private void BindLocalizedText(TextBlock textBlock, string key, string fallback)
        {
            if (textBlock == null)
                return;

            RegisterLocalizationBinding(() => textBlock.Text = L(key, fallback));
        }

        private void BindLocalizedContent(ContentControl control, string key, string fallback)
        {
            if (control == null)
                return;

            RegisterLocalizationBinding(() => control.Content = L(key, fallback));
        }

        private void BindLocalizedHeader(HeaderedContentControl control, string key, string fallback)
        {
            if (control == null)
                return;

            RegisterLocalizationBinding(() => control.Header = L(key, fallback));
        }

        private void BindLocalizedColumnHeader(DataGridColumn column, string key, string fallback)
        {
            if (column == null)
                return;

            RegisterLocalizationBinding(() => column.Header = L(key, fallback));
        }

        private void ApplyLocalization()
        {
            EnsureLocalizationInitialized();

            foreach (Action binding in _localizedUiBindings.ToList())
            {
                try
                {
                    binding?.Invoke();
                }
                catch
                {
                }
            }

            FrameworkElement styleContext = _headerRootGrid != null
                ? (FrameworkElement)_headerRootGrid
                : (FrameworkElement)this;

            if (_replicateButton != null)
                _replicateButton.Style = CreateReplicateButtonStyle(styleContext);
            if (_aiTradingButton != null)
                _aiTradingButton.Style = CreateAiTradingButtonStyle(styleContext);
            if (_flattenAllButton != null)
            {
                _flattenAllButton.Style = CreateFlattenButtonStyle(styleContext);
                if (!_isFlattenFeedbackActive)
                    _flattenAllButton.Content = L("header.button.flatten_all", "Flatten All");
            }
            UpdateReplicateButtonState();
            UpdateGlobalNewsLockoutBanner(_isGlobalNewsLockoutActive, _globalNewsLockoutRawText);
        }

        private UIElement CreateLanguageSwitcher(FrameworkElement context)
        {
            EnsureLocalizationInitialized();

            _languageSwitcherBorder = new Border
            {
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 2, 2),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent
            };

            var stack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            _languageSwitcherCombo = new ComboBox
            {
                MinWidth = 72,
                Height = 22,
                IsEditable = false,
                ItemsSource = _localizationService.SupportedLanguages,
                SelectedValuePath = nameof(GlitchLocalizationService.LanguageOption.Code),
                HorizontalContentAlignment = HorizontalAlignment.Left
            };
            _languageSwitcherCombo.ItemTemplate = CreateLanguageOptionDisplayTemplate(useCompactName: false);
            _languageSwitcherCombo.Style = CreateGroupMasterComboBoxStyle(context);
            _languageSwitcherCombo.BorderThickness = new Thickness(0);
            _languageSwitcherCombo.BorderBrush = Brushes.Transparent;
            _languageSwitcherCombo.Background = Brushes.Transparent;
            _languageSwitcherCombo.Padding = new Thickness(0);
            _languageSwitcherCombo.SelectionChanged += OnLanguageSwitcherSelectionChanged;
            _languageSwitcherCombo.DropDownOpened += OnLanguageSwitcherDropDownOpened;
            _languageSwitcherCombo.DropDownClosed += OnLanguageSwitcherDropDownClosed;
            stack.Children.Add(_languageSwitcherCombo);

            _languageSwitcherBorder.Child = stack;

            _isApplyingUiLanguage = true;
            _languageSwitcherCombo.SelectedValue = _localizationService.CurrentLanguageCode;
            _isApplyingUiLanguage = false;
            ApplyLanguageSwitcherDisplayMode(expanded: false);

            return _languageSwitcherBorder;
        }

        private void OnLanguageSwitcherSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isApplyingUiLanguage || _localizationService == null || _languageSwitcherCombo == null)
                return;

            string selectedCode = _languageSwitcherCombo.SelectedValue as string;
            if (string.IsNullOrWhiteSpace(selectedCode))
                return;

            if (selectedCode.Equals(_localizationService.CurrentLanguageCode, StringComparison.OrdinalIgnoreCase))
                return;

            _localizationService.SetLanguage(selectedCode, persist: true);
            ApplyLocalization();
            ApplyLanguageSwitcherDisplayMode(expanded: false);
        }

        private void OnLanguageSwitcherDropDownOpened(object sender, EventArgs e)
        {
            ApplyLanguageSwitcherDisplayMode(expanded: true);
        }

        private void OnLanguageSwitcherDropDownClosed(object sender, EventArgs e)
        {
            ApplyLanguageSwitcherDisplayMode(expanded: false);
        }

        private static DataTemplate CreateLanguageOptionDisplayTemplate(bool useCompactName)
        {
            var template = new DataTemplate(typeof(GlitchLocalizationService.LanguageOption));
            var textBlock = new FrameworkElementFactory(typeof(TextBlock));
            textBlock.SetBinding(TextBlock.TextProperty, new Binding(useCompactName ? "CompactName" : "DisplayName"));
            textBlock.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            template.VisualTree = textBlock;
            return template;
        }

        private void ApplyLanguageSwitcherDisplayMode(bool expanded)
        {
            if (_languageSwitcherCombo == null)
                return;

            string selectedCode = _languageSwitcherCombo.SelectedValue as string;
            _languageSwitcherCombo.MinWidth = expanded ? 128 : 72;
            _languageSwitcherCombo.ItemTemplate = CreateLanguageOptionDisplayTemplate(useCompactName: !expanded);

            if (!string.IsNullOrWhiteSpace(selectedCode))
                _languageSwitcherCombo.SelectedValue = selectedCode;
        }

        private void CacheGlobalNewsLockoutState(bool isActive, string rawText)
        {
            _isGlobalNewsLockoutActive = isActive;
            _globalNewsLockoutRawText = rawText;
        }

        private string BuildLocalizedNewsLockoutBannerText(string lockoutText)
        {
            if (string.IsNullOrWhiteSpace(lockoutText))
                return L("header.banner.news_event_in_progress", "News Event in Progress");

            string value = lockoutText.Trim();
            const string englishPrefix = "News Event in Progress:";
            if (value.StartsWith(englishPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string localizedPrefix = L("header.banner.news_event_in_progress_prefix", "News Event in Progress:");
                return localizedPrefix + value.Substring(englishPrefix.Length);
            }

            const string englishPlain = "News Event in Progress";
            if (value.StartsWith(englishPlain, StringComparison.OrdinalIgnoreCase))
            {
                string localizedPlain = L("header.banner.news_event_in_progress", "News Event in Progress");
                return localizedPlain + value.Substring(englishPlain.Length);
            }

            return value;
        }
    }
}
