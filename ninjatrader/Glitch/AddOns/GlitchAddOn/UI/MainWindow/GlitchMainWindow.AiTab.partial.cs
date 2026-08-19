using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
        private const int AiDecisionHistoryScanLimit = 2000;

        private sealed class AiDecisionFeedItem
        {
            public string DecisionJson { get; set; }
            public string ExecutionJson { get; set; }
            public string IntentId { get; set; }
            public DateTime? DecisionUtc { get; set; }
            public FileInfo PacketFile { get; set; }
            public List<AiSnapshotPreview> Snapshots { get; set; } = new List<AiSnapshotPreview>();
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

        private sealed class AiTabRefreshSnapshot
        {
            public DateTime CapturedUtc { get; set; }
            public bool TradingPaused { get; set; }
            public bool AiAutoOn { get; set; }
            public HashSet<string> EnabledMasters { get; set; }
            public FileInfo LatestFrame { get; set; }
            public List<AiDecisionFeedItem> History { get; set; }
            public GlitchAiHealthSnapshot Health { get; set; }
            public int CurrentFrames { get; set; }
            public DateTime DecisionsWriteUtc { get; set; }
            public DateTime ExecutionsWriteUtc { get; set; }
        }

        private readonly HashSet<string> _expandedAiDecisionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private List<AiDecisionFeedItem> _aiDecisionHistoryCache = new List<AiDecisionFeedItem>();
        private DateTime _aiDecisionHistoryDecisionWriteUtc;
        private DateTime _aiDecisionHistoryExecutionWriteUtc;
        private string _aiDecisionHistoryPacketFingerprint;
        private string _aiFeedRenderFingerprint;
        private string _aiScopeRenderFingerprint;
        private int _aiTabRefreshInFlight;
        private DateTime _lastAiTabRefreshQueuedUtc = DateTime.MinValue;
        private static readonly TimeSpan AiTabRefreshMinInterval = TimeSpan.FromSeconds(2);

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
                _aiScopeRenderFingerprint = null;
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

        private async void RefreshAiTab()
        {
            if (_isWindowClosed || _aiScopeRowsHost == null || _aiFeedHost == null)
                return;
            DateTime queuedUtc = DateTime.UtcNow;
            if (_lastAiTabRefreshQueuedUtc != DateTime.MinValue
                && (queuedUtc - _lastAiTabRefreshQueuedUtc) < AiTabRefreshMinInterval)
                return;
            if (Interlocked.CompareExchange(ref _aiTabRefreshInFlight, 1, 0) != 0)
                return;
            _lastAiTabRefreshQueuedUtc = queuedUtc;

            try
            {
                AiTabRefreshSnapshot snapshot = await Task.Run(BuildAiTabRefreshSnapshot);
                if (_isWindowClosed || snapshot == null || _aiScopeRowsHost == null || _aiFeedHost == null)
                    return;
                ApplyAiTabRefreshSnapshot(snapshot);
            }
            catch (Exception error)
            {
                if (!_isWindowClosed)
                    RecordSubsystemFault("ai_tab_refresh", error);
            }
            finally
            {
                Interlocked.Exchange(ref _aiTabRefreshInFlight, 0);
            }
        }

        private AiTabRefreshSnapshot BuildAiTabRefreshSnapshot()
        {
            DateTime nowUtc = DateTime.UtcNow;
            string exchangeRoot = GlitchStateStore.GetDefaultPath(Path.Combine("hermes", "exchange"));
            string minuteRoot = Path.Combine(exchangeRoot, "glitch", "minute-frames");
            string packetRoot = Path.Combine(exchangeRoot, "glitch", "decision-packets");
            string outboxRoot = Path.Combine(exchangeRoot, "hermes", "outbox");
            string decisionsPath = GlitchStateStore.GetDefaultPath(Path.Combine("intents", "decisions.jsonl"));
            string executionsPath = GlitchStateStore.GetDefaultPath(Path.Combine("intents", "executions.jsonl"));
            FileInfo latestFrame = GetNewestFile(minuteRoot, "*.json");
            List<AiDecisionFeedItem> history = LoadAiDecisionHistory(
                decisionsPath,
                executionsPath,
                packetRoot,
                outboxRoot);
            AiDecisionFeedItem latest = history.FirstOrDefault();
            GlitchAiHealthSnapshot health = GlitchAiHealthEvaluator.Evaluate(nowUtc);
            GlitchHermesControlState controlState = GlitchHermesControlStateStore.Load();
            GlitchAiRailPolicy policy = GlitchAiRailPolicyStore.Load();
            bool aiAutoOn = !controlState.TradingPaused && health.TradingJobEnabled;
            DateTime frameAnchorUtc = latest?.DecisionUtc ?? nowUtc.AddMinutes(-5);

            return new AiTabRefreshSnapshot
            {
                CapturedUtc = nowUtc,
                TradingPaused = controlState.TradingPaused,
                AiAutoOn = aiAutoOn,
                EnabledMasters = new HashSet<string>(
                    policy?.ProfileAccountBindings?.Values ?? Enumerable.Empty<string>(),
                    StringComparer.OrdinalIgnoreCase),
                LatestFrame = latestFrame,
                History = history,
                Health = health,
                CurrentFrames = CountFramesAfter(minuteRoot, frameAnchorUtc),
                DecisionsWriteUtc = File.Exists(decisionsPath)
                    ? File.GetLastWriteTimeUtc(decisionsPath)
                    : DateTime.MinValue,
                ExecutionsWriteUtc = File.Exists(executionsPath)
                    ? File.GetLastWriteTimeUtc(executionsPath)
                    : DateTime.MinValue
            };
        }

        private void ApplyAiTabRefreshSnapshot(AiTabRefreshSnapshot snapshot)
        {
            DateTime nowUtc = snapshot.CapturedUtc;
            FileInfo latestFrame = snapshot.LatestFrame;
            List<AiDecisionFeedItem> history = snapshot.History ?? new List<AiDecisionFeedItem>();
            AiDecisionFeedItem latest = history.FirstOrDefault();
            _expandedAiDecisionIds.IntersectWith(history
                .Where(value => !string.IsNullOrWhiteSpace(value.IntentId))
                .Select(value => value.IntentId));
            GlitchAiHealthSnapshot health = snapshot.Health ?? new GlitchAiHealthSnapshot
            {
                OverallStatus = "degraded",
                ReasonCodes = new List<string> { "health_unavailable" }
            };
            UpdateHermesModeUi(snapshot.TradingPaused);
            RefreshAiScopeRows(snapshot.EnabledMasters);

            string snapshotAge = latestFrame == null
                ? L("ai.value.none", "none")
                : Lf("ai.age.ago_format", "{0} ago", FormatAge(nowUtc - latestFrame.LastWriteTimeUtc));
            string decisionAge = latest?.DecisionUtc == null
                ? L("ai.value.none", "none")
                : Lf("ai.age.ago_format", "{0} ago", FormatAge(nowUtc - latest.DecisionUtc.Value));
            string healthLabel = string.Equals(health.OverallStatus, "on", StringComparison.Ordinal)
                ? L("ai.health.on", "On")
                : string.Equals(health.OverallStatus, "off", StringComparison.Ordinal)
                    ? L("ai.health.off", "Off")
                    : L("ai.health.degraded", "Degraded");
            string healthReason = health.ReasonCodes.Count > 0
                ? health.ReasonCodes[0]
                : L("ai.health.operating", "operating");
            if (health.ReasonCodes.Count == 0 && health.LearningReasonCodes.Count > 0)
                healthReason += " | learning: " + health.LearningReasonCodes[0];
            _aiFeedStatusText.Text = Lf(
                "ai.feed.health_status_format",
                "AI {0}: {1}  |  Latest snapshot {2}  |  Latest decision {3}",
                healthLabel,
                healthReason,
                snapshotAge,
                decisionAge);

            bool aiAutoOn = snapshot.AiAutoOn;
            int currentFrames = snapshot.CurrentFrames;
            string renderFingerprint = string.Join(
                "|",
                aiAutoOn ? "1" : "0",
                latestFrame?.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture) ?? "0",
                snapshot.DecisionsWriteUtc.Ticks.ToString(CultureInfo.InvariantCulture),
                snapshot.ExecutionsWriteUtc.Ticks.ToString(CultureInfo.InvariantCulture),
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
                nowUtc,
                health));

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

        private void RefreshAiScopeRows(HashSet<string> enabledMasters)
        {
            enabledMasters = enabledMasters ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string scopeFingerprint = BuildAiScopeFingerprint(enabledMasters);
            if (string.Equals(scopeFingerprint, _aiScopeRenderFingerprint, StringComparison.Ordinal))
                return;
            _aiScopeRenderFingerprint = scopeFingerprint;
            _aiScopeRowsHost.Children.Clear();

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

        private string BuildAiScopeFingerprint(HashSet<string> enabledMasters)
        {
            var parts = new List<string>();
            parts.AddRange(enabledMasters.OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .Select(value => "E:" + value));
            foreach (AccountGroupDefinition group in _accountGroups
                .Where(value => value != null && !string.IsNullOrWhiteSpace(value.MasterAccount))
                .OrderBy(value => value.MasterAccount, StringComparer.OrdinalIgnoreCase))
            {
                parts.Add("M:" + group.MasterAccount + ":" + ResolveAiAccountType(
                    _accountRows.FirstOrDefault(value => string.Equals(
                        value.DisplayName,
                        group.MasterAccount,
                        StringComparison.OrdinalIgnoreCase)),
                    group.MasterAccount));
                foreach (AccountGroupMemberRow member in (group.Members ?? new System.Collections.ObjectModel.ObservableCollection<AccountGroupMemberRow>())
                    .Where(value => value != null && !value.IsMasterRow)
                    .OrderBy(value => value.FollowerAccount, StringComparer.OrdinalIgnoreCase))
                {
                    parts.Add("F:" + member.FollowerAccount + ":" + (member.IsEnabled ? "1" : "0")
                        + ":" + member.Ratio.ToString("R", CultureInfo.InvariantCulture));
                }
            }
            return string.Join("|", parts);
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

            GlitchAiRailPolicy persistedPolicy = GlitchAiRailPolicyStore.Load();
            var persistedMasters = new HashSet<string>(
                persistedPolicy?.ProfileAccountBindings?.Values ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
            _aiScopeRenderFingerprint = null;
            RefreshAiScopeRows(persistedMasters);
            if (refresh)
                RefreshAiTab();
        }

        private UIElement CreateAiCurrentWindowPanel(
            int frameCount,
            DateTime? latestDecisionUtc,
            bool aiAutoOn,
            DateTime nowUtc,
            GlitchAiHealthSnapshot health)
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

            bool decisionWorkerUnhealthy = IsAiDecisionWorkerUnhealthy(health);
            string cadence = DescribeAiDecisionCadence(aiAutoOn, latestDecisionUtc, nowUtc, health);
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
                Foreground = aiAutoOn && decisionWorkerUnhealthy
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

        private string DescribeAiDecisionCadence(
            bool aiAutoOn,
            DateTime? latestDecisionUtc,
            DateTime nowUtc,
            GlitchAiHealthSnapshot health)
        {
            if (!aiAutoOn)
                return L("ai.cadence.paused", "Scheduled calls are paused");
            if (!latestDecisionUtc.HasValue)
                return L("ai.status.waiting_first", "Waiting for the first completed decision");
            if (health != null
                && string.Equals(health.DecisionWorkerStatus, "started", StringComparison.Ordinal)
                && health.DecisionAttemptAgeSeconds >= 0
                && health.DecisionAttemptAgeSeconds <= 360)
            {
                return Lf(
                    "ai.cadence.in_progress_format",
                    "Decision in progress ({0}s)",
                    Math.Max(1, (int)Math.Round(health.DecisionAttemptAgeSeconds)));
            }
            if (IsAiDecisionWorkerUnhealthy(health))
                return L("ai.cadence.overdue", "Decision overdue - inspect the background worker");

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
            return L("ai.cadence.due", "Decision due; waiting for the completed result");
        }

        private static bool IsAiDecisionWorkerUnhealthy(GlitchAiHealthSnapshot health)
        {
            return health != null
                && health.ReasonCodes.Any(code =>
                    code != null
                    && code.StartsWith("decision_worker_", StringComparison.Ordinal));
        }

        private void AddLatestAiDecision(AiDecisionFeedItem item)
        {
            string decision = item.DecisionJson ?? string.Empty;
            string execution = item.ExecutionJson ?? string.Empty;
            string action = GlitchAiJsonFields.ExtractString(decision, "action") ?? L("ai.value.waiting", "Waiting");
            string decisionStatus = GlitchAiJsonFields.ExtractString(decision, "status") ?? "waiting";
            string executionStatus = GlitchAiJsonFields.ExtractString(execution, "status") ?? "waiting";
            string executionCode = DescribeAiExecutionState(execution);
            List<AiSnapshotPreview> snapshots = item.Snapshots ?? new List<AiSnapshotPreview>();
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
                L("ai.field.cognition", "Cognition"), GlitchAiJsonFields.ExtractString(decision, "prompt_version") ?? "-",
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
            string executionCode = DescribeAiExecutionState(item.ExecutionJson);

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
            body.Children.Add(CreateAiSnapshotTable(item.Snapshots));
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

        private List<AiDecisionFeedItem> LoadAiDecisionHistory(
            string decisionsPath,
            string executionsPath,
            string packetRoot,
            string outboxRoot)
        {
            DateTime decisionsWriteUtc = File.Exists(decisionsPath) ? File.GetLastWriteTimeUtc(decisionsPath) : DateTime.MinValue;
            DateTime executionsWriteUtc = File.Exists(executionsPath) ? File.GetLastWriteTimeUtc(executionsPath) : DateTime.MinValue;
            if (_aiDecisionHistoryCache.Count > 0
                && decisionsWriteUtc == _aiDecisionHistoryDecisionWriteUtc
                && executionsWriteUtc == _aiDecisionHistoryExecutionWriteUtc)
                return _aiDecisionHistoryCache;

            List<string> decisions = CoalesceAiDecisionHistoryLines(
                ReadLastNonEmptyLines(decisionsPath, AiDecisionHistoryScanLimit),
                AiDecisionHistoryLimit);
            var intentIds = new HashSet<string>(
                decisions.Select(line => GlitchAiJsonFields.ExtractString(line, "intent_id"))
                    .Where(value => !string.IsNullOrWhiteSpace(value)),
                StringComparer.OrdinalIgnoreCase);
            var executionsByIntent = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(executionsPath) && intentIds.Count > 0)
            {
                foreach (string line in ReadLastNonEmptyLines(executionsPath, 2000))
                {
                    string intentId = GlitchAiJsonFields.ExtractString(line, "intent_id");
                    if (!string.IsNullOrWhiteSpace(intentId) && intentIds.Contains(intentId))
                    {
                        if (!executionsByIntent.TryGetValue(intentId, out string prior)
                            || AiExecutionEvidencePriority(line) >= AiExecutionEvidencePriority(prior))
                            executionsByIntent[intentId] = line;
                    }
                }
            }

            var items = new List<AiDecisionFeedItem>();
            var snapshotCache = new Dictionary<string, List<AiSnapshotPreview>>(StringComparer.OrdinalIgnoreCase);
            foreach (string decision in decisions.AsEnumerable().Reverse())
            {
                string intentId = GlitchAiJsonFields.ExtractString(decision, "intent_id") ?? string.Empty;
                DateTime? decisionUtc = GlitchAiJsonFields.TryExtractUtc(decision, "created_utc");
                string snapshotHash = GlitchAiJsonFields.ExtractString(decision, "snapshot_hash") ?? string.Empty;
                FileInfo packet = FindAiDecisionPacket(
                    packetRoot,
                    outboxRoot,
                    intentId,
                    snapshotHash,
                    decisionUtc);
                List<AiSnapshotPreview> snapshots = new List<AiSnapshotPreview>();
                if (packet != null)
                {
                    if (!snapshotCache.TryGetValue(packet.FullName, out snapshots))
                    {
                        snapshots = ReadAiSnapshotPreviews(packet.FullName);
                        snapshotCache[packet.FullName] = snapshots;
                    }
                }
                executionsByIntent.TryGetValue(intentId, out string execution);
                items.Add(new AiDecisionFeedItem
                {
                    DecisionJson = decision,
                    ExecutionJson = execution ?? string.Empty,
                    IntentId = intentId,
                    DecisionUtc = decisionUtc,
                    PacketFile = packet,
                    Snapshots = snapshots
                });
            }

            _aiDecisionHistoryDecisionWriteUtc = decisionsWriteUtc;
            _aiDecisionHistoryExecutionWriteUtc = executionsWriteUtc;
            _aiDecisionHistoryPacketFingerprint = string.Join(
                ",",
                items.Where(value => value.PacketFile != null)
                    .Select(value => value.PacketFile.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase));
            _aiDecisionHistoryCache = items;
            return _aiDecisionHistoryCache;
        }

        private static List<string> CoalesceAiDecisionHistoryLines(
            IEnumerable<string> source,
            int limit)
        {
            var retained = new LinkedList<string>();
            var lastActionByScope = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var latestHoldByScope = new Dictionary<string, LinkedListNode<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in source ?? Enumerable.Empty<string>())
            {
                string action = GlitchAiJsonFields.ExtractString(line, "action") ?? string.Empty;
                string account = GlitchAiJsonFields.ExtractString(line, "account") ?? string.Empty;
                string instrument = GlitchAiJsonFields.ExtractString(line, "instrument") ?? string.Empty;
                string scope = account + "|" + instrument;
                if (string.Equals(action, "HOLD", StringComparison.OrdinalIgnoreCase)
                    && lastActionByScope.TryGetValue(scope, out string priorAction)
                    && string.Equals(priorAction, "HOLD", StringComparison.OrdinalIgnoreCase)
                    && latestHoldByScope.TryGetValue(scope, out LinkedListNode<string> priorHold))
                {
                    retained.Remove(priorHold);
                }

                LinkedListNode<string> node = retained.AddLast(line);
                lastActionByScope[scope] = action;
                if (string.Equals(action, "HOLD", StringComparison.OrdinalIgnoreCase))
                    latestHoldByScope[scope] = node;
                else
                    latestHoldByScope.Remove(scope);
            }
            return TakeLastCompat(retained, limit).ToList();
        }

        private static string DescribeAiExecutionState(string executionJson)
        {
            string code = GlitchAiJsonFields.ExtractString(executionJson, "code");
            if (string.Equals(code, "intent_dispatched", StringComparison.OrdinalIgnoreCase))
                return "pending_native_result";
            return code
                ?? GlitchAiJsonFields.ExtractString(executionJson, "status")
                ?? "waiting";
        }

        private static int AiExecutionEvidencePriority(string executionJson)
        {
            string status = GlitchAiJsonFields.ExtractString(executionJson, "status") ?? string.Empty;
            string code = GlitchAiJsonFields.ExtractString(executionJson, "code") ?? string.Empty;
            if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase))
                return 4;
            if (code.EndsWith("_fill_observed", StringComparison.OrdinalIgnoreCase))
                return 3;
            if (string.Equals(status, "executed", StringComparison.OrdinalIgnoreCase))
                return 2;
            return 1;
        }

        private static FileInfo FindAiDecisionPacket(
            string packetRoot,
            string outboxRoot,
            string intentId,
            string snapshotHash,
            DateTime? decisionUtc)
        {
            if (!decisionUtc.HasValue || string.IsNullOrWhiteSpace(packetRoot))
                return null;

            DateTime anchorUtc = new DateTime(
                decisionUtc.Value.Year,
                decisionUtc.Value.Month,
                decisionUtc.Value.Day,
                decisionUtc.Value.Hour,
                decisionUtc.Value.Minute,
                0,
                DateTimeKind.Utc);
            for (int offset = 0; offset <= 30; offset++)
            {
                string cycleId = anchorUtc.AddMinutes(-offset)
                    .ToString("yyyyMMdd'T'HHmm'Z'", CultureInfo.InvariantCulture);
                string outboxPath = Path.Combine(outboxRoot, cycleId + ".json");
                if (!File.Exists(outboxPath))
                    continue;
                string outboxJson = ReadAllTextShared(outboxPath);
                if (string.IsNullOrWhiteSpace(outboxJson)
                    || string.IsNullOrWhiteSpace(intentId)
                    || outboxJson.IndexOf(
                        "\"intent_id\":" + GlitchSnapshotJson.String(intentId),
                        StringComparison.Ordinal) < 0)
                    continue;

                string packetPath = Path.Combine(packetRoot, cycleId + ".json");
                if (File.Exists(packetPath))
                    return new FileInfo(packetPath);
            }

            // Legacy records may predate cycle-linked outboxes. Inspect only the
            // bounded minute candidates nearest the decision rather than sorting
            // the complete, permanently growing packet directory.
            if (string.IsNullOrWhiteSpace(snapshotHash))
                return null;
            for (int offset = 0; offset <= 15; offset++)
            {
                string cycleId = anchorUtc.AddMinutes(-offset)
                    .ToString("yyyyMMdd'T'HHmm'Z'", CultureInfo.InvariantCulture);
                string packetPath = Path.Combine(packetRoot, cycleId + ".json");
                if (!File.Exists(packetPath))
                    continue;
                if (string.Equals(
                    ReadAiPacketFinalSnapshotHash(packetPath),
                    snapshotHash,
                    StringComparison.Ordinal))
                    return new FileInfo(packetPath);
            }
            return null;
        }

        private static string ReadAiPacketFinalSnapshotHash(string path)
        {
            try
            {
                if (!GlitchAiJsonFields.TryParseObject(ReadAllTextShared(path), out IDictionary packet))
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

        private static List<AiSnapshotPreview> ReadAiSnapshotPreviews(string path)
        {
            var results = new List<AiSnapshotPreview>();
            try
            {
                if (!GlitchAiJsonFields.TryParseObject(ReadAllTextShared(path), out IDictionary packet))
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
            if (!File.Exists(path) || limit <= 0)
                return new List<string>();

            const int blockSize = 64 * 1024;
            const int maxTailBytes = 16 * 1024 * 1024;
            try
            {
                using (var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                {
                    long cursor = stream.Length;
                    int newlineCount = 0;
                    int capturedBytes = 0;
                    var chunks = new List<byte[]>();
                    while (cursor > 0 && newlineCount <= limit && capturedBytes < maxTailBytes)
                    {
                        int count = (int)Math.Min(blockSize, cursor);
                        cursor -= count;
                        stream.Position = cursor;
                        var chunk = new byte[count];
                        int read = 0;
                        while (read < count)
                        {
                            int next = stream.Read(chunk, read, count - read);
                            if (next <= 0)
                                break;
                            read += next;
                        }
                        if (read != count)
                            Array.Resize(ref chunk, read);
                        for (int index = 0; index < chunk.Length; index++)
                        {
                            if (chunk[index] == (byte)'\n')
                                newlineCount++;
                        }
                        chunks.Add(chunk);
                        capturedBytes += chunk.Length;
                    }

                    chunks.Reverse();
                    var tail = new byte[capturedBytes];
                    int destination = 0;
                    foreach (byte[] chunk in chunks)
                    {
                        Buffer.BlockCopy(chunk, 0, tail, destination, chunk.Length);
                        destination += chunk.Length;
                    }
                    int start = 0;
                    if (cursor > 0)
                    {
                        while (start < tail.Length && tail[start] != (byte)'\n')
                            start++;
                        if (start < tail.Length)
                            start++;
                    }
                    string text = Encoding.UTF8.GetString(tail, start, tail.Length - start);
                    return TakeLastCompat(
                        text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(value => value.TrimEnd('\r'))
                            .Where(value => !string.IsNullOrWhiteSpace(value)),
                        limit).ToList();
                }
            }
            catch
            {
                return new List<string>();
            }
        }

        private static IEnumerable<string> TakeLastCompat(IEnumerable<string> source, int limit)
        {
            var queue = new Queue<string>();
            foreach (string value in source ?? Enumerable.Empty<string>())
            {
                queue.Enqueue(value);
                while (queue.Count > limit)
                    queue.Dequeue();
            }
            return queue;
        }

        private static string ReadAllTextShared(string path)
        {
            try
            {
                using (var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                    return reader.ReadToEnd();
            }
            catch
            {
                return string.Empty;
            }
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
