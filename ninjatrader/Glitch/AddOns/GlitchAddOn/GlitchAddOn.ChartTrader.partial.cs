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
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Glitch.Services;

namespace NinjaTrader.NinjaScript.AddOns
{
    public partial class GlitchAddOn
    {
        private const double ChartTraderWidgetTopOffset = 210d;
        private const string ChartTraderWidgetTag = "GLITCH_CHART_TRADER_WIDGET";
        private static readonly SolidColorBrush ChartTraderTealBrush = new SolidColorBrush(Color.FromRgb(26, 188, 156));
        private static readonly SolidColorBrush ChartTraderOrangeBrush = new SolidColorBrush(Color.FromRgb(255, 66, 0));
        private static readonly CultureInfo UsdCulture = CultureInfo.GetCultureInfo("en-US");
        private static readonly IEqualityComparer<Window> ChartWindowReferenceComparer = new ChartWindowComparer();
        private Dictionary<Window, ChartTraderWidgetHost> _chartTraderHosts;
        private readonly object _chartTraderHostsLock = new object();

        private sealed class ChartWindowComparer : IEqualityComparer<Window>
        {
            public bool Equals(Window x, Window y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(Window obj)
            {
                if (obj == null)
                    return 0;

                return RuntimeHelpers.GetHashCode(obj);
            }
        }

        private sealed class ChartTraderWidgetHost
        {
            public Window Window { get; set; }
            public FrameworkElement ChartTraderRoot { get; set; }
            public FrameworkElement WidgetRoot { get; set; }
            public Grid InsertedIntoGrid { get; set; }
            public int InsertedGridRowIndex { get; set; } = -1;
            public Panel InsertedIntoPanel { get; set; }
            public ComboBox AccountCombo { get; set; }
            public Button ReplicateButton { get; set; }
            public TextBlock GroupPnlValueText { get; set; }
            public TextBlock FollowersValueText { get; set; }
            public RoutedEventHandler WindowLoadedHandler { get; set; }
            public EventHandler WindowActivatedHandler { get; set; }
            public SelectionChangedEventHandler AccountSelectionChangedHandler { get; set; }
            public EventHandler BridgeStateChangedHandler { get; set; }
        }

        static GlitchAddOn()
        {
            ChartTraderTealBrush.Freeze();
            ChartTraderOrangeBrush.Freeze();
        }

        private void TryAttachChartTraderWidget(Window window)
        {
            if (!ReferenceEquals(_activeInstance, this))
                return;

            if (!IsChartWindow(window) || window == null)
                return;

            ChartTraderWidgetHost host = EnsureChartTraderHost(window);
            if (host == null)
                return;

            AttemptAttachChartTraderWidget(host);
        }

        private ChartTraderWidgetHost EnsureChartTraderHost(Window window)
        {
            if (window == null)
                return null;

            lock (_chartTraderHostsLock)
            {
                if (_chartTraderHosts == null)
                    _chartTraderHosts = new Dictionary<Window, ChartTraderWidgetHost>(ChartWindowReferenceComparer);

                if (_chartTraderHosts.TryGetValue(window, out ChartTraderWidgetHost existing) && existing != null)
                    return existing;

                var host = new ChartTraderWidgetHost { Window = window };
                host.WindowLoadedHandler = (sender, args) => AttemptAttachChartTraderWidget(host);
                host.WindowActivatedHandler = (sender, args) => AttemptAttachChartTraderWidget(host);
                window.Loaded += host.WindowLoadedHandler;
                window.Activated += host.WindowActivatedHandler;
                _chartTraderHosts[window] = host;
                return host;
            }
        }

        private void AttemptAttachChartTraderWidget(ChartTraderWidgetHost host)
        {
            if (!ReferenceEquals(_activeInstance, this))
                return;

            if (host?.Window == null)
                return;

            if (host.WidgetRoot != null && IsAttachedToVisualTree(host.WidgetRoot))
            {
                RequestChartTraderWidgetHostUpdate(host);
                return;
            }

            FrameworkElement chartTraderRoot = FindChartTraderRoot(host.Window);
            if (chartTraderRoot == null)
                return;

            RemoveStaleChartTraderWidgets(chartTraderRoot);
            host.ChartTraderRoot = chartTraderRoot;
            host.WidgetRoot = CreateChartTraderWidget(chartTraderRoot, host);
            if (!TryInsertChartTraderWidget(chartTraderRoot, host))
            {
                host.WidgetRoot = null;
                return;
            }

            host.AccountCombo = FindChartTraderAccountCombo(chartTraderRoot);
            if (host.AccountCombo != null)
            {
                host.AccountSelectionChangedHandler = (sender, args) => RequestChartTraderWidgetHostUpdate(host);
                host.AccountCombo.SelectionChanged += host.AccountSelectionChangedHandler;
            }

            host.BridgeStateChangedHandler = (sender, args) => RequestChartTraderWidgetHostUpdate(host);
            GlitchShellBridge.StateChanged += host.BridgeStateChangedHandler;
            RequestChartTraderWidgetHostUpdate(host);
        }

        private void DetachChartTraderWidget(Window window)
        {
            ChartTraderWidgetHost host = null;
            lock (_chartTraderHostsLock)
            {
                if (_chartTraderHosts == null)
                    return;

                if (window == null || !_chartTraderHosts.TryGetValue(window, out host) || host == null)
                    return;

                _chartTraderHosts.Remove(window);
            }

            if (host.WindowLoadedHandler != null)
                window.Loaded -= host.WindowLoadedHandler;
            if (host.WindowActivatedHandler != null)
                window.Activated -= host.WindowActivatedHandler;
            if (host.AccountCombo != null && host.AccountSelectionChangedHandler != null)
                host.AccountCombo.SelectionChanged -= host.AccountSelectionChangedHandler;
            if (host.BridgeStateChangedHandler != null)
                GlitchShellBridge.StateChanged -= host.BridgeStateChangedHandler;

            RemoveChartTraderWidget(host);
        }

        private void DetachAllChartTraderHosts()
        {
            List<Window> windows;
            lock (_chartTraderHostsLock)
            {
                if (_chartTraderHosts == null || _chartTraderHosts.Count == 0)
                    return;

                windows = _chartTraderHosts.Keys.ToList();
            }

            foreach (Window window in windows)
                DetachChartTraderWidget(window);
        }

        private void RemoveChartTraderWidget(ChartTraderWidgetHost host)
        {
            if (host?.WidgetRoot == null)
                return;

            if (host.InsertedIntoPanel != null)
                host.InsertedIntoPanel.Children.Remove(host.WidgetRoot);

            if (host.InsertedIntoGrid != null)
            {
                host.InsertedIntoGrid.Children.Remove(host.WidgetRoot);
                if (host.InsertedGridRowIndex >= 0 &&
                    host.InsertedGridRowIndex < host.InsertedIntoGrid.RowDefinitions.Count)
                {
                    host.InsertedIntoGrid.RowDefinitions.RemoveAt(host.InsertedGridRowIndex);
                }
            }

            host.InsertedIntoPanel = null;
            host.InsertedIntoGrid = null;
            host.InsertedGridRowIndex = -1;
            host.WidgetRoot = null;
        }

        private FrameworkElement CreateChartTraderWidget(FrameworkElement context, ChartTraderWidgetHost host)
        {
            var shell = new Border
            {
                Tag = ChartTraderWidgetTag,
                Margin = new Thickness(0, 8 + ChartTraderWidgetTopOffset, 0, 12),
                Padding = new Thickness(8, 8, 8, 7),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Top,
                BorderThickness = new Thickness(1)
            };
            ApplySkinResource(shell, Border.BackgroundProperty, "BackgroundMainWindow", "GridEntireBackground", "BackgroundTextInput");
            ApplySkinResource(shell, Border.BorderBrushProperty, "BorderThinBrush", "TabControlBorderBrush", "GridHeaderHighlight");

            var layout = new Grid { Margin = new Thickness(0) };
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var buttonGrid = new Grid();
            buttonGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            buttonGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var replicateButton = new Button
            {
                Margin = new Thickness(0, 0, 2, 0),
                Padding = new Thickness(0, 3, 0, 3),
                MinHeight = 24,
                MinWidth = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Style = CreateChartTraderReplicateButtonStyle(context)
            };
            replicateButton.Click += (sender, args) =>
            {
                if (!GlitchShellBridge.ToggleReplication())
                {
                    ShowMainWindowFromExternalSurface();
                    GlitchShellBridge.ToggleReplication();
                }
            };
            host.ReplicateButton = replicateButton;
            Grid.SetColumn(replicateButton, 0);
            buttonGrid.Children.Add(replicateButton);

            var flattenAllButton = new Button
            {
                Content = Translate("header.button.flatten_all", "Flatten All"),
                Margin = new Thickness(2, 0, 0, 0),
                Padding = new Thickness(0, 3, 0, 3),
                MinHeight = 24,
                MinWidth = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Style = CreateChartTraderFlattenButtonStyle(context)
            };
            flattenAllButton.Click += (sender, args) =>
            {
                if (!GlitchShellBridge.FlattenAll())
                {
                    ShowMainWindowFromExternalSurface();
                    GlitchShellBridge.FlattenAll();
                }
            };
            Grid.SetColumn(flattenAllButton, 1);
            buttonGrid.Children.Add(flattenAllButton);

            Grid.SetRow(buttonGrid, 0);
            layout.Children.Add(buttonGrid);

            var metricsGrid = new Grid { Margin = new Thickness(0, 7, 0, 0) };
            metricsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            metricsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            metricsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            metricsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var followersLabel = CreateChartTraderMetricLabel(Translate("charttrader.metric.followers_compact", "Followers:"));
            Grid.SetRow(followersLabel, 0);
            Grid.SetColumn(followersLabel, 0);
            metricsGrid.Children.Add(followersLabel);

            var followersValue = CreateChartTraderMetricValue("0");
            followersValue.HorizontalAlignment = HorizontalAlignment.Right;
            followersValue.FontSize = 12;
            host.FollowersValueText = followersValue;
            Grid.SetRow(followersValue, 0);
            Grid.SetColumn(followersValue, 1);
            metricsGrid.Children.Add(followersValue);

            var pnlLabel = CreateChartTraderMetricLabel(Translate("charttrader.metric.pnl_compact", "PnL:"));
            Grid.SetRow(pnlLabel, 1);
            Grid.SetColumn(pnlLabel, 0);
            metricsGrid.Children.Add(pnlLabel);

            var pnlValue = CreateChartTraderMetricValue("-");
            pnlValue.HorizontalAlignment = HorizontalAlignment.Right;
            pnlValue.FontSize = 12;
            host.GroupPnlValueText = pnlValue;
            Grid.SetRow(pnlValue, 1);
            Grid.SetColumn(pnlValue, 1);
            metricsGrid.Children.Add(pnlValue);

            Grid.SetRow(metricsGrid, 1);
            layout.Children.Add(metricsGrid);

            shell.Child = layout;
            return shell;
        }

        private static TextBlock CreateChartTraderMetricLabel(string text)
        {
            var textBlock = new TextBlock
            {
                Text = text,
                Margin = new Thickness(0, 1, 0, 1),
                FontSize = 10,
                Opacity = 0.88
            };
            ApplySkinResource(textBlock, TextBlock.ForegroundProperty, "FontControlBrush", "FontTableBrush");
            return textBlock;
        }

        private static TextBlock CreateChartTraderMetricValue(string text)
        {
            var textBlock = new TextBlock
            {
                Text = text,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold
            };
            ApplySkinResource(textBlock, TextBlock.ForegroundProperty, "FontControlBrush", "FontTableBrush");
            return textBlock;
        }

        private void RequestChartTraderWidgetHostUpdate(ChartTraderWidgetHost host)
        {
            if (!ReferenceEquals(_activeInstance, this))
                return;

            if (host?.Window == null)
                return;

            if (host.Window.Dispatcher.CheckAccess())
                UpdateChartTraderWidgetHost(host);
            else
                host.Window.Dispatcher.BeginInvoke(new Action(() => UpdateChartTraderWidgetHost(host)));
        }

        private void UpdateChartTraderWidgetHost(ChartTraderWidgetHost host)
        {
            if (host?.WidgetRoot == null ||
                host.ReplicateButton == null ||
                host.GroupPnlValueText == null ||
                host.FollowersValueText == null)
                return;

            var snapshot = GlitchShellBridge.GetSnapshot();
            host.ReplicateButton.Style = CreateChartTraderReplicateButtonStyle(host.WidgetRoot);
            host.ReplicateButton.Tag = snapshot.IsReplicating ? "Running" : "Stopped";

            string selectedAccount = ResolveChartTraderSelectedAccount(host.AccountCombo);
            if (!string.IsNullOrWhiteSpace(selectedAccount) &&
                snapshot.GroupsByMaster != null &&
                snapshot.GroupsByMaster.TryGetValue(selectedAccount.Trim(), out GlitchGroupRuntimeSummary summary) &&
                summary != null)
            {
                host.GroupPnlValueText.Text = FormatCurrency(summary.GroupPnlRaw);
                host.GroupPnlValueText.Foreground = ResolvePnlBrush(summary.GroupPnlRaw);
                host.FollowersValueText.Text = summary.EnabledFollowerCount.ToString("N0", CultureInfo.CurrentCulture);
            }
            else
            {
                host.GroupPnlValueText.Text = "-";
                host.GroupPnlValueText.Foreground = ResolveNeutralTextBrush();
                host.FollowersValueText.Text = "0";
            }
        }

        private bool TryInsertChartTraderWidget(FrameworkElement chartTraderRoot, ChartTraderWidgetHost host)
        {
            if (chartTraderRoot is Grid rootGrid)
            {
                int rowIndex = rootGrid.RowDefinitions.Count;
                rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetRow(host.WidgetRoot, rowIndex);
                Grid.SetColumn(host.WidgetRoot, 0);
                Grid.SetColumnSpan(host.WidgetRoot, Math.Max(1, rootGrid.ColumnDefinitions.Count));
                rootGrid.Children.Add(host.WidgetRoot);
                host.InsertedIntoGrid = rootGrid;
                host.InsertedGridRowIndex = rowIndex;
                return true;
            }

            if (chartTraderRoot is Panel rootPanel)
            {
                rootPanel.Children.Add(host.WidgetRoot);
                host.InsertedIntoPanel = rootPanel;
                return true;
            }

            if (chartTraderRoot is ContentControl contentControl && contentControl.Content is Panel contentPanel)
            {
                contentPanel.Children.Add(host.WidgetRoot);
                host.InsertedIntoPanel = contentPanel;
                return true;
            }

            return false;
        }

        private static bool IsChartWindow(Window window)
        {
            string fullName = window?.GetType().FullName ?? string.Empty;
            string typeName = window?.GetType().Name ?? string.Empty;
            return fullName.IndexOf("NinjaTrader.Gui.Chart", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   typeName.IndexOf("Chart", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static FrameworkElement FindChartTraderRoot(DependencyObject root)
        {
            return EnumerateVisualTree(root)
                .OfType<FrameworkElement>()
                .FirstOrDefault(element =>
                {
                    string name = element.Name ?? string.Empty;
                    string typeName = element.GetType().Name ?? string.Empty;
                    return (name.IndexOf("ChartTrader", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            typeName.IndexOf("ChartTrader", StringComparison.OrdinalIgnoreCase) >= 0) &&
                           (element is Grid || element is Panel || element is ContentControl);
                });
        }

        private static ComboBox FindChartTraderAccountCombo(DependencyObject root)
        {
            ComboBox byName = EnumerateVisualTree(root)
                .OfType<ComboBox>()
                .FirstOrDefault(combo => (combo.Name ?? string.Empty).IndexOf("Account", StringComparison.OrdinalIgnoreCase) >= 0);
            if (byName != null)
                return byName;

            return EnumerateVisualTree(root)
                .OfType<ComboBox>()
                .FirstOrDefault(combo =>
                    !string.IsNullOrWhiteSpace(ResolveAccountName(combo.SelectedItem)) ||
                    !string.IsNullOrWhiteSpace(ResolveAccountName(combo.SelectedValue)));
        }

        private static IEnumerable<DependencyObject> EnumerateVisualTree(DependencyObject root)
        {
            if (root == null)
                yield break;

            var queue = new Queue<DependencyObject>();
            queue.Enqueue(root);

            while (queue.Count > 0)
            {
                DependencyObject current = queue.Dequeue();
                yield return current;

                int childCount = VisualTreeHelper.GetChildrenCount(current);
                for (int i = 0; i < childCount; i++)
                {
                    DependencyObject child = VisualTreeHelper.GetChild(current, i);
                    if (child != null)
                        queue.Enqueue(child);
                }
            }
        }

        private static bool IsAttachedToVisualTree(FrameworkElement element)
        {
            if (element == null)
                return false;

            DependencyObject current = element;
            while (current != null)
            {
                if (current is Window)
                    return true;

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }

        private static void RemoveStaleChartTraderWidgets(FrameworkElement chartTraderRoot)
        {
            if (chartTraderRoot == null)
                return;

            List<FrameworkElement> staleWidgets = EnumerateVisualTree(chartTraderRoot)
                .OfType<FrameworkElement>()
                .Where(element =>
                    !ReferenceEquals(element, chartTraderRoot) &&
                    string.Equals(element.Tag as string, ChartTraderWidgetTag, StringComparison.Ordinal))
                .ToList();

            foreach (FrameworkElement staleWidget in staleWidgets)
            {
                if (staleWidget.Parent is Panel parentPanel)
                {
                    parentPanel.Children.Remove(staleWidget);
                    continue;
                }

                if (staleWidget.Parent is Grid parentGrid)
                {
                    int widgetRow = Grid.GetRow(staleWidget);
                    parentGrid.Children.Remove(staleWidget);

                    if (widgetRow >= 0 &&
                        widgetRow == parentGrid.RowDefinitions.Count - 1 &&
                        !parentGrid.Children.OfType<UIElement>().Any(child => Grid.GetRow(child) == widgetRow))
                    {
                        parentGrid.RowDefinitions.RemoveAt(widgetRow);
                    }
                }
            }
        }

        private static string ResolveChartTraderSelectedAccount(ComboBox accountCombo)
        {
            if (accountCombo == null)
                return null;

            return ResolveAccountName(accountCombo.SelectedItem) ??
                   ResolveAccountName(accountCombo.SelectedValue);
        }

        private static string ResolveAccountName(object value)
        {
            if (value == null)
                return null;

            if (value is string text && !string.IsNullOrWhiteSpace(text))
                return text.Trim();

            Type valueType = value.GetType();
            foreach (string propertyName in new[] { "Name", "AccountName", "DisplayName", "Text" })
            {
                PropertyInfo property = valueType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                if (property == null || property.PropertyType != typeof(string))
                    continue;

                string resolved = property.GetValue(value, null) as string;
                if (!string.IsNullOrWhiteSpace(resolved))
                    return resolved.Trim();
            }

            return null;
        }

        private static string Translate(string key, string fallback)
        {
            try
            {
                var service = new GlitchLocalizationService(
                    GlitchLocalizationService.GetDefaultLocalizationPath(),
                    GlitchLocalizationService.GetDefaultSettingsPath());
                return service.Translate(key, fallback);
            }
            catch
            {
                return fallback;
            }
        }

        private static string FormatCurrency(double value)
        {
            return value.ToString("C0", UsdCulture);
        }

        private static Brush ResolvePnlBrush(double pnl)
        {
            if (pnl > 0)
                return ChartTraderTealBrush;
            if (pnl < 0)
                return ChartTraderOrangeBrush;
            return ResolveNeutralTextBrush();
        }

        private static Brush ResolveNeutralTextBrush()
        {
            if (Application.Current != null)
            {
                if (Application.Current.TryFindResource("FontControlBrush") is Brush controlBrush)
                    return controlBrush;
                if (Application.Current.TryFindResource("FontTableBrush") is Brush tableBrush)
                    return tableBrush;
            }

            return Brushes.White;
        }

        private static void ApplySkinResource(FrameworkElement element, DependencyProperty property, params string[] keys)
        {
            if (element == null || property == null || keys == null)
                return;

            foreach (string key in keys.Where(k => !string.IsNullOrWhiteSpace(k)))
            {
                if (Application.Current == null)
                    break;

                if (Application.Current.TryFindResource(key) != null)
                {
                    element.SetResourceReference(property, key);
                    return;
                }
            }
        }

        private static Style FindSkinStyle(Type controlType)
        {
            if (Application.Current == null || controlType == null)
                return null;

            object resource = Application.Current.TryFindResource(controlType);
            return resource as Style;
        }

        private static double? FindSkinDouble(params string[] keys)
        {
            if (Application.Current == null || keys == null)
                return null;

            foreach (string key in keys.Where(k => !string.IsNullOrWhiteSpace(k)))
            {
                object resource = Application.Current.TryFindResource(key);
                if (resource is double doubleValue)
                    return doubleValue;
            }

            return null;
        }

        private static Style CreateChartTraderReplicateButtonStyle(FrameworkElement context)
        {
            var baseStyle = FindSkinStyle(typeof(Button));
            var style = new Style(typeof(Button), baseStyle);

            ApplySkinSetter(style, Control.BackgroundProperty, context, "BackgroundTextInput", "BackgroundMainWindow", "GridEntireBackground");
            ApplySkinSetter(style, Control.ForegroundProperty, context, "FontControlBrush", "FontTableBrush");
            ApplySkinSetter(style, Control.BorderBrushProperty, context, "BorderThinBrush", "TabControlBorderBrush");

            double? sharedFontSize = FindSkinDouble("FontTableHeight", "FontControlHeight", "FontHeaderLevel4Height");
            if (sharedFontSize.HasValue)
                style.Setters.Add(new Setter(Control.FontSizeProperty, sharedFontSize.Value));

            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Medium));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(Control.CursorProperty, System.Windows.Input.Cursors.Hand));
            style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));

            var stoppedTrigger = new Trigger { Property = FrameworkElement.TagProperty, Value = "Stopped" };
            stoppedTrigger.Setters.Add(new Setter(ContentControl.ContentProperty, Translate("header.button.replicate", "Replicate")));
            style.Triggers.Add(stoppedTrigger);

            var runningTrigger = new Trigger { Property = FrameworkElement.TagProperty, Value = "Running" };
            runningTrigger.Setters.Add(new Setter(ContentControl.ContentProperty, Translate("header.button.replicating", "Replicating")));
            runningTrigger.Setters.Add(new Setter(Control.BackgroundProperty, ChartTraderTealBrush));
            runningTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, ChartTraderTealBrush));
            runningTrigger.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            style.Triggers.Add(runningTrigger);

            var hoverStopped = new MultiTrigger();
            hoverStopped.Conditions.Add(new System.Windows.Condition(UIElement.IsMouseOverProperty, true));
            hoverStopped.Conditions.Add(new System.Windows.Condition(FrameworkElement.TagProperty, "Stopped"));
            hoverStopped.Setters.Add(new Setter(Control.BackgroundProperty, ChartTraderTealBrush));
            hoverStopped.Setters.Add(new Setter(Control.BorderBrushProperty, ChartTraderTealBrush));
            hoverStopped.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            hoverStopped.Setters.Add(new Setter(ContentControl.ContentProperty, Translate("header.button.start", "Start")));
            style.Triggers.Add(hoverStopped);

            var hoverRunning = new MultiTrigger();
            hoverRunning.Conditions.Add(new System.Windows.Condition(UIElement.IsMouseOverProperty, true));
            hoverRunning.Conditions.Add(new System.Windows.Condition(FrameworkElement.TagProperty, "Running"));
            hoverRunning.Setters.Add(new Setter(Control.BackgroundProperty, ChartTraderOrangeBrush));
            hoverRunning.Setters.Add(new Setter(Control.BorderBrushProperty, ChartTraderOrangeBrush));
            hoverRunning.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            hoverRunning.Setters.Add(new Setter(ContentControl.ContentProperty, Translate("header.button.stop", "Stop")));
            style.Triggers.Add(hoverRunning);

            return style;
        }

        private static Style CreateChartTraderFlattenButtonStyle(FrameworkElement context)
        {
            var baseStyle = FindSkinStyle(typeof(Button));
            var style = new Style(typeof(Button), baseStyle);

            ApplySkinSetter(style, Control.BackgroundProperty, context, "BackgroundTextInput", "BackgroundMainWindow", "GridEntireBackground");
            ApplySkinSetter(style, Control.ForegroundProperty, context, "FontControlBrush", "FontTableBrush");
            ApplySkinSetter(style, Control.BorderBrushProperty, context, "BorderThinBrush", "TabControlBorderBrush");

            double? sharedFontSize = FindSkinDouble("FontTableHeight", "FontControlHeight", "FontHeaderLevel4Height");
            if (sharedFontSize.HasValue)
                style.Setters.Add(new Setter(Control.FontSizeProperty, sharedFontSize.Value));

            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Medium));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(Control.CursorProperty, System.Windows.Input.Cursors.Hand));
            style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, ChartTraderOrangeBrush));
            hoverTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, ChartTraderOrangeBrush));
            hoverTrigger.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            style.Triggers.Add(hoverTrigger);

            return style;
        }

        private static void ApplySkinSetter(Style style, DependencyProperty property, FrameworkElement context, params string[] keys)
        {
            if (style == null || property == null || keys == null)
                return;

            foreach (string key in keys.Where(k => !string.IsNullOrWhiteSpace(k)))
            {
                if (Application.Current == null)
                    break;

                if (Application.Current.TryFindResource(key) != null)
                {
                    style.Setters.Add(new Setter(property, new DynamicResourceExtension(key)));
                    return;
                }
            }
        }
    }
}
