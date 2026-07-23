using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Glitch.Services;

namespace Glitch.UI
{
    public partial class GlitchMainWindow
    {
        private Grid _settingsRootGrid;
        private CheckBox _settingsBufferFreezeSimCheckBox;
        private CheckBox _settingsBufferFreezeEvalCheckBox;
        private CheckBox _settingsBufferFreezeApCheckBox;
        private TextBox _settingsBufferFreezeThresholdTextBox;
        private CheckBox _settingsOneContractSimCheckBox;
        private CheckBox _settingsOneContractEvalCheckBox;
        private CheckBox _settingsOneContractApCheckBox;
        private TextBox _settingsOneContractOnThresholdTextBox;
        private TextBox _settingsOneContractOffThresholdTextBox;
        private CheckBox _settingsUnrealizedFlattenSimCheckBox;
        private CheckBox _settingsUnrealizedFlattenEvalCheckBox;
        private CheckBox _settingsUnrealizedFlattenApCheckBox;
        private TextBox _settingsUnrealizedFlattenThresholdTextBox;
        private CheckBox _settingsEvalProfitTargetLockEvalCheckBox;
        private CheckBox _settingsMaxContractsFlattenSimCheckBox;
        private CheckBox _settingsMaxContractsFlattenEvalCheckBox;
        private CheckBox _settingsMaxContractsFlattenApCheckBox;
        private CheckBox _settingsNoProtectionFlattenSimCheckBox;
        private CheckBox _settingsNoProtectionFlattenEvalCheckBox;
        private CheckBox _settingsNoProtectionFlattenApCheckBox;
        private CheckBox _settingsAiDailyCloseCheckBox;
        private TextBox _settingsLicenseKeyTextBox;
        private Border _settingsPlanBadgeBorder;
        private TextBlock _settingsPlanBadgeText;
        private TextBlock _settingsUpdateBadgeText;
        private Button _settingsSaveButton;
        private bool _settingsSaveFeedbackActive;
        private bool _settingsLicensePlaceholderActive;
        private bool _settingsLicenseMaskedDisplayActive;
        private string _settingsLicenseKeyUnmaskedValue;
        private TextBlock _settingsCopyTradingPolicyNotice;

        private double ResolveSettingsBodyFontSize()
        {
            FrameworkElement skinContext = (FrameworkElement)_settingsRootGrid ?? _settingsPlanBadgeText;
            double? skinFontSize = FindSkinDouble(skinContext, "FontControlHeight", "FontTableHeight", "FontHeaderLevel4Height");
            return skinFontSize.HasValue && skinFontSize.Value > 0 ? skinFontSize.Value : 12d;
        }

        private double ResolveSettingsHeadingFontSize()
        {
            FrameworkElement skinContext = (FrameworkElement)_settingsRootGrid ?? _settingsPlanBadgeText;
            double? skinFontSize = FindSkinDouble(skinContext, "FontHeaderLevel4Height", "FontControlHeight", "FontTableHeight");
            return skinFontSize.HasValue && skinFontSize.Value > 0 ? skinFontSize.Value : ResolveSettingsBodyFontSize();
        }

        private UIElement CreateSettingsTabImpl()
        {
            var root = new Grid { Margin = new Thickness(20) };
            root.VerticalAlignment = VerticalAlignment.Stretch;
            root.HorizontalAlignment = HorizontalAlignment.Stretch;
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _settingsRootGrid = root;
            _settingsRootGrid.SizeChanged += OnSettingsRootSizeChanged;

            var settingsStack = new StackPanel
            {
                Orientation = Orientation.Vertical
            };

            var compliancePanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(0)
            };

            compliancePanel.Children.Add(BuildComplianceFeatureExpander(
                "settings.risk.enforce_15_flatten_freeze",
                "Flatten and freeze account when buffer falls below threshold.",
                sim: out _settingsBufferFreezeSimCheckBox,
                eval: out _settingsBufferFreezeEvalCheckBox,
                ap: out _settingsBufferFreezeApCheckBox,
                threshold: out _settingsBufferFreezeThresholdTextBox,
                thresholdOff: out _,
                includeAp: true,
                includeOffThreshold: false,
                defaultThreshold: 0.15));

            compliancePanel.Children.Add(BuildComplianceFeatureExpander(
                "settings.risk.enforce_20_one_contract",
                "Force one-contract replication when buffer falls below on-threshold (release at off-threshold).",
                sim: out _settingsOneContractSimCheckBox,
                eval: out _settingsOneContractEvalCheckBox,
                ap: out _settingsOneContractApCheckBox,
                threshold: out _settingsOneContractOnThresholdTextBox,
                thresholdOff: out _settingsOneContractOffThresholdTextBox,
                includeAp: true,
                includeOffThreshold: true,
                defaultThreshold: 0.20,
                defaultOffThreshold: 0.25));

            compliancePanel.Children.Add(BuildComplianceFeatureExpander(
                "settings.risk.enforce_80_flatten",
                "Flatten account when unrealized loss exceeds threshold of maximum intratrade loss.",
                sim: out _settingsUnrealizedFlattenSimCheckBox,
                eval: out _settingsUnrealizedFlattenEvalCheckBox,
                ap: out _settingsUnrealizedFlattenApCheckBox,
                threshold: out _settingsUnrealizedFlattenThresholdTextBox,
                thresholdOff: out _,
                includeAp: true,
                includeOffThreshold: false,
                defaultThreshold: 0.80));

            compliancePanel.Children.Add(BuildComplianceFeatureExpander(
                "settings.risk.enforce_eval_lock_flatten",
                "Flatten and lock evaluation account when equity reaches the evaluation target lock balance.",
                sim: out _,
                eval: out _settingsEvalProfitTargetLockEvalCheckBox,
                ap: out _,
                threshold: out _,
                thresholdOff: out _,
                includeAp: false,
                includeOffThreshold: false,
                defaultThreshold: 0,
                evalOnly: true));

            compliancePanel.Children.Add(BuildComplianceFeatureExpander(
                "settings.risk.enforce_max_contracts_flatten",
                "Flatten and lock account when open contracts exceed the configured max-contracts limit.",
                sim: out _settingsMaxContractsFlattenSimCheckBox,
                eval: out _settingsMaxContractsFlattenEvalCheckBox,
                ap: out _settingsMaxContractsFlattenApCheckBox,
                threshold: out _,
                thresholdOff: out _,
                includeAp: true,
                includeOffThreshold: false,
                defaultThreshold: 0,
                scopesOnly: true));

            compliancePanel.Children.Add(BuildComplianceFeatureExpander(
                "settings.risk.enforce_no_protection_flatten",
                "Flatten and lock account when an open position has no working protective stop within the configured timeout.",
                sim: out _settingsNoProtectionFlattenSimCheckBox,
                eval: out _settingsNoProtectionFlattenEvalCheckBox,
                ap: out _settingsNoProtectionFlattenApCheckBox,
                threshold: out _,
                thresholdOff: out _,
                includeAp: true,
                includeOffThreshold: false,
                defaultThreshold: 0,
                scopesOnly: true));

            compliancePanel.Children.Add(BuildAiDailyCloseOptIn());

            _settingsCopyTradingPolicyNotice = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0),
                Visibility = Visibility.Collapsed
            };
            ApplySkinResource(_settingsCopyTradingPolicyNotice, TextBlock.ForegroundProperty, "FontControlBrush", "FontTableBrush");
            compliancePanel.Children.Add(_settingsCopyTradingPolicyNotice);

            Expander riskManagementExpander = CreateAccordionExpander(root, "settings.risk.title", "Risk Management Rules");
            riskManagementExpander.IsExpanded = true;
            riskManagementExpander.Content = WrapAccordionSectionContent(compliancePanel);
            settingsStack.Children.Add(riskManagementExpander);

            var licensingPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(0)
            };
            _settingsLicenseKeyTextBox = BuildSettingsTextBox("settings.license.key_placeholder", "Enter license key");
            _settingsLicenseKeyTextBox.GotFocus += OnSettingsLicenseKeyTextBoxGotFocus;
            _settingsLicenseKeyTextBox.LostFocus += OnSettingsLicenseKeyTextBoxLostFocus;

            _settingsPlanBadgeText = new TextBlock
            {
                Margin = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = UiHeadingFontWeight,
                FontSize = ResolveSettingsBodyFontSize()
            };
            ApplySkinResource(_settingsPlanBadgeText, TextBlock.ForegroundProperty, "FontControlBrush", "FontHeaderLevel4Brush", "FontTableBrush");
            _settingsPlanBadgeBorder = new Border
            {
                CornerRadius = new CornerRadius(0),
                Padding = new Thickness(0),
                Margin = new Thickness(0, 10, 0, 8),
                BorderThickness = new Thickness(0),
                Child = _settingsPlanBadgeText
            };
            _settingsPlanBadgeBorder.Background = Brushes.Transparent;
            _settingsPlanBadgeBorder.BorderBrush = Brushes.Transparent;

            licensingPanel.Children.Add(BuildSettingsLabel("settings.license.key", "License Key"));
            licensingPanel.Children.Add(_settingsLicenseKeyTextBox);
            licensingPanel.Children.Add(_settingsPlanBadgeBorder);

            var actionHost = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 14, 0, 0)
            };

            var actionRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0)
            };

            _settingsSaveButton = new Button
            {
                Content = L("settings.button.save", "Save Settings"),
                Margin = new Thickness(0),
                Padding = new Thickness(10, 3, 10, 3),
                MinHeight = 28,
                MinWidth = 118,
                FontSize = ResolveSettingsBodyFontSize(),
                VerticalAlignment = VerticalAlignment.Top,
                Style = CreateSettingsActionButtonStyle(root)
            };
            RegisterLocalizationBinding(() => _settingsSaveButton.Content = L("settings.button.save", "Save Settings"));
            _settingsSaveButton.Click += OnSettingsSaveClick;
            actionRow.Children.Add(_settingsSaveButton);

            _settingsUpdateBadgeText = new TextBlock
            {
                Margin = new Thickness(0, 14, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = UiHeadingFontWeight,
                FontSize = ResolveSettingsBodyFontSize(),
                Visibility = Visibility.Collapsed
            };
            ApplySkinResource(_settingsUpdateBadgeText, TextBlock.ForegroundProperty, "FontControlBrush", "FontTableBrush");

            actionHost.Children.Add(actionRow);
            actionHost.Children.Add(_settingsUpdateBadgeText);
            licensingPanel.Children.Add(actionHost);

            Expander licensingExpander = CreateAccordionExpander(root, "settings.license.title", "License & Updates");
            licensingExpander.IsExpanded = true;
            licensingExpander.Content = WrapAccordionSectionContent(licensingPanel);
            settingsStack.Children.Add(licensingExpander);

            _settingsPageScroll = CreateAccordionPageScrollHost(settingsStack);
            _settingsPageScroll.Loaded += (_, __) => SyncSettingsPageScrollViewport();
            Grid.SetRow(_settingsPageScroll, 0);
            root.Children.Add(_settingsPageScroll);

            UpdateSettingsControlsFromRuntimePolicy();
            UpdateSettingsTabLicenseStatusText();
            UpdateSettingsCopyTradingPolicyNotice();
            SyncSettingsPageScrollViewport();
            return WrapTabBodyForScroll(root);
        }

        private void OnSettingsRootSizeChanged(object sender, SizeChangedEventArgs e)
        {
            SyncSettingsPageScrollViewport();
        }

        private void SyncSettingsPageScrollViewport()
        {
            SyncTabPageScrollViewport(_settingsRootGrid, _settingsPageScroll);
        }

        private void UpdateSettingsCopyTradingPolicyNotice()
        {
            if (_settingsCopyTradingPolicyNotice == null)
                return;

            var firmNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (AccountGridRow row in _accountRows)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.PropFirmDisplay))
                    continue;

                string firmId = ToFirmId(row.PropFirmDisplay);
                if (string.IsNullOrWhiteSpace(firmId) || firmId.Equals("None", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!_firmRules.TryGetValue(firmId, out FirmRuleMetadata metadata) || metadata == null)
                    continue;

                if (IsFirmStatusDiscontinued(metadata.Status) || !IsCopyTradingCleanlyAllowed(metadata))
                    firmNames.Add(ToFirmDisplayName(firmId));
            }

            if (firmNames.Count == 0)
            {
                _settingsCopyTradingPolicyNotice.Visibility = Visibility.Collapsed;
                _settingsCopyTradingPolicyNotice.Text = string.Empty;
                return;
            }

            string joined = string.Join(", ", firmNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
            _settingsCopyTradingPolicyNotice.Text = Lf(
                "settings.compliance.copy_trading_notice",
                "Check {0}'s trade-copier policy — see Settings › Compliance.",
                joined);
            _settingsCopyTradingPolicyNotice.Visibility = Visibility.Visible;
        }

        private TextBlock BuildSettingsLabel(string key, string fallback)
        {
            var label = new TextBlock
            {
                Text = L(key, fallback),
                FontWeight = UiHeadingFontWeight,
                FontSize = ResolveSettingsHeadingFontSize(),
                Margin = new Thickness(0, 6, 0, 2)
            };
            RegisterLocalizationBinding(() => label.Text = L(key, fallback));
            ApplySkinResource(label, TextBlock.ForegroundProperty, "FontControlBrush", "FontTableBrush");
            return label;
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

        private Expander BuildAiDailyCloseOptIn()
        {
            var panel = new StackPanel { Orientation = Orientation.Vertical };
            _settingsAiDailyCloseCheckBox = BuildScopeCheckBox(
                "settings.risk.enable",
                "Enable");
            _settingsAiDailyCloseCheckBox.Margin = new Thickness(0);
            panel.Children.Add(BuildPolicyToggleRow(_settingsAiDailyCloseCheckBox));
            var scope = new TextBlock
            {
                Text = BuildAiDailyCloseScopeDescription(),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
                FontSize = ResolveSettingsBodyFontSize()
            };
            ApplySkinResource(scope, TextBlock.ForegroundProperty, "FontControlBrush", "FontTableBrush");
            panel.Children.Add(scope);

            Expander expander = CreateDisclosureRowExpander(
                GetSettingsStyleContext(),
                "settings.risk.enforce_ai_daily_close",
                "Enable automated daily-close flatten only for the configured AI account scope.");
            expander.IsExpanded = false;
            expander.Content = WrapDisclosureRowContent(panel);
            return expander;
        }

        private string BuildAiDailyCloseScopeDescription()
        {
            GlitchAiRailPolicy policy = GlitchAiRailPolicyStore.Load();
            IEnumerable<string> names = policy?.AccountAllowlist ?? Enumerable.Empty<string>();
            string scope = string.Join(", ", names
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
            return string.IsNullOrWhiteSpace(scope)
                ? L("settings.risk.enforce_ai_daily_close_scope_empty",
                    "Action at 16:59 Eastern: no persisted AI accounts are configured, so this setting cannot flatten any account.")
                : L("settings.risk.enforce_ai_daily_close_scope",
                    "Action at 16:59 Eastern: enabling submits a broad account flatten and cancels working orders only for these persisted AI accounts (independent of Hermes pause):")
                    + " " + scope + ".";
        }

        private Expander BuildComplianceFeatureExpander(
            string titleKey,
            string descriptionFallback,
            out CheckBox sim,
            out CheckBox eval,
            out CheckBox ap,
            out TextBox threshold,
            out TextBox thresholdOff,
            bool includeAp,
            bool includeOffThreshold,
            double defaultThreshold,
            double defaultOffThreshold = 0.25,
            bool evalOnly = false,
            bool scopesOnly = false)
        {
            sim = null;
            eval = null;
            ap = null;
            threshold = null;
            thresholdOff = null;

            var panel = new StackPanel { Orientation = Orientation.Vertical };
            var description = new TextBlock
            {
                Text = descriptionFallback,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6),
                FontSize = ResolveSettingsBodyFontSize()
            };
            ApplySkinResource(description, TextBlock.ForegroundProperty, "FontControlBrush", "FontTableBrush");
            panel.Children.Add(description);

            if (!evalOnly)
            {
                var scopeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
                sim = BuildScopeCheckBox("settings.risk.scope_sim", "Sim");
                eval = BuildScopeCheckBox("settings.risk.scope_eval", "Eval");
                scopeRow.Children.Add(sim);
                scopeRow.Children.Add(eval);
                if (includeAp)
                {
                    ap = BuildScopeCheckBox("settings.risk.scope_pa", "PA");
                    scopeRow.Children.Add(ap);
                }
                panel.Children.Add(scopeRow);
            }
            else
            {
                eval = BuildScopeCheckBox("settings.risk.scope_eval", "Eval");
                panel.Children.Add(eval);
            }

            if (!evalOnly && !scopesOnly)
            {
                threshold = BuildThresholdTextBox(
                    includeOffThreshold
                        ? L("settings.risk.threshold_on", "On threshold (ratio)")
                        : L("settings.risk.threshold", "Threshold (ratio)"),
                    defaultThreshold);
                panel.Children.Add(BuildThresholdRow(threshold));

                if (includeOffThreshold)
                {
                    thresholdOff = BuildThresholdTextBox(L("settings.risk.threshold_off", "Off threshold (ratio)"), defaultOffThreshold);
                    panel.Children.Add(BuildThresholdRow(thresholdOff));
                }

                CheckBox simLocal = sim;
                CheckBox evalLocal = eval;
                CheckBox apLocal = ap;
                TextBox thresholdLocal = threshold;
                TextBox thresholdOffLocal = thresholdOff;
                RoutedEventHandler syncThresholdEnabled = (sender, args) =>
                    UpdateComplianceThresholdEnabled(simLocal, evalLocal, apLocal, thresholdLocal, thresholdOffLocal);
                if (simLocal != null) simLocal.Checked += syncThresholdEnabled;
                if (simLocal != null) simLocal.Unchecked += syncThresholdEnabled;
                if (evalLocal != null) evalLocal.Checked += syncThresholdEnabled;
                if (evalLocal != null) evalLocal.Unchecked += syncThresholdEnabled;
                if (apLocal != null) apLocal.Checked += syncThresholdEnabled;
                if (apLocal != null) apLocal.Unchecked += syncThresholdEnabled;
                UpdateComplianceThresholdEnabled(simLocal, evalLocal, apLocal, thresholdLocal, thresholdOffLocal);
            }

            Expander expander = CreateDisclosureRowExpander(GetSettingsStyleContext(), titleKey, descriptionFallback);
            expander.IsExpanded = false;
            expander.Content = WrapDisclosureRowContent(panel);
            return expander;
        }

        private FrameworkElement GetSettingsStyleContext()
        {
            return _settingsRootGrid != null ? _settingsRootGrid : (FrameworkElement)this;
        }

        private CheckBox BuildScopeCheckBox(string key, string fallback)
        {
            var checkBox = new CheckBox
            {
                Content = L(key, fallback),
                Margin = new Thickness(0, 0, 14, 0),
                Style = CreateSettingsPolicyCheckBoxStyle(GetSettingsStyleContext())
            };
            RegisterLocalizationBinding(() => checkBox.Content = L(key, fallback));
            return checkBox;
        }

        private TextBox BuildThresholdTextBox(string label, double defaultRatio)
        {
            var textBox = new TextBox
            {
                Tag = label,
                Text = defaultRatio.ToString("0.##", CultureInfo.InvariantCulture),
                MinWidth = 72,
                MaxWidth = 120,
                Margin = new Thickness(0, 0, 0, 0),
                FontSize = ResolveSettingsBodyFontSize(),
                Style = CreateSettingsLicenseTextBoxStyle(GetSettingsStyleContext())
            };
            textBox.FocusVisualStyle = null;
            return textBox;
        }

        private StackPanel BuildThresholdRow(TextBox thresholdTextBox)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            var label = new TextBlock
            {
                Text = thresholdTextBox?.Tag as string ?? L("settings.risk.threshold", "Threshold (ratio)"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
                MinWidth = 150
            };
            ApplySkinResource(label, TextBlock.ForegroundProperty, "FontControlBrush", "FontTableBrush");
            row.Children.Add(label);
            if (thresholdTextBox != null)
                row.Children.Add(thresholdTextBox);
            return row;
        }

        private static void UpdateComplianceThresholdEnabled(
            CheckBox sim,
            CheckBox eval,
            CheckBox ap,
            TextBox threshold,
            TextBox thresholdOff)
        {
            bool anyEnabled = (sim?.IsChecked == true) || (eval?.IsChecked == true) || (ap?.IsChecked == true);
            if (threshold != null)
                threshold.IsEnabled = anyEnabled;
            if (thresholdOff != null)
                thresholdOff.IsEnabled = anyEnabled;
        }

        private static bool TryReadComplianceThreshold(TextBox textBox, double fallback, double min, double max, out double value)
        {
            value = fallback;
            if (textBox == null)
                return true;

            string raw = (textBox.Text ?? string.Empty).Trim();
            if (raw.Length == 0)
                return true;

            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) &&
                !double.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed))
            {
                return false;
            }

            value = Math.Max(min, Math.Min(max, parsed));
            return true;
        }

        private Style CreateSettingsActionButtonStyle(FrameworkElement context)
        {
            var baseStyle = FindSkinStyle(context, typeof(Button));
            var style = new Style(typeof(Button), baseStyle);

            ApplySkinSetter(style, Control.BackgroundProperty, context, "BackgroundMainWindow", "GridEntireBackground", "BackgroundTextInput");
            ApplySkinSetter(style, Control.BorderBrushProperty, context, "BorderThinBrush", "TabControlBorderBrush");
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
            style.Setters.Add(new Setter(Control.CursorProperty, System.Windows.Input.Cursors.Hand));
            ApplySkinSetter(style, Control.ForegroundProperty, context, "FontControlBrush", "FontTableBrush", "GridRowForeground");
            style.Setters.Add(new Setter(Control.FontWeightProperty, UiActionFontWeight));
            style.Setters.Add(new Setter(Control.TemplateProperty, CreateActionButtonTemplate()));
            double? sharedFontSize = FindSkinDouble(context, "FontTableHeight", "FontControlHeight", "FontHeaderLevel4Height");
            if (sharedFontSize.HasValue)
                style.Setters.Add(new Setter(Control.FontSizeProperty, sharedFontSize.Value));
            ApplyTealAccentResourceOverrides(style);

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, TealAccentBrush));
            hoverTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, TealAccentBrush));
            hoverTrigger.Setters.Add(new Setter(Control.ForegroundProperty, AccentOnColorForegroundBrush));
            style.Triggers.Add(hoverTrigger);

            var pressedTrigger = new Trigger { Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Value = true };
            pressedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, TealAccentBrush));
            pressedTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, TealAccentBrush));
            pressedTrigger.Setters.Add(new Setter(Control.ForegroundProperty, AccentOnColorForegroundBrush));
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
            markFactory.SetValue(System.Windows.Shapes.Path.StrokeProperty, AccentOnColorForegroundBrush);
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
                FontSize = ResolveSettingsBodyFontSize(),
                Style = CreateSettingsPolicyCheckBoxStyle(_settingsRootGrid)
            };
            RegisterLocalizationBinding(() => checkBox.Content = L(key, fallback));
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
                FontSize = ResolveSettingsBodyFontSize(),
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

            bool aiDailyCloseWasEnabled = _runtimePolicySettings.EnforceAiDailyClose;
            _runtimePolicySettings.EnforceAccountLevelCompliance = false;
            _runtimePolicySettings.EnforceAiDailyClose = _settingsAiDailyCloseCheckBox?.IsChecked == true;
            _runtimePolicySettings.BufferFreezeScopes.Sim = _settingsBufferFreezeSimCheckBox?.IsChecked == true;
            _runtimePolicySettings.BufferFreezeScopes.Eval = _settingsBufferFreezeEvalCheckBox?.IsChecked == true;
            _runtimePolicySettings.BufferFreezeScopes.Ap = _settingsBufferFreezeApCheckBox?.IsChecked == true;
            _runtimePolicySettings.BufferOneContractScopes.Sim = _settingsOneContractSimCheckBox?.IsChecked == true;
            _runtimePolicySettings.BufferOneContractScopes.Eval = _settingsOneContractEvalCheckBox?.IsChecked == true;
            _runtimePolicySettings.BufferOneContractScopes.Ap = _settingsOneContractApCheckBox?.IsChecked == true;
            _runtimePolicySettings.UnrealizedFlattenScopes.Sim = _settingsUnrealizedFlattenSimCheckBox?.IsChecked == true;
            _runtimePolicySettings.UnrealizedFlattenScopes.Eval = _settingsUnrealizedFlattenEvalCheckBox?.IsChecked == true;
            _runtimePolicySettings.UnrealizedFlattenScopes.Ap = _settingsUnrealizedFlattenApCheckBox?.IsChecked == true;
            _runtimePolicySettings.EvalProfitTargetLockEnabled = _settingsEvalProfitTargetLockEvalCheckBox?.IsChecked == true;
            _runtimePolicySettings.MaxContractsFlattenScopes.Sim = _settingsMaxContractsFlattenSimCheckBox?.IsChecked == true;
            _runtimePolicySettings.MaxContractsFlattenScopes.Eval = _settingsMaxContractsFlattenEvalCheckBox?.IsChecked == true;
            _runtimePolicySettings.MaxContractsFlattenScopes.Ap = _settingsMaxContractsFlattenApCheckBox?.IsChecked == true;
            _runtimePolicySettings.NoProtectionFlattenScopes.Sim = _settingsNoProtectionFlattenSimCheckBox?.IsChecked == true;
            _runtimePolicySettings.NoProtectionFlattenScopes.Eval = _settingsNoProtectionFlattenEvalCheckBox?.IsChecked == true;
            _runtimePolicySettings.NoProtectionFlattenScopes.Ap = _settingsNoProtectionFlattenApCheckBox?.IsChecked == true;

            if (!TryReadComplianceThreshold(_settingsBufferFreezeThresholdTextBox, _runtimePolicySettings.BufferFreezeThresholdRatio, 0.01, 0.99, out double bufferFreezeThreshold))
                bufferFreezeThreshold = _runtimePolicySettings.BufferFreezeThresholdRatio;
            _runtimePolicySettings.BufferFreezeThresholdRatio = bufferFreezeThreshold;

            if (!TryReadComplianceThreshold(_settingsOneContractOnThresholdTextBox, _runtimePolicySettings.BufferOneContractOnThresholdRatio, 0.01, 0.99, out double oneContractOn))
                oneContractOn = _runtimePolicySettings.BufferOneContractOnThresholdRatio;
            _runtimePolicySettings.BufferOneContractOnThresholdRatio = oneContractOn;

            if (!TryReadComplianceThreshold(_settingsOneContractOffThresholdTextBox, _runtimePolicySettings.BufferOneContractOffThresholdRatio, oneContractOn, 0.99, out double oneContractOff))
                oneContractOff = Math.Max(oneContractOn, _runtimePolicySettings.BufferOneContractOffThresholdRatio);
            _runtimePolicySettings.BufferOneContractOffThresholdRatio = oneContractOff;

            if (!TryReadComplianceThreshold(_settingsUnrealizedFlattenThresholdTextBox, _runtimePolicySettings.UnrealizedFlattenThresholdRatio, 0.01, 0.99, out double unrealizedThreshold))
                unrealizedThreshold = _runtimePolicySettings.UnrealizedFlattenThresholdRatio;
            _runtimePolicySettings.UnrealizedFlattenThresholdRatio = unrealizedThreshold;

            _runtimePolicySettings.SyncLegacyComplianceFlags();
            _runtimePolicySettings.FlattenOnCriticalBufferLock = false;
            _runtimePolicySettings.LicenseKey = GetNormalizedSettingsLicenseKeyText();
            _settingsLicenseKeyUnmaskedValue = (_runtimePolicySettings.LicenseKey ?? string.Empty).Trim();
            ApplySettingsLicenseMaskedDisplay();
            if (string.IsNullOrWhiteSpace(_runtimePolicySettings.LicenseApiBaseUrl))
                _runtimePolicySettings.LicenseApiBaseUrl = "https://api.glitchtrader.com";

            GlitchRuntimePolicyStore.SaveSettings(_runtimePolicyFilePath, _runtimePolicySettings);
            if (aiDailyCloseWasEnabled != _runtimePolicySettings.EnforceAiDailyClose)
            {
                AppendJournal(
                    "System",
                    "Risk",
                    "ai_daily_close|origin=settings|result="
                        + (_runtimePolicySettings.EnforceAiDailyClose ? "enabled" : "disabled")
                        + "|scope=configured_ai_accounts");
            }
            AppendJournal("System", "Policy", "Runtime settings updated by user.");
            AppendJournal("System", "Runtime", BuildRuntimePolicySummaryLogLine());

            if (!_runtimePolicySettings.AnyRiskComplianceFeatureEnabled())
                ClearComplianceEnforcementRuntimeState();

            _nextLicenseHeartbeatUtc = DateTime.UtcNow;
            Task validateTask = RefreshLicenseStateAsync(useValidateEndpoint: true, force: true);
            _settingsSaveFeedbackActive = true;
            try
            {
                await ShowTransientTealButtonFeedbackAsync(_settingsSaveButton, L("settings.feedback.saving", "Saving!"), 3000);
                try
                {
                    await validateTask;
                }
                catch (Exception validateError)
                {
                    AppendJournal("System", "License", $"License validation failed after save: {validateError.Message}");
                }
            }
            finally
            {
                _settingsSaveFeedbackActive = false;
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
            button.Foreground = AccentOnColorForegroundBrush;

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

            if (_settingsBufferFreezeSimCheckBox != null)
                _settingsBufferFreezeSimCheckBox.IsChecked = _runtimePolicySettings.BufferFreezeScopes.Sim;
            if (_settingsBufferFreezeEvalCheckBox != null)
                _settingsBufferFreezeEvalCheckBox.IsChecked = _runtimePolicySettings.BufferFreezeScopes.Eval;
            if (_settingsBufferFreezeApCheckBox != null)
                _settingsBufferFreezeApCheckBox.IsChecked = _runtimePolicySettings.BufferFreezeScopes.Ap;
            if (_settingsBufferFreezeThresholdTextBox != null)
                _settingsBufferFreezeThresholdTextBox.Text = _runtimePolicySettings.BufferFreezeThresholdRatio.ToString("0.##", CultureInfo.InvariantCulture);

            if (_settingsOneContractSimCheckBox != null)
                _settingsOneContractSimCheckBox.IsChecked = _runtimePolicySettings.BufferOneContractScopes.Sim;
            if (_settingsOneContractEvalCheckBox != null)
                _settingsOneContractEvalCheckBox.IsChecked = _runtimePolicySettings.BufferOneContractScopes.Eval;
            if (_settingsOneContractApCheckBox != null)
                _settingsOneContractApCheckBox.IsChecked = _runtimePolicySettings.BufferOneContractScopes.Ap;
            if (_settingsOneContractOnThresholdTextBox != null)
                _settingsOneContractOnThresholdTextBox.Text = _runtimePolicySettings.BufferOneContractOnThresholdRatio.ToString("0.##", CultureInfo.InvariantCulture);
            if (_settingsOneContractOffThresholdTextBox != null)
                _settingsOneContractOffThresholdTextBox.Text = _runtimePolicySettings.BufferOneContractOffThresholdRatio.ToString("0.##", CultureInfo.InvariantCulture);

            if (_settingsUnrealizedFlattenSimCheckBox != null)
                _settingsUnrealizedFlattenSimCheckBox.IsChecked = _runtimePolicySettings.UnrealizedFlattenScopes.Sim;
            if (_settingsUnrealizedFlattenEvalCheckBox != null)
                _settingsUnrealizedFlattenEvalCheckBox.IsChecked = _runtimePolicySettings.UnrealizedFlattenScopes.Eval;
            if (_settingsUnrealizedFlattenApCheckBox != null)
                _settingsUnrealizedFlattenApCheckBox.IsChecked = _runtimePolicySettings.UnrealizedFlattenScopes.Ap;
            if (_settingsUnrealizedFlattenThresholdTextBox != null)
                _settingsUnrealizedFlattenThresholdTextBox.Text = _runtimePolicySettings.UnrealizedFlattenThresholdRatio.ToString("0.##", CultureInfo.InvariantCulture);

            if (_settingsEvalProfitTargetLockEvalCheckBox != null)
                _settingsEvalProfitTargetLockEvalCheckBox.IsChecked = _runtimePolicySettings.EvalProfitTargetLockEnabled;
            if (_settingsMaxContractsFlattenSimCheckBox != null)
                _settingsMaxContractsFlattenSimCheckBox.IsChecked = _runtimePolicySettings.MaxContractsFlattenScopes.Sim;
            if (_settingsMaxContractsFlattenEvalCheckBox != null)
                _settingsMaxContractsFlattenEvalCheckBox.IsChecked = _runtimePolicySettings.MaxContractsFlattenScopes.Eval;
            if (_settingsMaxContractsFlattenApCheckBox != null)
                _settingsMaxContractsFlattenApCheckBox.IsChecked = _runtimePolicySettings.MaxContractsFlattenScopes.Ap;
            if (_settingsNoProtectionFlattenSimCheckBox != null)
                _settingsNoProtectionFlattenSimCheckBox.IsChecked = _runtimePolicySettings.NoProtectionFlattenScopes.Sim;
            if (_settingsNoProtectionFlattenEvalCheckBox != null)
                _settingsNoProtectionFlattenEvalCheckBox.IsChecked = _runtimePolicySettings.NoProtectionFlattenScopes.Eval;
            if (_settingsNoProtectionFlattenApCheckBox != null)
                _settingsNoProtectionFlattenApCheckBox.IsChecked = _runtimePolicySettings.NoProtectionFlattenScopes.Ap;
            if (_settingsAiDailyCloseCheckBox != null)
                _settingsAiDailyCloseCheckBox.IsChecked = _runtimePolicySettings.EnforceAiDailyClose;

            UpdateComplianceThresholdEnabled(
                _settingsBufferFreezeSimCheckBox,
                _settingsBufferFreezeEvalCheckBox,
                _settingsBufferFreezeApCheckBox,
                _settingsBufferFreezeThresholdTextBox,
                null);
            UpdateComplianceThresholdEnabled(
                _settingsOneContractSimCheckBox,
                _settingsOneContractEvalCheckBox,
                _settingsOneContractApCheckBox,
                _settingsOneContractOnThresholdTextBox,
                _settingsOneContractOffThresholdTextBox);
            UpdateComplianceThresholdEnabled(
                _settingsUnrealizedFlattenSimCheckBox,
                _settingsUnrealizedFlattenEvalCheckBox,
                _settingsUnrealizedFlattenApCheckBox,
                _settingsUnrealizedFlattenThresholdTextBox,
                null);
            if (_settingsLicenseKeyTextBox != null)
            {
                string licenseKey = (_runtimePolicySettings.LicenseKey ?? string.Empty).Trim();
                _settingsLicenseKeyUnmaskedValue = licenseKey;
                if (string.IsNullOrWhiteSpace(licenseKey))
                {
                    ApplySettingsLicensePlaceholder();
                }
                else
                {
                    ApplySettingsLicenseMaskedDisplay();
                }
            }
        }

        private void OnSettingsLicenseKeyTextBoxGotFocus(object sender, RoutedEventArgs e)
        {
            if (_settingsLicensePlaceholderActive)
            {
                ClearSettingsLicensePlaceholder();
                return;
            }

            if (_settingsLicenseKeyTextBox == null)
                return;

            if (_settingsLicenseMaskedDisplayActive && !string.IsNullOrWhiteSpace(_settingsLicenseKeyUnmaskedValue))
            {
                _settingsLicenseMaskedDisplayActive = false;
                _settingsLicenseKeyTextBox.Text = _settingsLicenseKeyUnmaskedValue;
                _settingsLicenseKeyTextBox.CaretIndex = _settingsLicenseKeyTextBox.Text.Length;
                _settingsLicenseKeyTextBox.SelectAll();
            }
        }

        private void OnSettingsLicenseKeyTextBoxLostFocus(object sender, RoutedEventArgs e)
        {
            if (_settingsLicenseKeyTextBox == null)
                return;

            string normalized = (_settingsLicenseKeyTextBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                _settingsLicenseKeyUnmaskedValue = string.Empty;
                ApplySettingsLicensePlaceholder();
                return;
            }

            _settingsLicenseKeyUnmaskedValue = normalized;
            ApplySettingsLicenseMaskedDisplay();
        }

        private void ClearSettingsLicensePlaceholder()
        {
            if (_settingsLicenseKeyTextBox == null || !_settingsLicensePlaceholderActive)
                return;

            _settingsLicensePlaceholderActive = false;
            _settingsLicenseMaskedDisplayActive = false;
            _settingsLicenseKeyTextBox.Text = string.Empty;
            ApplySettingsLicenseInputNormalVisual();
        }

        private void ApplySettingsLicensePlaceholder()
        {
            if (_settingsLicenseKeyTextBox == null)
                return;

            _settingsLicensePlaceholderActive = true;
            _settingsLicenseMaskedDisplayActive = false;
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
            Brush normalBrush = FindSkinBrush(skinContext, "FontControlBrush", "FontTableBrush", "GridRowForeground") ?? Brushes.Black;
            _settingsLicenseKeyTextBox.Foreground = normalBrush;
        }

        private void ApplySettingsLicenseMaskedDisplay()
        {
            if (_settingsLicenseKeyTextBox == null)
                return;

            string normalized = (_settingsLicenseKeyUnmaskedValue ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                ApplySettingsLicensePlaceholder();
                return;
            }

            _settingsLicensePlaceholderActive = false;
            _settingsLicenseMaskedDisplayActive = true;
            _settingsLicenseKeyTextBox.Text = BuildMaskedLicenseKey(normalized);
            ApplySettingsLicenseInputNormalVisual();
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

            if (_settingsLicenseMaskedDisplayActive)
                return (_settingsLicenseKeyUnmaskedValue ?? string.Empty).Trim();

            _settingsLicenseKeyUnmaskedValue = value;
            return value;
        }

        private static string BuildMaskedLicenseKey(string licenseKey)
        {
            string normalized = (licenseKey ?? string.Empty).Trim();
            if (normalized.Length <= 8)
                return normalized;

            const int visiblePrefix = 4;
            const int visibleSuffix = 4;
            int maskLength = normalized.Length - visiblePrefix - visibleSuffix;
            if (maskLength <= 0)
                return normalized;

            return normalized.Substring(0, visiblePrefix)
                + new string('*', maskLength)
                + normalized.Substring(normalized.Length - visibleSuffix, visibleSuffix);
        }

        private void UpdateSettingsTabLicenseStatusText()
        {
            if (_settingsPlanBadgeText == null)
                return;

            GlitchLicenseCacheState cache = _licenseCacheState ?? new GlitchLicenseCacheState();
            string status = string.IsNullOrWhiteSpace(cache.LastStatus) ? "unknown" : cache.LastStatus.Trim().ToLowerInvariant();
            string plan = string.IsNullOrWhiteSpace(cache.Plan) ? "free_lite" : cache.Plan.Trim().ToLowerInvariant();
            bool hasProAccess =
                plan.Equals("premium", StringComparison.OrdinalIgnoreCase) &&
                (status.Equals("active", StringComparison.OrdinalIgnoreCase) ||
                 status.Equals("grace", StringComparison.OrdinalIgnoreCase));
            bool hasClientUpdate =
                _isClientUpdateAvailable &&
                !string.IsNullOrWhiteSpace(_latestClientVersion);
            string updateDownloadUrl = string.IsNullOrWhiteSpace(_latestDownloadUrl)
                ? DefaultLatestDownloadUrl
                : _latestDownloadUrl.Trim();

            FrameworkElement skinContext = (FrameworkElement)_settingsRootGrid ?? _settingsPlanBadgeText;
            Brush baseTextBrush = FindSkinBrush(skinContext, "FontControlBrush", "FontTableBrush", "GridRowForeground") ?? Brushes.Black;
            double sharedFontSize = _settingsPlanBadgeText.FontSize;
            if (sharedFontSize <= 0)
            {
                sharedFontSize = ResolveSettingsBodyFontSize();
            }

            _settingsPlanBadgeText.Inlines.Clear();
            if (_settingsUpdateBadgeText != null)
            {
                _settingsUpdateBadgeText.Inlines.Clear();
                _settingsUpdateBadgeText.Visibility = Visibility.Collapsed;
            }
            _settingsPlanBadgeText.Inlines.Add(new Run(L("settings.license.active_prefix", "Active License:"))
            {
                Foreground = baseTextBrush,
                FontWeight = UiHeadingFontWeight,
                FontSize = sharedFontSize
            });
            _settingsPlanBadgeText.Inlines.Add(new Run(" "));

            if (hasProAccess)
            {
                _settingsPlanBadgeText.Inlines.Add(new Run(L("settings.license.plan_pro", "Pro"))
                {
                    Foreground = OrangeAccentBrush,
                    FontWeight = UiHeadingFontWeight,
                    FontSize = sharedFontSize
                });
            }
            else
            {
                _settingsPlanBadgeText.Inlines.Add(new Run(L("settings.license.plan_lite", "Lite"))
                {
                    Foreground = TealAccentBrush,
                    FontWeight = UiHeadingFontWeight,
                    FontSize = sharedFontSize
                });
                _settingsPlanBadgeText.Inlines.Add(new Run(" - ")
                {
                    Foreground = baseTextBrush,
                    FontSize = sharedFontSize
                });

                var upgradeLink = new Hyperlink(new Run(L("settings.license.upgrade_to_pro", "Upgrade to Pro")))
                {
                    Foreground = OrangeAccentBrush,
                    FontWeight = UiHeadingFontWeight,
                    FontSize = sharedFontSize,
                    TextDecorations = null
                };
                upgradeLink.Click += (_, __) => OpenAnalyticsExternalUrl(GetWhopUpgradeCheckoutUrl());
                _settingsPlanBadgeText.Inlines.Add(upgradeLink);
            }

            if (!hasClientUpdate)
                return;

            if (_settingsUpdateBadgeText == null)
                return;

            _settingsUpdateBadgeText.Inlines.Clear();
            _settingsUpdateBadgeText.Visibility = Visibility.Visible;
            _settingsUpdateBadgeText.Inlines.Add(new Run(L("settings.update.available_prefix", "Update Available v."))
            {
                Foreground = baseTextBrush,
                FontWeight = UiHeadingFontWeight,
                FontSize = sharedFontSize
            });
            _settingsUpdateBadgeText.Inlines.Add(new Run(" "));
            _settingsUpdateBadgeText.Inlines.Add(new Run(_latestClientVersion.Trim())
            {
                Foreground = baseTextBrush,
                FontWeight = UiHeadingFontWeight,
                FontSize = sharedFontSize
            });
            _settingsUpdateBadgeText.Inlines.Add(new Run(" - ")
            {
                Foreground = baseTextBrush,
                FontSize = sharedFontSize
            });

            var downloadLink = new Hyperlink(new Run(L("settings.update.download_latest", "Download Latest")))
            {
                Foreground = TealAccentBrush,
                FontWeight = UiHeadingFontWeight,
                FontSize = sharedFontSize,
                TextDecorations = null
            };
            downloadLink.Click += (_, __) => OpenAnalyticsExternalUrl(updateDownloadUrl);
            _settingsUpdateBadgeText.Inlines.Add(downloadLink);
        }
    }
}
