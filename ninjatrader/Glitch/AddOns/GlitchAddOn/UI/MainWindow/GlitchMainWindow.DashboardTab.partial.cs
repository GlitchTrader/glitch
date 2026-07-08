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
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
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
        private UIElement CreateAccountsTabImpl()
        {
            var root = new Grid { Margin = new Thickness(20) };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.VerticalAlignment = VerticalAlignment.Stretch;
            root.HorizontalAlignment = HorizontalAlignment.Stretch;
            _dashboardRootGrid = root;
            _dashboardRootGrid.SizeChanged += OnDashboardRootSizeChanged;

            var grid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                ItemsSource = _accountRows,
                VerticalAlignment = VerticalAlignment.Top
            };
            ConfigureDataGridForPageScroll(grid, allowHorizontalScroll: true);
            ApplySkinResource(grid, Control.BackgroundProperty, "BackgroundMainWindow", "GridEntireBackground");
            ApplySkinResource(grid, Control.ForegroundProperty, "FontControlBrush", "FontTableBrush");
            ApplySkinResource(grid, Control.BorderBrushProperty, "BorderThinBrush", "TabControlBorderBrush");
            ApplySkinResource(grid, DataGrid.HorizontalGridLinesBrushProperty, "BorderThinBrush", "GridHeaderHighlight");
            ApplySkinResource(grid, DataGrid.VerticalGridLinesBrushProperty, "BorderThinBrush", "GridHeaderHighlight");
            grid.FocusVisualStyle = null;
            // Avoid doubled outer edge: cell/header borders already draw right/bottom edges.
            grid.BorderThickness = new Thickness(1, 1, 0, 0);
            grid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
            grid.AlternationCount = 2;
            ApplySkinResource(grid, DataGrid.RowBackgroundProperty, "BackgroundMainWindow", "GridEntireBackground");
            ApplySkinResource(grid, DataGrid.AlternatingRowBackgroundProperty, "BackgroundMainWindow", "GridEntireBackground", "BackgroundTextInput");
            grid.BeginningEdit += OnAccountsGridBeginningEdit;
            grid.CellEditEnding += OnAccountsGridCellEditEnding;
            grid.LostKeyboardFocus += OnAccountsGridLostKeyboardFocus;
            _accountsGrid = grid;

            var leftHeaderStyle = CreateSkinAwareColumnHeaderStyle(grid, HorizontalAlignment.Left);
            var centerHeaderStyle = CreateSkinAwareColumnHeaderStyle(grid, HorizontalAlignment.Center);
            var leftTextStyle = CreateTextBlockElementStyle(grid, HorizontalAlignment.Left, TextAlignment.Left, new Thickness(6, 3, 6, 3));
            var centerTextStyle = CreateTextBlockElementStyle(grid, HorizontalAlignment.Center, TextAlignment.Center, new Thickness(6, 3, 6, 3));
            var equityTextStyle = CreatePnlTextBlockElementStyle(grid, HorizontalAlignment.Center, TextAlignment.Center, new Thickness(6, 3, 6, 3), nameof(AccountGridRow.EquityVsSizeSign));
            var bufferRiskTextStyle = CreatePnlTextBlockElementStyle(grid, HorizontalAlignment.Center, TextAlignment.Center, new Thickness(6, 3, 6, 3), nameof(AccountGridRow.BufferVsMaxDdSign));
            var netLiqWarningTextStyle = CreateWarningTextBlockElementStyle(grid, HorizontalAlignment.Center, TextAlignment.Center, new Thickness(6, 3, 6, 3), nameof(AccountGridRow.IsNetLiqWarning));
            var realizedPnlTextStyle = CreatePnlTextBlockElementStyle(grid, HorizontalAlignment.Center, TextAlignment.Center, new Thickness(6, 3, 6, 3), nameof(AccountGridRow.RealizedPnlSign));
            var unrealizedPnlTextStyle = CreatePnlTextBlockElementStyle(grid, HorizontalAlignment.Center, TextAlignment.Center, new Thickness(6, 3, 6, 3), nameof(AccountGridRow.UnrealizedPnlSign));
            var totalPnlTextStyle = CreatePnlTextBlockElementStyle(grid, HorizontalAlignment.Center, TextAlignment.Center, new Thickness(6, 3, 6, 3), nameof(AccountGridRow.TotalPnlSign));
            var riskPercentTextStyle = CreatePnlTextBlockElementStyle(grid, HorizontalAlignment.Center, TextAlignment.Center, new Thickness(6, 3, 6, 3), nameof(AccountGridRow.RiskSign));
            var comboStyle = CreateSkinAwareComboBoxStyle(grid);

            grid.ColumnHeaderStyle = leftHeaderStyle;
            grid.RowStyle = CreateSkinAwareRowStyle(grid);
            grid.CellStyle = CreateSkinAwareCellStyle(grid);
            ApplyDataGridSelectionResources(grid, grid);

            var displayNameColumn = CreateTextColumn(L("dashboard.column.account_name", "Account Name"), nameof(AccountGridRow.DisplayName), leftTextStyle, leftHeaderStyle);
            displayNameColumn.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
            displayNameColumn.MinWidth = 180;
            displayNameColumn.IsReadOnly = true;
            BindLocalizedColumnHeader(displayNameColumn, "dashboard.column.account_name", "Account Name");
            grid.Columns.Add(displayNameColumn);

            var riskColumn = CreateRiskBarColumn(L("dashboard.column.risk", "Risk"), centerHeaderStyle, riskPercentTextStyle, grid);
            riskColumn.IsReadOnly = true;
            riskColumn.MinWidth = 148;
            riskColumn.Width = DataGridLength.Auto;
            BindLocalizedColumnHeader(riskColumn, "dashboard.column.risk", "Risk");
            grid.Columns.Add(riskColumn);

            var statusColumn = CreateEditableComboColumn(
                L("dashboard.column.status", "Status"),
                nameof(AccountGridRow.AccountStatus),
                nameof(AccountGridRow.AccountStatusOptions),
                centerTextStyle,
                centerHeaderStyle,
                comboStyle,
                minWidth: 58);
            BindLocalizedColumnHeader(statusColumn, "dashboard.column.status", "Status");
            grid.Columns.Add(statusColumn);
            var firmColumn = CreateEditableComboColumn(
                L("dashboard.column.firm", "Firm"),
                nameof(AccountGridRow.PropFirmDisplay),
                nameof(AccountGridRow.PropFirmOptions),
                centerTextStyle,
                centerHeaderStyle,
                comboStyle,
                minWidth: 66);
            BindLocalizedColumnHeader(firmColumn, "dashboard.column.firm", "Firm");
            grid.Columns.Add(firmColumn);
            var sizeColumn = CreateEditableComboColumn(
                L("dashboard.column.size", "Size"),
                nameof(AccountGridRow.AccountSizeSelection),
                nameof(AccountGridRow.AccountSizeOptions),
                centerTextStyle,
                centerHeaderStyle,
                comboStyle,
                minWidth: 58);
            BindLocalizedColumnHeader(sizeColumn, "dashboard.column.size", "Size");
            grid.Columns.Add(sizeColumn);

            var balanceColumn = CreateTextColumn(L("dashboard.column.equity", "Equity"), nameof(AccountGridRow.CashValue), equityTextStyle, centerHeaderStyle);
            balanceColumn.IsReadOnly = true;
            balanceColumn.MinWidth = 82;
            BindLocalizedColumnHeader(balanceColumn, "dashboard.column.equity", "Equity");
            grid.Columns.Add(balanceColumn);

            var minMarginColumn = CreateTextColumn(L("dashboard.column.netliq", "NetLiq"), nameof(AccountGridRow.MinMargin), netLiqWarningTextStyle, centerHeaderStyle);
            minMarginColumn.IsReadOnly = true;
            minMarginColumn.MinWidth = 76;
            BindLocalizedColumnHeader(minMarginColumn, "dashboard.column.netliq", "NetLiq");
            grid.Columns.Add(minMarginColumn);

            var bufferColumn = CreateTextColumn(L("dashboard.column.buffer", "Buffer"), nameof(AccountGridRow.BufferMargin), bufferRiskTextStyle, centerHeaderStyle);
            bufferColumn.IsReadOnly = true;
            bufferColumn.MinWidth = 72;
            BindLocalizedColumnHeader(bufferColumn, "dashboard.column.buffer", "Buffer");
            grid.Columns.Add(bufferColumn);

            var realizedColumn = CreateTextColumn(L("dashboard.column.realized_pnl", "R PnL"), nameof(AccountGridRow.RealizedPnl), realizedPnlTextStyle, centerHeaderStyle);
            realizedColumn.IsReadOnly = true;
            realizedColumn.MinWidth = 64;
            BindLocalizedColumnHeader(realizedColumn, "dashboard.column.realized_pnl", "R PnL");
            grid.Columns.Add(realizedColumn);

            var unrealizedColumn = CreateTextColumn(L("dashboard.column.unrealized_pnl", "U PnL"), nameof(AccountGridRow.UnrealizedPnl), unrealizedPnlTextStyle, centerHeaderStyle);
            unrealizedColumn.IsReadOnly = true;
            unrealizedColumn.MinWidth = 64;
            BindLocalizedColumnHeader(unrealizedColumn, "dashboard.column.unrealized_pnl", "U PnL");
            grid.Columns.Add(unrealizedColumn);

            var totalColumn = CreateTextColumn(L("dashboard.column.pnl", "PnL"), nameof(AccountGridRow.TotalPnl), totalPnlTextStyle, centerHeaderStyle);
            totalColumn.IsReadOnly = true;
            totalColumn.MinWidth = 64;
            BindLocalizedColumnHeader(totalColumn, "dashboard.column.pnl", "PnL");
            grid.Columns.Add(totalColumn);

            var groupsContent = CreateAccountGroupsSection(root);
            _dashboardGroupsSection = groupsContent as FrameworkElement;

            var accordionStack = new StackPanel();
            _dashboardReplicationExpander = CreateAccordionExpander(root, "dashboard.replication_groups", "Replication Groups");
            _dashboardReplicationExpander.IsExpanded = true;
            _dashboardReplicationExpander.Content = WrapAccordionSectionContent(groupsContent);
            accordionStack.Children.Add(_dashboardReplicationExpander);

            _dashboardConnectedAccountsExpander = CreateAccordionExpander(root, "dashboard.connected_accounts", "Connected Accounts");
            _dashboardConnectedAccountsExpander.IsExpanded = false;
            _dashboardConnectedAccountsExpander.Content = WrapAccordionSectionContent(grid);
            accordionStack.Children.Add(_dashboardConnectedAccountsExpander);

            _dashboardPageScroll = CreateAccordionPageScrollHost(accordionStack);
            Grid.SetRow(_dashboardPageScroll, 0);
            root.Children.Add(_dashboardPageScroll);

            ApplyDashboardResponsiveLayout(root.ActualWidth > 0 ? root.ActualWidth : Width);
            return WrapTabBodyForScroll(root);
        }


        private UIElement CreateAccountGroupsSection(FrameworkElement context)
                {
                    _accountGroupsHostPanel = new StackPanel
                    {
                        Orientation = Orientation.Vertical,
                        Background = Brushes.Transparent
                    };

                    RebuildAccountGroupsUi();
                    return _accountGroupsHostPanel;
                }
        
                private void OnDashboardRootSizeChanged(object sender, SizeChangedEventArgs e)
                {
                    ApplyDashboardResponsiveLayout(e.NewSize.Width);
                }
        
                private void ApplyDashboardResponsiveLayout(double width)
                {
                    if (_dashboardRootGrid == null)
                        return;
                    if (_isApplyingDashboardResponsiveLayout)
                        return;
        
                    try
                    {
                        _isApplyingDashboardResponsiveLayout = true;
                        double usableWidth = Math.Max(320, width);
                        const double hysteresis = 24.0;
                        bool narrow = ResolveBelowBreakpoint(usableWidth, _dashboardNarrowLayout, 640, hysteresis);
                        if (_dashboardNarrowLayout.HasValue && _dashboardNarrowLayout.Value == narrow)
                            return;

                        _dashboardNarrowLayout = narrow;
                        _dashboardRootGrid.Margin = narrow ? new Thickness(10) : new Thickness(20);

                        if (_dashboardGroupsSection != null)
                            _dashboardGroupsSection.Margin = new Thickness(0);
                    }
                    finally
                    {
                        _isApplyingDashboardResponsiveLayout = false;
                    }
                }
        
                private static void ConfigureDataGridScrolling(DataGrid grid)
                {
                    if (grid == null)
                        return;
        
                    grid.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
                    grid.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
                    grid.SetValue(ScrollViewer.CanContentScrollProperty, false);
                    grid.SetValue(VirtualizingPanel.IsVirtualizingProperty, true);
                    grid.SetValue(VirtualizingPanel.VirtualizationModeProperty, VirtualizationMode.Recycling);
                }

    }
}

