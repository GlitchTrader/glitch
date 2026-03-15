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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
        private ICollectionView _journalEntriesView;
        private Grid _journalLicenseGateOverlay;
        private TextBlock _journalLicenseGateMessageText;

        private UIElement CreateJournalTabImpl()
        {
            var root = new Grid { Margin = new Thickness(20) };
            _journalRootGrid = root;
            _journalRootGrid.SizeChanged += OnJournalRootSizeChanged;
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0.95, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.35, GridUnitType.Star) });

            var header = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = new TextBlock
            {
                Text = L("journal.title", "Journal"),
                FontWeight = UiHeadingFontWeight
            };
            BindLocalizedText(title, "journal.title", "Journal");
            ApplySkinResource(title, TextBlock.ForegroundProperty, "FontControlBrush", "FontHeaderLevel3Brush", "FontHeaderLevel4Brush", "FontTableBrush");
            header.Children.Add(title);

            _summaryAsOfText = new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            ApplySkinResource(_summaryAsOfText, TextBlock.ForegroundProperty, "FontControlBrush", "FontTableBrush");
            Grid.SetColumn(_summaryAsOfText, 1);
            header.Children.Add(_summaryAsOfText);

            var resetDataButton = new Button
            {
                Content = L("summary.reset_data", "Reset Data"),
                Padding = new Thickness(10, 4, 10, 4),
                MinWidth = 118,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Style = CreateGroupActionButtonStyle(root)
            };
            BindLocalizedContent(resetDataButton, "summary.reset_data", "Reset Data");
            resetDataButton.Click += OnResetSummaryAndJournalDataClick;
            Grid.SetColumn(resetDataButton, 2);
            header.Children.Add(resetDataButton);

            Grid.SetRow(header, 0);
            root.Children.Add(header);

            var cards = new UniformGrid
            {
                Margin = new Thickness(0, 0, 0, 12),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            cards.Children.Add(CreateSummaryCard(root, "journal.card.total_trades", "Total Trades", out _summaryTradesValueText));
            cards.Children.Add(CreateSummaryCard(root, "summary.card.net_pnl", "Net PnL", out _summaryNetPointsValueText));
            cards.Children.Add(CreateSummaryCard(root, "summary.card.win_rate", "Win Rate", out _summaryWinRateValueText));
            cards.Children.Add(CreateSummaryCard(root, "journal.card.avg_win", "Avg Win", out _summaryFleetTradesValueText));
            cards.Children.Add(CreateSummaryCard(root, "journal.card.avg_loss", "Avg Loss", out _summaryAccountsValueText));
            cards.Children.Add(CreateSummaryCard(root, "summary.card.profit_factor", "Profit Factor", out _summaryProfitFactorValueText));
            _summaryCardsPanel = cards;
            Grid.SetRow(cards, 1);
            root.Children.Add(cards);

            var topGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            topGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _journalTopGrid = topGrid;

            var metricsPanel = new Grid();
            metricsPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            metricsPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _journalMetricsPanel = metricsPanel;
            var metricsHeader = new TextBlock
            {
                Text = L("journal.trader_performance", "Trader Performance"),
                FontWeight = UiHeadingFontWeight,
                Margin = new Thickness(0, 0, 0, 8)
            };
            BindLocalizedText(metricsHeader, "journal.trader_performance", "Trader Performance");
            ApplySkinResource(metricsHeader, TextBlock.ForegroundProperty, "FontControlBrush", "FontHeaderLevel4Brush", "FontTableBrush");
            Grid.SetRow(metricsHeader, 0);
            metricsPanel.Children.Add(metricsHeader);

            var metricsGrid = CreateSummaryPerformanceGrid(root);
            _summaryPerformanceGrid = metricsGrid;
            Grid.SetRow(metricsGrid, 1);
            metricsPanel.Children.Add(metricsGrid);
            Grid.SetColumn(metricsPanel, 0);
            topGrid.Children.Add(metricsPanel);

            var warningsHeaderRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            warningsHeaderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var warningsHeader = new TextBlock
            {
                Text = L("journal.critical_warnings", "Critical Warnings"),
                FontWeight = UiHeadingFontWeight,
                VerticalAlignment = VerticalAlignment.Center
            };
            BindLocalizedText(warningsHeader, "journal.critical_warnings", "Critical Warnings");
            ApplySkinResource(warningsHeader, TextBlock.ForegroundProperty, "FontControlBrush", "FontHeaderLevel4Brush", "FontTableBrush");
            warningsHeaderRow.Children.Add(warningsHeader);
            _journalWarningsHeaderText = warningsHeader;
            _journalWarningsHeaderRow = warningsHeaderRow;

            var warningsPanel = new Grid();
            warningsPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            warningsPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _journalWarningsPanel = warningsPanel;
            Grid.SetRow(warningsHeaderRow, 0);
            warningsPanel.Children.Add(warningsHeaderRow);

            _criticalWarningsGrid = CreateCriticalWarningsGrid(root);
            Grid.SetRow(_criticalWarningsGrid, 1);
            warningsPanel.Children.Add(_criticalWarningsGrid);
            Grid.SetColumn(warningsPanel, 2);
            topGrid.Children.Add(warningsPanel);

            Grid.SetRow(topGrid, 2);
            root.Children.Add(topGrid);

            var tradeFeedHeader = new TextBlock
            {
                Text = L("journal.trade_feed", "Live Feed"),
                FontWeight = UiHeadingFontWeight,
                Margin = new Thickness(0, 0, 0, 8)
            };
            BindLocalizedText(tradeFeedHeader, "journal.trade_feed", "Live Feed");
            ApplySkinResource(tradeFeedHeader, TextBlock.ForegroundProperty, "FontControlBrush", "FontHeaderLevel4Brush", "FontTableBrush");
            Grid.SetRow(tradeFeedHeader, 3);
            root.Children.Add(tradeFeedHeader);

            var tradeFeedGrid = CreateSummaryRecentTradesGrid(root);
            _summaryRecentTradesGrid = tradeFeedGrid;
            Grid.SetRow(tradeFeedGrid, 4);
            root.Children.Add(tradeFeedGrid);

            _journalLicenseGateOverlay = CreateJournalLicenseGateOverlay(root);
            Thickness journalContentMargin = root.Margin;
            _journalLicenseGateOverlay.Margin = new Thickness(
                -journalContentMargin.Left,
                -journalContentMargin.Top,
                -journalContentMargin.Right,
                -journalContentMargin.Bottom);
            Grid.SetRow(_journalLicenseGateOverlay, 0);
            Grid.SetRowSpan(_journalLicenseGateOverlay, 5);
            Panel.SetZIndex(_journalLicenseGateOverlay, 1000);
            root.Children.Add(_journalLicenseGateOverlay);
            UpdateJournalLicenseGateOverlay();

            ApplyJournalResponsiveLayout(root.ActualWidth > 0 ? root.ActualWidth : Width);
            RegisterLocalizationBinding(() => RefreshSummaryInsightsIfNeeded(DateTime.UtcNow, true));
            return root;
        }


        private DataGrid CreateJournalDataGrid(FrameworkElement context)
                {
                    if (_journalEntriesView == null)
                    {
                        _journalEntriesView = CollectionViewSource.GetDefaultView(_journalEntries);
                        if (_journalEntriesView != null)
                            _journalEntriesView.Filter = ShouldDisplayJournalEntry;
                    }

                    IEnumerable journalSource = _journalEntriesView as IEnumerable;
                    if (journalSource == null)
                        journalSource = _journalEntries;

                    var grid = new DataGrid
                    {
                        AutoGenerateColumns = false,
                        IsReadOnly = true,
                        CanUserAddRows = false,
                        CanUserDeleteRows = false,
                        HeadersVisibility = DataGridHeadersVisibility.Column,
                        VerticalAlignment = VerticalAlignment.Stretch,
                        SelectionUnit = DataGridSelectionUnit.FullRow,
                        SelectionMode = DataGridSelectionMode.Single
                    };
                    var leftHeaderStyle = CreateSkinAwareColumnHeaderStyle(grid, HorizontalAlignment.Left);
                    var leftTextStyle = CreateTextBlockElementStyle(grid, HorizontalAlignment.Left, TextAlignment.Left, new Thickness(6, 3, 6, 3));
                    grid.ItemsSource = journalSource;
                    ConfigureDataGridScrolling(grid);
                    ApplySkinResource(grid, Control.BackgroundProperty, "BackgroundMainWindow", "GridEntireBackground");
                    ApplySkinResource(grid, Control.ForegroundProperty, "FontControlBrush", "FontTableBrush");
                    ApplySkinResource(grid, Control.BorderBrushProperty, "BorderThinBrush", "TabControlBorderBrush");
                    ApplySkinResource(grid, DataGrid.HorizontalGridLinesBrushProperty, "BorderThinBrush", "GridHeaderHighlight");
                    ApplySkinResource(grid, DataGrid.VerticalGridLinesBrushProperty, "BorderThinBrush", "GridHeaderHighlight");
                    grid.AlternationCount = 2;
                    grid.FocusVisualStyle = null;
                    grid.BorderThickness = new Thickness(1, 1, 0, 0);
                    grid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
                    ApplySkinResource(grid, DataGrid.RowBackgroundProperty, "BackgroundMainWindow", "GridEntireBackground");
                    ApplySkinResource(grid, DataGrid.AlternatingRowBackgroundProperty, "BackgroundMainWindow", "GridEntireBackground", "BackgroundTextInput");
                    grid.RowStyle = _accountsGrid?.RowStyle ?? CreateSkinAwareRowStyle(grid);
                    grid.CellStyle = CreatePassiveCellStyle(grid, _accountsGrid?.CellStyle ?? CreateSkinAwareCellStyle(grid));
                    grid.ColumnHeaderStyle = _accountsGrid?.ColumnHeaderStyle ?? leftHeaderStyle;
                    ApplyDataGridSelectionResources(grid, grid);
        
                    var timeColumn = CreateTextColumn(L("journal.column.time", "Time"), nameof(JournalEntry.TimeText), leftTextStyle, leftHeaderStyle);
                    timeColumn.Width = DataGridLength.Auto;
                    BindLocalizedColumnHeader(timeColumn, "journal.column.time", "Time");
                    grid.Columns.Add(timeColumn);

                    var accountColumn = CreateTextColumn(L("journal.column.account", "Account"), nameof(JournalEntry.AccountName), leftTextStyle, leftHeaderStyle);
                    accountColumn.Width = DataGridLength.Auto;
                    BindLocalizedColumnHeader(accountColumn, "journal.column.account", "Account");
                    grid.Columns.Add(accountColumn);

                    var typeColumn = CreateTextColumn(L("journal.column.type", "Type"), nameof(JournalEntry.Category), leftTextStyle, leftHeaderStyle);
                    typeColumn.Width = DataGridLength.Auto;
                    BindLocalizedColumnHeader(typeColumn, "journal.column.type", "Type");
                    grid.Columns.Add(typeColumn);

                    var messageColumn = CreateTextColumn(L("journal.column.message", "Message"), nameof(JournalEntry.Message), leftTextStyle, leftHeaderStyle);
                    messageColumn.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
                    messageColumn.MinWidth = 240;
                    BindLocalizedColumnHeader(messageColumn, "journal.column.message", "Message");
                    grid.Columns.Add(messageColumn);
        
                    return grid;
                }

                private bool ShouldDisplayJournalEntry(object item)
                {
                    if (!(item is JournalEntry entry))
                        return false;
                    if (!string.Equals(entry.Category, "Execution", StringComparison.OrdinalIgnoreCase))
                        return false;
                    if (string.IsNullOrWhiteSpace(entry.Message))
                        return false;

                    return entry.Message.StartsWith("Exec ", StringComparison.OrdinalIgnoreCase);
                }
        
                private DataGrid CreateCriticalWarningsGrid(FrameworkElement context)
                {
                    var grid = new DataGrid
                    {
                        AutoGenerateColumns = false,
                        IsReadOnly = true,
                        CanUserAddRows = false,
                        CanUserDeleteRows = false,
                        HeadersVisibility = DataGridHeadersVisibility.Column,
                        ItemsSource = _criticalWarningEntries,
                        VerticalAlignment = VerticalAlignment.Stretch,
                        SelectionUnit = DataGridSelectionUnit.FullRow,
                        SelectionMode = DataGridSelectionMode.Single
                    };
                    ConfigureDataGridScrolling(grid);
                    ApplySkinResource(grid, Control.BackgroundProperty, "BackgroundMainWindow", "GridEntireBackground");
                    ApplySkinResource(grid, Control.ForegroundProperty, "FontControlBrush", "FontTableBrush");
                    ApplySkinResource(grid, Control.BorderBrushProperty, "BorderThinBrush", "TabControlBorderBrush");
                    ApplySkinResource(grid, DataGrid.HorizontalGridLinesBrushProperty, "BorderThinBrush", "GridHeaderHighlight");
                    ApplySkinResource(grid, DataGrid.VerticalGridLinesBrushProperty, "BorderThinBrush", "GridHeaderHighlight");
                    grid.AlternationCount = 2;
                    grid.FocusVisualStyle = null;
                    grid.BorderThickness = new Thickness(1, 1, 0, 0);
                    grid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
                    ApplySkinResource(grid, DataGrid.RowBackgroundProperty, "BackgroundMainWindow", "GridEntireBackground");
                    ApplySkinResource(grid, DataGrid.AlternatingRowBackgroundProperty, "BackgroundMainWindow", "GridEntireBackground", "BackgroundTextInput");
        
                    var leftHeaderStyle = CreateSkinAwareColumnHeaderStyle(grid, HorizontalAlignment.Left);
                    var centerHeaderStyle = CreateSkinAwareColumnHeaderStyle(grid, HorizontalAlignment.Center);
                    var leftTextStyle = CreateTextBlockElementStyle(grid, HorizontalAlignment.Left, TextAlignment.Left, new Thickness(6, 3, 6, 3));
                    var timeColumn = CreateTextColumn(L("journal.column.time", "Time"), nameof(CriticalWarningEntry.TimeText), leftTextStyle, leftHeaderStyle);
                    timeColumn.Width = DataGridLength.Auto;
                    BindLocalizedColumnHeader(timeColumn, "journal.column.time", "Time");
                    grid.Columns.Add(timeColumn);
                    var accountColumn = CreateTextColumn(L("journal.column.account", "Account"), nameof(CriticalWarningEntry.AccountName), leftTextStyle, leftHeaderStyle);
                    BindLocalizedColumnHeader(accountColumn, "journal.column.account", "Account");
                    grid.Columns.Add(accountColumn);

                    var messageColumn = CreateTextColumn(L("journal.column.warning", "Warning"), nameof(CriticalWarningEntry.Message), leftTextStyle, leftHeaderStyle);
                    messageColumn.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
                    messageColumn.MinWidth = 240;
                    BindLocalizedColumnHeader(messageColumn, "journal.column.warning", "Warning");
                    grid.Columns.Add(messageColumn);

                    grid.RowStyle = _accountsGrid?.RowStyle ?? CreateSkinAwareRowStyle(grid);
                    grid.CellStyle = CreatePassiveCellStyle(grid, _accountsGrid?.CellStyle ?? CreateSkinAwareCellStyle(grid));
                    grid.ColumnHeaderStyle = _accountsGrid?.ColumnHeaderStyle ?? leftHeaderStyle;
                    ApplyDataGridSelectionResources(grid, grid);

                    grid.Columns.Add(CreateWarningDismissColumn(centerHeaderStyle, context));
                    return grid;
                }

                private Grid CreateJournalLicenseGateOverlay(FrameworkElement context)
                {
                    var overlay = new Grid
                    {
                        Visibility = Visibility.Collapsed,
                        Background = new SolidColorBrush(Color.FromArgb(217, 0, 0, 0)),
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
                    stack.Children.Add(new Border
                    {
                        Width = 96,
                        Height = 2,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Margin = new Thickness(0, 0, 0, 10),
                        Background = OrangeAccentBrush
                    });

                    var title = new TextBlock
                    {
                        Text = L("overlay.journal.go_pro_title", "Go Pro for Journal"),
                        FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(0, 0, 0, 8)
                    };
                    RegisterLocalizationBinding(() => title.Text = L("overlay.journal.go_pro_title", "Go Pro for Journal"));
                    ApplySkinResource(title, TextBlock.ForegroundProperty, "FontHeaderLevel3Brush", "FontHeaderLevel4Brush", "FontControlBrush", "FontTableBrush");
                    stack.Children.Add(title);
                    stack.Children.Add(CreateLiteLicensePromptLine(context));

                    _journalLicenseGateMessageText = new TextBlock
                    {
                        Text = BuildUnifiedPremiumGateMessage(),
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 0, 0, 12)
                    };
                    RegisterLocalizationBinding(() => _journalLicenseGateMessageText.Text = BuildUnifiedPremiumGateMessage());
                    ApplySkinResource(_journalLicenseGateMessageText, TextBlock.ForegroundProperty, "FontControlBrush", "FontTableBrush");
                    stack.Children.Add(_journalLicenseGateMessageText);

                    var buttonRow = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Left
                    };

                    var checkoutButton = new Button
                    {
                        Content = L("overlay.button.go_pro", "Go Pro"),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        MinWidth = 172,
                        Style = CreateGroupActionButtonStyle(context),
                        Background = OrangeAccentBrush,
                        BorderBrush = OrangeAccentBrush,
                        Foreground = Brushes.White
                    };
                    RegisterLocalizationBinding(() => checkoutButton.Content = L("overlay.button.go_pro", "Go Pro"));
                    checkoutButton.Click += (_, __) => OpenAnalyticsExternalUrl(GetWhopUpgradeCheckoutUrl());
                    buttonRow.Children.Add(checkoutButton);

                    var memberAppButton = new Button
                    {
                        Content = L("overlay.button.member_hub", "Open Member Hub"),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        MinWidth = 172,
                        Margin = new Thickness(8, 0, 0, 0),
                        Style = CreateGroupActionButtonStyle(context)
                    };
                    RegisterLocalizationBinding(() => memberAppButton.Content = L("overlay.button.member_hub", "Open Member Hub"));
                    memberAppButton.Click += (_, __) => OpenAnalyticsExternalUrl(GetWhopMemberHubUrl());
                    buttonRow.Children.Add(memberAppButton);
                    stack.Children.Add(buttonRow);
                    stack.Children.Add(CreateSettingsTabShortcutLine(context));

                    card.Child = stack;
                    overlay.Children.Add(card);
                    return overlay;
                }

                private bool ShouldGateJournalByLicense(out string message)
                {
                    message = null;
                    if (IsLicenseActiveOrGrace(DateTime.UtcNow) && !IsFreeLitePlan())
                        return false;
                    message = BuildPremiumAccessMessage("Journal");
                    return true;
                }

                private void UpdateJournalLicenseGateOverlay()
                {
                    if (_journalLicenseGateOverlay == null)
                        return;

                    if (ShouldGateJournalByLicense(out _))
                    {
                        if (_journalLicenseGateMessageText != null)
                            _journalLicenseGateMessageText.Text = BuildUnifiedPremiumGateMessage();
                        _journalLicenseGateOverlay.Visibility = Visibility.Visible;
                        return;
                    }

                    _journalLicenseGateOverlay.Visibility = Visibility.Collapsed;
                }
        
                private void OnJournalRootSizeChanged(object sender, SizeChangedEventArgs e)
                {
                    ApplyJournalResponsiveLayout(e.NewSize.Width);
                }
        
                private void ApplyJournalResponsiveLayout(double width)
                {
                    if (_journalRootGrid == null || _journalTopGrid == null || _journalWarningsHeaderRow == null || _journalWarningsHeaderText == null)
                        return;
                    if (_isApplyingJournalResponsiveLayout)
                        return;
        
                    try
                    {
                        _isApplyingJournalResponsiveLayout = true;
                        double usableWidth = Math.Max(320, width);
                        const double hysteresis = 24.0;
                        bool narrow = ResolveBelowBreakpoint(usableWidth, _journalNarrowLayout, 1040, hysteresis);
                        _journalNarrowLayout = narrow;
                        _journalRootGrid.Margin = narrow ? new Thickness(10) : new Thickness(20);
                        if (_journalLicenseGateOverlay != null)
                        {
                            Thickness margin = _journalRootGrid.Margin;
                            _journalLicenseGateOverlay.Margin = new Thickness(-margin.Left, -margin.Top, -margin.Right, -margin.Bottom);
                        }

                        UpdateSummaryCardsPanelLayout(usableWidth);

                        if (_summaryPerformanceGrid != null)
                            _summaryPerformanceGrid.MinHeight = narrow ? 150 : 180;
                        if (_summaryRecentTradesGrid != null)
                            _summaryRecentTradesGrid.MinHeight = narrow ? 200 : 240;
                        if (_criticalWarningsGrid != null)
                            _criticalWarningsGrid.MinHeight = narrow ? 140 : 180;

                        _journalTopGrid.RowDefinitions.Clear();
                        _journalTopGrid.ColumnDefinitions.Clear();
                        if (narrow)
                        {
                            _journalTopGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                            _journalTopGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
                            _journalTopGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                            _journalTopGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                            if (_journalMetricsPanel != null)
                            {
                                Grid.SetColumn(_journalMetricsPanel, 0);
                                Grid.SetRow(_journalMetricsPanel, 0);
                            }

                            if (_journalWarningsPanel != null)
                            {
                                Grid.SetColumn(_journalWarningsPanel, 0);
                                Grid.SetRow(_journalWarningsPanel, 2);
                            }
                        }
                        else
                        {
                            _journalTopGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                            _journalTopGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                            _journalTopGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
                            _journalTopGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                            if (_journalMetricsPanel != null)
                            {
                                Grid.SetColumn(_journalMetricsPanel, 0);
                                Grid.SetRow(_journalMetricsPanel, 0);
                            }

                            if (_journalWarningsPanel != null)
                            {
                                Grid.SetColumn(_journalWarningsPanel, 2);
                                Grid.SetRow(_journalWarningsPanel, 0);
                            }
                        }
        
                        _journalWarningsHeaderRow.ColumnDefinitions.Clear();
                        _journalWarningsHeaderRow.RowDefinitions.Clear();
                        _journalWarningsHeaderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        _journalWarningsHeaderRow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                        Grid.SetColumn(_journalWarningsHeaderText, 0);
                        Grid.SetRow(_journalWarningsHeaderText, 0);
                    }
                    finally
                    {
                        _isApplyingJournalResponsiveLayout = false;
                    }
                }

    }
}
