using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Glitch.Services;

namespace Glitch.UI
{
    public partial class GlitchMainWindow
    {
        private UIElement CreateAiTabImpl()
        {
            var root = new Grid { Margin = new Thickness(20, 16, 20, 20) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var scopeCard = CreateAiCard();
            var scope = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };
            scope.Children.Add(new TextBlock { Text = "AI Trading Scope", FontWeight = FontWeights.SemiBold, FontSize = 16 });
            scope.Children.Add(new TextBlock
            {
                Text = "Enable existing group masters. Glitch AI trades the master; Replication owns its followers and ratios.",
                Margin = new Thickness(0, 4, 0, 10),
                Opacity = 0.72
            });
            _aiScopeRowsHost = new StackPanel();
            scope.Children.Add(_aiScopeRowsHost);
            scopeCard.Child = scope;
            Grid.SetRow(scopeCard, 0);
            root.Children.Add(scopeCard);

            var feedCard = CreateAiCard();
            feedCard.Margin = new Thickness(0, 12, 0, 0);
            var feedScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _aiFeedHost = new StackPanel { Margin = new Thickness(14, 12, 14, 14) };
            var feedHeading = new Grid();
            feedHeading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            feedHeading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            feedHeading.Children.Add(new TextBlock { Text = "Glitch AI Feed", FontWeight = FontWeights.SemiBold, FontSize = 16 });
            _aiFeedStatusText = new TextBlock { Opacity = 0.72, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(_aiFeedStatusText, 1);
            feedHeading.Children.Add(_aiFeedStatusText);
            _aiFeedHost.Children.Add(feedHeading);
            feedScroll.Content = _aiFeedHost;
            feedCard.Child = feedScroll;
            Grid.SetRow(feedCard, 1);
            root.Children.Add(feedCard);

            RefreshAiTab();
            return root;
        }

        private Border CreateAiCard()
        {
            var card = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3)
            };
            ApplySkinResource(card, Border.BackgroundProperty, "BackgroundMainWindow", "GridEntireBackground", "BackgroundTextInput");
            ApplySkinResource(card, Border.BorderBrushProperty, "BorderThinBrush", "TabControlBorderBrush");
            return card;
        }

        private void RefreshAiTab()
        {
            if (_aiScopeRowsHost == null || _aiFeedHost == null)
                return;

            UpdateHermesModeUi(GlitchHermesControlStateStore.Load().TradingPaused);
            RefreshAiScopeRows();
            while (_aiFeedHost.Children.Count > 1)
                _aiFeedHost.Children.RemoveAt(1);

            string exchangeRoot = GlitchStateStore.GetDefaultPath(Path.Combine("hermes", "exchange"));
            string minuteRoot = Path.Combine(exchangeRoot, "glitch", "minute-frames");
            string packetRoot = Path.Combine(exchangeRoot, "glitch", "decision-packets");
            FileInfo latestFrame = GetNewestFile(minuteRoot, "*.json");
            FileInfo latestPacket = GetNewestFile(packetRoot, "*.json");
            string decision = ReadLastNonEmptyLine(GlitchStateStore.GetDefaultPath(Path.Combine("intents", "decisions.jsonl")));
            string decisionIntentId = GlitchAiJsonFields.ExtractString(decision, "intent_id") ?? string.Empty;
            string execution = ReadLastLineContaining(
                GlitchStateStore.GetDefaultPath(Path.Combine("intents", "executions.jsonl")),
                string.IsNullOrWhiteSpace(decisionIntentId) ? string.Empty : "\"intent_id\":\"" + decisionIntentId + "\"");

            int frames = CountCurrentCycleFrames(minuteRoot, latestPacket == null ? DateTime.UtcNow : latestPacket.LastWriteTimeUtc);
            string snapshotAge = latestFrame == null ? "no snapshots" : FormatAge(DateTime.UtcNow - latestFrame.LastWriteTimeUtc) + " ago";
            string cycleAge = latestPacket == null ? "no sealed cycle" : Path.GetFileNameWithoutExtension(latestPacket.Name) + " · " + FormatAge(DateTime.UtcNow - latestPacket.LastWriteTimeUtc) + " ago";
            _aiFeedStatusText.Text = "Latest snapshot " + snapshotAge + "  |  Last cycle " + cycleAge;

            string action = GlitchAiJsonFields.ExtractString(decision, "action") ?? "Waiting";
            string decisionStatus = GlitchAiJsonFields.ExtractString(decision, "status") ?? "waiting";
            string executionStatus = GlitchAiJsonFields.ExtractString(execution, "status") ?? "waiting";
            string executionCode = GlitchAiJsonFields.ExtractString(execution, "code") ?? string.Empty;

            var stops = new UniformGrid { Columns = 5, Margin = new Thickness(0, 14, 0, 12) };
            stops.Children.Add(CreateAiStop("1", "Snapshots", Math.Min(frames, 5).ToString() + "/5", frames > 0));
            stops.Children.Add(CreateAiStop("2", "Packet Sealed", latestPacket == null ? "Waiting" : "Ready", latestPacket != null));
            stops.Children.Add(CreateAiStop("3", "AI Decision", action, !string.IsNullOrWhiteSpace(decision)));
            stops.Children.Add(CreateAiStop("4", "Execution Check", decisionStatus, !string.IsNullOrWhiteSpace(decision)));
            stops.Children.Add(CreateAiStop("5", "Outcome", string.IsNullOrWhiteSpace(executionCode) ? executionStatus : executionCode, !string.IsNullOrWhiteSpace(execution)));
            _aiFeedHost.Children.Add(stops);

            var panels = new Grid();
            panels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            panels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            panels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Border decisionPanel = CreateAiDetailPanel(
                "AI DECISION",
                "Action", action,
                "Confidence", FormatJsonNumber(decision, "confidence"),
                "Reason", GlitchAiJsonFields.ExtractString(decision, "reason") ?? "No decision has been recorded yet.",
                "Bull case", GlitchAiJsonFields.ExtractString(decision, "bull_case") ?? "—",
                "Bear case", GlitchAiJsonFields.ExtractString(decision, "bear_case") ?? "—",
                "Changes when", GlitchAiJsonFields.ExtractString(decision, "change_condition") ?? "—");
            panels.Children.Add(decisionPanel);
            Border executionPanel = CreateAiDetailPanel(
                "EXECUTION CHECK",
                "Decision", decisionStatus,
                "Account", GlitchAiJsonFields.ExtractString(decision, "account") ?? "—",
                "Intent", GlitchAiJsonFields.ExtractString(decision, "intent_id") ?? "—",
                "Outcome", executionStatus,
                "Code", string.IsNullOrWhiteSpace(executionCode) ? "—" : executionCode,
                "Message", GlitchAiJsonFields.ExtractString(execution, "message") ?? "—");
            Grid.SetColumn(executionPanel, 2);
            panels.Children.Add(executionPanel);
            _aiFeedHost.Children.Add(panels);
        }

        private void RefreshAiScopeRows()
        {
            _aiScopeRowsHost.Children.Clear();
            GlitchAiRailPolicy policy = GlitchAiRailPolicyStore.Load();
            var enabledMasters = new HashSet<string>(policy.ProfileAccountBindings.Values, StringComparer.OrdinalIgnoreCase);
            foreach (AccountGroupDefinition group in _accountGroups.Where(value => value != null && !string.IsNullOrWhiteSpace(value.MasterAccount)))
            {
                var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(105) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var enabled = new CheckBox { IsChecked = enabledMasters.Contains(group.MasterAccount), Tag = group.MasterAccount, VerticalAlignment = VerticalAlignment.Center };
                enabled.Click += OnAiScopeCheckboxClick;
                row.Children.Add(enabled);
                var master = new TextBlock { Text = group.MasterAccount, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(master, 1);
                row.Children.Add(master);
                AccountGridRow accountRow = _accountRows.FirstOrDefault(value => string.Equals(value.DisplayName, group.MasterAccount, StringComparison.OrdinalIgnoreCase));
                var type = new Border { Background = TealAccentBrush, CornerRadius = new CornerRadius(3), Padding = new Thickness(7, 2, 7, 2), HorizontalAlignment = HorizontalAlignment.Left };
                type.Child = new TextBlock { Text = ResolveAiAccountType(accountRow, group.MasterAccount), Foreground = AccentOnColorForegroundBrush, FontWeight = FontWeights.SemiBold };
                Grid.SetColumn(type, 2);
                row.Children.Add(type);
                string followers = string.Join(", ", group.Members.Where(value => value != null && value.IsEnabled && !value.IsMasterRow && !string.Equals(value.FollowerAccount, group.MasterAccount, StringComparison.OrdinalIgnoreCase)).Select(value => value.FollowerAccount + " ×" + value.Ratio.ToString("0.##")));
                var detail = new TextBlock { Text = string.IsNullOrWhiteSpace(followers) ? "Standalone account" : "Replicates to " + followers, Opacity = 0.72, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(detail, 3);
                row.Children.Add(detail);
                _aiScopeRowsHost.Children.Add(row);
            }
            if (_aiScopeRowsHost.Children.Count == 0)
                _aiScopeRowsHost.Children.Add(new TextBlock { Text = "Create a replication group to make an account available to Glitch AI.", Opacity = 0.72 });
        }

        private void OnAiScopeCheckboxClick(object sender, RoutedEventArgs e)
        {
            var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Grid row in _aiScopeRowsHost.Children.OfType<Grid>())
            {
                CheckBox checkBox = row.Children.OfType<CheckBox>().FirstOrDefault();
                if (checkBox?.IsChecked == true && checkBox.Tag is string master)
                    selected.Add(master);
            }
            SaveAiTradingScope(selected, "user_click");
        }

        private void ReconcileAiTradingScopeWithGroups()
        {
            GlitchAiRailPolicy policy = GlitchAiRailPolicyStore.Load();
            var currentMasters = new HashSet<string>(_accountGroups.Where(group => group != null).Select(group => group.MasterAccount), StringComparer.OrdinalIgnoreCase);
            var enabled = new HashSet<string>(policy.ProfileAccountBindings.Values.Where(currentMasters.Contains), StringComparer.OrdinalIgnoreCase);
            SaveAiTradingScope(enabled, "group_reconcile", false);
        }

        private void SaveAiTradingScope(HashSet<string> enabledMasters, string origin, bool refresh = true)
        {
            var orderedMasters = _accountGroups
                .Where(group => group != null && enabledMasters.Contains(group.MasterAccount))
                .Select(group => group.MasterAccount)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (AccountGroupDefinition group in _accountGroups.Where(group => group != null && enabledMasters.Contains(group.MasterAccount)))
            {
                allowed.Add(group.MasterAccount);
                foreach (AccountGroupMemberRow member in group.Members.Where(member => member != null && member.IsEnabled && !string.IsNullOrWhiteSpace(member.FollowerAccount)))
                    allowed.Add(member.FollowerAccount);
            }
            if (!GlitchAiRailPolicyStore.TrySaveTradingScope(orderedMasters, allowed, out string error))
                AppendJournal("System", "Glitch AI", "scope_save_failed|" + error);
            else if (origin == "user_click")
                AppendJournal("System", "Glitch AI", "scope_updated|masters=" + string.Join(",", orderedMasters));
            if (refresh)
                RefreshAiTab();
        }

        private static Border CreateAiStop(string number, string title, string value, bool complete)
        {
            var border = new Border { BorderBrush = complete ? TealAccentBrush : Brushes.Gray, BorderThickness = new Thickness(1), Margin = new Thickness(3), Padding = new Thickness(8) };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = number + "  " + title, FontWeight = FontWeights.SemiBold });
            stack.Children.Add(new TextBlock { Text = value, Margin = new Thickness(0, 4, 0, 0), Foreground = complete ? TealAccentBrush : Brushes.Gray, TextTrimming = TextTrimming.CharacterEllipsis });
            border.Child = stack;
            return border;
        }

        private Border CreateAiDetailPanel(string title, params string[] rows)
        {
            Border panel = CreateAiCard();
            var stack = new StackPanel { Margin = new Thickness(12) };
            stack.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
            for (int i = 0; i + 1 < rows.Length; i += 2)
            {
                stack.Children.Add(new TextBlock { Text = rows[i].ToUpperInvariant(), FontSize = 10, Opacity = 0.58, Margin = new Thickness(0, i == 0 ? 0 : 8, 0, 2) });
                stack.Children.Add(new TextBlock { Text = rows[i + 1], TextWrapping = TextWrapping.Wrap });
            }
            panel.Child = stack;
            return panel;
        }

        private static FileInfo GetNewestFile(string directory, string pattern)
        {
            if (!Directory.Exists(directory))
                return null;
            return new DirectoryInfo(directory).GetFiles(pattern).OrderByDescending(file => file.LastWriteTimeUtc).FirstOrDefault();
        }

        private static int CountCurrentCycleFrames(string directory, DateTime anchorUtc)
        {
            if (!Directory.Exists(directory))
                return 0;
            DateTime floor = new DateTime(anchorUtc.Year, anchorUtc.Month, anchorUtc.Day, anchorUtc.Hour, anchorUtc.Minute - (anchorUtc.Minute % 5), 0, DateTimeKind.Utc);
            return new DirectoryInfo(directory).GetFiles("*.json").Count(file => file.LastWriteTimeUtc >= floor && file.LastWriteTimeUtc < floor.AddMinutes(5));
        }

        private static string ReadLastNonEmptyLine(string path)
        {
            if (!File.Exists(path))
                return string.Empty;
            string last = string.Empty;
            foreach (string line in File.ReadLines(path))
                if (!string.IsNullOrWhiteSpace(line))
                    last = line;
            return last;
        }

        private static string ReadLastLineContaining(string path, string needle)
        {
            if (string.IsNullOrWhiteSpace(needle) || !File.Exists(path))
                return string.Empty;
            string last = string.Empty;
            foreach (string line in File.ReadLines(path))
                if (line.IndexOf(needle, StringComparison.Ordinal) >= 0)
                    last = line;
            return last;
        }

        private static string FormatJsonNumber(string json, string key)
        {
            return GlitchAiJsonFields.TryExtractNumber(json, key, out double value) ? value.ToString("0.00") : "—";
        }

        private static string FormatAge(TimeSpan age)
        {
            if (age.TotalSeconds < 0) age = TimeSpan.Zero;
            if (age.TotalMinutes < 1) return Math.Max(0, (int)age.TotalSeconds) + "s";
            if (age.TotalHours < 1) return (int)age.TotalMinutes + "m";
            return (int)age.TotalHours + "h";
        }

        private static string ResolveAiAccountType(AccountGridRow row, string accountName)
        {
            if (row != null && !string.IsNullOrWhiteSpace(row.AccountStatus))
                return string.Equals(row.AccountStatus, "AP", StringComparison.OrdinalIgnoreCase) ? "PA" : row.AccountStatus.ToUpperInvariant();
            return accountName != null && accountName.StartsWith("Sim", StringComparison.OrdinalIgnoreCase) ? "SIM" : "LIVE";
        }
    }
}
