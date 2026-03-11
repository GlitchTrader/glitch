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
using System.Linq;
using System.Text;

namespace Glitch.UI
{
    internal static class GlitchTradingViewMacroHtmlFactory
    {
        private sealed class ThemePalette
        {
            public string WidgetTheme { get; set; }
            public string PageBackground { get; set; }
            public string CardBackground { get; set; }
            public string BorderColor { get; set; }
            public string GridColor { get; set; }
            public string TextColor { get; set; }
            public string MutedTextColor { get; set; }
            public string LinkColor { get; set; }
            public string ScrollTrackColor { get; set; }
            public string ScrollThumbColor { get; set; }
            public string ScrollThumbHoverColor { get; set; }
        }

        private sealed class SymbolDefinition
        {
            public string Symbol { get; set; }
            public string Title { get; set; }
        }

        private static readonly IReadOnlyList<SymbolDefinition> NasdaqAndMag7 = new[]
        {
            new SymbolDefinition { Symbol = "FOREXCOM:NSXUSD", Title = "US 100" },
            new SymbolDefinition { Symbol = "NASDAQ:AAPL", Title = "AAPL" },
            new SymbolDefinition { Symbol = "NASDAQ:MSFT", Title = "MSFT" },
            new SymbolDefinition { Symbol = "NASDAQ:NVDA", Title = "NVDA" },
            new SymbolDefinition { Symbol = "NASDAQ:AMZN", Title = "AMZN" },
            new SymbolDefinition { Symbol = "NASDAQ:META", Title = "META" },
            new SymbolDefinition { Symbol = "NASDAQ:GOOGL", Title = "GOOGL" },
            new SymbolDefinition { Symbol = "NASDAQ:TSLA", Title = "TSLA" }
        };

        private static readonly IReadOnlyList<SymbolDefinition> TechnicalNasdaqAndMag7 = new[]
        {
            new SymbolDefinition { Symbol = "NASDAQ:NDX", Title = "NASDAQ 100" },
            new SymbolDefinition { Symbol = "NASDAQ:AAPL", Title = "AAPL" },
            new SymbolDefinition { Symbol = "NASDAQ:MSFT", Title = "MSFT" },
            new SymbolDefinition { Symbol = "NASDAQ:NVDA", Title = "NVDA" },
            new SymbolDefinition { Symbol = "NASDAQ:AMZN", Title = "AMZN" },
            new SymbolDefinition { Symbol = "NASDAQ:META", Title = "META" },
            new SymbolDefinition { Symbol = "NASDAQ:GOOGL", Title = "GOOGL" },
            new SymbolDefinition { Symbol = "NASDAQ:TSLA", Title = "TSLA" }
        };

        public static string BuildTickerTapePage(bool isDarkTheme)
        {
            ThemePalette palette = CreatePalette(isDarkTheme);
            string symbolsJson = string.Join(
                ",\n",
                NasdaqAndMag7.Select(symbol => $@"    {{
      ""proName"": ""{symbol.Symbol}"",
      ""title"": ""{symbol.Title}""
    }}"));

            string body = $@"
<div class=""ticker-shell"">
  <div class=""tradingview-widget-container ticker-widget-container"">
    <div class=""tradingview-widget-container__widget""></div>
    <script type=""text/javascript"" src=""https://s3.tradingview.com/external-embedding/embed-widget-ticker-tape.js"" async>
{{
  ""symbols"": [
{symbolsJson}
  ],
  ""colorTheme"": ""{palette.WidgetTheme}"",
  ""locale"": ""en"",
  ""largeChartUrl"": """",
  ""isTransparent"": false,
  ""showSymbolLogo"": true,
  ""displayMode"": ""adaptive""
}}
    </script>
  </div>
</div>";

            return WrapPage("Glitch TradingView Macro", palette, body, @"
.ticker-shell {
  height: 100%;
  width: 100%;
}
.ticker-widget-container,
.ticker-widget-container__widget {
  height: 100%;
  width: 100%;
}");
        }

        public static string BuildChartsPage(bool isDarkTheme)
        {
            ThemePalette palette = CreatePalette(isDarkTheme);
            string widgets = string.Join(
                "\n",
                NasdaqAndMag7.Select(symbol => BuildAdvancedChartWidget(symbol, palette, "15")));

            return WrapPage("Glitch Charts", palette, $@"
<div class=""macro-grid charts-grid"">
{widgets}
</div>", @"
.macro-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(360px, 1fr));
  gap: 12px;
  min-height: 100%;
  align-content: start;
}
.charts-grid {
  grid-auto-rows: minmax(320px, 1fr);
}");
        }

        public static string BuildTechnicalsPage(bool isDarkTheme)
        {
            ThemePalette palette = CreatePalette(isDarkTheme);
            string widgets = string.Join(
                "\n",
                TechnicalNasdaqAndMag7.Select(symbol => BuildTechnicalWidget(symbol, palette, "15m")));

            return WrapPage("Glitch Technicals", palette, $@"
<div class=""macro-grid technical-grid"">
{widgets}
</div>", @"
.macro-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(320px, 1fr));
  gap: 12px;
  min-height: 100%;
  align-content: start;
}
.technical-grid {
  grid-auto-rows: minmax(375px, 1fr);
}");
        }

        public static string BuildMacroPage(bool isDarkTheme)
        {
            ThemePalette palette = CreatePalette(isDarkTheme);
            string body = $@"
<div class=""macro-layout"">
  <div class=""macro-cell macro-heatmap"">
    {BuildStockHeatmapWidget(palette)}
  </div>
  <div class=""macro-cell macro-news"">
    {BuildTimelineWidget(palette)}
  </div>
  <div class=""macro-cell macro-calendar"">
    {BuildEventsWidget(palette)}
  </div>
</div>";

            return WrapPage("Glitch Macro", palette, body, @"
.macro-layout {
  min-height: 100%;
  display: grid;
  grid-template-columns: minmax(460px, 1.6fr) minmax(320px, 1fr);
  grid-template-rows: minmax(320px, 1fr) minmax(320px, 1fr);
  gap: 12px;
}
.macro-cell {
  min-height: 0;
}
.macro-heatmap {
  grid-row: 1 / span 2;
}
@media (max-width: 1180px) {
  .macro-layout {
    grid-template-columns: 1fr;
    grid-template-rows: minmax(420px, 1fr) minmax(320px, 1fr) minmax(320px, 1fr);
  }
  .macro-heatmap {
    grid-row: auto;
  }
}");
        }

        private static string BuildAdvancedChartWidget(SymbolDefinition symbol, ThemePalette palette, string interval)
        {
            string title = $"{symbol.Title} chart";
            string linkUrl = GetSymbolUrl(symbol.Symbol);
            return $@"
<div class=""widget-card"">
  <div class=""tradingview-widget-container widget-fill"">
    <div class=""tradingview-widget-container__widget chart-body""></div>
    <script type=""text/javascript"" src=""https://s3.tradingview.com/external-embedding/embed-widget-advanced-chart.js"" async>
{{
  ""allow_symbol_change"": true,
  ""calendar"": false,
  ""details"": false,
  ""hide_side_toolbar"": true,
  ""hide_top_toolbar"": false,
  ""hide_legend"": false,
  ""hide_volume"": false,
  ""hotlist"": false,
  ""interval"": ""{interval}"",
  ""locale"": ""en"",
  ""save_image"": true,
  ""style"": ""1"",
  ""symbol"": ""{symbol.Symbol}"",
  ""theme"": ""{palette.WidgetTheme}"",
  ""timezone"": ""Etc/UTC"",
  ""backgroundColor"": ""{palette.PageBackground}"",
  ""gridColor"": ""{palette.GridColor}"",
  ""watchlist"": [],
  ""withdateranges"": false,
  ""compareSymbols"": [],
  ""studies"": [],
  ""autosize"": true
}}
    </script>
  </div>
</div>";
        }

        private static string BuildTechnicalWidget(SymbolDefinition symbol, ThemePalette palette, string interval)
        {
            string title = $"{symbol.Title} technical analysis";
            string linkUrl = GetSymbolUrl(symbol.Symbol, "/technicals/");
            return $@"
<div class=""widget-card"">
  <div class=""tradingview-widget-container widget-fill"">
    <div class=""tradingview-widget-container__widget widget-fill""></div>
    <script type=""text/javascript"" src=""https://s3.tradingview.com/external-embedding/embed-widget-technical-analysis.js"" async>
{{
  ""colorTheme"": ""{palette.WidgetTheme}"",
  ""displayMode"": ""single"",
  ""isTransparent"": false,
  ""locale"": ""en"",
  ""interval"": ""{interval}"",
  ""disableInterval"": false,
  ""width"": ""100%"",
  ""height"": ""100%"",
  ""symbol"": ""{symbol.Symbol}"",
  ""showIntervalTabs"": true
}}
    </script>
  </div>
</div>";
        }

        private static string BuildEventsWidget(ThemePalette palette)
        {
            return $@"
<div class=""widget-card widget-fill"">
  <div class=""tradingview-widget-container widget-fill"">
    <div class=""tradingview-widget-container__widget widget-fill""></div>
    <script type=""text/javascript"" src=""https://s3.tradingview.com/external-embedding/embed-widget-events.js"" async>
{{
  ""colorTheme"": ""{palette.WidgetTheme}"",
  ""isTransparent"": false,
  ""locale"": ""en"",
  ""countryFilter"": ""ar,au,br,ca,cn,fr,de,in,id,it,jp,kr,mx,ru,sa,za,tr,gb,us,eu"",
  ""importanceFilter"": ""-1,0,1"",
  ""width"": ""100%"",
  ""height"": ""100%""
}}
    </script>
  </div>
</div>";
        }

        private static string BuildTimelineWidget(ThemePalette palette)
        {
            return $@"
<div class=""widget-card widget-fill"">
  <div class=""tradingview-widget-container widget-fill"">
    <div class=""tradingview-widget-container__widget widget-fill""></div>
    <script type=""text/javascript"" src=""https://s3.tradingview.com/external-embedding/embed-widget-timeline.js"" async>
{{
  ""displayMode"": ""regular"",
  ""feedMode"": ""all_symbols"",
  ""colorTheme"": ""{palette.WidgetTheme}"",
  ""isTransparent"": false,
  ""locale"": ""en"",
  ""width"": ""100%"",
  ""height"": ""100%""
}}
    </script>
  </div>
</div>";
        }

        private static string BuildStockHeatmapWidget(ThemePalette palette)
        {
            return $@"
<div class=""widget-card widget-fill"">
  <div class=""tradingview-widget-container widget-fill"">
    <div class=""tradingview-widget-container__widget widget-fill""></div>
    <script type=""text/javascript"" src=""https://s3.tradingview.com/external-embedding/embed-widget-stock-heatmap.js"" async>
{{
  ""exchanges"": [],
  ""dataSource"": ""SPX500"",
  ""grouping"": ""sector"",
  ""blockSize"": ""market_cap_basic"",
  ""blockColor"": ""Perf.YTD"",
  ""locale"": ""en"",
  ""symbolUrl"": """",
  ""colorTheme"": ""{palette.WidgetTheme}"",
  ""hasTopBar"": false,
  ""isDataSetEnabled"": false,
  ""isZoomEnabled"": true,
  ""hasSymbolTooltip"": true,
  ""isMonoSize"": false,
  ""width"": ""100%"",
  ""height"": ""100%""
}}
    </script>
  </div>
</div>";
        }

        private static string WrapPage(string title, ThemePalette palette, string bodyHtml, string extraCss)
        {
            var html = new StringBuilder();
            html.AppendLine("<!DOCTYPE html>");
            html.AppendLine("<html>");
            html.AppendLine("<head>");
            html.AppendLine(@"  <meta charset=""utf-8"" />");
            html.AppendLine(@"  <meta http-equiv=""X-UA-Compatible"" content=""IE=Edge"" />");
            html.AppendLine(@"  <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"" />");
            html.AppendLine($"  <title>{title}</title>");
            html.AppendLine("  <style>");
            html.AppendLine("    html, body { height: 100%; width: 100%; margin: 0; padding: 0; overflow: hidden; }");
            html.AppendLine($"    body {{ background: {palette.PageBackground}; color: {palette.TextColor}; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif; }}");
            html.AppendLine("    #app { box-sizing: border-box; width: 100%; height: 100%; padding: 12px; overflow: auto; }");
            html.AppendLine($@"    html, body, #app {{
      scrollbar-color: {palette.ScrollThumbColor} {palette.ScrollTrackColor};
      scrollbar-width: thin;
    }}");
            html.AppendLine($@"    #app::-webkit-scrollbar {{
      width: 10px;
      height: 10px;
    }}");
            html.AppendLine($@"    #app::-webkit-scrollbar-track {{
      background: {palette.ScrollTrackColor};
      border: 1px solid {palette.BorderColor};
      border-radius: 8px;
    }}");
            html.AppendLine($@"    #app::-webkit-scrollbar-thumb {{
      background: {palette.ScrollThumbColor};
      border: 1px solid {palette.BorderColor};
      border-radius: 8px;
    }}");
            html.AppendLine($@"    #app::-webkit-scrollbar-thumb:hover {{
      background: {palette.ScrollThumbHoverColor};
    }}");
            html.AppendLine($@"    .widget-card {{
      box-sizing: border-box;
      height: 100%;
      min-height: 0;
      background: {palette.CardBackground};
      border: 1px solid {palette.BorderColor};
      border-radius: 6px;
      overflow: hidden;
    }}");
            html.AppendLine("    .widget-fill { width: 100%; height: 100%; min-height: 0; }");
            html.AppendLine("    .chart-body { height: calc(100% - 32px); width: 100%; }");
            html.AppendLine($@"    .tradingview-widget-copyright {{
      box-sizing: border-box;
      height: 32px;
      padding: 7px 10px;
      font-size: 11px;
      line-height: 18px;
      color: {palette.MutedTextColor};
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }}");
            html.AppendLine($@"    .tradingview-widget-copyright .blue-text {{ color: {palette.LinkColor}; }}");
            html.AppendLine("    .trademark { opacity: 0.72; }");
            html.AppendLine(extraCss ?? string.Empty);
            html.AppendLine("  </style>");
            html.AppendLine("</head>");
            html.AppendLine("<body>");
            html.AppendLine(@"  <div id=""app"">");
            html.AppendLine(bodyHtml);
            html.AppendLine("  </div>");
            html.AppendLine("</body>");
            html.AppendLine("</html>");
            return html.ToString();
        }

        private static ThemePalette CreatePalette(bool isDarkTheme)
        {
            if (isDarkTheme)
            {
                return new ThemePalette
                {
                    WidgetTheme = "dark",
                    PageBackground = "#0F0F0F",
                    CardBackground = "#151515",
                    BorderColor = "#2B2B2B",
                    GridColor = "rgba(242, 242, 242, 0.06)",
                    TextColor = "#E7E7E7",
                    MutedTextColor = "#9E9E9E",
                    LinkColor = "#38D5BF",
                    ScrollTrackColor = "#1B1C21",
                    ScrollThumbColor = "#4B4F59",
                    ScrollThumbHoverColor = "#6A707E"
                };
            }

            return new ThemePalette
            {
                WidgetTheme = "light",
                PageBackground = "#F5F6F8",
                CardBackground = "#FFFFFF",
                BorderColor = "#D7DADF",
                GridColor = "rgba(15, 23, 42, 0.08)",
                TextColor = "#1E293B",
                MutedTextColor = "#64748B",
                LinkColor = "#0F766E",
                ScrollTrackColor = "#E6E9EF",
                ScrollThumbColor = "#ADB5C4",
                ScrollThumbHoverColor = "#8D98AB"
            };
        }

        private static string GetSymbolUrl(string symbol, string suffix = "/")
        {
            string slug = (symbol ?? string.Empty).Replace(':', '-');
            return $"https://www.tradingview.com/symbols/{slug}{suffix}";
        }
    }
}
