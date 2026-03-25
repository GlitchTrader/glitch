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
// by GlitchTrader.com
//
// __________________________________________________
// __________________________________________________
//

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Glitch.Services;
using NinjaTrader.Cbi;
using NinjaTrader.Gui.Tools;

namespace Glitch.UI
{
    public partial class GlitchMainWindow
    {
        private static readonly Dictionary<string, double> FallbackInstrumentPointValues = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            { "MNQ", 2.0 },
            { "NQ", 20.0 },
            { "MES", 5.0 },
            { "ES", 50.0 },
            { "MYM", 0.5 },
            { "YM", 5.0 },
            { "M2K", 5.0 },
            { "RTY", 50.0 },
            { "MGC", 10.0 },
            { "GC", 100.0 },
            { "MCL", 100.0 },
            { "CL", 1000.0 }
        };
        private static readonly Dictionary<string, double> CachedInstrumentPointValues = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private static readonly object InstrumentPointValueSync = new object();

        private readonly GlitchTradeInsightsService _tradeInsightsService = new GlitchTradeInsightsService();
        private readonly GlitchTradeLedgerService _tradeLedgerService =
            new GlitchTradeLedgerService(GlitchStateStore.GetDefaultPath("TradeLedger.tsv"));
        private readonly GlitchRiskLockLedgerService _riskLockLedgerService =
            new GlitchRiskLockLedgerService(GlitchStateStore.GetDefaultPath("RiskLocks.tsv"));
        private readonly ObservableCollection<SummaryMetricRow> _summaryMetricRows = new ObservableCollection<SummaryMetricRow>();
        private readonly ObservableCollection<SummaryTradeRow> _summaryRecentTrades = new ObservableCollection<SummaryTradeRow>();
        private readonly ObservableCollection<string> _summaryReasonLines = new ObservableCollection<string>();

        private TextBlock _summaryTradesValueText;
        private TextBlock _summaryFleetTradesValueText;
        private TextBlock _summaryWinRateValueText;
        private TextBlock _summaryNetPointsValueText;
        private TextBlock _summaryProfitFactorValueText;
        private TextBlock _summaryAccountsValueText;
        private TextBlock _summaryAsOfText;
        private int _summaryLastExecutionCount = -1;
        private long _summaryLastExecutionTicks = -1;
        private int _summaryLastWarningCount = -1;
        private Grid _summaryRootGrid;
        private UniformGrid _summaryCardsPanel;
        private Grid _summaryMidGrid;
        private StackPanel _summaryReasonsPanel;
        private DataGrid _summaryPerformanceGrid;
        private DataGrid _summaryRecentTradesGrid;
        private ListBox _summaryReasonsList;

        private UIElement CreateSummaryTabImpl()
        {
            var root = new Grid { Margin = new Thickness(20) };
            _summaryRootGrid = root;
            _summaryRootGrid.SizeChanged += OnSummaryRootSizeChanged;
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var header = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = new TextBlock { Text = L("summary.title", "Trading Summary"), FontWeight = FontWeights.SemiBold };
            BindLocalizedText(title, "summary.title", "Trading Summary");
            ApplySkinResource(title, TextBlock.ForegroundProperty, "FontHeaderLevel3Brush", "FontHeaderLevel4Brush", "FontControlBrush", "FontTableBrush");
            header.Children.Add(title);
            _summaryAsOfText = new TextBlock { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
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
                Margin = new Thickness(0, 0, 0, 10),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            cards.Children.Add(CreateSummaryCard(root, "summary.card.trades", "Closed Trades", out _summaryTradesValueText));
            cards.Children.Add(CreateSummaryCard(root, "summary.card.fleet_trades", "Fleet Trades", out _summaryFleetTradesValueText));
            cards.Children.Add(CreateSummaryCard(root, "summary.card.win_rate", "Win Rate", out _summaryWinRateValueText));
            cards.Children.Add(CreateSummaryCard(root, "summary.card.net_pnl", "Net PnL", out _summaryNetPointsValueText));
            cards.Children.Add(CreateSummaryCard(root, "summary.card.profit_factor", "Profit Factor", out _summaryProfitFactorValueText));
            cards.Children.Add(CreateSummaryCard(root, "summary.card.accounts", "Accounts Traded", out _summaryAccountsValueText));
            _summaryCardsPanel = cards;
            Grid.SetRow(cards, 1);
            root.Children.Add(cards);

            var mid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            mid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.62, GridUnitType.Star) });
            mid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            mid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.38, GridUnitType.Star) });
            _summaryMidGrid = mid;
            Grid.SetRow(mid, 2);
            root.Children.Add(mid);

            var performance = CreateSummaryPerformanceGrid(root);
            _summaryPerformanceGrid = performance;
            Grid.SetColumn(performance, 0);
            mid.Children.Add(performance);

            var reasonsPanel = new StackPanel();
            var reasonsTitle = new TextBlock
            {
                Text = L("summary.close_reasons", "Close Reasons"),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            };
            BindLocalizedText(reasonsTitle, "summary.close_reasons", "Close Reasons");
            ApplySkinResource(reasonsTitle, TextBlock.ForegroundProperty, "FontHeaderLevel4Brush", "FontControlBrush", "FontTableBrush");
            reasonsPanel.Children.Add(reasonsTitle);
            var reasonsList = new ListBox { ItemsSource = _summaryReasonLines, MinHeight = 180 };
            _summaryReasonsList = reasonsList;
            ApplySkinResource(reasonsList, Control.BackgroundProperty, "BackgroundMainWindow", "GridEntireBackground");
            ApplySkinResource(reasonsList, Control.ForegroundProperty, "FontControlBrush", "FontTableBrush");
            ApplySkinResource(reasonsList, Control.BorderBrushProperty, "BorderThinBrush", "TabControlBorderBrush");
            reasonsList.BorderThickness = new Thickness(1);
            reasonsPanel.Children.Add(reasonsList);
            _summaryReasonsPanel = reasonsPanel;
            Grid.SetColumn(reasonsPanel, 2);
            mid.Children.Add(reasonsPanel);

            var recentGrid = CreateSummaryRecentTradesGrid(root);
            _summaryRecentTradesGrid = recentGrid;
            Grid.SetRow(recentGrid, 3);
            root.Children.Add(recentGrid);

            ApplySummaryResponsiveLayout(root.ActualWidth > 0 ? root.ActualWidth : Width);
            RegisterLocalizationBinding(() => RefreshSummaryInsightsIfNeeded(DateTime.UtcNow, true));
            return root;
        }

        private Border CreateSummaryCard(FrameworkElement context, string titleKey, string titleFallback, out TextBlock value)
        {
            var border = new Border
            {
                Margin = new Thickness(0),
                Padding = new Thickness(10, 8, 10, 8),
                BorderThickness = new Thickness(1)
            };
            ApplySkinResource(border, Border.BackgroundProperty, "BackgroundMainWindow", "GridEntireBackground", "BackgroundTextInput");
            ApplySkinResource(border, Border.BorderBrushProperty, "BorderThinBrush", "TabControlBorderBrush");
            var stack = new StackPanel();
            border.Child = stack;
            var header = new TextBlock { Text = titleFallback, FontWeight = FontWeights.Medium };
            BindLocalizedText(header, titleKey, titleFallback);
            ApplySkinResource(header, TextBlock.ForegroundProperty, "FontControlBrush", "FontTableBrush");
            stack.Children.Add(header);
            value = new TextBlock { Text = "-", Margin = new Thickness(0, 4, 0, 0), FontWeight = FontWeights.SemiBold };
            ApplySkinResource(value, TextBlock.ForegroundProperty, "FontHeaderLevel3Brush", "FontHeaderLevel4Brush", "FontControlBrush", "FontTableBrush");
            stack.Children.Add(value);
            return border;
        }

        private DataGrid CreateSummaryPerformanceGrid(FrameworkElement context)
        {
            var grid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                ItemsSource = _summaryMetricRows,
                MinHeight = 180
            };
            ConfigureDataGridScrolling(grid);
            ApplySkinResource(grid, Control.BackgroundProperty, "BackgroundMainWindow", "GridEntireBackground");
            ApplySkinResource(grid, Control.ForegroundProperty, "FontControlBrush", "FontTableBrush");
            ApplySkinResource(grid, Control.BorderBrushProperty, "BorderThinBrush", "TabControlBorderBrush");
            ApplySkinResource(grid, DataGrid.HorizontalGridLinesBrushProperty, "BorderThinBrush", "GridHeaderHighlight");
            ApplySkinResource(grid, DataGrid.VerticalGridLinesBrushProperty, "BorderThinBrush", "GridHeaderHighlight");
            grid.AlternationCount = 2;
            ApplySkinResource(grid, DataGrid.RowBackgroundProperty, "BackgroundMainWindow", "GridEntireBackground");
            ApplySkinResource(grid, DataGrid.AlternatingRowBackgroundProperty, "BackgroundMainWindow", "GridEntireBackground", "BackgroundTextInput");
            grid.FocusVisualStyle = null;
            grid.BorderThickness = new Thickness(1, 1, 0, 0);
            grid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
            var leftHeader = CreateSkinAwareColumnHeaderStyle(grid, HorizontalAlignment.Left);
            var centerHeader = CreateSkinAwareColumnHeaderStyle(grid, HorizontalAlignment.Center);
            var leftText = CreateTextBlockElementStyle(grid, HorizontalAlignment.Left, TextAlignment.Left, new Thickness(6, 3, 6, 3));
            var centerText = CreateTextBlockElementStyle(grid, HorizontalAlignment.Center, TextAlignment.Center, new Thickness(6, 3, 6, 3));
            grid.RowStyle = _accountsGrid?.RowStyle ?? CreateSkinAwareRowStyle(grid);
            grid.CellStyle = CreatePassiveCellStyle(grid, _accountsGrid?.CellStyle ?? CreateSkinAwareCellStyle(grid));
            grid.ColumnHeaderStyle = _accountsGrid?.ColumnHeaderStyle ?? leftHeader;
            ApplyDataGridSelectionResources(grid, grid);
            ConfigurePassiveDataGrid(grid);

            var metricColumn = CreateTextColumn(L("summary.metrics_column", "Metrics"), nameof(SummaryMetricRow.Metric), leftText, leftHeader);
            metricColumn.Width = new DataGridLength(0.44, DataGridLengthUnitType.Star);
            metricColumn.MinWidth = 170;
            BindLocalizedColumnHeader(metricColumn, "summary.metrics_column", "Metrics");
            grid.Columns.Add(metricColumn);
            var allColumn = CreateTextColumn(L("summary.all", "All"), nameof(SummaryMetricRow.All), centerText, centerHeader);
            allColumn.Width = DataGridLength.Auto;
            allColumn.MinWidth = 90;
            BindLocalizedColumnHeader(allColumn, "summary.all", "All");
            grid.Columns.Add(allColumn);
            var longColumn = CreateTextColumn(L("summary.long", "Long"), nameof(SummaryMetricRow.Long), centerText, centerHeader);
            longColumn.Width = DataGridLength.Auto;
            longColumn.MinWidth = 90;
            BindLocalizedColumnHeader(longColumn, "summary.long", "Long");
            grid.Columns.Add(longColumn);
            var shortColumn = CreateTextColumn(L("summary.short", "Short"), nameof(SummaryMetricRow.Short), centerText, centerHeader);
            shortColumn.Width = DataGridLength.Auto;
            shortColumn.MinWidth = 90;
            BindLocalizedColumnHeader(shortColumn, "summary.short", "Short");
            grid.Columns.Add(shortColumn);

            return grid;
        }

        private DataGrid CreateSummaryRecentTradesGrid(FrameworkElement context)
        {
            var grid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                ItemsSource = _summaryRecentTrades,
                MinHeight = 220
            };
            ConfigureDataGridScrolling(grid);
            ApplySkinResource(grid, Control.BackgroundProperty, "BackgroundMainWindow", "GridEntireBackground");
            ApplySkinResource(grid, Control.ForegroundProperty, "FontControlBrush", "FontTableBrush");
            ApplySkinResource(grid, Control.BorderBrushProperty, "BorderThinBrush", "TabControlBorderBrush");
            ApplySkinResource(grid, DataGrid.HorizontalGridLinesBrushProperty, "BorderThinBrush", "GridHeaderHighlight");
            ApplySkinResource(grid, DataGrid.VerticalGridLinesBrushProperty, "BorderThinBrush", "GridHeaderHighlight");
            grid.AlternationCount = 2;
            ApplySkinResource(grid, DataGrid.RowBackgroundProperty, "BackgroundMainWindow", "GridEntireBackground");
            ApplySkinResource(grid, DataGrid.AlternatingRowBackgroundProperty, "BackgroundMainWindow", "GridEntireBackground", "BackgroundTextInput");
            grid.FocusVisualStyle = null;
            grid.BorderThickness = new Thickness(1, 1, 0, 0);
            grid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
            var leftHeader = CreateSkinAwareColumnHeaderStyle(grid, HorizontalAlignment.Left);
            var centerHeader = CreateSkinAwareColumnHeaderStyle(grid, HorizontalAlignment.Center);
            var leftText = CreateTextBlockElementStyle(grid, HorizontalAlignment.Left, TextAlignment.Left, new Thickness(6, 3, 6, 3));
            var centerText = CreateTextBlockElementStyle(grid, HorizontalAlignment.Center, TextAlignment.Center, new Thickness(6, 3, 6, 3));
            var pnlText = CreatePnlTextBlockElementStyle(grid, HorizontalAlignment.Center, TextAlignment.Center, new Thickness(6, 3, 6, 3), nameof(SummaryTradeRow.PnlSign));
            grid.RowStyle = _accountsGrid?.RowStyle ?? CreateSkinAwareRowStyle(grid);
            grid.CellStyle = CreatePassiveCellStyle(grid, _accountsGrid?.CellStyle ?? CreateSkinAwareCellStyle(grid));
            grid.ColumnHeaderStyle = _accountsGrid?.ColumnHeaderStyle ?? leftHeader;
            ApplyDataGridSelectionResources(grid, grid);
            ConfigurePassiveDataGrid(grid);

            var accountColumn = CreateTextColumn(L("summary.accounts", "Accounts"), nameof(SummaryTradeRow.Account), leftText, leftHeader);
            accountColumn.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
            accountColumn.MinWidth = 160;
            BindLocalizedColumnHeader(accountColumn, "summary.accounts", "Accounts");
            grid.Columns.Add(accountColumn);

            var instrumentColumn = CreateTextColumn(L("summary.instrument", "Instrument"), nameof(SummaryTradeRow.Instrument), centerText, centerHeader);
            instrumentColumn.Width = DataGridLength.Auto;
            instrumentColumn.MinWidth = 72;
            BindLocalizedColumnHeader(instrumentColumn, "summary.instrument", "Instrument");
            grid.Columns.Add(instrumentColumn);

            var openTimeColumn = CreateTextColumn(L("summary.open_time", "Open Time"), nameof(SummaryTradeRow.OpenTime), leftText, leftHeader);
            openTimeColumn.Width = DataGridLength.Auto;
            openTimeColumn.MinWidth = 92;
            BindLocalizedColumnHeader(openTimeColumn, "summary.open_time", "Open Time");
            grid.Columns.Add(openTimeColumn);

            var closeTimeColumn = CreateTextColumn(L("summary.close_time", "Close Time"), nameof(SummaryTradeRow.CloseTime), leftText, leftHeader);
            closeTimeColumn.Width = DataGridLength.Auto;
            closeTimeColumn.MinWidth = 92;
            BindLocalizedColumnHeader(closeTimeColumn, "summary.close_time", "Close Time");
            grid.Columns.Add(closeTimeColumn);

            var durationColumn = CreateTextColumn(L("summary.duration", "Duration"), nameof(SummaryTradeRow.Duration), centerText, centerHeader);
            durationColumn.Width = DataGridLength.Auto;
            durationColumn.MinWidth = 82;
            BindLocalizedColumnHeader(durationColumn, "summary.duration", "Duration");
            grid.Columns.Add(durationColumn);

            var sideColumn = CreateTextColumn(L("summary.side", "Side"), nameof(SummaryTradeRow.Side), centerText, centerHeader);
            sideColumn.Width = DataGridLength.Auto;
            sideColumn.MinWidth = 60;
            BindLocalizedColumnHeader(sideColumn, "summary.side", "Side");
            grid.Columns.Add(sideColumn);

            var contractsColumn = CreateTextColumn(L("summary.contracts", "Contracts"), nameof(SummaryTradeRow.Contracts), centerText, centerHeader);
            contractsColumn.Width = DataGridLength.Auto;
            contractsColumn.MinWidth = 76;
            BindLocalizedColumnHeader(contractsColumn, "summary.contracts", "Contracts");
            grid.Columns.Add(contractsColumn);

            var exitColumn = CreateTextColumn(L("summary.exit", "Exit"), nameof(SummaryTradeRow.Exit), centerText, centerHeader);
            exitColumn.Width = DataGridLength.Auto;
            exitColumn.MinWidth = 88;
            BindLocalizedColumnHeader(exitColumn, "summary.exit", "Exit");
            grid.Columns.Add(exitColumn);

            var pnlColumn = CreateTextColumn(L("summary.pnl_short", "PnL"), nameof(SummaryTradeRow.PnlPoints), pnlText, centerHeader);
            pnlColumn.Width = DataGridLength.Auto;
            pnlColumn.MinWidth = 84;
            BindLocalizedColumnHeader(pnlColumn, "summary.pnl_short", "PnL");
            grid.Columns.Add(pnlColumn);
            return grid;
        }

        private void RefreshSummaryInsightsIfNeeded(DateTime nowUtc, bool force = false)
        {
            if (_summaryAsOfText == null)
                return;

            var executionEntries = (_journalEntries ?? new ObservableCollection<JournalEntry>())
                .Where(entry =>
                    entry != null &&
                    !string.IsNullOrWhiteSpace(entry.Message) &&
                    string.Equals(entry.Category, "Execution", StringComparison.OrdinalIgnoreCase))
                .ToList();

            int executionCount = executionEntries.Count;
            long newestExecutionTicks = executionCount > 0
                ? executionEntries.Max(entry => entry.TimestampUtc.ToUniversalTime().Ticks)
                : 0;
            int warningCount = _criticalWarningEntries?.Count ?? 0;
            if (!force &&
                executionCount == _summaryLastExecutionCount &&
                newestExecutionTicks == _summaryLastExecutionTicks &&
                warningCount == _summaryLastWarningCount)
                return;

            _summaryLastExecutionCount = executionCount;
            _summaryLastExecutionTicks = newestExecutionTicks;
            _summaryLastWarningCount = warningCount;

            var journalEvents = (_journalEntries ?? new ObservableCollection<JournalEntry>())
                .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.Message))
                .Select(entry => new GlitchTradeInsightsService.TradeJournalEvent
                {
                    UtcTime = entry.TimestampUtc.ToUniversalTime(),
                    AccountName = entry.AccountName,
                    Category = entry.Category,
                    Message = entry.Message
                })
                .ToList();

            var warningEvents = (_criticalWarningEntries ?? new ObservableCollection<CriticalWarningEntry>())
                .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.Message))
                .Select(entry => new GlitchTradeInsightsService.TradeWarningEvent
                {
                    UtcTime = entry.TimestampUtc.ToUniversalTime(),
                    AccountName = entry.AccountName,
                    WarningKey = entry.WarningKey,
                    Message = entry.Message,
                    IsDismissed = entry.IsDismissed
                })
                .ToList();

            GlitchTradeInsightsService.TradeInsightsSnapshot currentSnapshot = _tradeInsightsService.BuildSnapshot(journalEvents, warningEvents, nowUtc);
            IReadOnlyList<GlitchTradeInsightsService.TradeRoundTrip> ledgerTrades =
                _tradeLedgerService.MergeAndGetAll(currentSnapshot.ClosedTrades, nowUtc);
            _riskLockLedgerService.MergeAndGetSnapshot(warningEvents, nowUtc);
            IReadOnlyList<GlitchTradeInsightsService.TradeRoundTrip> usdLedgerTrades = NormalizeTradesToUsd(ledgerTrades);
            GlitchTradeInsightsService.TradeInsightsSnapshot accountSnapshot =
                _tradeInsightsService.BuildSnapshotFromClosedTrades(usdLedgerTrades, warningEvents, nowUtc);
            List<FleetTradeAggregate> fleetTrades = BuildFleetTradeAggregates(usdLedgerTrades);
            GlitchTradeInsightsService.TradeInsightsSnapshot snapshot =
                _tradeInsightsService.BuildSnapshotFromClosedTrades(
                    fleetTrades.Select(aggregate => aggregate.Trade).ToList(),
                    warningEvents,
                    nowUtc);

            int distinctAccountsTraded = accountSnapshot.ClosedTrades
                .Select(trade => trade.AccountName ?? string.Empty)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            FleetTradeGroupStats fleetTradeGroups = ComputeFleetTradeGroupStats(fleetTrades);

            _summaryTradesValueText.Text = snapshot.All.Trades.ToString("N0", CultureInfo.CurrentCulture);
            if (_summaryFleetTradesValueText != null)
                _summaryFleetTradesValueText.Text = FormatSignedCurrency(snapshot.All.AvgWinningTradePoints);
            _summaryWinRateValueText.Text = snapshot.All.WinRate.ToString("P1", CultureInfo.CurrentCulture);
            _summaryNetPointsValueText.Text = FormatSignedCurrency(snapshot.All.NetPoints);
            _summaryNetPointsValueText.Foreground = ResolveSignedBrush(snapshot.All.NetPoints);
            _summaryProfitFactorValueText.Text = snapshot.All.ProfitFactor > 0 ? snapshot.All.ProfitFactor.ToString("N2", CultureInfo.CurrentCulture) : "-";
            _summaryAccountsValueText.Text = FormatSignedCurrency(snapshot.All.AvgLosingTradePoints);
            _summaryAsOfText.Text = L("summary.updated", "Updated") + ": " + nowUtc.ToLocalTime().ToString("MM-dd HH:mm:ss", CultureInfo.CurrentCulture);

            if (_summaryFleetTradesValueText != null)
                _summaryFleetTradesValueText.Foreground = ResolveSignedBrush(snapshot.All.AvgWinningTradePoints);
            if (_summaryAccountsValueText != null)
                _summaryAccountsValueText.Foreground = ResolveSignedBrush(snapshot.All.AvgLosingTradePoints);

            _summaryMetricRows.Clear();
            _summaryMetricRows.Add(new SummaryMetricRow { Metric = L("summary.total_trades", "Total Trades"), All = snapshot.All.Trades.ToString("N0"), Long = snapshot.Long.Trades.ToString("N0"), Short = snapshot.Short.Trades.ToString("N0") });
            _summaryMetricRows.Add(new SummaryMetricRow { Metric = L("summary.net_pnl", "Net PnL"), All = FormatSignedCurrency(snapshot.All.NetPoints), Long = FormatSignedCurrency(snapshot.Long.NetPoints), Short = FormatSignedCurrency(snapshot.Short.NetPoints) });
            _summaryMetricRows.Add(new SummaryMetricRow { Metric = L("summary.gross_profit", "Gross Profit"), All = FormatSignedCurrency(snapshot.All.GrossProfitPoints), Long = FormatSignedCurrency(snapshot.Long.GrossProfitPoints), Short = FormatSignedCurrency(snapshot.Short.GrossProfitPoints) });
            _summaryMetricRows.Add(new SummaryMetricRow { Metric = L("summary.gross_loss", "Gross Loss"), All = FormatSignedCurrency(snapshot.All.GrossLossPoints), Long = FormatSignedCurrency(snapshot.Long.GrossLossPoints), Short = FormatSignedCurrency(snapshot.Short.GrossLossPoints) });
            _summaryMetricRows.Add(new SummaryMetricRow { Metric = L("summary.avg_trade", "Avg Trade"), All = FormatSignedCurrency(snapshot.All.AvgTradePoints), Long = FormatSignedCurrency(snapshot.Long.AvgTradePoints), Short = FormatSignedCurrency(snapshot.Short.AvgTradePoints) });
            _summaryMetricRows.Add(new SummaryMetricRow { Metric = L("summary.avg_win", "Avg Win"), All = FormatSignedCurrency(snapshot.All.AvgWinningTradePoints), Long = FormatSignedCurrency(snapshot.Long.AvgWinningTradePoints), Short = FormatSignedCurrency(snapshot.Short.AvgWinningTradePoints) });
            _summaryMetricRows.Add(new SummaryMetricRow { Metric = L("summary.avg_loss", "Avg Loss"), All = FormatSignedCurrency(snapshot.All.AvgLosingTradePoints), Long = FormatSignedCurrency(snapshot.Long.AvgLosingTradePoints), Short = FormatSignedCurrency(snapshot.Short.AvgLosingTradePoints) });
            _summaryMetricRows.Add(new SummaryMetricRow { Metric = L("summary.largest_win", "Largest Win"), All = FormatSignedCurrency(snapshot.All.LargestWinningTradePoints), Long = FormatSignedCurrency(snapshot.Long.LargestWinningTradePoints), Short = FormatSignedCurrency(snapshot.Short.LargestWinningTradePoints) });
            _summaryMetricRows.Add(new SummaryMetricRow { Metric = L("summary.largest_loss", "Largest Loss"), All = FormatSignedCurrency(snapshot.All.LargestLosingTradePoints), Long = FormatSignedCurrency(snapshot.Long.LargestLosingTradePoints), Short = FormatSignedCurrency(snapshot.Short.LargestLosingTradePoints) });
            _summaryMetricRows.Add(new SummaryMetricRow { Metric = L("summary.win_rate", "Win Rate"), All = snapshot.All.WinRate.ToString("P1"), Long = snapshot.Long.WinRate.ToString("P1"), Short = snapshot.Short.WinRate.ToString("P1") });
            _summaryMetricRows.Add(new SummaryMetricRow { Metric = L("summary.profit_factor", "Profit Factor"), All = FormatRatio(snapshot.All.ProfitFactor), Long = FormatRatio(snapshot.Long.ProfitFactor), Short = FormatRatio(snapshot.Short.ProfitFactor) });
            _summaryMetricRows.Add(new SummaryMetricRow { Metric = L("summary.max_win_streak", "Max Win Streak"), All = snapshot.All.MaxConsecutiveWinners.ToString("N0"), Long = snapshot.Long.MaxConsecutiveWinners.ToString("N0"), Short = snapshot.Short.MaxConsecutiveWinners.ToString("N0") });
            _summaryMetricRows.Add(new SummaryMetricRow { Metric = L("summary.max_loss_streak", "Max Loss Streak"), All = snapshot.All.MaxConsecutiveLosers.ToString("N0"), Long = snapshot.Long.MaxConsecutiveLosers.ToString("N0"), Short = snapshot.Short.MaxConsecutiveLosers.ToString("N0") });
            _summaryMetricRows.Add(new SummaryMetricRow { Metric = L("summary.account_trades", "Account Trades"), All = accountSnapshot.All.Trades.ToString("N0"), Long = accountSnapshot.Long.Trades.ToString("N0"), Short = accountSnapshot.Short.Trades.ToString("N0") });
            _summaryMetricRows.Add(new SummaryMetricRow { Metric = L("summary.avg_contracts_trade", "Avg Contracts / Trade"), All = FormatAverage(fleetTradeGroups.AvgContractsAll), Long = FormatAverage(fleetTradeGroups.AvgContractsLong), Short = FormatAverage(fleetTradeGroups.AvgContractsShort) });
            _summaryMetricRows.Add(new SummaryMetricRow { Metric = L("summary.avg_duration", "Avg Duration"), All = FormatDuration(snapshot.All.AvgTradeDuration), Long = FormatDuration(snapshot.Long.AvgTradeDuration), Short = FormatDuration(snapshot.Short.AvgTradeDuration) });

            _summaryReasonLines.Clear();
            foreach (GlitchTradeInsightsService.TradeCloseReasonSummary reason in snapshot.CloseReasons.Take(8))
                _summaryReasonLines.Add($"{LocalizeCloseReason(reason.CloseReason)}: {reason.Trades.ToString("N0", CultureInfo.CurrentCulture)} | {reason.WinRate.ToString("P1", CultureInfo.CurrentCulture)} | {FormatSignedCurrency(reason.AvgPoints)}");

            _summaryRecentTrades.Clear();
            foreach (FleetTradeAggregate aggregate in fleetTrades.Take(50))
            {
                GlitchTradeInsightsService.TradeRoundTrip trade = aggregate.Trade;
                _summaryRecentTrades.Add(new SummaryTradeRow
                {
                    OpenTime = trade.EntryUtc.ToLocalTime().ToString("MM-dd HH:mm:ss", CultureInfo.CurrentCulture),
                    CloseTime = trade.ExitUtc.ToLocalTime().ToString("MM-dd HH:mm:ss", CultureInfo.CurrentCulture),
                    Account = aggregate.AccountLabel,
                    Instrument = trade.Instrument,
                    Side = trade.IsLong ? L("summary.side.long", "Long") : L("summary.side.short", "Short"),
                    Contracts = FormatContracts(trade.Contracts),
                    PnlPoints = FormatSignedCurrency(trade.PnlPoints),
                    PnlSign = GetPnlSign(trade.PnlPoints),
                    Duration = FormatDuration(trade.Duration),
                    Exit = FormatCloseReasonCompact(trade.CloseReason),
                    CloseReason = LocalizeCloseReason(trade.CloseReason)
                });
            }
        }

        private void OnSummaryRootSizeChanged(object sender, SizeChangedEventArgs e)
        {
            ApplySummaryResponsiveLayout(e.NewSize.Width);
        }

        private void ApplySummaryResponsiveLayout(double width)
        {
            if (_summaryRootGrid == null || _summaryCardsPanel == null || _summaryMidGrid == null)
                return;
            if (_isApplyingSummaryResponsiveLayout)
                return;

            try
            {
                _isApplyingSummaryResponsiveLayout = true;
                double usableWidth = Math.Max(320, width);
                const double hysteresis = 24.0;
                bool narrow = ResolveBelowBreakpoint(usableWidth, _summaryNarrowLayout, 900, hysteresis);

                UpdateSummaryCardsPanelLayout(usableWidth);

                if (_summaryNarrowLayout.HasValue && _summaryNarrowLayout.Value == narrow)
                    return;

                _summaryNarrowLayout = narrow;
                _summaryRootGrid.Margin = narrow ? new Thickness(10) : new Thickness(20);
                _summaryMidGrid.ColumnDefinitions.Clear();
                _summaryMidGrid.RowDefinitions.Clear();
                if (narrow)
                {
                    _summaryMidGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    _summaryMidGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    _summaryMidGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                    if (_summaryPerformanceGrid != null)
                    {
                        _summaryPerformanceGrid.MinHeight = 160;
                        Grid.SetColumn(_summaryPerformanceGrid, 0);
                        Grid.SetRow(_summaryPerformanceGrid, 0);
                    }

                    if (_summaryReasonsPanel != null)
                    {
                        _summaryReasonsPanel.Margin = new Thickness(0, 10, 0, 0);
                        Grid.SetColumn(_summaryReasonsPanel, 0);
                        Grid.SetRow(_summaryReasonsPanel, 1);
                    }
                }
                else
                {
                    _summaryMidGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.62, GridUnitType.Star) });
                    _summaryMidGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
                    _summaryMidGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.38, GridUnitType.Star) });
                    _summaryMidGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                    if (_summaryPerformanceGrid != null)
                    {
                        _summaryPerformanceGrid.MinHeight = 180;
                        Grid.SetColumn(_summaryPerformanceGrid, 0);
                        Grid.SetRow(_summaryPerformanceGrid, 0);
                    }

                    if (_summaryReasonsPanel != null)
                    {
                        _summaryReasonsPanel.Margin = new Thickness(0);
                        Grid.SetColumn(_summaryReasonsPanel, 2);
                        Grid.SetRow(_summaryReasonsPanel, 0);
                    }
                }

                if (_summaryReasonsList != null)
                    _summaryReasonsList.MinHeight = narrow ? 130 : 180;
                if (_summaryRecentTradesGrid != null)
                    _summaryRecentTradesGrid.MinHeight = narrow ? 180 : 220;
            }
            finally
            {
                _isApplyingSummaryResponsiveLayout = false;
            }
        }

        private void UpdateSummaryCardsPanelLayout(double usableWidth)
        {
            if (_summaryCardsPanel == null)
                return;

            int cardCount = _summaryCardsPanel.Children.Count;
            if (cardCount <= 0)
                return;

            const double minCardWidth = 170.0;
            const double gap = 8.0;
            int columns = (int)Math.Floor((usableWidth + gap) / (minCardWidth + gap));
            columns = Math.Max(1, Math.Min(columns, cardCount));
            _summaryCardsPanel.Columns = columns;

            int totalRows = (int)Math.Ceiling(cardCount / (double)columns);
            for (int i = 0; i < cardCount; i++)
            {
                if (!(_summaryCardsPanel.Children[i] is FrameworkElement item))
                    continue;

                int rowIndex = i / columns;
                int columnIndex = i % columns;
                bool isLastRow = rowIndex == totalRows - 1;
                bool isLastColumn = columnIndex == columns - 1 || i == cardCount - 1;
                item.Margin = new Thickness(0, 0, gap, isLastRow ? 0 : gap);
                if (isLastColumn)
                    item.Margin = new Thickness(0, 0, 0, isLastRow ? 0 : gap);
            }
        }

        private async void OnResetSummaryAndJournalDataClick(object sender, RoutedEventArgs e)
        {
            if (!ShowResetDataConfirmationDialog())
                return;

            ResetSummaryAndJournalData();
            if (sender is Button button)
                await ShowTransientTealButtonFeedbackAsync(button, "Data reset", 3000);
        }

        private bool ShowResetDataConfirmationDialog()
        {
            var dialog = new NTWindow
            {
                Caption = L("summary.reset_data.title", "Reset Data"),
                Title = L("summary.reset_data.title", "Reset Data"),
                Width = 520,
                Height = 230,
                MinWidth = 460,
                MinHeight = 210,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Owner = this
            };
            ApplySkinResource(dialog, Control.BackgroundProperty, "BackgroundMainWindow", "GridEntireBackground", "BackgroundTextInput");
            ApplySkinResource(dialog, Control.ForegroundProperty, "FontControlBrush", "FontTableBrush", "GridRowForeground");

            var shell = new Border
            {
                Padding = new Thickness(14),
                BorderThickness = new Thickness(1)
            };
            ApplySkinResource(shell, Border.BackgroundProperty, "BackgroundMainWindow", "GridEntireBackground", "BackgroundTextInput");
            ApplySkinResource(shell, Border.BorderBrushProperty, "BorderThinBrush", "TabControlBorderBrush", "BorderMainWindowBrush");

            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var titleText = new TextBlock
            {
                Text = L("summary.reset_data.prompt", "Your data will be reset including statistics about your trades and accounts. Confirm?"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10),
                FontWeight = FontWeights.SemiBold
            };
            ApplySkinResource(titleText, TextBlock.ForegroundProperty, "FontHeaderLevel4Brush", "FontControlBrush", "FontTableBrush");
            Grid.SetRow(titleText, 0);
            layout.Children.Add(titleText);

            var detailsText = new TextBlock
            {
                Text = L("summary.reset_data.details", "This clears Journal, Critical Warnings, and all Summary trade statistics so you can start over."),
                TextWrapping = TextWrapping.Wrap
            };
            ApplySkinResource(detailsText, TextBlock.ForegroundProperty, "FontControlBrush", "FontTableBrush");
            Grid.SetRow(detailsText, 1);
            layout.Children.Add(detailsText);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };

            bool accepted = false;
            var proceed = new Button
            {
                Content = L("dialog.common.proceed", "Proceed"),
                MinWidth = 88,
                Margin = new Thickness(0, 0, 8, 0),
                Style = CreateGroupActionButtonStyle(layout)
            };
            proceed.IsDefault = true;
            proceed.Click += (_, __) =>
            {
                accepted = true;
                dialog.Close();
            };
            buttons.Children.Add(proceed);

            var cancel = new Button
            {
                Content = L("dialog.common.cancel", "Cancel"),
                MinWidth = 88,
                Style = CreateGroupActionButtonStyle(layout)
            };
            cancel.IsCancel = true;
            cancel.Click += (_, __) => dialog.Close();
            buttons.Children.Add(cancel);

            Grid.SetRow(buttons, 2);
            layout.Children.Add(buttons);

            shell.Child = layout;
            dialog.Content = shell;
            dialog.ShowDialog();
            return accepted;
        }

        private void ResetSummaryAndJournalData()
        {
            DateTime nowUtc = DateTime.UtcNow;

            _journalEntries.Clear();
            _criticalWarningEntries.Clear();
            _hasPendingAuditWrite = true;
            SaveAuditFeedsToDisk(force: true);
            UpdateWarningCountUi();

            _tradeLedgerService.Reset(nowUtc);
            _riskLockLedgerService.Reset(nowUtc);

            _summaryMetricRows.Clear();
            _summaryReasonLines.Clear();
            _summaryRecentTrades.Clear();
            _summaryLastExecutionCount = -1;
            _summaryLastExecutionTicks = -1;
            _summaryLastWarningCount = -1;

            _lastOrderJournalSnapshotByKey.Clear();
            _lastPositionJournalSnapshotByKey.Clear();
            _lastExecutionJournalSnapshotByKey.Clear();
            _runtimeJournalCooldownByKey.Clear();

            if (_journalEntriesView != null)
                _journalEntriesView.Refresh();

            RefreshSummaryInsightsIfNeeded(nowUtc, true);
        }

        private string LocalizeCloseReason(string closeReason)
        {
            string token = string.IsNullOrWhiteSpace(closeReason) ? string.Empty : closeReason.Trim();
            if (token.Length == 0)
                return L("summary.reason.unknown", "Unknown");

            if (token.Equals("Signal Flip", StringComparison.OrdinalIgnoreCase))
                return L("summary.reason.signal_flip", "Signal Flip");
            if (token.Equals("Stop Loss", StringComparison.OrdinalIgnoreCase))
                return L("summary.reason.stop_loss", "Stop Loss");
            if (token.Equals("Take Profit", StringComparison.OrdinalIgnoreCase))
                return L("summary.reason.take_profit", "Take Profit");
            if (token.Equals("Replication Sync", StringComparison.OrdinalIgnoreCase))
                return L("summary.reason.replication_sync", "Replication Sync");
            if (token.Equals("Session End", StringComparison.OrdinalIgnoreCase))
                return L("summary.reason.session_end", "Session End");
            if (token.Equals("News Event", StringComparison.OrdinalIgnoreCase))
                return L("summary.reason.news_event", "News Event");
            if (token.Equals("Risk Management", StringComparison.OrdinalIgnoreCase))
                return L("summary.reason.risk_management", "Risk Management");
            if (token.Equals("Manual / Other", StringComparison.OrdinalIgnoreCase))
                return L("summary.reason.manual_other", "Manual / Other");
            if (token.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                return L("summary.reason.unknown", "Unknown");

            return closeReason;
        }

        private string LocalizeOpenReason(string openReason)
        {
            string token = string.IsNullOrWhiteSpace(openReason) ? string.Empty : openReason.Trim();
            if (token.Length == 0)
                return L("summary.open_reason.unknown", "Manual / Unknown");

            if (token.Equals("Manual Entry", StringComparison.OrdinalIgnoreCase))
                return L("summary.open_reason.manual_entry", "Manual Entry");
            if (token.Equals("Replication Sync", StringComparison.OrdinalIgnoreCase))
                return L("summary.open_reason.replication_sync", "Replication Sync");
            if (token.Equals("Protective Follow-up", StringComparison.OrdinalIgnoreCase))
                return L("summary.open_reason.protective_followup", "Protective Follow-up");
            if (token.Equals("Manual / Unknown", StringComparison.OrdinalIgnoreCase))
                return L("summary.open_reason.unknown", "Manual / Unknown");

            return openReason;
        }

        private string FormatCloseReasonCompact(string closeReason)
        {
            string token = string.IsNullOrWhiteSpace(closeReason) ? string.Empty : closeReason.Trim();
            if (token.Length == 0)
                return L("summary.exit.unknown", "UNK");

            if (token.Equals("Stop Loss", StringComparison.OrdinalIgnoreCase))
                return L("summary.exit.stop_loss", "SL");
            if (token.Equals("Take Profit", StringComparison.OrdinalIgnoreCase))
                return L("summary.exit.take_profit", "TP");
            if (token.Equals("Risk Management", StringComparison.OrdinalIgnoreCase))
                return L("summary.exit.risk_management", "RM");
            if (token.Equals("Replication Sync", StringComparison.OrdinalIgnoreCase))
                return L("summary.exit.replication_sync", "Sync");
            if (token.Equals("Session End", StringComparison.OrdinalIgnoreCase))
                return L("summary.exit.session_end", "Close");
            if (token.Equals("News Event", StringComparison.OrdinalIgnoreCase))
                return L("summary.exit.news_event", "News");
            if (token.Equals("Signal Flip", StringComparison.OrdinalIgnoreCase))
                return L("summary.exit.signal_flip", "Flip");
            if (token.Equals("Manual / Other", StringComparison.OrdinalIgnoreCase))
                return L("summary.exit.manual", "Manual");
            if (token.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                return L("summary.exit.unknown", "UNK");

            string compact = token.Replace(" ", string.Empty).Replace("/", string.Empty);
            return compact.Length <= 10 ? compact : compact.Substring(0, 10);
        }

        private static Brush ResolveSignedBrush(double value)
        {
            if (value > 0)
                return TealAccentBrush;
            if (value < 0)
                return OrangeAccentBrush;
            return Brushes.White;
        }

        private static string FormatRatio(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
                return "-";
            return value.ToString("N2", CultureInfo.CurrentCulture);
        }

        private static string FormatDuration(TimeSpan duration)
        {
            if (duration <= TimeSpan.Zero)
                return "0s";
            if (duration.TotalHours >= 1)
                return string.Format(CultureInfo.CurrentCulture, "{0}h {1}m", Math.Floor(duration.TotalHours).ToString("N0", CultureInfo.CurrentCulture), duration.Minutes.ToString("N0", CultureInfo.CurrentCulture));
            if (duration.TotalMinutes >= 1)
                return string.Format(CultureInfo.CurrentCulture, "{0}m {1}s", Math.Floor(duration.TotalMinutes).ToString("N0", CultureInfo.CurrentCulture), duration.Seconds.ToString("N0", CultureInfo.CurrentCulture));
            return Math.Max(0, duration.Seconds).ToString("N0", CultureInfo.CurrentCulture) + "s";
        }

        private static string FormatContracts(double contracts)
        {
            if (Math.Abs(contracts - Math.Round(contracts)) <= 1e-6)
                return Math.Round(contracts).ToString("N0", CultureInfo.CurrentCulture);
            return contracts.ToString("N2", CultureInfo.CurrentCulture);
        }

        private static string FormatAverage(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return "-";
            return value.ToString("0.##", CultureInfo.CurrentCulture);
        }

        private static string FormatSignedCurrency(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return "-";

            string amount = Math.Abs(value).ToString("N2", CultureInfo.CurrentCulture);
            if (value > 0)
                return "+$" + amount;
            if (value < 0)
                return "-$" + amount;
            return "$0.00";
        }

        private static IReadOnlyList<GlitchTradeInsightsService.TradeRoundTrip> NormalizeTradesToUsd(
            IReadOnlyList<GlitchTradeInsightsService.TradeRoundTrip> trades)
        {
            if (trades == null || trades.Count == 0)
                return new List<GlitchTradeInsightsService.TradeRoundTrip>();

            var normalized = new List<GlitchTradeInsightsService.TradeRoundTrip>(trades.Count);
            foreach (GlitchTradeInsightsService.TradeRoundTrip trade in trades)
            {
                if (trade == null)
                    continue;

                double pointValue = ResolveInstrumentPointValue(trade.Instrument);
                normalized.Add(CloneTradeForDisplay(trade, trade.PnlPoints * pointValue));
            }

            return normalized;
        }

        private static GlitchTradeInsightsService.TradeRoundTrip CloneTradeForDisplay(
            GlitchTradeInsightsService.TradeRoundTrip trade,
            double pnlDisplayValue)
        {
            return new GlitchTradeInsightsService.TradeRoundTrip
            {
                TradeId = trade.TradeId,
                AccountName = trade.AccountName,
                Instrument = trade.Instrument,
                EntryUtc = trade.EntryUtc,
                ExitUtc = trade.ExitUtc,
                Duration = trade.Duration,
                IsLong = trade.IsLong,
                Contracts = trade.Contracts,
                EntryPrice = trade.EntryPrice,
                ExitPrice = trade.ExitPrice,
                PnlPoints = pnlDisplayValue,
                OpenReason = trade.OpenReason,
                CloseReason = trade.CloseReason,
                TradeSource = trade.TradeSource,
                EntryType = trade.EntryType,
                ExitType = trade.ExitType,
                EntrySignal = trade.EntrySignal,
                ExitSignal = trade.ExitSignal,
                EntrySession = trade.EntrySession,
                ExitSession = trade.ExitSession
            };
        }

        private static double ResolveInstrumentPointValue(string instrumentName)
        {
            foreach (string candidate in BuildInstrumentLookupCandidates(instrumentName))
            {
                lock (InstrumentPointValueSync)
                {
                    if (CachedInstrumentPointValues.TryGetValue(candidate, out double cachedValue) && cachedValue > 0)
                        return cachedValue;
                }

                double resolved = TryResolvePointValue(candidate, "GetInstrument");
                if (resolved > 0)
                {
                    CacheInstrumentPointValue(candidate, resolved);
                    return resolved;
                }

                resolved = TryResolvePointValue(candidate, "GetInstrumentFuzzy");
                if (resolved > 0)
                {
                    CacheInstrumentPointValue(candidate, resolved);
                    return resolved;
                }

                if (FallbackInstrumentPointValues.TryGetValue(candidate, out double fallbackValue) && fallbackValue > 0)
                {
                    CacheInstrumentPointValue(candidate, fallbackValue);
                    return fallbackValue;
                }
            }

            return 1.0;
        }

        private static void CacheInstrumentPointValue(string candidate, double value)
        {
            if (string.IsNullOrWhiteSpace(candidate) || value <= 0)
                return;

            lock (InstrumentPointValueSync)
                CachedInstrumentPointValues[candidate] = value;
        }

        private static IEnumerable<string> BuildInstrumentLookupCandidates(string instrumentName)
        {
            var candidates = new List<string>();
            string token = string.IsNullOrWhiteSpace(instrumentName) ? string.Empty : instrumentName.Trim().ToUpperInvariant();
            if (token.Length == 0)
                return candidates;

            candidates.Add(token);

            string beforeSpace = token.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(beforeSpace))
                candidates.Add(beforeSpace);

            return candidates
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static double TryResolvePointValue(string candidate, string methodName)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                return 0;

            try
            {
                MethodInfo resolver = typeof(Instrument)
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(method =>
                        string.Equals(method.Name, methodName, StringComparison.Ordinal) &&
                        method.ReturnType == typeof(Instrument) &&
                        method.GetParameters().Length == 1 &&
                        method.GetParameters()[0].ParameterType == typeof(string));
                if (resolver == null)
                    return 0;

                var instrument = resolver.Invoke(null, new object[] { candidate }) as Instrument;
                double pointValue = instrument?.MasterInstrument?.PointValue ?? 0;
                return pointValue > 0 ? pointValue : 0;
            }
            catch
            {
                return 0;
            }
        }

        private List<FleetTradeAggregate> BuildFleetTradeAggregates(IReadOnlyList<GlitchTradeInsightsService.TradeRoundTrip> trades)
        {
            var results = new List<FleetTradeAggregate>();
            if (trades == null || trades.Count == 0)
                return results;

            const long entryBucketTicks = TimeSpan.TicksPerSecond * 5;
            const long exitBucketTicks = TimeSpan.TicksPerSecond * 5;

            foreach (var group in trades
                .Where(trade => trade != null)
                .OrderByDescending(trade => trade.ExitUtc)
                .ThenByDescending(trade => trade.EntryUtc)
                .GroupBy(trade =>
                {
                    long entryBucket = trade.EntryUtc.ToUniversalTime().Ticks / entryBucketTicks;
                    long exitBucket = trade.ExitUtc.ToUniversalTime().Ticks / exitBucketTicks;
                    string instrument = (trade.Instrument ?? string.Empty).Trim().ToUpperInvariant();
                    string side = trade.IsLong ? "L" : "S";
                    return string.Join("|", instrument, side, entryBucket.ToString(CultureInfo.InvariantCulture), exitBucket.ToString(CultureInfo.InvariantCulture));
                }))
            {
                List<GlitchTradeInsightsService.TradeRoundTrip> groupedTrades = group
                    .OrderBy(trade => trade.EntryUtc)
                    .ThenBy(trade => trade.ExitUtc)
                    .ToList();
                if (groupedTrades.Count == 0)
                    continue;

                GlitchTradeInsightsService.TradeRoundTrip first = groupedTrades[0];
                double totalContracts = groupedTrades.Sum(trade => Math.Abs(trade.Contracts));
                if (totalContracts <= 0)
                    totalContracts = groupedTrades.Count;

                DateTime entryUtc = groupedTrades.Min(trade => trade.EntryUtc);
                DateTime exitUtc = groupedTrades.Max(trade => trade.ExitUtc);
                double entryPrice = totalContracts > 0
                    ? groupedTrades.Sum(trade => Math.Abs(trade.Contracts) * trade.EntryPrice) / totalContracts
                    : first.EntryPrice;
                double exitPrice = totalContracts > 0
                    ? groupedTrades.Sum(trade => Math.Abs(trade.Contracts) * trade.ExitPrice) / totalContracts
                    : first.ExitPrice;
                double pnlPoints = groupedTrades.Sum(trade => trade.PnlPoints);
                int accountCount = groupedTrades
                    .Select(trade => trade.AccountName ?? string.Empty)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();

                string openReason = ResolveAggregateReason(groupedTrades.Select(trade => trade.OpenReason));
                string closeReason = ResolveAggregateReason(groupedTrades.Select(trade => trade.CloseReason));
                string tradeSource = ResolveAggregateToken(groupedTrades.Select(trade => trade.TradeSource), "Mixed");
                string entryType = ResolveAggregateToken(groupedTrades.Select(trade => trade.EntryType), "Mixed");
                string exitType = ResolveAggregateToken(groupedTrades.Select(trade => trade.ExitType), "Mixed");
                string entrySignal = ResolveAggregateToken(groupedTrades.Select(trade => trade.EntrySignal), "Mixed");
                string exitSignal = ResolveAggregateToken(groupedTrades.Select(trade => trade.ExitSignal), "Mixed");
                string accountLabel = accountCount <= 1
                    ? (first.AccountName ?? string.Empty)
                    : Lf("summary.accounts_count_format", "{0} accounts", accountCount);

                var fleetTrade = new GlitchTradeInsightsService.TradeRoundTrip
                {
                    AccountName = accountLabel,
                    Instrument = first.Instrument,
                    EntryUtc = entryUtc,
                    ExitUtc = exitUtc,
                    Duration = exitUtc > entryUtc ? (exitUtc - entryUtc) : TimeSpan.Zero,
                    IsLong = first.IsLong,
                    EntryPrice = entryPrice,
                    ExitPrice = exitPrice,
                    Contracts = totalContracts,
                    PnlPoints = pnlPoints,
                    OpenReason = openReason,
                    CloseReason = closeReason,
                    TradeSource = tradeSource,
                    EntryType = entryType,
                    ExitType = exitType,
                    EntrySignal = entrySignal,
                    ExitSignal = exitSignal,
                    EntrySession = first.EntrySession,
                    ExitSession = groupedTrades.OrderByDescending(trade => trade.ExitUtc).First().ExitSession
                };
                fleetTrade.TradeId = GlitchTradeInsightsService.BuildTradeId(fleetTrade);

                results.Add(new FleetTradeAggregate
                {
                    Trade = fleetTrade,
                    AccountCount = Math.Max(1, accountCount),
                    AccountLabel = accountLabel
                });
            }

            return results
                .OrderByDescending(aggregate => aggregate.Trade.ExitUtc)
                .ThenByDescending(aggregate => aggregate.Trade.EntryUtc)
                .ToList();
        }

        private static string ResolveAggregateReason(IEnumerable<string> reasons)
        {
            List<string> normalized = (reasons ?? Enumerable.Empty<string>())
                .Where(reason => !string.IsNullOrWhiteSpace(reason))
                .Select(reason => reason.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (normalized.Count == 0)
                return "Unknown";
            if (normalized.Count == 1)
                return normalized[0];
            if (normalized.Any(reason => reason.Equals("Risk Management", StringComparison.OrdinalIgnoreCase)))
                return "Risk Management";
            if (normalized.Any(reason => reason.Equals("Stop Loss", StringComparison.OrdinalIgnoreCase)))
                return "Stop Loss";
            if (normalized.Any(reason => reason.Equals("Take Profit", StringComparison.OrdinalIgnoreCase)))
                return "Take Profit";

            return "Manual / Other";
        }

        private static string ResolveAggregateToken(IEnumerable<string> values, string fallback)
        {
            List<string> normalized = (values ?? Enumerable.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (normalized.Count == 0)
                return fallback;
            if (normalized.Count == 1)
                return normalized[0];
            return fallback;
        }

        private static FleetTradeGroupStats ComputeFleetTradeGroupStats(IReadOnlyList<FleetTradeAggregate> trades)
        {
            var stats = new FleetTradeGroupStats();
            if (trades == null || trades.Count == 0)
                return stats;

            List<FleetTradeAggregate> allTrades = trades.Where(trade => trade?.Trade != null).ToList();
            List<FleetTradeAggregate> longTrades = allTrades.Where(trade => trade.Trade.IsLong).ToList();
            List<FleetTradeAggregate> shortTrades = allTrades.Where(trade => !trade.Trade.IsLong).ToList();

            stats.All = allTrades.Count;
            stats.Long = longTrades.Count;
            stats.Short = shortTrades.Count;
            stats.AvgAccountsAll = allTrades.Count > 0 ? allTrades.Average(trade => trade.AccountCount) : 0;
            stats.AvgAccountsLong = longTrades.Count > 0 ? longTrades.Average(trade => trade.AccountCount) : 0;
            stats.AvgAccountsShort = shortTrades.Count > 0 ? shortTrades.Average(trade => trade.AccountCount) : 0;
            stats.AvgContractsAll = allTrades.Count > 0 ? allTrades.Average(trade => Math.Abs(trade.Trade.Contracts)) : 0;
            stats.AvgContractsLong = longTrades.Count > 0 ? longTrades.Average(trade => Math.Abs(trade.Trade.Contracts)) : 0;
            stats.AvgContractsShort = shortTrades.Count > 0 ? shortTrades.Average(trade => Math.Abs(trade.Trade.Contracts)) : 0;
            return stats;
        }

        private sealed class FleetTradeAggregate
        {
            public GlitchTradeInsightsService.TradeRoundTrip Trade { get; set; }
            public int AccountCount { get; set; }
            public string AccountLabel { get; set; }
        }

        private sealed class FleetTradeGroupStats
        {
            public int All { get; set; }
            public int Long { get; set; }
            public int Short { get; set; }
            public double AvgAccountsAll { get; set; }
            public double AvgAccountsLong { get; set; }
            public double AvgAccountsShort { get; set; }
            public double AvgContractsAll { get; set; }
            public double AvgContractsLong { get; set; }
            public double AvgContractsShort { get; set; }
        }
    }
}
