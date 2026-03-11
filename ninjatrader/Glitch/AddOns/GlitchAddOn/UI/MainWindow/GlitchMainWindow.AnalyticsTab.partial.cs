//
//
//   /$$$$$$  /$$ /$$   /$$               /$$      
//  /$$__  $$| $$|__/  | $$              | $$      
// | $$  \__/| $$ /$$ /$$$$$$    /$$$$$$$| $$$$$$$ 
// | $$ /$$$$| $$| $$|_  $$_/   /$$_____/| $$__  $$
// | $$|_  $$| $$| $$  | $$    | $$      | $$  \ $$
// | $$  \ $$| $$| $$  | $$ /$$| $$      | $$  | $$
// |  $$$$$$/| $$| $$  |  $$$$/|  $$$$$$$| $$  | $$
//  \______/ |__/|__/   \___/   \_______/|__/  |__/
//                                                                                                
//
// __________________________________________________
// __________________________________________________
//
//
// Glitch AddOn
//
// v.0.1.0.
// March 03, 2026
// by GlitchTrader.com
//
// __________________________________________________
// __________________________________________________
//

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Glitch.Services;
using NinjaTrader.Cbi;
using NinjaTrader.Gui.Tools;
namespace Glitch.UI
{
    public partial class GlitchMainWindow
    {
        private const double AnalyticsDialCenterX = 80.0;
        private const double AnalyticsDialCenterY = 72.0;
        private const double AnalyticsDialRadius = 60.0;
        private const double AnalyticsDialNeutralHalfWidth = 0.10;
        private const double AnalyticsUnifiedNeutralThreshold = 0.10;
        private const double AnalyticsFactorVisualGamma = 0.82;
        private const double AnalyticsComponentRawWeight = 0.35;
        private const double AnalyticsComponentMaWeight = 0.30;
        private const double AnalyticsComponentOscWeight = 0.20;
        private const double AnalyticsComponentOrderFlowWeight = 0.15;
        private static readonly Brush AnalyticsDialSellStrongBrush = CreateFrozenAnalyticsDialBrush(0xFF, 0x42, 0x00);
        private static readonly Brush AnalyticsDialSellMidBrush = CreateFrozenAnalyticsDialBrush(0xFF, 0x7A, 0x26);
        private static readonly Brush AnalyticsDialSellWeakBrush = CreateFrozenAnalyticsDialBrush(0xD6, 0x8A, 0x55);
        private static readonly Brush AnalyticsDialNeutralBrush = CreateFrozenAnalyticsDialBrush(0x9A, 0xA1, 0xAB);
        private static readonly Brush AnalyticsDialBuyWeakBrush = CreateFrozenAnalyticsDialBrush(0x6C, 0xC0, 0xB2);
        private static readonly Brush AnalyticsDialBuyMidBrush = CreateFrozenAnalyticsDialBrush(0x38, 0xD5, 0xBF);
        private static readonly Brush AnalyticsDialBuyStrongBrush = CreateFrozenAnalyticsDialBrush(0x1A, 0xBC, 0x9C);
        private TextBlock _analyticsTechnicalFeedFootnoteText;
        private ComboBox _analyticsUnifiedExecutionTfCombo;
        private int _analyticsUnifiedExecutionTimeframeMinutes = 5;
        private Border _analyticsUnifiedSignalCard;
        private Grid _analyticsUnifiedSignalGrid;
        private FrameworkElement _analyticsUnifiedBiasCell;
        private FrameworkElement _analyticsUnifiedExecutionSignalCell;
        private FrameworkElement _analyticsUnifiedSetupCell;
        private FrameworkElement _analyticsUnifiedQualityCell;
        private FrameworkElement _analyticsUnifiedGateCell;
        private FrameworkElement _analyticsUnifiedExecutionTfCell;
        private int _analyticsUnifiedLayoutMode = -1;
        private TextBlock _analyticsUnifiedBiasValueText;
        private TextBlock _analyticsUnifiedExecutionSignalValueText;
        private TextBlock _analyticsUnifiedSetupValueText;
        private TextBlock _analyticsUnifiedQualityValueText;
        private TextBlock _analyticsUnifiedGateValueText;
        private readonly Dictionary<int, AnalyticsCardExpansionVisual> _analyticsCardExpansionVisuals = new Dictionary<int, AnalyticsCardExpansionVisual>();
        private readonly Dictionary<int, bool> _analyticsExpandedCardStates = new Dictionary<int, bool>();
        private Grid _analyticsLicenseGateOverlay;
        private TextBlock _analyticsLicenseGateMessageText;

        private UIElement CreateAnalyticsTabImpl()
        {
            _analyticsDialVisuals.Clear();
            _analyticsTimeframeVisuals.Clear();
            _analyticsCardExpansionVisuals.Clear();

            var root = new Grid { Margin = new Thickness(20) };
            _analyticsRoot = root;
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.SizeChanged += OnAnalyticsRootSizeChanged;

            _analyticsTopBarGrid = new Grid();

            var topHeaderBand = CreateAnalyticsCard(root, new Thickness(0, 0, 0, 12), new Thickness(8, 6, 8, 6));
            topHeaderBand.Child = _analyticsTopBarGrid;
            Grid.SetRow(topHeaderBand, 0);
            root.Children.Add(topHeaderBand);

            var instrumentHost = new Grid
            {
                VerticalAlignment = VerticalAlignment.Center,
                MinHeight = 68,
                MinWidth = 119,
                Margin = new Thickness(0, 0, 8, 0)
            };
            var instrumentStack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                VerticalAlignment = VerticalAlignment.Center
            };
            var instrumentLabel = new TextBlock
            {
                Text = L("analytics.instrument", "Instrument"),
                Margin = new Thickness(0, 0, 0, 4)
            };
            BindLocalizedText(instrumentLabel, "analytics.instrument", "Instrument");
            ApplySkinResource(instrumentLabel, TextBlock.ForegroundProperty, "FontHeaderLevel4Brush", "FontControlBrush", "FontTableBrush");
            instrumentStack.Children.Add(instrumentLabel);

            _analyticsInstrumentCombo = new ComboBox
            {
                MinWidth = 105,
                IsEditable = false,
                IsTextSearchEnabled = true,
                HorizontalContentAlignment = HorizontalAlignment.Left
            };
            _analyticsInstrumentCombo.Style = CreateGroupMasterComboBoxStyle(root);
            _analyticsInstrumentCombo.SelectionChanged += OnAnalyticsInstrumentSelectionChanged;
            instrumentStack.Children.Add(_analyticsInstrumentCombo);
            instrumentHost.Children.Add(instrumentStack);
            _analyticsInstrumentHost = instrumentHost;
            _analyticsTopBarGrid.Children.Add(instrumentHost);

            var sessionRangeCard = CreateAnalyticsCard(root, new Thickness(0, 0, 8, 0), new Thickness(8, 6, 8, 6));
            sessionRangeCard.MinHeight = 68;
            sessionRangeCard.MinWidth = 85;
            sessionRangeCard.VerticalAlignment = VerticalAlignment.Bottom;
            sessionRangeCard.Background = Brushes.Transparent;
            sessionRangeCard.BorderThickness = new Thickness(0);
            sessionRangeCard.ClipToBounds = true;
            sessionRangeCard.Child = CreateAnalyticsSessionRangeVisual(root);
            _analyticsSessionRangeCard = sessionRangeCard;
            _analyticsTopBarGrid.Children.Add(sessionRangeCard);

            var sessionCard = CreateAnalyticsMetricCard(_analyticsTopBarGrid, "analytics.metric.session", "Session", out _analyticsSessionText, new Thickness(0, 0, 8, 0));
            sessionCard.Background = Brushes.Transparent;
            sessionCard.BorderThickness = new Thickness(0);
            _analyticsSessionCard = sessionCard;
            _analyticsTopBarGrid.Children.Add(sessionCard);

            var overallSignalCard = CreateAnalyticsMetricCard(_analyticsTopBarGrid, "analytics.metric.composite", "Glitch Score", out _analyticsOverallSignalText, new Thickness(0, 0, 8, 0));
            overallSignalCard.Background = Brushes.Transparent;
            overallSignalCard.BorderThickness = new Thickness(0);
            overallSignalCard.MinWidth = 168;
            _analyticsSignalCard = overallSignalCard;
            _analyticsTopBarGrid.Children.Add(overallSignalCard);

            var currentPriceCard = CreateAnalyticsMetricCard(_analyticsTopBarGrid, "analytics.metric.current_price", "Current Price", out _analyticsCurrentPriceText, new Thickness(0));
            currentPriceCard.Background = Brushes.Transparent;
            currentPriceCard.BorderThickness = new Thickness(0);
            _analyticsCurrentPriceCard = currentPriceCard;
            _analyticsTopBarGrid.Children.Add(currentPriceCard);

            var body = new Grid { VerticalAlignment = VerticalAlignment.Top };
            _analyticsBodyGrid = body;

            var timeframeGrid = new Grid { Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Top };
            _analyticsTimeframeGrid = timeframeGrid;

            _analyticsUnifiedSignalCard = CreateAnalyticsUnifiedSignalBar(root);
            timeframeGrid.Children.Add(_analyticsUnifiedSignalCard);

            Border oneMinuteCard = CreateAnalyticsCompositeTimeframeCard(root, 1, new Thickness(0, 0, 8, 8));
            timeframeGrid.Children.Add(oneMinuteCard);
            _analyticsOneMinuteCard = oneMinuteCard;

            Border fiveMinuteCard = CreateAnalyticsCompositeTimeframeCard(root, 5, new Thickness(0, 0, 0, 8));
            timeframeGrid.Children.Add(fiveMinuteCard);
            _analyticsFiveMinuteCard = fiveMinuteCard;

            Border fifteenMinuteCard = CreateAnalyticsCompositeTimeframeCard(root, 15, new Thickness(0, 0, 8, 0));
            timeframeGrid.Children.Add(fifteenMinuteCard);
            _analyticsFifteenMinuteCard = fifteenMinuteCard;

            Border sixtyMinuteCard = CreateAnalyticsCompositeTimeframeCard(root, 60, new Thickness(0));
            timeframeGrid.Children.Add(sixtyMinuteCard);
            _analyticsSixtyMinuteCard = sixtyMinuteCard;

            body.Children.Add(timeframeGrid);

            var fundamentalStack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                VerticalAlignment = VerticalAlignment.Top,
                MinWidth = 300
            };

            _analyticsOpenDetachedWindowButton = new Button
            {
                Content = L("analytics.button.open_window", "Open Nasdaq Macro"),
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(10, 4, 10, 4),
                MinWidth = 148,
                HorizontalAlignment = HorizontalAlignment.Right,
                Visibility = Visibility.Collapsed,
                Style = CreateGroupActionButtonStyle(root)
            };
            BindLocalizedContent(_analyticsOpenDetachedWindowButton, "analytics.button.open_window", "Open Nasdaq Macro");
            _analyticsOpenDetachedWindowButton.Click += OnAnalyticsOpenDetachedWindowClick;
            fundamentalStack.Children.Add(_analyticsOpenDetachedWindowButton);

            fundamentalStack.Children.Add(CreateAnalyticsMag7ScoringBlock(root, out _analyticsScoreSectionTitleText, out _analyticsMag7ItemsHost));
            fundamentalStack.Children.Add(CreateAnalyticsHeadlineListBlock(root, "analytics.block.latest_headlines", "Latest Headlines", out _analyticsLatestHeadlinesList));
            fundamentalStack.Children.Add(CreateAnalyticsInfoBlock(root, "analytics.block.upcoming_news_calendar", "Upcoming News Calendar", out _analyticsOfficialNewsText));
            fundamentalStack.Children.Add(CreateAnalyticsInfoBlock(root, "analytics.block.earnings_analysis", "Earnings Analysis", out _analyticsEarningsAnalysisText));

            _analyticsFundamentalFootnoteText = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0),
                Opacity = 0.85,
                Visibility = Visibility.Collapsed
            };
            ApplySkinResource(_analyticsFundamentalFootnoteText, TextBlock.ForegroundProperty, "FontControlBrush", "FontTableBrush");
            fundamentalStack.Children.Add(_analyticsFundamentalFootnoteText);

            _analyticsTechnicalFeedFootnoteText = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0),
                Opacity = 0.85,
                Visibility = Visibility.Collapsed
            };
            ApplySkinResource(_analyticsTechnicalFeedFootnoteText, TextBlock.ForegroundProperty, "FontControlBrush", "FontTableBrush");
            fundamentalStack.Children.Add(_analyticsTechnicalFeedFootnoteText);

            body.Children.Add(fundamentalStack);
            _analyticsFundamentalCard = fundamentalStack;

            Grid.SetRow(body, 1);
            root.Children.Add(body);

            _analyticsLicenseGateOverlay = CreatePremiumSurfaceOverlay(root, "analytics");
            Thickness analyticsContentMargin = root.Margin;
            _analyticsLicenseGateOverlay.Margin = new Thickness(
                -analyticsContentMargin.Left,
                -analyticsContentMargin.Top,
                -analyticsContentMargin.Right,
                -analyticsContentMargin.Bottom);
            Grid.SetRow(_analyticsLicenseGateOverlay, 0);
            Grid.SetRowSpan(_analyticsLicenseGateOverlay, 2);
            Panel.SetZIndex(_analyticsLicenseGateOverlay, 1000);
            root.Children.Add(_analyticsLicenseGateOverlay);

            ApplyAnalyticsResponsiveLayout(root.ActualWidth > 0 ? root.ActualWidth : Width);

            RefreshAnalyticsDashboard(GetActiveAccountsSnapshot());
            return root;
        }


        private void OnAnalyticsRootSizeChanged(object sender, SizeChangedEventArgs e)
        {
            ApplyAnalyticsResponsiveLayout(e.NewSize.Width);
        }
        
        private void ApplyAnalyticsResponsiveLayout(double width)
        {
            if (_analyticsTopBarGrid == null || _analyticsBodyGrid == null)
                return;
            if (_isApplyingAnalyticsResponsiveLayout)
                return;

            try
            {
                _isApplyingAnalyticsResponsiveLayout = true;
                double usableWidth = Math.Max(320, width);
                const double hysteresis = 24.0;

                // Keep top header in single-row mode until ~940px before wrapping session range.
                int topMode = ResolveResponsiveThreeBandMode(usableWidth, _analyticsTopLayoutMode, 940, 964, hysteresis);
                if (topMode != _analyticsTopLayoutMode)
                {
                    _analyticsTopLayoutMode = topMode;
                    ConfigureAnalyticsTopLayout(topMode);
                }

                bool bodyWide = ResolveAtOrAboveBreakpoint(usableWidth, _analyticsBodyWideLayout, 1180, hysteresis);
                if (!_analyticsBodyWideLayout.HasValue || _analyticsBodyWideLayout.Value != bodyWide)
                {
                    _analyticsBodyWideLayout = bodyWide;
                    ConfigureAnalyticsBodyLayout(bodyWide);
                }

                double technicalWidth = bodyWide
                    ? Math.Max(320, (usableWidth * 0.62) - 16)
                    : Math.Max(320, usableWidth - 32);
                int unifiedMode = ResolveResponsiveThreeBandMode(technicalWidth, _analyticsUnifiedLayoutMode, 540, 720, hysteresis);
                if (unifiedMode != _analyticsUnifiedLayoutMode)
                {
                    _analyticsUnifiedLayoutMode = unifiedMode;
                    ConfigureAnalyticsUnifiedSignalLayout(unifiedMode);
                }

                bool twoColumns = ResolveAtOrAboveBreakpoint(technicalWidth, _analyticsTimeframeTwoColumnLayout, 720, hysteresis);
                if (!_analyticsTimeframeTwoColumnLayout.HasValue || _analyticsTimeframeTwoColumnLayout.Value != twoColumns)
                {
                    _analyticsTimeframeTwoColumnLayout = twoColumns;
                    ConfigureAnalyticsTimeframeGrid(twoColumns);
                }

                RefreshAnalyticsCardExpansionLayout();
            }
            finally
            {
                _isApplyingAnalyticsResponsiveLayout = false;
            }
        }

        private void ConfigureAnalyticsUnifiedSignalLayout(int mode)
        {
            if (_analyticsUnifiedSignalGrid == null ||
                _analyticsUnifiedBiasCell == null ||
                _analyticsUnifiedExecutionSignalCell == null ||
                _analyticsUnifiedSetupCell == null ||
                _analyticsUnifiedQualityCell == null ||
                _analyticsUnifiedGateCell == null ||
                _analyticsUnifiedExecutionTfCell == null)
            {
                return;
            }

            _analyticsUnifiedSignalGrid.ColumnDefinitions.Clear();
            _analyticsUnifiedSignalGrid.RowDefinitions.Clear();

            if (mode == 0)
            {
                _analyticsUnifiedSignalGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                _analyticsUnifiedSignalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.90, GridUnitType.Star) });
                _analyticsUnifiedSignalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.95, GridUnitType.Star) });
                _analyticsUnifiedSignalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.35, GridUnitType.Star) });
                _analyticsUnifiedSignalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.75, GridUnitType.Star) });
                _analyticsUnifiedSignalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.75, GridUnitType.Star) });
                _analyticsUnifiedSignalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                ConfigureUnifiedSignalCell(_analyticsUnifiedBiasCell, 0, 0, 1, new Thickness(0, 0, 10, 0));
                ConfigureUnifiedSignalCell(_analyticsUnifiedExecutionSignalCell, 0, 1, 1, new Thickness(0, 0, 10, 0));
                ConfigureUnifiedSignalCell(_analyticsUnifiedSetupCell, 0, 2, 1, new Thickness(0, 0, 10, 0));
                ConfigureUnifiedSignalCell(_analyticsUnifiedQualityCell, 0, 3, 1, new Thickness(0, 0, 10, 0));
                ConfigureUnifiedSignalCell(_analyticsUnifiedGateCell, 0, 4, 1, new Thickness(0, 0, 10, 0));
                ConfigureUnifiedSignalCell(_analyticsUnifiedExecutionTfCell, 0, 5, 1, new Thickness(0));
                return;
            }

            if (mode == 1)
            {
                _analyticsUnifiedSignalGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                _analyticsUnifiedSignalGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                _analyticsUnifiedSignalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                _analyticsUnifiedSignalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                _analyticsUnifiedSignalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });

                ConfigureUnifiedSignalCell(_analyticsUnifiedBiasCell, 0, 0, 1, new Thickness(0, 0, 12, 6));
                ConfigureUnifiedSignalCell(_analyticsUnifiedExecutionSignalCell, 0, 1, 1, new Thickness(0, 0, 12, 6));
                ConfigureUnifiedSignalCell(_analyticsUnifiedSetupCell, 0, 2, 1, new Thickness(0, 0, 0, 6));
                ConfigureUnifiedSignalCell(_analyticsUnifiedQualityCell, 1, 0, 1, new Thickness(0, 0, 12, 0));
                ConfigureUnifiedSignalCell(_analyticsUnifiedGateCell, 1, 1, 1, new Thickness(0, 0, 12, 0));
                ConfigureUnifiedSignalCell(_analyticsUnifiedExecutionTfCell, 1, 2, 1, new Thickness(0));
                return;
            }

            _analyticsUnifiedSignalGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _analyticsUnifiedSignalGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _analyticsUnifiedSignalGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _analyticsUnifiedSignalGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _analyticsUnifiedSignalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _analyticsUnifiedSignalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            ConfigureUnifiedSignalCell(_analyticsUnifiedBiasCell, 0, 0, 1, new Thickness(0, 0, 12, 6));
            ConfigureUnifiedSignalCell(_analyticsUnifiedExecutionSignalCell, 0, 1, 1, new Thickness(0, 0, 0, 6));
            ConfigureUnifiedSignalCell(_analyticsUnifiedSetupCell, 1, 0, 2, new Thickness(0, 0, 0, 6));
            ConfigureUnifiedSignalCell(_analyticsUnifiedQualityCell, 2, 0, 1, new Thickness(0, 0, 12, 6));
            ConfigureUnifiedSignalCell(_analyticsUnifiedGateCell, 2, 1, 1, new Thickness(0, 0, 0, 6));
            ConfigureUnifiedSignalCell(_analyticsUnifiedExecutionTfCell, 3, 0, 2, new Thickness(0));
        }

        private static void ConfigureUnifiedSignalCell(
            FrameworkElement cell,
            int row,
            int column,
            int columnSpan,
            Thickness margin)
        {
            if (cell == null)
                return;

            cell.Margin = margin;
            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, column);
            Grid.SetColumnSpan(cell, Math.Max(1, columnSpan));
        }
        
                private void ConfigureAnalyticsTopLayout(int mode)
                {
                    if (_analyticsTopBarGrid == null ||
                        _analyticsInstrumentHost == null ||
                        _analyticsSessionRangeCard == null ||
                        _analyticsCurrentPriceCard == null ||
                        _analyticsSignalCard == null ||
                        _analyticsSessionCard == null)
                    {
                            return;
                    }
        
                    _analyticsTopBarGrid.ColumnDefinitions.Clear();
                    _analyticsTopBarGrid.RowDefinitions.Clear();
        
                    if (mode == 0)
                    {
                        _analyticsTopBarGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                        _analyticsTopBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                        _analyticsTopBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        _analyticsTopBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                        _analyticsTopBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                        _analyticsTopBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        
                        _analyticsInstrumentHost.Margin = new Thickness(0, 0, 8, 0);
                        _analyticsSessionRangeCard.Margin = new Thickness(0, 0, 8, 0);
                        _analyticsCurrentPriceCard.Margin = new Thickness(0);
                        _analyticsSignalCard.Margin = new Thickness(0, 0, 8, 0);
                        _analyticsSessionCard.Margin = new Thickness(0, 0, 8, 0);
        
                        Grid.SetRow(_analyticsInstrumentHost, 0);
                        Grid.SetColumn(_analyticsInstrumentHost, 0);
                        Grid.SetColumnSpan(_analyticsInstrumentHost, 1);
                        Grid.SetRow(_analyticsSessionRangeCard, 0);
                        Grid.SetColumn(_analyticsSessionRangeCard, 1);
                        Grid.SetColumnSpan(_analyticsSessionRangeCard, 1);
                        Grid.SetRow(_analyticsSessionCard, 0);
                        Grid.SetColumn(_analyticsSessionCard, 2);
                        Grid.SetColumnSpan(_analyticsSessionCard, 1);
                        Grid.SetRow(_analyticsSignalCard, 0);
                        Grid.SetColumn(_analyticsSignalCard, 3);
                        Grid.SetColumnSpan(_analyticsSignalCard, 1);
                        Grid.SetRow(_analyticsCurrentPriceCard, 0);
                        Grid.SetColumn(_analyticsCurrentPriceCard, 4);
                        Grid.SetColumnSpan(_analyticsCurrentPriceCard, 1);
        
                            return;
                    }
        
                    if (mode == 1)
                    {
                        _analyticsTopBarGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                        _analyticsTopBarGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                        _analyticsTopBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                        _analyticsTopBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                        _analyticsTopBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                        _analyticsTopBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        
                        _analyticsInstrumentHost.Margin = new Thickness(0, 0, 8, 0);
                        _analyticsSessionRangeCard.Margin = new Thickness(0, 8, 0, 0);
                        _analyticsCurrentPriceCard.Margin = new Thickness(0);
                        _analyticsSignalCard.Margin = new Thickness(0, 0, 8, 0);
                        _analyticsSessionCard.Margin = new Thickness(0, 0, 8, 0);
        
                        Grid.SetRow(_analyticsInstrumentHost, 0);
                        Grid.SetColumn(_analyticsInstrumentHost, 0);
                        Grid.SetColumnSpan(_analyticsInstrumentHost, 1);
                        Grid.SetRow(_analyticsSessionCard, 0);
                        Grid.SetColumn(_analyticsSessionCard, 1);
                        Grid.SetColumnSpan(_analyticsSessionCard, 1);
                        Grid.SetRow(_analyticsSignalCard, 0);
                        Grid.SetColumn(_analyticsSignalCard, 2);
                        Grid.SetColumnSpan(_analyticsSignalCard, 1);
                        Grid.SetRow(_analyticsCurrentPriceCard, 0);
                        Grid.SetColumn(_analyticsCurrentPriceCard, 3);
                        Grid.SetColumnSpan(_analyticsCurrentPriceCard, 1);
                        Grid.SetRow(_analyticsSessionRangeCard, 1);
                        Grid.SetColumn(_analyticsSessionRangeCard, 0);
                        Grid.SetColumnSpan(_analyticsSessionRangeCard, 4);
        
                            return;
                    }
        
                    _analyticsTopBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    _analyticsTopBarGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    _analyticsTopBarGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    _analyticsTopBarGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    _analyticsTopBarGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    _analyticsTopBarGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        
                    _analyticsInstrumentHost.Margin = new Thickness(0, 0, 0, 8);
                    _analyticsSessionRangeCard.Margin = new Thickness(0, 0, 0, 8);
                    _analyticsCurrentPriceCard.Margin = new Thickness(0, 0, 0, 8);
                    _analyticsSignalCard.Margin = new Thickness(0, 0, 0, 8);
                    _analyticsSessionCard.Margin = new Thickness(0);
        
                    Grid.SetColumn(_analyticsInstrumentHost, 0);
                    Grid.SetRow(_analyticsInstrumentHost, 0);
                    Grid.SetColumnSpan(_analyticsInstrumentHost, 1);
                    Grid.SetColumn(_analyticsSessionRangeCard, 0);
                    Grid.SetRow(_analyticsSessionRangeCard, 1);
                    Grid.SetColumnSpan(_analyticsSessionRangeCard, 1);
                    Grid.SetColumn(_analyticsSessionCard, 0);
                    Grid.SetRow(_analyticsSessionCard, 2);
                    Grid.SetColumnSpan(_analyticsSessionCard, 1);
                    Grid.SetColumn(_analyticsSignalCard, 0);
                    Grid.SetRow(_analyticsSignalCard, 3);
                    Grid.SetColumnSpan(_analyticsSignalCard, 1);
                    Grid.SetColumn(_analyticsCurrentPriceCard, 0);
                    Grid.SetRow(_analyticsCurrentPriceCard, 4);
                    Grid.SetColumnSpan(_analyticsCurrentPriceCard, 1);
        
                }
        
                private void ConfigureAnalyticsBodyLayout(bool wide)
                {
                    if (_analyticsBodyGrid == null || _analyticsTimeframeGrid == null || _analyticsFundamentalCard == null)
                            return;
        
                    _analyticsBodyGrid.ColumnDefinitions.Clear();
                    _analyticsBodyGrid.RowDefinitions.Clear();
        
                    if (wide)
                    {
                        _analyticsBodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
                        _analyticsBodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        _analyticsBodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        
                        _analyticsTimeframeGrid.Margin = new Thickness(0, 0, 8, 0);
                        Grid.SetColumn(_analyticsTimeframeGrid, 0);
                        Grid.SetRow(_analyticsTimeframeGrid, 0);
                        Grid.SetColumn(_analyticsFundamentalCard, 1);
                        Grid.SetRow(_analyticsFundamentalCard, 0);
                    }
                    else
                    {
                        _analyticsBodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        _analyticsBodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                        _analyticsBodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        
                        _analyticsTimeframeGrid.Margin = new Thickness(0, 0, 0, 8);
                        Grid.SetColumn(_analyticsTimeframeGrid, 0);
                        Grid.SetRow(_analyticsTimeframeGrid, 0);
                        Grid.SetColumn(_analyticsFundamentalCard, 0);
                        Grid.SetRow(_analyticsFundamentalCard, 1);
                    }
                }
        
                private void ConfigureAnalyticsTimeframeGrid(bool twoColumns)
                {
                    if (_analyticsTimeframeGrid == null ||
                        _analyticsUnifiedSignalCard == null ||
                        _analyticsOneMinuteCard == null ||
                        _analyticsFiveMinuteCard == null ||
                        _analyticsFifteenMinuteCard == null ||
                        _analyticsSixtyMinuteCard == null)
                    {
                            return;
                    }
        
                    _analyticsTimeframeGrid.ColumnDefinitions.Clear();
                    _analyticsTimeframeGrid.RowDefinitions.Clear();
        
                    if (twoColumns)
                    {
                        _analyticsTimeframeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        _analyticsTimeframeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        _analyticsTimeframeGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                        _analyticsTimeframeGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                        _analyticsTimeframeGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                        _analyticsUnifiedSignalCard.Margin = new Thickness(0, 0, 0, 8);
                        _analyticsOneMinuteCard.Margin = new Thickness(0, 0, 8, 8);
                        _analyticsFiveMinuteCard.Margin = new Thickness(0, 0, 0, 8);
                        _analyticsFifteenMinuteCard.Margin = new Thickness(0, 0, 8, 0);
                        _analyticsSixtyMinuteCard.Margin = new Thickness(0);

                        Grid.SetColumn(_analyticsUnifiedSignalCard, 0);
                        Grid.SetRow(_analyticsUnifiedSignalCard, 0);
                        Grid.SetColumnSpan(_analyticsUnifiedSignalCard, 2);
                        Grid.SetColumn(_analyticsOneMinuteCard, 0);
                        Grid.SetRow(_analyticsOneMinuteCard, 1);
                        Grid.SetColumn(_analyticsFiveMinuteCard, 1);
                        Grid.SetRow(_analyticsFiveMinuteCard, 1);
                        Grid.SetColumn(_analyticsFifteenMinuteCard, 0);
                        Grid.SetRow(_analyticsFifteenMinuteCard, 2);
                        Grid.SetColumn(_analyticsSixtyMinuteCard, 1);
                        Grid.SetRow(_analyticsSixtyMinuteCard, 2);
                            return;
                    }

                    _analyticsTimeframeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    _analyticsTimeframeGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    _analyticsTimeframeGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    _analyticsTimeframeGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    _analyticsTimeframeGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    _analyticsTimeframeGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                    _analyticsUnifiedSignalCard.Margin = new Thickness(0, 0, 0, 8);
                    _analyticsOneMinuteCard.Margin = new Thickness(0, 0, 0, 8);
                    _analyticsFiveMinuteCard.Margin = new Thickness(0, 0, 0, 8);
                    _analyticsFifteenMinuteCard.Margin = new Thickness(0, 0, 0, 8);
                    _analyticsSixtyMinuteCard.Margin = new Thickness(0);

                    Grid.SetColumn(_analyticsUnifiedSignalCard, 0);
                    Grid.SetRow(_analyticsUnifiedSignalCard, 0);
                    Grid.SetColumnSpan(_analyticsUnifiedSignalCard, 1);
                    Grid.SetColumn(_analyticsOneMinuteCard, 0);
                    Grid.SetRow(_analyticsOneMinuteCard, 1);
                    Grid.SetColumn(_analyticsFiveMinuteCard, 0);
                    Grid.SetRow(_analyticsFiveMinuteCard, 2);
                    Grid.SetColumn(_analyticsFifteenMinuteCard, 0);
                    Grid.SetRow(_analyticsFifteenMinuteCard, 3);
                    Grid.SetColumn(_analyticsSixtyMinuteCard, 0);
                    Grid.SetRow(_analyticsSixtyMinuteCard, 4);
                }

                private Border CreateAnalyticsUnifiedSignalBar(FrameworkElement context)
                {
                    var card = CreateAnalyticsCard(context, new Thickness(0, 0, 0, 8), new Thickness(10, 8, 10, 8));
                    card.MinHeight = 66;
                    card.VerticalAlignment = VerticalAlignment.Top;

                    var grid = new Grid
                    {
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    _analyticsUnifiedSignalGrid = grid;

                    var biasCell = CreateAnalyticsUnifiedSignalCell(
                        "analytics.unified.bias",
                        "Bias",
                        "Neutral",
                        out _analyticsUnifiedBiasValueText);
                    _analyticsUnifiedBiasCell = biasCell;
                    grid.Children.Add(biasCell);

                    var executionSignalCell = CreateAnalyticsUnifiedSignalCell(
                        "analytics.unified.exec_signal",
                        "Glitch TF Score",
                        "Neutral",
                        out _analyticsUnifiedExecutionSignalValueText);
                    _analyticsUnifiedExecutionSignalCell = executionSignalCell;
                    grid.Children.Add(executionSignalCell);

                    var setupCell = CreateAnalyticsUnifiedSignalCell(
                        "analytics.unified.setup",
                        "Setup",
                        "Flat",
                        out _analyticsUnifiedSetupValueText);
                    _analyticsUnifiedSetupCell = setupCell;
                    grid.Children.Add(setupCell);

                    var qualityCell = CreateAnalyticsUnifiedSignalCell(
                        "analytics.unified.quality",
                        "Quality",
                        "0%",
                        out _analyticsUnifiedQualityValueText);
                    _analyticsUnifiedQualityCell = qualityCell;
                    grid.Children.Add(qualityCell);

                    var gateCell = CreateAnalyticsUnifiedSignalCell(
                        "analytics.unified.action",
                        "Action",
                        "No Trade",
                        out _analyticsUnifiedGateValueText);
                    _analyticsUnifiedGateCell = gateCell;
                    grid.Children.Add(gateCell);

                    var executionTfCell = new StackPanel
                    {
                        Orientation = Orientation.Vertical,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Margin = new Thickness(0),
                        MinWidth = 110
                    };
                    var executionTfLabel = new TextBlock
                    {
                        Text = L("analytics.unified.execution_tf", "Execution TF"),
                        FontSize = 10,
                        FontWeight = FontWeights.Medium
                    };
                    BindLocalizedText(executionTfLabel, "analytics.unified.execution_tf", "Execution TF");
                    ApplySkinResource(executionTfLabel, TextBlock.ForegroundProperty, "FontHeaderLevel4Brush", "FontControlBrush", "FontTableBrush");
                    executionTfCell.Children.Add(executionTfLabel);

                    _analyticsUnifiedExecutionTfCombo = new ComboBox
                    {
                        MinWidth = 88,
                        Height = 24,
                        Margin = new Thickness(0, 2, 0, 0),
                        IsEditable = false,
                        IsTextSearchEnabled = false,
                        HorizontalContentAlignment = HorizontalAlignment.Left
                    };
                    _analyticsUnifiedExecutionTfCombo.Style = CreateGroupMasterComboBoxStyle(context);
                    _analyticsUnifiedExecutionTfCombo.Items.Add(new ComboBoxItem { Content = Lf("analytics.timeframe.min_format", "{0} min", 1), Tag = 1 });
                    _analyticsUnifiedExecutionTfCombo.Items.Add(new ComboBoxItem { Content = Lf("analytics.timeframe.min_format", "{0} min", 5), Tag = 5 });
                    _analyticsUnifiedExecutionTfCombo.Items.Add(new ComboBoxItem { Content = Lf("analytics.timeframe.min_format", "{0} min", 15), Tag = 15 });
                    _analyticsUnifiedExecutionTfCombo.Items.Add(new ComboBoxItem { Content = Lf("analytics.timeframe.min_format", "{0} min", 60), Tag = 60 });
                    RegisterLocalizationBinding(() =>
                    {
                        if (_analyticsUnifiedExecutionTfCombo == null)
                            return;

                        foreach (object item in _analyticsUnifiedExecutionTfCombo.Items)
                        {
                            var comboItem = item as ComboBoxItem;
                            if (comboItem == null)
                                continue;

                            int minutes = 0;
                            if (comboItem.Tag is int tagMinutes)
                                minutes = tagMinutes;
                            else
                                int.TryParse((comboItem.Tag ?? string.Empty).ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out minutes);

                            if (minutes > 0)
                                comboItem.Content = Lf("analytics.timeframe.min_format", "{0} min", minutes);
                        }
                    });
                    _analyticsUnifiedExecutionTfCombo.SelectionChanged += OnAnalyticsUnifiedExecutionTimeframeSelectionChanged;
                    executionTfCell.Children.Add(_analyticsUnifiedExecutionTfCombo);
                    SelectUnifiedExecutionTimeframeComboItem(_analyticsUnifiedExecutionTimeframeMinutes);

                    _analyticsUnifiedExecutionTfCell = executionTfCell;
                    grid.Children.Add(executionTfCell);
                    ConfigureAnalyticsUnifiedSignalLayout(_analyticsUnifiedLayoutMode < 0 ? 0 : _analyticsUnifiedLayoutMode);

                    card.Child = grid;
                    return card;
                }

                private StackPanel CreateAnalyticsUnifiedSignalCell(
                    string labelKey,
                    string labelFallback,
                    string valueFallback,
                    out TextBlock valueText)
                {
                    var stack = new StackPanel
                    {
                        Orientation = Orientation.Vertical,
                        Margin = new Thickness(0),
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    var labelText = new TextBlock
                    {
                        Text = L(labelKey, labelFallback),
                        FontSize = 10,
                        FontWeight = FontWeights.Medium
                    };
                    BindLocalizedText(labelText, labelKey, labelFallback);
                    ApplySkinResource(labelText, TextBlock.ForegroundProperty, "FontHeaderLevel4Brush", "FontControlBrush", "FontTableBrush");
                    stack.Children.Add(labelText);

                    valueText = new TextBlock
                    {
                        Text = valueFallback,
                        FontSize = 13,
                        FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(0, 2, 0, 0),
                        TextWrapping = TextWrapping.Wrap
                    };
                    ApplySkinResource(valueText, TextBlock.ForegroundProperty, "FontControlBrush", "FontTableBrush");
                    stack.Children.Add(valueText);

                    return stack;
                }

                private void OnAnalyticsUnifiedExecutionTimeframeSelectionChanged(object sender, SelectionChangedEventArgs e)
                {
                    int selectedMinutes = ResolveSelectedExecutionTimeframeMinutes();
                    if (selectedMinutes == _analyticsUnifiedExecutionTimeframeMinutes)
                        return;

                    _analyticsUnifiedExecutionTimeframeMinutes = selectedMinutes;
                    RefreshAnalyticsDashboard(GetActiveAccountsSnapshot());
                }

                private void SelectUnifiedExecutionTimeframeComboItem(int minutes)
                {
                    if (_analyticsUnifiedExecutionTfCombo == null)
                        return;

                    foreach (object item in _analyticsUnifiedExecutionTfCombo.Items)
                    {
                        var comboItem = item as ComboBoxItem;
                        if (comboItem == null)
                            continue;

                        int tagMinutes = 0;
                        if (comboItem.Tag is int parsedTag)
                            tagMinutes = parsedTag;
                        else
                            int.TryParse((comboItem.Tag ?? string.Empty).ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out tagMinutes);

                        if (tagMinutes == minutes)
                        {
                            _analyticsUnifiedExecutionTfCombo.SelectedItem = comboItem;
                            return;
                        }
                    }

                    if (_analyticsUnifiedExecutionTfCombo.Items.Count > 0)
                        _analyticsUnifiedExecutionTfCombo.SelectedIndex = 1;
                }

                private int ResolveSelectedExecutionTimeframeMinutes()
                {
                    if (_analyticsUnifiedExecutionTfCombo?.SelectedItem is ComboBoxItem selectedItem)
                    {
                        if (selectedItem.Tag is int selectedMinutes)
                            return NormalizeExecutionTimeframeMinutes(selectedMinutes);

                        if (int.TryParse((selectedItem.Tag ?? string.Empty).ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                            return NormalizeExecutionTimeframeMinutes(parsed);
                    }

                    return NormalizeExecutionTimeframeMinutes(_analyticsUnifiedExecutionTimeframeMinutes);
                }

                private static int NormalizeExecutionTimeframeMinutes(int minutes)
                {
                    if (minutes == 1 || minutes == 5 || minutes == 15 || minutes == 60)
                        return minutes;
                    return 5;
                }
        
                private Border CreateAnalyticsCompositeTimeframeCard(FrameworkElement context, int timeframeMinutes, Thickness margin)
                {
                    var card = CreateAnalyticsCard(context, margin, new Thickness(10));
                    card.MinWidth = 300;
                    card.MinHeight = 246;
                    card.VerticalAlignment = VerticalAlignment.Stretch;

                    var shell = new Grid();
                    shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                    var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 2) };
                    var title = new TextBlock
                    {
                        Text = Lf("analytics.timeframe.min_format", "{0} min", timeframeMinutes),
                        FontSize = 18,
                        FontWeight = FontWeights.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    RegisterLocalizationBinding(() => title.Text = Lf("analytics.timeframe.min_format", "{0} min", timeframeMinutes));
                    ApplySkinResource(title, TextBlock.ForegroundProperty, "FontHeaderLevel4Brush", "FontControlBrush", "FontTableBrush");
                    headerGrid.Children.Add(title);

                    var toggleButton = new Button
                    {
                        Width = 24,
                        Height = 24,
                        MinWidth = 24,
                        MinHeight = 24,
                        MaxWidth = 24,
                        MaxHeight = 24,
                        Padding = new Thickness(0),
                        Margin = new Thickness(0, 0, 0, 2),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Top,
                        Content = "+",
                        Style = CreateAnalyticsCardToggleButtonStyle(context)
                    };
                    toggleButton.Click += (_, __) => ToggleAnalyticsCardExpansion(timeframeMinutes);
                    headerGrid.Children.Add(toggleButton);
                    Grid.SetRow(headerGrid, 0);
                    shell.Children.Add(headerGrid);

                    var bodyHost = new Grid();
                    Grid.SetRow(bodyHost, 1);
                    shell.Children.Add(bodyHost);

                    var layout = new StackPanel
                    {
                        Orientation = Orientation.Vertical,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    bodyHost.Children.Add(layout);

                    var avgRow = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 2, 0, 0)
                    };
                    var avgLabel = new TextBlock
                    {
                        Text = L("analytics.avg_prefix", "Avg.") + " ",
                        FontWeight = FontWeights.Medium
                    };
                    BindLocalizedText(avgLabel, "analytics.avg_prefix", "Avg.");
                    RegisterLocalizationBinding(() =>
                    {
                        avgLabel.Text = L("analytics.avg_prefix", "Avg.") + " ";
                    });
                    ApplySkinResource(avgLabel, TextBlock.ForegroundProperty, "FontControlBrush", "FontTableBrush");
                    avgRow.Children.Add(avgLabel);
        
                    var avgValue = new TextBlock
                    {
                        Text = "-",
                        FontWeight = FontWeights.Medium
                    };
                    ApplySkinResource(avgValue, TextBlock.ForegroundProperty, "FontControlBrush", "FontTableBrush");
                    avgRow.Children.Add(avgValue);
                    layout.Children.Add(avgRow);
        
                    var dialCanvas = new Canvas
                    {
                        Width = 160,
                        Height = 90,
                        Margin = new Thickness(0, 6, 0, 4),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        ClipToBounds = false
                    };
        
                    AddAnalyticsDialBands(dialCanvas);
        
                    var needle = new System.Windows.Shapes.Line
                    {
                        X1 = 80,
                        Y1 = 72,
                        X2 = 80,
                        Y2 = 26,
                        Stroke = Brushes.White,
                        StrokeThickness = 2
                    };
                    var needleRotation = new RotateTransform(0, 80, 72);
                    needle.RenderTransform = needleRotation;
                    dialCanvas.Children.Add(needle);
        
                    var hub = new System.Windows.Shapes.Ellipse
                    {
                        Width = 6,
                        Height = 6,
                        Fill = Brushes.White
                    };
                    Canvas.SetLeft(hub, 77);
                    Canvas.SetTop(hub, 69);
                    dialCanvas.Children.Add(hub);
        
                    var sellLabel = new TextBlock { Text = L("analytics.dial.sell", "Sell"), FontSize = 10 };
                    BindLocalizedText(sellLabel, "analytics.dial.sell", "Sell");
                    ApplySkinResource(sellLabel, TextBlock.ForegroundProperty, "FontControlBrush", "FontTableBrush");
                    sellLabel.Width = 40;
                    sellLabel.TextAlignment = TextAlignment.Center;
                    Canvas.SetLeft(sellLabel, -34);
                    Canvas.SetTop(sellLabel, 58);
                    dialCanvas.Children.Add(sellLabel);
        
                    var buyLabel = new TextBlock { Text = L("analytics.dial.buy", "Buy"), FontSize = 10 };
                    BindLocalizedText(buyLabel, "analytics.dial.buy", "Buy");
                    ApplySkinResource(buyLabel, TextBlock.ForegroundProperty, "FontControlBrush", "FontTableBrush");
                    buyLabel.Width = 40;
                    buyLabel.TextAlignment = TextAlignment.Center;
                    Canvas.SetLeft(buyLabel, 154);
                    Canvas.SetTop(buyLabel, 58);
                    dialCanvas.Children.Add(buyLabel);
        
                    layout.Children.Add(dialCanvas);
        
                    var scoreText = new TextBlock
                    {
                        Text = "0.00",
                        FontSize = 18,
                        FontWeight = FontWeights.SemiBold,
                        HorizontalAlignment = HorizontalAlignment.Center
                    };
                    ApplySkinResource(scoreText, TextBlock.ForegroundProperty, "FontControlBrush", "FontTableBrush");
                    layout.Children.Add(scoreText);
        
                    var signalText = new TextBlock
                    {
                        Text = L("analytics.signal.neutral", "Neutral"),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        FontWeight = FontWeights.SemiBold
                    };
                    ApplySkinResource(signalText, TextBlock.ForegroundProperty, "FontControlBrush", "FontTableBrush");
                    layout.Children.Add(signalText);
        
                    var regimeText = new TextBlock
                    {
                        Text = "-",
                        Margin = new Thickness(0, 2, 0, 6),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        TextAlignment = TextAlignment.Center,
                        TextWrapping = TextWrapping.Wrap
                    };
                    ApplySkinResource(regimeText, TextBlock.ForegroundProperty, "FontControlBrush", "FontTableBrush");
                    layout.Children.Add(regimeText);

                    var detailsStack = new StackPanel
                    {
                        Orientation = Orientation.Vertical,
                        HorizontalAlignment = HorizontalAlignment.Center
                    };

                    var foldDivider = new Border
                    {
                        Height = 1,
                        Margin = new Thickness(10, 0, 10, 8)
                    };
                    ApplySkinResource(foldDivider, Border.BackgroundProperty, "BorderThinBrush", "TabControlBorderBrush", "GridHeaderHighlight");
                    detailsStack.Children.Add(foldDivider);

                    var indicatorBlock = new StackPanel
                    {
                        Orientation = Orientation.Vertical,
                        HorizontalAlignment = HorizontalAlignment.Center
                    };
                    indicatorBlock.Children.Add(CreateAnalyticsFactorRow(context, "analytics.factor.raw", "Raw", out AnalyticsFactorVisual rawFactor));
                    indicatorBlock.Children.Add(CreateAnalyticsFactorRow(context, "analytics.factor.ema", "EMA", out AnalyticsFactorVisual emaFactor));
                    indicatorBlock.Children.Add(CreateAnalyticsFactorRow(context, "analytics.factor.rsi", "RSI", out AnalyticsFactorVisual rsiFactor));
                    indicatorBlock.Children.Add(CreateAnalyticsFactorRow(context, "analytics.factor.stoch", "Stoch", out AnalyticsFactorVisual stochFactor));
                    indicatorBlock.Children.Add(CreateAnalyticsFactorRow(context, "analytics.factor.z_score", "Z-Score", out AnalyticsFactorVisual zFactor));
                    indicatorBlock.Children.Add(CreateAnalyticsFactorRow(context, "analytics.factor.osc", "Osc", out AnalyticsFactorVisual oscFactor));
                    indicatorBlock.Children.Add(CreateAnalyticsFactorRow(context, "analytics.factor.ma", "MA", out AnalyticsFactorVisual maFactor));
                    indicatorBlock.Children.Add(CreateAnalyticsFactorRow(context, "analytics.factor.order_flow", "OF", out AnalyticsFactorVisual orderFlowFactor));
                    indicatorBlock.Children.Add(CreateAnalyticsFactorRow(context, "analytics.factor.agreement", "Agree", out AnalyticsFactorVisual agreementFactor));
                    detailsStack.Children.Add(indicatorBlock);

                    var adxAtrValue = new TextBlock();

                    var detailsHost = new Border
                    {
                        Margin = new Thickness(0, 8, 0, 0),
                        ClipToBounds = true,
                        MaxHeight = 0,
                        Opacity = 0,
                        Visibility = Visibility.Collapsed,
                        Child = detailsStack
                    };
                    layout.Children.Add(detailsHost);
        
                    _analyticsDialVisuals[timeframeMinutes] = new AnalyticsDialVisual
                    {
                        NeedleRotation = needleRotation,
                        ScoreText = scoreText,
                        SignalText = signalText
                    };
        
                    _analyticsTimeframeVisuals[timeframeMinutes] = new AnalyticsTimeframeMetricVisual
                    {
                        SignalText = signalText,
                        AveragePriceValueText = avgValue,
                        AveragePriceHintText = new TextBlock(),
                        AtrValueText = regimeText,
                        AtrHintText = new TextBlock(),
                        AdxValueText = adxAtrValue,
                        AdxHintText = new TextBlock(),
                        RawFactor = rawFactor,
                        EmaFactor = emaFactor,
                        RsiFactor = rsiFactor,
                        StochFactor = stochFactor,
                        ZFactor = zFactor,
                        OscillatorFactor = oscFactor,
                        MaFactor = maFactor,
                        OrderFlowFactor = orderFlowFactor,
                        AgreementFactor = agreementFactor
                    };
                    SetAnalyticsFactorVisibility(orderFlowFactor, false);

                    _analyticsCardExpansionVisuals[timeframeMinutes] = new AnalyticsCardExpansionVisual
                    {
                        Card = card,
                        ToggleButton = toggleButton,
                        DetailsHost = detailsHost,
                        DetailsContent = detailsStack
                    };
                    ApplyAnalyticsCardExpansionState(timeframeMinutes, _analyticsExpandedCardStates.TryGetValue(timeframeMinutes, out bool isExpanded) && isExpanded, false);

                    card.Child = shell;
                    return card;
                }
        
                private Border CreateAnalyticsDialCard(FrameworkElement context, int timeframeMinutes, Thickness margin)
                {
                    var card = CreateAnalyticsCard(context, margin, new Thickness(10));
        
                    var layout = new Grid();
                    layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        
                    var title = new TextBlock
                    {
                        Text = Lf("analytics.timeframe.min_format", "{0} min", timeframeMinutes),
                        FontWeight = FontWeights.SemiBold,
                        HorizontalAlignment = HorizontalAlignment.Center
                    };
                    RegisterLocalizationBinding(() => title.Text = Lf("analytics.timeframe.min_format", "{0} min", timeframeMinutes));
                    ApplySkinResource(title, TextBlock.ForegroundProperty, "FontHeaderLevel4Brush", "FontControlBrush", "FontTableBrush");
                    Grid.SetRow(title, 0);
                    layout.Children.Add(title);
        
                    var dialCanvas = new Canvas
                    {
                        Width = 160,
                        Height = 90,
                        Margin = new Thickness(0, 6, 0, 4),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        ClipToBounds = false
                    };
        
                    AddAnalyticsDialBands(dialCanvas);
        
                    var needle = new System.Windows.Shapes.Line
                    {
                        X1 = 80,
                        Y1 = 72,
                        X2 = 80,
                        Y2 = 26,
                        Stroke = Brushes.White,
                        StrokeThickness = 2
                    };
                    var needleRotation = new RotateTransform(0, 80, 72);
                    needle.RenderTransform = needleRotation;
                    dialCanvas.Children.Add(needle);
        
                    var hub = new System.Windows.Shapes.Ellipse
                    {
                        Width = 6,
                        Height = 6,
                        Fill = Brushes.White
                    };
                    Canvas.SetLeft(hub, 77);
                    Canvas.SetTop(hub, 69);
                    dialCanvas.Children.Add(hub);
        
                    var sellLabel = new TextBlock { Text = L("analytics.dial.sell", "Sell"), FontSize = 10 };
                    BindLocalizedText(sellLabel, "analytics.dial.sell", "Sell");
                    ApplySkinResource(sellLabel, TextBlock.ForegroundProperty, "FontControlBrush", "FontTableBrush");
                    sellLabel.Width = 40;
                    sellLabel.TextAlignment = TextAlignment.Center;
                    Canvas.SetLeft(sellLabel, -34);
                    Canvas.SetTop(sellLabel, 58);
                    dialCanvas.Children.Add(sellLabel);
        
                    var buyLabel = new TextBlock { Text = L("analytics.dial.buy", "Buy"), FontSize = 10 };
                    BindLocalizedText(buyLabel, "analytics.dial.buy", "Buy");
                    ApplySkinResource(buyLabel, TextBlock.ForegroundProperty, "FontControlBrush", "FontTableBrush");
                    buyLabel.Width = 40;
                    buyLabel.TextAlignment = TextAlignment.Center;
                    Canvas.SetLeft(buyLabel, 154);
                    Canvas.SetTop(buyLabel, 58);
                    dialCanvas.Children.Add(buyLabel);
        
                    Grid.SetRow(dialCanvas, 1);
                    layout.Children.Add(dialCanvas);
        
                    var scoreText = new TextBlock
                    {
                        Text = "0.00",
                        FontSize = 18,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        FontWeight = FontWeights.SemiBold
                    };
                    ApplySkinResource(scoreText, TextBlock.ForegroundProperty, "FontControlBrush", "FontTableBrush");
                    Grid.SetRow(scoreText, 2);
                    layout.Children.Add(scoreText);
        
                    var signalText = new TextBlock
                    {
                        Text = L("analytics.signal.neutral", "Neutral"),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 2, 0, 0)
                    };
                    ApplySkinResource(signalText, TextBlock.ForegroundProperty, "FontControlBrush", "FontTableBrush");
                    Grid.SetRow(signalText, 3);
                    layout.Children.Add(signalText);
        
                    _analyticsDialVisuals[timeframeMinutes] = new AnalyticsDialVisual
                    {
                        NeedleRotation = needleRotation,
                        ScoreText = scoreText,
                        SignalText = signalText
                    };
        
                    card.Child = layout;
                    return card;
                }
        
                private static System.Windows.Shapes.Path CreateAnalyticsArcPath(Point start, Point end, Size size, Brush stroke, double thickness)
                {
                    var figure = new PathFigure { StartPoint = start };
                    figure.Segments.Add(new ArcSegment
                    {
                        Point = end,
                        Size = size,
                        SweepDirection = SweepDirection.Clockwise,
                        IsLargeArc = false
                    });
        
                    var geometry = new PathGeometry();
                    geometry.Figures.Add(figure);
        
                    return new System.Windows.Shapes.Path
                    {
                        Data = geometry,
                        Stroke = stroke,
                        StrokeThickness = thickness,
                        SnapsToDevicePixels = true
                    };
                }

                private static SolidColorBrush CreateFrozenAnalyticsDialBrush(byte red, byte green, byte blue)
                {
                    var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
                    brush.Freeze();
                    return brush;
                }

                private static Point MapAnalyticsDialPoint(double score)
                {
                    double clamped = Math.Max(-1.0, Math.Min(1.0, score));
                    double normalized = (clamped + 1.0) * 0.5;
                    double radians = Math.PI * (1.0 - normalized);
                    double x = AnalyticsDialCenterX + (AnalyticsDialRadius * Math.Cos(radians));
                    double y = AnalyticsDialCenterY - (AnalyticsDialRadius * Math.Sin(radians));
                    return new Point(x, y);
                }

                private static void AddAnalyticsDialSegment(Canvas dialCanvas, double fromScore, double toScore, Brush brush, double thickness)
                {
                    if (dialCanvas == null || brush == null)
                        return;

                    double startScore = Math.Max(-1.0, Math.Min(1.0, fromScore));
                    double endScore = Math.Max(-1.0, Math.Min(1.0, toScore));
                    if (endScore <= startScore)
                        return;

                    dialCanvas.Children.Add(
                        CreateAnalyticsArcPath(
                            MapAnalyticsDialPoint(startScore),
                            MapAnalyticsDialPoint(endScore),
                            new Size(AnalyticsDialRadius, AnalyticsDialRadius),
                            brush,
                            thickness));
                }

                private static void AddAnalyticsDialBands(Canvas dialCanvas)
                {
                    const double dialThickness = 7.0;

                    // Sell side: strong -> medium -> weak
                    AddAnalyticsDialSegment(dialCanvas, -1.00, -0.60, AnalyticsDialSellStrongBrush, dialThickness);
                    AddAnalyticsDialSegment(dialCanvas, -0.60, -0.30, AnalyticsDialSellMidBrush, dialThickness);
                    AddAnalyticsDialSegment(dialCanvas, -0.30, -AnalyticsDialNeutralHalfWidth, AnalyticsDialSellWeakBrush, dialThickness);

                    // Neutral center band
                    AddAnalyticsDialSegment(dialCanvas, -AnalyticsDialNeutralHalfWidth, AnalyticsDialNeutralHalfWidth, AnalyticsDialNeutralBrush, dialThickness);

                    // Buy side: weak -> medium -> strong
                    AddAnalyticsDialSegment(dialCanvas, AnalyticsDialNeutralHalfWidth, 0.30, AnalyticsDialBuyWeakBrush, dialThickness);
                    AddAnalyticsDialSegment(dialCanvas, 0.30, 0.60, AnalyticsDialBuyMidBrush, dialThickness);
                    AddAnalyticsDialSegment(dialCanvas, 0.60, 1.00, AnalyticsDialBuyStrongBrush, dialThickness);
                }
        
                private Border CreateAnalyticsTimeframeCard(FrameworkElement context, int timeframeMinutes)
                {
                    var card = CreateAnalyticsCard(context, new Thickness(0), new Thickness(10));
        
                    var stack = new StackPanel { Orientation = Orientation.Vertical };
                    var headerRow = new Grid();
                    headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        
                    var title = new TextBlock
                    {
                        Text = Lf("analytics.timeframe.avg_price_title", "{0} min Avg Price", timeframeMinutes),
                        FontWeight = FontWeights.SemiBold
                    };
                    RegisterLocalizationBinding(() => title.Text = Lf("analytics.timeframe.avg_price_title", "{0} min Avg Price", timeframeMinutes));
                    ApplySkinResource(title, TextBlock.ForegroundProperty, "FontHeaderLevel4Brush", "FontControlBrush", "FontTableBrush");
                    headerRow.Children.Add(title);

                    var signalText = new TextBlock
                    {
                        Text = L("analytics.signal.neutral", "Neutral") + " (0.00)",
                        HorizontalAlignment = HorizontalAlignment.Right
                    };
                    ApplySkinResource(signalText, TextBlock.ForegroundProperty, "FontControlBrush", "FontTableBrush");
                    Grid.SetColumn(signalText, 1);
                    headerRow.Children.Add(signalText);
                    stack.Children.Add(headerRow);
        
                    stack.Children.Add(CreateAnalyticsMetricLine("analytics.metric.avg_price", "Avg Price", out TextBlock avgValue, out TextBlock avgHint));
                    stack.Children.Add(CreateAnalyticsMetricLine("analytics.regime", "Regime", out TextBlock atrValue, out TextBlock atrHint));
                    stack.Children.Add(CreateAnalyticsMetricLine("analytics.adx_atr_row", "ADX(14) | ATR(14)", out TextBlock adxValue, out TextBlock adxHint));
        
                    stack.Children.Add(CreateAnalyticsFactorRow(context, "analytics.factor.ema", "EMA", out AnalyticsFactorVisual emaFactor));
                    stack.Children.Add(CreateAnalyticsFactorRow(context, "analytics.factor.rsi", "RSI", out AnalyticsFactorVisual rsiFactor));
                    stack.Children.Add(CreateAnalyticsFactorRow(context, "analytics.factor.stoch", "Stoch", out AnalyticsFactorVisual stochFactor));
                    stack.Children.Add(CreateAnalyticsFactorRow(context, "analytics.factor.z_score", "Z-Score", out AnalyticsFactorVisual zFactor));
                    stack.Children.Add(CreateAnalyticsFactorRow(context, "analytics.factor.order_flow", "OF", out AnalyticsFactorVisual orderFlowFactor));
        
                    _analyticsTimeframeVisuals[timeframeMinutes] = new AnalyticsTimeframeMetricVisual
                    {
                        SignalText = signalText,
                        AveragePriceValueText = avgValue,
                        AveragePriceHintText = avgHint,
                        AtrValueText = atrValue,
                        AtrHintText = atrHint,
                        AdxValueText = adxValue,
                        AdxHintText = adxHint,
                        EmaFactor = emaFactor,
                        RsiFactor = rsiFactor,
                        StochFactor = stochFactor,
                        ZFactor = zFactor,
                        OrderFlowFactor = orderFlowFactor
                    };
                    SetAnalyticsFactorVisibility(orderFlowFactor, false);
         
                    card.Child = stack;
                    return card;
                }
        
                private Grid CreateAnalyticsMetricLine(string labelKey, string labelFallback, out TextBlock valueText, out TextBlock hintText)
                {
                    var row = new Grid { Margin = new Thickness(0, 4, 0, 0) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                    var labelText = new TextBlock
                    {
                        Text = L(labelKey, labelFallback) + ":",
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    RegisterLocalizationBinding(() => labelText.Text = L(labelKey, labelFallback) + ":");
                    ApplySkinResource(labelText, TextBlock.ForegroundProperty, "FontControlBrush", "FontTableBrush");
                    row.Children.Add(labelText);
        
                    valueText = new TextBlock
                    {
                        Text = "-",
                        FontWeight = FontWeights.Medium,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    ApplySkinResource(valueText, TextBlock.ForegroundProperty, "FontControlBrush", "FontTableBrush");
                    Grid.SetColumn(valueText, 1);
                    row.Children.Add(valueText);
        
                    hintText = new TextBlock
                    {
                        Text = "-",
                        Margin = new Thickness(8, 0, 0, 0),
                        Opacity = 0.85,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    ApplySkinResource(hintText, TextBlock.ForegroundProperty, "FontControlBrush", "FontTableBrush");
                    Grid.SetColumn(hintText, 2);
                    row.Children.Add(hintText);
        
                    return row;
                }
        
                private Grid CreateAnalyticsDecisionDetailRow(
                    string labelKey,
                    string labelFallback,
                    out TextBlock valueText)
                {
                    var row = new Grid
                    {
                        Margin = new Thickness(0, 1, 0, 0),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        MinWidth = 186
                    };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });

                    var labelText = new TextBlock
                    {
                        Text = L(labelKey, labelFallback) + ":",
                        FontSize = 10,
                        FontWeight = FontWeights.Medium,
                        Margin = new Thickness(0, 0, 6, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        TextAlignment = TextAlignment.Right
                    };
                    RegisterLocalizationBinding(() =>
                    {
                        labelText.Text = L(labelKey, labelFallback) + ":";
                    });
                    ApplySkinResource(labelText, TextBlock.ForegroundProperty, "FontControlBrush", "FontTableBrush");
                    Grid.SetColumn(labelText, 0);
                    row.Children.Add(labelText);

                    valueText = new TextBlock
                    {
                        Text = "-",
                        FontSize = 10,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        TextAlignment = TextAlignment.Left
                    };
                    ApplySkinResource(valueText, TextBlock.ForegroundProperty, "FontControlBrush", "FontTableBrush");
                    Grid.SetColumn(valueText, 1);
                    row.Children.Add(valueText);

                    return row;
                }

                private Grid CreateAnalyticsFactorRow(FrameworkElement context, string labelKey, string labelFallback, out AnalyticsFactorVisual visual)
                {
                    const double halfWidth = 58.0;
                    var row = new Grid
                    {
                        Margin = new Thickness(0, 3, 0, 0),
                        HorizontalAlignment = HorizontalAlignment.Center
                    };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(126) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        
                    var labelText = new TextBlock
                    {
                        Text = L(labelKey, labelFallback),
                        FontSize = 11,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        TextAlignment = TextAlignment.Right,
                        Margin = new Thickness(0, 0, 6, 0)
                    };
                    BindLocalizedText(labelText, labelKey, labelFallback);
                    ApplySkinResource(labelText, TextBlock.ForegroundProperty, "FontControlBrush", "FontTableBrush");
                    row.Children.Add(labelText);
        
                    var trackGrid = new Grid
                    {
                        Width = halfWidth * 2,
                        Height = 6,
                        Margin = new Thickness(0, 0, 0, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        SnapsToDevicePixels = true
                    };
                    Grid.SetColumn(trackGrid, 1);
        
                    var track = new Border
                    {
                        CornerRadius = new CornerRadius(2),
                        BorderThickness = new Thickness(1)
                    };
                    ApplySkinResource(track, Border.BackgroundProperty, "BackgroundMainWindow", "GridEntireBackground", "BackgroundTextInput");
                    ApplySkinResource(track, Border.BorderBrushProperty, "BorderThinBrush", "TabControlBorderBrush", "GridHeaderHighlight");
                    trackGrid.Children.Add(track);
        
                    var centerMarker = new Border
                    {
                        Width = 1,
                        HorizontalAlignment = HorizontalAlignment.Center
                    };
                    ApplySkinResource(centerMarker, Border.BackgroundProperty, "BorderThinBrush", "TabControlBorderBrush", "GridHeaderHighlight");
                    trackGrid.Children.Add(centerMarker);
        
                    var leftContainer = new Grid
                    {
                        Width = halfWidth,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        ClipToBounds = true
                    };
                    var leftFill = new Border
                    {
                        Width = 0,
                        Height = 4,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Center,
                        CornerRadius = new CornerRadius(2, 0, 0, 2),
                        Background = OrangeAccentBrush
                    };
                    leftContainer.Children.Add(leftFill);
                    trackGrid.Children.Add(leftContainer);
        
                    var rightContainer = new Grid
                    {
                        Width = halfWidth,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        ClipToBounds = true
                    };
                    var rightFill = new Border
                    {
                        Width = 0,
                        Height = 4,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Center,
                        CornerRadius = new CornerRadius(0, 2, 2, 0),
                        Background = TealAccentBrush
                    };
                    rightContainer.Children.Add(rightFill);
                    trackGrid.Children.Add(rightContainer);
        
                    row.Children.Add(trackGrid);
        
                    var valueText = new TextBlock
                    {
                        Text = "0.00",
                        FontSize = 11,
                        Margin = new Thickness(6, 0, 0, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        TextAlignment = TextAlignment.Left
                    };
                    ApplySkinResource(valueText, TextBlock.ForegroundProperty, "FontControlBrush", "FontTableBrush");
                    Grid.SetColumn(valueText, 2);
                    row.Children.Add(valueText);
        
                    visual = new AnalyticsFactorVisual
                    {
                        RowHost = row,
                        LeftFill = leftFill,
                        RightFill = rightFill,
                        ValueText = valueText,
                        HalfWidth = halfWidth
                    };
                    return row;
                }
        
                private UIElement CreateAnalyticsInfoBlock(FrameworkElement context, string titleKey, string titleFallback, out TextBlock valueText)
                {
                    var block = CreateAnalyticsCard(context, new Thickness(0, 0, 0, 8), new Thickness(10));
                    var stack = new StackPanel { Orientation = Orientation.Vertical };
        
                    var titleText = new TextBlock
                    {
                        Text = L(titleKey, titleFallback),
                        FontWeight = FontWeights.Medium,
                        Margin = new Thickness(0, 0, 0, 4)
                    };
                    BindLocalizedText(titleText, titleKey, titleFallback);
                    ApplySkinResource(titleText, TextBlock.ForegroundProperty, "FontHeaderLevel4Brush", "FontControlBrush", "FontTableBrush");
                    stack.Children.Add(titleText);
        
                    valueText = new TextBlock
                    {
                        Text = "-",
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = 11
                    };
                    ApplySkinResource(valueText, TextBlock.ForegroundProperty, "FontControlBrush", "FontTableBrush");
                    stack.Children.Add(valueText);
        
                    block.Child = stack;
                    return block;
                }
        
                private UIElement CreateAnalyticsHeadlineListBlock(FrameworkElement context, string titleKey, string titleFallback, out ListBox listBox)
                {
                    var block = CreateAnalyticsCard(context, new Thickness(0, 0, 0, 8), new Thickness(10));
                    var stack = new StackPanel { Orientation = Orientation.Vertical };
        
                    var titleText = new TextBlock
                    {
                        Text = L(titleKey, titleFallback),
                        FontWeight = FontWeights.Medium,
                        Margin = new Thickness(0, 0, 0, 4)
                    };
                    BindLocalizedText(titleText, titleKey, titleFallback);
                    ApplySkinResource(titleText, TextBlock.ForegroundProperty, "FontHeaderLevel4Brush", "FontControlBrush", "FontTableBrush");
                    stack.Children.Add(titleText);
        
                    listBox = new ListBox
                    {
                        BorderThickness = new Thickness(0),
                        MaxHeight = 120,
                        MinHeight = 72,
                        HorizontalContentAlignment = HorizontalAlignment.Stretch,
                        Margin = new Thickness(0),
                        FocusVisualStyle = null
                    };
                    ApplySkinResource(listBox, Control.BackgroundProperty, "BackgroundMainWindow", "GridEntireBackground", "BackgroundTextInput");
                    ApplySkinResource(listBox, Control.ForegroundProperty, "FontControlBrush", "FontTableBrush");
        
                    var itemStyle = new Style(typeof(ListBoxItem));
                    itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0, 2, 0, 2)));
                    itemStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
                    itemStyle.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
                    itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
                    itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
                    listBox.ItemContainerStyle = itemStyle;
                    listBox.ItemTemplate = CreateHeadlineListItemTemplate();
        
                    stack.Children.Add(listBox);
                    block.Child = stack;
                    return block;
                }
        
                private UIElement CreateAnalyticsMag7ScoringBlock(
                    FrameworkElement context,
                    out TextBlock titleText,
                    out StackPanel itemsHost)
                {
                    var block = CreateAnalyticsCard(context, new Thickness(0, 0, 0, 8), new Thickness(10));
                    var stack = new StackPanel { Orientation = Orientation.Vertical };
        
                    titleText = new TextBlock
                    {
                        Text = L("analytics.block.instrument_overview", "Instrument Overview"),
                        FontWeight = FontWeights.Medium,
                        Margin = new Thickness(0, 0, 0, 4)
                    };
                    BindLocalizedText(titleText, "analytics.block.instrument_overview", "Instrument Overview");
                    ApplySkinResource(titleText, TextBlock.ForegroundProperty, "FontHeaderLevel4Brush", "FontControlBrush", "FontTableBrush");
                    stack.Children.Add(titleText);
        
                    itemsHost = new StackPanel
                    {
                        Orientation = Orientation.Vertical
                    };
        
                    stack.Children.Add(itemsHost);
                    block.Child = stack;
                    return block;
                }
        
                private static DataTemplate CreateHeadlineListItemTemplate()
                {
                    var text = new FrameworkElementFactory(typeof(TextBlock));
                    text.SetBinding(TextBlock.TextProperty, new Binding("."));
                    text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
                    text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
                    text.SetValue(TextBlock.FontSizeProperty, 11.0);
                    text.SetValue(TextBlock.LineHeightProperty, 16.0);
                    text.SetValue(TextBlock.LineStackingStrategyProperty, LineStackingStrategy.BlockLineHeight);
                    text.SetValue(FrameworkElement.MaxHeightProperty, 32.0);
                    return new DataTemplate
                    {
                        VisualTree = text
                    };
                }
        
                private static Border CreateAnalyticsCard(FrameworkElement context, Thickness margin, Thickness padding)
                {
                    var card = new Border
                    {
                        Margin = margin,
                        Padding = padding,
                        BorderThickness = new Thickness(1)
                    };
                    ApplySkinResource(card, Border.BackgroundProperty, "BackgroundMainWindow", "GridEntireBackground", "BackgroundTextInput");
                    ApplySkinResource(card, Border.BorderBrushProperty, "BorderThinBrush", "TabControlBorderBrush", "GridHeaderHighlight");
                    return card;
                }
        
                private Border CreateAnalyticsMetricCard(FrameworkElement context, string titleKey, string titleFallback, out TextBlock valueText, Thickness margin)
                {
                    var card = CreateAnalyticsCard(context, margin, new Thickness(10, 6, 10, 6));
                    card.MinHeight = 68;
                    card.VerticalAlignment = VerticalAlignment.Bottom;
                    var stack = new StackPanel
                    {
                        VerticalAlignment = VerticalAlignment.Center
                    };
        
                    var titleText = new TextBlock
                    {
                        Text = L(titleKey, titleFallback),
                        FontSize = 11,
                        FontWeight = FontWeights.Bold,
                        Margin = new Thickness(0, 0, 0, 3)
                    };
                    BindLocalizedText(titleText, titleKey, titleFallback);
                    ApplySkinResource(titleText, TextBlock.ForegroundProperty, "FontHeaderLevel4Brush", "FontControlBrush", "FontTableBrush");
                    stack.Children.Add(titleText);
        
                    valueText = new TextBlock
                    {
                        Text = "-",
                        FontSize = 16,
                        FontWeight = FontWeights.Bold
                    };
                    ApplySkinResource(valueText, TextBlock.ForegroundProperty, "FontControlBrush", "FontTableBrush", "GridRowForeground");
                    stack.Children.Add(valueText);
        
                    card.Child = stack;
                    return card;
                }
        
                private UIElement CreateAnalyticsSessionRangeVisual(FrameworkElement context)
                {
                    var host = new Grid
                    {
                        VerticalAlignment = VerticalAlignment.Center,
                        ClipToBounds = true
                    };
                    host.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    host.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        
                    _analyticsSessionRangeLowLabel = new TextBlock
                    {
                        Text = L("analytics.session_range.low", "Session Low") + Environment.NewLine + "-",
                        Margin = new Thickness(0, 0, 6, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        FontSize = 10,
                        TextAlignment = TextAlignment.Left,
                        MinWidth = 42
                    };
                    ApplySkinResource(_analyticsSessionRangeLowLabel, TextBlock.ForegroundProperty, "FontControlBrush", "FontTableBrush");
                    host.Children.Add(_analyticsSessionRangeLowLabel);
        
                    _analyticsSessionRangeCanvas = new Canvas
                    {
                        Height = 54,
                        MinWidth = 72,
                        Margin = new Thickness(0, 0, 0, 0),
                        HorizontalAlignment = HorizontalAlignment.Stretch
                    };
                    _analyticsSessionRangeCanvas.SizeChanged += OnAnalyticsSessionRangeCanvasSizeChanged;
                    Grid.SetColumn(_analyticsSessionRangeCanvas, 1);
                    host.Children.Add(_analyticsSessionRangeCanvas);
        
                    _analyticsSessionRangeHighLabel = new TextBlock
                    {
                        Text = L("analytics.session_range.high", "Session High") + Environment.NewLine + "-",
                        Margin = new Thickness(6, 0, 0, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        FontSize = 10,
                        TextAlignment = TextAlignment.Right,
                        MinWidth = 42
                    };
                    ApplySkinResource(_analyticsSessionRangeHighLabel, TextBlock.ForegroundProperty, "FontControlBrush", "FontTableBrush");
                    Grid.SetColumn(_analyticsSessionRangeHighLabel, 2);
                    host.Children.Add(_analyticsSessionRangeHighLabel);
        
                    _analyticsSessionRangeTrack = new Border
                    {
                        Height = 5,
                        CornerRadius = new CornerRadius(3),
                        Background = new SolidColorBrush(Color.FromRgb(96, 100, 106))
                    };
                    _analyticsSessionRangeCanvas.Children.Add(_analyticsSessionRangeTrack);
        
                    _analyticsSessionRangeAvgMarker = new Border
                    {
                        Width = 2,
                        Height = 13,
                        Background = new SolidColorBrush(Color.FromRgb(173, 181, 189))
                    };
                    _analyticsSessionRangeCanvas.Children.Add(_analyticsSessionRangeAvgMarker);
        
                    _analyticsSessionRangeCurrentMarker = new Border
                    {
                        Width = 3,
                        Height = 18,
                        Background = Brushes.White
                    };
                    _analyticsSessionRangeCanvas.Children.Add(_analyticsSessionRangeCurrentMarker);
        
                    _analyticsSessionRangeAvgText = new TextBlock
                    {
                        Text = L("analytics.session_range.avg", "Avg") + " -",
                        FontSize = 10,
                        TextWrapping = TextWrapping.NoWrap
                    };
                    ApplySkinResource(_analyticsSessionRangeAvgText, TextBlock.ForegroundProperty, "FontControlBrush", "FontTableBrush");
                    _analyticsSessionRangeCanvas.Children.Add(_analyticsSessionRangeAvgText);
        
                    _analyticsSessionRangeCurrentText = new TextBlock
                    {
                        Text = L("analytics.session_range.current", "Current") + " -",
                        FontSize = 10,
                        TextWrapping = TextWrapping.NoWrap
                    };
                    ApplySkinResource(_analyticsSessionRangeCurrentText, TextBlock.ForegroundProperty, "FontControlBrush", "FontTableBrush");
                    _analyticsSessionRangeCanvas.Children.Add(_analyticsSessionRangeCurrentText);
        
                    RenderAnalyticsSessionRangeVisual();
                    return host;
                }
        
                private void OnAnalyticsSessionRangeCanvasSizeChanged(object sender, SizeChangedEventArgs e)
                {
                    RenderAnalyticsSessionRangeVisual();
                }
        
        private void OnAnalyticsInstrumentSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_analyticsInstrumentCombo == null || _isUpdatingAnalyticsInstrumentSelection)
                    return;

            _selectedAnalyticsInstrument = _analyticsInstrumentCombo.SelectedItem as string;
            UpdateAnalyticsDetachedWindowButtonVisibility();
            RefreshAnalyticsDashboard(GetActiveAccountsSnapshot());
        }
        
                private void RefreshAnalyticsDashboard(IReadOnlyList<Account> activeAccounts)
                {
                    if (!CanAccessAnalyticsPremium(out string lockedMessage))
                    {
                        ApplyAnalyticsLockedState(lockedMessage);
                        UpdateAnalyticsLicenseGateOverlay();
                        return;
                    }
                    UpdateAnalyticsLicenseGateOverlay();

                    if (_analyticsEngine == null || _analyticsInstrumentCombo == null)
                    {
                        UpdateAnalyticsDetachedWindowButtonVisibility();
                        UpdateAnalyticsUnifiedSignal(null);
                        UpdateTechnicalFeedStatusText(null);
                            return;
                    }
        
                    IReadOnlyList<Account> accounts = activeAccounts ?? GetActiveAccountsSnapshot();
                    EnsureAnalyticsInstrumentOptions(accounts);
        
                    string instrument = _selectedAnalyticsInstrument;
                    if (string.IsNullOrWhiteSpace(instrument))
                        instrument = _analyticsInstrumentCombo.SelectedItem as string;
                    if (string.IsNullOrWhiteSpace(instrument) && _analyticsInstrumentCombo.Items.Count > 0)
                        instrument = _analyticsInstrumentCombo.Items[0] as string;
                    if (string.IsNullOrWhiteSpace(instrument))
                    {
                        UpdateAnalyticsDetachedWindowButtonVisibility();
                        UpdateAnalyticsUnifiedSignal(null);
                        UpdateTechnicalFeedStatusText(null);
                            return;
                    }
        
            _selectedAnalyticsInstrument = instrument;
            UpdateAnalyticsDetachedWindowButtonVisibility();
            if (!string.Equals(_analyticsInstrumentCombo.SelectedItem as string, instrument, StringComparison.OrdinalIgnoreCase))
            {
                _isUpdatingAnalyticsInstrumentSelection = true;
                try
                {
                            _analyticsInstrumentCombo.SelectedItem = instrument;
                        }
                        finally
                        {
                            _isUpdatingAnalyticsInstrumentSelection = false;
                        }
                    }
        
                    DateTime nowUtc = DateTime.UtcNow;
                    GlitchAnalyticsSnapshot snapshot = _analyticsEngine.BuildSnapshot(instrument, accounts, nowUtc);
                    if (_fundamentalAnalysisService != null)
                    {
                        try
                        {
                            GlitchFundamentalAnalysisSnapshot fundamentals = _fundamentalAnalysisService.GetSnapshot(
                                instrument,
                                nowUtc,
                                _runtimePolicySettings?.LicenseApiBaseUrl,
                                _runtimePolicySettings?.LicenseKey,
                                _runtimePolicySettings?.InstallationId,
                                _licenseDeviceFingerprintHash,
                                CurrentClientVersion);
                            if (fundamentals != null)
                            {
                                snapshot.NewsSentiment = fundamentals.NewsSentiment;
                                snapshot.EarningsAnalysis = fundamentals.EarningsAnalysis;
                                snapshot.OfficialNews = fundamentals.OfficialNews;
                                snapshot.ScoreSectionTitle = fundamentals.ScoreSectionTitle;
                                snapshot.IsNewsEventLockoutActive = fundamentals.IsNewsLockoutActive;
                                snapshot.NewsEventLockoutText = fundamentals.NewsLockoutText;
                                snapshot.Mag7ScoreLines = fundamentals.Mag7ScoreLines;
                                snapshot.LatestHeadlineLines = fundamentals.LatestHeadlineLines;
                                snapshot.OfficialNewsLines = fundamentals.OfficialNewsLines;
                                ApplyMag7Influence(snapshot, fundamentals.Mag7InfluenceScore);
                            }
                        }
                        catch (Exception ex)
                        {
                            string errorLabel = L("analytics.fundamental.error", "Fundamental feed error");
                            snapshot.NewsSentiment = errorLabel;
                            snapshot.EarningsAnalysis = errorLabel;
                            snapshot.OfficialNews = errorLabel + ": " + ex.Message;
                            snapshot.ScoreSectionTitle = L("analytics.block.instrument_scoring", "Instrument Scoring");
                            snapshot.IsNewsEventLockoutActive = false;
                            snapshot.NewsEventLockoutText = null;
                            snapshot.Mag7ScoreLines = null;
                            snapshot.LatestHeadlineLines = null;
                            snapshot.OfficialNewsLines = null;
                        }
                    }
                    ApplyAnalyticsSnapshot(snapshot);
                }
        
                private void ApplyMag7Influence(GlitchAnalyticsSnapshot snapshot, double mag7InfluenceScore)
                {
                    if (snapshot == null)
                            return;
        
                    double influence = ClampScore(mag7InfluenceScore, -1, 1);
                    if (Math.Abs(influence) < 1e-6)
                            return;
        
                    List<GlitchTimeframeReading> readings = snapshot.TimeframeReadings == null
                        ? null
                        : snapshot.TimeframeReadings.Where(x => x != null).ToList();
                    if (readings == null || readings.Count == 0)
                            return;
        
                    List<GlitchTimeframeReading> activeReadings = readings
                        .Where(x => x.AveragePrice.HasValue || x.AtrProxy.HasValue || x.AdxProxy.HasValue)
                        .ToList();
                    if (activeReadings.Count == 0)
                            return;
        
                    for (int i = 0; i < activeReadings.Count; i++)
                    {
                        GlitchTimeframeReading reading = activeReadings[i];
                        double timeframeWeight = ResolveMag7InfluenceWeight(reading.Minutes);
                        reading.Score = ClampScore(reading.Score + (influence * timeframeWeight), -1, 1);
                        reading.SignalLabel = GlitchSignalScale.ToLabel(reading.Score);
                    }
        
                    snapshot.CompositeScore = ComputeWeightedCompositeScore(activeReadings);
                    snapshot.CompositeSignal = GlitchSignalScale.ToLabel(snapshot.CompositeScore);
                }
        
                private static double ResolveMag7InfluenceWeight(int minutes)
                {
                    if (minutes <= 1)
                        return 0.24;
                    if (minutes <= 5)
                        return 0.20;
                    if (minutes <= 15)
                        return 0.16;
                    return 0.12;
                }
        
                private double ComputeWeightedCompositeScore(IEnumerable<GlitchTimeframeReading> readings)
                {
                    if (readings == null)
                        return 0;
        
                    double weighted = 0;
                    double total = 0;
                    foreach (GlitchTimeframeReading reading in readings)
                    {
                        if (reading == null)
                            continue;
        
                        double weight = ResolveCompositeWeight(reading.Minutes);
                        if (weight <= 0)
                            continue;
        
                        AnalyticsDecisionComponentScore components = BuildDecisionComponentScore(reading);
                        weighted += components.EffectiveScore * weight;
                        total += weight;
                    }
        
                    if (total <= 1e-8)
                        return 0;
        
                    return weighted / total;
                }
        
                private static double ResolveCompositeWeight(int minutes)
                {
                    if (minutes <= 1)
                        return 0.45;
                    if (minutes <= 5)
                        return 0.30;
                    if (minutes <= 15)
                        return 0.17;
                    return 0.08;
                }
        
                private static double ClampScore(double value, double min, double max)
                {
                    if (value < min)
                        return min;
                    if (value > max)
                        return max;
                    return value;
                }
        
                private void EnsureAnalyticsInstrumentOptions(IEnumerable<Account> accounts)
                {
                    if (_analyticsInstrumentCombo == null || _analyticsEngine == null)
                            return;
        
                    IReadOnlyList<string> options = _analyticsEngine.BuildInstrumentOptions(accounts, _selectedAnalyticsInstrument);
                    string optionSnapshot = string.Join("|", options);
        
                    string previousSelection = _selectedAnalyticsInstrument;
                    if (string.IsNullOrWhiteSpace(previousSelection))
                        previousSelection = _analyticsInstrumentCombo.SelectedItem as string;
        
                    if (string.Equals(optionSnapshot, _analyticsInstrumentOptionsSnapshot, StringComparison.Ordinal))
                            return;
        
                    _isUpdatingAnalyticsInstrumentSelection = true;
                    try
                    {
                        _analyticsInstrumentCombo.ItemsSource = options;
                        _analyticsInstrumentOptionsSnapshot = optionSnapshot;
        
                        string nextSelection = options.FirstOrDefault(x =>
                            !string.IsNullOrWhiteSpace(previousSelection) &&
                            x.Equals(previousSelection, StringComparison.OrdinalIgnoreCase));
        
                    if (string.IsNullOrWhiteSpace(nextSelection) && options.Count > 0)
                        nextSelection = options[0];

                    _analyticsInstrumentCombo.SelectedItem = nextSelection;
                    _selectedAnalyticsInstrument = nextSelection;
                    UpdateAnalyticsDetachedWindowButtonVisibility();
                }
                finally
                {
                    _isUpdatingAnalyticsInstrumentSelection = false;
                }
            }
        
                private void ApplyAnalyticsSnapshot(GlitchAnalyticsSnapshot snapshot)
                {
                    if (snapshot == null)
                            return;
                    if (_analyticsCurrentPriceText == null ||
                        _analyticsOverallSignalText == null ||
                        _analyticsSessionText == null)
                    {
                            return;
                    }
        
                    _analyticsCurrentPriceText.Text = FormatAnalyticsPrice(snapshot.CurrentPrice);
                    _analyticsSessionText.Text = string.IsNullOrWhiteSpace(snapshot.SessionName) ? "-" : snapshot.SessionName;
                    double? sessionAvg = ResolveSessionAverage(snapshot.SessionHigh, snapshot.SessionLow);
                    UpdateAnalyticsSessionRangeVisual(snapshot.InstrumentRoot, snapshot.SessionLow, sessionAvg, snapshot.CurrentPrice, snapshot.SessionHigh);
        
                    _analyticsOverallSignalText.Text =
                        $"{snapshot.CompositeSignal} ({snapshot.CompositeScore.ToString("+0.00;-0.00;0.00", CultureInfo.CurrentCulture)})";
                    _analyticsOverallSignalText.Foreground = ResolveAnalyticsSignalBrush(snapshot.CompositeScore);
        
                    if (_analyticsScoreSectionTitleText != null)
                    {
                        string fallbackTitle = L("analytics.block.instrument_overview", "Instrument Overview");
                        _analyticsScoreSectionTitleText.Text = string.IsNullOrWhiteSpace(snapshot.ScoreSectionTitle)
                            ? fallbackTitle
                            : snapshot.ScoreSectionTitle;
                    }
        
                    ApplyMag7ScoringItems(snapshot.Mag7ScoreLines, snapshot.NewsSentiment);
                    _analyticsEarningsAnalysisText.Text = string.IsNullOrWhiteSpace(snapshot.EarningsAnalysis)
                        ? "-"
                        : snapshot.EarningsAnalysis.Replace(" | ", Environment.NewLine);
                    _analyticsOfficialNewsText.Text = JoinAnalyticsLines(snapshot.OfficialNewsLines, snapshot.OfficialNews);
                    if (_analyticsLatestHeadlinesList != null)
                    {
                        IReadOnlyList<string> headlines = snapshot.LatestHeadlineLines;
                        if (headlines == null || headlines.Count == 0)
                            headlines = new[] { L("analytics.no_recent_headlines", "No recent instrument headlines.") };
                        _analyticsLatestHeadlinesList.ItemsSource = headlines;
                    }
                    UpdateFundamentalKeyStatusText();
                    UpdateTechnicalFeedStatusText(snapshot);
                    UpdateGlobalNewsLockoutBanner(snapshot.IsNewsEventLockoutActive, snapshot.NewsEventLockoutText);
        
                    var readings = snapshot.TimeframeReadings?
                        .Where(x => x != null)
                        .ToDictionary(x => x.Minutes, x => x);

                    UpdateAnalyticsUnifiedSignal(readings);
        
                    foreach (var kvp in _analyticsDialVisuals)
                    {
                        GlitchTimeframeReading reading = null;
                        if (readings != null)
                            readings.TryGetValue(kvp.Key, out reading);
                        UpdateAnalyticsDialVisual(kvp.Value, reading);
                    }
        
                    foreach (var kvp in _analyticsTimeframeVisuals)
                    {
                        GlitchTimeframeReading reading = null;
                        GlitchTimeframeReading anchorReading = null;
                        if (readings != null)
                        {
                            readings.TryGetValue(kvp.Key, out reading);
                            readings.TryGetValue(ResolveDecisionAnchorTimeframe(kvp.Key), out anchorReading);
                        }
                        UpdateAnalyticsTimeframeVisual(kvp.Value, reading, anchorReading);
                    }
                }
        
                private void ApplyMag7ScoringItems(IReadOnlyList<string> lines, string fallbackText)
                {
                    if (_analyticsMag7ItemsHost == null)
                            return;
        
                    _analyticsMag7ItemsHost.Children.Clear();
                    List<string> nonEmptyLines = lines == null
                        ? null
                        : lines.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        
                    if (nonEmptyLines == null || nonEmptyLines.Count == 0)
                    {
                        var fallback = new TextBlock
                        {
                            Text = string.IsNullOrWhiteSpace(fallbackText) ? "-" : fallbackText,
                            TextWrapping = TextWrapping.Wrap,
                            FontSize = 11
                        };
                        ApplySkinResource(fallback, TextBlock.ForegroundProperty, "FontControlBrush", "FontTableBrush");
                        _analyticsMag7ItemsHost.Children.Add(fallback);
                            return;
                    }
        
                    Brush defaultBrush = ResolveSkinBrush(
                        "FontControlBrush",
                        "FontTableBrush") ?? Brushes.White;
                    const double headingFontSize = 11.0;
                    const double detailFontSize = 10.0;
                    bool firstItem = true;
        
                    for (int i = 0; i < nonEmptyLines.Count; i++)
                    {
                        string headingLine = nonEmptyLines[i].Trim();
                        if (!IsMag7HeadingLine(headingLine))
                            continue;
        
                        string detailLine = string.Empty;
                        if (i + 1 < nonEmptyLines.Count)
                        {
                            string candidate = nonEmptyLines[i + 1].Trim();
                            if (!IsMag7HeadingLine(candidate))
                            {
                                detailLine = candidate;
                                i++;
                            }
                        }
        
                        var itemHost = new StackPanel
                        {
                            Orientation = Orientation.Vertical,
                            Margin = new Thickness(0, 8, 0, 0)
                        };
        
                        var heading = new TextBlock
                        {
                            TextWrapping = TextWrapping.Wrap
                        };
                        AppendLineWithColoredScores(
                            heading,
                            headingLine,
                            headingFontSize,
                            FontWeights.Medium,
                            defaultBrush);
                        itemHost.Children.Add(heading);
        
                        var detail = new TextBlock
                        {
                            TextWrapping = TextWrapping.Wrap,
                            Margin = new Thickness(0, 3, 0, 0)
                        };
                        AppendLineWithColoredScores(
                            detail,
                            string.IsNullOrWhiteSpace(detailLine) ? "-" : detailLine,
                            detailFontSize,
                            FontWeights.Normal,
                            defaultBrush);
                        itemHost.Children.Add(detail);
        
                        _analyticsMag7ItemsHost.Children.Add(itemHost);
                        firstItem = false;
                    }
        
                    if (firstItem)
                    {
                        var fallback = new TextBlock
                        {
                            Text = string.IsNullOrWhiteSpace(fallbackText) ? "-" : fallbackText,
                            TextWrapping = TextWrapping.Wrap,
                            FontSize = 11
                        };
                        ApplySkinResource(fallback, TextBlock.ForegroundProperty, "FontControlBrush", "FontTableBrush");
                        _analyticsMag7ItemsHost.Children.Add(fallback);
                    }
                }
        
                private static void AppendLineWithColoredScores(
                    TextBlock host,
                    string line,
                    double fontSize,
                    FontWeight fontWeight,
                    Brush defaultBrush)
                {
                    if (host == null)
                            return;
        
                    string source = line ?? string.Empty;
                    int cursor = 0;
                    MatchCollection matches = Regex.Matches(source, "[+-](?:\\$\\d+(?:[\\.,]\\d+)?|\\d+(?:[\\.,]\\d+)?%?)");
                    foreach (Match match in matches)
                    {
                        if (!match.Success)
                            continue;
        
                        if (match.Index > cursor)
                        {
                            host.Inlines.Add(new Run(source.Substring(cursor, match.Index - cursor))
                            {
                                FontSize = fontSize,
                                FontWeight = fontWeight,
                                Foreground = defaultBrush
                            });
                        }
        
                        double score = 0;
                        bool parsed = double.TryParse(
                            match.Value.Replace("$", string.Empty).Replace("%", string.Empty).Replace(',', '.'),
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out score);
        
                        host.Inlines.Add(new Run(match.Value)
                        {
                            FontSize = fontSize,
                            FontWeight = fontWeight,
                            Foreground = parsed ? ResolveSignedScoreBrush(score, defaultBrush) : defaultBrush
                        });
        
                        cursor = match.Index + match.Length;
                    }
        
                    if (cursor < source.Length)
                    {
                        host.Inlines.Add(new Run(source.Substring(cursor))
                        {
                            FontSize = fontSize,
                            FontWeight = fontWeight,
                            Foreground = defaultBrush
                        });
                    }
                }
        
                private static bool IsMag7HeadingLine(string line)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        return false;
        
                    return Regex.IsMatch(line, "^[A-Z]{2,6}\\b");
                }
        
                private Brush ResolveSkinBrush(params string[] resourceKeys)
                {
                    if (resourceKeys == null || resourceKeys.Length == 0)
                        return null;
        
                    for (int i = 0; i < resourceKeys.Length; i++)
                    {
                        string key = resourceKeys[i];
                        if (string.IsNullOrWhiteSpace(key))
                            continue;
        
                        object value = TryFindResource(key);
                        if (value is Brush brush)
                            return brush;
                    }
        
                    return null;
                }
        
                private static Brush ResolveSignedScoreBrush(double score, Brush defaultBrush)
                {
                    if (score > 0)
                        return TealAccentBrush;
                    if (score < 0)
                        return OrangeAccentBrush;
                    return defaultBrush;
                }
        
                private void UpdateAnalyticsDialVisual(AnalyticsDialVisual visual, GlitchTimeframeReading reading)
                {
                    if (visual == null)
                            return;
        
                    double score = reading?.Score ?? 0;
                    if (visual.NeedleRotation != null)
                        visual.NeedleRotation.Angle = Math.Max(-90, Math.Min(90, score * 90.0));
        
                    if (visual.ScoreText != null)
                        visual.ScoreText.Text = score.ToString("+0.00;-0.00;0.00", CultureInfo.CurrentCulture);
        
                    if (visual.SignalText != null)
                    {
                        visual.SignalText.Text = ResolveDisplaySignalLabel(reading?.SignalLabel, score);
                        visual.SignalText.Foreground = ResolveAnalyticsSignalBrush(score);
                    }
                }
        
                private void UpdateAnalyticsTimeframeVisual(
                    AnalyticsTimeframeMetricVisual visual,
                    GlitchTimeframeReading reading,
                    GlitchTimeframeReading anchorReading)
                {
                    if (visual == null)
                            return;
        
                    if (reading == null || IsAwaitingFeedSignal(reading.SignalLabel))
                    {
                        visual.SignalText.Text = L("analytics.signal.neutral", "Neutral");
                        visual.SignalText.Foreground = ResolveAnalyticsSignalBrush(0);
                        visual.AveragePriceValueText.Text = "-";
                        visual.AveragePriceHintText.Text = string.Empty;
                        visual.AtrValueText.Text = "-";
                        visual.AtrHintText.Text = string.Empty;
                        visual.AdxValueText.Text = "-";
                        visual.AdxHintText.Text = L("analytics.order_flow.unavailable", "Order flow unavailable");
                        UpdateAnalyticsFactorVisual(visual.RawFactor, 0, "-");
                        UpdateAnalyticsFactorVisual(visual.EmaFactor, 0, "-");
                        UpdateAnalyticsFactorVisual(visual.RsiFactor, 0, "-");
                        UpdateAnalyticsFactorVisual(visual.StochFactor, 0, "-");
                        UpdateAnalyticsFactorVisual(visual.ZFactor, 0, "-");
                        UpdateAnalyticsFactorVisual(visual.OscillatorFactor, 0, "-");
                        UpdateAnalyticsFactorVisual(visual.MaFactor, 0, "-");
                        UpdateAnalyticsFactorVisual(visual.OrderFlowFactor, 0, "-");
                        UpdateAnalyticsFactorVisual(visual.AgreementFactor, 0, "-");
                        SetAnalyticsFactorVisibility(visual.OrderFlowFactor, false);
                        SetAnalyticsFactorVisibility(visual.AgreementFactor, true);
                        ApplyAnalyticsDecisionDetails(
                            visual,
                            new AnalyticsUnifiedSignalState
                            {
                                BiasLabel = L("analytics.unified.bias.neutral", "Neutral"),
                                BiasScore = 0,
                                ExecutionSignalLabel = L("analytics.signal.neutral", "Neutral"),
                                ExecutionScore = 0,
                                SetupLabel = L("analytics.unified.setup.flat", "Flat"),
                                Confidence = 0,
                                Tradeability = 0,
                                Quality = 0,
                                AgreementScore = 0,
                                GateLabel = L("analytics.unified.action.no_trade", "No Trade"),
                                IsActionable = false,
                                RegimeLabel = string.Empty,
                                NoTradeReasons = string.Empty
                            });
                        return;
                    }
        
                    visual.SignalText.Text = ResolveDisplaySignalLabel(reading.SignalLabel, reading.Score);
                    visual.SignalText.Foreground = ResolveAnalyticsSignalBrush(reading.Score);
        
                    visual.AveragePriceValueText.Text = FormatAnalyticsPriceWhole(reading.AveragePrice);
                    visual.AveragePriceHintText.Text = string.Empty;
                    visual.AtrValueText.Text = BuildRegimeSummary(reading);
                    visual.AtrHintText.Text = string.Empty;
                    visual.AdxValueText.Text = BuildAdxAtrSummary(reading.AdxProxy, reading.AtrProxy);
                    visual.AdxHintText.Text = BuildOrderFlowSummary(reading);
                    visual.AdxHintText.Foreground = ResolveAnalyticsSignalBrush(reading.OrderFlowScore ?? 0);

                    double rawScore = ClampAnalyticsFactor(reading.RawScore);
                    double finalScore = ClampAnalyticsFactor(reading.Score);
                    double directionalScore = ClampAnalyticsFactor(reading.DirectionalScore);
                    double oscillatorCompositeScore = ClampAnalyticsFactor(reading.OscillatorCompositeScore ?? 0);
                    double maCompositeScore = ClampAnalyticsFactor(reading.MaCompositeScore ?? 0);
                    double emaSignal = ClampAnalyticsFactor(reading.EmaAlignment ?? 0);
                    double rsiSignal = reading.Rsi.HasValue
                        ? ClampAnalyticsFactor((reading.Rsi.Value - 50.0) / 22.0)
                        : 0;
                    double stochSignal = reading.StochK.HasValue
                        ? ClampAnalyticsFactor((reading.StochK.Value - 50.0) / 28.0)
                        : 0;
                    double zSignal = reading.ZScore.HasValue
                        ? ClampAnalyticsFactor(reading.ZScore.Value / 2.2)
                        : 0;

                    UpdateAnalyticsFactorVisual(
                        visual.RawFactor,
                        rawScore,
                        rawScore.ToString("+0.00;-0.00;0.00", CultureInfo.CurrentCulture),
                        1.0,
                        BuildAnalyticsContributionTooltip(
                            L("analytics.factor.raw", "Raw"),
                            "Technical base score before order-flow blending.",
                            rawScore,
                            new[]
                            {
                                BuildAnalyticsTooltipMetric("Directional", directionalScore),
                                BuildAnalyticsTooltipMetric("Final", finalScore)
                            }));

                    UpdateAnalyticsFactorVisual(
                        visual.EmaFactor,
                        emaSignal,
                        emaSignal.ToString("+0.00;-0.00;0.00", CultureInfo.CurrentCulture),
                        1.0,
                        BuildAnalyticsContributionTooltip(
                            L("analytics.factor.ema", "EMA"),
                            "EMA alignment normalized to -1..+1.",
                            emaSignal,
                            new[]
                            {
                                BuildAnalyticsTooltipMetric("Raw alignment", reading.EmaAlignment),
                                BuildAnalyticsTooltipMetric("Raw base", rawScore)
                            }));
                    UpdateAnalyticsFactorVisual(
                        visual.RsiFactor,
                        rsiSignal,
                        reading.Rsi.HasValue
                            ? rsiSignal.ToString("+0.00;-0.00;0.00", CultureInfo.CurrentCulture)
                            : "-",
                        1.0,
                        BuildAnalyticsContributionTooltip(
                            L("analytics.factor.rsi", "RSI"),
                            "RSI signal normalized to -1..+1.",
                            rsiSignal,
                            new[]
                            {
                                BuildAnalyticsTooltipMetric("Raw RSI", reading.Rsi)
                            }));
                    UpdateAnalyticsFactorVisual(
                        visual.StochFactor,
                        stochSignal,
                        reading.StochK.HasValue
                            ? stochSignal.ToString("+0.00;-0.00;0.00", CultureInfo.CurrentCulture)
                            : "-",
                        1.0,
                        BuildAnalyticsContributionTooltip(
                            L("analytics.factor.stoch", "Stoch"),
                            "Stoch signal normalized to -1..+1.",
                            stochSignal,
                            new[]
                            {
                                BuildAnalyticsTooltipMetric("Raw Stoch", reading.StochK)
                            }));
                    UpdateAnalyticsFactorVisual(
                        visual.ZFactor,
                        zSignal,
                        reading.ZScore.HasValue
                            ? zSignal.ToString("+0.00;-0.00;0.00", CultureInfo.CurrentCulture)
                            : "-",
                        1.0,
                        BuildAnalyticsContributionTooltip(
                            L("analytics.factor.z_score", "Z-Score"),
                            "Z-Score signal normalized to -1..+1.",
                            zSignal,
                            new[]
                            {
                                BuildAnalyticsTooltipMetric("Raw Z", reading.ZScore)
                            }));

                    bool hasOscFactor = reading.OscillatorCompositeScore.HasValue;
                    SetAnalyticsFactorVisibility(visual.OscillatorFactor, true);
                    if (hasOscFactor)
                    {
                        UpdateAnalyticsFactorVisual(
                            visual.OscillatorFactor,
                            oscillatorCompositeScore,
                            oscillatorCompositeScore.ToString("+0.00;-0.00;0.00", CultureInfo.CurrentCulture),
                            1.0,
                            BuildAnalyticsContributionTooltip(
                                L("analytics.factor.osc", "Osc"),
                                "Oscillator vote composite normalized to -1..+1.",
                                oscillatorCompositeScore,
                                new[]
                                {
                                    BuildAnalyticsTooltipMetric("Raw", reading.OscillatorCompositeScore)
                                }));
                    }
                    else
                    {
                        UpdateAnalyticsFactorVisual(visual.OscillatorFactor, 0, "-", 1.0, null);
                    }

                    bool hasMaFactor = reading.MaCompositeScore.HasValue;
                    SetAnalyticsFactorVisibility(visual.MaFactor, true);
                    if (hasMaFactor)
                    {
                        UpdateAnalyticsFactorVisual(
                            visual.MaFactor,
                            maCompositeScore,
                            maCompositeScore.ToString("+0.00;-0.00;0.00", CultureInfo.CurrentCulture),
                            1.0,
                            BuildAnalyticsContributionTooltip(
                                L("analytics.factor.ma", "MA"),
                                "Moving-average vote composite normalized to -1..+1.",
                                maCompositeScore,
                                new[]
                                {
                                    BuildAnalyticsTooltipMetric("Raw", reading.MaCompositeScore)
                                }));
                    }
                    else
                    {
                        UpdateAnalyticsFactorVisual(visual.MaFactor, 0, "-", 1.0, null);
                    }

                    bool hasOrderFlowFactor = reading.OrderFlowScore.HasValue;
                    SetAnalyticsFactorVisibility(visual.OrderFlowFactor, hasOrderFlowFactor);
                    if (hasOrderFlowFactor)
                    {
                        double orderFlowFactor = ClampAnalyticsFactor(reading.OrderFlowScore ?? 0);
                        string orderFlowText = orderFlowFactor.ToString("+0.00;-0.00;0.00", CultureInfo.CurrentCulture);

                        UpdateAnalyticsFactorVisual(
                            visual.OrderFlowFactor,
                            orderFlowFactor,
                            orderFlowText,
                            1.0,
                            BuildAnalyticsContributionTooltip(
                                L("analytics.factor.order_flow", "OF"),
                                "Order-flow signal normalized to -1..+1.",
                                orderFlowFactor,
                                new[]
                                {
                                    BuildAnalyticsTooltipMetric("Raw OF", reading.OrderFlowScore),
                                    BuildAnalyticsTooltipMetric("Final", finalScore)
                                }));
                    }
                    else
                    {
                        UpdateAnalyticsFactorVisual(visual.OrderFlowFactor, 0, "-", 1.0, null);
                    }

                    AnalyticsDecisionComponentScore readingComponents = BuildDecisionComponentScore(reading);
                    SetAnalyticsFactorVisibility(visual.AgreementFactor, true);
                    UpdateAnalyticsFactorVisual(
                        visual.AgreementFactor,
                        readingComponents.SignedAgreementScore,
                        readingComponents.SignedAgreementScore.ToString("+0.00;-0.00;0.00", CultureInfo.CurrentCulture),
                        1.0,
                        BuildAnalyticsContributionTooltip(
                            L("analytics.factor.agreement", "Agree"),
                            "Directional agreement across Raw, Osc, MA and OF (weighted).",
                            readingComponents.SignedAgreementScore,
                            new[]
                            {
                                BuildAnalyticsTooltipMetric("Agreement", readingComponents.AgreementScore),
                                BuildAnalyticsTooltipMetric("Technical blend", readingComponents.TechnicalScore),
                                BuildAnalyticsTooltipMetric("Effective", readingComponents.EffectiveScore)
                            }));

                    AnalyticsDecisionComponentScore anchorComponents = HasDecisionReading(anchorReading)
                        ? BuildDecisionComponentScore(anchorReading)
                        : readingComponents;
                    AnalyticsUnifiedSignalState decision = BuildDecisionSignalState(
                        anchorComponents.EffectiveScore,
                        readingComponents.EffectiveScore,
                        readingComponents.EffectiveScore,
                        anchorComponents.AgreementScore,
                        readingComponents.AgreementScore,
                        readingComponents.AgreementScore,
                        reading.AdxProxy,
                        reading);
                    ApplyAnalyticsDecisionDetails(visual, decision);
                }

                private void UpdateAnalyticsUnifiedSignal(IReadOnlyDictionary<int, GlitchTimeframeReading> readings)
                {
                    AnalyticsUnifiedSignalState state = BuildGlobalDecisionSignalState(readings);

                    if (_analyticsUnifiedBiasValueText != null)
                    {
                        _analyticsUnifiedBiasValueText.Text =
                            state.BiasLabel +
                            " (" +
                            state.BiasScore.ToString("+0.00;-0.00;0.00", CultureInfo.CurrentCulture) +
                            ")";
                        _analyticsUnifiedBiasValueText.Foreground = ResolveAnalyticsSignalBrush(state.BiasScore);
                    }

                    if (_analyticsUnifiedExecutionSignalValueText != null)
                    {
                        _analyticsUnifiedExecutionSignalValueText.Text =
                            state.ExecutionSignalLabel +
                            " (" +
                            state.ExecutionScore.ToString("+0.00;-0.00;0.00", CultureInfo.CurrentCulture) +
                            ")";
                        _analyticsUnifiedExecutionSignalValueText.Foreground = ResolveAnalyticsSignalBrush(state.ExecutionScore);
                    }

                    if (_analyticsUnifiedSetupValueText != null)
                    {
                        string setupText = state.SetupLabel;
                        if (!string.IsNullOrWhiteSpace(state.RegimeLabel))
                            setupText = setupText + " | " + state.RegimeLabel;
                        _analyticsUnifiedSetupValueText.Text = setupText;
                        _analyticsUnifiedSetupValueText.Foreground = ResolveAnalyticsSignalBrush(state.BiasScore);
                        _analyticsUnifiedSetupValueText.ToolTip = string.IsNullOrWhiteSpace(state.RegimeLabel)
                            ? state.SetupLabel
                            : state.SetupLabel + " | " + state.RegimeLabel;
                    }

                    if (_analyticsUnifiedQualityValueText != null)
                    {
                        _analyticsUnifiedQualityValueText.Text =
                            state.Quality.ToString("0", CultureInfo.CurrentCulture) + "%";
                        _analyticsUnifiedQualityValueText.Foreground = ResolveQualityBrush(state.Quality);
                        _analyticsUnifiedQualityValueText.ToolTip =
                            "Confidence " +
                            state.Confidence.ToString("0", CultureInfo.CurrentCulture) +
                            "% | Tradeability " +
                            state.Tradeability.ToString("0", CultureInfo.CurrentCulture) +
                            "% | Agreement " +
                            (state.AgreementScore * 100.0).ToString("0", CultureInfo.CurrentCulture) +
                            "%";
                    }

                    if (_analyticsUnifiedGateValueText != null)
                    {
                        string actionText = ResolveDecisionActionText(state);
                        _analyticsUnifiedGateValueText.Text = actionText;
                        _analyticsUnifiedGateValueText.Foreground = ResolveGateBrush(actionText);
                        _analyticsUnifiedGateValueText.ToolTip = string.IsNullOrWhiteSpace(state.NoTradeReasons)
                            ? null
                            : (object)state.NoTradeReasons;
                    }
                }

                private AnalyticsUnifiedSignalState BuildGlobalDecisionSignalState(IReadOnlyDictionary<int, GlitchTimeframeReading> readings)
                {
                    GlitchTimeframeReading oneMinute = TryGetReading(readings, 1);
                    GlitchTimeframeReading fiveMinute = TryGetReading(readings, 5);
                    GlitchTimeframeReading fifteenMinute = TryGetReading(readings, 15);
                    GlitchTimeframeReading sixtyMinute = TryGetReading(readings, 60);

                    bool hasLive = HasDecisionReading(oneMinute) ||
                                   HasDecisionReading(fiveMinute) ||
                                   HasDecisionReading(fifteenMinute) ||
                                   HasDecisionReading(sixtyMinute);
                    if (!hasLive)
                    {
                        return new AnalyticsUnifiedSignalState
                        {
                            BiasLabel = L("analytics.unified.bias.neutral", "Neutral"),
                            BiasScore = 0,
                            ExecutionSignalLabel = L("analytics.signal.neutral", "Neutral"),
                            ExecutionScore = 0,
                            SetupLabel = L("analytics.unified.setup.flat", "Flat"),
                            Confidence = 0,
                            Tradeability = 0,
                            Quality = 0,
                            AgreementScore = 0,
                            GateLabel = L("analytics.unified.action.no_trade", "No Trade"),
                            IsActionable = false,
                            RegimeLabel = string.Empty,
                            NoTradeReasons = string.Empty
                        };
                    }

                    int executionMinutes = ResolveSelectedExecutionTimeframeMinutes();
                    bool has60 = HasDecisionReading(sixtyMinute);
                    bool has15 = HasDecisionReading(fifteenMinute);
                    bool has5 = HasDecisionReading(fiveMinute);
                    bool has1 = HasDecisionReading(oneMinute);

                    AnalyticsDecisionComponentScore oneMinuteComponents = has1
                        ? BuildDecisionComponentScore(oneMinute)
                        : default(AnalyticsDecisionComponentScore);
                    AnalyticsDecisionComponentScore fiveMinuteComponents = has5
                        ? BuildDecisionComponentScore(fiveMinute)
                        : default(AnalyticsDecisionComponentScore);
                    AnalyticsDecisionComponentScore fifteenMinuteComponents = has15
                        ? BuildDecisionComponentScore(fifteenMinute)
                        : default(AnalyticsDecisionComponentScore);
                    AnalyticsDecisionComponentScore sixtyMinuteComponents = has60
                        ? BuildDecisionComponentScore(sixtyMinute)
                        : default(AnalyticsDecisionComponentScore);

                    double contextScore;
                    double triggerScore;
                    double microScore;
                    double contextAgreement;
                    double triggerAgreement;
                    double microAgreement;
                    double? adxProxy;

                    if (executionMinutes <= 1)
                    {
                        contextScore = ComputeDecisionWeightedScore(
                            has60 ? sixtyMinuteComponents.EffectiveScore : 0, has60 ? 0.50 : 0,
                            has15 ? fifteenMinuteComponents.EffectiveScore : 0, has15 ? 0.30 : 0,
                            has5 ? fiveMinuteComponents.EffectiveScore : 0, has5 ? 0.20 : 0);
                        if (!has60 && !has15 && has5)
                            contextScore = fiveMinuteComponents.EffectiveScore;

                        contextAgreement = ComputeDecisionWeightedScore(
                            has60 ? sixtyMinuteComponents.AgreementScore : 0, has60 ? 0.50 : 0,
                            has15 ? fifteenMinuteComponents.AgreementScore : 0, has15 ? 0.30 : 0,
                            has5 ? fiveMinuteComponents.AgreementScore : 0, has5 ? 0.20 : 0);
                        if (!has60 && !has15 && has5)
                            contextAgreement = fiveMinuteComponents.AgreementScore;

                        triggerScore = ComputeDecisionWeightedScore(
                            has1 ? oneMinuteComponents.EffectiveScore : 0, has1 ? 0.75 : 0,
                            has5 ? fiveMinuteComponents.EffectiveScore : 0, has5 ? 0.25 : 0,
                            0, 0);
                        if (!has1 && has5)
                            triggerScore = fiveMinuteComponents.EffectiveScore;

                        triggerAgreement = ComputeDecisionWeightedScore(
                            has1 ? oneMinuteComponents.AgreementScore : 0, has1 ? 0.75 : 0,
                            has5 ? fiveMinuteComponents.AgreementScore : 0, has5 ? 0.25 : 0,
                            0, 0);
                        if (!has1 && has5)
                            triggerAgreement = fiveMinuteComponents.AgreementScore;

                        microScore = has1 ? oneMinuteComponents.EffectiveScore : triggerScore;
                        microAgreement = has1 ? oneMinuteComponents.AgreementScore : triggerAgreement;
                        adxProxy = has5
                            ? fiveMinute.AdxProxy
                            : has15
                                ? fifteenMinute.AdxProxy
                                : has60
                                    ? sixtyMinute.AdxProxy
                                    : (double?)null;
                    }
                    else if (executionMinutes <= 5)
                    {
                        contextScore = ComputeDecisionWeightedScore(
                            has60 ? sixtyMinuteComponents.EffectiveScore : 0, has60 ? 0.60 : 0,
                            has15 ? fifteenMinuteComponents.EffectiveScore : 0, has15 ? 0.40 : 0,
                            0, 0);
                        if (!has60 && has15)
                            contextScore = fifteenMinuteComponents.EffectiveScore;

                        contextAgreement = ComputeDecisionWeightedScore(
                            has60 ? sixtyMinuteComponents.AgreementScore : 0, has60 ? 0.60 : 0,
                            has15 ? fifteenMinuteComponents.AgreementScore : 0, has15 ? 0.40 : 0,
                            0, 0);
                        if (!has60 && has15)
                            contextAgreement = fifteenMinuteComponents.AgreementScore;

                        triggerScore = ComputeDecisionWeightedScore(
                            has5 ? fiveMinuteComponents.EffectiveScore : 0, has5 ? 0.75 : 0,
                            has1 ? oneMinuteComponents.EffectiveScore : 0, has1 ? 0.25 : 0,
                            0, 0);
                        if (!has5 && has1)
                            triggerScore = oneMinuteComponents.EffectiveScore;

                        triggerAgreement = ComputeDecisionWeightedScore(
                            has5 ? fiveMinuteComponents.AgreementScore : 0, has5 ? 0.75 : 0,
                            has1 ? oneMinuteComponents.AgreementScore : 0, has1 ? 0.25 : 0,
                            0, 0);
                        if (!has5 && has1)
                            triggerAgreement = oneMinuteComponents.AgreementScore;

                        microScore = has1 ? oneMinuteComponents.EffectiveScore : triggerScore;
                        microAgreement = has1 ? oneMinuteComponents.AgreementScore : triggerAgreement;
                        adxProxy = has5
                            ? fiveMinute.AdxProxy
                            : has15
                                ? fifteenMinute.AdxProxy
                                : has60
                                    ? sixtyMinute.AdxProxy
                                    : (double?)null;
                    }
                    else if (executionMinutes <= 15)
                    {
                        contextScore = has60 ? sixtyMinuteComponents.EffectiveScore : (has15 ? fifteenMinuteComponents.EffectiveScore : 0);
                        contextAgreement = has60 ? sixtyMinuteComponents.AgreementScore : (has15 ? fifteenMinuteComponents.AgreementScore : 0);
                        triggerScore = ComputeDecisionWeightedScore(
                            has15 ? fifteenMinuteComponents.EffectiveScore : 0, has15 ? 0.80 : 0,
                            has5 ? fiveMinuteComponents.EffectiveScore : 0, has5 ? 0.20 : 0,
                            0, 0);
                        if (!has15 && has5)
                            triggerScore = fiveMinuteComponents.EffectiveScore;

                        triggerAgreement = ComputeDecisionWeightedScore(
                            has15 ? fifteenMinuteComponents.AgreementScore : 0, has15 ? 0.80 : 0,
                            has5 ? fiveMinuteComponents.AgreementScore : 0, has5 ? 0.20 : 0,
                            0, 0);
                        if (!has15 && has5)
                            triggerAgreement = fiveMinuteComponents.AgreementScore;

                        microScore = has5 ? fiveMinuteComponents.EffectiveScore : triggerScore;
                        microAgreement = has5 ? fiveMinuteComponents.AgreementScore : triggerAgreement;
                        adxProxy = has15
                            ? fifteenMinute.AdxProxy
                            : has60
                                ? sixtyMinute.AdxProxy
                                : has5
                                    ? fiveMinute.AdxProxy
                                    : (double?)null;
                    }
                    else
                    {
                        contextScore = has60 ? sixtyMinuteComponents.EffectiveScore : 0;
                        contextAgreement = has60 ? sixtyMinuteComponents.AgreementScore : 0;
                        triggerScore = ComputeDecisionWeightedScore(
                            has60 ? sixtyMinuteComponents.EffectiveScore : 0, has60 ? 0.85 : 0,
                            has15 ? fifteenMinuteComponents.EffectiveScore : 0, has15 ? 0.15 : 0,
                            0, 0);
                        if (!has60 && has15)
                            triggerScore = fifteenMinuteComponents.EffectiveScore;

                        triggerAgreement = ComputeDecisionWeightedScore(
                            has60 ? sixtyMinuteComponents.AgreementScore : 0, has60 ? 0.85 : 0,
                            has15 ? fifteenMinuteComponents.AgreementScore : 0, has15 ? 0.15 : 0,
                            0, 0);
                        if (!has60 && has15)
                            triggerAgreement = fifteenMinuteComponents.AgreementScore;

                        microScore = has15 ? fifteenMinuteComponents.EffectiveScore : triggerScore;
                        microAgreement = has15 ? fifteenMinuteComponents.AgreementScore : triggerAgreement;
                        adxProxy = has60
                            ? sixtyMinute.AdxProxy
                            : has15
                                ? fifteenMinute.AdxProxy
                                : (double?)null;
                    }

                    GlitchTimeframeReading executionReading = TryGetReading(readings, executionMinutes);
                    return BuildDecisionSignalState(
                        contextScore,
                        triggerScore,
                        microScore,
                        contextAgreement,
                        triggerAgreement,
                        microAgreement,
                        adxProxy,
                        executionReading);
                }

                private AnalyticsUnifiedSignalState BuildDecisionSignalState(
                    double contextScore,
                    double triggerScore,
                    double microScore,
                    double contextAgreement,
                    double triggerAgreement,
                    double microAgreement,
                    double? adxProxy,
                    GlitchTimeframeReading executionReading)
                {
                    contextScore = NormalizeDecisionScore(contextScore);
                    triggerScore = NormalizeDecisionScore(triggerScore);
                    microScore = NormalizeDecisionScore(microScore);
                    contextAgreement = ClampAnalyticsUnitScore(contextAgreement);
                    triggerAgreement = ClampAnalyticsUnitScore(triggerAgreement);
                    microAgreement = ClampAnalyticsUnitScore(microAgreement);

                    int contextSign = ResolveDecisionScoreSign(contextScore);
                    int triggerSign = ResolveDecisionScoreSign(triggerScore);
                    int microSign = ResolveDecisionScoreSign(microScore);
                    int biasSign = contextSign != 0 ? contextSign : triggerSign;
                    double biasScore = NormalizeDecisionScore(contextSign != 0 ? contextScore : triggerScore);

                    double triggerStrength = Math.Min(1.0, Math.Abs(triggerScore));
                    double biasStrength = Math.Min(1.0, Math.Abs(biasScore));
                    double timeframeAlignment = ClampAnalyticsUnitScore(
                        (ResolveDirectionalAlignmentScore(biasSign, contextScore) * 0.45) +
                        (ResolveDirectionalAlignmentScore(biasSign, triggerScore) * 0.40) +
                        (ResolveDirectionalAlignmentScore(biasSign, microScore) * 0.15));
                    double agreementScore = ClampAnalyticsUnitScore(
                        (contextAgreement * 0.35) +
                        (triggerAgreement * 0.45) +
                        (microAgreement * 0.20));
                    double divergence = Math.Min(1.0, Math.Abs(triggerScore - contextScore));

                    int setupKind;
                    if (biasSign == 0 || (contextSign == 0 && triggerSign == 0))
                    {
                        setupKind = 0;
                    }
                    else if (contextSign == biasSign &&
                             triggerSign == biasSign &&
                             timeframeAlignment >= 0.58 &&
                             agreementScore >= 0.52)
                    {
                        setupKind = 1;
                    }
                    else if (contextSign == biasSign &&
                             (triggerSign == 0 || triggerSign == -biasSign) &&
                             microSign == biasSign &&
                             agreementScore >= 0.46)
                    {
                        setupKind = 2;
                    }
                    else if (contextSign != 0 && triggerSign != 0 && contextSign != triggerSign)
                    {
                        setupKind = 3;
                    }
                    else if (triggerSign == biasSign && agreementScore >= 0.45)
                    {
                        setupKind = 1;
                    }
                    else
                    {
                        setupKind = 2;
                    }
                    string setupLabel = ResolveDecisionSetupLabel(setupKind);

                    double adxNorm = 0;
                    if (adxProxy.HasValue && !double.IsNaN(adxProxy.Value) && !double.IsInfinity(adxProxy.Value))
                    {
                        adxNorm = (adxProxy.Value - 15.0) / 25.0;
                        adxNorm = ClampAnalyticsUnitScore(adxNorm);
                    }

                    double setupScore;
                    if (setupKind == 1)
                    {
                        setupScore = 1.0;
                    }
                    else if (setupKind == 2)
                    {
                        setupScore = 0.72;
                    }
                    else if (setupKind == 3)
                    {
                        setupScore = 0.45;
                    }
                    else
                    {
                        setupScore = 0.20;
                    }

                    double confidenceRaw =
                        (timeframeAlignment * 0.30) +
                        (agreementScore * 0.24) +
                        (triggerStrength * 0.18) +
                        (biasStrength * 0.10) +
                        (adxNorm * 0.08) +
                        (setupScore * 0.10);
                    if (setupKind == 3)
                        confidenceRaw -= 0.05;
                    if (setupKind == 2)
                        confidenceRaw -= (0.05 * divergence);
                    confidenceRaw = ClampAnalyticsUnitScore(confidenceRaw);

                    double tradeabilityRaw = ClampAnalyticsUnitScore(
                        (triggerStrength * 0.42) +
                        (agreementScore * 0.24) +
                        (timeframeAlignment * 0.20) +
                        (adxNorm * 0.14));
                    if (executionReading != null &&
                        executionReading.TradeabilityScore.HasValue &&
                        !double.IsNaN(executionReading.TradeabilityScore.Value) &&
                        !double.IsInfinity(executionReading.TradeabilityScore.Value))
                    {
                        double bridgeTradeability = ClampAnalyticsUnitScore(executionReading.TradeabilityScore.Value);
                        tradeabilityRaw = ClampAnalyticsUnitScore((tradeabilityRaw * 0.45) + (bridgeTradeability * 0.55));
                    }

                    int confidence = (int)Math.Round(
                        ClampAnalyticsUnitScore((confidenceRaw * 0.80) + (tradeabilityRaw * 0.20)) * 100.0,
                        MidpointRounding.AwayFromZero);
                    int tradeability = (int)Math.Round(tradeabilityRaw * 100.0, MidpointRounding.AwayFromZero);
                    if (confidence > 99)
                        confidence = 99;
                    if (tradeability > 99)
                        tradeability = 99;

                    double qualityRaw =
                        (confidenceRaw * 0.44) +
                        (tradeabilityRaw * 0.36) +
                        (agreementScore * 0.20);
                    int quality = (int)Math.Round(ClampAnalyticsUnitScore(qualityRaw) * 100.0, MidpointRounding.AwayFromZero);
                    if (quality > 99)
                        quality = 99;

                    bool hasDirectionalBias = biasSign != 0 && biasStrength >= 0.14;
                    bool strongAlignment = timeframeAlignment >= 0.62 && agreementScore >= 0.58;
                    bool moderateAlignment = timeframeAlignment >= 0.52 && agreementScore >= 0.48;
                    bool strongQuality = quality >= 60 && confidence >= 58 && tradeability >= 52;
                    bool moderateQuality = quality >= 42 && confidence >= 38 && tradeability >= 30;
                    bool strongEdge = triggerStrength >= 0.25;
                    bool moderateEdge = triggerStrength >= 0.12;
                    int gateKind;
                    if (!hasDirectionalBias || setupKind == 0)
                    {
                        gateKind = 0;
                    }
                    else if (setupKind == 3)
                    {
                        gateKind = (strongAlignment && strongQuality && strongEdge && quality >= 70) ? 1 : 0;
                    }
                    else if (strongAlignment && strongQuality && strongEdge)
                    {
                        gateKind = 2;
                    }
                    else if (moderateAlignment && moderateQuality && moderateEdge)
                    {
                        gateKind = 1;
                    }
                    else
                    {
                        gateKind = 0;
                    }
                    bool actionable = gateKind == 2;

                    double executionScore = triggerScore;
                    if (executionReading != null)
                    {
                        AnalyticsDecisionComponentScore executionComponents = BuildDecisionComponentScore(executionReading);
                        executionScore = executionComponents.EffectiveScore;
                    }
                    executionScore = NormalizeDecisionScore(executionScore);
                    string executionSignalLabel = GlitchSignalScale.ToLabel(executionScore);

                    string regimeLabel = executionReading == null
                        ? string.Empty
                        : (executionReading.RegimeLabel ?? string.Empty);
                    string noTradeReasons = executionReading == null
                        ? string.Empty
                        : (executionReading.NoTradeReasons ?? string.Empty);
                    if (gateKind == 2)
                    {
                        noTradeReasons = string.Empty;
                    }
                    else
                    {
                        var reasons = new List<string>();
                        if (biasSign == 0)
                            reasons.Add("neutral bias");
                        if (agreementScore < 0.48)
                            reasons.Add("low component agreement");
                        if (timeframeAlignment < 0.52)
                            reasons.Add("timeframe conflict");
                        if (triggerStrength < 0.15)
                            reasons.Add("low directional edge");
                        if (tradeability < 35)
                            reasons.Add("low tradeability");
                        if (confidence < 40)
                            reasons.Add("low confidence");
                        if (setupKind == 3)
                            reasons.Add("reversal setup");
                        if (!string.IsNullOrWhiteSpace(noTradeReasons))
                            reasons.Add(noTradeReasons);

                        noTradeReasons = string.Join(
                            ", ",
                            reasons
                                .Where(x => !string.IsNullOrWhiteSpace(x))
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .Take(3));
                    }

                    return new AnalyticsUnifiedSignalState
                    {
                        BiasLabel = ResolveDecisionBiasLabel(biasSign),
                        BiasScore = biasScore,
                        ExecutionSignalLabel = executionSignalLabel,
                        ExecutionScore = executionScore,
                        SetupLabel = setupLabel,
                        Confidence = confidence,
                        Tradeability = tradeability,
                        Quality = quality,
                        AgreementScore = agreementScore,
                        GateLabel = ResolveDecisionGateLabel(gateKind),
                        IsActionable = actionable,
                        RegimeLabel = regimeLabel,
                        NoTradeReasons = noTradeReasons
                    };
                }

                private void ApplyAnalyticsDecisionDetails(AnalyticsTimeframeMetricVisual visual, AnalyticsUnifiedSignalState state)
                {
                    if (visual == null || state == null)
                        return;

                    if (visual.BiasValueText != null)
                    {
                        visual.BiasValueText.Text =
                            state.BiasLabel +
                            " (" +
                            state.BiasScore.ToString("+0.00;-0.00;0.00", CultureInfo.CurrentCulture) +
                            ")";
                        visual.BiasValueText.Foreground = ResolveAnalyticsSignalBrush(state.BiasScore);
                    }

                    if (visual.SetupValueText != null)
                    {
                        string setupText = state.SetupLabel;
                        if (!string.IsNullOrWhiteSpace(state.RegimeLabel))
                            setupText = setupText + " | " + state.RegimeLabel;
                        visual.SetupValueText.Text = setupText;
                        visual.SetupValueText.Foreground = ResolveAnalyticsSignalBrush(state.BiasScore);
                    }

                    if (visual.QualityValueText != null)
                    {
                        visual.QualityValueText.Text =
                            state.Quality.ToString("0", CultureInfo.CurrentCulture) + "%";
                        visual.QualityValueText.Foreground = ResolveQualityBrush(state.Quality);
                        visual.QualityValueText.ToolTip =
                            "Confidence " +
                            state.Confidence.ToString("0", CultureInfo.CurrentCulture) +
                            "% | Tradeability " +
                            state.Tradeability.ToString("0", CultureInfo.CurrentCulture) +
                            "% | Agreement " +
                            (state.AgreementScore * 100.0).ToString("0", CultureInfo.CurrentCulture) +
                            "%";
                    }

                    if (visual.GateValueText != null)
                    {
                        string actionText = ResolveDecisionActionText(state);
                        visual.GateValueText.Text = actionText;
                        visual.GateValueText.Foreground = ResolveGateBrush(actionText);
                        visual.GateValueText.ToolTip = string.IsNullOrWhiteSpace(state.NoTradeReasons)
                            ? null
                            : (object)state.NoTradeReasons;
                    }
                }

                private Brush ResolveQualityBrush(int quality)
                {
                    if (quality >= 65)
                        return TealAccentBrush;
                    if (quality <= 35)
                        return ResolveAnalyticsSignalBrush(-0.20);
                    return _analyticsCurrentPriceText?.Foreground ?? Brushes.White;
                }

                private Brush ResolveGateBrush(string gateLabel)
                {
                    string normalized = (gateLabel ?? string.Empty).Trim();
                    if (normalized.Equals(L("analytics.unified.action.execute", "Execute"), StringComparison.OrdinalIgnoreCase) ||
                        normalized.Equals(L("analytics.unified.action.execute_long", "Execute Long"), StringComparison.OrdinalIgnoreCase) ||
                        normalized.Equals(L("analytics.unified.action.execute_short", "Execute Short"), StringComparison.OrdinalIgnoreCase) ||
                        normalized.Equals(L("analytics.unified.gate.go", "Go"), StringComparison.OrdinalIgnoreCase) ||
                        normalized.Equals("Execute", StringComparison.OrdinalIgnoreCase) ||
                        normalized.Equals("Execute Long", StringComparison.OrdinalIgnoreCase) ||
                        normalized.Equals("Execute Short", StringComparison.OrdinalIgnoreCase) ||
                        normalized.Equals("Go", StringComparison.OrdinalIgnoreCase))
                        return TealAccentBrush;
                    if (normalized.Equals(L("analytics.unified.action.no_trade", "No Trade"), StringComparison.OrdinalIgnoreCase) ||
                        normalized.Equals(L("analytics.unified.gate.pass", "Pass"), StringComparison.OrdinalIgnoreCase) ||
                        normalized.Equals("No Trade", StringComparison.OrdinalIgnoreCase) ||
                        normalized.Equals("Pass", StringComparison.OrdinalIgnoreCase))
                        return ResolveAnalyticsSignalBrush(-0.20);
                    if (normalized.Equals(L("analytics.unified.action.wait", "Wait"), StringComparison.OrdinalIgnoreCase) ||
                        normalized.Equals(L("analytics.unified.gate.watch", "Watch"), StringComparison.OrdinalIgnoreCase) ||
                        normalized.Equals("Wait", StringComparison.OrdinalIgnoreCase) ||
                        normalized.Equals("Watch", StringComparison.OrdinalIgnoreCase))
                        return _analyticsCurrentPriceText?.Foreground ?? Brushes.White;

                    return _analyticsCurrentPriceText?.Foreground ?? Brushes.White;
                }

                private string ResolveDecisionActionText(AnalyticsUnifiedSignalState state)
                {
                    if (state == null)
                        return L("analytics.unified.action.no_trade", "No Trade");

                    if (state.IsActionable)
                    {
                        if (state.BiasScore >= AnalyticsUnifiedNeutralThreshold)
                            return L("analytics.unified.action.execute_long", "Execute Long");
                        if (state.BiasScore <= -AnalyticsUnifiedNeutralThreshold)
                            return L("analytics.unified.action.execute_short", "Execute Short");
                        return L("analytics.unified.action.execute", "Execute");
                    }

                    string normalized = (state.GateLabel ?? string.Empty).Trim();
                    if (normalized.Equals(L("analytics.unified.action.wait", "Wait"), StringComparison.OrdinalIgnoreCase) ||
                        normalized.Equals(L("analytics.unified.gate.watch", "Watch"), StringComparison.OrdinalIgnoreCase) ||
                        normalized.Equals("Wait", StringComparison.OrdinalIgnoreCase) ||
                        normalized.Equals("Watch", StringComparison.OrdinalIgnoreCase))
                        return L("analytics.unified.action.wait", "Wait");

                    return L("analytics.unified.action.no_trade", "No Trade");
                }

                private static int ResolveDecisionAnchorTimeframe(int timeframeMinutes)
                {
                    if (timeframeMinutes <= 1)
                        return 5;
                    if (timeframeMinutes <= 5)
                        return 15;
                    if (timeframeMinutes <= 15)
                        return 60;
                    return 60;
                }

                private static GlitchTimeframeReading TryGetReading(IReadOnlyDictionary<int, GlitchTimeframeReading> readings, int timeframeMinutes)
                {
                    if (readings == null)
                        return null;

                    readings.TryGetValue(timeframeMinutes, out GlitchTimeframeReading reading);
                    return reading;
                }

                private static bool HasDecisionReading(GlitchTimeframeReading reading)
                {
                    if (reading == null)
                        return false;

                    return !IsAwaitingFeedSignal(reading.SignalLabel);
                }

                private string ResolveDisplaySignalLabel(string signalLabel, double score)
                {
                    if (IsAwaitingFeedSignal(signalLabel))
                        return L("analytics.signal.neutral", "Neutral");

                    if (!string.IsNullOrWhiteSpace(signalLabel))
                        return signalLabel;

                    return GlitchSignalScale.ToLabel(score);
                }

                private static bool IsAwaitingFeedSignal(string signalLabel)
                {
                    string normalized = (signalLabel ?? string.Empty).Trim();
                    if (normalized.Length == 0)
                        return true;

                    return normalized.IndexOf("awaiting feed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           normalized.IndexOf("no live chart feed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           normalized.IndexOf("attach glitchanalyticsbridge", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           normalized.IndexOf("awaiting live samples", StringComparison.OrdinalIgnoreCase) >= 0;
                }

                private static int ResolveDecisionScoreSign(double score)
                {
                    if (score >= AnalyticsUnifiedNeutralThreshold)
                        return 1;
                    if (score <= -AnalyticsUnifiedNeutralThreshold)
                        return -1;
                    return 0;
                }

                private static double NormalizeDecisionScore(double score)
                {
                    if (double.IsNaN(score) || double.IsInfinity(score))
                        return 0;

                    if (score < -1.0)
                        return -1.0;
                    if (score > 1.0)
                        return 1.0;
                    return score;
                }

                private static double ClampAnalyticsUnitScore(double value)
                {
                    if (double.IsNaN(value) || double.IsInfinity(value))
                        return 0;
                    if (value <= 0)
                        return 0;
                    if (value >= 1)
                        return 1;
                    return value;
                }

                private static double ResolveDirectionalAlignmentScore(int biasSign, double score)
                {
                    double normalizedScore = NormalizeDecisionScore(score);
                    if (biasSign == 0)
                        return 0.50;

                    double aligned = (1.0 + (biasSign * normalizedScore)) * 0.50;
                    return ClampAnalyticsUnitScore(aligned);
                }

                private string ResolveDecisionBiasLabel(int sign)
                {
                    if (sign > 0)
                        return L("analytics.unified.bias.long", "Long");
                    if (sign < 0)
                        return L("analytics.unified.bias.short", "Short");
                    return L("analytics.unified.bias.neutral", "Neutral");
                }

                private string ResolveDecisionSetupLabel(int setupKind)
                {
                    if (setupKind == 1)
                        return L("analytics.unified.setup.continue", "Continue");
                    if (setupKind == 2)
                        return L("analytics.unified.setup.pullback", "Pullback");
                    if (setupKind == 3)
                        return L("analytics.unified.setup.reversal", "Reversal");
                    return L("analytics.unified.setup.flat", "Flat");
                }

                private string ResolveDecisionGateLabel(int gateKind)
                {
                    if (gateKind >= 2)
                        return L("analytics.unified.action.execute", "Execute");
                    if (gateKind == 1)
                        return L("analytics.unified.action.wait", "Wait");
                    return L("analytics.unified.action.no_trade", "No Trade");
                }

                private static double ComputeDecisionWeightedScore(
                    double valueA,
                    double weightA,
                    double valueB,
                    double weightB,
                    double valueC,
                    double weightC)
                {
                    double sumWeights = 0;
                    double sum = 0;

                    if (weightA > 0)
                    {
                        sum += valueA * weightA;
                        sumWeights += weightA;
                    }
                    if (weightB > 0)
                    {
                        sum += valueB * weightB;
                        sumWeights += weightB;
                    }
                    if (weightC > 0)
                    {
                        sum += valueC * weightC;
                        sumWeights += weightC;
                    }

                    if (sumWeights <= 1e-8)
                        return 0;
                    return sum / sumWeights;
                }

                private static double ComputeDecisionWeightedScore(
                    double valueA,
                    double weightA,
                    double valueB,
                    double weightB,
                    double valueC,
                    double weightC,
                    double valueD,
                    double weightD)
                {
                    double sumWeights = 0;
                    double sum = 0;

                    if (weightA > 0)
                    {
                        sum += valueA * weightA;
                        sumWeights += weightA;
                    }
                    if (weightB > 0)
                    {
                        sum += valueB * weightB;
                        sumWeights += weightB;
                    }
                    if (weightC > 0)
                    {
                        sum += valueC * weightC;
                        sumWeights += weightC;
                    }
                    if (weightD > 0)
                    {
                        sum += valueD * weightD;
                        sumWeights += weightD;
                    }

                    if (sumWeights <= 1e-8)
                        return 0;
                    return sum / sumWeights;
                }

                private AnalyticsDecisionComponentScore BuildDecisionComponentScore(GlitchTimeframeReading reading)
                {
                    if (reading == null)
                        return default(AnalyticsDecisionComponentScore);

                    double finalScore = NormalizeDecisionScore(reading.Score);
                    double rawScore = NormalizeDecisionScore(reading.RawScore);
                    bool hasMa = reading.MaCompositeScore.HasValue;
                    bool hasOsc = reading.OscillatorCompositeScore.HasValue;
                    bool hasOrderFlow = reading.OrderFlowScore.HasValue;

                    double maScore = hasMa ? NormalizeDecisionScore(reading.MaCompositeScore.Value) : 0;
                    double oscScore = hasOsc ? NormalizeDecisionScore(reading.OscillatorCompositeScore.Value) : 0;
                    double orderFlowScore = hasOrderFlow ? NormalizeDecisionScore(reading.OrderFlowScore.Value) : 0;
                    double orderFlowReliability = hasOrderFlow
                        ? ClampAnalyticsUnitScore(reading.OrderFlowReliability ?? 0.50)
                        : 0;

                    double rawWeight = AnalyticsComponentRawWeight;
                    double maWeight = hasMa ? AnalyticsComponentMaWeight : 0;
                    double oscWeight = hasOsc ? AnalyticsComponentOscWeight : 0;
                    double orderFlowWeight = hasOrderFlow
                        ? AnalyticsComponentOrderFlowWeight * (0.60 + (0.40 * orderFlowReliability))
                        : 0;

                    double technicalScore = ComputeDecisionWeightedScore(
                        rawScore, rawWeight,
                        maScore, maWeight,
                        oscScore, oscWeight,
                        orderFlowScore, orderFlowWeight);

                    double netScore =
                        (rawScore * rawWeight) +
                        (maScore * maWeight) +
                        (oscScore * oscWeight) +
                        (orderFlowScore * orderFlowWeight);
                    int directionSign = Math.Sign(netScore);

                    double alignedWeight = 0;
                    double opposedWeight = 0;
                    double weightedMagnitude = 0;
                    if (rawWeight > 0)
                    {
                        weightedMagnitude += Math.Abs(rawScore) * rawWeight;
                        if (directionSign != 0)
                        {
                            alignedWeight += Math.Max(0, directionSign * rawScore) * rawWeight;
                            opposedWeight += Math.Max(0, -directionSign * rawScore) * rawWeight;
                        }
                    }
                    if (maWeight > 0)
                    {
                        weightedMagnitude += Math.Abs(maScore) * maWeight;
                        if (directionSign != 0)
                        {
                            alignedWeight += Math.Max(0, directionSign * maScore) * maWeight;
                            opposedWeight += Math.Max(0, -directionSign * maScore) * maWeight;
                        }
                    }
                    if (oscWeight > 0)
                    {
                        weightedMagnitude += Math.Abs(oscScore) * oscWeight;
                        if (directionSign != 0)
                        {
                            alignedWeight += Math.Max(0, directionSign * oscScore) * oscWeight;
                            opposedWeight += Math.Max(0, -directionSign * oscScore) * oscWeight;
                        }
                    }
                    if (orderFlowWeight > 0)
                    {
                        weightedMagnitude += Math.Abs(orderFlowScore) * orderFlowWeight;
                        if (directionSign != 0)
                        {
                            alignedWeight += Math.Max(0, directionSign * orderFlowScore) * orderFlowWeight;
                            opposedWeight += Math.Max(0, -directionSign * orderFlowScore) * orderFlowWeight;
                        }
                    }

                    double activeWeight = rawWeight + maWeight + oscWeight + orderFlowWeight;
                    double maxWeight =
                        AnalyticsComponentRawWeight +
                        AnalyticsComponentMaWeight +
                        AnalyticsComponentOscWeight +
                        AnalyticsComponentOrderFlowWeight;
                    double coherence = (alignedWeight + opposedWeight) > 1e-8
                        ? alignedWeight / (alignedWeight + opposedWeight)
                        : 0.50;
                    double strength = activeWeight > 1e-8
                        ? ClampAnalyticsUnitScore(weightedMagnitude / activeWeight)
                        : 0;
                    double coverage = ClampAnalyticsUnitScore(activeWeight / maxWeight);
                    double agreementScore = directionSign == 0
                        ? 0
                        : ClampAnalyticsUnitScore(
                            ((coherence * 0.70) + (strength * 0.30)) *
                            ((coverage * 0.65) + 0.35));
                    double signedAgreement = directionSign == 0 ? 0 : (agreementScore * directionSign);

                    double effectiveScore = NormalizeDecisionScore(
                        (finalScore * 0.55) +
                        (technicalScore * 0.30) +
                        (signedAgreement * 0.15));

                    return new AnalyticsDecisionComponentScore
                    {
                        EffectiveScore = effectiveScore,
                        TechnicalScore = technicalScore,
                        AgreementScore = agreementScore,
                        SignedAgreementScore = signedAgreement
                    };
                }
        
                private void UpdateAnalyticsFactorVisual(AnalyticsFactorVisual visual, double factor, string text, double scaleMaxAbs = 1.0, string tooltip = null)
                {
                    if (visual == null)
                            return;
        
                    double clamped = ClampAnalyticsFactor(factor);
                    double safeScale = Math.Max(0.0001, Math.Abs(scaleMaxAbs));
                    double normalizedMagnitude = Math.Min(1.0, Math.Abs(factor) / safeScale);
                    // Mild gamma shaping reduces visual dominance when values pin near +/-1.
                    double shapedMagnitude = Math.Pow(normalizedMagnitude, AnalyticsFactorVisualGamma);
                    double magnitudeWidth = shapedMagnitude * visual.HalfWidth;
        
                    if (visual.LeftFill != null)
                        visual.LeftFill.Width = clamped < 0 ? magnitudeWidth : 0;
                    if (visual.RightFill != null)
                        visual.RightFill.Width = clamped > 0 ? magnitudeWidth : 0;
                    if (visual.ValueText != null)
                    {
                        visual.ValueText.Text = string.IsNullOrWhiteSpace(text) ? "-" : text;
                        visual.ValueText.Foreground = ResolveAnalyticsSignalBrush(clamped);
                    }
                    if (visual.RowHost != null)
                        visual.RowHost.ToolTip = string.IsNullOrWhiteSpace(tooltip) ? null : tooltip;
                }

                private static string BuildAnalyticsContributionTooltip(
                    string title,
                    string description,
                    double contribution,
                    IEnumerable<string> metrics)
                {
                    var lines = new List<string>();
                    if (!string.IsNullOrWhiteSpace(title))
                        lines.Add(title);
                    if (!string.IsNullOrWhiteSpace(description))
                        lines.Add(description);

                    lines.Add("Contribution: " + contribution.ToString("+0.00;-0.00;0.00", CultureInfo.CurrentCulture));

                    if (metrics != null)
                    {
                        foreach (string metric in metrics)
                        {
                            if (!string.IsNullOrWhiteSpace(metric))
                                lines.Add(metric);
                        }
                    }

                    return string.Join(Environment.NewLine, lines);
                }

                private static string BuildAnalyticsTooltipMetric(string label, double? value)
                {
                    if (string.IsNullOrWhiteSpace(label) || !value.HasValue)
                        return null;

                    return label + ": " + value.Value.ToString("+0.00;-0.00;0.00", CultureInfo.CurrentCulture);
                }

                private static void SetAnalyticsFactorVisibility(AnalyticsFactorVisual visual, bool isVisible)
                {
                    if (visual == null || visual.RowHost == null)
                        return;

                    visual.RowHost.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
                }

                private Style CreateAnalyticsCardToggleButtonStyle(FrameworkElement context)
                {
                    var baseStyle = FindSkinStyle(context, typeof(Button));
                    var style = new Style(typeof(Button), baseStyle);

                    ApplySkinSetter(style, Control.BackgroundProperty, context, "BackgroundTextInput", "BackgroundMainWindow", "GridEntireBackground");
                    ApplySkinSetter(style, Control.ForegroundProperty, context, "FontControlBrush", "FontTableBrush", "GridRowForeground");
                    ApplySkinSetter(style, Control.BorderBrushProperty, context, "BorderThinBrush", "TabControlBorderBrush");
                    style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
                    style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
                    style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
                    style.Setters.Add(new Setter(Control.CursorProperty, System.Windows.Input.Cursors.Hand));
                    style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
                    style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
                    style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));

                    double? tableFontSize = FindSkinDouble(context, "FontTableHeight", "FontControlHeight", "FontHeaderLevel4Height");
                    if (tableFontSize.HasValue)
                        style.Setters.Add(new Setter(Control.FontSizeProperty, Math.Max(10.0, tableFontSize.Value)));

                    var borderFactory = new FrameworkElementFactory(typeof(Border));
                    borderFactory.Name = "Chrome";
                    borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(999));
                    borderFactory.SetBinding(
                        Border.BackgroundProperty,
                        new Binding(nameof(Control.Background)) { RelativeSource = RelativeSource.TemplatedParent });
                    borderFactory.SetBinding(
                        Border.BorderBrushProperty,
                        new Binding(nameof(Control.BorderBrush)) { RelativeSource = RelativeSource.TemplatedParent });
                    borderFactory.SetBinding(
                        Border.BorderThicknessProperty,
                        new Binding(nameof(Control.BorderThickness)) { RelativeSource = RelativeSource.TemplatedParent });

                    var contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
                    contentFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
                    contentFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
                    contentFactory.SetBinding(
                        ContentPresenter.ContentProperty,
                        new Binding(nameof(ContentControl.Content)) { RelativeSource = RelativeSource.TemplatedParent });
                    contentFactory.SetBinding(
                        ContentPresenter.ContentTemplateProperty,
                        new Binding(nameof(ContentControl.ContentTemplate)) { RelativeSource = RelativeSource.TemplatedParent });
                    contentFactory.SetBinding(
                        ContentPresenter.MarginProperty,
                        new Binding(nameof(Control.Padding)) { RelativeSource = RelativeSource.TemplatedParent });
                    borderFactory.AppendChild(contentFactory);

                    var template = new ControlTemplate(typeof(Button))
                    {
                        VisualTree = borderFactory
                    };
                    style.Setters.Add(new Setter(Control.TemplateProperty, template));

                    string hoverBackgroundKey = FindSkinResourceKey(context, "BackgroundTableHeader", "BackgroundTextInput", "GridEntireBackground");
                    string hoverBorderKey = FindSkinResourceKey(context, "GridHeaderHighlight", "BorderThinBrush", "TabControlBorderBrush");
                    var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
                    if (!string.IsNullOrWhiteSpace(hoverBackgroundKey))
                        hoverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension(hoverBackgroundKey)));
                    if (!string.IsNullOrWhiteSpace(hoverBorderKey))
                        hoverTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension(hoverBorderKey)));
                    style.Triggers.Add(hoverTrigger);

                    return style;
                }

                private void ToggleAnalyticsCardExpansion(int timeframeMinutes)
                {
                    bool isExpanded = _analyticsExpandedCardStates.TryGetValue(timeframeMinutes, out bool current) && current;
                    ApplyAnalyticsCardExpansionState(timeframeMinutes, !isExpanded, true);
                }

                private void RefreshAnalyticsCardExpansionLayout()
                {
                    foreach (KeyValuePair<int, AnalyticsCardExpansionVisual> kvp in _analyticsCardExpansionVisuals)
                    {
                        bool isExpanded = _analyticsExpandedCardStates.TryGetValue(kvp.Key, out bool expanded) && expanded;
                        if (!isExpanded)
                            continue;

                        AnalyticsCardExpansionVisual visual = kvp.Value;
                        if (visual?.DetailsHost == null || visual.DetailsContent == null)
                            continue;

                        visual.DetailsHost.Visibility = Visibility.Visible;
                        visual.DetailsHost.MaxHeight = MeasureAnalyticsDetailsHeight(visual);
                        visual.DetailsHost.Opacity = 1;
                    }
                }

                private void ApplyAnalyticsCardExpansionState(int timeframeMinutes, bool isExpanded, bool animate)
                {
                    _analyticsExpandedCardStates[timeframeMinutes] = isExpanded;

                    if (!_analyticsCardExpansionVisuals.TryGetValue(timeframeMinutes, out AnalyticsCardExpansionVisual visual) || visual == null)
                        return;

                    if (visual.ToggleButton != null)
                        visual.ToggleButton.Content = isExpanded ? "-" : "+";

                    Border detailsHost = visual.DetailsHost;
                    if (detailsHost == null)
                        return;

                    detailsHost.BeginAnimation(FrameworkElement.MaxHeightProperty, null);
                    detailsHost.BeginAnimation(UIElement.OpacityProperty, null);

                    if (!isExpanded)
                    {
                        if (!animate || detailsHost.Visibility != Visibility.Visible)
                        {
                            detailsHost.MaxHeight = 0;
                            detailsHost.Opacity = 0;
                            detailsHost.Visibility = Visibility.Collapsed;
                            return;
                        }

                        double currentHeight = Math.Max(detailsHost.ActualHeight, MeasureAnalyticsDetailsHeight(visual));
                        var collapseHeight = new DoubleAnimation(currentHeight, 0, TimeSpan.FromMilliseconds(170))
                        {
                            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
                        };
                        collapseHeight.Completed += (_, __) =>
                        {
                            detailsHost.MaxHeight = 0;
                            detailsHost.Opacity = 0;
                            detailsHost.Visibility = Visibility.Collapsed;
                        };
                        var collapseOpacity = new DoubleAnimation(detailsHost.Opacity, 0, TimeSpan.FromMilliseconds(130))
                        {
                            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
                        };
                        detailsHost.BeginAnimation(FrameworkElement.MaxHeightProperty, collapseHeight);
                        detailsHost.BeginAnimation(UIElement.OpacityProperty, collapseOpacity);
                        return;
                    }

                    double targetHeight = MeasureAnalyticsDetailsHeight(visual);
                    detailsHost.Visibility = Visibility.Visible;

                    if (!animate)
                    {
                        detailsHost.MaxHeight = targetHeight;
                        detailsHost.Opacity = 1;
                        return;
                    }

                    detailsHost.MaxHeight = 0;
                    detailsHost.Opacity = 0;

                    var expandHeight = new DoubleAnimation(0, targetHeight, TimeSpan.FromMilliseconds(190))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    expandHeight.Completed += (_, __) => detailsHost.MaxHeight = targetHeight;
                    var expandOpacity = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };
                    detailsHost.BeginAnimation(FrameworkElement.MaxHeightProperty, expandHeight);
                    detailsHost.BeginAnimation(UIElement.OpacityProperty, expandOpacity);
                }

                private static double MeasureAnalyticsDetailsHeight(AnalyticsCardExpansionVisual visual)
                {
                    if (visual?.DetailsContent == null)
                        return 0;

                    double availableWidth = double.PositiveInfinity;
                    if (visual.DetailsHost != null && visual.DetailsHost.ActualWidth > 0)
                        availableWidth = visual.DetailsHost.ActualWidth;
                    else if (visual.Card != null && visual.Card.ActualWidth > 32)
                        availableWidth = visual.Card.ActualWidth - 32;

                    visual.DetailsContent.Measure(new Size(availableWidth, double.PositiveInfinity));
                    return Math.Max(0, visual.DetailsContent.DesiredSize.Height);
                }
        
                private static double ClampAnalyticsFactor(double value)
                {
                    if (double.IsNaN(value) || double.IsInfinity(value))
                        return 0;
                    if (value < -1.0)
                        return -1.0;
                    if (value > 1.0)
                        return 1.0;
                    return value;
                }
        
                private Brush ResolveAnalyticsSignalBrush(double score)
                {
                    if (score >= 0.10)
                        return TealAccentBrush;
                    if (score <= -0.10)
                        return OrangeAccentBrush;
                    return _analyticsCurrentPriceText?.Foreground ?? Brushes.White;
                }
        
                private static string FormatAnalyticsPrice(double? value)
                {
                    if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
                        return "-";
                    return "$ " + value.Value.ToString("N3", CultureInfo.CurrentCulture);
                }
        
                private static double? ResolveSessionAverage(double? sessionHigh, double? sessionLow)
                {
                    if (sessionHigh.HasValue && sessionLow.HasValue)
                        return (sessionHigh.Value + sessionLow.Value) / 2.0;
        
                    if (sessionHigh.HasValue)
                        return sessionHigh;
                    if (sessionLow.HasValue)
                        return sessionLow;
                    return null;
                }
        
                private void UpdateAnalyticsSessionRangeVisual(
                    string instrumentRoot,
                    double? sessionLow,
                    double? sessionAvg,
                    double? currentPrice,
                    double? sessionHigh)
                {
                    _analyticsSessionRangeInstrumentRoot = instrumentRoot;
                    _analyticsSessionRangeLowValue = sessionLow;
                    _analyticsSessionRangeAvgValue = sessionAvg;
                    _analyticsSessionRangeCurrentValue = currentPrice;
                    _analyticsSessionRangeHighValue = sessionHigh;
                    RenderAnalyticsSessionRangeVisual();
                }
        
                private void RenderAnalyticsSessionRangeVisual()
                {
                    if (_analyticsSessionRangeCanvas == null ||
                        _analyticsSessionRangeTrack == null ||
                        _analyticsSessionRangeAvgMarker == null ||
                        _analyticsSessionRangeCurrentMarker == null ||
                        _analyticsSessionRangeAvgText == null ||
                        _analyticsSessionRangeCurrentText == null)
                    {
                            return;
                    }
        
                    double width = _analyticsSessionRangeCanvas.ActualWidth;
                    if (width <= 1)
                            return;
        
                    const double leftPad = 8;
                    const double rightPad = 8;
                    const double trackTop = 24;
                    const double currentTextTop = 2;
                    const double avgTextTop = 36;
                    double trackWidth = Math.Max(10, width - leftPad - rightPad);
        
                    _analyticsSessionRangeTrack.Width = trackWidth;
                    Canvas.SetLeft(_analyticsSessionRangeTrack, leftPad);
                    Canvas.SetTop(_analyticsSessionRangeTrack, trackTop);
        
                    SetSessionRangeEdgeLabel(
                        _analyticsSessionRangeLowLabel,
                        L("analytics.session_range.low", "Session Low"),
                        FormatSessionRangeValue(_analyticsSessionRangeLowValue),
                        OrangeAccentBrush);
                    SetSessionRangeEdgeLabel(
                        _analyticsSessionRangeHighLabel,
                        L("analytics.session_range.high", "Session High"),
                        FormatSessionRangeValue(_analyticsSessionRangeHighValue),
                        TealAccentBrush);
        
                    bool hasRange =
                        _analyticsSessionRangeLowValue.HasValue &&
                        _analyticsSessionRangeHighValue.HasValue &&
                        _analyticsSessionRangeHighValue.Value > _analyticsSessionRangeLowValue.Value;
        
                    if (!hasRange)
                    {
                        _analyticsSessionRangeAvgMarker.Visibility = Visibility.Collapsed;
                        _analyticsSessionRangeCurrentMarker.Visibility = Visibility.Collapsed;
                        _analyticsSessionRangeCurrentText.Text = L("analytics.session_range.current", "Current") + " -";
                        _analyticsSessionRangeAvgText.Text = L("analytics.session_range.avg", "Avg") + " -";
                        _analyticsSessionRangeCurrentText.Foreground = ResolveSkinBrush("FontControlBrush", "FontTableBrush") ?? Brushes.White;
                        double centerX = leftPad + (trackWidth / 2.0);
                        double currentTextWidth = MeasureTextWidth(_analyticsSessionRangeCurrentText);
                        double avgTextWidth = MeasureTextWidth(_analyticsSessionRangeAvgText);
                        Canvas.SetLeft(
                            _analyticsSessionRangeCurrentText,
                            ClampToRange(centerX - (currentTextWidth / 2.0), leftPad, Math.Max(leftPad, width - rightPad - currentTextWidth)));
                        Canvas.SetTop(_analyticsSessionRangeCurrentText, currentTextTop);
                        Canvas.SetLeft(
                            _analyticsSessionRangeAvgText,
                            ClampToRange(centerX - (avgTextWidth / 2.0), leftPad, Math.Max(leftPad, width - rightPad - avgTextWidth)));
                        Canvas.SetTop(_analyticsSessionRangeAvgText, avgTextTop);
                            return;
                    }
        
                    double low = _analyticsSessionRangeLowValue.Value;
                    double high = _analyticsSessionRangeHighValue.Value;
                    double avg = _analyticsSessionRangeAvgValue ?? ((high + low) / 2.0);
                    double current = _analyticsSessionRangeCurrentValue ?? avg;
                    avg = ClampToRange(avg, low, high);
                    current = ClampToRange(current, low, high);
        
                    double avgRatio = (avg - low) / Math.Max(high - low, 1e-8);
                    double currentRatio = (current - low) / Math.Max(high - low, 1e-8);
                    double avgX = leftPad + (avgRatio * trackWidth);
                    double currentX = leftPad + (currentRatio * trackWidth);
        
                    _analyticsSessionRangeAvgMarker.Visibility = Visibility.Visible;
                    _analyticsSessionRangeCurrentMarker.Visibility = Visibility.Visible;
                    _analyticsSessionRangeCurrentMarker.Background = current >= avg ? TealAccentBrush : OrangeAccentBrush;
                    Canvas.SetLeft(_analyticsSessionRangeAvgMarker, avgX - (_analyticsSessionRangeAvgMarker.Width / 2.0));
                    Canvas.SetTop(_analyticsSessionRangeAvgMarker, trackTop - 5);
                    Canvas.SetLeft(_analyticsSessionRangeCurrentMarker, currentX - (_analyticsSessionRangeCurrentMarker.Width / 2.0));
                    Canvas.SetTop(_analyticsSessionRangeCurrentMarker, trackTop - 7);
        
                    _analyticsSessionRangeCurrentText.Text = L("analytics.session_range.current", "Current") + " " + FormatSessionRangeValue(current);
                    _analyticsSessionRangeAvgText.Text = L("analytics.session_range.avg", "Avg") + " " + FormatSessionRangeValue(avg);
                    _analyticsSessionRangeCurrentText.Foreground = _analyticsSessionRangeCurrentMarker.Background;
        
                    double currentTextMeasured = MeasureTextWidth(_analyticsSessionRangeCurrentText);
                    double avgTextMeasured = MeasureTextWidth(_analyticsSessionRangeAvgText);
                    Canvas.SetLeft(
                        _analyticsSessionRangeCurrentText,
                        ClampToRange(currentX - (currentTextMeasured / 2.0), leftPad, Math.Max(leftPad, width - rightPad - currentTextMeasured)));
                    Canvas.SetTop(_analyticsSessionRangeCurrentText, currentTextTop);
                    Canvas.SetLeft(
                        _analyticsSessionRangeAvgText,
                        ClampToRange(avgX - (avgTextMeasured / 2.0), leftPad, Math.Max(leftPad, width - rightPad - avgTextMeasured)));
                    Canvas.SetTop(_analyticsSessionRangeAvgText, avgTextTop);
                }
        
                private static double ClampToRange(double value, double min, double max)
                {
                    if (value < min)
                        return min;
                    if (value > max)
                        return max;
                    return value;
                }
        
                private static double MeasureTextWidth(TextBlock textBlock)
                {
                    if (textBlock == null)
                        return 0;
        
                    textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    return textBlock.DesiredSize.Width;
                }
        
                private void SetSessionRangeEdgeLabel(TextBlock host, string caption, string valueText, Brush valueBrush)
                {
                    if (host == null)
                            return;
        
                    Brush defaultBrush = ResolveSkinBrush("FontControlBrush", "FontTableBrush") ?? Brushes.White;
                    host.Inlines.Clear();
                    host.Inlines.Add(new Run((caption ?? "-") + Environment.NewLine)
                    {
                        Foreground = defaultBrush,
                        FontSize = host.FontSize > 0 ? host.FontSize : 10
                    });
                    host.Inlines.Add(new Run(string.IsNullOrWhiteSpace(valueText) ? "-" : valueText)
                    {
                        Foreground = valueBrush ?? defaultBrush,
                        FontWeight = FontWeights.SemiBold,
                        FontSize = host.FontSize > 0 ? host.FontSize : 10
                    });
                }
        
                private string FormatSessionRangeValue(double? value)
                {
                    if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
                        return "-";
        
                    bool pointBased = IsPointBasedInstrumentRoot(_analyticsSessionRangeInstrumentRoot);
                    return pointBased
                        ? value.Value.ToString("N0", CultureInfo.CurrentCulture)
                        : "$ " + value.Value.ToString("N0", CultureInfo.CurrentCulture);
                }
        
                private static bool IsPointBasedInstrumentRoot(string instrumentRoot)
                {
                    if (string.IsNullOrWhiteSpace(instrumentRoot))
                        return false;
        
                    string root = instrumentRoot.Trim().ToUpperInvariant();
                    int spaceIndex = root.IndexOf(' ');
                    if (spaceIndex > 0)
                        root = root.Substring(0, spaceIndex);
                    int dotIndex = root.IndexOf('.');
                    if (dotIndex > 0)
                        root = root.Substring(0, dotIndex);
        
                    switch (root)
                    {
                        case "MNQ":
                        case "NQ":
                        case "MES":
                        case "ES":
                        case "MYM":
                        case "YM":
                        case "M2K":
                        case "RTY":
                        case "MGC":
                        case "GC":
                        case "MCL":
                        case "CL":
                        case "SI":
                        case "HG":
                        case "NG":
                        case "ZB":
                        case "ZN":
                        case "ZF":
                        case "ZT":
                        case "6E":
                        case "6B":
                        case "6J":
                        case "6A":
                        case "6C":
                        case "6S":
                        case "6N":
                            return true;
                        default:
                            return false;
                    }
                }
        
                private static string FormatAnalyticsPriceWhole(double? value)
                {
                    if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
                        return "-";
                    return "$ " + value.Value.ToString("N0", CultureInfo.CurrentCulture);
                }
        
                private static string BuildAdxAtrSummary(double? adx, double? atr)
                {
                    string adxText = adx.HasValue
                        ? adx.Value.ToString("N1", CultureInfo.CurrentCulture)
                        : "-";
                    string atrText = atr.HasValue
                        ? atr.Value.ToString("N2", CultureInfo.CurrentCulture)
                        : "-";
                    return adxText + " | " + atrText;
                }

                private string BuildOrderFlowSummary(GlitchTimeframeReading reading)
                {
                    if (reading == null)
                        return L("analytics.order_flow.unavailable", "Order flow unavailable");

                    if (!string.IsNullOrWhiteSpace(reading.OrderFlowHint) &&
                        !reading.OrderFlowScore.HasValue &&
                        !reading.OrderFlowDeltaChange.HasValue)
                    {
                        return reading.OrderFlowHint;
                    }

                    if (!reading.OrderFlowScore.HasValue && !reading.OrderFlowDeltaChange.HasValue)
                        return L("analytics.order_flow.unavailable", "Order flow unavailable");

                    string scoreToken = reading.OrderFlowScore.HasValue
                        ? reading.OrderFlowScore.Value.ToString("+0.00;-0.00;0.00", CultureInfo.CurrentCulture)
                        : "-";
                    string confidenceToken = reading.OrderFlowConfidence.HasValue
                        ? reading.OrderFlowConfidence.Value.ToString("0.00", CultureInfo.CurrentCulture)
                        : "-";
                    string reliabilityToken = reading.OrderFlowReliability.HasValue
                        ? reading.OrderFlowReliability.Value.ToString("0.00", CultureInfo.CurrentCulture)
                        : "-";
                    string deltaToken = reading.OrderFlowDeltaChange.HasValue
                        ? reading.OrderFlowDeltaChange.Value.ToString("+0;-0;0", CultureInfo.CurrentCulture)
                        : "-";

                    string summary =
                        L("analytics.order_flow.prefix", "OrderFlow") +
                        " " +
                        scoreToken +
                        " | " +
                        L("analytics.order_flow.confidence", "Conf") +
                        " " +
                        confidenceToken +
                        " | Rel " +
                        reliabilityToken +
                        " | Delta " +
                        deltaToken;

                    if (!string.IsNullOrWhiteSpace(reading.NoTradeReasons))
                        summary += " | Gate " + reading.NoTradeReasons;

                    if (!string.IsNullOrWhiteSpace(reading.OrderFlowHint))
                        summary += " | " + reading.OrderFlowHint;

                    return summary;
                }

                private string BuildRegimeSummary(GlitchTimeframeReading reading)
                {
                    if (reading == null)
                        return L("analytics.awaiting_live_samples", "Awaiting live samples");

                    string regime = string.IsNullOrWhiteSpace(reading.RegimeLabel)
                        ? ResolveRegimeToken(reading.TrendHint)
                        : reading.RegimeLabel;
                    string volatility = ResolveVolatilityToken(reading.VolatilityHint);

                    if (string.IsNullOrWhiteSpace(regime) && string.IsNullOrWhiteSpace(volatility))
                        return L("analytics.awaiting_live_samples", "Awaiting live samples");
                    if (string.IsNullOrWhiteSpace(regime))
                        return volatility;
                    if (string.IsNullOrWhiteSpace(volatility))
                        return regime;

                    return regime + " " + L("analytics.regime.with", "with") + " " + volatility;
                }

                private string BuildRegimeSummary(string trendHint, string volatilityHint)
                {
                    string regime = ResolveRegimeToken(trendHint);
                    string volatility = ResolveVolatilityToken(volatilityHint);
        
                    if (string.IsNullOrWhiteSpace(regime) && string.IsNullOrWhiteSpace(volatility))
                        return L("analytics.awaiting_live_samples", "Awaiting live samples");
                    if (string.IsNullOrWhiteSpace(regime))
                        return volatility;
                    if (string.IsNullOrWhiteSpace(volatility))
                        return regime;
        
                    return regime + " " + L("analytics.regime.with", "with") + " " + volatility;
                }
        
                private string ResolveRegimeToken(string trendHint)
                {
                    if (string.IsNullOrWhiteSpace(trendHint))
                        return string.Empty;
        
                    if (trendHint.IndexOf("Trending", StringComparison.OrdinalIgnoreCase) >= 0)
                        return L("analytics.regime.trending", "Trending");
                    if (trendHint.IndexOf("Transitional", StringComparison.OrdinalIgnoreCase) >= 0)
                        return L("analytics.regime.transitional", "Transitional");
                    if (trendHint.IndexOf("Choppy", StringComparison.OrdinalIgnoreCase) >= 0)
                        return L("analytics.regime.choppy", "Choppy");
        
                    int separator = trendHint.IndexOf('|');
                    if (separator > 0)
                        return trendHint.Substring(0, separator).Trim();
        
                    return trendHint.Trim();
                }
        
                private string ResolveVolatilityToken(string volatilityHint)
                {
                    if (string.IsNullOrWhiteSpace(volatilityHint))
                        return string.Empty;
        
                    if (volatilityHint.IndexOf("High volatility", StringComparison.OrdinalIgnoreCase) >= 0)
                        return L("analytics.volatility.high", "High volatility");
                    if (volatilityHint.IndexOf("Moderate volatility", StringComparison.OrdinalIgnoreCase) >= 0)
                        return L("analytics.volatility.moderate", "Moderate volatility");
                    if (volatilityHint.IndexOf("Low volatility", StringComparison.OrdinalIgnoreCase) >= 0)
                        return L("analytics.volatility.low", "Low volatility");
        
                    return volatilityHint.Trim();
                }
        
                private static string JoinAnalyticsLines(IReadOnlyList<string> lines, string fallbackText)
                {
                    if (lines != null)
                    {
                        List<string> nonEmpty = lines
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .ToList();
                        if (nonEmpty.Count > 0)
                            return string.Join(Environment.NewLine, nonEmpty);
                    }
        
                    return string.IsNullOrWhiteSpace(fallbackText) ? "-" : fallbackText;
                }
        
                private void UpdateFundamentalKeyStatusText()
                {
                    if (_analyticsFundamentalFootnoteText == null)
                        return;

                    _analyticsFundamentalFootnoteText.Text = string.Empty;
                    _analyticsFundamentalFootnoteText.Visibility = Visibility.Collapsed;
                }

                private void UpdateTechnicalFeedStatusText(GlitchAnalyticsSnapshot snapshot)
                {
                    if (_analyticsTechnicalFeedFootnoteText == null)
                        return;

                    if (HasLiveTechnicalFeed(snapshot))
                    {
                        _analyticsTechnicalFeedFootnoteText.Text = string.Empty;
                        _analyticsTechnicalFeedFootnoteText.Visibility = Visibility.Collapsed;
                        return;
                    }

                    _analyticsTechnicalFeedFootnoteText.Text = L(
                        "analytics.technical.bridge_missing_cta",
                        "To enable Technical Analysis, add GlitchAnalyticsBridgeIndicator to a NinjaTrader chart. Data is available during market hours. If refresh is needed, re-apply the indicator on the chart.");
                    _analyticsTechnicalFeedFootnoteText.Visibility = Visibility.Visible;
                }

                private static bool HasLiveTechnicalFeed(GlitchAnalyticsSnapshot snapshot)
                {
                    if (snapshot?.TimeframeReadings == null)
                        return false;

                    foreach (GlitchTimeframeReading reading in snapshot.TimeframeReadings)
                    {
                        if (reading == null)
                            continue;

                        if (!IsAwaitingFeedSignal(reading.SignalLabel))
                            return true;
                    }

                    return false;
                }

        private async void OnAnalyticsOpenDetachedWindowClick(object sender, RoutedEventArgs e)
        {
            ShowAnalyticsDetachedWindow();
            if (sender is Button button)
                await ShowTransientTealButtonFeedbackAsync(button, L("analytics.button.opened_macro", "Opened Nasdaq Macro"), 3000);
        }

        private void ShowAnalyticsDetachedWindow()
        {
            if (_analyticsDetachedWindow != null)
            {
                _analyticsDetachedWindow.Show();
                _analyticsDetachedWindow.Activate();
                return;
            }

            var window = new GlitchTradingViewMacroWindow(this)
            {
                Caption = L("analytics.window.secondary_title", "Nasdaq Macro"),
                Title = L("analytics.window.secondary_title", "Nasdaq Macro")
            };
            window.Closed += OnAnalyticsDetachedWindowClosed;
            _analyticsDetachedWindow = window;
            _analyticsDetachedWindow.Show();
            _analyticsDetachedWindow.Activate();
        }

        private void OnAnalyticsDetachedWindowClosed(object sender, EventArgs e)
        {
            if (_analyticsDetachedWindow == null)
                return;

            _analyticsDetachedWindow.Closed -= OnAnalyticsDetachedWindowClosed;
            _analyticsDetachedWindow = null;
        }

        private void UpdateAnalyticsDetachedWindowButtonVisibility()
        {
            if (_analyticsOpenDetachedWindowButton == null)
                return;

            string instrument = ResolveAnalyticsInstrumentForMacroButton();

            _analyticsOpenDetachedWindowButton.Visibility = IsNasdaqMacroInstrument(instrument)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private string ResolveAnalyticsInstrumentForMacroButton()
        {
            string instrument = _selectedAnalyticsInstrument;
            if (string.IsNullOrWhiteSpace(instrument))
                instrument = _analyticsInstrumentCombo?.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(instrument))
                instrument = _analyticsInstrumentCombo?.Text;
            if (string.IsNullOrWhiteSpace(instrument))
                instrument = _analyticsSessionRangeInstrumentRoot;

            return instrument;
        }

        private static bool IsNasdaqMacroInstrument(string instrument)
        {
            if (string.IsNullOrWhiteSpace(instrument))
                return false;

            string root = instrument.Trim().ToUpperInvariant();
            int spaceIndex = root.IndexOf(' ');
            if (spaceIndex > 0)
                root = root.Substring(0, spaceIndex);
            int dotIndex = root.IndexOf('.');
            if (dotIndex > 0)
                root = root.Substring(0, dotIndex);
            int colonIndex = root.LastIndexOf(':');
            if (colonIndex >= 0 && colonIndex + 1 < root.Length)
                root = root.Substring(colonIndex + 1);

            if (root.StartsWith("@", StringComparison.Ordinal))
                root = root.Substring(1);

            switch (root)
            {
                case "NQ":
                case "MNQ":
                case "NDX":
                case "NAS100":
                case "US100":
                case "QQQ":
                case "AAPL":
                case "MSFT":
                case "NVDA":
                case "AMZN":
                case "META":
                case "GOOGL":
                case "TSLA":
                    return true;
                default:
                    return root.IndexOf("NASDAQ", StringComparison.Ordinal) >= 0;
            }
        }

                private bool CanAccessAnalyticsPremium(out string lockedMessage)
                {
                    lockedMessage = null;
                    string licenseKey = (_runtimePolicySettings?.LicenseKey ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(licenseKey))
                    {
                        lockedMessage = BuildPremiumAccessMessage("Analytics");
                        return false;
                    }

                    string status = (_licenseCacheState?.LastStatus ?? string.Empty).Trim();
                    if (!status.Equals("active", StringComparison.OrdinalIgnoreCase))
                    {
                        lockedMessage = BuildPremiumAccessMessage("Analytics");
                        return false;
                    }

                    bool analyticsEnabled = _licenseCacheState?.FeatureAnalytics ?? false;
                    if (!analyticsEnabled)
                    {
                        lockedMessage = "Premium required. Upgrade your license to unlock Analytics, Macro, and Strategy modules.";
                        return false;
                    }

                    return true;
                }

                private void ApplyAnalyticsLockedState(string lockedMessage)
                {
                    string cta = string.IsNullOrWhiteSpace(lockedMessage)
                        ? "Premium required. Upgrade to unlock Analytics."
                        : lockedMessage;

                    if (_analyticsCurrentPriceText != null)
                        _analyticsCurrentPriceText.Text = "-";
                    if (_analyticsSessionText != null)
                        _analyticsSessionText.Text = "-";
                    if (_analyticsOverallSignalText != null)
                    {
                        _analyticsOverallSignalText.Text = "Premium Locked";
                        _analyticsOverallSignalText.Foreground = OrangeAccentBrush;
                    }

                    if (_analyticsScoreSectionTitleText != null)
                        _analyticsScoreSectionTitleText.Text = L("analytics.block.instrument_scoring", "Instrument Scoring");
                    if (_analyticsEarningsAnalysisText != null)
                        _analyticsEarningsAnalysisText.Text = "-";
                    if (_analyticsOfficialNewsText != null)
                        _analyticsOfficialNewsText.Text = cta;
                    if (_analyticsLatestHeadlinesList != null)
                        _analyticsLatestHeadlinesList.ItemsSource = new[] { cta };
                    if (_analyticsFundamentalFootnoteText != null)
                    {
                        _analyticsFundamentalFootnoteText.Text = string.Empty;
                        _analyticsFundamentalFootnoteText.Visibility = Visibility.Collapsed;
                    }
                    if (_analyticsTechnicalFeedFootnoteText != null)
                        _analyticsTechnicalFeedFootnoteText.Visibility = Visibility.Collapsed;
                    if (_analyticsOpenDetachedWindowButton != null)
                        _analyticsOpenDetachedWindowButton.Visibility = Visibility.Collapsed;

                    UpdateAnalyticsSessionRangeVisual(_analyticsSessionRangeInstrumentRoot, null, null, null, null);
                    UpdateGlobalNewsLockoutBanner(false, null);
                    UpdateAnalyticsUnifiedSignal(null);
                    foreach (var kvp in _analyticsDialVisuals)
                        UpdateAnalyticsDialVisual(kvp.Value, null);
                    foreach (var kvp in _analyticsTimeframeVisuals)
                        UpdateAnalyticsTimeframeVisual(kvp.Value, null, null);
                }

                private Grid CreatePremiumSurfaceOverlay(FrameworkElement context, string surfaceName)
                {
                    var overlay = new Grid
                    {
                        Visibility = Visibility.Collapsed,
                        Background = new SolidColorBrush(Color.FromArgb(204, 0, 0, 0)),
                        IsHitTestVisible = true
                    };

                    var card = new Border
                    {
                        MaxWidth = 640,
                        Padding = new Thickness(20),
                        CornerRadius = new CornerRadius(6),
                        BorderThickness = new Thickness(1),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    ApplySkinResource(card, Border.BackgroundProperty, "BackgroundMainWindow", "GridEntireBackground", "BackgroundTextInput");
                    ApplySkinResource(card, Border.BorderBrushProperty, "BorderThinBrush", "TabControlBorderBrush");

                    var stack = new StackPanel { Orientation = Orientation.Vertical };
                    var title = new TextBlock
                    {
                        Text = L("overlay.license_required", "License Required"),
                        FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(0, 0, 0, 8)
                    };
                    RegisterLocalizationBinding(() => title.Text = L("overlay.license_required", "License Required"));
                    ApplySkinResource(title, TextBlock.ForegroundProperty, "FontHeaderLevel3Brush", "FontHeaderLevel4Brush", "FontControlBrush", "FontTableBrush");
                    stack.Children.Add(title);

                    _analyticsLicenseGateMessageText = new TextBlock
                    {
                        Text = L("overlay.premium_gate_message", "To enable premium features purchase your license below and validate it in the Settings tab."),
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 0, 0, 12)
                    };
                    RegisterLocalizationBinding(() => _analyticsLicenseGateMessageText.Text = L("overlay.premium_gate_message", "To enable premium features purchase your license below and validate it in the Settings tab."));
                    ApplySkinResource(_analyticsLicenseGateMessageText, TextBlock.ForegroundProperty, "FontControlBrush", "FontTableBrush");
                    stack.Children.Add(_analyticsLicenseGateMessageText);

                    var buttonRow = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Left
                    };

                    var checkoutButton = new Button
                    {
                        Content = L("overlay.button.get_license", "Get License"),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        MinWidth = 172,
                        Style = CreateGroupAddButtonStyle(context)
                    };
                    RegisterLocalizationBinding(() => checkoutButton.Content = L("overlay.button.get_license", "Get License"));
                    checkoutButton.Click += (_, __) => OpenAnalyticsExternalUrl("https://whop.com/checkout/plan_W6nOCfXPm7pka");
                    buttonRow.Children.Add(checkoutButton);

                    var memberAppButton = new Button
                    {
                        Content = L("overlay.button.manage_membership", "Manage Membership"),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        MinWidth = 172,
                        Margin = new Thickness(8, 0, 0, 0),
                        Style = CreateGroupActionButtonStyle(context)
                    };
                    RegisterLocalizationBinding(() => memberAppButton.Content = L("overlay.button.manage_membership", "Manage Membership"));
                    memberAppButton.Click += (_, __) => OpenAnalyticsExternalUrl("https://whop.com/joined/glitchtrader/glitch-download-h1FPM8xSe5zaYs/app/");
                    buttonRow.Children.Add(memberAppButton);
                    stack.Children.Add(buttonRow);

                    card.Child = stack;
                    overlay.Children.Add(card);
                    return overlay;
                }

                private void UpdateAnalyticsLicenseGateOverlay()
                {
                    if (_analyticsLicenseGateOverlay == null)
                        return;

                    if (!CanAccessAnalyticsPremium(out string lockedMessage))
                    {
                        if (_analyticsLicenseGateMessageText != null)
                            _analyticsLicenseGateMessageText.Text = L("overlay.premium_gate_message", "To enable premium features purchase your license below and validate it in the Settings tab.");
                        _analyticsLicenseGateOverlay.Visibility = Visibility.Visible;
                        return;
                    }

                    _analyticsLicenseGateOverlay.Visibility = Visibility.Collapsed;
                }

                private static void OpenAnalyticsExternalUrl(string url)
                {
                    if (string.IsNullOrWhiteSpace(url))
                        return;

                    try
                    {
                        var startInfo = new ProcessStartInfo
                        {
                            FileName = url,
                            UseShellExecute = true
                        };
                        Process.Start(startInfo);
                    }
                    catch
                    {
                    }
                }

    }
}




