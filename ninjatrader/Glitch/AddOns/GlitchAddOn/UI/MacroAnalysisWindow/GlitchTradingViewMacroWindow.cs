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
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Xml.Linq;
using NinjaTrader.Gui.Tools;

namespace Glitch.UI
{
    internal sealed class GlitchTradingViewMacroWindow : NTWindow
    {
        private bool _isDarkTheme;
        private readonly List<DeferredBrowserTab> _deferredTabs = new List<DeferredBrowserTab>();
        private BrowserHost _tickerBrowser;
        private TabControl _tabControl;

        private sealed class BrowserHost
        {
            public FrameworkElement View { get; set; }
            public Action<string> NavigateToHtml { get; set; }
            public Action Dispose { get; set; }
        }

        private sealed class DeferredBrowserTab
        {
            public TabItem TabItem { get; set; }
            public Func<string> HtmlFactory { get; set; }
            public bool IsLoaded { get; set; }
            public BrowserHost BrowserHost { get; set; }
        }

        public GlitchTradingViewMacroWindow(Window owner)
        {
            _isDarkTheme = ResolveDarkTheme(owner as FrameworkElement);

            Caption = "Nasdaq Macro";
            Title = "Nasdaq Macro";
            Width = 1200;
            Height = 900;
            MinWidth = 360;
            MinHeight = 680;
            WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
            Owner = owner;

            ApplySkinResource(this, Control.BackgroundProperty, "BackgroundMainWindow", "GridEntireBackground", "BackgroundTextInput");
            ApplySkinResource(this, Control.ForegroundProperty, "FontControlBrush", "FontTableBrush", "GridRowForeground");

            Content = CreateLayout();

            Loaded += OnLoaded;
            Closed += OnClosed;
        }

        private UIElement CreateLayout()
        {
            var layout = new Grid();
            layout.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)] = CreateSkinAwareScrollBarStyle(layout);
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(84) });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var tickerHost = CreateBrowserCard(new Thickness(0, 6, 0, 6), out _tickerBrowser);
            Grid.SetRow(tickerHost, 0);
            layout.Children.Add(tickerHost);

            _tabControl = new TabControl();
            Style tabControlStyle = FindSkinStyle(typeof(TabControl));
            if (tabControlStyle != null)
                _tabControl.Style = tabControlStyle;
            ApplySkinResource(_tabControl, Control.BackgroundProperty, "BackgroundTextInput", "GridEntireBackground", "BackgroundMainWindow");
            ApplySkinResource(_tabControl, Control.ForegroundProperty, "FontControlBrush", "FontTableBrush");
            ApplySkinResource(_tabControl, Control.BorderBrushProperty, "BorderThinBrush", "TabControlBorderBrush");
            _tabControl.ItemContainerStyle = CreateSkinAwareTabItemStyle(_tabControl);
            _tabControl.SelectionChanged += OnTabSelectionChanged;

            _tabControl.Items.Add(CreateDeferredBrowserTab("Charts", () => GlitchTradingViewMacroHtmlFactory.BuildChartsPage(_isDarkTheme)));
            _tabControl.Items.Add(CreateDeferredBrowserTab("Technicals", () => GlitchTradingViewMacroHtmlFactory.BuildTechnicalsPage(_isDarkTheme)));
            _tabControl.Items.Add(CreateDeferredBrowserTab("Macro", () => GlitchTradingViewMacroHtmlFactory.BuildMacroPage(_isDarkTheme)));

            Grid.SetRow(_tabControl, 1);
            layout.Children.Add(_tabControl);

            return layout;
        }

        private TabItem CreateDeferredBrowserTab(string header, Func<string> htmlFactory)
        {
            var placeholder = new Border
            {
                Padding = new Thickness(16)
            };
            ApplySkinResource(placeholder, Border.BackgroundProperty, "BackgroundMainWindow", "GridEntireBackground", "BackgroundTextInput");

            var placeholderText = new TextBlock
            {
                Text = "Loading TradingView macro...",
                VerticalAlignment = VerticalAlignment.Center
            };
            ApplySkinResource(placeholderText, TextBlock.ForegroundProperty, "FontControlBrush", "FontTableBrush");
            placeholder.Child = placeholderText;

            var tabItem = new TabItem
            {
                Header = header,
                Content = placeholder
            };

            _deferredTabs.Add(new DeferredBrowserTab
            {
                TabItem = tabItem,
                HtmlFactory = htmlFactory
            });

            return tabItem;
        }

        private Border CreateBrowserCard(Thickness margin, out BrowserHost browserHost)
        {
            var border = new Border
            {
                Margin = margin,
                Padding = new Thickness(0),
                BorderThickness = new Thickness(1)
            };
            ApplySkinResource(border, Border.BackgroundProperty, "BackgroundMainWindow", "GridEntireBackground", "BackgroundTextInput");
            ApplySkinResource(border, Border.BorderBrushProperty, "BorderThinBrush", "TabControlBorderBrush", "BorderMainWindowBrush");

            browserHost = CreateBrowserHost();
            border.Child = browserHost.View;
            return border;
        }

        private BrowserHost CreateBrowserHost()
        {
            BrowserHost host = TryCreateWebView2Host();
            if (host != null)
                return host;

            return CreateFallbackBrowserHost();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Resolve theme after NT skin resources have been applied to this window.
            _isDarkTheme = ResolveDarkTheme(this);

            if (_tickerBrowser != null)
                _tickerBrowser.NavigateToHtml(GlitchTradingViewMacroHtmlFactory.BuildTickerTapePage(_isDarkTheme));

            if (_tabControl?.SelectedItem is TabItem selectedTab)
                EnsureTabLoaded(selectedTab);
        }

        private void OnClosed(object sender, EventArgs e)
        {
            if (_tickerBrowser != null)
            {
                _tickerBrowser.Dispose?.Invoke();
                _tickerBrowser = null;
            }

            if (_tabControl != null)
                _tabControl.SelectionChanged -= OnTabSelectionChanged;

            foreach (DeferredBrowserTab tab in _deferredTabs)
            {
                if (tab.BrowserHost == null)
                    continue;

                tab.BrowserHost.Dispose?.Invoke();
                tab.BrowserHost = null;
                tab.IsLoaded = false;
            }

            _deferredTabs.Clear();
        }

        private void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(e.Source is TabControl))
                return;

            if (_tabControl?.SelectedItem is TabItem selectedTab)
                EnsureTabLoaded(selectedTab);
        }

        private void EnsureTabLoaded(TabItem tabItem)
        {
            DeferredBrowserTab tab = _deferredTabs.Find(entry => ReferenceEquals(entry.TabItem, tabItem));
            if (tab == null || tab.IsLoaded)
                return;

            var card = CreateBrowserCard(new Thickness(0), out BrowserHost browserHost);
            tab.BrowserHost = browserHost;
            tab.TabItem.Content = card;
            tab.IsLoaded = true;

            Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() => browserHost.NavigateToHtml(tab.HtmlFactory())));
        }

        private BrowserHost TryCreateWebView2Host()
        {
            Type webViewType = ResolveWebView2Type();
            if (webViewType == null)
                return null;

            var view = Activator.CreateInstance(webViewType) as FrameworkElement;
            if (view == null)
                return null;

            view.HorizontalAlignment = HorizontalAlignment.Stretch;
            view.VerticalAlignment = VerticalAlignment.Stretch;
            view.Visibility = Visibility.Hidden;

            var statusText = new TextBlock
            {
                Text = "Loading TradingView...",
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(16),
                Opacity = 0.9
            };
            ApplySkinResource(statusText, TextBlock.ForegroundProperty, "FontControlBrush", "FontTableBrush");

            var hostGrid = new Grid();
            hostGrid.Children.Add(view);
            hostGrid.Children.Add(statusText);

            return new BrowserHost
            {
                View = hostGrid,
                NavigateToHtml = html => NavigateWebView2ToHtml(view, html, statusText),
                Dispose = () => DisposeWebView2(view)
            };
        }

        private BrowserHost CreateFallbackBrowserHost()
        {
            var host = new Border
            {
                Padding = new Thickness(18)
            };
            ApplySkinResource(host, Border.BackgroundProperty, "BackgroundMainWindow", "GridEntireBackground", "BackgroundTextInput");

            var text = new TextBlock
            {
                Text = "TradingView requires WebView2. This NinjaTrader environment could not create a modern embedded browser host.",
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            ApplySkinResource(text, TextBlock.ForegroundProperty, "FontControlBrush", "FontTableBrush");
            host.Child = text;

            return new BrowserHost
            {
                View = host,
                NavigateToHtml = html => { },
                Dispose = () => { }
            };
        }

        private static Type ResolveWebView2Type()
        {
            Type resolved = Type.GetType("Microsoft.Web.WebView2.Wpf.WebView2, Microsoft.Web.WebView2.Wpf", false);
            if (resolved != null)
                return resolved;

            if (!IsUnsafeWebViewAssemblyProbeEnabled())
                return null;

            foreach (string candidateDirectory in EnumerateWebView2ProbeDirectories())
            {
                try
                {
                    string wpfAssemblyPath = Path.Combine(candidateDirectory, "Microsoft.Web.WebView2.Wpf.dll");
                    if (!File.Exists(wpfAssemblyPath))
                        continue;

                    Assembly assembly = Assembly.LoadFrom(wpfAssemblyPath);
                    Type type = assembly.GetType("Microsoft.Web.WebView2.Wpf.WebView2", throwOnError: false);
                    if (type != null)
                        return type;
                }
                catch
                {
                }
            }

            return null;
        }

        private static bool IsUnsafeWebViewAssemblyProbeEnabled()
        {
#if DEBUG
            return true;
#else
            try
            {
                string flag = Environment.GetEnvironmentVariable("GLITCH_ALLOW_WEBVIEW2_PROBE_LOAD");
                return string.Equals(flag, "1", StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
#endif
        }

        private static IEnumerable<string> EnumerateWebView2ProbeDirectories()
        {
            var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void YieldIfValid(string path)
            {
                if (string.IsNullOrWhiteSpace(path))
                    return;

                try
                {
                    string normalized = Path.GetFullPath(path);
                    if (!Directory.Exists(normalized))
                        return;

                    if (yielded.Add(normalized))
                        _ = normalized;
                }
                catch
                {
                }
            }

            string appDomainBase = AppDomain.CurrentDomain.BaseDirectory;
            if (!string.IsNullOrWhiteSpace(appDomainBase))
                YieldIfValid(appDomainBase);

            try
            {
                string processDirectory = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName);
                if (!string.IsNullOrWhiteSpace(processDirectory))
                    YieldIfValid(processDirectory);
            }
            catch
            {
            }

            string programFilesPath = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrWhiteSpace(programFilesPath))
                YieldIfValid(Path.Combine(programFilesPath, "NinjaTrader 8", "bin"));

            foreach (string path in yielded)
                yield return path;
        }

        private static async void NavigateWebView2ToHtml(FrameworkElement view, string html, TextBlock statusText)
        {
            if (view == null)
                return;

            try
            {
                if (statusText != null)
                {
                    statusText.Text = "Loading TradingView...";
                    statusText.Visibility = Visibility.Visible;
                }

                MethodInfo ensureMethod = view.GetType()
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .FirstOrDefault(method =>
                    {
                        if (!string.Equals(method.Name, "EnsureCoreWebView2Async", StringComparison.Ordinal))
                            return false;

                        ParameterInfo[] parameters = method.GetParameters();
                        return parameters.Length == 1;
                    });

                if (ensureMethod == null)
                    throw new InvalidOperationException("WebView2 EnsureCoreWebView2Async method was not found.");

                Type environmentType = ensureMethod.GetParameters()[0].ParameterType;
                object environment = await CreateWebView2EnvironmentAsync(environmentType).ConfigureAwait(true);
                if (environment == null)
                    throw new InvalidOperationException("WebView2 environment could not be created.");

                var ensureTask = ensureMethod.Invoke(view, new[] { environment }) as Task;
                if (ensureTask != null)
                    await ensureTask.ConfigureAwait(true);

                MethodInfo navigateMethod = view.GetType().GetMethod(
                    "NavigateToString",
                    BindingFlags.Instance | BindingFlags.Public,
                    null,
                    new[] { typeof(string) },
                    null);

                if (navigateMethod == null)
                    throw new InvalidOperationException("WebView2 NavigateToString method was not found.");

                navigateMethod.Invoke(view, new object[] { html ?? string.Empty });
                view.Visibility = Visibility.Visible;
                if (statusText != null)
                    statusText.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                if (statusText != null)
                {
                    statusText.Text = "TradingView failed to load.\n" + ex.Message;
                    statusText.Visibility = Visibility.Visible;
                }
            }
        }

        private static async Task<object> CreateWebView2EnvironmentAsync(Type environmentType)
        {
            if (environmentType == null)
                return null;

            MethodInfo createAsyncMethod = environmentType
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(method =>
                {
                    if (!string.Equals(method.Name, "CreateAsync", StringComparison.Ordinal))
                        return false;

                    ParameterInfo[] parameters = method.GetParameters();
                    return parameters.Length == 3 &&
                           parameters[0].ParameterType == typeof(string) &&
                           parameters[1].ParameterType == typeof(string);
                });
            if (createAsyncMethod == null)
                return null;

            string userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Glitch",
                "WebView2");
            Directory.CreateDirectory(userDataFolder);

            var task = createAsyncMethod.Invoke(null, new object[] { null, userDataFolder, null }) as Task;
            if (task == null)
                return null;

            await task.ConfigureAwait(true);
            return task.GetType().GetProperty("Result", BindingFlags.Instance | BindingFlags.Public)?.GetValue(task);
        }

        private static void DisposeWebView2(FrameworkElement view)
        {
            if (view == null)
                return;

            try
            {
                MethodInfo stopMethod = view.GetType().GetMethod("Stop", BindingFlags.Instance | BindingFlags.Public);
                stopMethod?.Invoke(view, null);
            }
            catch
            {
            }

            try
            {
                MethodInfo disposeMethod = view.GetType().GetMethod("Dispose", BindingFlags.Instance | BindingFlags.Public);
                disposeMethod?.Invoke(view, null);
            }
            catch
            {
            }
        }

        private static bool ResolveDarkTheme(FrameworkElement context)
        {
            bool? configSkinTheme = TryResolveThemeFromConfig();
            if (configSkinTheme.HasValue)
                return configSkinTheme.Value;

            var backgroundBrush = ResolveSolidBrush(
                FindSkinResource(context, "BackgroundMainWindow"),
                FindSkinResource(context, "GridEntireBackground"),
                FindSkinResource(context, "BackgroundTextInput"),
                Application.Current?.TryFindResource("BackgroundMainWindow"),
                Application.Current?.TryFindResource("GridEntireBackground"),
                Application.Current?.TryFindResource("BackgroundTextInput"),
                context?.GetValue(Control.BackgroundProperty),
                (context as Control)?.Background);

            var foregroundBrush = ResolveSolidBrush(
                FindSkinResource(context, "FontControlBrush"),
                FindSkinResource(context, "FontTableBrush"),
                FindSkinResource(context, "GridRowForeground"),
                FindSkinResource(context, "FontHeaderLevel4Brush"),
                Application.Current?.TryFindResource("FontControlBrush"),
                Application.Current?.TryFindResource("FontTableBrush"),
                Application.Current?.TryFindResource("GridRowForeground"),
                Application.Current?.TryFindResource("FontHeaderLevel4Brush"),
                context?.GetValue(Control.ForegroundProperty),
                (context as Control)?.Foreground);

            double? backgroundLuminance = ComputeLuminance(backgroundBrush);
            double? foregroundLuminance = ComputeLuminance(foregroundBrush);

            if (backgroundLuminance.HasValue && foregroundLuminance.HasValue)
            {
                // Dark skins are generally light text over dark backgrounds.
                double luminanceDelta = foregroundLuminance.Value - backgroundLuminance.Value;
                if (Math.Abs(luminanceDelta) >= 0.05d)
                    return luminanceDelta > 0d;
            }

            if (backgroundLuminance.HasValue)
                return backgroundLuminance.Value < 0.52d;

            if (foregroundLuminance.HasValue)
                return foregroundLuminance.Value > 0.52d;

            // Safe default for NT trading layouts.
            return true;
        }

        private static bool? TryResolveThemeFromConfig()
        {
            try
            {
                string runtimeSkinName = TryGetSkinNameFromRuntime();
                if (!string.IsNullOrWhiteSpace(runtimeSkinName))
                {
                    bool? runtimeBlueprintTheme = TryResolveThemeFromSkinBlueprint(runtimeSkinName);
                    if (runtimeBlueprintTheme.HasValue)
                        return runtimeBlueprintTheme.Value;

                    bool? runtimeNamedTheme = TryResolveThemeFromSkinName(runtimeSkinName);
                    if (runtimeNamedTheme.HasValue)
                        return runtimeNamedTheme.Value;
                }

                string configPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "NinjaTrader 8",
                    "Config.xml");
                if (!File.Exists(configPath))
                    return null;

                XDocument document = XDocument.Load(configPath);
                string skinName = document
                    .Descendants()
                    .FirstOrDefault(node => string.Equals(node.Name.LocalName, "Skin", StringComparison.OrdinalIgnoreCase))
                    ?.Value
                    ?.Trim();

                if (string.IsNullOrWhiteSpace(skinName))
                    return null;

                bool? blueprintTheme = TryResolveThemeFromSkinBlueprint(skinName);
                if (blueprintTheme.HasValue)
                    return blueprintTheme.Value;

                bool? namedTheme = TryResolveThemeFromSkinName(skinName);
                if (namedTheme.HasValue)
                    return namedTheme.Value;
            }
            catch
            {
            }

            return null;
        }

        private static bool? TryResolveThemeFromSkinName(string skinName)
        {
            if (string.IsNullOrWhiteSpace(skinName))
                return null;

            string normalized = skinName.Trim().ToLowerInvariant();
            if (normalized.Contains("light"))
                return false;

            if (normalized.Contains("dark") || normalized.Contains("gray"))
                return true;

            // Default NinjaTrader skin is light.
            if (string.Equals(normalized, "ninjatrader", StringComparison.Ordinal))
                return false;

            return null;
        }

        private static string TryGetSkinNameFromRuntime()
        {
            try
            {
                Type globalsType = Type.GetType("NinjaTrader.Core.Globals, NinjaTrader.Core", throwOnError: false);
                if (globalsType == null)
                {
                    globalsType = AppDomain.CurrentDomain
                        .GetAssemblies()
                        .Select(assembly => assembly.GetType("NinjaTrader.Core.Globals", throwOnError: false))
                        .FirstOrDefault(type => type != null);
                }

                if (globalsType == null)
                    return null;

                PropertyInfo generalOptionsProperty = globalsType.GetProperty(
                    "GeneralOptions",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                object generalOptions = generalOptionsProperty?.GetValue(null);
                if (generalOptions == null)
                    return null;

                PropertyInfo skinProperty = generalOptions.GetType().GetProperty(
                    "Skin",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                string skinName = skinProperty?.GetValue(generalOptions)?.ToString();
                return string.IsNullOrWhiteSpace(skinName) ? null : skinName.Trim();
            }
            catch
            {
                return null;
            }
        }

        private static bool? TryResolveThemeFromSkinBlueprint(string skinName)
        {
            try
            {
                string blueprintPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "NinjaTrader 8",
                    "templates",
                    "Skins",
                    skinName,
                    "BluePrint.xaml");
                if (!File.Exists(blueprintPath))
                    return null;

                string xaml = File.ReadAllText(blueprintPath);
                string backgroundHex = ExtractBrushColorFromBlueprint(xaml, "BackgroundMainWindow")
                    ?? ExtractBrushColorFromBlueprint(xaml, "BackgroundTextInput")
                    ?? ExtractBrushColorFromBlueprint(xaml, "GridEntireBackground");
                string foregroundHex = ExtractBrushColorFromBlueprint(xaml, "FontControlBrush")
                    ?? ExtractBrushColorFromBlueprint(xaml, "FontTableBrush")
                    ?? ExtractBrushColorFromBlueprint(xaml, "GridRowForeground");

                double? backgroundLuminance = ParseHexLuminance(backgroundHex);
                double? foregroundLuminance = ParseHexLuminance(foregroundHex);

                if (backgroundLuminance.HasValue && foregroundLuminance.HasValue)
                {
                    double luminanceDelta = foregroundLuminance.Value - backgroundLuminance.Value;
                    if (Math.Abs(luminanceDelta) >= 0.05d)
                        return luminanceDelta > 0d;
                }

                if (backgroundLuminance.HasValue)
                    return backgroundLuminance.Value < 0.52d;

                if (foregroundLuminance.HasValue)
                    return foregroundLuminance.Value > 0.52d;
            }
            catch
            {
            }

            return null;
        }

        private static string ExtractBrushColorFromBlueprint(string xaml, string key)
        {
            if (string.IsNullOrWhiteSpace(xaml) || string.IsNullOrWhiteSpace(key))
                return null;

            string solidPattern = $@"<SolidColorBrush[^>]*x:Key\s*=\s*""{Regex.Escape(key)}""[^>]*Color\s*=\s*""(?<color>#[0-9A-Fa-f]{{6,8}})""";
            Match solidMatch = Regex.Match(xaml, solidPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (solidMatch.Success)
                return solidMatch.Groups["color"].Value;

            string gradientPattern = $@"<(?:LinearGradientBrush|RadialGradientBrush)[^>]*x:Key\s*=\s*""{Regex.Escape(key)}""[^>]*>(?<body>[\s\S]*?)</(?:LinearGradientBrush|RadialGradientBrush)>";
            Match gradientMatch = Regex.Match(xaml, gradientPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!gradientMatch.Success)
                return null;

            Match stopColorMatch = Regex.Match(
                gradientMatch.Groups["body"].Value,
                @"Color\s*=\s*""(?<color>#[0-9A-Fa-f]{6,8})""",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return stopColorMatch.Success ? stopColorMatch.Groups["color"].Value : null;
        }

        private static double? ParseHexLuminance(string colorHex)
        {
            if (string.IsNullOrWhiteSpace(colorHex))
                return null;

            try
            {
                object converted = System.Windows.Media.ColorConverter.ConvertFromString(colorHex);
                if (!(converted is System.Windows.Media.Color color))
                    return null;

                // Ignore alpha so transparent colors do not misclassify light/dark.
                return ((0.2126 * color.R) +
                        (0.7152 * color.G) +
                        (0.0722 * color.B)) / 255d;
            }
            catch
            {
                return null;
            }
        }

        private static System.Windows.Media.SolidColorBrush ResolveSolidBrush(params object[] candidates)
        {
            if (candidates == null)
                return null;

            foreach (object candidate in candidates)
            {
                if (candidate is System.Windows.Media.SolidColorBrush solidBrush)
                    return solidBrush;
            }

            return null;
        }

        private static double? ComputeLuminance(System.Windows.Media.SolidColorBrush brush)
        {
            if (brush == null)
                return null;

            return ((0.2126 * brush.Color.R) +
                    (0.7152 * brush.Color.G) +
                    (0.0722 * brush.Color.B)) / 255d;
        }

        private static void ApplySkinResource(FrameworkElement element, DependencyProperty property, params string[] keys)
        {
            if (element == null || property == null || keys == null)
                return;

            foreach (string key in keys)
            {
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                if (FindSkinResource(element, key) != null)
                {
                    element.SetResourceReference(property, key);
                    return;
                }
            }
        }

        private static Style FindSkinStyle(Type targetType)
        {
            if (targetType == null)
                return null;

            object resource = Application.Current?.TryFindResource(targetType);
            return resource as Style;
        }

        private static Style CreateSkinAwareTabItemStyle(FrameworkElement context)
        {
            var style = new Style(typeof(TabItem));

            string windowBackgroundKey = FindSkinResourceKey(context, "BackgroundMainWindow", "GridEntireBackground");
            string tabBackgroundKey = windowBackgroundKey ?? FindSkinResourceKey(context, "GridEntireBackground", "BackgroundMainWindow");
            string selectedBackgroundKey = FindSkinResourceKey(context, "BackgroundTextInput", "GridEntireBackground", "BackgroundMainWindow");
            string hoverBackgroundKey = FindSkinResourceKey(context, "BackgroundTableHeader", "BackgroundTableHeaderVertical", "BackgroundTextInput", "GridEntireBackground");
            string tabForegroundKey = FindSkinResourceKey(context, "FontControlBrush", "FontTableBrush", "GridRowForeground");
            string activeBorderKey = FindSkinResourceKey(context, "BorderThinBrush", "TabControlBorderBrush", "GridHeaderHighlight");
            string inactiveBorderKey = windowBackgroundKey ?? activeBorderKey;

            if (!string.IsNullOrWhiteSpace(tabBackgroundKey))
                style.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension(tabBackgroundKey)));
            if (!string.IsNullOrWhiteSpace(tabForegroundKey))
                style.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension(tabForegroundKey)));
            if (!string.IsNullOrWhiteSpace(inactiveBorderKey))
                style.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension(inactiveBorderKey)));
            if (!string.IsNullOrWhiteSpace(activeBorderKey))
                style.Setters.Add(new Setter(FrameworkElement.TagProperty, new DynamicResourceExtension(activeBorderKey)));

            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1, 0, 1, 1)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(14, 7, 14, 7)));
            style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 30d));
            style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Normal));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Stretch));
            style.Setters.Add(new Setter(UIElement.OpacityProperty, 0.85));
            style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));

            double? tabFontSize = FindSkinDouble(context, "FontControlHeight", "FontTableHeight", "FontHeaderLevel4Height");
            if (tabFontSize.HasValue)
                style.Setters.Add(new Setter(Control.FontSizeProperty, tabFontSize.Value));

            var template = new ControlTemplate(typeof(TabItem));
            var rootFactory = new FrameworkElementFactory(typeof(Grid));

            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.Name = "TabBorder";
            borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            borderFactory.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            borderFactory.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            borderFactory.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            borderFactory.SetValue(Border.SnapsToDevicePixelsProperty, true);

            var contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            contentFactory.Name = "TabHeaderContent";
            contentFactory.SetValue(ContentPresenter.ContentSourceProperty, "Header");
            contentFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            contentFactory.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
            borderFactory.AppendChild(contentFactory);
            rootFactory.AppendChild(borderFactory);

            var topBorderFactory = new FrameworkElementFactory(typeof(Border));
            topBorderFactory.Name = "TabTopBorder";
            topBorderFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top);
            topBorderFactory.SetValue(FrameworkElement.HeightProperty, 1.0);
            topBorderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(FrameworkElement.TagProperty));
            topBorderFactory.SetValue(UIElement.IsHitTestVisibleProperty, false);
            rootFactory.AppendChild(topBorderFactory);

            template.VisualTree = rootFactory;

            var selectedTrigger = new Trigger { Property = TabItem.IsSelectedProperty, Value = true };
            if (!string.IsNullOrWhiteSpace(selectedBackgroundKey))
                selectedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension(selectedBackgroundKey)));
            if (!string.IsNullOrWhiteSpace(selectedBackgroundKey))
                selectedTrigger.Setters.Add(new Setter(FrameworkElement.TagProperty, new DynamicResourceExtension(selectedBackgroundKey)));
            if (!string.IsNullOrWhiteSpace(tabForegroundKey))
                selectedTrigger.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension(tabForegroundKey)));
            if (!string.IsNullOrWhiteSpace(activeBorderKey))
                selectedTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension(activeBorderKey)));
            selectedTrigger.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Medium));
            selectedTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 1.0));
            template.Triggers.Add(selectedTrigger);

            var hoverTrigger = new MultiTrigger();
            hoverTrigger.Conditions.Add(new Condition(UIElement.IsMouseOverProperty, true));
            hoverTrigger.Conditions.Add(new Condition(TabItem.IsSelectedProperty, false));
            if (!string.IsNullOrWhiteSpace(hoverBackgroundKey))
                hoverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension(hoverBackgroundKey)));
            template.Triggers.Add(hoverTrigger);

            var disabledTrigger = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabledTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.6));
            template.Triggers.Add(disabledTrigger);

            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            return style;
        }

        private static string FindSkinResourceKey(FrameworkElement context, params string[] keys)
        {
            if (keys == null)
                return null;

            foreach (string key in keys)
            {
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                if (FindSkinResource(context, key) != null)
                    return key;
            }

            return null;
        }

        private static double? FindSkinDouble(FrameworkElement context, params string[] keys)
        {
            if (keys == null)
                return null;

            foreach (string key in keys)
            {
                object resource = FindSkinResource(context, key);
                if (resource is double directDouble)
                    return directDouble;
                if (resource is float directFloat)
                    return directFloat;
                if (resource is decimal directDecimal)
                    return (double)directDecimal;
                if (resource is int directInt)
                    return directInt;
                if (resource is string text &&
                    double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
                {
                    return parsed;
                }
            }

            return null;
        }

        private static Style CreateSkinAwareScrollBarStyle(FrameworkElement context)
        {
            var baseStyle = FindSkinStyle(typeof(System.Windows.Controls.Primitives.ScrollBar));
            var style = new Style(typeof(System.Windows.Controls.Primitives.ScrollBar), baseStyle);

            ApplySkinResourceSetter(style, Control.BackgroundProperty, context, "BackgroundTextInput", "BackgroundMainWindow", "GridEntireBackground");
            ApplySkinResourceSetter(style, Control.ForegroundProperty, context, "FontControlBrush", "FontTableBrush");
            ApplySkinResourceSetter(style, Control.BorderBrushProperty, context, "BorderThinBrush", "TabControlBorderBrush");

            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
            style.Setters.Add(new Setter(FrameworkElement.SnapsToDevicePixelsProperty, true));
            style.Setters.Add(new Setter(FrameworkElement.WidthProperty, 11d));
            style.Setters.Add(new Setter(FrameworkElement.HeightProperty, 11d));
            style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 11d));
            style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 11d));

            var verticalTrigger = new Trigger
            {
                Property = System.Windows.Controls.Primitives.ScrollBar.OrientationProperty,
                Value = Orientation.Vertical
            };
            verticalTrigger.Setters.Add(new Setter(FrameworkElement.WidthProperty, 11d));
            verticalTrigger.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 11d));
            style.Triggers.Add(verticalTrigger);

            var horizontalTrigger = new Trigger
            {
                Property = System.Windows.Controls.Primitives.ScrollBar.OrientationProperty,
                Value = Orientation.Horizontal
            };
            horizontalTrigger.Setters.Add(new Setter(FrameworkElement.HeightProperty, 11d));
            horizontalTrigger.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 11d));
            style.Triggers.Add(horizontalTrigger);

            return style;
        }

        private static void ApplySkinResourceSetter(Style style, DependencyProperty property, FrameworkElement context, params string[] keys)
        {
            if (style == null || property == null || keys == null)
                return;

            foreach (string key in keys)
            {
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                object resource = FindSkinResource(context, key);
                if (resource == null)
                    continue;

                style.Setters.Add(new Setter(property, resource));
                return;
            }
        }

        private static object FindSkinResource(FrameworkElement context, string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return null;

            return context?.TryFindResource(key) ?? Application.Current?.TryFindResource(key);
        }
    }
}
