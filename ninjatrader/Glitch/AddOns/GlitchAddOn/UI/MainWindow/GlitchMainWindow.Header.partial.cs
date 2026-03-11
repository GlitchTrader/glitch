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
        private UIElement CreateHeaderBarImpl()
        {
            const double headerGap = 8.0;
            const double actionButtonMinHeight = 46.0;
            const double actionButtonMaxHeight = 52.0;

            _headerRootGrid = new Grid { Margin = new Thickness(20, 20, 20, 12) };
            _headerRootGrid.SizeChanged += OnHeaderRootSizeChanged;

            _headerMetricHostGrid = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Top
            };
            _headerMetricBoxesPanel = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Top
            };
            _headerMetricBoxesPanel.Children.Add(CreateHeaderMetricDataBox(_headerRootGrid, "header.metric.daily_pnl", "Daily PnL", out _totalPnlValueText, new Thickness(0)));
            _headerMetricBoxesPanel.Children.Add(CreateHeaderMetricDataBox(_headerRootGrid, "header.metric.global_risk", "Global Risk", out _globalHeadroomValueText, new Thickness(0)));
            _headerMetricBoxesPanel.Children.Add(CreateHeaderMetricDataBox(_headerRootGrid, "header.metric.pa_pnl", "PA PnL", out _paPnlValueText, new Thickness(0)));
            _headerMetricBoxesPanel.Children.Add(CreateHeaderMetricDataBox(_headerRootGrid, "header.metric.pa_risk", "PA Risk", out _paHeadroomValueText, new Thickness(0)));
            _headerMetricBoxesPanel.Children.Add(CreateHeaderMetricDataBox(_headerRootGrid, "header.metric.eval_pnl", "Eval PnL", out _evalPnlValueText, new Thickness(0)));
            _headerMetricBoxesPanel.Children.Add(CreateHeaderMetricDataBox(_headerRootGrid, "header.metric.eval_risk", "Eval Risk", out _evalHeadroomValueText, new Thickness(0)));
            _headerMetricBoxesPanel.Children.Add(CreateHeaderMetricDataBox(_headerRootGrid, "header.metric.warnings", "Warnings", out _warningCountValueText, new Thickness(0)));
            _headerMetricHostGrid.Children.Add(_headerMetricBoxesPanel);
            Grid.SetRow(_headerMetricHostGrid, 0);
            Grid.SetColumn(_headerMetricHostGrid, 0);
            _headerRootGrid.Children.Add(_headerMetricHostGrid);
            UpdateWarningCountUi();

            _headerActionsGrid = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top
            };
            _headerActionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _headerActionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _replicateButton = new Button
            {
                MinWidth = 132,
                MinHeight = actionButtonMinHeight,
                MaxHeight = actionButtonMaxHeight,
                Margin = new Thickness(0, 0, headerGap, 0),
                Style = CreateReplicateButtonStyle(_headerRootGrid)
            };
            _replicateButton.Click += OnReplicateButtonClick;
            UpdateReplicateButtonState();
            Grid.SetColumn(_replicateButton, 0);
            _headerActionsGrid.Children.Add(_replicateButton);

            _flattenAllButton = new Button
            {
                Content = L("header.button.flatten_all", "Flatten All"),
                MinWidth = 132,
                MinHeight = actionButtonMinHeight,
                MaxHeight = actionButtonMaxHeight,
                Style = CreateFlattenButtonStyle(_headerRootGrid)
            };
            _flattenAllButton.Click += OnFlattenAllButtonClick;
            Grid.SetColumn(_flattenAllButton, 1);
            _headerActionsGrid.Children.Add(_flattenAllButton);

            Grid.SetRow(_headerActionsGrid, 0);
            Grid.SetColumn(_headerActionsGrid, 1);
            _headerRootGrid.Children.Add(_headerActionsGrid);

            _headerNewsLockoutBanner = new Border
            {
                BorderBrush = OrangeAccentBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 8, 0, 0),
                Visibility = Visibility.Collapsed,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Top
            };
            ApplySkinResource(_headerNewsLockoutBanner, Border.BackgroundProperty, "BackgroundMainWindow", "GridEntireBackground", "BackgroundTextInput");

            _headerNewsLockoutText = new TextBlock
            {
                Text = L("header.banner.news_event_in_progress", "News Event in Progress"),
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Foreground = OrangeAccentBrush
            };
            _headerNewsLockoutBanner.Child = _headerNewsLockoutText;
            _headerRootGrid.Children.Add(_headerNewsLockoutBanner);

            _headerRootGrid.Loaded += (sender, args) =>
            {
                _headerRootGrid.Dispatcher.BeginInvoke(
                    new Action(() =>
                    {
                        ApplyHeaderResponsiveLayout(_headerRootGrid.ActualWidth);
                        UpdateHeaderMetricItemWidth(_headerMetricBoxesPanel, _headerMetricHostGrid.ActualWidth);
                    }),
                    DispatcherPriority.Loaded);
            };

            ApplyHeaderResponsiveLayout(Width > 0 ? Width : 1200);
            UpdateHeaderMetricItemWidth(_headerMetricBoxesPanel, _headerMetricHostGrid.ActualWidth);
            return _headerRootGrid;
        }


        private void OnHeaderRootSizeChanged(object sender, SizeChangedEventArgs e)
        {
            ApplyHeaderResponsiveLayout(e.NewSize.Width);
            UpdateHeaderMetricItemWidth(_headerMetricBoxesPanel, _headerMetricHostGrid?.ActualWidth ?? 0);
        }
        
        private void ApplyHeaderResponsiveLayout(double width)
        {
            if (_headerRootGrid == null || _headerMetricHostGrid == null || _headerActionsGrid == null)
                return;
            if (_isApplyingHeaderResponsiveLayout)
                return;

            try
            {
                _isApplyingHeaderResponsiveLayout = true;

                double usableWidth = Math.Max(320, width);
                const double hysteresis = 24.0;
                int mode = ResolveResponsiveThreeBandMode(usableWidth, _headerResponsiveMode, 640, 980, hysteresis);
                if (mode == _headerResponsiveMode)
                    return;

                _headerResponsiveMode = mode;
                _headerRootGrid.ColumnDefinitions.Clear();
                _headerRootGrid.RowDefinitions.Clear();

                if (mode == 0)
                {
                    _headerRootGrid.Margin = new Thickness(20, 20, 20, 12);
                    _headerRootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    _headerRootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    _headerRootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    _headerRootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    Grid.SetRow(_headerMetricHostGrid, 0);
                    Grid.SetColumn(_headerMetricHostGrid, 0);
                    Grid.SetRow(_headerActionsGrid, 0);
                    Grid.SetColumn(_headerActionsGrid, 1);
                    if (_headerNewsLockoutBanner != null)
                    {
                        Grid.SetRow(_headerNewsLockoutBanner, 1);
                        Grid.SetColumn(_headerNewsLockoutBanner, 0);
                        Grid.SetColumnSpan(_headerNewsLockoutBanner, 2);
                    }

                    _headerActionsGrid.HorizontalAlignment = HorizontalAlignment.Right;
                    _headerActionsGrid.Margin = new Thickness(8, 0, 0, 0);
                    ConfigureHeaderActionColumns(fillWidth: false);
                    return;
                }

                _headerRootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                _headerRootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                _headerRootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                _headerRootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                Grid.SetRow(_headerMetricHostGrid, 0);
                Grid.SetColumn(_headerMetricHostGrid, 0);
                Grid.SetRow(_headerActionsGrid, 1);
                Grid.SetColumn(_headerActionsGrid, 0);
                if (_headerNewsLockoutBanner != null)
                {
                    Grid.SetRow(_headerNewsLockoutBanner, 2);
                    Grid.SetColumn(_headerNewsLockoutBanner, 0);
                    Grid.SetColumnSpan(_headerNewsLockoutBanner, 1);
                }

                if (mode == 1)
                {
                    _headerRootGrid.Margin = new Thickness(16, 16, 16, 10);
                    _headerActionsGrid.HorizontalAlignment = HorizontalAlignment.Right;
                    _headerActionsGrid.Margin = new Thickness(0, 8, 0, 0);
                    ConfigureHeaderActionColumns(fillWidth: false);
                    return;
                }

                _headerRootGrid.Margin = new Thickness(12, 12, 12, 8);
                _headerActionsGrid.HorizontalAlignment = HorizontalAlignment.Stretch;
                _headerActionsGrid.Margin = new Thickness(0, 8, 0, 0);
                ConfigureHeaderActionColumns(fillWidth: true);
            }
            finally
            {
                _isApplyingHeaderResponsiveLayout = false;
            }
        }
        
                private void ConfigureHeaderActionColumns(bool fillWidth)
                {
                    if (_headerActionsGrid == null || _replicateButton == null || _flattenAllButton == null)
                        return;
        
                    _headerActionsGrid.ColumnDefinitions.Clear();
                    _headerActionsGrid.ColumnDefinitions.Add(new ColumnDefinition
                    {
                        Width = fillWidth ? new GridLength(1, GridUnitType.Star) : GridLength.Auto
                    });
                    _headerActionsGrid.ColumnDefinitions.Add(new ColumnDefinition
                    {
                        Width = fillWidth ? new GridLength(1, GridUnitType.Star) : GridLength.Auto
                    });
        
                    Grid.SetColumn(_replicateButton, 0);
                    Grid.SetColumn(_flattenAllButton, 1);
        
                    _replicateButton.Margin = new Thickness(0, 0, fillWidth ? 6 : 8, 0);
                    _replicateButton.MinWidth = fillWidth ? 108 : 132;
                    _replicateButton.HorizontalAlignment = fillWidth ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        
                    _flattenAllButton.MinWidth = fillWidth ? 108 : 132;
                    _flattenAllButton.HorizontalAlignment = fillWidth ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
                }

                private void UpdateGlobalNewsLockoutBanner(bool isActive, string lockoutText)
                {
                    if (_headerNewsLockoutBanner == null || _headerNewsLockoutText == null)
                        return;

                    CacheGlobalNewsLockoutState(isActive, lockoutText);
                    _headerNewsLockoutBanner.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
                    _headerNewsLockoutText.Text = BuildLocalizedNewsLockoutBannerText(lockoutText);
                }
         
                private static void UpdateHeaderMetricItemWidth(WrapPanel panel, double availableWidth)
                {
                    if (panel == null)
                        return;
        
                    int itemCount = panel.Children.Count;
                    if (itemCount <= 0 || availableWidth <= 0)
                    {
                        panel.ItemWidth = double.NaN;
                        return;
                    }
        
                    const double minWidth = 90.0;
                    const double gap = 8.0;
                    int columns = (int)Math.Floor(availableWidth / (minWidth + gap));
                    columns = Math.Max(1, Math.Min(columns, itemCount));
        
                    double computedWidth = (availableWidth - (gap * columns)) / columns;
                    if (computedWidth < minWidth)
                        computedWidth = minWidth;
        
                    panel.ItemWidth = computedWidth;
        
                    int totalRows = (int)Math.Ceiling(itemCount / (double)columns);
                    for (int i = 0; i < itemCount; i++)
                    {
                        if (!(panel.Children[i] is FrameworkElement item))
                            continue;
        
                        int rowIndex = i / columns;
                        bool isLastRow = rowIndex == totalRows - 1;
        
                        item.Margin = new Thickness(
                            0,
                            0,
                            gap,
                            isLastRow ? 0 : gap);
                    }
                }
        
                private Border CreateHeaderMetricDataBox(FrameworkElement context, string titleKey, string titleFallback, out TextBlock valueTextBlock, Thickness margin)
                {
                    var dataBox = new Border
                    {
                        BorderThickness = new Thickness(1),
                        Padding = new Thickness(6, 4, 6, 4),
                        Margin = margin,
                        MinWidth = 90
                    };
                    ApplySkinResource(dataBox, Border.BackgroundProperty,
                        "BackgroundMainWindow", "GridEntireBackground", "BackgroundTextInput");
                    ApplySkinResource(dataBox, Border.BorderBrushProperty,
                        "BorderThinBrush", "TabControlBorderBrush", "BorderMainWindowBrush");
        
                    var stack = new StackPanel();
                    var titleText = new TextBlock
                    {
                        Text = titleFallback,
                        FontSize = 11,
                        FontWeight = FontWeights.Bold,
                        Margin = new Thickness(0, 0, 0, 3)
                    };
                    ApplySkinResource(titleText, TextBlock.ForegroundProperty, "FontHeaderLevel4Brush", "FontControlBrush", "FontTableBrush");
                    BindLocalizedText(titleText, titleKey, titleFallback);
                    stack.Children.Add(titleText);
        
                    valueTextBlock = new TextBlock
                    {
                        Text = "-",
                        FontSize = 18,
                        FontWeight = FontWeights.Bold
                    };
                    ApplySkinResource(valueTextBlock, TextBlock.ForegroundProperty, "FontControlBrush", "FontTableBrush", "GridRowForeground");
                    stack.Children.Add(valueTextBlock);
                    dataBox.Child = stack;
        
                    return dataBox;
                }

    }
}


