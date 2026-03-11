using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Glitch.Services;

namespace Glitch.UI
{
    public partial class GlitchMainWindow
    {
        private Grid _settingsRootGrid;
        private CheckBox _settingsBufferFreeze15CheckBox;
        private CheckBox _settingsBufferOneContract20CheckBox;
        private CheckBox _settingsUnrealizedFlatten80CheckBox;
        private CheckBox _settingsEvalProfitTargetLockCheckBox;
        private TextBox _settingsLicenseKeyTextBox;
        private Border _settingsPlanBadgeBorder;
        private TextBlock _settingsPlanBadgeText;
        private Button _settingsSaveButton;
        private Button _settingsValidateButton;
        private bool _settingsSaveFeedbackActive;
        private bool _settingsValidateFeedbackActive;
        private bool _settingsLicensePlaceholderActive;

        private UIElement CreateSettingsTabImpl()
        {
            var root = new Grid { Margin = new Thickness(20) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _settingsRootGrid = root;

            var title = new TextBlock
            {
                Text = L("settings.title", "Settings"),
                FontWeight = FontWeights.Medium,
                Margin = new Thickness(0, 0, 0, 10)
            };
            BindLocalizedText(title, "settings.title", "Settings");
            ApplySkinResource(title, TextBlock.ForegroundProperty, "FontHeaderLevel4Brush", "FontControlBrush", "FontTableBrush");
            Grid.SetRow(title, 0);
            root.Children.Add(title);

            var compliancePanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(12)
            };
            compliancePanel.Children.Add(BuildSectionHeader("settings.risk.title", "Risk Management Rules"));

            _settingsBufferFreeze15CheckBox = BuildPolicyCheckBox(
                "settings.risk.enforce_15_flatten_freeze",
                "Flatten and freeze account when buffer falls below 15% of maximum drawdown.");
            _settingsBufferOneContract20CheckBox = BuildPolicyCheckBox(
                "settings.risk.enforce_20_one_contract",
                "Force one-contract replication when buffer falls below 20% of maximum drawdown (release at 25%).");
            _settingsUnrealizedFlatten80CheckBox = BuildPolicyCheckBox(
                "settings.risk.enforce_80_flatten",
                "Flatten account when unrealized loss exceeds 80% of maximum intratrade loss (max loss limit).");
            _settingsEvalProfitTargetLockCheckBox = BuildPolicyCheckBox(
                "settings.risk.enforce_eval_lock_flatten",
                "Flatten and lock evaluation account when equity reaches the evaluation target lock balance.");

            compliancePanel.Children.Add(BuildPolicyToggleRow(_settingsBufferFreeze15CheckBox));
            compliancePanel.Children.Add(BuildPolicyToggleRow(_settingsBufferOneContract20CheckBox));
            compliancePanel.Children.Add(BuildPolicyToggleRow(_settingsUnrealizedFlatten80CheckBox));
            compliancePanel.Children.Add(BuildPolicyToggleRow(_settingsEvalProfitTargetLockCheckBox));

            var complianceCard = CreateSettingsCard();
            complianceCard.Child = compliancePanel;
            Grid.SetRow(complianceCard, 1);
            root.Children.Add(complianceCard);

            var licensingPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(12)
            };
            licensingPanel.Children.Add(BuildSectionHeader("settings.license.title", "License"));

            _settingsLicenseKeyTextBox = BuildSettingsTextBox("settings.license.key_placeholder", "Enter license key");
            _settingsLicenseKeyTextBox.GotFocus += OnSettingsLicenseKeyTextBoxGotFocus;
            _settingsLicenseKeyTextBox.LostFocus += OnSettingsLicenseKeyTextBoxLostFocus;

            _settingsPlanBadgeText = new TextBlock
            {
                Margin = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold
            };
            ApplySkinResource(_settingsPlanBadgeText, TextBlock.ForegroundProperty, "FontHeaderLevel4Brush", "FontControlBrush", "FontTableBrush");
            _settingsPlanBadgeBorder = new Border
            {
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 8, 0, 0),
                BorderThickness = new Thickness(1),
                Child = _settingsPlanBadgeText
            };
            ApplySkinResource(_settingsPlanBadgeBorder, Border.BackgroundProperty, "BackgroundMainWindow", "GridEntireBackground", "BackgroundTextInput");
            ApplySkinResource(_settingsPlanBadgeBorder, Border.BorderBrushProperty, "BorderThinBrush", "TabControlBorderBrush");

            licensingPanel.Children.Add(BuildSettingsLabel("settings.license.key", "License Key"));
            licensingPanel.Children.Add(_settingsLicenseKeyTextBox);
            licensingPanel.Children.Add(_settingsPlanBadgeBorder);

            var licensingCard = CreateSettingsCard();
            licensingCard.Child = licensingPanel;
            Grid.SetRow(licensingCard, 2);
            root.Children.Add(licensingCard);

            var actionRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 4, 0, 0)
            };

            _settingsSaveButton = new Button
            {
                Content = L("settings.button.save", "Save Settings"),
                Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(10, 3, 10, 3),
                MinHeight = 28,
                MinWidth = 118,
                VerticalAlignment = VerticalAlignment.Top,
                Style = CreateSettingsActionButtonStyle(root)
            };
            _settingsSaveButton.Click += OnSettingsSaveClick;
            actionRow.Children.Add(_settingsSaveButton);

            _settingsValidateButton = new Button
            {
                Content = L("settings.button.validate", "Validate License Now"),
                Padding = new Thickness(10, 3, 10, 3),
                MinHeight = 28,
                MinWidth = 142,
                VerticalAlignment = VerticalAlignment.Top,
                Style = CreateSettingsActionButtonStyle(root)
            };
            _settingsValidateButton.Click += OnValidateLicenseNowClick;
            actionRow.Children.Add(_settingsValidateButton);

            Grid.SetRow(actionRow, 3);
            root.Children.Add(actionRow);

            UpdateSettingsControlsFromRuntimePolicy();
            UpdateSettingsTabLicenseStatusText();
            return root;
        }

        private TextBlock BuildSectionHeader(string key, string fallback)
        {
            var header = new TextBlock
            {
                Text = L(key, fallback),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            };
            ApplySkinResource(header, TextBlock.ForegroundProperty, "FontHeaderLevel4Brush", "FontControlBrush", "FontTableBrush");
            return header;
        }

        private TextBlock BuildSettingsLabel(string key, string fallback)
        {
            var label = new TextBlock
            {
                Text = L(key, fallback),
                Margin = new Thickness(0, 6, 0, 2)
            };
            ApplySkinResource(label, TextBlock.ForegroundProperty, "FontControlBrush", "FontTableBrush");
            return label;
        }

        private Border CreateSettingsCard()
        {
            var border = new Border
            {
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 10)
            };
            ApplySkinResource(border, Border.BackgroundProperty, "BackgroundTextInput", "BackgroundMainWindow", "GridEntireBackground");
            ApplySkinResource(border, Border.BorderBrushProperty, "BorderThinBrush", "TabControlBorderBrush");
            return border;
        }

        private Border BuildPolicyToggleRow(CheckBox checkBox)
        {
            var row = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(0, 2, 0, 2),
                Padding = new Thickness(8, 4, 8, 4)
            };
            ApplySkinResource(row, Border.BackgroundProperty, "BackgroundMainWindow", "GridEntireBackground", "BackgroundTextInput");
            ApplySkinResource(row, Border.BorderBrushProperty, "BorderThinBrush", "TabControlBorderBrush");
            row.Child = checkBox;
            return row;
        }

        private Style CreateSettingsActionButtonStyle(FrameworkElement context)
        {
            var baseStyle = FindSkinStyle(context, typeof(Button));
            var style = new Style(typeof(Button), baseStyle);

            ApplySkinSetter(style, Control.BackgroundProperty, context, "BackgroundMainWindow", "GridEntireBackground", "BackgroundTextInput");
            ApplySkinSetter(style, Control.ForegroundProperty, context, "FontControlBrush", "FontTableBrush");
            ApplySkinSetter(style, Control.BorderBrushProperty, context, "BorderThinBrush", "TabControlBorderBrush");
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
            style.Setters.Add(new Setter(Control.CursorProperty, System.Windows.Input.Cursors.Hand));
            style.Setters.Add(new Setter(Control.TemplateProperty, CreateActionButtonTemplate()));
            double? sharedFontSize = FindSkinDouble(context, "FontTableHeight", "FontControlHeight", "FontHeaderLevel4Height");
            if (sharedFontSize.HasValue)
                style.Setters.Add(new Setter(Control.FontSizeProperty, sharedFontSize.Value));
            ApplyTealAccentResourceOverrides(style);

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, TealAccentBrush));
            hoverTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, TealAccentBrush));
            hoverTrigger.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            style.Triggers.Add(hoverTrigger);

            var pressedTrigger = new Trigger { Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Value = true };
            pressedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, TealAccentBrush));
            pressedTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, TealAccentBrush));
            pressedTrigger.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            style.Triggers.Add(pressedTrigger);

            var keyboardFocusTrigger = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
            keyboardFocusTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, TealAccentBrush));
            style.Triggers.Add(keyboardFocusTrigger);

            return style;
        }

        private Style CreateSettingsPolicyCheckBoxStyle(FrameworkElement context)
        {
            var style = new Style(typeof(CheckBox));

            ApplySkinSetter(style, Control.BorderBrushProperty, context, "BorderThinBrush", "TabControlBorderBrush", "GridHeaderHighlight");
            ApplySkinSetter(style, Control.ForegroundProperty, context, "FontControlBrush", "FontTableBrush", "GridRowForeground");
            ApplySkinSetter(style, Control.BackgroundProperty, context, "BackgroundTextInput", "BackgroundMainWindow", "GridEntireBackground");
            style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));

            var template = new ControlTemplate(typeof(CheckBox));

            var rootFactory = new FrameworkElementFactory(typeof(StackPanel));
            rootFactory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            rootFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.Name = "CheckBoxBorder";
            borderFactory.SetValue(FrameworkElement.WidthProperty, 12.0);
            borderFactory.SetValue(FrameworkElement.HeightProperty, 12.0);
            borderFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
            borderFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFactory.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            borderFactory.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            borderFactory.SetValue(Border.SnapsToDevicePixelsProperty, true);

            var markFactory = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
            markFactory.Name = "CheckMark";
            markFactory.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse("M 2,7 L 5,10 L 11,3"));
            markFactory.SetValue(System.Windows.Shapes.Path.StrokeThicknessProperty, 1.8);
            markFactory.SetValue(System.Windows.Shapes.Path.StrokeProperty, Brushes.White);
            markFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            markFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            markFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(0, -1, 0, 0));
            markFactory.SetValue(UIElement.RenderTransformOriginProperty, new Point(0.5, 0.5));
            markFactory.SetValue(UIElement.RenderTransformProperty, new ScaleTransform(7.0 / 9.0, 7.0 / 9.0));
            markFactory.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            markFactory.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
            borderFactory.AppendChild(markFactory);
            rootFactory.AppendChild(borderFactory);

            var contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            contentFactory.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
            contentFactory.SetValue(ContentPresenter.ContentTemplateProperty, new TemplateBindingExtension(ContentControl.ContentTemplateProperty));
            contentFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            contentFactory.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
            rootFactory.AppendChild(contentFactory);

            template.VisualTree = rootFactory;

            var checkedTrigger = new Trigger { Property = System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, Value = true };
            checkedTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "CheckMark"));
            checkedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, TealAccentBrush, "CheckBoxBorder"));
            checkedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, TealAccentBrush, "CheckBoxBorder"));
            template.Triggers.Add(checkedTrigger);

            string hoverBorderKey = FindSkinResourceKey(context, "GridHeaderHighlight", "BorderThinBrush", "TabControlBorderBrush");
            if (!string.IsNullOrWhiteSpace(hoverBorderKey))
            {
                var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
                hoverTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension(hoverBorderKey)));
                template.Triggers.Add(hoverTrigger);
            }

            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            return style;
        }

        private CheckBox BuildPolicyCheckBox(string key, string fallback)
        {
            var checkBox = new CheckBox
            {
                Content = L(key, fallback),
                Margin = new Thickness(0),
                FontWeight = FontWeights.Medium,
                Style = CreateSettingsPolicyCheckBoxStyle(_settingsRootGrid)
            };
            ApplySkinResource(checkBox, Control.ForegroundProperty, "FontControlBrush", "FontTableBrush");
            return checkBox;
        }

        private TextBox BuildSettingsTextBox(string key, string fallback)
        {
            var textBox = new TextBox
            {
                Text = string.Empty,
                Margin = new Thickness(0, 0, 0, 2),
                MinWidth = 420,
                Padding = new Thickness(8, 5, 8, 5),
                BorderThickness = new Thickness(1),
                ToolTip = L(key, fallback),
                Style = CreateSettingsLicenseTextBoxStyle(_settingsRootGrid)
            };
            textBox.FocusVisualStyle = null;
            return textBox;
        }

        private Style CreateSettingsLicenseTextBoxStyle(FrameworkElement context)
        {
            var baseStyle = FindSkinStyle(context, typeof(TextBox));
            var style = new Style(typeof(TextBox), baseStyle);

            ApplySkinSetter(style, Control.ForegroundProperty, context, "FontControlBrush", "FontTableBrush");
            ApplySkinSetter(style, Control.BackgroundProperty, context, "BackgroundMainWindow", "GridEntireBackground", "BackgroundTextInput");
            ApplySkinSetter(style, Control.BorderBrushProperty, context, "BorderThinBrush", "TabControlBorderBrush");

            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 5, 8, 5)));
            style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
            style.Setters.Add(new Setter(Control.TemplateProperty, CreateSettingsLicenseTextBoxTemplate()));

            var focusTrigger = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
            focusTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, TealAccentBrush));
            style.Triggers.Add(focusTrigger);

            return style;
        }

        private static ControlTemplate CreateSettingsLicenseTextBoxTemplate()
        {
            var template = new ControlTemplate(typeof(TextBox));

            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.Name = "TextBoxBorder";
            borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            borderFactory.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            borderFactory.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
            borderFactory.SetValue(UIElement.SnapsToDevicePixelsProperty, true);

            var contentHostFactory = new FrameworkElementFactory(typeof(ScrollViewer));
            contentHostFactory.Name = "PART_ContentHost";
            contentHostFactory.SetValue(FrameworkElement.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
            contentHostFactory.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
            borderFactory.AppendChild(contentHostFactory);

            template.VisualTree = borderFactory;
            return template;
        }

        private async void OnSettingsSaveClick(object sender, RoutedEventArgs e)
        {
            if (_settingsSaveFeedbackActive)
                return;

            if (_runtimePolicySettings == null)
                _runtimePolicySettings = new GlitchRuntimePolicySettings();

            _runtimePolicySettings.EnforceAccountLevelCompliance = false;
            _runtimePolicySettings.EnforceBufferFreeze15Percent = _settingsBufferFreeze15CheckBox?.IsChecked == true;
            _runtimePolicySettings.EnforceBufferOneContract30Percent = _settingsBufferOneContract20CheckBox?.IsChecked == true;
            _runtimePolicySettings.EnforceUnrealizedFlatten70Percent = _settingsUnrealizedFlatten80CheckBox?.IsChecked == true;
            _runtimePolicySettings.EnforceEvalProfitTargetLock = _settingsEvalProfitTargetLockCheckBox?.IsChecked == true;
            _runtimePolicySettings.FlattenOnCriticalBufferLock = false;
            _runtimePolicySettings.LicenseKey = GetNormalizedSettingsLicenseKeyText();
            if (string.IsNullOrWhiteSpace(_runtimePolicySettings.LicenseApiBaseUrl))
                _runtimePolicySettings.LicenseApiBaseUrl = "https://api.glitchtrader.com";

            GlitchRuntimePolicyStore.SaveSettings(_runtimePolicyFilePath, _runtimePolicySettings);
            AppendJournal("System", "Policy", "Runtime settings updated by user.");
            AppendJournal("System", "Runtime", BuildRuntimePolicySummaryLogLine());

            if (!_runtimePolicySettings.EnforceBufferFreeze15Percent &&
                !_runtimePolicySettings.EnforceBufferOneContract30Percent &&
                !_runtimePolicySettings.EnforceUnrealizedFlatten70Percent &&
                !_runtimePolicySettings.EnforceEvalProfitTargetLock)
                ClearComplianceEnforcementRuntimeState();

            _nextLicenseHeartbeatUtc = DateTime.UtcNow;
            _ = RefreshLicenseStateAsync(useValidateEndpoint: true, force: true);
            _settingsSaveFeedbackActive = true;
            try
            {
                await ShowTransientTealButtonFeedbackAsync(_settingsSaveButton, L("settings.feedback.saving", "Saving!"), 3000);
            }
            finally
            {
                _settingsSaveFeedbackActive = false;
            }
        }

        private async void OnValidateLicenseNowClick(object sender, RoutedEventArgs e)
        {
            if (_settingsValidateFeedbackActive)
                return;

            _settingsValidateFeedbackActive = true;
            try
            {
                Task validateTask = RefreshLicenseStateAsync(useValidateEndpoint: true, force: true);
                await ShowTransientTealButtonFeedbackAsync(_settingsValidateButton, L("settings.feedback.validating", "Validating, please wait..."), 3000);
                await validateTask;
            }
            finally
            {
                _settingsValidateFeedbackActive = false;
            }
        }

        private static async Task ShowTransientTealButtonFeedbackAsync(Button button, string message, int milliseconds)
        {
            if (button == null)
                return;

            object originalContent = button.Content;
            bool wasEnabled = button.IsEnabled;

            button.IsEnabled = false;
            button.Content = message;
            button.Background = TealAccentBrush;
            button.BorderBrush = TealAccentBrush;
            button.Foreground = Brushes.White;

            try
            {
                await Task.Delay(Math.Max(250, milliseconds));
            }
            finally
            {
                button.Content = originalContent;
                button.ClearValue(Control.BackgroundProperty);
                button.ClearValue(Control.BorderBrushProperty);
                button.ClearValue(Control.ForegroundProperty);
                button.IsEnabled = wasEnabled;
            }
        }

        private void UpdateSettingsControlsFromRuntimePolicy()
        {
            if (_runtimePolicySettings == null)
                _runtimePolicySettings = new GlitchRuntimePolicySettings();

            if (_settingsBufferFreeze15CheckBox != null)
                _settingsBufferFreeze15CheckBox.IsChecked = _runtimePolicySettings.EnforceBufferFreeze15Percent;
            if (_settingsBufferOneContract20CheckBox != null)
                _settingsBufferOneContract20CheckBox.IsChecked = _runtimePolicySettings.EnforceBufferOneContract30Percent;
            if (_settingsUnrealizedFlatten80CheckBox != null)
                _settingsUnrealizedFlatten80CheckBox.IsChecked = _runtimePolicySettings.EnforceUnrealizedFlatten70Percent;
            if (_settingsEvalProfitTargetLockCheckBox != null)
                _settingsEvalProfitTargetLockCheckBox.IsChecked = _runtimePolicySettings.EnforceEvalProfitTargetLock;
            if (_settingsLicenseKeyTextBox != null)
            {
                string licenseKey = (_runtimePolicySettings.LicenseKey ?? string.Empty).Trim();
                _settingsLicensePlaceholderActive = false;
                _settingsLicenseKeyTextBox.Text = licenseKey;
                if (string.IsNullOrWhiteSpace(licenseKey))
                {
                    ApplySettingsLicensePlaceholder();
                }
                else
                {
                    ApplySettingsLicenseInputNormalVisual();
                }
            }
        }

        private void OnSettingsLicenseKeyTextBoxGotFocus(object sender, RoutedEventArgs e)
        {
            if (_settingsLicensePlaceholderActive)
                ClearSettingsLicensePlaceholder();
        }

        private void OnSettingsLicenseKeyTextBoxLostFocus(object sender, RoutedEventArgs e)
        {
            if (_settingsLicenseKeyTextBox == null)
                return;

            if (string.IsNullOrWhiteSpace(_settingsLicenseKeyTextBox.Text))
                ApplySettingsLicensePlaceholder();
        }

        private void ClearSettingsLicensePlaceholder()
        {
            if (_settingsLicenseKeyTextBox == null || !_settingsLicensePlaceholderActive)
                return;

            _settingsLicensePlaceholderActive = false;
            _settingsLicenseKeyTextBox.Text = string.Empty;
            ApplySettingsLicenseInputNormalVisual();
        }

        private void ApplySettingsLicensePlaceholder()
        {
            if (_settingsLicenseKeyTextBox == null)
                return;

            _settingsLicensePlaceholderActive = true;
            _settingsLicenseKeyTextBox.Text = L("settings.license.paste_placeholder", "Paste your license here");
            FrameworkElement skinContext = (FrameworkElement)_settingsRootGrid ?? _settingsLicenseKeyTextBox;
            Brush placeholderBrush = FindSkinBrush(skinContext, "FontTableBrush", "FontControlBrush") ?? Brushes.Gray;
            _settingsLicenseKeyTextBox.Foreground = placeholderBrush;
        }

        private void ApplySettingsLicenseInputNormalVisual()
        {
            if (_settingsLicenseKeyTextBox == null)
                return;

            FrameworkElement skinContext = (FrameworkElement)_settingsRootGrid ?? _settingsLicenseKeyTextBox;
            Brush normalBrush = FindSkinBrush(skinContext, "FontControlBrush", "FontTableBrush") ?? Brushes.White;
            _settingsLicenseKeyTextBox.Foreground = normalBrush;
        }

        private string GetNormalizedSettingsLicenseKeyText()
        {
            if (_settingsLicenseKeyTextBox == null)
                return string.Empty;

            if (_settingsLicensePlaceholderActive)
                return string.Empty;

            string value = (_settingsLicenseKeyTextBox.Text ?? string.Empty).Trim();
            if (value.Equals(L("settings.license.paste_placeholder", "Paste your license here"), StringComparison.Ordinal))
                return string.Empty;

            return value;
        }

        private void UpdateSettingsTabLicenseStatusText()
        {
            if (_settingsPlanBadgeText == null)
                return;

            GlitchLicenseCacheState cache = _licenseCacheState ?? new GlitchLicenseCacheState();
            string status = string.IsNullOrWhiteSpace(cache.LastStatus) ? "unknown" : cache.LastStatus.Trim().ToLowerInvariant();
            string plan = string.IsNullOrWhiteSpace(cache.Plan) ? "free_lite" : cache.Plan.Trim().ToLowerInvariant();
            DateTime validUntilUtc = cache.SignedTokenExpiresUtc != DateTime.MinValue
                ? cache.SignedTokenExpiresUtc
                : (cache.GraceUntilUtc != DateTime.MinValue ? cache.GraceUntilUtc : cache.LastSuccessUtc);
            string validUntilText = validUntilUtc == DateTime.MinValue
                ? "-"
                : validUntilUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            bool isPremiumPlan = plan.Equals("premium", StringComparison.OrdinalIgnoreCase);
            string statusLabel;
            if (status.Equals("active", StringComparison.OrdinalIgnoreCase))
                statusLabel = isPremiumPlan ? L("settings.license.status.active_premium", "Active Premium") : L("settings.license.status.free_lite", "Free Lite");
            else if (status.Equals("grace", StringComparison.OrdinalIgnoreCase))
                statusLabel = isPremiumPlan ? L("settings.license.status.grace_premium", "Grace Premium") : L("settings.license.status.grace_free_lite", "Grace Free Lite");
            else if (isPremiumPlan)
                statusLabel = L("settings.license.status.inactive_premium", "Inactive Premium");
            else
                statusLabel = L("settings.license.status.free_lite", "Free Lite");

            _settingsPlanBadgeText.Text = Lf("settings.license.status_format", "License Status: {0}, Valid until {1}", statusLabel, validUntilText);
            if (status.Equals("active", StringComparison.OrdinalIgnoreCase) && isPremiumPlan)
            {
                _settingsPlanBadgeText.Foreground = TealAccentBrush;
                _settingsPlanBadgeBorder.BorderBrush = TealAccentBrush;
            }
            else if (status.Equals("grace", StringComparison.OrdinalIgnoreCase) ||
                     status.Equals("expired", StringComparison.OrdinalIgnoreCase) ||
                     status.Equals("inactive", StringComparison.OrdinalIgnoreCase))
            {
                _settingsPlanBadgeText.Foreground = OrangeAccentBrush;
                _settingsPlanBadgeBorder.BorderBrush = OrangeAccentBrush;
            }
            else
            {
                ApplySkinResource(_settingsPlanBadgeText, TextBlock.ForegroundProperty, "FontControlBrush", "FontTableBrush");
                ApplySkinResource(_settingsPlanBadgeBorder, Border.BorderBrushProperty, "BorderThinBrush", "TabControlBorderBrush");
            }
        }
    }
}
