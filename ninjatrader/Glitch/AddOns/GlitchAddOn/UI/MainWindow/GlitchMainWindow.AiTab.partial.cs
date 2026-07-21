using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
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
        private const int AiDecisionHistoryLimit = 20;

        private sealed class AiDecisionFeedItem
        {
            public string DecisionJson { get; set; }
            public string ExecutionJson { get; set; }
            public string IntentId { get; set; }
            public DateTime? DecisionUtc { get; set; }
            public FileInfo PacketFile { get; set; }
        }

        private sealed class AiSnapshotPreview
        {
            public string MinuteId { get; set; }
            public DateTime? CapturedUtc { get; set; }
            public double? Price { get; set; }
            public double? DirectionalScore { get; set; }
            public double? TradeabilityScore { get; set; }
            public double? Rsi { get; set; }
            public double? Atr { get; set; }
        }

        private readonly HashSet<string> _expandedAiDecisionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private List<AiDecisionFeedItem> _aiDecisionHistoryCache = new List<AiDecisionFeedItem>();
        private DateTime _aiDecisionHistoryDecisionWriteUtc;
        private DateTime _aiDecisionHistoryExecutionWriteUtc;
        private string _aiDecisionHistoryPacketFingerprint;
        private string _aiSnapshotPreviewCachePath;
        private DateTime _aiSnapshotPreviewCacheWriteUtc;
        private List<AiSnapshotPreview> _aiSnapshotPreviewCache = new List<AiSnapshotPreview>();
        private string _aiFeedRenderFingerprint;

        private UIElement CreateAiTabImpl()
        {
            var root = new Grid { Margin = new Thickness(20, 16, 20, 20) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var scope = new StackPanel { Margin = new Thickness(14, 0, 14, 0) };
            var scopeDescription = new TextBlock
            {
                Margin = new Thickness(0, 0, 0, 10),
                Opacity = 0.72
            };
            BindLocalizedText(
                scopeDescription,
                "ai.scope.description",
                "Enable existing group masters. Glitch AI trades the master; Replication owns its followers and ratios.");
            scope.Children.Add(scopeDescription);
            _aiScopeRowsHost = new StackPanel();
            scope.Children.Add(_aiScopeRowsHost);
            Expander scopeExpander = CreateAccordionExpander(root, "ai.scope.title", "AI Trading Scope");
            scopeExpander.IsExpanded = false;
            scopeExpander.Content = WrapAccordionSectionContent(scope);
            Grid.SetRow(scopeExpander, 0);
            root.Children.Add(scopeExpander);

            var feedCard = CreateAiCard();
            feedCard.Margin = new Thickness(0, 12, 0, 0);
            var feedScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _aiFeedHost = new StackPanel { Margin = new Thickness(14, 12, 14, 14) };
            var feedHeading = new Grid();
            feedHeading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            feedHeading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var feedTitle = new TextBlock { FontWeight = FontWeights.SemiBold, FontSize = 16 };
            BindLocalizedText(feedTitle, "ai.feed.title", "Glitch AI Feed");
            feedHeading.Children.Add(feedTitle);
            _aiFeedStatusText = new TextBlock { Opacity = 0.72, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(_aiFeedStatusText, 1);
            feedHeading.Children.Add(_aiFeedStatusText);
            _aiFeedHost.Children.Add(feedHeading);
            feedScroll.Content = _aiFeedHost;
            feedCard.Child = feedScroll;
            Grid.SetRow(feedCard, 1);
            root.Children.Add(feedCard);

            RegisterLocalizationBinding(() =>
            {
                _aiFeedRenderFingerprint = null;
                RefreshAiTab();
            });
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

            GlitchHermesControlState controlState = GlitchHermesControlStateStore.Load();
            UpdateHermesModeUi(controlState.TradingPaused);
            RefreshAiScopeRows();

            DateTime nowUtc = DateTime.UtcNow;
            string exchangeRoot = GlitchStateStore.GetDefaultPath(Path.Combine("hermes", "exchange"));
            string minuteRoot = Path.Combine(exchangeRoot, "glitch", "minute-frames");
            string packetRoot = Path.Combine(exchangeRoot, "glitch", "decision-packets");
            string decisionsPath = GlitchStateStore.GetDefaultPath(Path.Combine("intents", "decisions.jsonl"));
            string executionsPath = GlitchStateStore.GetDefaultPath(Path.Combine("intents", "executions.jsonl"));
            FileInfo latestFrame = GetNewestFile(minuteRoot, "*.json");
            List<AiDecisionFeedItem> history = LoadAiDecisionHistory(decisionsPath, executionsPath, packetRoot);
            AiDecisionFeedItem latest = history.FirstOrDefault();

            string snapshotAge = latestFrame == null
                ? L("ai.value.none", "none")
                : Lf("ai.age.ago_format", "{0} ago", FormatAge(nowUtc - latestFrame.LastWriteTimeUtc));
            string decisionAge = latest?.DecisionUtc == null
                ? L("ai.value.none", "none")
                : Lf("ai.age.ago_format", "{0} ago", FormatAge(nowUtc - latest.DecisionUtc.Value));
            _aiFeedStatusText.Text = Lf(
                "ai.feed.latest_status_format",
                "Latest snapshot {0}  |  Latest decision {1}",
                snapshotAge,
                decisionAge);

            bool aiAutoOn = !controlState.TradingPaused && GlitchAiAutoRuntimeController.IsTradingJobEnabled();
            DateTime frameAnchorUtc = latest?.DecisionUtc ?? nowUtc.AddMinutes(-5);
            int currentFrames = CountFramesAfter(minuteRoot, frameAnchorUtc);
            string renderFingerprint = string.Join(
                "|",
                aiAutoOn ? "1" : "0",
                latestFrame?.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture) ?? "0",
                File.Exists(decisionsPath) ? File.GetLastWriteTimeUtc(decisionsPath).Ticks.ToString(CultureInfo.InvariantCulture) : "0",
                File.Exists(executionsPath) ? File.GetLastWriteTimeUtc(executionsPath).Ticks.ToString(CultureInfo.InvariantCulture) : "0",
                _aiDecisionHistoryPacketFingerprint ?? "0",
                currentFrames.ToString(CultureInfo.InvariantCulture));
            if (string.Equals(renderFingerprint, _aiFeedRenderFingerprint, StringComparison.Ordinal))
                return;
            _aiFeedRenderFingerprint = renderFingerprint;
            while (_aiFeedHost.Children.Count > 1)
                _aiFeedHost.Children.RemoveAt(1);

            _aiFeedHost.Children.Add(CreateAiCurrentWindowPanel(
                currentFrames,
                latest?.DecisionUtc,
                aiAutoOn,
                nowUtc));

            if (latest == null)
            {
                Border waiting = CreateAiDetailPanel(
                    L("ai.decision.latest", "Latest AI Decision").ToUpperInvariant(),
                    L("ai.field.status", "Status"), aiAutoOn
                        ? L("ai.status.waiting_first", "Waiting for the first completed decision")
                        : L("ai.status.auto_off", "AI Auto is off"),
                    L("ai.field.snapshots", "Snapshots"), Lf(
                        "ai.snapshots.collected_short_format",
                        "{0}/5 collected",
                        Math.Min(currentFrames, 5)));
                waiting.Margin = new Thickness(0, 12, 0, 0);
                _aiFeedHost.Children.Add(waiting);
                return;
            }

            AddLatestAiDecision(latest);

            var historyHeading = new Grid { Margin = new Thickness(0, 18, 0, 8) };
            historyHeading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            historyHeading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            historyHeading.Children.Add(new TextBlock { Text = L("ai.history.title", "Decision History"), FontWeight = FontWeights.SemiBold, FontSize = 15 });
            var historyCount = new TextBlock { Text = Lf("ai.history.last_format", "Last {0}", history.Count), Opacity = 0.65 };
            Grid.SetColumn(historyCount, 1);
            historyHeading.Children.Add(historyCount);
            _aiFeedHost.Children.Add(historyHeading);

            foreach (AiDecisionFeedItem item in history)
                _aiFeedHost.Children.Add(CreateAiDecisionExpander(item));
        }

        private void RefreshAiScopeRows()
        {
            _aiScopeRowsHost.Children.Clear();
            GlitchAiRailPolicy policy = GlitchAiRailPolicyStore.Load();
            var enabledMasters = new HashSet<string>(policy.ProfileAccountBindings.Values, StringComparer.OrdinalIgnoreCase);

            var headings = new Grid { Margin = new Thickness(0, 0, 0, 5) };
            headings.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
            headings.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            headings.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            headings.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            AddAiScopeHeading(headings, 0, L("ai.scope.column.trade", "Trade").ToUpperInvariant());
            AddAiScopeHeading(headings, 1, L("ai.scope.column.master", "Master").ToUpperInvariant());
            AddAiScopeHeading(headings, 2, L("ai.scope.column.type", "Type").ToUpperInvariant());
            AddAiScopeHeading(headings, 3, L("ai.scope.column.route", "Replication Route").ToUpperInvariant());
            _aiScopeRowsHost.Children.Add(headings);

            foreach (AccountGroupDefinition group in _accountGroups.Where(value => value != null && !string.IsNullOrWhiteSpace(value.MasterAccount)))
            {
                var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
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
                string followers = string.Join(", ", group.Members.Where(value => value != null && value.IsEnabled && !value.IsMasterRow && !string.Equals(value.FollowerAccount, group.MasterAccount, StringComparison.OrdinalIgnoreCase)).Select(value => value.FollowerAccount + " x" + value.Ratio.ToString("0.##", CultureInfo.InvariantCulture)));
                string routeText = string.IsNullOrWhiteSpace(followers)
                    ? L("ai.scope.route.standalone", "Standalone master")
                    : Lf("ai.scope.route.replicated_format", "Master trades; Replication -> {0}", followers);
                var detail = new TextBlock { Text = routeText, Opacity = 0.72, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(detail, 3);
                row.Children.Add(detail);
                _aiScopeRowsHost.Children.Add(row);
            }
            if (_aiScopeRowsHost.Children.Count == 1)
                _aiScopeRowsHost.Children.Add(new TextBlock { Text = L("ai.scope.empty", "Create a replication group to make an account available to Glitch AI."), Opacity = 0.72 });
        }

        private static void AddAiScopeHeading(Grid grid, int column, string text)
        {
            var heading = new TextBlock { Text = text, FontSize = 10, FontWeight = FontWeights.SemiBold, Opacity = 0.55 };
            Grid.SetColumn(heading, column);
            grid.Children.Add(heading);
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

        private UIElement CreateAiCurrentWindowPanel(
            int frameCount,
            DateTime? latestDecisionUtc,
            bool aiAutoOn,
            DateTime nowUtc)
        {
            Border card = CreateAiCard();
            card.Margin = new Thickness(0, 12, 0, 0);
            var layout = new Grid { Margin = new Thickness(12, 10, 12, 10) };
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel();
            left.Children.Add(new TextBlock { Text = L("ai.window.title", "Current Window"), FontWeight = FontWeights.SemiBold });
            left.Children.Add(new TextBlock
            {
                Text = Lf(
                    "ai.window.snapshots_format",
                    "{0}/5 snapshots collected for the next decision",
                    Math.Min(frameCount, 5)),
                Margin = new Thickness(0, 4, 0, 0),
                Opacity = 0.72
            });
            layout.Children.Add(left);

            string cadence = DescribeAiDecisionCadence(aiAutoOn, latestDecisionUtc, nowUtc);
            var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
            right.Children.Add(new TextBlock
            {
                Text = aiAutoOn
                    ? L("ai.auto.on", "AI Auto On")
                    : L("ai.auto.off", "AI Auto Off"),
                Foreground = aiAutoOn ? TealAccentBrush : Brushes.Gray,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Right
            });
            right.Children.Add(new TextBlock
            {
                Text = cadence,
                Margin = new Thickness(0, 4, 0, 0),
                Foreground = aiAutoOn && latestDecisionUtc.HasValue && nowUtc - latestDecisionUtc.Value > TimeSpan.FromMinutes(12)
                    ? OrangeAccentBrush
                    : null,
                Opacity = 0.78,
                HorizontalAlignment = HorizontalAlignment.Right
            });
            Grid.SetColumn(right, 1);
            layout.Children.Add(right);
            card.Child = layout;
            return card;
        }

        private string DescribeAiDecisionCadence(bool aiAutoOn, DateTime? latestDecisionUtc, DateTime nowUtc)
        {
            if (!aiAutoOn)
                return L("ai.cadence.paused", "Scheduled calls are paused");
            if (!latestDecisionUtc.HasValue)
                return L("ai.status.waiting_first", "Waiting for the first completed decision");

            TimeSpan age = nowUtc - latestDecisionUtc.Value;
            if (age < TimeSpan.Zero)
                age = TimeSpan.Zero;
            if (age <= TimeSpan.FromMinutes(4))
            {
                TimeSpan remaining = TimeSpan.FromMinutes(5) - age;
                return Lf(
                    "ai.cadence.next_format",
                    "Next decision in about {0}m",
                    Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes)));
            }
            if (age <= TimeSpan.FromMinutes(12))
                return L("ai.cadence.due", "Decision due; waiting for the completed result");
            return L("ai.cadence.overdue", "Decision overdue - inspect the background worker");
        }

        private void AddLatestAiDecision(AiDecisionFeedItem item)
        {
            string decision = item.DecisionJson ?? string.Empty;
            string execution = item.ExecutionJson ?? string.Empty;
            string action = GlitchAiJsonFields.ExtractString(decision, "action") ?? L("ai.value.waiting", "Waiting");
            string decisionStatus = GlitchAiJsonFields.ExtractString(decision, "status") ?? "waiting";
            string executionStatus = GlitchAiJsonFields.ExtractString(execution, "status") ?? "waiting";
            string executionCode = GlitchAiJsonFields.ExtractString(execution, "code") ?? string.Empty;
            List<AiSnapshotPreview> snapshots = GetAiSnapshotPreviews(item.PacketFile);
            bool packetReady = item.PacketFile != null;

            var heading = new Grid { Margin = new Thickness(0, 16, 0, 0) };
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            heading.Children.Add(new TextBlock { Text = L("ai.decision.latest", "Latest AI Decision"), FontWeight = FontWeights.SemiBold, FontSize = 15 });
            var timestamp = new TextBlock { Text = FormatDecisionTimestamp(item.DecisionUtc), Opacity = 0.65 };
            Grid.SetColumn(timestamp, 1);
            heading.Children.Add(timestamp);
            _aiFeedHost.Children.Add(heading);

            var stops = new UniformGrid { Columns = 5, Margin = new Thickness(0, 8, 0, 12) };
            stops.Children.Add(CreateAiStop("1", L("ai.stage.snapshots", "Snapshots"), snapshots.Count.ToString(CultureInfo.InvariantCulture) + "/5", snapshots.Count >= 5));
            stops.Children.Add(CreateAiStop("2", L("ai.stage.packet", "Packet Sealed"), packetReady ? Path.GetFileNameWithoutExtension(item.PacketFile.Name) : L("ai.value.missing", "Missing"), packetReady));
            stops.Children.Add(CreateAiStop("3", L("ai.stage.decision", "AI Decision"), action, true));
            stops.Children.Add(CreateAiStop("4", L("ai.stage.execution", "Execution Check"), decisionStatus, true));
            stops.Children.Add(CreateAiStop("5", L("ai.stage.outcome", "Outcome"), string.IsNullOrWhiteSpace(executionCode) ? executionStatus : executionCode, !string.IsNullOrWhiteSpace(execution)));
            _aiFeedHost.Children.Add(stops);

            _aiFeedHost.Children.Add(CreateAiDecisionPanels(item));
            _aiFeedHost.Children.Add(CreateAiSnapshotTable(snapshots));
        }

        private UIElement CreateAiDecisionPanels(AiDecisionFeedItem item)
        {
            string decision = item.DecisionJson ?? string.Empty;
            string execution = item.ExecutionJson ?? string.Empty;
            string executionCode = GlitchAiJsonFields.ExtractString(execution, "code") ?? string.Empty;
            var panels = new Grid();
            panels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            panels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            panels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Border decisionPanel = CreateAiDetailPanel(
                L("ai.panel.decision", "AI Decision").ToUpperInvariant(),
                L("ai.field.time", "Time"), FormatDecisionTimestamp(item.DecisionUtc),
                L("ai.field.action", "Action"), GlitchAiJsonFields.ExtractString(decision, "action") ?? L("ai.value.waiting", "Waiting"),
                L("ai.field.confidence", "Confidence"), FormatJsonNumber(decision, "confidence"),
                L("ai.field.reason", "Reason"), GlitchAiJsonFields.ExtractString(decision, "reason") ?? L("ai.value.no_reason", "No reason recorded."),
                L("ai.field.bull_case", "Bull case"), GlitchAiJsonFields.ExtractString(decision, "bull_case") ?? "-",
                L("ai.field.bear_case", "Bear case"), GlitchAiJsonFields.ExtractString(decision, "bear_case") ?? "-",
                L("ai.field.changes_when", "Changes when"), GlitchAiJsonFields.ExtractString(decision, "change_condition") ?? "-");
            panels.Children.Add(decisionPanel);

            Border executionPanel = CreateAiDetailPanel(
                L("ai.panel.execution", "Execution Check").ToUpperInvariant(),
                L("ai.field.decision", "Decision"), GlitchAiJsonFields.ExtractString(decision, "status") ?? "waiting",
                L("ai.field.account", "Account"), GlitchAiJsonFields.ExtractString(decision, "account") ?? "-",
                L("ai.field.quantity", "Quantity"), FormatOptionalJsonNumber(decision, "quantity"),
                L("ai.field.protection", "Protection"), BuildAiProtectionSummary(decision),
                L("ai.field.intent", "Intent"), item.IntentId ?? "-",
                L("ai.field.outcome", "Outcome"), GlitchAiJsonFields.ExtractString(execution, "status") ?? "waiting",
                L("ai.field.code", "Code"), string.IsNullOrWhiteSpace(executionCode) ? "-" : executionCode,
                L("ai.field.message", "Message"), GlitchAiJsonFields.ExtractString(execution, "message") ?? "-");
            Grid.SetColumn(executionPanel, 2);
            panels.Children.Add(executionPanel);
            return panels;
        }

        private Expander CreateAiDecisionExpander(AiDecisionFeedItem item)
        {
            string action = GlitchAiJsonFields.ExtractString(item.DecisionJson, "action") ?? L("ai.value.waiting", "Waiting");
            string account = GlitchAiJsonFields.ExtractString(item.DecisionJson, "account") ?? "-";
            string executionCode = GlitchAiJsonFields.ExtractString(item.ExecutionJson, "code")
                ?? GlitchAiJsonFields.ExtractString(item.ExecutionJson, "status")
                ?? "waiting";

            string headerText = string.Join(
                "   |   ",
                FormatDecisionTimestamp(item.DecisionUtc),
                action,
                account,
                executionCode);

            var content = new ContentControl();
            Expander expander = CreateDisclosureRowExpander(_aiFeedHost, headerText);
            expander.Content = WrapDisclosureRowContent(content);
            expander.IsExpanded = !string.IsNullOrWhiteSpace(item.IntentId)
                && _expandedAiDecisionIds.Contains(item.IntentId);
            expander.Expanded += (sender, args) =>
            {
                if (!string.IsNullOrWhiteSpace(item.IntentId))
                    _expandedAiDecisionIds.Add(item.IntentId);
                if (content.Content == null)
                    content.Content = CreateAiDecisionHistoryBody(item);
            };
            expander.Collapsed += (sender, args) =>
            {
                if (!string.IsNullOrWhiteSpace(item.IntentId))
                    _expandedAiDecisionIds.Remove(item.IntentId);
            };
            if (expander.IsExpanded)
                content.Content = CreateAiDecisionHistoryBody(item);
            return expander;
        }

        private UIElement CreateAiDecisionHistoryBody(AiDecisionFeedItem item)
        {
            var body = new StackPanel();
            body.Children.Add(CreateAiDecisionPanels(item));
            body.Children.Add(CreateAiSnapshotTable(GetAiSnapshotPreviews(item.PacketFile)));
            return body;
        }

        private UIElement CreateAiSnapshotTable(IReadOnlyList<AiSnapshotPreview> snapshots)
        {
            Border card = CreateAiCard();
            card.Margin = new Thickness(0, 10, 0, 0);
            var stack = new StackPanel { Margin = new Thickness(12, 10, 12, 10) };
            stack.Children.Add(new TextBlock
            {
                Text = L("ai.snapshots.supporting", "Supporting Snapshots").ToUpperInvariant(),
                FontWeight = FontWeights.SemiBold,
                FontSize = 10,
                Opacity = 0.65,
                Margin = new Thickness(0, 0, 0, 7)
            });

            if (snapshots == null || snapshots.Count == 0)
            {
                stack.Children.Add(new TextBlock { Text = L("ai.snapshots.none", "No matching decision packet was found."), Opacity = 0.7 });
                card.Child = stack;
                return card;
            }

            var table = new Grid();
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            for (int column = 1; column < 7; column++)
                table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            AddAiSnapshotRow(
                table,
                0,
                true,
                L("ai.snapshot.column.minute", "Minute"),
                "MNQ",
                L("ai.snapshot.column.direction", "Direction"),
                L("ai.snapshot.column.tradeability", "Tradeability"),
                "RSI",
                "ATR",
                L("ai.snapshot.column.captured", "Captured"));
            int rowIndex = 1;
            foreach (AiSnapshotPreview snapshot in snapshots.Take(5))
            {
                AddAiSnapshotRow(
                    table,
                    rowIndex++,
                    false,
                    snapshot.MinuteId ?? "-",
                    FormatOptionalNumber(snapshot.Price, "0.00"),
                    FormatOptionalNumber(snapshot.DirectionalScore, "+0.000;-0.000;0.000"),
                    FormatOptionalNumber(snapshot.TradeabilityScore, "0.000"),
                    FormatOptionalNumber(snapshot.Rsi, "0.0"),
                    FormatOptionalNumber(snapshot.Atr, "0.00"),
                    snapshot.CapturedUtc.HasValue ? snapshot.CapturedUtc.Value.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture) : "-");
            }
            stack.Children.Add(table);
            card.Child = stack;
            return card;
        }

        private static void AddAiSnapshotRow(Grid table, int rowIndex, bool heading, params string[] values)
        {
            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int column = 0; column < values.Length; column++)
            {
                var cell = new TextBlock
                {
                    Text = values[column],
                    FontSize = heading ? 10 : 11,
                    FontWeight = heading ? FontWeights.SemiBold : FontWeights.Normal,
                    Opacity = heading ? 0.58 : 0.82,
                    Margin = new Thickness(2, heading ? 2 : 4, 6, heading ? 4 : 2),
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Grid.SetRow(cell, rowIndex);
                Grid.SetColumn(cell, column);
                table.Children.Add(cell);
            }
        }

        private List<AiDecisionFeedItem> LoadAiDecisionHistory(string decisionsPath, string executionsPath, string packetRoot)
        {
            DateTime decisionsWriteUtc = File.Exists(decisionsPath) ? File.GetLastWriteTimeUtc(decisionsPath) : DateTime.MinValue;
            DateTime executionsWriteUtc = File.Exists(executionsPath) ? File.GetLastWriteTimeUtc(executionsPath) : DateTime.MinValue;
            FileInfo[] packetFiles = Directory.Exists(packetRoot)
                ? new DirectoryInfo(packetRoot).GetFiles("*.json")
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .Take(200)
                    .ToArray()
                : new FileInfo[0];
            string packetFingerprint = packetFiles.Length.ToString(CultureInfo.InvariantCulture) + ":"
                + (packetFiles.FirstOrDefault()?.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture) ?? "0");
            if (_aiDecisionHistoryCache.Count > 0
                && decisionsWriteUtc == _aiDecisionHistoryDecisionWriteUtc
                && executionsWriteUtc == _aiDecisionHistoryExecutionWriteUtc
                && string.Equals(packetFingerprint, _aiDecisionHistoryPacketFingerprint, StringComparison.Ordinal))
                return _aiDecisionHistoryCache;

            List<string> decisions = ReadLastNonEmptyLines(decisionsPath, AiDecisionHistoryLimit);
            var intentIds = new HashSet<string>(
                decisions.Select(line => GlitchAiJsonFields.ExtractString(line, "intent_id"))
                    .Where(value => !string.IsNullOrWhiteSpace(value)),
                StringComparer.OrdinalIgnoreCase);
            var executionsByIntent = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(executionsPath) && intentIds.Count > 0)
            {
                foreach (string line in File.ReadLines(executionsPath))
                {
                    string intentId = GlitchAiJsonFields.ExtractString(line, "intent_id");
                    if (!string.IsNullOrWhiteSpace(intentId) && intentIds.Contains(intentId))
                        executionsByIntent[intentId] = line;
                }
            }

            var packetsBySnapshotHash = new Dictionary<string, FileInfo>(StringComparer.Ordinal);
            foreach (FileInfo packetFile in packetFiles)
            {
                string snapshotHash = ReadAiPacketFinalSnapshotHash(packetFile.FullName);
                if (!string.IsNullOrWhiteSpace(snapshotHash) && !packetsBySnapshotHash.ContainsKey(snapshotHash))
                    packetsBySnapshotHash.Add(snapshotHash, packetFile);
            }

            var items = new List<AiDecisionFeedItem>();
            foreach (string decision in decisions.AsEnumerable().Reverse())
            {
                string intentId = GlitchAiJsonFields.ExtractString(decision, "intent_id") ?? string.Empty;
                DateTime? decisionUtc = GlitchAiJsonFields.TryExtractUtc(decision, "created_utc");
                string snapshotHash = GlitchAiJsonFields.ExtractString(decision, "snapshot_hash") ?? string.Empty;
                FileInfo packet = null;
                if (!string.IsNullOrWhiteSpace(snapshotHash))
                    packetsBySnapshotHash.TryGetValue(snapshotHash, out packet);
                executionsByIntent.TryGetValue(intentId, out string execution);
                items.Add(new AiDecisionFeedItem
                {
                    DecisionJson = decision,
                    ExecutionJson = execution ?? string.Empty,
                    IntentId = intentId,
                    DecisionUtc = decisionUtc,
                    PacketFile = packet
                });
            }

            _aiDecisionHistoryDecisionWriteUtc = decisionsWriteUtc;
            _aiDecisionHistoryExecutionWriteUtc = executionsWriteUtc;
            _aiDecisionHistoryPacketFingerprint = packetFingerprint;
            _aiDecisionHistoryCache = items;
            return _aiDecisionHistoryCache;
        }

        private static string ReadAiPacketFinalSnapshotHash(string path)
        {
            try
            {
                if (!GlitchAiJsonFields.TryParseObject(File.ReadAllText(path), out IDictionary packet))
                    return null;
                IList frames = GetAiJsonList(packet, "frames");
                if (frames == null)
                    return null;
                for (int index = frames.Count - 1; index >= 0; index--)
                {
                    IDictionary frame = frames[index] as IDictionary;
                    string snapshotHash = GetAiJsonString(GetAiJsonObject(frame, "market_snapshot"), "snapshot_hash");
                    if (!string.IsNullOrWhiteSpace(snapshotHash))
                        return snapshotHash;
                }
            }
            catch
            {
            }
            return null;
        }

        private List<AiSnapshotPreview> GetAiSnapshotPreviews(FileInfo packetFile)
        {
            if (packetFile == null || !packetFile.Exists)
                return new List<AiSnapshotPreview>();
            if (string.Equals(_aiSnapshotPreviewCachePath, packetFile.FullName, StringComparison.OrdinalIgnoreCase)
                && _aiSnapshotPreviewCacheWriteUtc == packetFile.LastWriteTimeUtc)
                return _aiSnapshotPreviewCache;

            _aiSnapshotPreviewCachePath = packetFile.FullName;
            _aiSnapshotPreviewCacheWriteUtc = packetFile.LastWriteTimeUtc;
            _aiSnapshotPreviewCache = ReadAiSnapshotPreviews(packetFile.FullName);
            return _aiSnapshotPreviewCache;
        }

        private static List<AiSnapshotPreview> ReadAiSnapshotPreviews(string path)
        {
            var results = new List<AiSnapshotPreview>();
            try
            {
                if (!GlitchAiJsonFields.TryParseObject(File.ReadAllText(path), out IDictionary packet))
                    return results;
                IList frames = GetAiJsonList(packet, "frames");
                if (frames == null)
                    return results;

                foreach (object frameValue in frames)
                {
                    IDictionary frame = frameValue as IDictionary;
                    IDictionary market = GetAiJsonObject(frame, "market_snapshot");
                    IList instruments = GetAiJsonList(market, "instruments");
                    IDictionary mnq = instruments?.Cast<object>()
                        .Select(value => value as IDictionary)
                        .FirstOrDefault(value => string.Equals(GetAiJsonString(value, "instrument"), "MNQ", StringComparison.OrdinalIgnoreCase));
                    if (mnq == null)
                        continue;

                    IDictionary oneMinute = GetAiJsonList(mnq, "timeframe_bars")?.Cast<object>()
                        .Select(value => value as IDictionary)
                        .FirstOrDefault(value => Math.Abs((GetAiJsonNumber(value, "minutes") ?? 0) - 1) < 0.01);
                    IDictionary analytics = GetAiJsonObject(oneMinute, "derived_analytics");
                    IDictionary indicators = GetAiJsonObject(oneMinute, "indicators");
                    results.Add(new AiSnapshotPreview
                    {
                        MinuteId = GetAiJsonString(frame, "minute_id"),
                        CapturedUtc = ParseAiUtc(GetAiJsonString(frame, "captured_utc")),
                        Price = GetAiJsonNumber(mnq, "current_price"),
                        DirectionalScore = GetAiJsonNumber(analytics, "directional_score"),
                        TradeabilityScore = GetAiJsonNumber(analytics, "tradeability_score"),
                        Rsi = GetAiJsonNumber(indicators, "rsi"),
                        Atr = GetAiJsonNumber(indicators, "atr")
                    });
                }
            }
            catch
            {
                return new List<AiSnapshotPreview>();
            }
            return results;
        }

        private static object GetAiJsonValue(IDictionary value, string key)
        {
            return value != null && value.Contains(key) ? value[key] : null;
        }

        private static IDictionary GetAiJsonObject(IDictionary value, string key)
        {
            return GetAiJsonValue(value, key) as IDictionary;
        }

        private static IList GetAiJsonList(IDictionary value, string key)
        {
            return GetAiJsonValue(value, key) as IList;
        }

        private static string GetAiJsonString(IDictionary value, string key)
        {
            object raw = GetAiJsonValue(value, key);
            return raw == null ? null : Convert.ToString(raw, CultureInfo.InvariantCulture);
        }

        private static double? GetAiJsonNumber(IDictionary value, string key)
        {
            object raw = GetAiJsonValue(value, key);
            if (raw == null)
                return null;
            try { return Convert.ToDouble(raw, CultureInfo.InvariantCulture); }
            catch { return null; }
        }

        private static DateTime? ParseAiUtc(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            return DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out DateTime parsed)
                ? parsed.ToUniversalTime()
                : (DateTime?)null;
        }

        private static List<string> ReadLastNonEmptyLines(string path, int limit)
        {
            var lines = new Queue<string>();
            if (!File.Exists(path) || limit <= 0)
                return new List<string>();
            foreach (string line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                lines.Enqueue(line);
                while (lines.Count > limit)
                    lines.Dequeue();
            }
            return lines.ToList();
        }

        private static int CountFramesAfter(string directory, DateTime anchorUtc)
        {
            if (!Directory.Exists(directory))
                return 0;
            return Math.Min(5, new DirectoryInfo(directory).GetFiles("*.json")
                .Count(file => file.LastWriteTimeUtc > anchorUtc));
        }

        private static string FormatDecisionTimestamp(DateTime? value)
        {
            return value.HasValue
                ? value.Value.ToLocalTime().ToString("MM-dd HH:mm:ss", CultureInfo.CurrentCulture)
                : "-";
        }

        private static string FormatOptionalJsonNumber(string json, string key)
        {
            return GlitchAiJsonFields.TryExtractNumber(json, key, out double value)
                ? value.ToString("0.##", CultureInfo.InvariantCulture)
                : "-";
        }

        private static string BuildAiProtectionSummary(string decision)
        {
            string stop = FormatOptionalJsonNumber(decision, "stop_loss");
            string target1 = FormatOptionalJsonNumber(decision, "take_profit_1");
            string target2 = FormatOptionalJsonNumber(decision, "take_profit_2");
            string target3 = FormatOptionalJsonNumber(decision, "take_profit_3");
            if (stop == "-" && target1 == "-")
                return "-";
            var targets = new[] { target1, target2, target3 }.Where(value => value != "-").ToArray();
            return "SL " + stop + (targets.Length == 0 ? string.Empty : " | TP " + string.Join(" / ", targets));
        }

        private static string FormatOptionalNumber(double? value, string format)
        {
            return value.HasValue ? value.Value.ToString(format, CultureInfo.InvariantCulture) : "-";
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

        private static string FormatJsonNumber(string json, string key)
        {
            return GlitchAiJsonFields.TryExtractNumber(json, key, out double value) ? value.ToString("0.00") : "—";
        }

        private string FormatAge(TimeSpan age)
        {
            if (age.TotalSeconds < 0) age = TimeSpan.Zero;
            if (age.TotalMinutes < 1)
                return Lf("ai.age.seconds_format", "{0}s", Math.Max(0, (int)age.TotalSeconds));
            if (age.TotalHours < 1)
                return Lf("ai.age.minutes_format", "{0}m", (int)age.TotalMinutes);
            return Lf("ai.age.hours_format", "{0}h", (int)age.TotalHours);
        }

        private static string ResolveAiAccountType(AccountGridRow row, string accountName)
        {
            if (row != null && !string.IsNullOrWhiteSpace(row.AccountStatus))
                return string.Equals(row.AccountStatus, "AP", StringComparison.OrdinalIgnoreCase) ? "PA" : row.AccountStatus.ToUpperInvariant();
            return accountName != null && accountName.StartsWith("Sim", StringComparison.OrdinalIgnoreCase) ? "SIM" : "LIVE";
        }
    }
}
