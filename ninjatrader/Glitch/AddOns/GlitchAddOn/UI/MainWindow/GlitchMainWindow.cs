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
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using LinqExpression = System.Linq.Expressions.Expression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Glitch.Services;
using NinjaTrader.Cbi;
using NinjaTrader.Gui.Tools;

namespace Glitch.UI
{
    /// <summary>
    /// Main window root: shared state, lifecycle, and cross-tab wiring.
    /// Concern-specific behavior lives in the sibling partials under UI/MainWindow.
    /// </summary>
    public partial class GlitchMainWindow : NTWindow, IWorkspacePersistence
    {
        private static readonly SolidColorBrush TealAccentBrush = new SolidColorBrush(Color.FromRgb(26, 188, 156));   // #1ABC9C
        private static readonly SolidColorBrush OrangeAccentBrush = new SolidColorBrush(Color.FromRgb(255, 66, 0));  // #FF4200
        private static readonly FontWeight UiHeadingFontWeight = FontWeights.Medium;
        private static readonly FontWeight UiActionFontWeight = FontWeights.Medium;
        private static readonly FontWeight UiTabFontWeight = FontWeights.Medium;
        private static readonly Brush UiPrimaryTextBrush = Brushes.White;
        private const string ReplicationSignalName = "GLT-SYNC";
        private const string ProtectiveStopSignalName = "GLT-PROT-STP";
        private const string ProtectiveTargetSignalName = "GLT-PROT-TGT";
        private const string RiskFlattenUnrealizedSignalName = "GLT-RISK-FLAT-UNRLZ";
        private const string RiskFlattenBufferSignalName = "GLT-RISK-FLAT-BUFFER";
        private const string RiskFlattenMaxQtySignalName = "GLT-RISK-FLAT-MAXQTY";
        private const string RiskFlattenNakedSignalName = "GLT-RISK-FLAT-NAKED";
        private const string RiskFlattenDailyLimitSignalName = "GLT-RISK-FLAT-DAILYLIMIT";
        private const string CurrentClientVersion = "addon-0.0.1.2";
        private const string DefaultLatestDownloadUrl = "https://download.glitchtrader.com/latest";
        private const double UnrealizedLossFlattenThresholdRatio = 0.80;
        private const double BufferCriticalLockThresholdRatio = 0.15;
        private const double BufferOneContractThresholdRatio = 0.20;
        private const double BufferOneContractReleaseThresholdRatio = 0.25;
        private const double DefaultMicroContractMultiplier = 10.0;
        private const string DefaultMicroContractRootRegex = "^M[A-Z0-9]+$";
        private const int MaxMicroContractRegexLength = 128;
        private const int RiskFlattenConfirmationTimeoutMs = 500;
        private const int RiskFlattenConfirmationPollMs = 25;
        private static readonly TimeSpan RiskMitigationCooldown = TimeSpan.FromSeconds(4);
        private static readonly TimeSpan ReplicationStartupWarmup = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan InformationalWarningJournalCooldown = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan StrategySourceSnapshotTtl = TimeSpan.FromHours(12);
        private static readonly TimeSpan MicroContractRegexTimeout = TimeSpan.FromMilliseconds(75);

        private readonly ObservableCollection<AccountGridRow> _accountRows;
        private readonly ObservableCollection<JournalEntry> _journalEntries;
        private readonly ObservableCollection<CriticalWarningEntry> _criticalWarningEntries;
        private readonly Dictionary<string, AccountSelectionOverride> _selectionOverrides;
        private readonly Dictionary<string, FirmRuleMetadata> _firmRules;
        private readonly Dictionary<string, string> _firmIdToDisplay;
        private readonly Dictionary<string, string> _firmDisplayToId;
        private readonly List<string> _propFirmDisplayOptions;
        private readonly List<string> _accountStatusOptions;
        private readonly List<double> _globalAccountSizeOptions;
        private readonly ObservableCollection<AccountGroupDefinition> _accountGroups;
        private readonly string _overridesFilePath;
        private readonly string _peakStateFilePath;
        private readonly string _accountGroupsFilePath;
        private readonly string _windowPlacementFilePath;
        private readonly string _journalFilePath;
        private readonly string _warningsFilePath;
        private readonly string _runtimePolicyFilePath;
        private readonly string _licenseCacheFilePath;
        private GlitchRuntimePolicySettings _runtimePolicySettings;
        private GlitchLicenseCacheState _licenseCacheState;
        private readonly DispatcherTimer _refreshTimer;
        private readonly Dictionary<string, PeakState> _peakStatesByAccount;
        private readonly Dictionary<string, List<EventBridgeSubscription>> _accountEventSubscriptions;
        private readonly object _peakStateLock = new object();
        private readonly Dictionary<string, DateTime> _replicationSubmitCooldownByKey;
        private readonly Dictionary<string, ReplicationPendingSubmitState> _replicationPendingSubmitByKey;
        private readonly object _replicationOrderLock = new object();
        private readonly Dictionary<string, DateTime> _protectiveSyncCooldownByKey;
        private readonly object _protectiveOrderLock = new object();
        private readonly HashSet<string> _riskLockedAccounts;
        private readonly HashSet<string> _evalTargetLockedAccounts;
        private readonly HashSet<string> _riskLockAcknowledgedAccounts;
        private readonly HashSet<string> _riskOneContractAccounts;
        private readonly HashSet<string> _unrealizedLossFlattenTriggeredAccounts;
        private readonly HashSet<string> _replicationFrozenKeys;
        private readonly Dictionary<string, ReplicationBurstState> _replicationBurstStateByKey;
        private readonly Dictionary<string, DateTime> _noProtectionDetectedSinceByKey;
        private readonly Dictionary<string, DateTime> _riskMitigationCooldownByKey;
        private readonly Dictionary<string, DateTime> _riskFlattenFallbackWarningCooldownByKey;
        private readonly object _riskFlattenFallbackWarningLock = new object();
        private readonly Dictionary<string, TradeSourceKind> _tradeSourceByAccountInstrument;
        private readonly Dictionary<string, DateTime> _tradeSourceObservedUtcByAccountInstrument;
        private readonly object _tradeSourceLock = new object();
        private readonly Dictionary<string, string> _lastOrderJournalSnapshotByKey;
        private readonly Dictionary<string, string> _lastPositionJournalSnapshotByKey;
        private readonly Dictionary<string, string> _lastExecutionJournalSnapshotByKey;
        private readonly Dictionary<string, DateTime> _runtimeJournalCooldownByKey;
        private readonly Dictionary<string, DateTime> _informationalWarningJournalCooldownByKey;
        private readonly int _replicationSubmitMaxAttempts;
        private readonly int _replicationSubmitCooldownMs;
        private readonly int _protectiveSyncCooldownMs;
        private readonly GlitchAnalyticsEngine _analyticsEngine;
        private readonly GlitchFundamentalAnalysisService _fundamentalAnalysisService;
        private readonly Dictionary<int, AnalyticsDialVisual> _analyticsDialVisuals;
        private readonly Dictionary<int, AnalyticsTimeframeMetricVisual> _analyticsTimeframeVisuals;
        private static readonly TimeSpan ReplicationUiRefreshInterval = TimeSpan.FromSeconds(3);
        private Button _flattenAllButton;
        private Button _replicateButton;
        private Grid _headerRootGrid;
        private Grid _headerMetricHostGrid;
        private WrapPanel _headerMetricBoxesPanel;
        private Grid _headerActionsGrid;
        private Border _headerNewsLockoutBanner;
        private TextBlock _headerNewsLockoutText;
        private ComboBox _analyticsInstrumentCombo;
        private StackPanel _accountGroupsHostPanel;
        private DataGrid _accountsGrid;
        private DataGrid _criticalWarningsGrid;
        private DataGrid _noticeWarningsGrid;
        private Expander _journalNoticeHistoryExpander;
        private ICollectionView _criticalWarningsView;
        private ICollectionView _noticeWarningsView;
        private Grid _dashboardRootGrid;
        private FrameworkElement _dashboardGroupsSection;
        private Grid _journalRootGrid;
        private Grid _journalTopGrid;
        private int _headerResponsiveMode = -1;
        private bool? _dashboardNarrowLayout;
        private bool? _journalNarrowLayout;
        private bool? _summaryNarrowLayout;
        private int _analyticsTopLayoutMode = -1;
        private bool? _analyticsBodyWideLayout;
        private bool? _analyticsTimeframeTwoColumnLayout;
        private bool _isApplyingHeaderResponsiveLayout;
        private bool _isApplyingDashboardResponsiveLayout;
        private bool _isApplyingJournalResponsiveLayout;
        private bool _isApplyingSummaryResponsiveLayout;
        private bool _isApplyingAnalyticsResponsiveLayout;
        private bool _isUpdatingAnalyticsInstrumentSelection;
        private bool _isEditingAccountsGrid;
        private bool _isCommittingAccountsGridEdit;
        private bool _isFlattenFeedbackActive;
        private bool _isFlattenAllInProgress;
        private bool _isReplicatingUi;
        private bool _restoreMaximizedOnLoad;
        private bool _hasPendingPeakStateWrite;
        private bool _hasPendingAuditWrite;
        private string _groupMasterOptionsSnapshot;
        private string _analyticsInstrumentOptionsSnapshot;
        private string _selectedAnalyticsInstrument;
        private bool _isLicenseCheckInFlight;
        private DateTime _nextLicenseHeartbeatUtc;
        private string _licenseDeviceFingerprintHash;
        private bool _isClientUpdateAvailable;
        private string _latestClientVersion;
        private string _latestDownloadUrl;
        private DateTime _lastPeakStateWriteUtc;
        private DateTime _lastAuditWriteUtc;
        private DateTime _lastUiRefreshUtc;
        private DateTime _replicationWarmupUntilUtc;
        private bool _hasLoggedStartupRuntimeSettings;
        private bool _isWindowClosed;
        private TextBlock _totalPnlValueText;
        private TextBlock _evalPnlValueText;
        private TextBlock _paPnlValueText;
        private TextBlock _evalHeadroomValueText;
        private TextBlock _paHeadroomValueText;
        private TextBlock _globalHeadroomValueText;
        private TextBlock _warningCountValueText;
        private TextBlock _analyticsCurrentPriceText;
        private TextBlock _analyticsOverallSignalText;
        private TextBlock _analyticsSessionText;
        private TextBlock _analyticsSessionRangeLowLabel;
        private TextBlock _analyticsSessionRangeHighLabel;
        private Canvas _analyticsSessionRangeCanvas;
        private Border _analyticsSessionRangeTrack;
        private Border _analyticsSessionRangeAvgMarker;
        private Border _analyticsSessionRangeCurrentMarker;
        private TextBlock _analyticsSessionRangeAvgText;
        private TextBlock _analyticsSessionRangeCurrentText;
        private double? _analyticsSessionRangeLowValue;
        private double? _analyticsSessionRangeAvgValue;
        private double? _analyticsSessionRangeCurrentValue;
        private double? _analyticsSessionRangeHighValue;
        private string _analyticsSessionRangeInstrumentRoot;
        private TextBlock _analyticsScoreSectionTitleText;
        private StackPanel _analyticsMag7ItemsHost;
        private TextBlock _analyticsEarningsAnalysisText;
        private TextBlock _analyticsOfficialNewsText;
        private TextBlock _analyticsFundamentalFootnoteText;
        private ListBox _analyticsLatestHeadlinesList;
        private Button _analyticsOpenDetachedWindowButton;
        private Grid _analyticsTopBarGrid;
        private TabControl _mainTabControl;
        private FrameworkElement _analyticsInstrumentHost;
        private FrameworkElement _analyticsRoot;
        private Border _analyticsSessionRangeCard;
        private Border _analyticsCurrentPriceCard;
        private Border _analyticsSignalCard;
        private Border _analyticsSessionCard;
        private Grid _analyticsBodyGrid;
        private Grid _analyticsTimeframeGrid;
        private Border _analyticsOneMinuteCard;
        private Border _analyticsFiveMinuteCard;
        private Border _analyticsFifteenMinuteCard;
        private Border _analyticsSixtyMinuteCard;
        private FrameworkElement _analyticsFundamentalCard;
        private NTWindow _analyticsDetachedWindow;

        private enum TradeSourceKind
        {
            Unknown = 0,
            Manual = 1,
            Strategy = 2
        }

        static GlitchMainWindow()
        {
            TealAccentBrush.Freeze();
            OrangeAccentBrush.Freeze();
        }

        public GlitchMainWindow()
        {
            Caption = "Glitch";
            Width = 1400;
            Height = 900;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            _accountRows = new ObservableCollection<AccountGridRow>();
            _journalEntries = new ObservableCollection<JournalEntry>();
            _criticalWarningEntries = new ObservableCollection<CriticalWarningEntry>();
            _selectionOverrides = new Dictionary<string, AccountSelectionOverride>(StringComparer.OrdinalIgnoreCase);
            _firmRules = LoadFirmRuleMetadata();
            _firmIdToDisplay = BuildFirmIdToDisplayMap(_firmRules);
            _firmDisplayToId = BuildFirmDisplayToIdMap(_firmIdToDisplay);
            _propFirmDisplayOptions = BuildPropFirmDisplayOptions(_firmRules, _firmIdToDisplay);
            _accountStatusOptions = new List<string> { "Sim", "Eval", "AP" };
            _globalAccountSizeOptions = BuildGlobalAccountSizeOptions(_firmRules);
            _accountGroups = new ObservableCollection<AccountGroupDefinition>();
            _overridesFilePath = GetOverridesFilePath();
            _peakStateFilePath = GetPeakStateFilePath();
            _accountGroupsFilePath = GetAccountGroupsFilePath();
            _windowPlacementFilePath = GetWindowPlacementFilePath();
            _journalFilePath = GetJournalFilePath();
            _warningsFilePath = GetWarningsFilePath();
            _runtimePolicyFilePath = GetRuntimePolicySettingsFilePath();
            _licenseCacheFilePath = GetLicenseCacheFilePath();
            GlitchRuntimePolicyStore.EnsureTemplatesExist(_runtimePolicyFilePath, _licenseCacheFilePath);
            _runtimePolicySettings = GlitchRuntimePolicyStore.LoadSettings(_runtimePolicyFilePath);
            _licenseCacheState = GlitchRuntimePolicyStore.LoadLicenseCache(_licenseCacheFilePath);
            _peakStatesByAccount = new Dictionary<string, PeakState>(StringComparer.OrdinalIgnoreCase);
            _accountEventSubscriptions = new Dictionary<string, List<EventBridgeSubscription>>(StringComparer.OrdinalIgnoreCase);
            _replicationSubmitCooldownByKey = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            _replicationPendingSubmitByKey = new Dictionary<string, ReplicationPendingSubmitState>(StringComparer.OrdinalIgnoreCase);
            _protectiveSyncCooldownByKey = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            _riskLockedAccounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _evalTargetLockedAccounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _riskLockAcknowledgedAccounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _riskOneContractAccounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _unrealizedLossFlattenTriggeredAccounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _replicationFrozenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _replicationBurstStateByKey = new Dictionary<string, ReplicationBurstState>(StringComparer.OrdinalIgnoreCase);
            _noProtectionDetectedSinceByKey = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            _riskMitigationCooldownByKey = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            _riskFlattenFallbackWarningCooldownByKey = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            _tradeSourceByAccountInstrument = new Dictionary<string, TradeSourceKind>(StringComparer.OrdinalIgnoreCase);
            _tradeSourceObservedUtcByAccountInstrument = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            _lastOrderJournalSnapshotByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _lastPositionJournalSnapshotByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _lastExecutionJournalSnapshotByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _runtimeJournalCooldownByKey = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            _informationalWarningJournalCooldownByKey = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            _replicationSubmitMaxAttempts = ResolveRuntimeIntSetting("GLITCH_REPLICATION_SUBMIT_MAX_ATTEMPTS", 2, 1, 5);
            _replicationSubmitCooldownMs = ResolveRuntimeIntSetting("GLITCH_REPLICATION_SUBMIT_COOLDOWN_MS", 300, 250, 10000);
            _protectiveSyncCooldownMs = ResolveRuntimeIntSetting("GLITCH_PROTECTIVE_SYNC_COOLDOWN_MS", 750, 100, 5000);
            _analyticsEngine = new GlitchAnalyticsEngine();
            _fundamentalAnalysisService = new GlitchFundamentalAnalysisService();
            _analyticsDialVisuals = new Dictionary<int, AnalyticsDialVisual>();
            _analyticsTimeframeVisuals = new Dictionary<int, AnalyticsTimeframeMetricVisual>();
            _lastUiRefreshUtc = DateTime.MinValue;
            _replicationWarmupUntilUtc = DateTime.MinValue;
            _nextLicenseHeartbeatUtc = DateTime.UtcNow;
            _licenseDeviceFingerprintHash = ComputeDeviceFingerprintHash();
            _isClientUpdateAvailable = false;
            _latestClientVersion = string.Empty;
            _latestDownloadUrl = DefaultLatestDownloadUrl;
            RehydrateLicenseStateFromSignedCache();
            GlitchRuntimePolicyStore.SaveLicenseCache(_licenseCacheFilePath, _licenseCacheState);
            LoadSelectionOverridesFromDisk(overwriteExisting: true);
            LoadPeakStatesFromDisk();
            LoadAccountGroupsFromDisk();
            LoadAuditFeedsFromDisk();
            RestoreWindowPlacementFromDisk();
            _refreshTimer = new DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _refreshTimer.Tick += OnRefreshTimerTick;
            GlitchShellBridge.RegisterMainWindow(this);

            Loaded += OnWindowLoaded;
            Closed += OnWindowClosed;

            Content = CreateMainLayout();
            PublishGlitchShellState();
        }

        public void Restore(System.Xml.Linq.XDocument document, System.Xml.Linq.XElement element)
        {
            _selectionOverrides.Clear();
            if (element != null)
            {
                var overridesNode = element.Element("AccountOverrides");
                if (overridesNode != null)
                {
                    foreach (var accountNode in overridesNode.Elements("Account"))
                    {
                        string accountName = (string)accountNode.Attribute("Name");
                        if (string.IsNullOrWhiteSpace(accountName))
                            continue;

                        string status = NormalizeAccountStatus((string)accountNode.Attribute("Status"));
                        string firmId = (string)accountNode.Attribute("FirmId");
                        string sizeRaw = (string)accountNode.Attribute("Size");
                        bool isManual = ParseBooleanToken((string)accountNode.Attribute("Manual"));
                        if (!isManual)
                            continue;

                        double? parsedSize = null;
                        if (!string.IsNullOrWhiteSpace(sizeRaw) &&
                            double.TryParse(sizeRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out double sizeValue) &&
                            sizeValue > 0)
                        {
                            parsedSize = sizeValue;
                        }

                        _selectionOverrides[accountName] = new AccountSelectionOverride
                        {
                            AccountStatus = status,
                            PropFirmId = string.IsNullOrWhiteSpace(firmId) ? "None" : firmId,
                            AccountSize = parsedSize,
                            IsManual = true
                        };
                    }
                }
            }

            LoadSelectionOverridesFromDisk(overwriteExisting: false);
            LoadAccountGroupsFromDisk();
            RebuildAccountGroupsUi();
        }

        public void Save(System.Xml.Linq.XDocument document, System.Xml.Linq.XElement element)
        {
            if (element == null)
                return;

            CaptureSelectionOverridesFromRows();

            element.Element("AccountOverrides")?.Remove();
            var overridesNode = new System.Xml.Linq.XElement("AccountOverrides");

            foreach (var kvp in _selectionOverrides.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(kvp.Key) || kvp.Value == null || !kvp.Value.IsManual)
                    continue;

                var accountNode = new System.Xml.Linq.XElement("Account");
                accountNode.SetAttributeValue("Name", kvp.Key);
                accountNode.SetAttributeValue("Status", NormalizeAccountStatus(kvp.Value.AccountStatus));
                accountNode.SetAttributeValue("FirmId", string.IsNullOrWhiteSpace(kvp.Value.PropFirmId) ? "None" : kvp.Value.PropFirmId);
                if (kvp.Value.AccountSize.HasValue && kvp.Value.AccountSize.Value > 0)
                    accountNode.SetAttributeValue("Size", kvp.Value.AccountSize.Value.ToString("F0", CultureInfo.InvariantCulture));
                accountNode.SetAttributeValue("Manual", true);

                overridesNode.Add(accountNode);
            }

            element.Add(overridesNode);
            SaveSelectionOverridesToDisk();
            SaveAccountGroupsToDisk();
            SavePeakStatesToDisk(force: true);
            SaveAuditFeedsToDisk(force: true);
        }
        public WorkspaceOptions WorkspaceOptions { get; set; }

        private UIElement CreateMainLayout()
        {
            EnsureLocalizationInitialized();

            var root = new Grid();
            root.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)] = CreateSkinAwareScrollBarStyle(root);
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var header = CreateHeaderBar();
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            var tabs = new TabControl
            {
                TabStripPlacement = Dock.Top,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch
            };
            _mainTabControl = tabs;
            tabs.FocusVisualStyle = null;
            ApplySkinResource(tabs, Control.BackgroundProperty, "BackgroundTextInput", "GridEntireBackground", "BackgroundMainWindow");
            ApplySkinResource(tabs, Control.ForegroundProperty, "FontControlBrush", "FontTableBrush");
            ApplySkinResource(tabs, Control.BorderBrushProperty, "BorderThinBrush", "TabControlBorderBrush");
            tabs.BorderThickness = new Thickness(1);
            tabs.ItemContainerStyle = CreateSkinAwareTabItemStyle(tabs);

            var dashboardTab = new TabItem
            {
                Content = CreateAccountsTab()
            };
            BindLocalizedHeader(dashboardTab, "tab.dashboard", "Dashboard");
            tabs.Items.Add(dashboardTab);

            var analyticsTab = new TabItem
            {
                Content = CreateAnalyticsTab()
            };
            BindLocalizedHeader(analyticsTab, "tab.analytics", "Analytics");
            tabs.Items.Add(analyticsTab);

            var journalTab = new TabItem
            {
                Content = CreateJournalTab()
            };
            BindLocalizedHeader(journalTab, "tab.journal", "Journal");
            tabs.Items.Add(journalTab);

            var settingsTab = new TabItem
            {
                Content = CreateSettingsTab()
            };
            BindLocalizedHeader(settingsTab, "tab.settings", "Settings");
            tabs.Items.Add(settingsTab);
            tabs.SelectedIndex = 0;
            tabs.SelectionChanged += OnMainTabSelectionChanged;

            Grid.SetRow(tabs, 1);
            root.Children.Add(tabs);

            UIElement languageSwitcher = CreateLanguageSwitcher(root);
            if (languageSwitcher != null)
            {
                Grid.SetRow(languageSwitcher, 1);
                Panel.SetZIndex(languageSwitcher, 2000);
                root.Children.Add(languageSwitcher);
            }

            ApplyLocalization();

            return root;
        }

        private UIElement CreateHeaderBar()
        {
            return CreateHeaderBarImpl();
        }

        private UIElement CreateSummaryTab()
        {
            return CreateSummaryTabImpl();
        }

        private UIElement CreateAnalyticsTab()
        {
            return CreateAnalyticsTabImpl();
        }

        private UIElement CreateJournalTab()
        {
            return CreateJournalTabImpl();
        }

        private UIElement CreateSettingsTab()
        {
            return CreateSettingsTabImpl();
        }

        // Keeps responsive breakpoints from oscillating when layout changes alter available width.
        private static bool ResolveBelowBreakpoint(double width, bool? currentBelowBreakpoint, double breakpoint, double hysteresis)
        {
            if (!currentBelowBreakpoint.HasValue)
                return width < breakpoint;

            if (currentBelowBreakpoint.Value)
                return width < breakpoint + hysteresis;

            return width < breakpoint - hysteresis;
        }

        private static bool ResolveAtOrAboveBreakpoint(double width, bool? currentAtOrAboveBreakpoint, double breakpoint, double hysteresis)
        {
            if (!currentAtOrAboveBreakpoint.HasValue)
                return width >= breakpoint;

            if (currentAtOrAboveBreakpoint.Value)
                return width >= breakpoint - hysteresis;

            return width >= breakpoint + hysteresis;
        }

        private static int ResolveResponsiveThreeBandMode(double width, int currentMode, double lowBreakpoint, double highBreakpoint, double hysteresis)
        {
            if (currentMode < 0)
            {
                if (width >= highBreakpoint)
                    return 0;
                if (width >= lowBreakpoint)
                    return 1;
                return 2;
            }

            if (currentMode == 0)
                return width < highBreakpoint - hysteresis ? 1 : 0;

            if (currentMode == 1)
            {
                if (width >= highBreakpoint + hysteresis)
                    return 0;
                if (width < lowBreakpoint - hysteresis)
                    return 2;
                return 1;
            }

            return width >= lowBreakpoint + hysteresis ? 1 : 2;
        }

        private Style CreateReplicateButtonStyle(FrameworkElement context)
        {
            var baseStyle = FindSkinStyle(context, typeof(Button));
            var style = new Style(typeof(Button), baseStyle);

            ApplySkinSetter(style, Control.BackgroundProperty, context, "BackgroundTextInput", "BackgroundMainWindow", "GridEntireBackground");
            ApplySkinSetter(style, Control.BorderBrushProperty, context, "BorderThinBrush", "TabControlBorderBrush");

            double? sharedFontSize = FindSkinDouble(context, "FontTableHeight", "FontControlHeight", "FontHeaderLevel4Height");
            if (sharedFontSize.HasValue)
                style.Setters.Add(new Setter(Control.FontSizeProperty, sharedFontSize.Value));

            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(14, 6, 14, 6)));
            style.Setters.Add(new Setter(Control.ForegroundProperty, UiPrimaryTextBrush));
            style.Setters.Add(new Setter(Control.FontWeightProperty, UiActionFontWeight));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(Control.CursorProperty, System.Windows.Input.Cursors.Hand));
            style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));

            var stoppedTrigger = new Trigger { Property = FrameworkElement.TagProperty, Value = "Stopped" };
            stoppedTrigger.Setters.Add(new Setter(ContentControl.ContentProperty, L("header.button.replicate", "Replicate")));
            style.Triggers.Add(stoppedTrigger);

            var runningTrigger = new Trigger { Property = FrameworkElement.TagProperty, Value = "Running" };
            runningTrigger.Setters.Add(new Setter(ContentControl.ContentProperty, L("header.button.replicating", "Replicating")));
            runningTrigger.Setters.Add(new Setter(Control.BackgroundProperty, TealAccentBrush));
            runningTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, TealAccentBrush));
            runningTrigger.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            style.Triggers.Add(runningTrigger);

            var hoverStopped = new MultiTrigger();
            hoverStopped.Conditions.Add(new Condition(UIElement.IsMouseOverProperty, true));
            hoverStopped.Conditions.Add(new Condition(FrameworkElement.TagProperty, "Stopped"));
            hoverStopped.Setters.Add(new Setter(Control.BackgroundProperty, TealAccentBrush));
            hoverStopped.Setters.Add(new Setter(Control.BorderBrushProperty, TealAccentBrush));
            hoverStopped.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            hoverStopped.Setters.Add(new Setter(ContentControl.ContentProperty, L("header.button.start", "Start")));
            style.Triggers.Add(hoverStopped);

            var hoverRunning = new MultiTrigger();
            hoverRunning.Conditions.Add(new Condition(UIElement.IsMouseOverProperty, true));
            hoverRunning.Conditions.Add(new Condition(FrameworkElement.TagProperty, "Running"));
            hoverRunning.Setters.Add(new Setter(Control.BackgroundProperty, OrangeAccentBrush));
            hoverRunning.Setters.Add(new Setter(Control.BorderBrushProperty, OrangeAccentBrush));
            hoverRunning.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            hoverRunning.Setters.Add(new Setter(ContentControl.ContentProperty, L("header.button.stop", "Stop")));
            style.Triggers.Add(hoverRunning);

            return style;
        }

        private Style CreateFlattenButtonStyle(FrameworkElement context)
        {
            var baseStyle = FindSkinStyle(context, typeof(Button));
            var style = new Style(typeof(Button), baseStyle);

            ApplySkinSetter(style, Control.BackgroundProperty, context, "BackgroundTextInput", "BackgroundMainWindow", "GridEntireBackground");
            ApplySkinSetter(style, Control.BorderBrushProperty, context, "BorderThinBrush", "TabControlBorderBrush");

            double? sharedFontSize = FindSkinDouble(context, "FontTableHeight", "FontControlHeight", "FontHeaderLevel4Height");
            if (sharedFontSize.HasValue)
                style.Setters.Add(new Setter(Control.FontSizeProperty, sharedFontSize.Value));

            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(14, 6, 14, 6)));
            style.Setters.Add(new Setter(Control.ForegroundProperty, UiPrimaryTextBrush));
            style.Setters.Add(new Setter(Control.FontWeightProperty, UiActionFontWeight));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(Control.CursorProperty, System.Windows.Input.Cursors.Hand));
            style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, OrangeAccentBrush));
            hoverTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, OrangeAccentBrush));
            hoverTrigger.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            style.Triggers.Add(hoverTrigger);

            return style;
        }

        private static Style CreateGroupActionButtonStyle(FrameworkElement context)
        {
            var baseStyle = FindSkinStyle(context, typeof(Button));
            var style = new Style(typeof(Button), baseStyle);

            ApplySkinSetter(style, Control.BackgroundProperty, context, "BackgroundMainWindow", "GridEntireBackground", "BackgroundTextInput");
            ApplySkinSetter(style, Control.BorderBrushProperty, context, "BorderThinBrush", "TabControlBorderBrush");

            double? sharedFontSize = FindSkinDouble(context, "FontTableHeight", "FontControlHeight", "FontHeaderLevel4Height");
            if (sharedFontSize.HasValue)
                style.Setters.Add(new Setter(Control.FontSizeProperty, sharedFontSize.Value));

            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 4, 10, 4)));
            style.Setters.Add(new Setter(Control.MinHeightProperty, 28d));
            style.Setters.Add(new Setter(Control.ForegroundProperty, UiPrimaryTextBrush));
            style.Setters.Add(new Setter(Control.FontWeightProperty, UiActionFontWeight));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(Control.CursorProperty, System.Windows.Input.Cursors.Hand));
            style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
            style.Setters.Add(new Setter(Control.TemplateProperty, CreateActionButtonTemplate()));
            ApplyTealAccentResourceOverrides(style);

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, TealAccentBrush));
            hoverTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, TealAccentBrush));
            hoverTrigger.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            style.Triggers.Add(hoverTrigger);

            var pressedTrigger = new Trigger { Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Value = true };
            pressedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, TealAccentBrush));
            pressedTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, TealAccentBrush));
            pressedTrigger.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            style.Triggers.Add(pressedTrigger);

            var keyboardFocusTrigger = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
            keyboardFocusTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, TealAccentBrush));
            style.Triggers.Add(keyboardFocusTrigger);

            return style;
        }

        private static ControlTemplate CreateActionButtonTemplate()
        {
            var template = new ControlTemplate(typeof(Button));

            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.Name = "ButtonBorder";
            borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            borderFactory.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            borderFactory.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            borderFactory.SetValue(Border.SnapsToDevicePixelsProperty, true);

            var contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            contentFactory.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
            contentFactory.SetValue(ContentPresenter.ContentTemplateProperty, new TemplateBindingExtension(ContentControl.ContentTemplateProperty));
            contentFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
            contentFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, new TemplateBindingExtension(Control.VerticalContentAlignmentProperty));
            contentFactory.SetValue(ContentPresenter.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
            contentFactory.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
            borderFactory.AppendChild(contentFactory);

            template.VisualTree = borderFactory;

            var disabledTrigger = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabledTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.65));
            template.Triggers.Add(disabledTrigger);
            return template;
        }

        private static Style CreateGroupAddButtonStyle(FrameworkElement context)
        {
            var style = new Style(typeof(Button), CreateGroupActionButtonStyle(context));

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, TealAccentBrush));
            hoverTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, TealAccentBrush));
            hoverTrigger.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            style.Triggers.Add(hoverTrigger);
            return style;
        }

        private static Style CreateGroupRemoveButtonStyle(FrameworkElement context)
        {
            var style = new Style(typeof(Button), CreateGroupActionButtonStyle(context));
            style.Setters.Add(new Setter(UIElement.OpacityProperty, 0.58));

            var enabledTrigger = new Trigger { Property = UIElement.IsEnabledProperty, Value = true };
            enabledTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 1.0));
            enabledTrigger.Setters.Add(new Setter(Control.BackgroundProperty, OrangeAccentBrush));
            enabledTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, OrangeAccentBrush));
            enabledTrigger.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            style.Triggers.Add(enabledTrigger);

            var disabledTrigger = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabledTrigger.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            disabledTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
            style.Triggers.Add(disabledTrigger);

            return style;
        }

        private void OnReplicateButtonClick(object sender, RoutedEventArgs e)
        {
            if (!_isReplicatingUi)
            {
                if (!CanEnableReplication(out string denialReason))
                {
                    AppendJournal("System", "Replication", $"Replication start blocked. {denialReason}");
                    RaiseCriticalWarning(
                        "System",
                        "Replication start blocked: " + denialReason,
                        "PolicyReplicationBlocked",
                        unlocksTrading: false);
                    return;
                }

                ApplyPlanLimitsToAccountGroups("replication_start");
            }

            _isReplicatingUi = !_isReplicatingUi;
            _lastUiRefreshUtc = DateTime.MinValue;
            if (_isReplicatingUi)
            {
                _replicationWarmupUntilUtc = DateTime.UtcNow.Add(ReplicationStartupWarmup);
                ClearReplicationSubmitCooldowns();
                ClearProtectiveSyncCooldowns();
            }
            else
            {
                ClearReplicationSubmitCooldowns();
                ClearProtectiveSyncCooldowns();
                ClearComplianceEnforcementRuntimeState();
                _replicationWarmupUntilUtc = DateTime.MinValue;
            }

            AppendJournal(
                "System",
                "Replication",
                _isReplicatingUi
                    ? $"Replication gate opened. Warm-up {ReplicationStartupWarmup.TotalSeconds:0}s."
                    : "Replication gate closed.");
            UpdateReplicateButtonState();
            UpdateRefreshTimerCadence();
            PublishGlitchShellState();
        }

        internal void ToggleReplicationFromExternalSurface()
        {
            OnReplicateButtonClick(this, new RoutedEventArgs());
        }

        internal void FlattenAllFromExternalSurface()
        {
            OnFlattenAllButtonClick(this, new RoutedEventArgs());
        }

        private void UpdateReplicateButtonState()
        {
            if (_replicateButton == null)
                return;

            _replicateButton.Tag = _isReplicatingUi ? "Running" : "Stopped";
        }

        private void UpdateRefreshTimerCadence()
        {
            if (_refreshTimer == null)
                return;

            _refreshTimer.Interval = _isReplicatingUi
                ? TimeSpan.FromMilliseconds(500)
                : (IsGlitchShellUiActive() ? TimeSpan.FromSeconds(1.5) : IdleBackgroundUiRefreshInterval);
        }

        private string BuildRuntimePolicySummaryLogLine()
        {
            GlitchRuntimePolicySettings settings = _runtimePolicySettings ?? new GlitchRuntimePolicySettings();
            GlitchLicenseCacheState cache = _licenseCacheState ?? new GlitchLicenseCacheState();
            string plan = string.IsNullOrWhiteSpace(cache.Plan) ? "free_lite" : cache.Plan;
            string status = string.IsNullOrWhiteSpace(cache.LastStatus) ? "unknown" : cache.LastStatus;
            bool tokenActive = HasCurrentSignedToken(DateTime.UtcNow);

            return
                $"Effective policy: status={status}, plan={plan}, " +
                $"signedTokenActive={tokenActive}, " +
                $"flattenFreeze15={settings.EnforceBufferFreeze15Percent}, " +
                $"oneContract20to25={settings.EnforceBufferOneContract30Percent}, " +
                $"unrlzdFlatten80={settings.EnforceUnrealizedFlatten70Percent}, " +
                $"evalLock={settings.EnforceEvalProfitTargetLock}, " +
                $"replMaxDelta={settings.ReplicationMaxDeltaPerCycle}, " +
                $"replBurstMs={settings.ReplicationBurstWindowMs}, " +
                $"noProtMs={settings.NoProtectionTimeoutMs}, " +
                $"rearmMs={settings.RearmTimeoutMs}, " +
                $"limits={{groups:{cache.MaxGroups}, followersPerGroup:{cache.MaxFollowersPerGroup}}}.";
        }

        private void RehydrateLicenseStateFromSignedCache()
        {
            if (_licenseCacheState == null)
                _licenseCacheState = new GlitchLicenseCacheState();
            if (_runtimePolicySettings == null)
                _runtimePolicySettings = new GlitchRuntimePolicySettings();

            string signedToken = (_licenseCacheState.SignedLicenseToken ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(signedToken))
            {
                ApplyFreeLitePolicyToCache("expired", "startup_missing_signed_token");
                return;
            }

            if (!GlitchLicenseService.TryReadVerifiedCachedTokenClaims(
                    signedToken,
                    _runtimePolicySettings.InstallationId,
                    _licenseDeviceFingerprintHash,
                    out GlitchLicenseTokenClaims claims,
                    out string tokenFailureReason))
            {
                _licenseCacheState.SignedLicenseToken = string.Empty;
                _licenseCacheState.SignedTokenExpiresUtc = DateTime.MinValue;
                ApplyFreeLitePolicyToCache("expired", string.IsNullOrWhiteSpace(tokenFailureReason) ? "startup_token_invalid" : tokenFailureReason);
                return;
            }

            DateTime nowUtc = DateTime.UtcNow;
            string restoredStatus = claims.ExpiresAtUtc > nowUtc ? "active" : "grace";
            string restoredReason = restoredStatus.Equals("grace", StringComparison.OrdinalIgnoreCase)
                ? "startup_grace_from_signed_cache"
                : string.Empty;
            ApplyTokenClaimsToCache(claims, restoredStatus, restoredReason);
            _licenseCacheState.SignedLicenseToken = signedToken;
            _licenseCacheState.SignedTokenExpiresUtc = claims.ExpiresAtUtc;
            if (_licenseCacheState.LastSuccessUtc == DateTime.MinValue)
                _licenseCacheState.LastSuccessUtc = nowUtc;
        }

        private void ApplyTokenClaimsToCache(GlitchLicenseTokenClaims claims, string status, string reason)
        {
            if (_licenseCacheState == null)
                _licenseCacheState = new GlitchLicenseCacheState();

            if (claims == null || claims.Policy == null)
            {
                ApplyFreeLitePolicyToCache("expired", "token_claims_missing");
                return;
            }

            GlitchLicensePolicy policy = claims.Policy;
            _licenseCacheState.Plan = string.IsNullOrWhiteSpace(policy.Plan) ? "free_lite" : policy.Plan.Trim().ToLowerInvariant();
            _licenseCacheState.BillingVariant = (claims.BillingVariant ?? string.Empty).Trim().ToLowerInvariant();
            _licenseCacheState.SourceProductId = (claims.SourceProductId ?? string.Empty).Trim();
            _licenseCacheState.SourcePlanCode = (claims.SourcePlanCode ?? string.Empty).Trim();
            _licenseCacheState.FeatureAnalytics = policy.Analytics;
            _licenseCacheState.FeatureMacro = policy.Macro;
            _licenseCacheState.FeatureFundamental = policy.Fundamental;
            _licenseCacheState.FeatureStrategies = policy.Strategies;
            _licenseCacheState.FeatureAdvancedReplication = policy.AdvancedReplication;
            _licenseCacheState.MaxGroups = Math.Max(1, policy.MaxGroups);
            _licenseCacheState.MaxFollowersPerGroup = Math.Max(1, policy.MaxFollowersPerGroup);
            _licenseCacheState.LastStatus = string.IsNullOrWhiteSpace(status) ? "active" : status;
            _licenseCacheState.LastReason = reason ?? string.Empty;
            _licenseCacheState.SignedTokenExpiresUtc = claims.ExpiresAtUtc;
            _licenseCacheState.GraceUntilUtc = claims.GraceUntilUtc != DateTime.MinValue
                ? claims.GraceUntilUtc
                : claims.ExpiresAtUtc;
        }

        private bool HasCurrentSignedToken(DateTime nowUtc)
        {
            if (_licenseCacheState == null)
                return false;

            if (string.IsNullOrWhiteSpace(_licenseCacheState.SignedLicenseToken))
                return false;

            if (_licenseCacheState.SignedTokenExpiresUtc == DateTime.MinValue)
                return false;

            return _licenseCacheState.SignedTokenExpiresUtc > nowUtc;
        }

        private static string BuildRuleEvent(
            string ruleId,
            string action,
            double observedValue,
            double thresholdValue,
            string detail)
        {
            return $"Rule={ruleId}; Action={action}; Observed={observedValue:F2}; Threshold={thresholdValue:F2}; Detail={detail}";
        }

        private bool IsFreeLitePlan()
        {
            return string.Equals(_licenseCacheState?.Plan, "free_lite", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsLicenseActiveOrGrace(DateTime nowUtc)
        {
            string status = (_licenseCacheState?.LastStatus ?? string.Empty).Trim();
            if (status.Equals("active", StringComparison.OrdinalIgnoreCase))
                return HasCurrentSignedToken(nowUtc) || IsWithinGraceWindow(nowUtc);
            if (status.Equals("grace", StringComparison.OrdinalIgnoreCase))
                return IsWithinGraceWindow(nowUtc);
            if ((_licenseCacheState?.LastSuccessUtc ?? DateTime.MinValue) == DateTime.MinValue)
                return false;
            return IsWithinGraceWindow(nowUtc);
        }

        private bool IsWithinGraceWindow(DateTime nowUtc)
        {
            if (_licenseCacheState == null)
                return false;
            if (_licenseCacheState.GraceUntilUtc == DateTime.MinValue)
                return false;
            return _licenseCacheState.GraceUntilUtc > nowUtc;
        }

        private bool HasSuccessfulLicenseValidation()
        {
            return (_licenseCacheState?.LastSuccessUtc ?? DateTime.MinValue) > DateTime.MinValue;
        }

        private string BuildPremiumAccessMessage(string moduleLabel)
        {
            string normalizedModule = string.IsNullOrWhiteSpace(moduleLabel) ? "premium features" : moduleLabel.Trim();
            string licenseKey = (_runtimePolicySettings?.LicenseKey ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(licenseKey))
                return $"Free Lite is active. Go Pro to unlock {normalizedModule}.";

            string status = (_licenseCacheState?.LastStatus ?? string.Empty).Trim();
            string reason = (_licenseCacheState?.LastReason ?? string.Empty).Trim();

            if (status.Equals("grace", StringComparison.OrdinalIgnoreCase))
                return $"Pro access is in grace period. Open Membership Hub to renew and keep {normalizedModule} unlocked.";

            if (reason.Equals("bound_to_other_installation", StringComparison.OrdinalIgnoreCase))
                return "This license is bound to another installation. Open Membership Hub to rebind this machine.";

            if (reason.Equals("device_fingerprint_mismatch", StringComparison.OrdinalIgnoreCase))
                return "License fingerprint mismatch. Open Membership Hub to rebind this device.";

            if (reason.Equals("license_not_found", StringComparison.OrdinalIgnoreCase) ||
                reason.Equals("missing_license_key", StringComparison.OrdinalIgnoreCase))
            {
                return $"License key not found. Verify your key in Settings, or Go Pro to unlock {normalizedModule}.";
            }

            if (reason.Equals("binding_not_found", StringComparison.OrdinalIgnoreCase))
                return $"This machine is not activated yet. Save Settings to validate, or Go Pro to unlock {normalizedModule}.";

            if (reason.Equals("binding_metadata_conflict", StringComparison.OrdinalIgnoreCase))
                return "License metadata is out of sync. Open Membership Hub to reset and rebind.";

            return $"Lite mode is active. Go Pro to unlock {normalizedModule}.";
        }

        private string BuildUnifiedPremiumGateMessage()
        {
            return BuildPremiumAccessMessage("premium modules");
        }

        private void NavigateToSettingsTabFromOverlay()
        {
            if (Dispatcher != null && !Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(NavigateToSettingsTabFromOverlay));
                return;
            }

            if (_mainTabControl == null)
                return;

            if (_mainTabControl.Items.Count > 3)
                _mainTabControl.SelectedIndex = 3;

            if (_settingsLicenseKeyTextBox != null)
            {
                _settingsLicenseKeyTextBox.Focus();
                _settingsLicenseKeyTextBox.SelectAll();
            }
        }

        private void ApplyFreeLitePolicyToCache(string status, string reason)
        {
            if (_licenseCacheState == null)
                _licenseCacheState = new GlitchLicenseCacheState();

            _licenseCacheState.Plan = "free_lite";
            _licenseCacheState.BillingVariant = "free";
            _licenseCacheState.SourceProductId = string.Empty;
            _licenseCacheState.SourcePlanCode = string.Empty;
            _licenseCacheState.FeatureAnalytics = false;
            _licenseCacheState.FeatureMacro = false;
            _licenseCacheState.FeatureFundamental = false;
            _licenseCacheState.FeatureStrategies = false;
            _licenseCacheState.FeatureAdvancedReplication = false;
            _licenseCacheState.MaxGroups = 1;
            _licenseCacheState.MaxFollowersPerGroup = 2;
            _licenseCacheState.LastStatus = string.IsNullOrWhiteSpace(status) ? "expired" : status;
            _licenseCacheState.LastReason = reason ?? string.Empty;
            if (!_licenseCacheState.LastStatus.Equals("grace", StringComparison.OrdinalIgnoreCase))
            {
                _licenseCacheState.SignedLicenseToken = string.Empty;
                _licenseCacheState.SignedTokenExpiresUtc = DateTime.MinValue;
            }
        }

        private bool CanEnableReplication(out string denialReason)
        {
            denialReason = null;
            DateTime nowUtc = DateTime.UtcNow;
            if (!IsLicenseActiveOrGrace(nowUtc))
                ApplyFreeLitePolicyToCache("expired", _licenseCacheState?.LastReason ?? "grace_window_elapsed");

            if (IsFreeLitePlan())
            {
                ApplyPlanLimitsToAccountGroups("replication_gate");
                if (CountConfiguredGroups() > (_licenseCacheState?.MaxGroups ?? 1))
                {
                    denialReason = "Free Lite allows only one configured group.";
                    return false;
                }

                if (AnyGroupHasEnabledFollowersOverLimit(_licenseCacheState?.MaxFollowersPerGroup ?? 2))
                {
                    denialReason = "Free Lite allows a maximum of two enabled followers per group.";
                    return false;
                }
            }

            return true;
        }

        private int CountConfiguredGroups()
        {
            return (_accountGroups ?? new ObservableCollection<AccountGroupDefinition>())
                .Count(group => group != null && !string.IsNullOrWhiteSpace(group.MasterAccount));
        }

        private bool AnyGroupHasEnabledFollowersOverLimit(int maxFollowersPerGroup)
        {
            int safeLimit = Math.Max(1, maxFollowersPerGroup);
            foreach (AccountGroupDefinition group in _accountGroups ?? new ObservableCollection<AccountGroupDefinition>())
            {
                if (group?.Members == null)
                    continue;

                int enabledCount = group.Members.Count(member => member != null && member.IsEnabled);
                if (enabledCount > safeLimit)
                    return true;
            }

            return false;
        }

        private void ApplyPlanLimitsToAccountGroups(string source)
        {
            if (!IsFreeLitePlan() || _accountGroups == null)
                return;

            int maxGroups = Math.Max(1, _licenseCacheState?.MaxGroups ?? 1);
            int maxFollowers = Math.Max(1, _licenseCacheState?.MaxFollowersPerGroup ?? 2);
            bool changed = false;

            for (int groupIndex = 0; groupIndex < _accountGroups.Count; groupIndex++)
            {
                AccountGroupDefinition group = _accountGroups[groupIndex];
                if (group?.Members == null)
                    continue;

                if (groupIndex >= maxGroups)
                {
                    foreach (AccountGroupMemberRow member in group.Members.Where(member => member != null && member.IsEnabled))
                    {
                        member.IsEnabled = false;
                        changed = true;
                    }

                    continue;
                }

                var enabledMembers = group.Members.Where(member => member != null && member.IsEnabled).ToList();
                for (int i = maxFollowers; i < enabledMembers.Count; i++)
                {
                    enabledMembers[i].IsEnabled = false;
                    changed = true;
                }
            }

            if (!changed)
                return;

            AppendJournal(
                "System",
                "Policy",
                $"Plan limits applied ({source}). Free Lite caps: maxGroups={maxGroups}, maxFollowersPerGroup={maxFollowers}. Extras were safely disabled.");
            SaveAccountGroupsToDisk();
            RebuildAccountGroupsUi();
        }

        private void MaybeRunLicenseHeartbeat(DateTime nowUtc)
        {
            if (_isLicenseCheckInFlight)
                return;
            if (nowUtc < _nextLicenseHeartbeatUtc)
                return;

            _ = RefreshLicenseStateAsync(useValidateEndpoint: false, force: false);
        }

        private static string ResolveCurrentClientVersion()
        {
            try
            {
                Assembly assembly = typeof(GlitchMainWindow).Assembly;
                AssemblyName name = assembly?.GetName();
                Version version = name?.Version;
                if (name != null &&
                    version != null &&
                    string.Equals(name.Name, "Glitch", StringComparison.OrdinalIgnoreCase))
                {
                    int build = Math.Max(0, version.Build);
                    int revision = Math.Max(0, version.Revision);
                    return $"addon-{version.Major}.{version.Minor}.{build}.{revision}";
                }
            }
            catch
            {
            }

            return CurrentClientVersion;
        }

        private void ApplyClientUpdateStateFromSnapshot(GlitchLicenseSnapshot snapshot)
        {
            if (snapshot == null || !snapshot.UpdateChecked)
                return;

            _isClientUpdateAvailable = snapshot.UpdateAvailable;
            _latestClientVersion = (snapshot.LatestClientVersion ?? string.Empty).Trim();

            string snapshotDownloadUrl = (snapshot.UpdateDownloadUrl ?? string.Empty).Trim();
            _latestDownloadUrl = string.IsNullOrWhiteSpace(snapshotDownloadUrl)
                ? DefaultLatestDownloadUrl
                : snapshotDownloadUrl;
        }

        private async Task RefreshLicenseStateAsync(bool useValidateEndpoint, bool force)
        {
            if (_isLicenseCheckInFlight && !force)
                return;

            if (_runtimePolicySettings == null)
                return;

            if (string.IsNullOrWhiteSpace(_runtimePolicySettings.LicenseApiBaseUrl))
                return;

            string licenseKey = (_runtimePolicySettings.LicenseKey ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(licenseKey))
            {
                DateTime nowNoKey = DateTime.UtcNow;
                _licenseCacheState.LastCheckedUtc = nowNoKey;
                ApplyFreeLitePolicyToCache("free_lite", "missing_license_key");
                try
                {
                    string clientVersion = ResolveCurrentClientVersion();
                    GlitchLicenseSnapshot updateSnapshot = await GlitchLicenseService.HeartbeatAsync(
                        _runtimePolicySettings.LicenseApiBaseUrl,
                        string.Empty,
                        _runtimePolicySettings.InstallationId,
                        _licenseDeviceFingerprintHash,
                        clientVersion);
                    ApplyClientUpdateStateFromSnapshot(updateSnapshot);
                    int nextCheckInSeconds = updateSnapshot?.NextCheckInSeconds ?? 14400;
                    nextCheckInSeconds = Math.Max(900, Math.Min(14400, nextCheckInSeconds));
                    _nextLicenseHeartbeatUtc = nowNoKey.AddSeconds(nextCheckInSeconds);
                }
                catch (Exception updateError)
                {
                    AppendJournal("System", "License", $"Update check failed without license key: {updateError.Message}");
                    _nextLicenseHeartbeatUtc = nowNoKey.AddSeconds(14400);
                }
                GlitchRuntimePolicyStore.SaveLicenseCache(_licenseCacheFilePath, _licenseCacheState);
                UpdateSettingsTabLicenseStatusText();
                UpdateAnalyticsLicenseGateOverlay();
                UpdateJournalLicenseGateOverlay();
                return;
            }

            _isLicenseCheckInFlight = true;
            try
            {
                string clientVersion = ResolveCurrentClientVersion();
                GlitchLicenseSnapshot snapshot = useValidateEndpoint
                    ? await GlitchLicenseService.ValidateAsync(
                        _runtimePolicySettings.LicenseApiBaseUrl,
                        licenseKey,
                        _runtimePolicySettings.InstallationId,
                        _licenseDeviceFingerprintHash,
                        clientVersion)
                    : await GlitchLicenseService.HeartbeatAsync(
                        _runtimePolicySettings.LicenseApiBaseUrl,
                        licenseKey,
                        _runtimePolicySettings.InstallationId,
                        _licenseDeviceFingerprintHash,
                        clientVersion);

                ApplyClientUpdateStateFromSnapshot(snapshot);

                DateTime nowUtc = DateTime.UtcNow;
                _licenseCacheState.LastCheckedUtc = nowUtc;
                _licenseCacheState.LastReason = snapshot?.Reason ?? string.Empty;

                if (snapshot != null &&
                    snapshot.RequestSucceeded &&
                    snapshot.LicenseValid &&
                    snapshot.HasVerifiedToken &&
                    snapshot.TokenClaims != null)
                {
                    _licenseCacheState.LastSuccessUtc = nowUtc;
                    ApplyTokenClaimsToCache(snapshot.TokenClaims, "active", snapshot.Reason ?? string.Empty);
                    _licenseCacheState.SignedLicenseToken = snapshot.LicenseToken ?? string.Empty;
                    _licenseCacheState.SignedTokenExpiresUtc = snapshot.TokenClaims.ExpiresAtUtc;
                }
                else if (snapshot != null && snapshot.RequestSucceeded && !snapshot.LicenseValid)
                {
                    bool inGrace = IsWithinGraceWindow(nowUtc);
                    _licenseCacheState.LastStatus = inGrace ? "grace" : "expired";
                    if (!inGrace)
                        ApplyFreeLitePolicyToCache("expired", _licenseCacheState.LastReason);
                }
                else
                {
                    bool inGrace = IsWithinGraceWindow(nowUtc);
                    _licenseCacheState.LastStatus = inGrace ? "grace" : "expired";
                    if (!inGrace)
                        ApplyFreeLitePolicyToCache("expired", _licenseCacheState.LastReason);
                }

                GlitchRuntimePolicyStore.SaveLicenseCache(_licenseCacheFilePath, _licenseCacheState);
                UpdateSettingsTabLicenseStatusText();
                ApplyPlanLimitsToAccountGroups(useValidateEndpoint ? "validate" : "heartbeat");
                UpdateAnalyticsLicenseGateOverlay();
                UpdateJournalLicenseGateOverlay();

                int nextCheckInSeconds = snapshot?.NextCheckInSeconds ?? 14400;
                nextCheckInSeconds = Math.Max(15, Math.Min(14400, nextCheckInSeconds));
                _nextLicenseHeartbeatUtc = nowUtc.AddSeconds(nextCheckInSeconds);
            }
            catch (Exception error)
            {
                DateTime nowUtc = DateTime.UtcNow;
                _licenseCacheState.LastCheckedUtc = nowUtc;
                bool inGrace = IsWithinGraceWindow(nowUtc);
                _licenseCacheState.LastStatus = inGrace ? "grace" : "expired";
                _licenseCacheState.LastReason = error.Message ?? "license_check_failed";
                if (!inGrace)
                    ApplyFreeLitePolicyToCache("expired", _licenseCacheState.LastReason);
                GlitchRuntimePolicyStore.SaveLicenseCache(_licenseCacheFilePath, _licenseCacheState);
                UpdateSettingsTabLicenseStatusText();
                UpdateAnalyticsLicenseGateOverlay();
                UpdateJournalLicenseGateOverlay();
                _nextLicenseHeartbeatUtc = nowUtc.AddSeconds(14400);
            }
            finally
            {
                _isLicenseCheckInFlight = false;
            }
        }

        private string ComputeDeviceFingerprintHash()
        {
            try
            {
                string machineName = Environment.MachineName ?? string.Empty;
                string userName = Environment.UserName ?? string.Empty;
                string osVersion = Environment.OSVersion?.VersionString ?? string.Empty;
                string payload = $"{machineName}|{userName}|{osVersion}";
                using (var sha = SHA256.Create())
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(payload);
                    byte[] hash = sha.ComputeHash(bytes);
                    var builder = new StringBuilder(hash.Length * 2);
                    foreach (byte b in hash)
                        builder.Append(b.ToString("x2"));
                    return builder.ToString();
                }
            }
            catch
            {
                return Guid.NewGuid().ToString("N");
            }
        }

        private async void OnFlattenAllButtonClick(object sender, RoutedEventArgs e)
        {
            if (_flattenAllButton == null || _isFlattenFeedbackActive)
                return;

            Color normalBackground = ResolveBrushColor(_flattenAllButton.Background, Color.FromRgb(45, 45, 48));
            Color normalBorder = ResolveBrushColor(_flattenAllButton.BorderBrush, normalBackground);
            Color normalForeground = ResolveBrushColor(_flattenAllButton.Foreground, Colors.White);
            bool flattenSucceeded = await TryExecuteFlattenAllAsync();
            if (!flattenSucceeded)
                return;

            _isFlattenFeedbackActive = true;
            _flattenAllButton.IsEnabled = false;
            _flattenAllButton.Content = L("header.button.flattened", "Flattened!");
            AnimateButtonColors(
                _flattenAllButton,
                normalBackground,
                TealAccentBrush.Color,
                normalBorder,
                TealAccentBrush.Color,
                normalForeground,
                Colors.White);

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
            finally
            {
                if (_flattenAllButton != null)
                {
                    _flattenAllButton.Content = L("header.button.flatten_all", "Flatten All");
                    AnimateButtonColors(
                        _flattenAllButton,
                        TealAccentBrush.Color,
                        normalBackground,
                        TealAccentBrush.Color,
                        normalBorder,
                        Colors.White,
                        normalForeground);
                    await Task.Delay(TimeSpan.FromMilliseconds(260));
                    _flattenAllButton.ClearValue(Control.BackgroundProperty);
                    _flattenAllButton.ClearValue(Control.BorderBrushProperty);
                    _flattenAllButton.ClearValue(Control.ForegroundProperty);
                    _flattenAllButton.IsEnabled = true;
                }

                _isFlattenFeedbackActive = false;
            }
        }

        private async Task<bool> TryExecuteFlattenAllAsync()
        {
            if (_isFlattenAllInProgress)
                return false;

            _isFlattenAllInProgress = true;
            var flattenStopwatch = Stopwatch.StartNew();
            int flattenSubmitCount = 0;
            try
            {
                var accounts = GetActiveAccountsSnapshot();
                if (accounts.Count == 0)
                    return true;

                foreach (Account account in accounts)
                {
                    foreach (Instrument instrument in GetOpenPositionInstruments(account))
                    {
                        string instrumentToken = CleanJournalToken(GetInstrumentRoot(instrument));
                        const string flattenAllReason = "flatten_all_manual";
                        AppendJournal(
                            account.Name,
                            "Risk",
                            $"SYNC|event=flatten_attempt|reason={flattenAllReason}|instrument={instrumentToken}|signal={RiskFlattenBufferSignalName}");

                        bool namedConfirmed = TrySubmitNamedRiskFlattenOrder(
                            account,
                            instrument,
                            RiskFlattenBufferSignalName,
                            out string namedResult,
                            out Order submittedOrder);
                        if (namedConfirmed)
                        {
                            flattenSubmitCount++;
                            AppendJournal(
                                account.Name,
                                "Risk",
                                $"SYNC|event=flatten_named_result|reason={flattenAllReason}|instrument={instrumentToken}|signal={RiskFlattenBufferSignalName}|origin={FlattenOrigin.AddonGovernor}|result=confirmed|cause={CleanJournalToken(namedResult)}");
                            AppendJournal(
                                account.Name,
                                "Risk",
                                $"SYNC|event=flatten_origin|reason={flattenAllReason}|origin={FlattenOrigin.AddonGovernor}|signal={RiskFlattenBufferSignalName}|instrument={instrumentToken}");
                            continue;
                        }

                        if (submittedOrder != null)
                        {
                            flattenSubmitCount++;
                            AppendJournal(
                                account.Name,
                                "Risk",
                                $"SYNC|event=flatten_named_result|reason={flattenAllReason}|instrument={instrumentToken}|signal={RiskFlattenBufferSignalName}|origin={FlattenOrigin.Unknown}|result=pending|cause={CleanJournalToken(namedResult)}");
                            ScheduleNamedRiskFlattenConfirmationFallback(
                                account,
                                instrument,
                                submittedOrder,
                                RiskFlattenBufferSignalName,
                                flattenAllReason,
                                mitigationKey: "FLATTENALL|" + CleanJournalToken(account.Name) + "|" + instrumentToken,
                                allowFallbackWarning: false);
                            continue;
                        }

                        TryIssueInstrumentFlattenFallback(
                            account,
                            instrument,
                            RiskFlattenBufferSignalName,
                            flattenAllReason,
                            namedResult,
                            mitigationKey: "FLATTENALL|" + CleanJournalToken(account.Name) + "|" + instrumentToken,
                            allowFallbackWarning: false);
                        flattenSubmitCount++;
                    }
                }

                flattenStopwatch.Stop();
                AppendJournal(
                    "System",
                    "Perf",
                    $"METRIC|flatten_submit_ms={flattenStopwatch.ElapsedMilliseconds}|orders={flattenSubmitCount}");

                bool flattened = await WaitForAllAccountsFlatAsync(accounts, TimeSpan.FromSeconds(5));
                if (flattened)
                {
                    ClearReplicationSubmitCooldowns();
                    ClearProtectiveSyncCooldowns();
                    AppendJournal("System", "Risk", "Flatten All executed successfully.");
                    RefreshAccountData();
                }

                return flattened;
            }
            catch
            {
                return false;
            }
            finally
            {
                _isFlattenAllInProgress = false;
            }
        }

        private void OnCreateGroupClick(object sender, RoutedEventArgs e)
        {
            if (IsFreeLitePlan())
            {
                int maxGroups = Math.Max(1, _licenseCacheState?.MaxGroups ?? 1);
                if (CountConfiguredGroups() >= maxGroups)
                {
                    string message = $"Free Lite limit reached: maximum {maxGroups} group(s).";
                    AppendJournal("System", "Policy", message);
                    RaiseCriticalWarning("System", message, "PolicyGroupLimit", unlocksTrading: false);
                    return;
                }
            }

            var availableMasters = GetConnectedAccountNames();

            if (availableMasters.Count == 0)
                return;

            string selectedMaster = ShowAccountPickerDialog(
                L("dialog.create_group.title", "Create Account Group"),
                L("dialog.create_group.prompt", "Pick a master account:"),
                availableMasters);
            if (string.IsNullOrWhiteSpace(selectedMaster))
                return;

            double masterSize = ResolveAccountSizeForName(selectedMaster);
            if (masterSize <= 0)
                masterSize = 25000;

            var group = new AccountGroupDefinition
            {
                GroupId = Guid.NewGuid().ToString("N"),
                MasterAccount = selectedMaster,
                MasterSize = masterSize,
                Members = new ObservableCollection<AccountGroupMemberRow>()
            };
            _accountGroups.Add(group);
            SaveAccountGroupsToDisk();
            RebuildAccountGroupsUi();
        }

        private void RebuildAccountGroupsUi()
        {
            if (_accountGroupsHostPanel == null)
                return;

            _accountGroupsHostPanel.Children.Clear();

            int index = 1;
            List<string> connectedAccounts = GetConnectedAccountNames();
            foreach (AccountGroupDefinition group in _accountGroups)
            {
                var container = new Border
                {
                    BorderThickness = new Thickness(0),
                    Background = Brushes.Transparent,
                    BorderBrush = Brushes.Transparent,
                    Padding = new Thickness(0, 10, 0, 10),
                    Margin = new Thickness(0, 0, 0, 10)
                };

                var groupRoot = new Grid();
                groupRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                groupRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var titleRow = new Grid();
                titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var masterPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var groupLabel = new TextBlock
                {
                    Text = Lf("dashboard.group.master_label_format", "Group {0} | Master:", index),
                    FontWeight = UiHeadingFontWeight,
                    VerticalAlignment = VerticalAlignment.Center
                };
                ApplySkinResource(groupLabel, TextBlock.ForegroundProperty, "FontControlBrush", "FontHeaderLevel4Brush", "FontTableBrush");
                masterPanel.Children.Add(groupLabel);

                var masterOptions = GetMasterOptionsForGroup(connectedAccounts, group.MasterAccount);
                var masterPicker = new ComboBox
                {
                    Width = 210,
                    Margin = new Thickness(6, 0, 0, 0),
                    ItemsSource = masterOptions,
                    SelectedItem = masterOptions.FirstOrDefault(option => option.Equals(group.MasterAccount, StringComparison.OrdinalIgnoreCase)) ?? masterOptions.FirstOrDefault(),
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Style = CreateGroupMasterComboBoxStyle(container)
                };
                masterPicker.SelectionChanged += (s, e) =>
                {
                    if (!(masterPicker.SelectedItem is string selectedMaster) || string.IsNullOrWhiteSpace(selectedMaster))
                        return;

                    if (group.MasterAccount != null &&
                        group.MasterAccount.Equals(selectedMaster, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    UpdateGroupMasterSelection(group, selectedMaster);
                    RebuildAccountGroupsUi();
                };
                masterPanel.Children.Add(masterPicker);

                Grid.SetColumn(masterPanel, 0);
                titleRow.Children.Add(masterPanel);

                var removeButton = new Button
                {
                    Content = group.Members != null && group.Members.Count > 0
                        ? L("dashboard.group.remove", "Remove")
                        : L("dashboard.group.remove_group", "Remove Group"),
                    MinWidth = 108,
                    Margin = new Thickness(8, 0, 6, 0),
                    Padding = new Thickness(10, 3, 10, 3),
                    Style = CreateGroupActionButtonStyle(container)
                };
                var removeNeutralStyle = CreateGroupActionButtonStyle(container);
                var removeArmedStyle = CreateGroupRemoveButtonStyle(container);

                var addButton = new Button
                {
                    Content = L("dashboard.group.add_account", "+ Add Account"),
                    MinWidth = 108,
                    Padding = new Thickness(10, 3, 10, 3),
                    Style = CreateGroupAddButtonStyle(container)
                };

                var dataGrid = CreateGroupMembersGrid(group, container);

                Action updateRemoveButtonState = () =>
                {
                    bool hasMembers = group.Members != null && group.Members.Count > 0;
                    if (!hasMembers)
                    {
                        removeButton.Content = L("dashboard.group.remove_group", "Remove Group");
                        removeButton.Style = removeNeutralStyle;
                        removeButton.IsEnabled = true;
                        return;
                    }

                    removeButton.Content = L("dashboard.group.remove", "Remove");
                    removeButton.Style = removeArmedStyle;
                    removeButton.IsEnabled = group.Members.Any(member => member != null && member.IsSelected);
                };

                Action queueRemoveButtonStateRefresh = () =>
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        updateRemoveButtonState();
                        SaveAccountGroupsToDisk();
                    }), DispatcherPriority.Background);

                addButton.Click += (s, e) => AddFollowerToGroup(group);
                removeButton.Click += (s, e) =>
                {
                    if (group.Members == null || group.Members.Count == 0)
                    {
                        _accountGroups.Remove(group);
                        SaveAccountGroupsToDisk();
                        RebuildAccountGroupsUi();
                        return;
                    }

                    var selectedRows = group.Members
                        .Where(member => member != null && member.IsSelected)
                        .ToList();
                    if (selectedRows.Count == 0)
                        return;

                    foreach (AccountGroupMemberRow member in selectedRows)
                        group.Members.Remove(member);

                    SaveAccountGroupsToDisk();
                    RebuildAccountGroupsUi();
                };

                dataGrid.CurrentCellChanged += (s, e) => queueRemoveButtonStateRefresh();
                dataGrid.CellEditEnding += (s, e) => queueRemoveButtonStateRefresh();
                dataGrid.PreviewMouseLeftButtonUp += (s, e) => queueRemoveButtonStateRefresh();
                dataGrid.PreviewKeyUp += (s, e) =>
                {
                    if (e != null && (e.Key == System.Windows.Input.Key.Space || e.Key == System.Windows.Input.Key.Enter))
                        queueRemoveButtonStateRefresh();
                };

                Grid.SetColumn(removeButton, 1);
                Grid.SetColumn(addButton, 2);
                titleRow.Children.Add(removeButton);
                titleRow.Children.Add(addButton);
                updateRemoveButtonState();

                Grid.SetRow(titleRow, 0);
                groupRoot.Children.Add(titleRow);

                Grid.SetRow(dataGrid, 1);
                groupRoot.Children.Add(dataGrid);

                container.Child = groupRoot;
                _accountGroupsHostPanel.Children.Add(container);
                index++;
            }

            var createButtonRow = new Grid
            {
                Margin = new Thickness(0, _accountGroups.Count > 0 ? 2 : 0, 0, 0)
            };
            createButtonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            createButtonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var createButton = new Button
            {
                Content = L("dashboard.group.create_group", "+ Create Group"),
                MinWidth = 128,
                Padding = new Thickness(12, 4, 12, 4),
                Style = CreateGroupAddButtonStyle(_accountGroupsHostPanel)
            };
            createButton.Click += OnCreateGroupClick;
            Grid.SetColumn(createButton, 1);
            createButtonRow.Children.Add(createButton);

            _accountGroupsHostPanel.Children.Add(createButtonRow);
        }

        private DataGrid CreateGroupMembersGrid(AccountGroupDefinition group, FrameworkElement context)
        {
            var grid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                Margin = new Thickness(0, 8, 0, 0),
                ItemsSource = group.Members,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Top
            };
            ConfigureDataGridForPageScroll(grid);

            ApplySkinResource(grid, Control.BackgroundProperty, "BackgroundMainWindow", "GridEntireBackground");
            ApplySkinResource(grid, Control.ForegroundProperty, "FontControlBrush", "FontTableBrush");
            ApplySkinResource(grid, Control.BorderBrushProperty, "BorderThinBrush", "TabControlBorderBrush");
            ApplySkinResource(grid, DataGrid.HorizontalGridLinesBrushProperty, "BorderThinBrush", "GridHeaderHighlight");
            ApplySkinResource(grid, DataGrid.VerticalGridLinesBrushProperty, "BorderThinBrush", "GridHeaderHighlight");
            grid.FocusVisualStyle = null;
            grid.BorderThickness = new Thickness(1, 1, 0, 0);
            grid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
            grid.AlternationCount = 2;
            ApplySkinResource(grid, DataGrid.RowBackgroundProperty, "BackgroundMainWindow", "GridEntireBackground");
            ApplySkinResource(grid, DataGrid.AlternatingRowBackgroundProperty, "BackgroundMainWindow", "GridEntireBackground", "BackgroundTextInput");

            var leftHeaderStyle = CreateSkinAwareColumnHeaderStyle(context, HorizontalAlignment.Left);
            var centerHeaderStyle = CreateSkinAwareColumnHeaderStyle(context, HorizontalAlignment.Center);
            var leftTextStyle = CreateTextBlockElementStyle(context, HorizontalAlignment.Left, TextAlignment.Left, new Thickness(6, 3, 6, 3));
            var centerTextStyle = CreateTextBlockElementStyle(context, HorizontalAlignment.Center, TextAlignment.Center, new Thickness(6, 3, 6, 3));
            var pnlTextStyle = CreatePnlTextBlockElementStyle(context, HorizontalAlignment.Center, TextAlignment.Center, new Thickness(6, 3, 6, 3), nameof(AccountGroupMemberRow.PnlSign));

            grid.ColumnHeaderStyle = _accountsGrid?.ColumnHeaderStyle ?? leftHeaderStyle;
            grid.RowStyle = _accountsGrid?.RowStyle ?? CreateSkinAwareRowStyle(context);
            grid.CellStyle = _accountsGrid?.CellStyle ?? CreateSkinAwareCellStyle(context);
            ApplyDataGridSelectionResources(grid, context);

            var selectColumn = CreateGroupSelectColumn(context, centerHeaderStyle);
            var selectAllCheckBox = new CheckBox
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0),
                Style = CreateGroupSelectionCheckBoxStyle(context),
                ToolTip = L("dashboard.group.select_all_tooltip", "Select all followers")
            };

            bool suppressSelectAllToggle = false;
            Action refreshSelectAllHeaderState = () =>
            {
                if (group == null || group.Members == null || group.Members.Count == 0)
                {
                    suppressSelectAllToggle = true;
                    selectAllCheckBox.IsChecked = false;
                    suppressSelectAllToggle = false;
                    return;
                }

                bool allSelected = group.Members.All(member => member != null && member.IsSelected);
                suppressSelectAllToggle = true;
                selectAllCheckBox.IsChecked = allSelected;
                suppressSelectAllToggle = false;
            };

            selectAllCheckBox.Checked += (s, e) =>
            {
                if (suppressSelectAllToggle || group == null || group.Members == null)
                    return;

                foreach (AccountGroupMemberRow member in group.Members)
                {
                    if (member != null)
                        member.IsSelected = true;
                }
            };

            selectAllCheckBox.Unchecked += (s, e) =>
            {
                if (suppressSelectAllToggle || group == null || group.Members == null)
                    return;

                foreach (AccountGroupMemberRow member in group.Members)
                {
                    if (member != null)
                        member.IsSelected = false;
                }
            };

            selectColumn.Header = selectAllCheckBox;
            grid.Columns.Add(selectColumn);

            var followerColumn = CreateTextColumn(L("dashboard.group.column.follower_account", "Follower Account"), nameof(AccountGroupMemberRow.FollowerAccount), leftTextStyle, leftHeaderStyle);
            followerColumn.IsReadOnly = true;
            followerColumn.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
            followerColumn.MinWidth = 180;
            grid.Columns.Add(followerColumn);

            var followerSizeColumn = CreateTextColumn(L("dashboard.column.size", "Size"), nameof(AccountGroupMemberRow.FollowerSizeDisplay), centerTextStyle, centerHeaderStyle);
            followerSizeColumn.IsReadOnly = true;
            followerSizeColumn.Width = DataGridLength.Auto;
            followerSizeColumn.MinWidth = 54;
            grid.Columns.Add(followerSizeColumn);

            var masterSizeColumn = CreateTextColumn(L("dashboard.group.column.master", "Master"), nameof(AccountGroupMemberRow.MasterSizeDisplay), centerTextStyle, centerHeaderStyle);
            masterSizeColumn.IsReadOnly = true;
            masterSizeColumn.Width = DataGridLength.Auto;
            masterSizeColumn.MinWidth = 54;
            grid.Columns.Add(masterSizeColumn);

            var ratioColumn = CreateEditableFollowerRatioColumn(context, centerHeaderStyle);
            ratioColumn.MinWidth = 56;
            grid.Columns.Add(ratioColumn);

            var maxDdColumn = CreateTextColumn(L("dashboard.group.column.max_dd", "Max DD"), nameof(AccountGroupMemberRow.MaxDd), centerTextStyle, centerHeaderStyle);
            maxDdColumn.IsReadOnly = true;
            maxDdColumn.Width = DataGridLength.Auto;
            maxDdColumn.MinWidth = 64;
            grid.Columns.Add(maxDdColumn);

            var maxLColumn = CreateTextColumn(L("dashboard.group.column.max_l", "Max L"), nameof(AccountGroupMemberRow.MaxL), centerTextStyle, centerHeaderStyle);
            maxLColumn.IsReadOnly = true;
            maxLColumn.Width = DataGridLength.Auto;
            maxLColumn.MinWidth = 54;
            grid.Columns.Add(maxLColumn);

            var maxContractsColumn = CreateTextColumn(L("dashboard.group.column.max_c", "Max C"), nameof(AccountGroupMemberRow.MaxContracts), centerTextStyle, centerHeaderStyle);
            maxContractsColumn.IsReadOnly = true;
            maxContractsColumn.Width = DataGridLength.Auto;
            maxContractsColumn.MinWidth = 64;
            grid.Columns.Add(maxContractsColumn);

            var positionColumn = CreateTextColumn(L("dashboard.group.column.position", "Position"), nameof(AccountGroupMemberRow.Position), centerTextStyle, centerHeaderStyle);
            positionColumn.IsReadOnly = true;
            positionColumn.Width = DataGridLength.Auto;
            positionColumn.MinWidth = 56;
            grid.Columns.Add(positionColumn);

            var pnlColumn = CreateTextColumn(L("dashboard.column.pnl", "PnL"), nameof(AccountGroupMemberRow.Pnl), pnlTextStyle, centerHeaderStyle);
            pnlColumn.IsReadOnly = true;
            pnlColumn.Width = DataGridLength.Auto;
            pnlColumn.MinWidth = 60;
            grid.Columns.Add(pnlColumn);

            var enableColumn = CreateGroupEnableColumn(context, centerHeaderStyle);
            grid.Columns.Add(enableColumn);

            grid.CellEditEnding += (s, e) =>
            {
                var editedMember = e?.Row?.Item as AccountGroupMemberRow;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (editedMember != null)
                    {
                        if (double.IsNaN(editedMember.Ratio) || double.IsInfinity(editedMember.Ratio) || editedMember.Ratio <= 0)
                            editedMember.Ratio = ComputeDefaultRatio(editedMember.FollowerSize, editedMember.MasterSize);
                        else
                            editedMember.Ratio = Math.Round(editedMember.Ratio, 4);
                    }
                    SaveAccountGroupsToDisk();
                }), DispatcherPriority.Background);
            };

            Action queueHeaderSelectionRefresh = () =>
                Dispatcher.BeginInvoke(new Action(refreshSelectAllHeaderState), DispatcherPriority.Background);

            grid.CurrentCellChanged += (s, e) => queueHeaderSelectionRefresh();
            grid.CellEditEnding += (s, e) =>
            {
                if (e?.Row?.Item is AccountGroupMemberRow ratioMember)
                    e.Row.ToolTip = BuildFollowerRatioMathTooltip(ratioMember);
                queueHeaderSelectionRefresh();
            };
            grid.LoadingRow += (s, e) =>
            {
                if (e?.Row?.Item is AccountGroupMemberRow member)
                    e.Row.ToolTip = BuildFollowerRatioMathTooltip(member);
            };
            grid.PreparingCellForEdit += (s, e) =>
            {
                if (e?.Column is DataGridTextColumn ratioColumn &&
                    ratioColumn.Binding is Binding ratioBinding &&
                    string.Equals(ratioBinding.Path?.Path, nameof(AccountGroupMemberRow.Ratio), StringComparison.Ordinal) &&
                    e.EditingElement is TextBox ratioEditor)
                {
                    ratioEditor.ToolTip = BuildFollowerRatioMathTooltip(e.Row?.Item as AccountGroupMemberRow);
                }
            };
            grid.PreviewMouseLeftButtonUp += (s, e) => queueHeaderSelectionRefresh();
            grid.PreviewKeyUp += (s, e) =>
            {
                if (e != null && (e.Key == System.Windows.Input.Key.Space || e.Key == System.Windows.Input.Key.Enter))
                    queueHeaderSelectionRefresh();
            };
            grid.Loaded += (s, e) => queueHeaderSelectionRefresh();

            return grid;
        }

        private static void ApplyDataGridSelectionResources(DataGrid grid, FrameworkElement context)
        {
            if (grid == null || context == null)
                return;

            object selectionForeground = null;
            foreach (string key in new[] { "FontControlBrush", "FontTableBrush", "GridRowForeground" })
            {
                selectionForeground = FindSkinResource(context, key);
                if (selectionForeground is Brush)
                    break;
            }

            grid.Resources[SystemColors.HighlightBrushKey] = Brushes.Transparent;
            grid.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = Brushes.Transparent;

            if (selectionForeground is Brush foregroundBrush)
            {
                grid.Resources[SystemColors.HighlightTextBrushKey] = foregroundBrush;
                grid.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = foregroundBrush;
            }
        }

        private static DataGridTemplateColumn CreateGroupSelectColumn(FrameworkElement context, Style headerStyle)
        {
            var checkFactory = new FrameworkElementFactory(typeof(CheckBox));
            checkFactory.SetBinding(
                System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,
                new Binding(nameof(AccountGroupMemberRow.IsSelected))
                {
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                });
            checkFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            checkFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            checkFactory.SetValue(FrameworkElement.StyleProperty, CreateGroupSelectionCheckBoxStyle(context));

            var template = new DataTemplate
            {
                VisualTree = checkFactory
            };

            return new DataGridTemplateColumn
            {
                Header = string.Empty,
                Width = new DataGridLength(44),
                HeaderStyle = headerStyle,
                CellTemplate = template,
                CellEditingTemplate = template
            };
        }

        private DataGridTemplateColumn CreateGroupEnableColumn(FrameworkElement context, Style headerStyle)
        {
            var checkFactory = new FrameworkElementFactory(typeof(CheckBox));
            checkFactory.SetBinding(
                System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,
                new Binding(nameof(AccountGroupMemberRow.IsEnabled))
                {
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                });
            checkFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            checkFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            checkFactory.SetValue(FrameworkElement.StyleProperty, CreateGroupEnableCheckBoxStyle(context));

            var template = new DataTemplate
            {
                VisualTree = checkFactory
            };

            var column = new DataGridTemplateColumn
            {
                Header = L("dashboard.group.column.enable", "Enable"),
                Width = DataGridLength.Auto,
                HeaderStyle = headerStyle,
                CellTemplate = template,
                CellEditingTemplate = template
            };
            BindLocalizedColumnHeader(column, "dashboard.group.column.enable", "Enable");
            return column;
        }

        private static Style CreateGroupEnableCheckBoxStyle(FrameworkElement context)
        {
            var style = new Style(typeof(CheckBox));

            ApplySkinSetter(style, Control.BorderBrushProperty, context, "BorderThinBrush", "TabControlBorderBrush", "GridHeaderHighlight");
            ApplySkinSetter(style, Control.ForegroundProperty, context, "FontControlBrush", "FontTableBrush", "GridRowForeground");
            ApplySkinSetter(style, Control.BackgroundProperty, context, "BackgroundTextInput", "BackgroundMainWindow", "GridEntireBackground");
            style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
            style.Setters.Add(new Setter(FrameworkElement.WidthProperty, 12.0));
            style.Setters.Add(new Setter(FrameworkElement.HeightProperty, 12.0));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));

            var template = new ControlTemplate(typeof(CheckBox));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.Name = "CheckBoxBorder";
            borderFactory.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            borderFactory.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            borderFactory.SetValue(Border.SnapsToDevicePixelsProperty, true);

            var markFactory = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
            markFactory.Name = "CheckMark";
            markFactory.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse("M 2,7 L 5,10 L 11,3"));
            markFactory.SetValue(System.Windows.Shapes.Path.StrokeThicknessProperty, 1.8);
            markFactory.SetValue(System.Windows.Shapes.Path.StrokeProperty, new TemplateBindingExtension(Control.ForegroundProperty));
            markFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            markFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            markFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(0, -1, 0, 0));
            markFactory.SetValue(UIElement.RenderTransformOriginProperty, new Point(0.5, 0.5));
            markFactory.SetValue(UIElement.RenderTransformProperty, new ScaleTransform(7.0 / 9.0, 7.0 / 9.0));
            markFactory.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            markFactory.SetValue(UIElement.SnapsToDevicePixelsProperty, true);

            borderFactory.AppendChild(markFactory);
            template.VisualTree = borderFactory;

            var checkedTrigger = new Trigger { Property = System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, Value = true };
            checkedTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "CheckMark"));
            checkedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, TealAccentBrush, "CheckBoxBorder"));
            checkedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, TealAccentBrush, "CheckBoxBorder"));
            checkedTrigger.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            template.Triggers.Add(checkedTrigger);

            string hoverBorderKey = FindSkinResourceKey(context, "GridHeaderHighlight", "BorderThinBrush", "TabControlBorderBrush");
            if (!string.IsNullOrWhiteSpace(hoverBorderKey))
            {
                var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
                hoverTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension(hoverBorderKey)));
                template.Triggers.Add(hoverTrigger);
            }

            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            return style;
        }

        private static Style CreateGroupSelectionCheckBoxStyle(FrameworkElement context)
        {
            var style = new Style(typeof(CheckBox));

            ApplySkinSetter(style, Control.BorderBrushProperty, context, "BorderThinBrush", "TabControlBorderBrush", "GridHeaderHighlight");
            ApplySkinSetter(style, Control.ForegroundProperty, context, "FontControlBrush", "FontTableBrush", "GridRowForeground");
            ApplySkinSetter(style, Control.BackgroundProperty, context, "BackgroundTextInput", "BackgroundMainWindow", "GridEntireBackground");
            style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
            style.Setters.Add(new Setter(FrameworkElement.WidthProperty, 12.0));
            style.Setters.Add(new Setter(FrameworkElement.HeightProperty, 12.0));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));

            var template = new ControlTemplate(typeof(CheckBox));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.Name = "CheckBoxBorder";
            borderFactory.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            borderFactory.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            borderFactory.SetValue(Border.SnapsToDevicePixelsProperty, true);

            var markFactory = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
            markFactory.Name = "CheckMark";
            markFactory.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse("M 2,7 L 5,10 L 11,3"));
            markFactory.SetValue(System.Windows.Shapes.Path.StrokeThicknessProperty, 1.8);
            markFactory.SetValue(System.Windows.Shapes.Path.StrokeProperty, new TemplateBindingExtension(Control.ForegroundProperty));
            markFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            markFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            markFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(0, -1, 0, 0));
            markFactory.SetValue(UIElement.RenderTransformOriginProperty, new Point(0.5, 0.5));
            markFactory.SetValue(UIElement.RenderTransformProperty, new ScaleTransform(7.0 / 9.0, 7.0 / 9.0));
            markFactory.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            markFactory.SetValue(UIElement.SnapsToDevicePixelsProperty, true);

            borderFactory.AppendChild(markFactory);
            template.VisualTree = borderFactory;

            var checkedTrigger = new Trigger { Property = System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, Value = true };
            checkedTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "CheckMark"));
            template.Triggers.Add(checkedTrigger);

            string hoverBorderKey = FindSkinResourceKey(context, "GridHeaderHighlight", "BorderThinBrush", "TabControlBorderBrush");
            if (!string.IsNullOrWhiteSpace(hoverBorderKey))
            {
                var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
                hoverTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension(hoverBorderKey)));
                template.Triggers.Add(hoverTrigger);
            }

            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            return style;
        }

        private DataGridTextColumn CreateEditableFollowerRatioColumn(FrameworkElement context, Style centerHeaderStyle)
        {
            var column = new DataGridTextColumn
            {
                Header = L("dashboard.group.column.ratio", "Ratio"),
                Binding = new Binding(nameof(AccountGroupMemberRow.Ratio))
                {
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.LostFocus,
                    StringFormat = "0.####",
                    ConverterCulture = CultureInfo.CurrentCulture
                },
                Width = DataGridLength.Auto,
                ElementStyle = CreateEditableFollowerRatioElementStyle(context),
                EditingElementStyle = CreateEditableRatioTextBoxStyle(context),
                HeaderStyle = centerHeaderStyle
            };
            return column;
        }

        private static Style CreateEditableFollowerRatioElementStyle(FrameworkElement context)
        {
            var baseStyle = FindSkinStyle(context, typeof(TextBlock));
            var style = new Style(typeof(TextBlock), baseStyle);
            ApplySkinSetter(style, TextBlock.ForegroundProperty, context, "FontControlBrush", "FontTableBrush");
            style.Setters.Add(new Setter(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center));
            style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Center));
            style.Setters.Add(new Setter(TextBlock.PaddingProperty, new Thickness(6, 3, 16, 3)));
            // ponytail: static style factory — literal fallback; row LoadingRow sets localized tooltip
            style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, "Double-click to edit ratio"));
            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromArgb(28, 255, 255, 255))));
            style.Triggers.Add(hoverTrigger);
            return style;
        }

        private string BuildFollowerRatioMathTooltip(AccountGroupMemberRow member)
        {
            if (member == null)
                return L("dashboard.group.ratio_math", "Follower contracts = master contracts × ratio");

            double ratio = member.Ratio;
            if (double.IsNaN(ratio) || double.IsInfinity(ratio) || ratio <= 0)
                ratio = 1.0;

            int exampleMaster = 2;
            int exampleFollower = Math.Max(1, (int)Math.Round(exampleMaster * ratio, MidpointRounding.AwayFromZero));
            return Lf(
                "dashboard.group.ratio_math_format",
                "master {0} × ratio {1} ⇒ follower {2} contracts",
                exampleMaster.ToString(CultureInfo.CurrentCulture),
                ratio.ToString("0.####", CultureInfo.CurrentCulture),
                exampleFollower.ToString(CultureInfo.CurrentCulture));
        }

        private static Style CreateEditableRatioTextBoxStyle(FrameworkElement context)
        {
            var baseStyle = FindSkinStyle(context, typeof(TextBox));
            var style = new Style(typeof(TextBox), baseStyle);
            ApplySkinSetter(style, Control.BackgroundProperty, context, "BackgroundTextInput", "BackgroundMainWindow", "GridEntireBackground");
            ApplySkinSetter(style, Control.ForegroundProperty, context, "FontControlBrush", "FontTableBrush");
            ApplySkinSetter(style, Control.BorderBrushProperty, context, "BorderThinBrush", "TabControlBorderBrush");
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(4, 2, 4, 2)));
            style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
            return style;
        }

        private List<string> GetConnectedAccountNames()
        {
            return _accountRows
                .Where(row => row != null && !string.IsNullOrWhiteSpace(row.DisplayName))
                .Select(row => row.DisplayName.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<string> GetMasterOptionsForGroup(IList<string> connectedAccounts, string currentMaster)
        {
            var options = new List<string>();
            if (connectedAccounts != null)
                options.AddRange(connectedAccounts.Where(name => !string.IsNullOrWhiteSpace(name)));

            if (!string.IsNullOrWhiteSpace(currentMaster) &&
                !options.Contains(currentMaster, StringComparer.OrdinalIgnoreCase))
            {
                options.Insert(0, currentMaster);
            }

            return options
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private void UpdateGroupMasterSelection(AccountGroupDefinition group, string selectedMaster)
        {
            if (group == null || string.IsNullOrWhiteSpace(selectedMaster))
                return;

            group.MasterAccount = selectedMaster;
            group.MasterSize = ResolveAccountSizeForName(selectedMaster, group.MasterSize > 0 ? group.MasterSize : 25000);

            if (group.Members != null)
            {
                foreach (AccountGroupMemberRow member in group.Members)
                {
                    if (member == null)
                        continue;

                    member.MasterAccount = group.MasterAccount;
                    member.MasterSize = group.MasterSize;
                    member.MasterSizeDisplay = FormatAccountSize(group.MasterSize);
                    member.Ratio = ComputeDefaultRatio(member.FollowerSize, group.MasterSize);
                    member.IsSelected = false;
                }
            }

            SaveAccountGroupsToDisk();
        }

        private void AddFollowerToGroup(AccountGroupDefinition group)
        {
            if (group == null)
                return;

            if (IsFreeLitePlan())
            {
                int maxFollowers = Math.Max(1, _licenseCacheState?.MaxFollowersPerGroup ?? 2);
                int currentFollowers = group.Members?.Count(member => member != null && !string.IsNullOrWhiteSpace(member.FollowerAccount)) ?? 0;
                if (currentFollowers >= maxFollowers)
                {
                    string message = $"Free Lite limit reached: maximum {maxFollowers} follower(s) per group.";
                    AppendJournal("System", "Policy", message);
                    RaiseCriticalWarning("System", message, "PolicyFollowerLimit", unlocksTrading: false);
                    return;
                }
            }

            var existingFollowers = new HashSet<string>(
                group.Members.Select(m => m.FollowerAccount),
                StringComparer.OrdinalIgnoreCase);

            var candidates = _accountRows
                .Where(r => r != null && !string.IsNullOrWhiteSpace(r.DisplayName))
                .Select(r => r.DisplayName)
                .Where(name => !name.Equals(group.MasterAccount, StringComparison.OrdinalIgnoreCase))
                .Where(name => !existingFollowers.Contains(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (candidates.Count == 0)
                return;

            string selectedFollower = ShowAccountPickerDialog(
                L("dialog.add_follower.title", "Add Follower Account"),
                L("dialog.add_follower.prompt", "Pick a follower account:"),
                candidates);
            if (string.IsNullOrWhiteSpace(selectedFollower))
                return;

            double masterSize = group.MasterSize > 0 ? group.MasterSize : ResolveAccountSizeForName(group.MasterAccount, 25000);
            double followerSize = ResolveAccountSizeForName(selectedFollower, masterSize);
            double ratio = ComputeDefaultRatio(followerSize, masterSize);
            AccountGridRow followerRow = FindAccountRowByName(selectedFollower);

            string followerPnl = followerRow?.TotalPnl;
            if (string.IsNullOrWhiteSpace(followerPnl))
                followerPnl = "-";
            string followerPnlSign = followerRow?.TotalPnlSign;
            if (string.IsNullOrWhiteSpace(followerPnlSign))
                followerPnlSign = "Neutral";
            string followerMaxDd = followerRow?.MaxDrawdown;
            if (string.IsNullOrWhiteSpace(followerMaxDd))
                followerMaxDd = "-";
            string followerMaxL = followerRow?.IntratradeDrawdown;
            if (string.IsNullOrWhiteSpace(followerMaxL))
                followerMaxL = "-";
            string followerMaxContracts = followerRow?.MaxContracts;
            if (string.IsNullOrWhiteSpace(followerMaxContracts))
                followerMaxContracts = "-";
            string followerPosition = followerRow?.Position;
            if (string.IsNullOrWhiteSpace(followerPosition))
                followerPosition = "0";

            group.Members.Add(new AccountGroupMemberRow
            {
                MasterAccount = group.MasterAccount,
                MasterSize = masterSize,
                MasterSizeDisplay = FormatAccountSize(masterSize),
                FollowerAccount = selectedFollower,
                FollowerSize = followerSize,
                FollowerSizeDisplay = FormatAccountSize(followerSize),
                Ratio = ratio,
                IsSelected = false,
                IsEnabled = false,
                Pnl = followerPnl,
                PnlSign = followerPnlSign,
                MaxDd = followerMaxDd,
                MaxL = followerMaxL,
                MaxContracts = followerMaxContracts,
                Position = followerPosition
            });

            SaveAccountGroupsToDisk();
            RebuildAccountGroupsUi();
        }

        private string ShowAccountPickerDialog(string title, string prompt, IList<string> options)
        {
            if (options == null || options.Count == 0)
                return null;

            var dialog = new NTWindow
            {
                Caption = title,
                Title = title,
                Width = 360,
                Height = 186,
                MinWidth = 320,
                MinHeight = 170,
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
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            ApplySkinResource(layout, Panel.BackgroundProperty, "BackgroundMainWindow", "GridEntireBackground", "BackgroundTextInput");

            var promptText = new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 6) };
            ApplySkinResource(promptText, TextBlock.ForegroundProperty, "FontControlBrush", "FontTableBrush");
            Grid.SetRow(promptText, 0);
            layout.Children.Add(promptText);

            var picker = new ComboBox
            {
                ItemsSource = options,
                SelectedIndex = 0,
                HorizontalContentAlignment = HorizontalAlignment.Left
            };
            picker.Style = CreateSkinAwareComboBoxStyle(layout);
            Grid.SetRow(picker, 1);
            layout.Children.Add(picker);

            var divider = new Border
            {
                Height = 1,
                Margin = new Thickness(0, 10, 0, 0)
            };
            ApplySkinResource(divider, Border.BackgroundProperty, "BorderThinBrush", "TabControlBorderBrush", "GridHeaderHighlight");
            Grid.SetRow(divider, 2);
            layout.Children.Add(divider);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };

            bool accepted = false;
            var ok = new Button
            {
                Content = L("dialog.common.ok", "OK"),
                MinWidth = 72,
                Margin = new Thickness(0, 0, 8, 0),
                Style = CreateGroupActionButtonStyle(layout)
            };
            ok.IsDefault = true;
            ok.Click += (s, e) =>
            {
                accepted = true;
                dialog.Close();
            };
            buttons.Children.Add(ok);

            var cancel = new Button
            {
                Content = L("dialog.common.cancel", "Cancel"),
                MinWidth = 72,
                Style = CreateGroupActionButtonStyle(layout)
            };
            cancel.IsCancel = true;
            cancel.Click += (s, e) => dialog.Close();
            buttons.Children.Add(cancel);

            Grid.SetRow(buttons, 3);
            layout.Children.Add(buttons);

            shell.Child = layout;
            dialog.Content = shell;
            dialog.ShowDialog();

            if (!accepted)
                return null;

            return picker.SelectedItem as string;
        }

        private double ResolveAccountSizeForName(string accountName, double fallback = 0)
        {
            if (!string.IsNullOrWhiteSpace(accountName))
            {
                AccountGridRow row = FindAccountRowByName(accountName);
                if (row != null)
                {
                    double? parsed = ParseAccountSize(row.AccountSizeSelection);
                    if (parsed.HasValue && parsed.Value > 0)
                        return parsed.Value;
                }

                if (_selectionOverrides.TryGetValue(accountName, out AccountSelectionOverride selectionOverride) &&
                    selectionOverride?.AccountSize.HasValue == true &&
                    selectionOverride.AccountSize.Value > 0)
                {
                    return selectionOverride.AccountSize.Value;
                }
            }

            return fallback > 0 ? fallback : 0;
        }

        private double ResolveAccountTotalPnlRaw(string accountName)
        {
            AccountGridRow row = FindAccountRowByName(accountName);
            return row?.TotalPnlRaw ?? 0;
        }

        private AccountGridRow FindAccountRowByName(string accountName)
        {
            if (string.IsNullOrWhiteSpace(accountName))
                return null;

            return _accountRows.FirstOrDefault(r =>
                r != null &&
                !string.IsNullOrWhiteSpace(r.DisplayName) &&
                r.DisplayName.Equals(accountName, StringComparison.OrdinalIgnoreCase));
        }

        private static double ComputeDefaultRatio(double followerSize, double masterSize)
        {
            if (masterSize <= 0 || followerSize <= 0)
                return 1.0;

            return Math.Round(followerSize / masterSize, 4);
        }

        private static void AnimateButtonColors(
            Button button,
            Color backgroundFrom,
            Color backgroundTo,
            Color borderFrom,
            Color borderTo,
            Color foregroundFrom,
            Color foregroundTo)
        {
            if (button == null)
                return;

            var easing = new CubicEase { EasingMode = EasingMode.EaseInOut };
            var duration = new Duration(TimeSpan.FromMilliseconds(250));

            var backgroundBrush = new SolidColorBrush(backgroundFrom);
            var borderBrush = new SolidColorBrush(borderFrom);
            var foregroundBrush = new SolidColorBrush(foregroundFrom);

            button.Background = backgroundBrush;
            button.BorderBrush = borderBrush;
            button.Foreground = foregroundBrush;

            backgroundBrush.BeginAnimation(
                SolidColorBrush.ColorProperty,
                new ColorAnimation(backgroundTo, duration) { EasingFunction = easing },
                HandoffBehavior.SnapshotAndReplace);
            borderBrush.BeginAnimation(
                SolidColorBrush.ColorProperty,
                new ColorAnimation(borderTo, duration) { EasingFunction = easing },
                HandoffBehavior.SnapshotAndReplace);
            foregroundBrush.BeginAnimation(
                SolidColorBrush.ColorProperty,
                new ColorAnimation(foregroundTo, duration) { EasingFunction = easing },
                HandoffBehavior.SnapshotAndReplace);
        }

        private static Color ResolveBrushColor(Brush brush, Color fallback)
        {
            if (brush is SolidColorBrush solidBrush)
                return solidBrush.Color;

            if (brush is GradientBrush gradientBrush &&
                gradientBrush.GradientStops != null &&
                gradientBrush.GradientStops.Count > 0)
            {
                return gradientBrush.GradientStops[0].Color;
            }

            return fallback;
        }

        private UIElement CreateAccountsTab()
        {
            return CreateAccountsTabImpl();
        }

        private static DataGridTextColumn CreateTextColumn(
            string header,
            string bindingPath,
            Style elementStyle,
            Style headerStyle,
            bool visible = true,
            double minWidth = 36)
        {
            var column = new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding(bindingPath),
                Width = DataGridLength.Auto,
                ElementStyle = elementStyle,
                HeaderStyle = headerStyle
            };

            if (minWidth > 0)
                column.MinWidth = minWidth;

            if (!visible)
                column.Visibility = Visibility.Collapsed;

            return column;
        }

        private static DataGridTemplateColumn CreateRiskBarColumn(
            string header,
            Style headerStyle,
            Style percentTextStyle,
            FrameworkElement context)
        {
            var trackStyle = new Style(typeof(Border));
            ApplySkinSetter(trackStyle, Border.BackgroundProperty, context, "BackgroundMainWindow", "GridEntireBackground", "BackgroundTextInput");
            ApplySkinSetter(trackStyle, Border.BorderBrushProperty, context, "BorderThinBrush", "TabControlBorderBrush");
            trackStyle.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(1)));

            var safeFillStyle = new Style(typeof(Border));
            safeFillStyle.Setters.Add(new Setter(Border.BackgroundProperty, TealAccentBrush));

            var usedFillStyle = new Style(typeof(Border));
            usedFillStyle.Setters.Add(new Setter(Border.BackgroundProperty, OrangeAccentBrush));

            var rootFactory = new FrameworkElementFactory(typeof(StackPanel));
            rootFactory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            rootFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            rootFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);

            var trackFactory = new FrameworkElementFactory(typeof(Border));
            trackFactory.SetValue(FrameworkElement.WidthProperty, 110.0);
            trackFactory.SetValue(FrameworkElement.HeightProperty, 5.0);
            trackFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
            trackFactory.SetValue(FrameworkElement.StyleProperty, trackStyle);

            var fillHostFactory = new FrameworkElementFactory(typeof(StackPanel));
            fillHostFactory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            fillHostFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            fillHostFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

            var safeFillFactory = new FrameworkElementFactory(typeof(Border));
            safeFillFactory.SetValue(FrameworkElement.HeightProperty, 5.0);
            safeFillFactory.SetBinding(FrameworkElement.WidthProperty, new Binding(nameof(AccountGridRow.HeadroomSafeWidth)));
            safeFillFactory.SetValue(FrameworkElement.StyleProperty, safeFillStyle);
            fillHostFactory.AppendChild(safeFillFactory);

            var usedFillFactory = new FrameworkElementFactory(typeof(Border));
            usedFillFactory.SetValue(FrameworkElement.HeightProperty, 5.0);
            usedFillFactory.SetBinding(FrameworkElement.WidthProperty, new Binding(nameof(AccountGridRow.HeadroomUsedWidth)));
            usedFillFactory.SetValue(FrameworkElement.StyleProperty, usedFillStyle);
            fillHostFactory.AppendChild(usedFillFactory);

            trackFactory.AppendChild(fillHostFactory);
            rootFactory.AppendChild(trackFactory);

            var percentFactory = new FrameworkElementFactory(typeof(TextBlock));
            percentFactory.SetBinding(TextBlock.TextProperty, new Binding(nameof(AccountGridRow.RiskDisplay)));
            if (percentTextStyle != null)
                percentFactory.SetValue(FrameworkElement.StyleProperty, percentTextStyle);
            rootFactory.AppendChild(percentFactory);

            var template = new DataTemplate { VisualTree = rootFactory };

            return new DataGridTemplateColumn
            {
                Header = header,
                Width = DataGridLength.Auto,
                MinWidth = 140,
                HeaderStyle = headerStyle,
                CellTemplate = template
            };
        }

        private DataGridTemplateColumn CreateWarningDismissColumn(Style headerStyle, FrameworkElement context)
        {
            var buttonFactory = new FrameworkElementFactory(typeof(Button));
            buttonFactory.SetValue(ContentControl.ContentProperty, L("journal.column.dismiss", "Dismiss"));
            buttonFactory.SetValue(FrameworkElement.MinWidthProperty, 92.0);
            buttonFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            buttonFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            buttonFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 1, 0, 1));
            buttonFactory.SetBinding(FrameworkElement.TagProperty, new Binding("."));
            buttonFactory.SetValue(FrameworkElement.StyleProperty, CreateDismissWarningButtonStyle(context));
            buttonFactory.AddHandler(Button.ClickEvent, new RoutedEventHandler(OnDismissWarningButtonClick));

            var template = new DataTemplate { VisualTree = buttonFactory };
            var column = new DataGridTemplateColumn
            {
                Header = L("journal.column.dismiss", "Dismiss"),
                Width = DataGridLength.Auto,
                MinWidth = 104,
                HeaderStyle = headerStyle,
                CellTemplate = template
            };
            BindLocalizedColumnHeader(column, "journal.column.dismiss", "Dismiss");
            return column;
        }

        private Style CreateDismissWarningButtonStyle(FrameworkElement context)
        {
            var baseStyle = FindSkinStyle(context, typeof(Button));
            var style = new Style(typeof(Button), baseStyle);
            string neutralBackgroundKey = FindSkinResourceKey(context, "BackgroundMainWindow", "GridEntireBackground", "BackgroundTextInput");
            string neutralForegroundKey = FindSkinResourceKey(context, "FontControlBrush", "FontTableBrush", "GridRowForeground");
            string neutralBorderKey = FindSkinResourceKey(context, "BorderThinBrush", "TabControlBorderBrush");
            double? tableFontSize = FindSkinDouble(context, "FontTableHeight", "FontControlHeight", "FontHeaderLevel4Height");

            style.Setters.Add(new Setter(Control.BackgroundProperty, OrangeAccentBrush));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, OrangeAccentBrush));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 1, 8, 1)));
            style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
            style.Setters.Add(new Setter(Control.CursorProperty, System.Windows.Input.Cursors.Hand));
            if (tableFontSize.HasValue)
                style.Setters.Add(new Setter(Control.FontSizeProperty, tableFontSize.Value));

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, OrangeAccentBrush));
            hoverTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, OrangeAccentBrush));
            hoverTrigger.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            style.Triggers.Add(hoverTrigger);

            var dismissedTrigger = new DataTrigger
            {
                Binding = new Binding(nameof(CriticalWarningEntry.IsDismissed)),
                Value = true
            };
            dismissedTrigger.Setters.Add(new Setter(UIElement.IsEnabledProperty, false));
            dismissedTrigger.Setters.Add(new Setter(ContentControl.ContentProperty, "-"));
            dismissedTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.58));
            if (!string.IsNullOrWhiteSpace(neutralBackgroundKey))
                dismissedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension(neutralBackgroundKey)));
            if (!string.IsNullOrWhiteSpace(neutralForegroundKey))
                dismissedTrigger.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension(neutralForegroundKey)));
            if (!string.IsNullOrWhiteSpace(neutralBorderKey))
                dismissedTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension(neutralBorderKey)));
            style.Triggers.Add(dismissedTrigger);

            return style;
        }

        private static DataGridTemplateColumn CreateEditableComboColumn(
            string header,
            string selectedBindingPath,
            string optionsBindingPath,
            Style elementStyle,
            Style headerStyle,
            Style comboStyle,
            double minWidth = 36)
        {
            var displayTextFactory = new FrameworkElementFactory(typeof(TextBlock));
            displayTextFactory.SetBinding(TextBlock.TextProperty, new Binding(selectedBindingPath));
            if (elementStyle != null)
                displayTextFactory.SetValue(FrameworkElement.StyleProperty, elementStyle);

            var displayTemplate = new DataTemplate
            {
                VisualTree = displayTextFactory
            };

            var editorFactory = new FrameworkElementFactory(typeof(ComboBox));
            editorFactory.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(optionsBindingPath));
            editorFactory.SetBinding(ComboBox.SelectedItemProperty, new Binding(selectedBindingPath)
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            editorFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            editorFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            editorFactory.SetValue(ComboBox.IsEditableProperty, false);
            editorFactory.SetValue(ComboBox.IsTextSearchEnabledProperty, true);
            if (comboStyle != null)
                editorFactory.SetValue(FrameworkElement.StyleProperty, comboStyle);

            var editingTemplate = new DataTemplate
            {
                VisualTree = editorFactory
            };

            return new DataGridTemplateColumn
            {
                Header = header,
                Width = DataGridLength.Auto,
                MinWidth = minWidth,
                HeaderStyle = headerStyle,
                CellTemplate = displayTemplate,
                CellEditingTemplate = editingTemplate
            };
        }

        private static Style CreateSkinAwareTabItemStyle(FrameworkElement context)
        {
            var style = new Style(typeof(TabItem));

            string selectedBackgroundKey = FindSkinResourceKey(context, "BackgroundTextInput", "GridEntireBackground", "BackgroundMainWindow");
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.ForegroundProperty, UiPrimaryTextBrush));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));

            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 0, 2)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(14, 7, 14, 7)));
            style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 30d));
            style.Setters.Add(new Setter(Control.FontWeightProperty, UiTabFontWeight));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Stretch));
            style.Setters.Add(new Setter(Control.CursorProperty, Cursors.Hand));
            style.Setters.Add(new Setter(UIElement.OpacityProperty, 1.0));
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
            contentFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 1, 0, 0));
            contentFactory.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
            borderFactory.AppendChild(contentFactory);
            rootFactory.AppendChild(borderFactory);

            var indicatorFactory = new FrameworkElementFactory(typeof(Border));
            indicatorFactory.Name = "TabBottomIndicator";
            indicatorFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Bottom);
            indicatorFactory.SetValue(FrameworkElement.HeightProperty, 2.0);
            indicatorFactory.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            indicatorFactory.SetValue(UIElement.IsHitTestVisibleProperty, false);
            indicatorFactory.SetValue(Border.SnapsToDevicePixelsProperty, true);
            rootFactory.AppendChild(indicatorFactory);

            template.VisualTree = rootFactory;

            var selectedTrigger = new Trigger { Property = TabItem.IsSelectedProperty, Value = true };
            if (!string.IsNullOrWhiteSpace(selectedBackgroundKey))
                selectedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension(selectedBackgroundKey)));
            selectedTrigger.Setters.Add(new Setter(Control.ForegroundProperty, UiPrimaryTextBrush));
            selectedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, OrangeAccentBrush, "TabBottomIndicator"));
            selectedTrigger.Setters.Add(new Setter(Control.FontWeightProperty, UiTabFontWeight));
            selectedTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 1.0));
            template.Triggers.Add(selectedTrigger);

            var disabledTrigger = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabledTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.6));
            template.Triggers.Add(disabledTrigger);

            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            return style;
        }

        private static Style CreateSkinAwareComboBoxStyle(FrameworkElement context)
        {
            var baseStyle = FindSkinStyle(context, typeof(ComboBox));
            var style = new Style(typeof(ComboBox), baseStyle);

            ApplySkinSetter(style, Control.BackgroundProperty, context, "BackgroundTextInput", "BackgroundMainWindow", "GridEntireBackground");
            ApplySkinSetter(style, Control.ForegroundProperty, context, "FontControlBrush", "FontTableBrush");
            ApplySkinSetter(style, Control.BorderBrushProperty, context, "BorderThinBrush", "TabControlBorderBrush");

            double? sharedFontSize = FindSkinDouble(context, "FontTableHeight", "FontHeaderLevel4Height", "FontControlHeight");
            if (sharedFontSize.HasValue)
                style.Setters.Add(new Setter(Control.FontSizeProperty, sharedFontSize.Value));

            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 3, 8, 3)));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Control.MinHeightProperty, 28d));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
            style.Setters.Add(new Setter(Control.CursorProperty, System.Windows.Input.Cursors.Hand));
            style.Setters.Add(new Setter(ItemsControl.ItemContainerStyleProperty, CreateSkinAwareComboBoxItemStyle(context)));
            ApplyTealAccentResourceOverrides(style);
            Brush neutralBorder = FindSkinBrush(context, "BorderThinBrush", "TabControlBorderBrush") ?? Brushes.Gray;
            Brush neutralBackground = FindSkinBrush(context, "BackgroundTextInput", "BackgroundMainWindow", "GridEntireBackground") ?? Brushes.Transparent;
            Brush neutralForeground = FindSkinBrush(context, "FontControlBrush", "FontTableBrush") ?? Brushes.White;

            style.Resources["ComboBox.Static.Background"] = neutralBackground;
            style.Resources["ComboBox.Static.Border"] = neutralBorder;
            style.Resources["ComboBox.Static.Glyph"] = neutralForeground;
            style.Resources["ComboBox.MouseOver.Background"] = neutralBackground;
            style.Resources["ComboBox.MouseOver.Border"] = TealAccentBrush;
            style.Resources["ComboBox.MouseOver.Glyph"] = neutralForeground;
            style.Resources["ComboBox.Focused.Background"] = neutralBackground;
            style.Resources["ComboBox.Focused.Border"] = TealAccentBrush;
            style.Resources["ComboBox.Focused.Glyph"] = neutralForeground;
            style.Resources["ComboBox.Pressed.Background"] = neutralBackground;
            style.Resources["ComboBox.Pressed.Border"] = TealAccentBrush;
            style.Resources["ComboBox.Pressed.Glyph"] = neutralForeground;

            style.Resources["ComboBox.Disabled.Background"] = neutralBackground;
            style.Resources["ComboBox.Disabled.Border"] = neutralBorder;
            style.Resources["ComboBox.Disabled.Glyph"] = neutralForeground;
            style.Resources["ComboBox.Disabled.Editable.Background"] = neutralBackground;
            style.Resources["ComboBox.Disabled.Editable.Border"] = neutralBorder;

            style.Resources["ComboBox.Editable.Background"] = neutralBackground;
            style.Resources["ComboBox.Editable.Border"] = neutralBorder;
            style.Resources["ComboBox.Editable.Glyph"] = neutralForeground;
            style.Resources["ComboBox.Editable.MouseOver.Background"] = neutralBackground;
            style.Resources["ComboBox.Editable.MouseOver.Border"] = TealAccentBrush;
            style.Resources["ComboBox.Editable.MouseOver.Glyph"] = neutralForeground;
            style.Resources["ComboBox.Editable.Focused.Background"] = neutralBackground;
            style.Resources["ComboBox.Editable.Focused.Border"] = TealAccentBrush;
            style.Resources["ComboBox.Editable.Focused.Glyph"] = neutralForeground;
            style.Resources["ComboBox.Editable.Button.Background"] = neutralBackground;
            style.Resources["ComboBox.Editable.Button.Border"] = neutralBorder;
            style.Resources["ComboBox.Editable.Button.MouseOver.Background"] = neutralBackground;
            style.Resources["ComboBox.Editable.Button.MouseOver.Border"] = TealAccentBrush;
            style.Resources["ComboBox.Editable.Button.Pressed.Background"] = neutralBackground;
            style.Resources["ComboBox.Editable.Button.Pressed.Border"] = TealAccentBrush;

            // Some skins use internal ToggleButton state keys for the active (opened) shell border.
            style.Resources["ToggleButton.Static.Background"] = neutralBackground;
            style.Resources["ToggleButton.Static.Border"] = neutralBorder;
            style.Resources["ToggleButton.Static.Foreground"] = neutralForeground;
            style.Resources["ToggleButton.MouseOver.Background"] = neutralBackground;
            style.Resources["ToggleButton.MouseOver.Border"] = TealAccentBrush;
            style.Resources["ToggleButton.MouseOver.Foreground"] = neutralForeground;
            style.Resources["ToggleButton.Pressed.Background"] = neutralBackground;
            style.Resources["ToggleButton.Pressed.Border"] = TealAccentBrush;
            style.Resources["ToggleButton.Pressed.Foreground"] = neutralForeground;
            style.Resources["ToggleButton.Checked.Background"] = neutralBackground;
            style.Resources["ToggleButton.Checked.Border"] = TealAccentBrush;
            style.Resources["ToggleButton.Checked.Foreground"] = neutralForeground;
            style.Resources["ToggleButton.Checked.MouseOver.Background"] = neutralBackground;
            style.Resources["ToggleButton.Checked.MouseOver.Border"] = TealAccentBrush;
            style.Resources["ToggleButton.Checked.MouseOver.Foreground"] = neutralForeground;
            style.Resources["ToggleButton.Disabled.Background"] = neutralBackground;
            style.Resources["ToggleButton.Disabled.Border"] = neutralBorder;
            style.Resources["ToggleButton.Disabled.Foreground"] = neutralForeground;

            style.Resources["TextControlBorder"] = neutralBorder;
            style.Resources["TextControlBorderFocused"] = TealAccentBrush;
            style.Resources["ComboBoxItem.ItemsviewHover.Background"] = TealAccentBrush;
            style.Resources["ComboBoxItem.ItemsviewHover.Border"] = TealAccentBrush;
            style.Resources["ComboBoxItem.ItemsviewHover.Foreground"] = Brushes.White;
            style.Resources["ComboBoxItem.ItemsviewHoverFocus.Background"] = TealAccentBrush;
            style.Resources["ComboBoxItem.ItemsviewHoverFocus.Border"] = TealAccentBrush;
            style.Resources["ComboBoxItem.ItemsviewHoverFocus.Foreground"] = Brushes.White;
            style.Resources["ComboBoxItem.ItemsviewSelected.Background"] = TealAccentBrush;
            style.Resources["ComboBoxItem.ItemsviewSelected.Border"] = TealAccentBrush;
            style.Resources["ComboBoxItem.ItemsviewSelected.Foreground"] = Brushes.White;
            style.Resources["ComboBoxItem.ItemsviewSelectedHover.Foreground"] = Brushes.White;
            style.Resources["ComboBoxItem.ItemsviewSelectedHover.Background"] = TealAccentBrush;
            style.Resources["ComboBoxItem.ItemsviewSelectedHover.Border"] = TealAccentBrush;
            style.Resources["ComboBoxItem.ItemsviewSelectedNoFocus.Background"] = TealAccentBrush;
            style.Resources["ComboBoxItem.ItemsviewSelectedNoFocus.Border"] = TealAccentBrush;
            style.Resources["ComboBoxItem.ItemsviewSelectedNoFocus.Foreground"] = Brushes.White;

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, TealAccentBrush));
            style.Triggers.Add(hoverTrigger);

            var focusTrigger = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
            focusTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, TealAccentBrush));
            style.Triggers.Add(focusTrigger);

            var dropDownOpenTrigger = new Trigger { Property = ComboBox.IsDropDownOpenProperty, Value = true };
            dropDownOpenTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, TealAccentBrush));
            style.Triggers.Add(dropDownOpenTrigger);
            return style;
        }

        private static ControlTemplate CreateSkinAwareComboBoxTemplate()
        {
            var template = new ControlTemplate(typeof(ComboBox));

            var rootFactory = new FrameworkElementFactory(typeof(Grid));

            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.Name = "ComboBorder";
            borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            borderFactory.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            borderFactory.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            borderFactory.SetValue(Border.SnapsToDevicePixelsProperty, true);
            rootFactory.AppendChild(borderFactory);

            var contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            contentFactory.Name = "ContentSite";
            contentFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 1, 24, 1));
            contentFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            contentFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            contentFactory.SetValue(UIElement.IsHitTestVisibleProperty, false);
            contentFactory.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ComboBox.SelectionBoxItemProperty));
            contentFactory.SetValue(ContentPresenter.ContentTemplateProperty, new TemplateBindingExtension(ComboBox.SelectionBoxItemTemplateProperty));
            contentFactory.SetValue(ContentPresenter.ContentTemplateSelectorProperty, new TemplateBindingExtension(ItemsControl.ItemTemplateSelectorProperty));
            contentFactory.SetValue(TextElement.ForegroundProperty, new TemplateBindingExtension(Control.ForegroundProperty));
            rootFactory.AppendChild(contentFactory);

            var toggleFactory = new FrameworkElementFactory(typeof(ToggleButton));
            toggleFactory.Name = "DropDownToggle";
            toggleFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            toggleFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Stretch);
            toggleFactory.SetValue(Control.BackgroundProperty, Brushes.Transparent);
            toggleFactory.SetValue(Control.BorderBrushProperty, Brushes.Transparent);
            toggleFactory.SetValue(Control.BorderThicknessProperty, new Thickness(0));
            toggleFactory.SetValue(Control.FocusVisualStyleProperty, null);
            toggleFactory.SetValue(UIElement.FocusableProperty, false);
            toggleFactory.SetValue(Control.TemplateProperty, CreateComboBoxToggleButtonTemplate());
            var isCheckedBinding = new Binding(nameof(ComboBox.IsDropDownOpen))
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
                Mode = BindingMode.TwoWay
            };
            toggleFactory.SetBinding(ToggleButton.IsCheckedProperty, isCheckedBinding);
            rootFactory.AppendChild(toggleFactory);

            var popupFactory = new FrameworkElementFactory(typeof(Popup));
            popupFactory.Name = "PART_Popup";
            popupFactory.SetValue(Popup.PlacementProperty, PlacementMode.Bottom);
            popupFactory.SetValue(Popup.PopupAnimationProperty, PopupAnimation.Fade);
            popupFactory.SetValue(Popup.AllowsTransparencyProperty, true);
            popupFactory.SetValue(UIElement.FocusableProperty, false);
            var popupOpenBinding = new Binding(nameof(ComboBox.IsDropDownOpen))
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
                Mode = BindingMode.TwoWay
            };
            popupFactory.SetBinding(Popup.IsOpenProperty, popupOpenBinding);

            var popupBorderFactory = new FrameworkElementFactory(typeof(Border));
            popupBorderFactory.Name = "DropDownBorder";
            popupBorderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            popupBorderFactory.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            popupBorderFactory.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            popupBorderFactory.SetValue(Border.SnapsToDevicePixelsProperty, true);
            popupBorderFactory.SetValue(FrameworkElement.MaxHeightProperty, 260d);
            var popupMinWidthBinding = new Binding(nameof(FrameworkElement.ActualWidth))
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
            };
            popupBorderFactory.SetBinding(FrameworkElement.MinWidthProperty, popupMinWidthBinding);

            var scrollViewerFactory = new FrameworkElementFactory(typeof(ScrollViewer));
            scrollViewerFactory.SetValue(ScrollViewer.CanContentScrollProperty, true);
            scrollViewerFactory.SetValue(FrameworkElement.SnapsToDevicePixelsProperty, true);

            var itemsPresenterFactory = new FrameworkElementFactory(typeof(ItemsPresenter));
            scrollViewerFactory.AppendChild(itemsPresenterFactory);
            popupBorderFactory.AppendChild(scrollViewerFactory);
            popupFactory.AppendChild(popupBorderFactory);
            rootFactory.AppendChild(popupFactory);

            template.VisualTree = rootFactory;

            var disabledTrigger = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabledTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.65));
            template.Triggers.Add(disabledTrigger);

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, TealAccentBrush, "ComboBorder"));
            template.Triggers.Add(hoverTrigger);

            var focusTrigger = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
            focusTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, TealAccentBrush, "ComboBorder"));
            template.Triggers.Add(focusTrigger);

            var openTrigger = new Trigger { Property = ComboBox.IsDropDownOpenProperty, Value = true };
            openTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, TealAccentBrush, "ComboBorder"));
            openTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, TealAccentBrush, "DropDownBorder"));
            template.Triggers.Add(openTrigger);

            return template;
        }

        private static ControlTemplate CreateComboBoxToggleButtonTemplate()
        {
            var template = new ControlTemplate(typeof(ToggleButton));

            var rootFactory = new FrameworkElementFactory(typeof(Grid));
            var arrowFactory = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
            arrowFactory.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse("M 0,0 L 4,4 L 8,0"));
            arrowFactory.SetValue(System.Windows.Shapes.Path.StrokeProperty, new TemplateBindingExtension(Control.ForegroundProperty));
            arrowFactory.SetValue(System.Windows.Shapes.Path.StrokeThicknessProperty, 1.6);
            arrowFactory.SetValue(System.Windows.Shapes.Path.StrokeStartLineCapProperty, PenLineCap.Round);
            arrowFactory.SetValue(System.Windows.Shapes.Path.StrokeEndLineCapProperty, PenLineCap.Round);
            arrowFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
            arrowFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            arrowFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
            rootFactory.AppendChild(arrowFactory);

            template.VisualTree = rootFactory;
            return template;
        }

        private static Style CreateSkinAwareComboBoxItemStyle(FrameworkElement context)
        {
            var baseStyle = FindSkinStyle(context, typeof(ComboBoxItem));
            var style = new Style(typeof(ComboBoxItem), baseStyle);

            ApplySkinSetter(style, Control.BackgroundProperty, context, "BackgroundMainWindow", "GridEntireBackground", "BackgroundTextInput");
            ApplySkinSetter(style, Control.ForegroundProperty, context, "FontControlBrush", "FontTableBrush");
            ApplySkinSetter(style, Control.BorderBrushProperty, context, "BorderThinBrush", "TabControlBorderBrush");

            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 3, 8, 3)));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
            style.Setters.Add(new Setter(Control.TemplateProperty, CreateComboBoxItemTemplate()));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, TealAccentBrush));
            hoverTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, TealAccentBrush));
            hoverTrigger.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            style.Triggers.Add(hoverTrigger);

            var selectedTrigger = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            selectedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, TealAccentBrush));
            selectedTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, TealAccentBrush));
            selectedTrigger.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            style.Triggers.Add(selectedTrigger);

            return style;
        }

        private static ControlTemplate CreateComboBoxItemTemplate()
        {
            var template = new ControlTemplate(typeof(ComboBoxItem));

            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.Name = "ItemBorder";
            borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            borderFactory.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            borderFactory.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            borderFactory.SetValue(Border.SnapsToDevicePixelsProperty, true);

            var contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            contentFactory.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
            contentFactory.SetValue(ContentPresenter.ContentTemplateProperty, new TemplateBindingExtension(ContentControl.ContentTemplateProperty));
            contentFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
            contentFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, new TemplateBindingExtension(Control.VerticalContentAlignmentProperty));
            contentFactory.SetValue(ContentPresenter.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
            contentFactory.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
            borderFactory.AppendChild(contentFactory);

            template.VisualTree = borderFactory;

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, TealAccentBrush, "ItemBorder"));
            hoverTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, TealAccentBrush, "ItemBorder"));
            hoverTrigger.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            template.Triggers.Add(hoverTrigger);

            var selectedTrigger = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            selectedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, TealAccentBrush, "ItemBorder"));
            selectedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, TealAccentBrush, "ItemBorder"));
            selectedTrigger.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            template.Triggers.Add(selectedTrigger);

            var disabledTrigger = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabledTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.65));
            template.Triggers.Add(disabledTrigger);
            return template;
        }

        private static void ApplyTealAccentResourceOverrides(Style style)
        {
            if (style == null)
                return;

            style.Resources[SystemColors.HighlightBrushKey] = TealAccentBrush;
            style.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = TealAccentBrush;
            style.Resources[SystemColors.HighlightTextBrushKey] = Brushes.White;
            style.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = Brushes.White;
            style.Resources[SystemColors.HotTrackBrushKey] = TealAccentBrush;
        }

        private static Style CreateSkinAwareScrollBarStyle(FrameworkElement context)
        {
            var baseStyle = FindSkinStyle(context, typeof(System.Windows.Controls.Primitives.ScrollBar));
            var style = new Style(typeof(System.Windows.Controls.Primitives.ScrollBar), baseStyle);

            ApplySkinSetter(style, Control.BackgroundProperty, context, "BackgroundTextInput", "BackgroundMainWindow", "GridEntireBackground");
            ApplySkinSetter(style, Control.ForegroundProperty, context, "FontControlBrush", "FontTableBrush");
            ApplySkinSetter(style, Control.BorderBrushProperty, context, "BorderThinBrush", "TabControlBorderBrush");

            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
            style.Setters.Add(new Setter(FrameworkElement.SnapsToDevicePixelsProperty, true));

            // ponytail: only pin the cross-axis; forcing both Width and Height to 11px collapses vertical tracks.
            var verticalTrigger = new Trigger
            {
                Property = System.Windows.Controls.Primitives.ScrollBar.OrientationProperty,
                Value = Orientation.Vertical
            };
            verticalTrigger.Setters.Add(new Setter(FrameworkElement.WidthProperty, 11d));
            verticalTrigger.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 11d));
            verticalTrigger.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty, 11d));
            verticalTrigger.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right));
            verticalTrigger.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Stretch));
            style.Triggers.Add(verticalTrigger);

            var horizontalTrigger = new Trigger
            {
                Property = System.Windows.Controls.Primitives.ScrollBar.OrientationProperty,
                Value = Orientation.Horizontal
            };
            horizontalTrigger.Setters.Add(new Setter(FrameworkElement.HeightProperty, 11d));
            horizontalTrigger.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 11d));
            horizontalTrigger.Setters.Add(new Setter(FrameworkElement.MaxHeightProperty, 11d));
            horizontalTrigger.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch));
            horizontalTrigger.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Bottom));
            style.Triggers.Add(horizontalTrigger);

            return style;
        }

        private static Style CreateGroupMasterComboBoxStyle(FrameworkElement context)
        {
            var style = CreateSkinAwareComboBoxStyle(context);
            ApplySkinSetter(style, Control.BackgroundProperty, context, "BackgroundMainWindow", "GridEntireBackground", "BackgroundTextInput");
            return style;
        }

        private static Style CreateSkinAwareColumnHeaderStyle(FrameworkElement context, HorizontalAlignment horizontalAlignment)
        {
            var baseStyle = FindSkinStyle(context, typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));
            var style = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader), baseStyle);

            ApplySkinSetter(style, Control.BackgroundProperty, context, "BackgroundMainWindow", "GridEntireBackground", "BackgroundTextInput");
            ApplySkinSetter(style, Control.ForegroundProperty, context, "FontHeaderLevel4Brush", "FontControlBrush", "FontTableBrush");
            ApplySkinSetter(style, Control.BorderBrushProperty, context, "BorderThinBrush", "TabControlBorderBrush");

            double? sharedFontSize = FindSkinDouble(context, "FontTableHeight", "FontHeaderLevel4Height", "FontControlHeight");
            if (sharedFontSize.HasValue)
                style.Setters.Add(new Setter(Control.FontSizeProperty, sharedFontSize.Value));

            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 5, 6, 5)));
            style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Medium));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, horizontalAlignment));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
            return style;
        }

        private static Style CreateTextBlockElementStyle(
            FrameworkElement context,
            HorizontalAlignment horizontalAlignment,
            TextAlignment textAlignment,
            Thickness margin)
        {
            var style = new Style(typeof(TextBlock));

            style.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, horizontalAlignment));
            style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, textAlignment));
            style.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(FrameworkElement.MarginProperty, margin));
            ApplySkinSetter(style, TextBlock.ForegroundProperty, context, "FontControlBrush", "FontTableBrush", "GridRowForeground");

            double? sharedFontSize = FindSkinDouble(context, "FontTableHeight", "FontHeaderLevel4Height", "FontControlHeight");
            if (sharedFontSize.HasValue)
                style.Setters.Add(new Setter(TextBlock.FontSizeProperty, sharedFontSize.Value));
            return style;
        }

        private static Style CreatePnlTextBlockElementStyle(
            FrameworkElement context,
            HorizontalAlignment horizontalAlignment,
            TextAlignment textAlignment,
            Thickness margin,
            string signBindingPath)
        {
            var style = CreateTextBlockElementStyle(context, horizontalAlignment, textAlignment, margin);
            if (string.IsNullOrWhiteSpace(signBindingPath))
                return style;

            var positiveTrigger = new DataTrigger
            {
                Binding = new Binding(signBindingPath),
                Value = "Positive"
            };
            positiveTrigger.Setters.Add(new Setter(TextBlock.ForegroundProperty, TealAccentBrush));
            style.Triggers.Add(positiveTrigger);

            var negativeTrigger = new DataTrigger
            {
                Binding = new Binding(signBindingPath),
                Value = "Negative"
            };
            negativeTrigger.Setters.Add(new Setter(TextBlock.ForegroundProperty, OrangeAccentBrush));
            style.Triggers.Add(negativeTrigger);

            return style;
        }

        private static Style CreateWarningTextBlockElementStyle(
            FrameworkElement context,
            HorizontalAlignment horizontalAlignment,
            TextAlignment textAlignment,
            Thickness margin,
            string warningBindingPath)
        {
            var style = CreateTextBlockElementStyle(context, horizontalAlignment, textAlignment, margin);
            if (string.IsNullOrWhiteSpace(warningBindingPath))
                return style;

            var warningTrigger = new DataTrigger
            {
                Binding = new Binding(warningBindingPath),
                Value = true
            };
            warningTrigger.Setters.Add(new Setter(TextBlock.ForegroundProperty, OrangeAccentBrush));
            style.Triggers.Add(warningTrigger);

            return style;
        }

        private static Style CreateSkinAwareRowStyle(FrameworkElement context)
        {
            var baseStyle = FindSkinStyle(context, typeof(DataGridRow));
            var style = new Style(typeof(DataGridRow), baseStyle);
            ApplySkinSetter(style, Control.ForegroundProperty, context, "FontControlBrush", "FontTableBrush", "GridRowForeground");

            var selectedTrigger = new Trigger { Property = DataGridRow.IsSelectedProperty, Value = true };
            selectedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Triggers.Add(selectedTrigger);

            return style;
        }

        private static Style CreateSkinAwareCellStyle(FrameworkElement context)
        {
            var baseStyle = FindSkinStyle(context, typeof(DataGridCell));
            var style = new Style(typeof(DataGridCell), baseStyle);

            ApplySkinSetter(style, Control.BorderBrushProperty, context, "BorderThinBrush", "TabControlBorderBrush");
            ApplySkinSetter(style, Control.ForegroundProperty, context, "FontControlBrush", "FontTableBrush", "GridRowForeground");
            string borderBrushKey = FindSkinResourceKey(context, "BorderThinBrush", "TabControlBorderBrush");

            double? sharedFontSize = FindSkinDouble(context, "FontTableHeight", "FontHeaderLevel4Height", "FontControlHeight");
            if (sharedFontSize.HasValue)
                style.Setters.Add(new Setter(Control.FontSizeProperty, sharedFontSize.Value));

            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 1, 0)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 3, 6, 3)));
            style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));

            var selectedTrigger = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
            selectedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Triggers.Add(selectedTrigger);

            if (!string.IsNullOrWhiteSpace(borderBrushKey))
            {
                var focusWithinTrigger = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
                focusWithinTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension(borderBrushKey)));
                focusWithinTrigger.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
                style.Triggers.Add(focusWithinTrigger);

                var focusedTrigger = new Trigger { Property = UIElement.IsFocusedProperty, Value = true };
                focusedTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension(borderBrushKey)));
                focusedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
                style.Triggers.Add(focusedTrigger);
            }

            return style;
        }

        private static Style CreatePassiveCellStyle(FrameworkElement context, Style baseStyle = null)
        {
            var style = new Style(typeof(DataGridCell), baseStyle ?? CreateSkinAwareCellStyle(context));
            style.Setters.Add(new Setter(UIElement.FocusableProperty, false));
            style.Setters.Add(new Setter(Control.IsTabStopProperty, false));
            return style;
        }

        private static void ConfigurePassiveDataGrid(DataGrid grid)
        {
            if (grid == null)
                return;

            bool isClearingTransientSelection = false;
            Action clearTransientSelection = () =>
            {
                if (isClearingTransientSelection)
                    return;

                try
                {
                    isClearingTransientSelection = true;
                    grid.CurrentCell = new DataGridCellInfo();
                    grid.UnselectAllCells();
                    grid.UnselectAll();
                    Keyboard.ClearFocus();
                }
                catch
                {
                }
                finally
                {
                    isClearingTransientSelection = false;
                }
            };

            grid.Focusable = false;
            grid.SelectionUnit = DataGridSelectionUnit.FullRow;
            grid.SelectionMode = DataGridSelectionMode.Single;
            grid.IsTabStop = false;
            KeyboardNavigation.SetTabNavigation(grid, KeyboardNavigationMode.None);
            KeyboardNavigation.SetDirectionalNavigation(grid, KeyboardNavigationMode.None);

            grid.PreviewMouseLeftButtonUp += (_, __) =>
            {
                clearTransientSelection();
            };

            grid.PreviewKeyUp += (_, e) =>
            {
                if (e == null)
                    return;

                if (e.Key == Key.Space || e.Key == Key.Enter)
                    clearTransientSelection();
            };

            grid.GotKeyboardFocus += (_, __) => clearTransientSelection();
            grid.CurrentCellChanged += (_, __) => clearTransientSelection();
        }

        private UIElement CreatePlaceholderTab(string text)
        {
            return new Grid
            {
                Margin = new Thickness(20),
                Children =
                {
                    new TextBlock
                    {
                        Text = text,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            };
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (_restoreMaximizedOnLoad)
                WindowState = WindowState.Maximized;

            _replicationWarmupUntilUtc = DateTime.UtcNow.Add(ReplicationStartupWarmup);
            ApplyPlanLimitsToAccountGroups("startup");
            LogStartupRuntimeSettingsOnce();
            RefreshAccountData();
            _refreshTimer.Start();
            _ = RefreshLicenseStateAsync(useValidateEndpoint: true, force: true);
        }

        private void LogStartupRuntimeSettingsOnce()
        {
            if (_hasLoggedStartupRuntimeSettings)
                return;

            _hasLoggedStartupRuntimeSettings = true;
            AppendJournal(
                "System",
                "Runtime",
                $"Replication settings active: attempts={_replicationSubmitMaxAttempts}, submitCooldown={_replicationSubmitCooldownMs}ms, protectiveCooldown={_protectiveSyncCooldownMs}ms.");
            AppendJournal(
                "System",
                "Runtime",
                BuildRuntimePolicySummaryLogLine());
        }

        private void OnWindowClosed(object sender, EventArgs e)
        {
            _isWindowClosed = true;
            _refreshTimer.Stop();
            _refreshTimer.Tick -= OnRefreshTimerTick;
            CaptureSelectionOverridesFromRows();
            SaveSelectionOverridesToDisk();
            SaveAccountGroupsToDisk();
            UnsubscribeFromAllAccountRuntimeEvents();
            SavePeakStatesToDisk(force: true);
            SaveAuditFeedsToDisk(force: true);
            SaveWindowPlacementToDisk();
            ClearReplicationSubmitCooldowns();
            ClearProtectiveSyncCooldowns();
            _isFlattenAllInProgress = false;
            _isReplicatingUi = false;
            _replicationWarmupUntilUtc = DateTime.MinValue;
            PublishGlitchShellState();
            GlitchShellBridge.UnregisterMainWindow(this);

            if (_accountsGrid != null)
            {
                _accountsGrid.BeginningEdit -= OnAccountsGridBeginningEdit;
                _accountsGrid.CellEditEnding -= OnAccountsGridCellEditEnding;
                _accountsGrid.LostKeyboardFocus -= OnAccountsGridLostKeyboardFocus;
            }
            if (_headerRootGrid != null)
                _headerRootGrid.SizeChanged -= OnHeaderRootSizeChanged;
            if (_dashboardRootGrid != null)
                _dashboardRootGrid.SizeChanged -= OnDashboardRootSizeChanged;
            if (_journalRootGrid != null)
                _journalRootGrid.SizeChanged -= OnJournalRootSizeChanged;
            if (_summaryRootGrid != null)
                _summaryRootGrid.SizeChanged -= OnSummaryRootSizeChanged;
            if (_analyticsRoot != null)
                _analyticsRoot.SizeChanged -= OnAnalyticsRootSizeChanged;
            if (_analyticsSessionRangeCanvas != null)
                _analyticsSessionRangeCanvas.SizeChanged -= OnAnalyticsSessionRangeCanvasSizeChanged;
            if (_replicateButton != null)
            {
                _replicateButton.Click -= OnReplicateButtonClick;
                _replicateButton = null;
            }
            if (_flattenAllButton != null)
            {
                _flattenAllButton.Click -= OnFlattenAllButtonClick;
                _flattenAllButton = null;
            }
            if (_analyticsInstrumentCombo != null)
            {
                _analyticsInstrumentCombo.SelectionChanged -= OnAnalyticsInstrumentSelectionChanged;
                _analyticsInstrumentCombo = null;
            }
            if (_analyticsOpenDetachedWindowButton != null)
            {
                _analyticsOpenDetachedWindowButton.Click -= OnAnalyticsOpenDetachedWindowClick;
                _analyticsOpenDetachedWindowButton = null;
            }
            if (_analyticsDetachedWindow != null)
            {
                try
                {
                    _analyticsDetachedWindow.Close();
                }
                catch
                {
                }

                _analyticsDetachedWindow = null;
            }

            _headerRootGrid = null;
            _headerMetricHostGrid = null;
            _headerMetricBoxesPanel = null;
            _headerActionsGrid = null;
            _dashboardRootGrid = null;
            _dashboardGroupsSection = null;
            _journalRootGrid = null;
            _journalTopGrid = null;
            ClearAccordionLayoutRefs();
            _summaryRootGrid = null;
            _analyticsRoot = null;
            _headerResponsiveMode = -1;
            _dashboardNarrowLayout = null;
            _journalNarrowLayout = null;
            _summaryNarrowLayout = null;
            _analyticsTopLayoutMode = -1;
            _analyticsBodyWideLayout = null;
            _analyticsTimeframeTwoColumnLayout = null;
            _isApplyingHeaderResponsiveLayout = false;
            _isApplyingDashboardResponsiveLayout = false;
            _isApplyingJournalResponsiveLayout = false;
            _isApplyingSummaryResponsiveLayout = false;
            _isApplyingAnalyticsResponsiveLayout = false;

            _fundamentalAnalysisService?.Dispose();
            _tradeLedgerService?.Flush(DateTime.UtcNow, force: true);
            _riskLockLedgerService?.Flush(DateTime.UtcNow, force: true);

            if (_mainTabControl != null)
                _mainTabControl.SelectionChanged -= OnMainTabSelectionChanged;
            _mainTabControl = null;

            Loaded -= OnWindowLoaded;
            Closed -= OnWindowClosed;
        }

        private void OnRefreshTimerTick(object sender, EventArgs e)
        {
            if (_isWindowClosed || _refreshTimerTickInFlight)
                return;

            _refreshTimerTickInFlight = true;
            try
            {
                OnRefreshTimerTickCore();
            }
            catch (Exception error)
            {
                RecordSubsystemFault("refresh_timer", error);
            }
            finally
            {
                _refreshTimerTickInFlight = false;
            }
        }

        private void OnRefreshTimerTickCore()
        {
            DateTime nowUtc = DateTime.UtcNow;
            PruneRuntimeJournalCaches(nowUtc);
            PruneInformationalWarningJournalCooldowns(nowUtc);
            PruneTradeSourceSnapshots(nowUtc);
            PruneAccountItemUpdateThrottle(nowUtc);
            MaybeRunLicenseHeartbeat(nowUtc);

            if (_isEditingAccountsGrid || _isCommittingAccountsGridEdit)
            {
                SavePeakStatesToDisk(force: false);
                SaveAuditFeedsToDisk(force: false);
                return;
            }

            bool uiActive = IsGlitchShellUiActive();
            if (!uiActive)
            {
                if (_isReplicatingUi)
                    RefreshAccountData(heavyTabWork: false);

                SavePeakStatesToDisk(force: false);
                SaveAuditFeedsToDisk(force: false);
                FlushPendingJournalEntries();
                return;
            }

            bool shouldRunFullUiRefresh =
                !_isReplicatingUi ||
                _isFlattenAllInProgress ||
                (nowUtc - _lastUiRefreshUtc) >= ReplicationUiRefreshInterval;

            if (shouldRunFullUiRefresh)
            {
                RefreshAccountData(heavyTabWork: true);
                if (GetSelectedMainTabIndex() == MainTabJournal)
                    RefreshSummaryInsightsIfNeeded(nowUtc);
                _lastUiRefreshUtc = nowUtc;
            }
            else
            {
                RefreshAccountData(heavyTabWork: false);
            }

            SavePeakStatesToDisk(force: false);
            SaveAuditFeedsToDisk(force: false);
            FlushPendingJournalEntries();
        }

        private void OnAccountsGridBeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            _isEditingAccountsGrid = true;
        }

        private void OnAccountsGridCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            _isCommittingAccountsGridEdit = true;
            var editedRow = e.Row?.Item as AccountGridRow;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (editedRow != null)
                    {
                        editedRow.IsManualSelection = true;
                        UpsertSelectionOverrideFromRow(editedRow);
                    }
                    else
                    {
                        CaptureSelectionOverridesFromRows();
                    }

                    SaveSelectionOverridesToDisk();
                    RefreshAccountData();
                }
                finally
                {
                    _isCommittingAccountsGridEdit = false;
                    _isEditingAccountsGrid = false;
                }
            }), DispatcherPriority.Background);
        }

        private void OnAccountsGridLostKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
        {
            if (_accountsGrid == null)
                return;

            if (_accountsGrid.IsKeyboardFocusWithin)
                return;

            _isEditingAccountsGrid = false;
        }

        private void RefreshAccountData(bool heavyTabWork = true)
        {
            ApplyPlanLimitsToAccountGroups("refresh");

            List<Account> activeAccounts = GetActiveAccountsSnapshot();
            SyncAccountRuntimeEventSubscriptionsThrottled(activeAccounts);

            var rows = activeAccounts
                .Select(account =>
                {
                    _selectionOverrides.TryGetValue(account.Name, out AccountSelectionOverride selectionOverride);
                    return BuildAccountRow(account, selectionOverride);
                })
                .ToList();

            ApplyAccountRows(rows);

            ApplyRiskMitigations(rows, activeAccounts);
            RefreshGroupMasterDropdownOptionsIfNeeded(rows);
            ExecuteReplicationCycle(activeAccounts);

            double totalPnl = rows.Sum(r => r.TotalPnlRaw);
            double evalPnl = rows
                .Where(r => string.Equals(r.AccountStatus, "Eval", StringComparison.OrdinalIgnoreCase))
                .Sum(r => r.TotalPnlRaw);
            double paPnl = rows
                .Where(r => string.Equals(r.AccountStatus, "AP", StringComparison.OrdinalIgnoreCase))
                .Sum(r => r.TotalPnlRaw);
            UpdatePnlMetricText(_totalPnlValueText, totalPnl);
            UpdatePnlMetricText(_paPnlValueText, paPnl);
            UpdatePnlMetricText(_evalPnlValueText, evalPnl);

            double evalHeadroom = ComputeAggregateHeadroomRatio(rows, "Eval");
            double paHeadroom = ComputeAggregateHeadroomRatio(rows, "AP");
            double globalHeadroom = ComputeAggregateHeadroomRatio(rows, null);

            double globalRisk = ToRiskRatio(globalHeadroom);
            double paRisk = ToRiskRatio(paHeadroom);
            double evalRisk = ToRiskRatio(evalHeadroom);

            UpdateRiskMetricText(_globalHeadroomValueText, globalRisk);
            UpdateRiskMetricText(_paHeadroomValueText, paRisk);
            UpdateRiskMetricText(_evalHeadroomValueText, evalRisk);
            PublishGlitchShellState(rows);
            if (heavyTabWork)
            {
                if (IsAnalyticsUiActive())
                    RefreshAnalyticsDashboard(activeAccounts);
                if (GetSelectedMainTabIndex() == MainTabJournal)
                    UpdateJournalLicenseGateOverlay();
                if (GetSelectedMainTabIndex() == MainTabSettings)
                    UpdateSettingsCopyTradingPolicyNotice();
            }
        }

        private void ApplyAccountRows(IReadOnlyList<AccountGridRow> rows)
        {
            var incoming = (rows ?? Array.Empty<AccountGridRow>())
                .Where(row => row != null && !string.IsNullOrWhiteSpace(row.DisplayName))
                .ToList();

            int targetIndex = 0;
            foreach (AccountGridRow nextRow in incoming)
            {
                int existingIndex = FindAccountRowIndexByName(nextRow.DisplayName);
                if (existingIndex < 0)
                {
                    _accountRows.Insert(targetIndex, nextRow);
                    targetIndex++;
                    continue;
                }

                if (existingIndex != targetIndex)
                    _accountRows.Move(existingIndex, targetIndex);

                AccountGridRow currentRow = _accountRows[targetIndex];
                currentRow.ApplyFrom(nextRow);

                targetIndex++;
            }

            while (_accountRows.Count > targetIndex)
                _accountRows.RemoveAt(_accountRows.Count - 1);
        }

        private int FindAccountRowIndexByName(string accountName)
        {
            if (string.IsNullOrWhiteSpace(accountName))
                return -1;

            for (int i = 0; i < _accountRows.Count; i++)
            {
                AccountGridRow row = _accountRows[i];
                if (row == null || string.IsNullOrWhiteSpace(row.DisplayName))
                    continue;
                if (string.Equals(row.DisplayName, accountName, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }

        private void UpdatePnlMetricText(TextBlock textBlock, double pnlValue)
        {
            if (textBlock == null)
                return;

            string brushKey;
            if (double.IsNaN(pnlValue) || double.IsInfinity(pnlValue) || Math.Abs(pnlValue) < 0.0000001)
                brushKey = "neutral";
            else
                brushKey = pnlValue < 0 ? "negative" : "positive";

            string signature = FormatCurrency(pnlValue) + "|" + brushKey;
            if (_headerMetricSignatures.TryGetValue(textBlock, out string existing) &&
                string.Equals(existing, signature, StringComparison.Ordinal))
            {
                return;
            }

            _headerMetricSignatures[textBlock] = signature;
            textBlock.Text = FormatCurrency(pnlValue);
            if (brushKey == "neutral")
            {
                ApplySkinResource(textBlock, TextBlock.ForegroundProperty, "FontControlBrush", "FontTableBrush", "GridRowForeground");
                return;
            }

            textBlock.Foreground = pnlValue < 0 ? OrangeAccentBrush : TealAccentBrush;
        }

        private void UpdateRiskMetricText(TextBlock textBlock, double riskRatio)
        {
            if (textBlock == null)
                return;

            string brushKey;
            if (double.IsNaN(riskRatio) || double.IsInfinity(riskRatio))
                brushKey = "neutral";
            else if (riskRatio > 0.70)
                brushKey = "negative";
            else if (riskRatio < 0.70)
                brushKey = "positive";
            else
                brushKey = "neutral";

            string signature = FormatPercentOrDash(riskRatio) + "|" + brushKey;
            if (_headerMetricSignatures.TryGetValue(textBlock, out string existing) &&
                string.Equals(existing, signature, StringComparison.Ordinal))
            {
                return;
            }

            _headerMetricSignatures[textBlock] = signature;
            textBlock.Text = FormatPercentOrDash(riskRatio);
            if (brushKey == "neutral")
            {
                ApplySkinResource(textBlock, TextBlock.ForegroundProperty, "FontControlBrush", "FontTableBrush", "GridRowForeground");
                return;
            }

            if (brushKey == "negative")
                textBlock.Foreground = OrangeAccentBrush;
            else
                textBlock.Foreground = TealAccentBrush;
        }

        private void ApplyRiskMitigations(IReadOnlyList<AccountGridRow> rows, IReadOnlyList<Account> activeAccounts)
        {
            if (!_isReplicatingUi ||
                _runtimePolicySettings == null)
            {
                ClearComplianceEnforcementRuntimeState();
                return;
            }

            if (!_runtimePolicySettings.EnforceBufferFreeze15Percent &&
                !_runtimePolicySettings.EnforceBufferOneContract30Percent &&
                !_runtimePolicySettings.EnforceUnrealizedFlatten70Percent &&
                !_runtimePolicySettings.EnforceEvalProfitTargetLock)
            {
                ClearComplianceEnforcementRuntimeState();
                return;
            }

            var rowsSnapshot = rows ?? Array.Empty<AccountGridRow>();
            var accountsByName = (activeAccounts ?? Array.Empty<Account>())
                .Where(account => account != null && !string.IsNullOrWhiteSpace(account.Name))
                .GroupBy(account => account.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

            DateTime nowUtc = DateTime.UtcNow;
            PruneRiskMitigationCooldowns(nowUtc);
            HashSet<string> strategyComplianceAccounts = BuildStrategyComplianceAccountSet(activeAccounts, nowUtc);

            var seenAccounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (AccountGridRow row in rowsSnapshot)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.DisplayName))
                    continue;

                string accountName = row.DisplayName.Trim();
                seenAccounts.Add(accountName);
                bool enforceForStrategy = strategyComplianceAccounts.Contains(accountName);
                if (!enforceForStrategy)
                {
                    _riskLockedAccounts.Remove(accountName);
                    _evalTargetLockedAccounts.Remove(accountName);
                    _riskOneContractAccounts.Remove(accountName);
                    _unrealizedLossFlattenTriggeredAccounts.Remove(accountName);
                    _replicationFrozenKeys.Remove(accountName);
                    _riskLockAcknowledgedAccounts.Remove(BuildTradingLockAckKey(accountName, "BufferCriticalLock"));
                    _riskLockAcknowledgedAccounts.Remove(BuildTradingLockAckKey(accountName, "EvalProfitTargetLock"));
                    continue;
                }

                if (accountsByName.TryGetValue(accountName, out Account liveAccount))
                {
                    int declaredCap = row.MaxContractsRaw > 0
                        ? Math.Max(1, (int)Math.Round(row.MaxContractsRaw, MidpointRounding.AwayFromZero))
                        : (_runtimePolicySettings != null ? Math.Max(0, _runtimePolicySettings.ReplicationDeclaredCapContracts) : 0);
                    int currentAbsContracts = GetTotalAbsoluteOpenContracts(liveAccount);
                    if (declaredCap > 0 && currentAbsContracts > declaredCap)
                    {
                        _riskLockedAccounts.Add(accountName);
                        _riskOneContractAccounts.Remove(accountName);
                        _replicationFrozenKeys.Add(accountName);
                        AppendJournal(
                            accountName,
                            "Risk",
                            $"SYNC|event=local_compliance_breach|reason={ComplianceBreachReason.MaxContractsExceeded}|current={currentAbsContracts}|cap={declaredCap}");
                        RaiseCriticalWarning(
                            accountName,
                            $"Max contracts breach detected ({currentAbsContracts} > {declaredCap}). Trading locked until manual dismiss.",
                            "MaxContractsBreach",
                            unlocksTrading: _runtimePolicySettings == null || _runtimePolicySettings.LockRequiresManualAcknowledge);
                        TryFlattenAccountForRisk(
                            liveAccount,
                            $"MAXQTY|{accountName}",
                            "Max contracts breach");
                    }

                    if (TryDetectNoProtectionBreach(liveAccount, nowUtc, out string breachedInstrumentRoot, out string breachDetail))
                    {
                        _riskLockedAccounts.Add(accountName);
                        _riskOneContractAccounts.Remove(accountName);
                        _replicationFrozenKeys.Add(accountName);
                        AppendJournal(
                            accountName,
                            "Risk",
                            $"SYNC|event=local_compliance_breach|reason={ComplianceBreachReason.NoProtectionDetected}|instrument={CleanJournalToken(breachedInstrumentRoot)}|detail={CleanJournalToken(breachDetail)}");
                        RaiseCriticalWarning(
                            accountName,
                            $"No protection breach on {CleanJournalToken(breachedInstrumentRoot)}. {breachDetail}. Trading locked until manual dismiss.",
                            "NoProtectionLock",
                            unlocksTrading: _runtimePolicySettings == null || _runtimePolicySettings.LockRequiresManualAcknowledge);
                        TryFlattenAccountForRisk(
                            liveAccount,
                            $"NAKED|{accountName}",
                            "No protection detector breach");
                    }
                }

                bool criticalBufferBreach = IsBufferCriticalRiskTriggered(row);
                bool evalProfitTargetReached = IsEvalProfitTargetLockTriggered(row);
                bool oneContractBreach = IsBufferOneContractRiskTriggered(row);
                bool unrealizedLossBreach = IsUnrealizedLossRiskTriggered(row);
                bool isRiskLocked = _riskLockedAccounts.Contains(accountName);
                bool isEvalTargetLocked = _evalTargetLockedAccounts.Contains(accountName);
                bool isBufferLockAcknowledged = _riskLockAcknowledgedAccounts.Contains(BuildTradingLockAckKey(accountName, "BufferCriticalLock"));
                bool isEvalLockAcknowledged = _riskLockAcknowledgedAccounts.Contains(BuildTradingLockAckKey(accountName, "EvalProfitTargetLock"));

                if (!_runtimePolicySettings.EnforceBufferFreeze15Percent)
                {
                    _riskLockedAccounts.Remove(accountName);
                    _riskLockAcknowledgedAccounts.Remove(BuildTradingLockAckKey(accountName, "BufferCriticalLock"));
                    isRiskLocked = false;
                    isBufferLockAcknowledged = false;
                }

                if (!_runtimePolicySettings.EnforceBufferOneContract30Percent)
                {
                    _riskOneContractAccounts.Remove(accountName);
                }

                if (!_runtimePolicySettings.EnforceUnrealizedFlatten70Percent || !unrealizedLossBreach)
                {
                    _unrealizedLossFlattenTriggeredAccounts.Remove(accountName);
                }

                if (_runtimePolicySettings.EnforceBufferFreeze15Percent && criticalBufferBreach)
                {
                    if (!isRiskLocked && !isBufferLockAcknowledged)
                    {
                        _riskLockedAccounts.Add(accountName);
                        _riskOneContractAccounts.Remove(accountName);
                        AppendJournal(
                            accountName,
                            "Risk",
                            BuildRuleEvent(
                                "BufferCriticalLock",
                                "flatten_and_lock",
                                row.BufferMarginRaw,
                                row.MaxDrawdownRaw * BufferCriticalLockThresholdRatio,
                                "Buffer fell below 15% of max drawdown. Account flattened and strategy replication frozen pending manual dismiss."));
                        RaiseCriticalWarning(
                            accountName,
                            BuildRuleEvent(
                                "BufferCriticalLock",
                                "flatten_and_lock",
                                row.BufferMarginRaw,
                                row.MaxDrawdownRaw * BufferCriticalLockThresholdRatio,
                                "Critical buffer fell below 15% of max drawdown. Account flattened and strategy replication frozen pending manual dismiss."),
                            "BufferCriticalLock",
                            unlocksTrading: true);
                        isRiskLocked = true;

                        if (accountsByName.TryGetValue(accountName, out Account lockFlattenAccount))
                        {
                            TryFlattenAccountForRisk(
                                lockFlattenAccount,
                                $"LOCK|{accountName}",
                                "Critical buffer lock");
                        }
                    }
                }
                else
                {
                    if (!criticalBufferBreach)
                        _riskLockedAccounts.Remove(accountName);
                }

                if (_runtimePolicySettings.EnforceEvalProfitTargetLock && evalProfitTargetReached)
                {
                    if (!isEvalTargetLocked && !isEvalLockAcknowledged)
                    {
                        _evalTargetLockedAccounts.Add(accountName);
                        _riskOneContractAccounts.Remove(accountName);
                        AppendJournal(
                            accountName,
                            "Risk",
                            BuildRuleEvent(
                                "EvalProfitTargetLock",
                                "flatten_and_lock",
                                row.EquityRaw,
                                row.EvalProfitTargetLockBalanceRaw,
                                "Evaluation equity reached the target lock balance. Account flattened and trading locked pending manual dismiss."));
                        RaiseCriticalWarning(
                            accountName,
                            BuildRuleEvent(
                                "EvalProfitTargetLock",
                                "flatten_and_lock",
                                row.EquityRaw,
                                row.EvalProfitTargetLockBalanceRaw,
                                "Evaluation equity reached the target lock balance. Account flattened and trading locked pending manual dismiss."),
                            "EvalProfitTargetLock",
                            unlocksTrading: true);
                        isEvalTargetLocked = true;

                        if (accountsByName.TryGetValue(accountName, out Account evalTargetLockAccount))
                        {
                            TryFlattenAccountForRisk(
                                evalTargetLockAccount,
                                $"EVAL|{accountName}",
                                "Eval profit target lock");
                        }
                    }
                }
                else
                {
                    _evalTargetLockedAccounts.Remove(accountName);
                }

                if (!criticalBufferBreach)
                    _riskLockAcknowledgedAccounts.Remove(BuildTradingLockAckKey(accountName, "BufferCriticalLock"));
                if (!evalProfitTargetReached)
                    _riskLockAcknowledgedAccounts.Remove(BuildTradingLockAckKey(accountName, "EvalProfitTargetLock"));

                bool oneContractRecovery = IsBufferOneContractRecoveryTriggered(row);
                if (IsTradingLocked(accountName))
                {
                    _riskOneContractAccounts.Remove(accountName);
                }
                else if (_runtimePolicySettings.EnforceBufferOneContract30Percent && oneContractBreach)
                {
                    if (_riskOneContractAccounts.Add(accountName))
                        AppendJournal(
                            accountName,
                            "Risk",
                            BuildRuleEvent(
                                "BufferOneContractMode",
                                "one_contract_mode_on",
                                row.BufferMarginRaw,
                                row.MaxDrawdownRaw * BufferOneContractThresholdRatio,
                                "Buffer fell below 20% of max drawdown. Replication limited to one contract."));
                }
                else if (_riskOneContractAccounts.Contains(accountName) && oneContractRecovery)
                {
                    if (_riskOneContractAccounts.Remove(accountName))
                        AppendJournal(
                            accountName,
                            "Risk",
                            BuildRuleEvent(
                                "BufferOneContractMode",
                                "one_contract_mode_off",
                                row.BufferMarginRaw,
                                row.MaxDrawdownRaw * BufferOneContractReleaseThresholdRatio,
                                "Buffer recovered to 25% of max drawdown or higher. One-contract replication limit removed."));
                }

                if (!IsTradingLocked(accountName) &&
                    _runtimePolicySettings.EnforceUnrealizedFlatten70Percent &&
                    unrealizedLossBreach &&
                    !_unrealizedLossFlattenTriggeredAccounts.Contains(accountName) &&
                    accountsByName.TryGetValue(accountName, out Account unrealizedFlattenAccount))
                {
                    _unrealizedLossFlattenTriggeredAccounts.Add(accountName);
                    double unrealizedLossThreshold = row.IntratradeDrawdownRaw * UnrealizedLossFlattenThresholdRatio;
                    if (TryFlattenAccountForRisk(
                            unrealizedFlattenAccount,
                            $"UNRLZ|{accountName}",
                            "Unrealized loss exceeded 80% of max loss (intratrade drawdown)"))
                    {
                        RaiseCriticalWarning(
                            accountName,
                            BuildRuleEvent(
                                "UnrealizedLossFlatten",
                                "flatten",
                                Math.Max(0, -row.UnrealizedPnlRaw),
                                unrealizedLossThreshold,
                                "Unrealized loss exceeded 80% of max loss (intratrade drawdown). Position flattened automatically."),
                            "UnrealizedLossFlatten",
                            unlocksTrading: false);
                    }
                    else
                    {
                        _unrealizedLossFlattenTriggeredAccounts.Remove(accountName);
                    }
                }
            }

            foreach (string stale in _riskLockedAccounts.Where(name => !seenAccounts.Contains(name)).ToList())
                _riskLockedAccounts.Remove(stale);

            foreach (string stale in _evalTargetLockedAccounts.Where(name => !seenAccounts.Contains(name)).ToList())
                _evalTargetLockedAccounts.Remove(stale);

            foreach (string stale in _riskOneContractAccounts.Where(name => !seenAccounts.Contains(name)).ToList())
                _riskOneContractAccounts.Remove(stale);

            foreach (string stale in _unrealizedLossFlattenTriggeredAccounts.Where(name => !seenAccounts.Contains(name)).ToList())
                _unrealizedLossFlattenTriggeredAccounts.Remove(stale);

            foreach (string stale in _replicationFrozenKeys.Where(name => !seenAccounts.Contains(name)).ToList())
                _replicationFrozenKeys.Remove(stale);

            foreach (string stale in _riskLockAcknowledgedAccounts
                         .Where(key => !seenAccounts.Contains(ExtractTradingLockAckAccountName(key)))
                         .ToList())
                _riskLockAcknowledgedAccounts.Remove(stale);
        }

        private void ClearComplianceEnforcementRuntimeState()
        {
            _riskLockedAccounts.Clear();
            _evalTargetLockedAccounts.Clear();
            _riskOneContractAccounts.Clear();
            _unrealizedLossFlattenTriggeredAccounts.Clear();
            _riskLockAcknowledgedAccounts.Clear();
            _replicationFrozenKeys.Clear();
            _replicationBurstStateByKey.Clear();
            _noProtectionDetectedSinceByKey.Clear();
            _riskMitigationCooldownByKey.Clear();
            lock (_riskFlattenFallbackWarningLock)
                _riskFlattenFallbackWarningCooldownByKey.Clear();
        }

        private static bool IsUnrealizedLossRiskTriggered(AccountGridRow row)
        {
            if (row == null || row.IntratradeDrawdownRaw <= 0 || double.IsNaN(row.IntratradeDrawdownRaw) || double.IsInfinity(row.IntratradeDrawdownRaw))
                return false;
            if (double.IsNaN(row.UnrealizedPnlRaw) || double.IsInfinity(row.UnrealizedPnlRaw))
                return false;

            double unrealizedLoss = Math.Max(0, -row.UnrealizedPnlRaw);
            return unrealizedLoss > (UnrealizedLossFlattenThresholdRatio * row.IntratradeDrawdownRaw);
        }

        private static bool IsEvalProfitTargetLockTriggered(AccountGridRow row)
        {
            if (row == null)
                return false;
            if (!string.Equals(row.AccountStatus, "Eval", StringComparison.OrdinalIgnoreCase))
                return false;
            if (double.IsNaN(row.EvalProfitTargetLockBalanceRaw) || double.IsInfinity(row.EvalProfitTargetLockBalanceRaw))
                return false;
            if (row.EvalProfitTargetLockBalanceRaw <= 0)
                return false;
            if (double.IsNaN(row.EquityRaw) || double.IsInfinity(row.EquityRaw))
                return false;

            return row.EquityRaw >= row.EvalProfitTargetLockBalanceRaw;
        }

        private static bool IsBufferCriticalRiskTriggered(AccountGridRow row)
        {
            if (row == null || row.MaxDrawdownRaw <= 0 || double.IsNaN(row.MaxDrawdownRaw) || double.IsInfinity(row.MaxDrawdownRaw))
                return false;
            if (double.IsNaN(row.BufferMarginRaw) || double.IsInfinity(row.BufferMarginRaw))
                return false;

            return row.BufferMarginRaw < (BufferCriticalLockThresholdRatio * row.MaxDrawdownRaw);
        }

        private static bool IsBufferOneContractRiskTriggered(AccountGridRow row)
        {
            if (row == null || row.MaxDrawdownRaw <= 0 || double.IsNaN(row.MaxDrawdownRaw) || double.IsInfinity(row.MaxDrawdownRaw))
                return false;
            if (double.IsNaN(row.BufferMarginRaw) || double.IsInfinity(row.BufferMarginRaw))
                return false;

            return row.BufferMarginRaw < (BufferOneContractThresholdRatio * row.MaxDrawdownRaw);
        }

        private static bool IsBufferOneContractRecoveryTriggered(AccountGridRow row)
        {
            if (row == null || row.MaxDrawdownRaw <= 0 || double.IsNaN(row.MaxDrawdownRaw) || double.IsInfinity(row.MaxDrawdownRaw))
                return false;
            if (double.IsNaN(row.BufferMarginRaw) || double.IsInfinity(row.BufferMarginRaw))
                return false;

            return row.BufferMarginRaw >= (BufferOneContractReleaseThresholdRatio * row.MaxDrawdownRaw);
        }

        private static int GetTotalAbsoluteOpenContracts(Account account)
        {
            if (account == null)
                return 0;

            int total = 0;
            try
            {
                foreach (Position position in account.Positions)
                {
                    if (position == null || position.MarketPosition == MarketPosition.Flat)
                        continue;

                    total += Math.Abs(position.Quantity);
                }
            }
            catch
            {
            }

            return total;
        }

        private static bool HasWorkingProtectiveStop(Account account, string instrumentRoot)
        {
            if (account == null || string.IsNullOrWhiteSpace(instrumentRoot))
                return false;

            try
            {
                return account.Orders.Any(order =>
                    order != null &&
                    order.Instrument != null &&
                    IsWorkingOrderState(order.OrderState) &&
                    string.Equals(GetInstrumentRoot(order.Instrument), instrumentRoot, StringComparison.OrdinalIgnoreCase) &&
                    IsStopLikeOrder(order));
            }
            catch
            {
                return false;
            }
        }

        private bool TryDetectNoProtectionBreach(Account account, DateTime nowUtc, out string breachedInstrumentRoot, out string detail)
        {
            breachedInstrumentRoot = string.Empty;
            detail = string.Empty;
            if (account == null || string.IsNullOrWhiteSpace(account.Name))
                return false;

            string accountName = account.Name.Trim();
            int timeoutMs = _runtimePolicySettings != null
                ? Math.Max(100, _runtimePolicySettings.NoProtectionTimeoutMs)
                : 1000;

            var openRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (Position position in account.Positions)
                {
                    if (position == null || position.Instrument == null || position.MarketPosition == MarketPosition.Flat)
                        continue;

                    string instrumentRoot = GetInstrumentRoot(position.Instrument);
                    if (string.IsNullOrWhiteSpace(instrumentRoot))
                        continue;

                    openRoots.Add(instrumentRoot);
                    string key = accountName + "|" + instrumentRoot;
                    if (HasWorkingProtectiveStop(account, instrumentRoot))
                    {
                        _noProtectionDetectedSinceByKey.Remove(key);
                        continue;
                    }

                    if (!_noProtectionDetectedSinceByKey.TryGetValue(key, out DateTime sinceUtc))
                    {
                        _noProtectionDetectedSinceByKey[key] = nowUtc;
                        continue;
                    }

                    if ((nowUtc - sinceUtc).TotalMilliseconds >= timeoutMs)
                    {
                        breachedInstrumentRoot = instrumentRoot;
                        detail = $"no protective stop for {timeoutMs}ms";
                        return true;
                    }
                }
            }
            catch
            {
                return false;
            }

            foreach (string staleKey in _noProtectionDetectedSinceByKey.Keys
                .Where(key =>
                {
                    if (!key.StartsWith(accountName + "|", StringComparison.OrdinalIgnoreCase))
                        return false;
                    string root = key.Substring(accountName.Length + 1);
                    return !openRoots.Contains(root);
                })
                .ToList())
            {
                _noProtectionDetectedSinceByKey.Remove(staleKey);
            }

            return false;
        }

        private bool TryFlattenAccountForRisk(Account account, string mitigationKey, string reason)
        {
            if (account == null || string.IsNullOrWhiteSpace(mitigationKey))
                return false;

            DateTime nowUtc = DateTime.UtcNow;
            if (IsRiskMitigationCoolingDown(mitigationKey, nowUtc))
                return false;

            List<Instrument> instruments = GetOpenPositionInstruments(account);
            if (instruments.Count == 0)
                return false;

            string flattenSignalName = ResolveRiskFlattenSignalName(mitigationKey);
            string reasonToken = CleanJournalToken(reason);
            bool flattenIssued = false;
            foreach (Instrument instrument in instruments)
            {
                string instrumentToken = CleanJournalToken(GetInstrumentRoot(instrument));
                AppendJournal(
                    account.Name,
                    "Risk",
                    $"SYNC|event=flatten_attempt|reason={reasonToken}|instrument={instrumentToken}|signal={flattenSignalName}");

                bool namedConfirmed;
                string namedResult;
                Order submittedOrder;
                try
                {
                    namedConfirmed = TrySubmitNamedRiskFlattenOrder(account, instrument, flattenSignalName, out namedResult, out submittedOrder);
                }
                catch (Exception ex)
                {
                    namedConfirmed = false;
                    namedResult = "named_submit_exception_" + ex.GetType().Name;
                    submittedOrder = null;
                }

                if (namedConfirmed)
                {
                    flattenIssued = true;
                    AppendJournal(
                        account.Name,
                        "Risk",
                        $"SYNC|event=flatten_named_result|reason={reasonToken}|instrument={instrumentToken}|signal={flattenSignalName}|origin={FlattenOrigin.AddonGovernor}|result=confirmed|cause={CleanJournalToken(namedResult)}");
                    AppendJournal(
                        account.Name,
                        "Risk",
                        $"SYNC|event=flatten_origin|reason={reasonToken}|origin={FlattenOrigin.AddonGovernor}|signal={flattenSignalName}|instrument={instrumentToken}");
                    continue;
                }

                if (submittedOrder != null)
                {
                    flattenIssued = true;
                    AppendJournal(
                        account.Name,
                        "Risk",
                        $"SYNC|event=flatten_named_result|reason={reasonToken}|instrument={instrumentToken}|signal={flattenSignalName}|origin={FlattenOrigin.Unknown}|result=pending|cause={CleanJournalToken(namedResult)}");
                    ScheduleNamedRiskFlattenConfirmationFallback(
                        account,
                        instrument,
                        submittedOrder,
                        flattenSignalName,
                        reasonToken,
                        mitigationKey,
                        allowFallbackWarning: true);
                    continue;
                }

                bool fallbackIssued = TryIssueInstrumentFlattenFallback(
                    account,
                    instrument,
                    flattenSignalName,
                    reasonToken,
                    namedResult,
                    mitigationKey,
                    allowFallbackWarning: true);
                AppendJournal(
                    account.Name,
                    "Risk",
                    $"SYNC|event=flatten_named_result|reason={reasonToken}|instrument={instrumentToken}|signal={flattenSignalName}|origin={FlattenOrigin.Unknown}|result=failed|cause={CleanJournalToken(namedResult)}");
                if (fallbackIssued)
                    flattenIssued = true;
            }

            if (!flattenIssued)
                return false;

            MarkRiskMitigation(mitigationKey, nowUtc);
            AppendJournal(account.Name, "Risk", reason + ". Flatten issued.");
            return true;
        }

        private static string ResolveRiskFlattenSignalName(string mitigationKey)
        {
            string key = string.IsNullOrWhiteSpace(mitigationKey) ? string.Empty : mitigationKey.Trim().ToUpperInvariant();
            if (key.StartsWith("UNRLZ", StringComparison.OrdinalIgnoreCase))
                return RiskFlattenUnrealizedSignalName;
            if (key.StartsWith("MAXQTY", StringComparison.OrdinalIgnoreCase))
                return RiskFlattenMaxQtySignalName;
            if (key.StartsWith("NAKED", StringComparison.OrdinalIgnoreCase))
                return RiskFlattenNakedSignalName;
            if (key.StartsWith("DAILYLIMIT", StringComparison.OrdinalIgnoreCase))
                return RiskFlattenDailyLimitSignalName;
            if (key.StartsWith("LOCK", StringComparison.OrdinalIgnoreCase) || key.StartsWith("EVAL", StringComparison.OrdinalIgnoreCase))
                return RiskFlattenBufferSignalName;

            return RiskFlattenBufferSignalName;
        }

        private bool TryIssueInstrumentFlattenFallback(
            Account account,
            Instrument instrument,
            string flattenSignalName,
            string reasonToken,
            string cause,
            string mitigationKey,
            bool allowFallbackWarning)
        {
            if (account == null || instrument == null)
                return false;

            string instrumentToken = CleanJournalToken(GetInstrumentRoot(instrument));
            string fallbackCause = string.IsNullOrWhiteSpace(cause) ? "named_unconfirmed" : cause;
            bool fallbackIssued = false;
            try
            {
                account.Flatten(new[] { instrument });
                fallbackIssued = true;
            }
            catch (Exception ex)
            {
                fallbackCause = fallbackCause + ";fallback_exception_" + ex.GetType().Name;
            }

            AppendJournal(
                account.Name,
                "Risk",
                $"SYNC|event=flatten_fallback_used|reason={CleanJournalToken(reasonToken)}|instrument={instrumentToken}|signal={flattenSignalName}|origin={FlattenOrigin.FallbackAccountFlatten}|result={(fallbackIssued ? "issued" : "failed")}|cause={CleanJournalToken(fallbackCause)}");
            if (!fallbackIssued)
                return false;

            AppendJournal(
                account.Name,
                "Risk",
                $"SYNC|event=flatten_origin|reason={CleanJournalToken(reasonToken)}|origin={FlattenOrigin.FallbackAccountFlatten}|signal={flattenSignalName}|instrument={instrumentToken}");
            if (allowFallbackWarning)
                RaiseRiskFlattenFallbackWarningIfNeeded(account.Name, mitigationKey, flattenSignalName, CleanJournalToken(reasonToken));

            return true;
        }

        private void RaiseRiskFlattenFallbackWarningIfNeeded(string accountName, string mitigationKey, string flattenSignalName, string reasonToken)
        {
            if (_isWindowClosed)
                return;

            DateTime nowUtc = DateTime.UtcNow;
            if (!TryMarkRiskFlattenFallbackWarning(mitigationKey, nowUtc))
                return;

            RaiseCriticalWarning(
                accountName,
                "Risk flatten fallback used. Review connection health and broker attribution.",
                "RiskFlattenFallback",
                unlocksTrading: false);
            AppendJournal(
                accountName,
                "Risk",
                $"SYNC|event=flatten_fallback_used|reason={CleanJournalToken(reasonToken)}|instrument=ALL|signal={flattenSignalName}|origin={FlattenOrigin.FallbackAccountFlatten}|result=warning_raised|cause=manual_review_required");
        }

        private bool TryMarkRiskFlattenFallbackWarning(string mitigationKey, DateTime nowUtc)
        {
            string key = string.IsNullOrWhiteSpace(mitigationKey)
                ? "GLOBAL"
                : mitigationKey.Trim();

            lock (_riskFlattenFallbackWarningLock)
            {
                if (_riskFlattenFallbackWarningCooldownByKey.TryGetValue(key, out DateTime cooldownUntilUtc) &&
                    cooldownUntilUtc > nowUtc)
                {
                    return false;
                }

                _riskFlattenFallbackWarningCooldownByKey[key] = nowUtc.Add(RiskMitigationCooldown);
                return true;
            }
        }

        private void ScheduleNamedRiskFlattenConfirmationFallback(
            Account account,
            Instrument instrument,
            Order submittedOrder,
            string flattenSignalName,
            string reasonToken,
            string mitigationKey,
            bool allowFallbackWarning)
        {
            if (_isWindowClosed || account == null || instrument == null || submittedOrder == null)
                return;

            string accountName = account.Name ?? "System";
            string instrumentToken = CleanJournalToken(GetInstrumentRoot(instrument));
            string normalizedReason = CleanJournalToken(reasonToken);

            _ = Task.Run(async () =>
            {
                if (_isWindowClosed)
                    return;

                try
                {
                    string confirmationResult = await ConfirmNamedRiskFlattenOrderAsync(submittedOrder).ConfigureAwait(false);
                    bool confirmed = confirmationResult.StartsWith("confirmed_", StringComparison.OrdinalIgnoreCase);
                    if (confirmed)
                    {
                        AppendJournal(
                            accountName,
                            "Risk",
                            $"SYNC|event=flatten_named_result|reason={normalizedReason}|instrument={instrumentToken}|signal={flattenSignalName}|origin={FlattenOrigin.AddonGovernor}|result=confirmed_async|cause={CleanJournalToken(confirmationResult)}");
                        AppendJournal(
                            accountName,
                            "Risk",
                            $"SYNC|event=flatten_origin|reason={normalizedReason}|origin={FlattenOrigin.AddonGovernor}|signal={flattenSignalName}|instrument={instrumentToken}");
                        return;
                    }

                    AppendJournal(
                        accountName,
                        "Risk",
                        $"SYNC|event=flatten_named_result|reason={normalizedReason}|instrument={instrumentToken}|signal={flattenSignalName}|origin={FlattenOrigin.Unknown}|result=failed_async|cause={CleanJournalToken(confirmationResult)}");
                    TryIssueInstrumentFlattenFallback(
                        account,
                        instrument,
                        flattenSignalName,
                        normalizedReason,
                        confirmationResult,
                        mitigationKey,
                        allowFallbackWarning);
                }
                catch
                {
                }
            });
        }

        private static bool TrySubmitNamedRiskFlattenOrder(Account account, Instrument instrument, string signalName, out string result, out Order submittedOrder)
        {
            result = "unknown";
            submittedOrder = null;
            if (account == null || instrument == null)
            {
                result = "invalid_account_or_instrument";
                return false;
            }

            Position position = null;
            try
            {
                position = account.Positions.FirstOrDefault(p =>
                    p != null &&
                    p.Instrument != null &&
                    string.Equals(GetInstrumentRoot(p.Instrument), GetInstrumentRoot(instrument), StringComparison.OrdinalIgnoreCase) &&
                    p.MarketPosition != MarketPosition.Flat);
            }
            catch
            {
                result = "position_lookup_failed";
                return false;
            }

            if (position == null || position.MarketPosition == MarketPosition.Flat)
            {
                result = "no_open_position";
                return false;
            }

            int qty = Math.Abs(position.Quantity);
            if (qty <= 0)
            {
                result = "invalid_qty";
                return false;
            }

            OrderAction action = position.MarketPosition == MarketPosition.Long
                ? OrderAction.Sell
                : OrderAction.BuyToCover;

            try
            {
                Order order = account.CreateOrder(
                    instrument,
                    action,
                    OrderType.Market,
                    GetPreferredFollowerOrderEntry(),
                    TimeInForce.Day,
                    qty,
                    0.0,
                    0.0,
                    string.Empty,
                    signalName ?? RiskFlattenBufferSignalName,
                    DateTime.MaxValue,
                    null);
                if (order == null)
                {
                    result = "create_order_null";
                    return false;
                }

                account.Submit(new[] { order });
                OrderState state = order.OrderState;
                if (state == OrderState.Accepted ||
                    state == OrderState.Working ||
                    state == OrderState.PartFilled ||
                    state == OrderState.Filled)
                {
                    result = "confirmed_" + state;
                    return true;
                }

                if (state == OrderState.Cancelled || state == OrderState.Rejected)
                {
                    result = "failed_" + state;
                    return false;
                }

                submittedOrder = order;
                result = "pending_" + state;
                return false;
            }
            catch
            {
                result = "submit_exception";
                return false;
            }
        }

        private static async Task<string> ConfirmNamedRiskFlattenOrderAsync(Order order)
        {
            if (order == null)
                return "failed_order_null";

            DateTime deadlineUtc = DateTime.UtcNow.AddMilliseconds(RiskFlattenConfirmationTimeoutMs);
            while (DateTime.UtcNow <= deadlineUtc)
            {
                OrderState state = order.OrderState;
                if (state == OrderState.Accepted ||
                    state == OrderState.Working ||
                    state == OrderState.PartFilled ||
                    state == OrderState.Filled)
                {
                    return "confirmed_" + state;
                }

                if (state == OrderState.Cancelled || state == OrderState.Rejected)
                {
                    return "failed_" + state;
                }

                await Task.Delay(RiskFlattenConfirmationPollMs).ConfigureAwait(false);
            }

            return "failed_timeout_" + order.OrderState;
        }

        private bool IsRiskMitigationCoolingDown(string mitigationKey, DateTime nowUtc)
        {
            if (string.IsNullOrWhiteSpace(mitigationKey))
                return false;

            return _riskMitigationCooldownByKey.TryGetValue(mitigationKey, out DateTime cooldownUntilUtc) &&
                   cooldownUntilUtc > nowUtc;
        }

        private void MarkRiskMitigation(string mitigationKey, DateTime nowUtc)
        {
            if (string.IsNullOrWhiteSpace(mitigationKey))
                return;

            _riskMitigationCooldownByKey[mitigationKey] = nowUtc.Add(RiskMitigationCooldown);
        }

        private void PruneRiskMitigationCooldowns(DateTime nowUtc)
        {
            foreach (string stale in _riskMitigationCooldownByKey
                .Where(kvp => kvp.Value <= nowUtc)
                .Select(kvp => kvp.Key)
                .ToList())
            {
                _riskMitigationCooldownByKey.Remove(stale);
            }

            lock (_riskFlattenFallbackWarningLock)
            {
                foreach (string stale in _riskFlattenFallbackWarningCooldownByKey
                    .Where(kvp => kvp.Value <= nowUtc)
                    .Select(kvp => kvp.Key)
                    .ToList())
                {
                    _riskFlattenFallbackWarningCooldownByKey.Remove(stale);
                }
            }
        }

        private void AppendJournal(string accountName, string category, string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => AppendJournal(accountName, category, message)), DispatcherPriority.Background);
                return;
            }

            if (string.IsNullOrWhiteSpace(accountName))
                accountName = "System";
            if (string.IsNullOrWhiteSpace(category))
                category = "Info";
            if (string.IsNullOrWhiteSpace(message))
                return;

            QueueJournalEntry(new JournalEntry
            {
                TimestampUtc = DateTime.UtcNow,
                AccountName = accountName.Trim(),
                Category = category.Trim(),
                Message = message.Trim()
            });
            _hasPendingAuditWrite = true;
        }

        private static WarningSeverity ResolveWarningSeverity(string warningType)
        {
            string token = string.IsNullOrWhiteSpace(warningType) ? string.Empty : warningType.Trim();
            if (token.Length == 0)
                return WarningSeverity.Notice;

            if (token.StartsWith("BufferCriticalLock", StringComparison.OrdinalIgnoreCase) ||
                token.StartsWith("UnrealizedLossFlatten", StringComparison.OrdinalIgnoreCase) ||
                token.StartsWith("MaxContractsBreach", StringComparison.OrdinalIgnoreCase) ||
                token.StartsWith("NoProtectionLock", StringComparison.OrdinalIgnoreCase) ||
                token.StartsWith("ReplicationFreeze", StringComparison.OrdinalIgnoreCase) ||
                token.StartsWith("EvalProfitTargetLock", StringComparison.OrdinalIgnoreCase))
            {
                return WarningSeverity.Critical;
            }

            if (token.StartsWith("ReplicationSubmit|", StringComparison.OrdinalIgnoreCase) ||
                token.StartsWith("ProtectiveRejected|", StringComparison.OrdinalIgnoreCase) ||
                token.StartsWith("RiskFlattenFallback", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("RiskFlattenFallback", StringComparison.OrdinalIgnoreCase) ||
                token.StartsWith("PolicyGroupLimit", StringComparison.OrdinalIgnoreCase) ||
                token.StartsWith("PolicyFollowerLimit", StringComparison.OrdinalIgnoreCase) ||
                token.StartsWith("PolicyReplicationBlocked", StringComparison.OrdinalIgnoreCase) ||
                token.StartsWith("PointValueUnknown|", StringComparison.OrdinalIgnoreCase))
            {
                return WarningSeverity.Informational;
            }

            return WarningSeverity.Notice;
        }

        private static bool IsNoticeOnlyWarningSeverity(WarningSeverity severity)
        {
            return severity == WarningSeverity.Notice ||
                   severity == WarningSeverity.Operational ||
                   severity == WarningSeverity.Informational;
        }

        private static WarningSeverity NormalizeWarningSeverity(WarningSeverity severity)
        {
            if (severity == WarningSeverity.Operational)
                return WarningSeverity.Notice;
            return severity;
        }

        private static bool ShouldFilterCriticalWarningEntry(object item)
        {
            if (!(item is CriticalWarningEntry entry) || entry == null || entry.IsDismissed)
                return false;

            WarningSeverity severity = NormalizeWarningSeverity(entry.Severity);
            return severity == WarningSeverity.Critical;
        }

        private static bool ShouldFilterNoticeWarningEntry(object item)
        {
            if (!(item is CriticalWarningEntry entry) || entry == null)
                return false;

            WarningSeverity severity = NormalizeWarningSeverity(entry.Severity);
            return severity == WarningSeverity.Notice || severity == WarningSeverity.Informational;
        }

        private void RefreshWarningCollectionViews()
        {
            _criticalWarningsView?.Refresh();
            _noticeWarningsView?.Refresh();
        }

        private bool TryMarkInformationalWarningJournalCooldown(string warningKey, DateTime nowUtc)
        {
            if (string.IsNullOrWhiteSpace(warningKey))
                return true;

            if (_informationalWarningJournalCooldownByKey.TryGetValue(warningKey, out DateTime cooldownUntilUtc) &&
                cooldownUntilUtc > nowUtc)
            {
                return false;
            }

            _informationalWarningJournalCooldownByKey[warningKey] = nowUtc.Add(InformationalWarningJournalCooldown);
            return true;
        }

        private void PruneInformationalWarningJournalCooldowns(DateTime nowUtc)
        {
            foreach (string expiredKey in _informationalWarningJournalCooldownByKey
                .Where(kvp => kvp.Value <= nowUtc)
                .Select(kvp => kvp.Key)
                .ToList())
            {
                _informationalWarningJournalCooldownByKey.Remove(expiredKey);
            }
        }

        private void RaiseCriticalWarning(string accountName, string message, string warningType, bool unlocksTrading)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(
                    new Action(() => RaiseCriticalWarning(accountName, message, warningType, unlocksTrading)),
                    DispatcherPriority.Background);
                return;
            }

            if (string.IsNullOrWhiteSpace(message))
                return;

            string normalizedAccount = string.IsNullOrWhiteSpace(accountName) ? "System" : accountName.Trim();
            string normalizedType = string.IsNullOrWhiteSpace(warningType) ? "Generic" : warningType.Trim();
            string warningKey = normalizedType + "|" + normalizedAccount;
            WarningSeverity severity = NormalizeWarningSeverity(ResolveWarningSeverity(normalizedType));
            DateTime nowUtc = DateTime.UtcNow;

            if (severity == WarningSeverity.Informational)
            {
                if (TryMarkInformationalWarningJournalCooldown(warningKey, nowUtc))
                    AppendJournal(normalizedAccount, "Warning", message.Trim());
            }

            bool alreadyActive = _criticalWarningEntries.Any(entry =>
                entry != null &&
                !entry.IsDismissed &&
                string.Equals(entry.WarningKey, warningKey, StringComparison.OrdinalIgnoreCase));
            if (alreadyActive)
                return;

            _criticalWarningEntries.Insert(0, new CriticalWarningEntry
            {
                TimestampUtc = nowUtc,
                AccountName = normalizedAccount,
                Message = message.Trim(),
                WarningKey = warningKey,
                UnlocksTrading = unlocksTrading,
                Severity = severity
            });
            _hasPendingAuditWrite = true;

            const int maxWarningEntries = 300;
            while (_criticalWarningEntries.Count > maxWarningEntries)
                _criticalWarningEntries.RemoveAt(_criticalWarningEntries.Count - 1);

            RefreshWarningCollectionViews();
            UpdateWarningCountUi();
        }

        private void OnDismissSelectedWarningClick(object sender, RoutedEventArgs e)
        {
            if (_criticalWarningsGrid == null)
                return;

            var selectedWarnings = _criticalWarningsGrid.SelectedItems?
                .OfType<CriticalWarningEntry>()
                .Where(entry => entry != null)
                .Distinct()
                .ToList();

            if (selectedWarnings == null || selectedWarnings.Count == 0)
            {
                if (_criticalWarningsGrid.SelectedItem is CriticalWarningEntry single)
                    selectedWarnings = new List<CriticalWarningEntry> { single };
                else
                    return;
            }

            foreach (CriticalWarningEntry warning in selectedWarnings)
                DismissWarning(warning);
        }

        private void OnDismissWarningButtonClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is CriticalWarningEntry warning)
                DismissWarning(warning);
        }

        private void DismissWarning(CriticalWarningEntry warning)
        {
            if (warning == null || warning.IsDismissed)
                return;

            warning.DismissedUtc = DateTime.UtcNow;
            warning.IsDismissed = true;
            _hasPendingAuditWrite = true;

            if (warning.UnlocksTrading && !string.IsNullOrWhiteSpace(warning.AccountName))
            {
                string accountName = warning.AccountName.Trim();
                string warningKey = warning.WarningKey ?? string.Empty;
                bool removedAnyLock = false;
                if (warningKey.StartsWith("BufferCriticalLock|", StringComparison.OrdinalIgnoreCase))
                {
                    removedAnyLock = _riskLockedAccounts.Remove(accountName);
                }
                else if (warningKey.StartsWith("EvalProfitTargetLock|", StringComparison.OrdinalIgnoreCase))
                {
                    removedAnyLock = _evalTargetLockedAccounts.Remove(accountName);
                }
                else if (warningKey.StartsWith("ReplicationFreeze|", StringComparison.OrdinalIgnoreCase))
                {
                    removedAnyLock = _replicationFrozenKeys.Remove(accountName);
                }
                else if (warningKey.StartsWith("MaxContractsBreach|", StringComparison.OrdinalIgnoreCase) ||
                         warningKey.StartsWith("NoProtectionLock|", StringComparison.OrdinalIgnoreCase))
                {
                    removedAnyLock = _riskLockedAccounts.Remove(accountName);
                    if (_replicationFrozenKeys.Remove(accountName))
                        removedAnyLock = true;
                }
                else
                {
                    removedAnyLock = _riskLockedAccounts.Remove(accountName);
                    if (_evalTargetLockedAccounts.Remove(accountName))
                        removedAnyLock = true;
                }

                if (removedAnyLock)
                {
                    string ackKey = BuildTradingLockAckKey(accountName, warningKey.Split(new[] { '|' }, 2)[0]);
                    _riskLockAcknowledgedAccounts.Add(ackKey);
                    AppendJournal(accountName, "Risk", "Manual intervention acknowledged. Trading lock removed.");
                }
            }

            UpdateWarningCountUi();
            RefreshWarningCollectionViews();
        }

        private void UpdateWarningCountUi()
        {
            if (_warningCountValueText == null)
                return;

            int criticalCount = _criticalWarningEntries.Count(entry =>
                entry != null &&
                !entry.IsDismissed &&
                NormalizeWarningSeverity(entry.Severity) == WarningSeverity.Critical);
            _warningCountValueText.Text = criticalCount.ToString("N0", CultureInfo.CurrentCulture);
            _warningCountValueText.Foreground = Brushes.White;
        }

        private static double ToRiskRatio(double headroomRatio)
        {
            if (double.IsNaN(headroomRatio) || double.IsInfinity(headroomRatio))
                return double.NaN;

            double clampedHeadroom = Math.Max(0, Math.Min(1, headroomRatio));
            return 1.0 - clampedHeadroom;
        }

        private static double ComputeAggregateHeadroomRatio(IEnumerable<AccountGridRow> rows, string statusFilter)
        {
            if (rows == null)
                return double.NaN;

            var filtered = rows
                .Where(row => row != null && row.MaxDrawdownRaw > 0 && !double.IsNaN(row.BufferMarginRaw) && !double.IsInfinity(row.BufferMarginRaw))
                .Where(row =>
                    string.IsNullOrWhiteSpace(statusFilter)
                        ? !string.Equals(row.AccountStatus, "Sim", StringComparison.OrdinalIgnoreCase)
                        : string.Equals(row.AccountStatus, statusFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();

            double maxDd = filtered.Sum(row => row.MaxDrawdownRaw);
            if (maxDd <= 0)
                return double.NaN;

            double buffer = filtered.Sum(row => row.BufferMarginRaw);
            return buffer / maxDd;
        }

        private static string FormatPercentOrDash(double ratio)
        {
            if (double.IsNaN(ratio) || double.IsInfinity(ratio))
                return "-";

            return (ratio * 100.0).ToString("N0", CultureInfo.CurrentCulture) + "%";
        }

        private void RefreshGroupMasterDropdownOptionsIfNeeded(IReadOnlyList<AccountGridRow> rows)
        {
            if (_accountGroups == null || _accountGroups.Count == 0)
                return;

            BuildAndApplyGroupMemberPnlSnapshot(rows);
            bool requiresRebuild = false;

            string currentSnapshot = BuildConnectedAccountSnapshot(rows);
            if (!string.Equals(_groupMasterOptionsSnapshot, currentSnapshot, StringComparison.Ordinal))
            {
                _groupMasterOptionsSnapshot = currentSnapshot;
                requiresRebuild = true;
            }

            if (requiresRebuild)
                RebuildAccountGroupsUi();
        }

        private static string BuildConnectedAccountSnapshot(IEnumerable<AccountGridRow> rows)
        {
            if (rows == null)
                return string.Empty;

            return string.Join("|", rows
                .Where(row => row != null && !string.IsNullOrWhiteSpace(row.DisplayName))
                .Select(row => row.DisplayName.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
        }

        private void BuildAndApplyGroupMemberPnlSnapshot(IReadOnlyList<AccountGridRow> rows)
        {
            var rowsByAccount = (rows ?? Array.Empty<AccountGridRow>())
                .Where(row => row != null && !string.IsNullOrWhiteSpace(row.DisplayName))
                .GroupBy(row => row.DisplayName.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

            foreach (AccountGroupDefinition group in _accountGroups)
            {
                if (group?.Members == null)
                    continue;

                foreach (AccountGroupMemberRow member in group.Members)
                {
                    if (member == null || string.IsNullOrWhiteSpace(member.FollowerAccount))
                        continue;

                    string pnlDisplay = "-";
                    string pnlSign = "Neutral";
                    string maxDd = "-";
                    string maxL = "-";
                    string maxContracts = "-";
                    string position = "0";
                    if (rowsByAccount.TryGetValue(member.FollowerAccount.Trim(), out AccountGridRow row))
                    {
                        pnlDisplay = string.IsNullOrWhiteSpace(row.TotalPnl) ? "-" : row.TotalPnl;
                        pnlSign = string.IsNullOrWhiteSpace(row.TotalPnlSign) ? "Neutral" : row.TotalPnlSign;
                        maxDd = string.IsNullOrWhiteSpace(row.MaxDrawdown) ? "-" : row.MaxDrawdown;
                        maxL = string.IsNullOrWhiteSpace(row.IntratradeDrawdown) ? "-" : row.IntratradeDrawdown;
                        maxContracts = string.IsNullOrWhiteSpace(row.MaxContracts) ? "-" : row.MaxContracts;
                        position = string.IsNullOrWhiteSpace(row.Position) ? "0" : row.Position;
                    }

                    if (!string.Equals(member.Pnl, pnlDisplay, StringComparison.Ordinal))
                        member.Pnl = pnlDisplay;
                    if (!string.Equals(member.PnlSign, pnlSign, StringComparison.Ordinal))
                        member.PnlSign = pnlSign;
                    if (!string.Equals(member.MaxDd, maxDd, StringComparison.Ordinal))
                        member.MaxDd = maxDd;
                    if (!string.Equals(member.MaxL, maxL, StringComparison.Ordinal))
                        member.MaxL = maxL;
                    if (!string.Equals(member.MaxContracts, maxContracts, StringComparison.Ordinal))
                        member.MaxContracts = maxContracts;
                    if (!string.Equals(member.Position, position, StringComparison.Ordinal))
                        member.Position = position;
                }
            }
        }

        private void PublishGlitchShellState(IReadOnlyList<AccountGridRow> rows = null)
        {
            if (!TryPrepareShellSnapshotPublish(rows, out GlitchShellSnapshot snapshot))
                return;

            GlitchShellBridge.Publish(snapshot);
        }

        private IReadOnlyDictionary<string, GlitchGroupRuntimeSummary> BuildGlitchShellGroupSummaries(IReadOnlyList<AccountGridRow> rows)
        {
            var rowsByAccount = (rows ?? _accountRows ?? new ObservableCollection<AccountGridRow>())
                .Where(row => row != null && !string.IsNullOrWhiteSpace(row.DisplayName))
                .GroupBy(row => row.DisplayName.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

            var summaries = new Dictionary<string, GlitchGroupRuntimeSummary>(StringComparer.OrdinalIgnoreCase);
            foreach (AccountGroupDefinition group in _accountGroups ?? new ObservableCollection<AccountGroupDefinition>())
            {
                string masterAccount = group?.MasterAccount?.Trim();
                if (string.IsNullOrWhiteSpace(masterAccount))
                    continue;

                if (!summaries.TryGetValue(masterAccount, out GlitchGroupRuntimeSummary summary))
                {
                    summary = new GlitchGroupRuntimeSummary
                    {
                        MasterAccount = masterAccount,
                        EnabledFollowerCount = 0,
                        GroupPnlRaw = 0
                    };
                    summaries[masterAccount] = summary;
                }

                if (rowsByAccount.TryGetValue(masterAccount, out AccountGridRow masterRow) && masterRow != null)
                    summary.GroupPnlRaw += masterRow.TotalPnlRaw;

                foreach (AccountGroupMemberRow member in group.Members ?? new ObservableCollection<AccountGroupMemberRow>())
                {
                    if (member == null || !member.IsEnabled || string.IsNullOrWhiteSpace(member.FollowerAccount))
                        continue;

                    summary.EnabledFollowerCount++;
                    if (rowsByAccount.TryGetValue(member.FollowerAccount.Trim(), out AccountGridRow followerRow) && followerRow != null)
                        summary.GroupPnlRaw += followerRow.TotalPnlRaw;
                }
            }

            return summaries;
        }


        private void CaptureSelectionOverridesFromRows()
        {
            foreach (var row in _accountRows)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.DisplayName))
                    continue;
                if (!row.IsManualSelection)
                    continue;

                UpsertSelectionOverrideFromRow(row);
            }
        }

        private void UpsertSelectionOverrideFromRow(AccountGridRow row)
        {
            if (row == null || string.IsNullOrWhiteSpace(row.DisplayName))
                return;

            string status = NormalizeAccountStatus(row.AccountStatus);
            string firmId = ToFirmId(row.PropFirmDisplay);
            if (string.Equals(status, "Sim", StringComparison.OrdinalIgnoreCase))
                firmId = "None";
            double? selectedSize = ParseAccountSize(row.AccountSizeSelection);

            _selectionOverrides[row.DisplayName] = new AccountSelectionOverride
            {
                AccountStatus = status,
                PropFirmId = firmId,
                AccountSize = selectedSize,
                IsManual = true
            };
        }

        private string GetOverridesFilePath()
        {
            return GlitchStateStore.GetDefaultPath("AccountOverrides.tsv");
        }

        private string GetPeakStateFilePath()
        {
            return GlitchStateStore.GetDefaultPath("AccountPeaks.tsv");
        }

        private string GetWindowPlacementFilePath()
        {
            return GlitchStateStore.GetDefaultPath("WindowPlacement.tsv");
        }

        private string GetJournalFilePath()
        {
            return GlitchStateStore.GetDefaultPath("Journal.tsv");
        }

        private string GetWarningsFilePath()
        {
            return GlitchStateStore.GetDefaultPath("CriticalWarnings.tsv");
        }

        private string GetRuntimePolicySettingsFilePath()
        {
            return GlitchRuntimePolicyStore.GetDefaultSettingsPath();
        }

        private string GetLicenseCacheFilePath()
        {
            return GlitchRuntimePolicyStore.GetDefaultLicenseCachePath();
        }

        private string GetAccountGroupsFilePath()
        {
            return GlitchStateStore.GetDefaultPath("AccountGroups.tsv");
        }

        private void LoadAuditFeedsFromDisk()
        {
            try
            {
                _journalEntries.Clear();
                foreach (GlitchStateStore.JournalRecord persisted in GlitchStateStore.LoadJournalEntries(_journalFilePath))
                {
                    if (persisted == null || string.IsNullOrWhiteSpace(persisted.Message))
                        continue;

                    _journalEntries.Add(new JournalEntry
                    {
                        TimestampUtc = persisted.TimestampUtc == default(DateTime) ? DateTime.UtcNow : persisted.TimestampUtc,
                        AccountName = string.IsNullOrWhiteSpace(persisted.AccountName) ? "System" : persisted.AccountName,
                        Category = string.IsNullOrWhiteSpace(persisted.Category) ? "Info" : persisted.Category,
                        Message = persisted.Message
                    });
                }

                _criticalWarningEntries.Clear();
                foreach (GlitchStateStore.CriticalWarningRecord persisted in GlitchStateStore.LoadCriticalWarnings(_warningsFilePath))
                {
                    if (persisted == null || string.IsNullOrWhiteSpace(persisted.Message))
                        continue;

                    string warningKey = string.IsNullOrWhiteSpace(persisted.WarningKey) ? "Generic|System" : persisted.WarningKey;
                    WarningSeverity severity = NormalizeWarningSeverity(ResolveWarningSeverity(warningKey.Split('|')[0]));

                    _criticalWarningEntries.Add(new CriticalWarningEntry
                    {
                        TimestampUtc = persisted.TimestampUtc == default(DateTime) ? DateTime.UtcNow : persisted.TimestampUtc,
                        AccountName = string.IsNullOrWhiteSpace(persisted.AccountName) ? "System" : persisted.AccountName,
                        Message = persisted.Message,
                        WarningKey = warningKey,
                        UnlocksTrading = persisted.UnlocksTrading,
                        IsDismissed = persisted.IsDismissed,
                        DismissedUtc = persisted.DismissedUtc,
                        Severity = severity
                    });
                }

                UpdateWarningCountUi();
                RefreshWarningCollectionViews();
            }
            catch
            {
            }
        }

        private void SaveAuditFeedsToDisk(bool force)
        {
            try
            {
                DateTime now = DateTime.UtcNow;
                if (!force)
                {
                    if (!_hasPendingAuditWrite)
                        return;
                    if ((now - _lastAuditWriteUtc).TotalSeconds < 2.0)
                        return;
                }

                var journalSnapshot = _journalEntries
                    .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.Message))
                    .Select(entry => new GlitchStateStore.JournalRecord
                    {
                        TimestampUtc = entry.TimestampUtc,
                        AccountName = entry.AccountName,
                        Category = entry.Category,
                        Message = entry.Message
                    })
                    .ToList();

                var warningSnapshot = _criticalWarningEntries
                    .Where(entry =>
                        entry != null &&
                        !string.IsNullOrWhiteSpace(entry.Message))
                    .Select(entry => new GlitchStateStore.CriticalWarningRecord
                    {
                        TimestampUtc = entry.TimestampUtc,
                        AccountName = entry.AccountName,
                        Message = entry.Message,
                        WarningKey = entry.WarningKey,
                        UnlocksTrading = entry.UnlocksTrading,
                        IsDismissed = entry.IsDismissed,
                        DismissedUtc = entry.DismissedUtc
                    })
                    .ToList();

                GlitchStateStore.SaveJournalEntries(_journalFilePath, journalSnapshot);
                GlitchStateStore.SaveCriticalWarnings(_warningsFilePath, warningSnapshot);

                _hasPendingAuditWrite = false;
                _lastAuditWriteUtc = now;
            }
            catch
            {
            }
        }

        private void LoadAccountGroupsFromDisk()
        {
            try
            {
                _accountGroups.Clear();
                List<GlitchStateStore.AccountGroupRecord> persistedGroups = GlitchStateStore.LoadAccountGroups(_accountGroupsFilePath);
                foreach (GlitchStateStore.AccountGroupRecord persisted in persistedGroups)
                {
                    if (persisted == null || string.IsNullOrWhiteSpace(persisted.GroupId) || string.IsNullOrWhiteSpace(persisted.MasterAccount))
                        continue;

                    double masterSize = persisted.MasterSize > 0
                        ? persisted.MasterSize
                        : ResolveAccountSizeForName(persisted.MasterAccount, 25000);

                    var group = new AccountGroupDefinition
                    {
                        GroupId = persisted.GroupId,
                        MasterAccount = persisted.MasterAccount,
                        MasterSize = masterSize,
                        Members = new ObservableCollection<AccountGroupMemberRow>()
                    };

                    if (persisted.Members != null)
                    {
                        foreach (GlitchStateStore.AccountGroupMemberRecord member in persisted.Members)
                        {
                            if (member == null || string.IsNullOrWhiteSpace(member.FollowerAccount))
                                continue;

                            double followerSize = member.FollowerSize > 0
                                ? member.FollowerSize
                                : ResolveAccountSizeForName(member.FollowerAccount, masterSize);
                            double ratio = member.Ratio > 0
                                ? member.Ratio
                                : ComputeDefaultRatio(followerSize, masterSize);
                            double memberMasterSize = member.MasterSize > 0 ? member.MasterSize : masterSize;

                            group.Members.Add(new AccountGroupMemberRow
                            {
                                IsSelected = false,
                                IsEnabled = member.IsEnabled,
                                MasterAccount = group.MasterAccount,
                                MasterSize = memberMasterSize,
                                MasterSizeDisplay = FormatAccountSize(memberMasterSize),
                                FollowerAccount = member.FollowerAccount,
                                FollowerSize = followerSize,
                                FollowerSizeDisplay = FormatAccountSize(followerSize),
                                Ratio = ratio,
                                Pnl = "-",
                                PnlSign = "Neutral",
                                MaxDd = "-",
                                MaxL = "-",
                                MaxContracts = "-",
                                Position = "0"
                            });
                        }
                    }

                    _accountGroups.Add(group);
                }
            }
            catch
            {
            }
        }

        private void SaveAccountGroupsToDisk()
        {
            try
            {
                var records = new List<GlitchStateStore.AccountGroupRecord>();
                foreach (AccountGroupDefinition group in _accountGroups)
                {
                    if (group == null || string.IsNullOrWhiteSpace(group.GroupId) || string.IsNullOrWhiteSpace(group.MasterAccount))
                        continue;

                    double masterSize = group.MasterSize > 0 ? group.MasterSize : ResolveAccountSizeForName(group.MasterAccount, 25000);
                    var record = new GlitchStateStore.AccountGroupRecord
                    {
                        GroupId = group.GroupId,
                        MasterAccount = group.MasterAccount,
                        MasterSize = masterSize,
                        Members = new List<GlitchStateStore.AccountGroupMemberRecord>()
                    };

                    if (group.Members != null)
                    {
                        foreach (AccountGroupMemberRow member in group.Members.Where(m => m != null && !string.IsNullOrWhiteSpace(m.FollowerAccount)))
                        {
                            double followerSize = member.FollowerSize > 0 ? member.FollowerSize : ResolveAccountSizeForName(member.FollowerAccount, masterSize);
                            double ratio = member.Ratio > 0 ? member.Ratio : ComputeDefaultRatio(followerSize, masterSize);
                            record.Members.Add(new GlitchStateStore.AccountGroupMemberRecord
                            {
                                FollowerAccount = member.FollowerAccount,
                                FollowerSize = followerSize,
                                Ratio = ratio,
                                MasterSize = masterSize,
                                IsEnabled = member.IsEnabled
                            });
                        }
                    }

                    records.Add(record);
                }

                GlitchStateStore.SaveAccountGroups(_accountGroupsFilePath, records);
            }
            catch
            {
            }

            PublishGlitchShellState();
        }

        private void RestoreWindowPlacementFromDisk()
        {
            try
            {
                if (!GlitchStateStore.TryLoadWindowPlacement(_windowPlacementFilePath, out GlitchStateStore.WindowPlacementRecord placement) || placement == null)
                    return;

                var restoredRect = CoerceWindowRectToVisibleBounds(new Rect(placement.Left, placement.Top, placement.Width, placement.Height));

                Left = restoredRect.Left;
                Top = restoredRect.Top;
                Width = restoredRect.Width;
                Height = restoredRect.Height;
                WindowStartupLocation = WindowStartupLocation.Manual;

                _restoreMaximizedOnLoad = placement.IsMaximized;
            }
            catch
            {
            }
        }

        private void SaveWindowPlacementToDisk()
        {
            try
            {
                Rect bounds = GetWindowPlacementBounds();
                Rect safeBounds = CoerceWindowRectToVisibleBounds(bounds);
                GlitchStateStore.SaveWindowPlacement(_windowPlacementFilePath, new GlitchStateStore.WindowPlacementRecord
                {
                    Left = safeBounds.Left,
                    Top = safeBounds.Top,
                    Width = safeBounds.Width,
                    Height = safeBounds.Height,
                    IsMaximized = WindowState == WindowState.Maximized
                });
            }
            catch
            {
            }
        }

        private Rect GetWindowPlacementBounds()
        {
            if (WindowState == WindowState.Normal)
            {
                double width = ActualWidth > 0 ? ActualWidth : Width;
                double height = ActualHeight > 0 ? ActualHeight : Height;
                return new Rect(Left, Top, width, height);
            }

            Rect restore = RestoreBounds;
            if (restore.Width > 0 && restore.Height > 0)
                return restore;

            double fallbackWidth = Width > 0 ? Width : 1400;
            double fallbackHeight = Height > 0 ? Height : 900;
            return new Rect(Left, Top, fallbackWidth, fallbackHeight);
        }

        private static Rect CoerceWindowRectToVisibleBounds(Rect rect)
        {
            const double minWidth = 720;
            const double minHeight = 460;
            const double minVisibleEdge = 48;

            double width = Math.Max(minWidth, rect.Width);
            double height = Math.Max(minHeight, rect.Height);
            double currentVirtualWidth = Math.Max(minWidth, SystemParameters.VirtualScreenWidth);
            double currentVirtualHeight = Math.Max(minHeight, SystemParameters.VirtualScreenHeight);
            width = Math.Min(width, currentVirtualWidth);
            height = Math.Min(height, currentVirtualHeight);

            double virtualLeft = SystemParameters.VirtualScreenLeft;
            double virtualTop = SystemParameters.VirtualScreenTop;
            double virtualWidth = currentVirtualWidth;
            double virtualHeight = currentVirtualHeight;
            double virtualRight = virtualLeft + virtualWidth;
            double virtualBottom = virtualTop + virtualHeight;

            double left = rect.Left;
            double top = rect.Top;

            if (left + minVisibleEdge > virtualRight || left + width - minVisibleEdge < virtualLeft)
                left = virtualLeft + Math.Max(0, (virtualWidth - width) / 2.0);
            if (top + minVisibleEdge > virtualBottom || top + height - minVisibleEdge < virtualTop)
                top = virtualTop + Math.Max(0, (virtualHeight - height) / 2.0);

            left = Math.Max(virtualLeft, Math.Min(left, virtualRight - width));
            top = Math.Max(virtualTop, Math.Min(top, virtualBottom - height));

            return new Rect(left, top, width, height);
        }

        private void LoadPeakStatesFromDisk()
        {
            try
            {
                lock (_peakStateLock)
                {
                    _peakStatesByAccount.Clear();
                    Dictionary<string, GlitchStateStore.PeakStateRecord> persisted = GlitchStateStore.LoadPeakStates(_peakStateFilePath);
                    foreach (var kvp in persisted)
                    {
                        if (string.IsNullOrWhiteSpace(kvp.Key) || kvp.Value == null || kvp.Value.PeakEquity <= 0)
                            continue;

                        _peakStatesByAccount[kvp.Key] = new PeakState
                        {
                            AccountName = kvp.Value.AccountName,
                            PeakEquity = kvp.Value.PeakEquity,
                            LastEquity = kvp.Value.LastEquity,
                            UpdatedUtc = kvp.Value.UpdatedUtc
                        };
                    }
                }
            }
            catch
            {
            }
        }

        private void SavePeakStatesToDisk(bool force)
        {
            try
            {
                List<PeakState> snapshot;
                DateTime now = DateTime.UtcNow;

                lock (_peakStateLock)
                {
                    if (!force)
                    {
                        if (!_hasPendingPeakStateWrite)
                            return;
                        if ((now - _lastPeakStateWriteUtc).TotalSeconds < 2.0)
                            return;
                    }

                    snapshot = _peakStatesByAccount.Values
                        .Where(p => p != null && !string.IsNullOrWhiteSpace(p.AccountName) && p.PeakEquity > 0)
                        .OrderBy(p => p.AccountName, StringComparer.OrdinalIgnoreCase)
                        .Select(p => new PeakState
                        {
                            AccountName = p.AccountName,
                            PeakEquity = p.PeakEquity,
                            LastEquity = p.LastEquity,
                            UpdatedUtc = p.UpdatedUtc
                        })
                        .ToList();
                }

                GlitchStateStore.SavePeakStates(
                    _peakStateFilePath,
                    snapshot.Select(state => new GlitchStateStore.PeakStateRecord
                    {
                        AccountName = state.AccountName,
                        PeakEquity = state.PeakEquity,
                        LastEquity = state.LastEquity,
                        UpdatedUtc = state.UpdatedUtc
                    }).ToList());

                lock (_peakStateLock)
                {
                    _hasPendingPeakStateWrite = false;
                    _lastPeakStateWriteUtc = now;
                }
            }
            catch
            {
            }
        }

        private void SyncAccountRuntimeEventSubscriptions(IReadOnlyList<Account> activeAccounts)
        {
            var activeByName = new Dictionary<string, Account>(StringComparer.OrdinalIgnoreCase);
            if (activeAccounts != null)
            {
                foreach (Account account in activeAccounts)
                {
                    if (account == null || string.IsNullOrWhiteSpace(account.Name))
                        continue;
                    activeByName[account.Name.Trim()] = account;
                }
            }

            foreach (string stale in _accountEventSubscriptions.Keys.Where(k => !activeByName.ContainsKey(k)).ToList())
                UnsubscribeFromAccountRuntimeEvents(stale);

            foreach (Account account in activeByName.Values)
                EnsureAccountRuntimeEventsSubscribed(account);
        }

        private void EnsureAccountRuntimeEventsSubscribed(Account account)
        {
            if (account == null || string.IsNullOrWhiteSpace(account.Name))
                return;

            string accountName = account.Name.Trim();

            if (_accountEventSubscriptions.TryGetValue(accountName, out List<EventBridgeSubscription> existing) && existing.Count > 0)
            {
                // Keep a single subscription set per account name for this window lifetime.
                // Re-subscribing on object-reference churn can stack duplicate handlers.
                return;
            }

            var subscriptions = new List<EventBridgeSubscription>();
            string[] eventNames = { "ExecutionUpdate", "PositionUpdate", "OrderUpdate", "AccountStatusUpdate" };

            foreach (string eventName in eventNames)
            {
                try
                {
                    string eventNameLocal = eventName;
                    EventInfo eventInfo = account.GetType().GetEvent(eventName, BindingFlags.Public | BindingFlags.Instance);
                    if (eventInfo == null || eventInfo.EventHandlerType == null)
                        continue;

                    Action<object, object> callback = (runtimeSender, runtimeArgs) =>
                        OnAccountRuntimeEventBridge(eventNameLocal, runtimeSender, runtimeArgs);
                    Delegate handler = CreateEventBridgeDelegate(eventInfo.EventHandlerType, callback);
                    if (handler == null)
                        continue;

                    eventInfo.AddEventHandler(account, handler);
                    subscriptions.Add(new EventBridgeSubscription
                    {
                        Account = account,
                        EventInfo = eventInfo,
                        Handler = handler
                    });
                }
                catch
                {
                }
            }

            if (subscriptions.Count > 0)
                _accountEventSubscriptions[accountName] = subscriptions;
        }

        private void OnAccountRuntimeEventBridge(string eventName, object sender, object eventArgs)
        {
            try
            {
                Account account = sender as Account ?? TryExtractAccountFromEventArgs(eventArgs);
                if (account == null || string.IsNullOrWhiteSpace(account.Name))
                    return;

                DateTime nowUtc = DateTime.UtcNow;
                bool isAccountItemUpdate = string.Equals(eventName, "AccountItemUpdate", StringComparison.OrdinalIgnoreCase);
                if (isAccountItemUpdate)
                {
                    if (ShouldThrottleAccountItemUpdate(account.Name, nowUtc))
                        return;
                }

                if (!isAccountItemUpdate)
                {
                    double fallbackCash = TryGetAccountItem(account, "CashValue");
                    double equity = GetCurrentEquity(account, fallbackCash);
                    if (TryExtractEquityFromAccountItemEvent(eventArgs, account, fallbackCash, out double eventEquity) && eventEquity > 0)
                        equity = eventEquity;
                    double unrealizedSnapshot = TryGetAccountItem(account, "UnrealizedProfitLoss", "UnrealizedPnL");
                    double unrealizedEquityCandidate = GetUnrealizedEquityCandidate(fallbackCash, unrealizedSnapshot);
                    if (unrealizedEquityCandidate > equity)
                        equity = unrealizedEquityCandidate;
                    UpdatePeakState(BuildPeakStateKey(account.Name, "TrailingUnrealized"), equity);

                    double eodReference = fallbackCash > 0 ? fallbackCash : equity;
                    UpdatePeakState(BuildPeakStateKey(account.Name, "TrailingEod"), eodReference);
                }

                CaptureTradeSourceFromRuntimeEvent(eventName, account, eventArgs, nowUtc);
                TryAppendRuntimeEventJournalEntry(eventName, account, eventArgs);
            }
            catch
            {
            }
        }

        private void CaptureTradeSourceFromRuntimeEvent(string eventName, Account account, object eventArgs, DateTime nowUtc)
        {
            if (account == null || string.IsNullOrWhiteSpace(account.Name) || string.IsNullOrWhiteSpace(eventName))
                return;

            string normalizedEvent = eventName.Trim();
            if (!normalizedEvent.Equals("ExecutionUpdate", StringComparison.OrdinalIgnoreCase) &&
                !normalizedEvent.Equals("OrderUpdate", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!TryResolveTradeSourceFromEvent(eventArgs, out string instrumentRoot, out TradeSourceKind sourceKind))
                return;
            if (sourceKind != TradeSourceKind.Manual && sourceKind != TradeSourceKind.Strategy)
                return;

            string key = BuildAccountInstrumentKey(account.Name, instrumentRoot);
            if (string.IsNullOrWhiteSpace(key))
                return;

            lock (_tradeSourceLock)
            {
                _tradeSourceByAccountInstrument[key] = sourceKind;
                _tradeSourceObservedUtcByAccountInstrument[key] = nowUtc;
            }
        }

        private static bool TryResolveTradeSourceFromEvent(object eventArgs, out string instrumentRoot, out TradeSourceKind sourceKind)
        {
            instrumentRoot = null;
            sourceKind = TradeSourceKind.Unknown;

            object executionObject = TryGetNestedPropertyValue(eventArgs, "Execution");
            object orderObject = TryGetNestedPropertyValue(eventArgs, "Order");
            object sourceObject = executionObject ?? orderObject ?? eventArgs;
            if (sourceObject == null)
                return false;

            string instrumentToken =
                TryGetNestedPropertyValueAsString(sourceObject, "Instrument.MasterInstrument.Name", "Instrument.FullName", "Instrument") ??
                TryGetNestedPropertyValueAsString(orderObject, "Instrument.MasterInstrument.Name", "Instrument.FullName", "Instrument");
            string normalizedInstrument = NormalizeExecutionInstrumentToken(instrumentToken);
            if (string.IsNullOrWhiteSpace(normalizedInstrument) || normalizedInstrument == "-")
                return false;

            string signalName =
                TryGetNestedPropertyValueAsString(sourceObject, "Order.Name", "Name") ??
                TryGetNestedPropertyValueAsString(orderObject, "Name");
            if (IsReplicationInternalSignal(signalName))
                return false;

            string orderEntryToken =
                TryGetNestedPropertyValueAsString(sourceObject, "Order.OrderEntry", "OrderEntry", "Order.Entry") ??
                TryGetNestedPropertyValueAsString(orderObject, "OrderEntry", "Entry");
            sourceKind = ClassifyTradeSource(orderEntryToken, signalName);
            if (sourceKind == TradeSourceKind.Unknown)
                return false;

            instrumentRoot = normalizedInstrument;
            return true;
        }

        private static TradeSourceKind ClassifyTradeSource(string orderEntryToken, string signalName)
        {
            if (ContainsText(orderEntryToken, "Automated") || ContainsText(orderEntryToken, "Strategy"))
                return TradeSourceKind.Strategy;
            if (ContainsText(orderEntryToken, "Manual"))
                return TradeSourceKind.Manual;

            // Fallback only for explicit manual marker to avoid false strategy positives.
            if (ContainsText(signalName, "Manual"))
                return TradeSourceKind.Manual;

            return TradeSourceKind.Unknown;
        }

        private static bool IsReplicationInternalSignal(string signalName)
        {
            if (string.IsNullOrWhiteSpace(signalName))
                return false;

            string token = signalName.Trim();
            return token.StartsWith(ReplicationSignalName, StringComparison.OrdinalIgnoreCase) ||
                   token.StartsWith(ProtectiveStopSignalName, StringComparison.OrdinalIgnoreCase) ||
                   token.StartsWith(ProtectiveTargetSignalName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsText(string value, string token)
        {
            if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(token))
                return false;

            return value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string BuildAccountInstrumentKey(string accountName, string instrumentRoot)
        {
            if (string.IsNullOrWhiteSpace(accountName) || string.IsNullOrWhiteSpace(instrumentRoot))
                return string.Empty;

            return accountName.Trim() + "|" + instrumentRoot.Trim().ToUpperInvariant();
        }

        private void PruneTradeSourceSnapshots(DateTime nowUtc)
        {
            lock (_tradeSourceLock)
            {
                foreach (string staleKey in _tradeSourceObservedUtcByAccountInstrument
                             .Where(kvp => (nowUtc - kvp.Value) > StrategySourceSnapshotTtl)
                             .Select(kvp => kvp.Key)
                             .ToList())
                {
                    _tradeSourceObservedUtcByAccountInstrument.Remove(staleKey);
                    _tradeSourceByAccountInstrument.Remove(staleKey);
                }
            }
        }

        private bool IsStrategyDrivenMasterInstrument(Account masterAccount, string instrumentRoot, DateTime nowUtc)
        {
            if (masterAccount == null || string.IsNullOrWhiteSpace(masterAccount.Name) || string.IsNullOrWhiteSpace(instrumentRoot))
                return false;

            string normalizedRoot = instrumentRoot.Trim().ToUpperInvariant();
            if (HasStrategyWorkingOrdersForInstrumentRoot(masterAccount, normalizedRoot))
                return true;

            int netQty = GetNetQuantityForInstrumentRoot(masterAccount, normalizedRoot);
            string key = BuildAccountInstrumentKey(masterAccount.Name, normalizedRoot);
            if (string.IsNullOrWhiteSpace(key))
                return false;

            lock (_tradeSourceLock)
            {
                if (!_tradeSourceByAccountInstrument.TryGetValue(key, out TradeSourceKind source))
                    return false;

                if (!_tradeSourceObservedUtcByAccountInstrument.TryGetValue(key, out DateTime observedUtc))
                {
                    _tradeSourceByAccountInstrument.Remove(key);
                    return false;
                }

                if ((nowUtc - observedUtc) > StrategySourceSnapshotTtl)
                {
                    _tradeSourceByAccountInstrument.Remove(key);
                    _tradeSourceObservedUtcByAccountInstrument.Remove(key);
                    return false;
                }

                if (netQty == 0 && source != TradeSourceKind.Strategy)
                {
                    _tradeSourceByAccountInstrument.Remove(key);
                    _tradeSourceObservedUtcByAccountInstrument.Remove(key);
                    return false;
                }

                return source == TradeSourceKind.Strategy && netQty != 0;
            }
        }

        private bool HasStrategyWorkingOrdersForInstrumentRoot(Account account, string instrumentRoot)
        {
            if (account == null || string.IsNullOrWhiteSpace(instrumentRoot))
                return false;

            try
            {
                foreach (Order order in GetWorkingOrdersForInstrumentRoot(account, instrumentRoot))
                {
                    if (order == null || IsReplicatedEntryOrder(order) || IsReplicatedProtectiveOrder(order))
                        continue;

                    string orderEntryText = TryGetNestedPropertyValueAsString(order, "OrderEntry", "Entry");
                    if (ContainsText(orderEntryText, "Automated") || ContainsText(orderEntryText, "Strategy"))
                        return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private HashSet<string> BuildStrategyComplianceAccountSet(IReadOnlyList<Account> activeAccounts, DateTime nowUtc)
        {
            var enforceAccounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!_isReplicatingUi || _accountGroups == null || _accountGroups.Count == 0)
                return enforceAccounts;

            var accountsByName = (activeAccounts ?? Array.Empty<Account>())
                .Where(account => account != null && !string.IsNullOrWhiteSpace(account.Name))
                .GroupBy(account => account.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

            foreach (AccountGroupDefinition group in _accountGroups)
            {
                if (group == null || string.IsNullOrWhiteSpace(group.MasterAccount) || group.Members == null || group.Members.Count == 0)
                    continue;
                if (!accountsByName.TryGetValue(group.MasterAccount.Trim(), out Account masterAccount) || masterAccount == null)
                    continue;

                foreach (AccountGroupMemberRow member in group.Members)
                {
                    if (member == null || !member.IsEnabled || string.IsNullOrWhiteSpace(member.FollowerAccount))
                        continue;
                    if (!accountsByName.TryGetValue(member.FollowerAccount.Trim(), out Account followerAccount) || followerAccount == null)
                        continue;

                    foreach (string instrumentRoot in GetSyncInstrumentRoots(masterAccount, followerAccount))
                    {
                        if (string.IsNullOrWhiteSpace(instrumentRoot))
                            continue;

                        if (IsStrategyDrivenMasterInstrument(masterAccount, instrumentRoot, nowUtc))
                        {
                            enforceAccounts.Add(followerAccount.Name.Trim());
                            break;
                        }
                    }
                }
            }

            return enforceAccounts;
        }

        private void TryAppendRuntimeEventJournalEntry(string eventName, Account account, object eventArgs)
        {
            if (account == null || string.IsNullOrWhiteSpace(account.Name))
                return;
            if (string.IsNullOrWhiteSpace(eventName))
                return;

            string accountName = account.Name.Trim();
            string normalizedEvent = eventName.Trim();

            if (normalizedEvent.Equals("AccountItemUpdate", StringComparison.OrdinalIgnoreCase))
                return;

            if (normalizedEvent.Equals("ExecutionUpdate", StringComparison.OrdinalIgnoreCase))
            {
                if (TryBuildExecutionJournalMessage(eventArgs, out string executionMessage))
                {
                    if (TryBuildExecutionJournalSnapshotKey(accountName, eventArgs, executionMessage, out string executionKey, out string executionSnapshot))
                    {
                        if (ShouldLogExecutionSnapshot(executionKey, executionSnapshot, DateTime.UtcNow))
                            AppendJournal(accountName, "Execution", executionMessage);
                    }
                    else
                    {
                        // Fall back to current behavior if we cannot resolve a stable dedupe identity.
                        AppendJournal(accountName, "Execution", executionMessage);
                    }
                }
                return;
            }

            if (normalizedEvent.Equals("OrderUpdate", StringComparison.OrdinalIgnoreCase))
            {
                if (TryBuildOrderJournalMessage(accountName, eventArgs, out string orderKey, out string orderSnapshot, out string orderMessage))
                {
                    if (ShouldLogRuntimeSnapshot(_lastOrderJournalSnapshotByKey, orderKey, orderSnapshot, DateTime.UtcNow))
                        AppendJournal(accountName, "Order", orderMessage);
                }

                string stateText = TryGetNestedPropertyValueAsString(eventArgs, "Order.OrderState", "OrderState", "State");
                string signalName = TryGetNestedPropertyValueAsString(eventArgs, "Order.Name", "Name");
                if (!string.IsNullOrWhiteSpace(stateText) &&
                    stateText.IndexOf("Rejected", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    !string.IsNullOrWhiteSpace(signalName) &&
                    signalName.StartsWith("GLT-PROT-", StringComparison.OrdinalIgnoreCase))
                {
                    string instrument = TryGetNestedPropertyValueAsString(eventArgs, "Order.Instrument.MasterInstrument.Name", "Order.Instrument.FullName", "Order.Instrument");
                    RaiseCriticalWarning(
                        accountName,
                        $"Protective order rejected on {CleanJournalToken(instrument)} ({CleanJournalToken(signalName)}). Replication may be unsynced; verify follower account.",
                        $"ProtectiveRejected|{CleanJournalToken(instrument)}",
                        unlocksTrading: false);
                }
                return;
            }

            if (normalizedEvent.Equals("PositionUpdate", StringComparison.OrdinalIgnoreCase))
            {
                if (TryBuildPositionJournalMessage(accountName, eventArgs, out string positionKey, out string positionSnapshot, out string positionMessage))
                {
                    if (ShouldLogRuntimeSnapshot(_lastPositionJournalSnapshotByKey, positionKey, positionSnapshot, DateTime.UtcNow))
                        AppendJournal(accountName, "Position", positionMessage);
                }
                return;
            }

            if (normalizedEvent.Equals("AccountStatusUpdate", StringComparison.OrdinalIgnoreCase))
            {
                string statusText = TryGetNestedPropertyValueAsString(eventArgs, "Status", "AccountStatus", "State");
                if (!string.IsNullOrWhiteSpace(statusText))
                    AppendJournal(accountName, "Account", "Status update: " + statusText.Trim());
                return;
            }

            string fallback = eventArgs?.ToString();
            if (!string.IsNullOrWhiteSpace(fallback))
                AppendJournal(accountName, "Runtime", normalizedEvent + ": " + fallback.Trim());
        }

        private bool ShouldLogRuntimeSnapshot(
            IDictionary<string, string> snapshots,
            string snapshotKey,
            string snapshotValue,
            DateTime nowUtc)
        {
            if (snapshots == null || string.IsNullOrWhiteSpace(snapshotKey))
                return false;

            if (snapshots.TryGetValue(snapshotKey, out string previousSnapshot) &&
                string.Equals(previousSnapshot, snapshotValue, StringComparison.Ordinal))
            {
                return false;
            }

            string cooldownKey = "RT|" + snapshotKey;
            if (_runtimeJournalCooldownByKey.TryGetValue(cooldownKey, out DateTime cooldownUntilUtc) &&
                cooldownUntilUtc > nowUtc)
            {
                snapshots[snapshotKey] = snapshotValue;
                return false;
            }

            snapshots[snapshotKey] = snapshotValue;
            _runtimeJournalCooldownByKey[cooldownKey] = nowUtc.AddMilliseconds(250);
            return true;
        }

        private bool ShouldLogExecutionSnapshot(string snapshotKey, string snapshotValue, DateTime nowUtc)
        {
            if (string.IsNullOrWhiteSpace(snapshotKey))
                return false;

            bool hasExecutionId = snapshotKey.IndexOf("|EXECID|", StringComparison.OrdinalIgnoreCase) >= 0;
            if (_lastExecutionJournalSnapshotByKey.TryGetValue(snapshotKey, out string previousSnapshot) &&
                (hasExecutionId || string.Equals(previousSnapshot, snapshotValue, StringComparison.Ordinal)))
            {
                return false;
            }

            if (!hasExecutionId)
            {
                string cooldownKey = "EXEC|" + snapshotKey;
                if (_runtimeJournalCooldownByKey.TryGetValue(cooldownKey, out DateTime cooldownUntilUtc) &&
                    cooldownUntilUtc > nowUtc)
                {
                    return false;
                }

                _runtimeJournalCooldownByKey[cooldownKey] = nowUtc.AddMilliseconds(500);
            }

            _lastExecutionJournalSnapshotByKey[snapshotKey] = snapshotValue ?? string.Empty;
            return true;
        }

        private static bool TryBuildExecutionJournalSnapshotKey(
            string accountName,
            object eventArgs,
            string executionMessage,
            out string key,
            out string snapshot)
        {
            key = null;
            snapshot = null;

            if (string.IsNullOrWhiteSpace(accountName) || string.IsNullOrWhiteSpace(executionMessage))
                return false;

            object executionObject = TryGetNestedPropertyValue(eventArgs, "Execution");
            if (executionObject == null)
                executionObject = eventArgs;

            string executionId = TryGetNestedPropertyValueAsString(executionObject, "ExecutionId", "Id");
            string orderId = TryGetNestedPropertyValueAsString(executionObject, "Order.OrderId", "Order.Id", "OrderId");
            string instrument = TryGetNestedPropertyValueAsString(executionObject, "Instrument.MasterInstrument.Name", "Instrument.FullName", "Instrument");
            string action = TryGetNestedPropertyValueAsString(executionObject, "Order.OrderAction", "OrderAction", "MarketPosition");
            string quantity = TryGetNestedPropertyValueAsString(executionObject, "Quantity");
            string price = TryGetNestedPropertyValueAsString(executionObject, "Price", "ExecutionPrice", "FillPrice");
            string signalName = TryGetNestedPropertyValueAsString(executionObject, "Order.Name", "Name");
            string executionTime = TryGetNestedPropertyValueAsString(executionObject, "Time", "ExecutionTime", "Timestamp", "Order.Time");
            string normalizedAction = NormalizeExecutionActionToken(action);
            string normalizedQuantity = NormalizeJournalNumericToken(quantity, "0.####");
            string normalizedPrice = NormalizeJournalNumericToken(price, "0.########");
            string normalizedInstrument = NormalizeExecutionInstrumentToken(instrument);
            string normalizedSignal = CleanJournalToken(signalName);
            string normalizedExecutionTime = NormalizeExecutionTimeToken(executionTime);

            snapshot = string.Join("|",
                normalizedAction,
                normalizedQuantity,
                normalizedInstrument,
                normalizedPrice,
                normalizedSignal,
                normalizedExecutionTime,
                CleanJournalToken(executionMessage));

            string accountToken = CleanJournalToken(accountName);
            if (!string.IsNullOrWhiteSpace(executionId))
            {
                key = accountToken + "|EXECID|" + CleanJournalToken(executionId);
                return true;
            }

            if (!string.IsNullOrWhiteSpace(orderId))
            {
                key = string.Join("|",
                    accountToken,
                    "ORDER",
                    CleanJournalToken(orderId),
                    normalizedInstrument,
                    normalizedAction,
                    normalizedQuantity,
                    normalizedPrice,
                    normalizedSignal,
                    normalizedExecutionTime);
                return true;
            }

            key = accountToken + "|FALLBACK|" + snapshot;
            return true;
        }

        private void PruneRuntimeJournalCaches(DateTime nowUtc)
        {
            foreach (string staleKey in _runtimeJournalCooldownByKey
                .Where(kvp => kvp.Value <= nowUtc)
                .Select(kvp => kvp.Key)
                .ToList())
            {
                _runtimeJournalCooldownByKey.Remove(staleKey);
            }

            const int maxSnapshots = 6000;
            if (_lastOrderJournalSnapshotByKey.Count > maxSnapshots)
                _lastOrderJournalSnapshotByKey.Clear();
            if (_lastPositionJournalSnapshotByKey.Count > maxSnapshots)
                _lastPositionJournalSnapshotByKey.Clear();
            if (_lastExecutionJournalSnapshotByKey.Count > maxSnapshots)
                _lastExecutionJournalSnapshotByKey.Clear();
        }

        private static bool TryBuildExecutionJournalMessage(object eventArgs, out string message)
        {
            message = null;
            object executionObject = TryGetNestedPropertyValue(eventArgs, "Execution");
            if (executionObject == null)
                executionObject = eventArgs;

            string instrument = TryGetNestedPropertyValueAsString(executionObject, "Instrument.MasterInstrument.Name", "Instrument.FullName", "Instrument");
            string action = TryGetNestedPropertyValueAsString(executionObject, "Order.OrderAction", "OrderAction", "MarketPosition");
            string quantity = TryGetNestedPropertyValueAsString(executionObject, "Quantity");
            string price = TryGetNestedPropertyValueAsString(executionObject, "Price", "ExecutionPrice", "FillPrice");
            string orderName = TryGetNestedPropertyValueAsString(executionObject, "Order.Name", "Name");
            string orderEntry = TryGetNestedPropertyValueAsString(executionObject, "Order.OrderEntry", "OrderEntry", "Order.Entry");
            string executionId = TryGetNestedPropertyValueAsString(executionObject, "ExecutionId", "Id");

            if (string.IsNullOrWhiteSpace(instrument) && string.IsNullOrWhiteSpace(quantity) && string.IsNullOrWhiteSpace(price))
                return false;

            message = $"Exec {NormalizeExecutionActionToken(action)} {CleanJournalToken(quantity)} {NormalizeExecutionInstrumentToken(instrument)} @ {CleanJournalToken(price)}";
            if (!string.IsNullOrWhiteSpace(orderName))
                message += $" ({CleanJournalToken(orderName)})";
            string sourceToken = ResolveExecutionSourceLabel(orderEntry, orderName);
            if (!string.IsNullOrWhiteSpace(sourceToken))
                message += $" [SRC:{sourceToken}]";
            string tagToken = ResolveExecutionTagToken(orderName);
            if (!string.IsNullOrWhiteSpace(tagToken))
                message += $" [TAG:{tagToken}]";
            if (!string.IsNullOrWhiteSpace(executionId))
                message += $" [EID:{CleanJournalToken(executionId)}]";
            return true;
        }

        private static string ResolveExecutionSourceLabel(string orderEntryToken, string signalName)
        {
            if (IsReplicationInternalSignal(signalName))
                return "Replication";

            string orderEntry = string.IsNullOrWhiteSpace(orderEntryToken) ? string.Empty : orderEntryToken.Trim();
            if (orderEntry.IndexOf("Automated", StringComparison.OrdinalIgnoreCase) >= 0 ||
                orderEntry.IndexOf("Strategy", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Strategy";
            }

            if (orderEntry.IndexOf("Manual", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Manual";

            if (!string.IsNullOrWhiteSpace(signalName) &&
                signalName.IndexOf("Manual", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Manual";
            }

            return "Unknown";
        }

        private static string ResolveExecutionTagToken(string signalName)
        {
            if (string.IsNullOrWhiteSpace(signalName))
                return string.Empty;

            string signal = signalName.Trim().ToUpperInvariant();
            if (signal.Contains("TRAIL") || signal.Contains("TSL"))
                return "TSL";
            if (signal.StartsWith(ProtectiveTargetSignalName, StringComparison.OrdinalIgnoreCase) ||
                signal.Contains("TARGET") ||
                signal.Contains("TGT"))
            {
                return "TP";
            }

            if (signal.StartsWith(ProtectiveStopSignalName, StringComparison.OrdinalIgnoreCase) ||
                signal.Contains("STOP") ||
                signal.Contains("STP"))
            {
                return "SL";
            }

            if (signal.StartsWith(ReplicationSignalName, StringComparison.OrdinalIgnoreCase))
                return "SYNC";
            if (signal.StartsWith("ENTRY", StringComparison.OrdinalIgnoreCase))
                return "ENTRY";
            if (signal.StartsWith("EXIT", StringComparison.OrdinalIgnoreCase) ||
                signal.StartsWith("CLOSE", StringComparison.OrdinalIgnoreCase))
            {
                return "EXIT";
            }
            if (signal.Contains("FLIP"))
                return "FLIP";

            return string.Empty;
        }

        private static string NormalizeExecutionActionToken(string action)
        {
            string token = CleanJournalToken(action);
            if (token == "-")
                return token;

            return token
                .Replace(" ", string.Empty)
                .Replace("_", string.Empty)
                .Replace("-", string.Empty)
                .Trim();
        }

        private static string NormalizeJournalNumericToken(string token, string format)
        {
            if (string.IsNullOrWhiteSpace(token))
                return "-";

            string trimmed = token.Trim();
            if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double invariantParsed))
                return invariantParsed.ToString(format, CultureInfo.InvariantCulture);
            if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.CurrentCulture, out double currentParsed))
                return currentParsed.ToString(format, CultureInfo.InvariantCulture);

            string dotNormalized = trimmed.Replace(',', '.');
            if (double.TryParse(dotNormalized, NumberStyles.Float, CultureInfo.InvariantCulture, out double dotParsed))
                return dotParsed.ToString(format, CultureInfo.InvariantCulture);

            return CleanJournalToken(trimmed);
        }

        private static string NormalizeExecutionInstrumentToken(string token)
        {
            string cleaned = CleanJournalToken(token);
            if (cleaned == "-")
                return cleaned;

            int delimiter = cleaned.IndexOf(' ');
            if (delimiter > 0)
                cleaned = cleaned.Substring(0, delimiter);

            return cleaned.Trim().ToUpperInvariant();
        }

        private static string NormalizeExecutionTimeToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return "-";

            string trimmed = token.Trim();
            if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime parsedUtc))
                return parsedUtc.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture);
            if (DateTime.TryParse(trimmed, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out parsedUtc))
                return parsedUtc.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture);

            return CleanJournalToken(trimmed);
        }

        private static bool TryBuildOrderJournalMessage(
            string accountName,
            object eventArgs,
            out string key,
            out string snapshot,
            out string message)
        {
            key = null;
            snapshot = null;
            message = null;

            object orderObject = TryGetNestedPropertyValue(eventArgs, "Order");
            if (orderObject == null)
                orderObject = eventArgs;

            string orderId = TryGetNestedPropertyValueAsString(orderObject, "OrderId", "Id", "Name");
            string state = TryGetNestedPropertyValueAsString(orderObject, "OrderState", "State");
            string signalName = TryGetNestedPropertyValueAsString(orderObject, "Name");
            string instrument = TryGetNestedPropertyValueAsString(orderObject, "Instrument.MasterInstrument.Name", "Instrument.FullName", "Instrument");
            string quantity = TryGetNestedPropertyValueAsString(orderObject, "Quantity");
            string limitPrice = TryGetNestedPropertyValueAsString(orderObject, "LimitPrice");
            string stopPrice = TryGetNestedPropertyValueAsString(orderObject, "StopPrice");
            string action = TryGetNestedPropertyValueAsString(orderObject, "OrderAction", "Action");

            if (string.IsNullOrWhiteSpace(orderId) &&
                string.IsNullOrWhiteSpace(state) &&
                string.IsNullOrWhiteSpace(signalName) &&
                string.IsNullOrWhiteSpace(instrument))
            {
                return false;
            }

            bool isProtectiveSignal =
                !string.IsNullOrWhiteSpace(signalName) &&
                (signalName.StartsWith(ProtectiveStopSignalName, StringComparison.OrdinalIgnoreCase) ||
                 signalName.StartsWith(ProtectiveTargetSignalName, StringComparison.OrdinalIgnoreCase));

            if (isProtectiveSignal &&
                !string.IsNullOrWhiteSpace(state) &&
                state.IndexOf("Initialized", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            string orderToken;
            if (isProtectiveSignal)
            {
                orderToken = (signalName ?? "protective") + "|" + CleanJournalToken(instrument);
            }
            else
            {
                orderToken = !string.IsNullOrWhiteSpace(orderId) ? orderId : (signalName ?? "order");
            }
            key = (accountName ?? "account") + "|" + orderToken;
            snapshot = string.Join("|",
                CleanJournalToken(state),
                CleanJournalToken(quantity),
                CleanJournalToken(limitPrice),
                CleanJournalToken(stopPrice),
                CleanJournalToken(action),
                CleanJournalToken(instrument));

            message = $"Order {CleanJournalToken(signalName)} {CleanJournalToken(state)} {CleanJournalToken(action)} {CleanJournalToken(quantity)} {CleanJournalToken(instrument)}";
            if (!string.IsNullOrWhiteSpace(limitPrice))
                message += $" L:{CleanJournalToken(limitPrice)}";
            if (!string.IsNullOrWhiteSpace(stopPrice))
                message += $" S:{CleanJournalToken(stopPrice)}";
            return true;
        }

        private static bool TryBuildPositionJournalMessage(
            string accountName,
            object eventArgs,
            out string key,
            out string snapshot,
            out string message)
        {
            key = null;
            snapshot = null;
            message = null;

            object positionObject = TryGetNestedPropertyValue(eventArgs, "Position");
            if (positionObject == null)
                positionObject = eventArgs;

            string instrument = TryGetNestedPropertyValueAsString(positionObject, "Instrument.MasterInstrument.Name", "Instrument.FullName", "Instrument");
            string quantity = TryGetNestedPropertyValueAsString(positionObject, "Quantity");
            string marketPosition = TryGetNestedPropertyValueAsString(positionObject, "MarketPosition");

            if (string.IsNullOrWhiteSpace(instrument) &&
                string.IsNullOrWhiteSpace(quantity) &&
                string.IsNullOrWhiteSpace(marketPosition))
            {
                return false;
            }

            key = (accountName ?? "account") + "|" + CleanJournalToken(instrument);
            snapshot = CleanJournalToken(quantity) + "|" + CleanJournalToken(marketPosition);
            message = $"Position {CleanJournalToken(instrument)} => {CleanJournalToken(marketPosition)} {CleanJournalToken(quantity)}";
            return true;
        }

        private static string CleanJournalToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "-";
            return value.Replace("\r", " ").Replace("\n", " ").Trim();
        }

        private static Delegate CreateEventBridgeDelegate(Type handlerType, Action<object, object> callback)
        {
            if (handlerType == null || callback == null)
                return null;

            MethodInfo invokeMethod = handlerType.GetMethod("Invoke");
            if (invokeMethod == null || invokeMethod.ReturnType != typeof(void))
                return null;

            ParameterInfo[] parameters = invokeMethod.GetParameters();
            if (parameters.Length != 2)
                return null;

            var senderParameter = LinqExpression.Parameter(parameters[0].ParameterType, "sender");
            var argsParameter = LinqExpression.Parameter(parameters[1].ParameterType, "eventArgs");
            var callbackTarget = LinqExpression.Constant(callback);
            MethodInfo callbackInvoke = typeof(Action<object, object>).GetMethod("Invoke");

            var call = LinqExpression.Call(
                callbackTarget,
                callbackInvoke,
                LinqExpression.Convert(senderParameter, typeof(object)),
                LinqExpression.Convert(argsParameter, typeof(object)));

            return LinqExpression.Lambda(handlerType, call, senderParameter, argsParameter).Compile();
        }

        private static Account TryExtractAccountFromEventArgs(object eventArgs)
        {
            if (eventArgs == null)
                return null;

            object direct = TryGetNestedPropertyValue(eventArgs, "Account", "Execution.Account", "Order.Account", "Position.Account");
            return direct as Account;
        }

        private static bool TryExtractEquityFromAccountItemEvent(object eventArgs, Account account, double fallbackCash, out double equity)
        {
            equity = 0;
            if (eventArgs == null || account == null)
                return false;

            object itemObject = TryGetNestedPropertyValue(eventArgs, "AccountItem", "Item");
            object valueObject = TryGetNestedPropertyValue(eventArgs, "Value");
            if (itemObject == null || valueObject == null)
                return false;
            if (!TryConvertToDouble(valueObject, out double value))
                return false;

            string itemName = itemObject.ToString();
            if (string.IsNullOrWhiteSpace(itemName))
                return false;

            if (itemName.IndexOf("NetLiquidation", StringComparison.OrdinalIgnoreCase) >= 0 && value > 0)
            {
                equity = value;
                return true;
            }

            if (itemName.IndexOf("UnrealizedProfitLoss", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                double cash = fallbackCash > 0 ? fallbackCash : TryGetAccountItem(account, "CashValue");
                double reconstructedEquity = cash + value;
                if (reconstructedEquity > 0)
                {
                    equity = reconstructedEquity;
                    return true;
                }
            }

            return false;
        }

        private static object TryGetNestedPropertyValue(object root, params string[] propertyPaths)
        {
            if (root == null || propertyPaths == null)
                return null;

            foreach (string path in propertyPaths)
            {
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                object current = root;
                string[] segments = path.Split('.');
                bool failed = false;

                foreach (string segment in segments)
                {
                    if (current == null)
                    {
                        failed = true;
                        break;
                    }

                    PropertyInfo property = current.GetType().GetProperty(segment, BindingFlags.Public | BindingFlags.Instance);
                    if (property == null)
                    {
                        failed = true;
                        break;
                    }

                    current = property.GetValue(current, null);
                }

                if (!failed && current != null)
                    return current;
            }

            return null;
        }

        private void UnsubscribeFromAccountRuntimeEvents(string accountName)
        {
            if (string.IsNullOrWhiteSpace(accountName))
                return;

            if (!_accountEventSubscriptions.TryGetValue(accountName, out List<EventBridgeSubscription> subscriptions) || subscriptions == null)
                return;

            foreach (EventBridgeSubscription subscription in subscriptions)
            {
                if (subscription?.Account == null || subscription.EventInfo == null || subscription.Handler == null)
                    continue;

                try
                {
                    subscription.EventInfo.RemoveEventHandler(subscription.Account, subscription.Handler);
                }
                catch
                {
                }
            }

            _accountEventSubscriptions.Remove(accountName);
        }

        private void UnsubscribeFromAllAccountRuntimeEvents()
        {
            foreach (string accountName in _accountEventSubscriptions.Keys.ToList())
                UnsubscribeFromAccountRuntimeEvents(accountName);
        }

        private void LoadSelectionOverridesFromDisk(bool overwriteExisting)
        {
            try
            {
                Dictionary<string, GlitchStateStore.SelectionOverrideRecord> persisted =
                    GlitchStateStore.LoadSelectionOverrides(_overridesFilePath, NormalizeAccountStatus);

                foreach (var kvp in persisted)
                {
                    string accountName = kvp.Key?.Trim();
                    if (string.IsNullOrWhiteSpace(accountName))
                        continue;
                    if (!overwriteExisting && _selectionOverrides.ContainsKey(accountName))
                        continue;

                    GlitchStateStore.SelectionOverrideRecord persistedRow = kvp.Value ?? new GlitchStateStore.SelectionOverrideRecord();

                    _selectionOverrides[accountName] = new AccountSelectionOverride
                    {
                        AccountStatus = NormalizeAccountStatus(persistedRow.AccountStatus),
                        PropFirmId = string.IsNullOrWhiteSpace(persistedRow.PropFirmId) ? "None" : persistedRow.PropFirmId.Trim(),
                        AccountSize = persistedRow.AccountSize,
                        IsManual = persistedRow.IsManual
                    };
                }
            }
            catch
            {
            }
        }

        private void SaveSelectionOverridesToDisk()
        {
            try
            {
                var records = _selectionOverrides
                    .ToDictionary(
                        kvp => kvp.Key,
                        kvp => new GlitchStateStore.SelectionOverrideRecord
                        {
                            AccountStatus = NormalizeAccountStatus(kvp.Value?.AccountStatus),
                            PropFirmId = string.IsNullOrWhiteSpace(kvp.Value?.PropFirmId) ? "None" : kvp.Value.PropFirmId,
                            AccountSize = kvp.Value?.AccountSize,
                            IsManual = kvp.Value?.IsManual ?? false
                        },
                        StringComparer.OrdinalIgnoreCase);

                GlitchStateStore.SaveSelectionOverrides(_overridesFilePath, records);
            }
            catch
            {
            }
        }

        private static string CleanPersistToken(string value)
        {
            return GlitchStateStore.CleanPersistToken(value);
        }

        private static bool ParseBooleanToken(string value)
        {
            return GlitchStateStore.ParseBooleanToken(value);
        }

        private static bool TryConvertToDouble(object rawValue, out double value)
        {
            value = 0;
            if (rawValue == null)
                return false;

            switch (rawValue)
            {
                case double d when !double.IsNaN(d) && !double.IsInfinity(d):
                    value = d;
                    return true;
                case float f when !float.IsNaN(f) && !float.IsInfinity(f):
                    value = f;
                    return true;
                case decimal m:
                    value = (double)m;
                    return true;
                case int i:
                    value = i;
                    return true;
                case long l:
                    value = l;
                    return true;
            }

            string text = rawValue.ToString();
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
            {
                value = parsed;
                return true;
            }

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed))
            {
                value = parsed;
                return true;
            }

            return false;
        }

        private static List<Account> GetActiveAccountsSnapshot()
        {
            var accounts = new List<Account>();

            try
            {
                if (Account.All == null)
                    return accounts;

                lock (Account.All)
                {
                    foreach (var account in Account.All)
                    {
                        if (account == null || string.IsNullOrWhiteSpace(account.Name))
                            continue;
                        if (!IsActiveAccount(account))
                            continue;

                        accounts.Add(account);
                    }
                }
            }
            catch
            {
            }

            return accounts
                .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool IsActiveAccount(Account account)
        {
            if (account == null || string.IsNullOrWhiteSpace(account.Name))
                return false;

            var accountName = account.Name.Trim();
            if (accountName.Equals("Backtest", StringComparison.OrdinalIgnoreCase) ||
                accountName.StartsWith("Playback", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                var accountType = account.GetType();
                bool? isArchived = TryGetBoolProperty(account, accountType, "IsArchived", "Archived", "IsArchive");
                if (isArchived == true)
                    return false;

                bool? isConnected = TryGetBoolProperty(account, accountType, "IsConnected", "Connected");
                if (isConnected.HasValue && !isConnected.Value)
                    return false;

                var connectionProperty = accountType.GetProperty("Connection");
                var connection = connectionProperty?.GetValue(account, null);
                if (connection != null)
                {
                    var statusProperty = connection.GetType().GetProperty("Status");
                    var status = statusProperty?.GetValue(connection, null);
                    if (status != null)
                    {
                        var statusText = status.ToString();
                        if (!string.Equals(statusText, "Connected", StringComparison.OrdinalIgnoreCase))
                            return false;
                    }
                }
            }
            catch
            {
            }

            // Mirror old-simplified eligibility behavior: only accounts with retrievable size are treated as active.
            return GetAccountSizeFromNt(account) > 0;
        }

        private static bool? TryGetBoolProperty(object instance, Type type, params string[] names)
        {
            if (instance == null || type == null || names == null)
                return null;

            foreach (string name in names)
            {
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                PropertyInfo property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (property == null || property.PropertyType != typeof(bool))
                    continue;

                try
                {
                    return (bool)property.GetValue(instance, null);
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }

        private static double GetAccountSizeFromNt(Account account)
        {
            if (account == null)
                return 0;

            double netLiquidation = TryGetAccountItem(account,
                "NetLiquidation", "NetLiquidationValue", "NetLiquidationAmount", "NetLiq");
            if (netLiquidation > 0)
                return netLiquidation;

            double cashValue = TryGetAccountItem(account, "CashValue");
            if (cashValue > 0)
                return cashValue;

            return 0;
        }

        private AccountGridRow BuildAccountRow(Account account, AccountSelectionOverride selectionOverride)
        {
            double accountSizeRaw = GetAccountSizeFromNt(account);
            double cashValue = TryGetAccountItem(account, "CashValue");
            double realizedPnl = TryGetAccountItem(account, "RealizedProfitLoss", "RealizedPnL", "RealizedProfit", "RealizedLoss");
            double unrealizedPnl = TryGetAccountItem(account, "UnrealizedProfitLoss", "UnrealizedPnL");
            double totalPnl = TryGetAccountItem(account, "TotalProfitLoss", "TotalPnL");
            bool hasSelectionOverride = selectionOverride != null;
            bool hasManualOverride = hasSelectionOverride && selectionOverride.IsManual;

            if (Math.Abs(totalPnl) < 0.0000001)
                totalPnl = realizedPnl + unrealizedPnl;

            string inferredFirmId = InferPropFirmId(account, out _);
            string selectedFirmId = hasManualOverride ? selectionOverride.PropFirmId : null;
            if (string.IsNullOrWhiteSpace(selectedFirmId))
                selectedFirmId = inferredFirmId;
            if (string.IsNullOrWhiteSpace(selectedFirmId))
                selectedFirmId = "None";

            string inferredStatus = InferAccountStatus(account, selectedFirmId, out _);
            string selectedStatus = hasManualOverride ? selectionOverride.AccountStatus : null;
            if (string.IsNullOrWhiteSpace(selectedStatus))
                selectedStatus = inferredStatus;
            selectedStatus = NormalizeAccountStatus(selectedStatus);

            if (string.Equals(selectedStatus, "Sim", StringComparison.OrdinalIgnoreCase))
                selectedFirmId = "None";

            string executionProvider = GetExecutionProviderHint(account);
            var accountSizeChoices = GetAccountSizeOptionsForFirm(selectedFirmId, selectedStatus, executionProvider);
            double selectedAccountSize = hasSelectionOverride ? (selectionOverride.AccountSize ?? 0) : 0;
            if (selectedAccountSize <= 0)
            {
                double? inferredAccountSize = InferAccountSizeFromName(account?.Name);
                if (inferredAccountSize.HasValue && inferredAccountSize.Value > 0)
                    selectedAccountSize = inferredAccountSize.Value;
            }
            if (selectedAccountSize <= 0)
                selectedAccountSize = accountSizeRaw;

            if (accountSizeChoices.Count > 0)
                selectedAccountSize = FindNearestAccountSize(selectedAccountSize, accountSizeChoices);
            else
                selectedAccountSize = RoundToNearestStep(selectedAccountSize, 25000);

            if (!hasManualOverride &&
                account != null &&
                !string.IsNullOrWhiteSpace(account.Name) &&
                selectedAccountSize > 0)
            {
                _selectionOverrides[account.Name] = new AccountSelectionOverride
                {
                    AccountStatus = selectedStatus,
                    PropFirmId = selectedFirmId,
                    AccountSize = selectedAccountSize,
                    IsManual = false
                };
            }

            _firmRules.TryGetValue(selectedFirmId, out FirmRuleMetadata selectedFirmRule);
            double currentEquity = GetCurrentEquity(account, cashValue);
            double effectiveBalance = currentEquity > 0 ? currentEquity : cashValue;
            double tierProfitReference = cashValue > 0 ? cashValue : effectiveBalance;
            double tierProfit = Math.Max(0, tierProfitReference - selectedAccountSize);
            var tierRule = GetRuleForFirmAndSize(selectedFirmId, selectedStatus, executionProvider, selectedAccountSize, tierProfit);
            double profitTarget = tierRule?.ProfitTarget ?? 0;
            double maxDrawdown = tierRule?.MaxDrawdown ?? 0;
            double intratradeDrawdown = tierRule?.IntratradeDrawdown ?? 0;
            double dailyLossLimit = tierRule?.DailyLossLimit ?? 0;
            double configuredMicroContractMultiplier = ResolveMicroContractMultiplier(selectedFirmRule);
            string microContractRootRegex = ResolveMicroContractRootRegex(selectedFirmRule);
            double maxContracts = ResolveMaxContractsLimit(tierRule, configuredMicroContractMultiplier);
            double maxMicroContracts = ResolveMaxMicroContractLimit(tierRule, configuredMicroContractMultiplier);
            double effectiveMicroContractMultiplier = ResolveEffectiveMicroContractMultiplier(
                maxContracts,
                maxMicroContracts,
                configuredMicroContractMultiplier);
            string position = GetAccountEffectivePositionDisplay(account);
            if (intratradeDrawdown <= 0 &&
                maxDrawdown > 0 &&
                !string.Equals(selectedStatus, "Sim", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(selectedFirmId, "ApexTraderFunding", StringComparison.OrdinalIgnoreCase))
            {
                intratradeDrawdown = Math.Round(maxDrawdown * 0.30, MidpointRounding.AwayFromZero);
            }

            double unrealizedEquityCandidate = GetUnrealizedEquityCandidate(cashValue, unrealizedPnl);
            string maxLossTracking = NormalizeMaxLossTracking(selectedFirmRule?.MaxLossTracking, selectedFirmRule?.DrawdownType);
            double peakSourceValue = string.Equals(maxLossTracking, "TrailingEod", StringComparison.OrdinalIgnoreCase)
                ? (cashValue > 0 ? cashValue : currentEquity)
                : Math.Max(currentEquity, unrealizedEquityCandidate);
            string peakStateKey = BuildPeakStateKey(account?.Name, maxLossTracking);
            double trailingPeak = GetOrUpdateTrailingPeak(peakStateKey, peakSourceValue);
            // Source B (PropFirmRules semantics + live peak/provider context): modeled liquidation threshold.
            double? modeledMinMargin = CalculateMinMargin(
                selectedStatus,
                selectedFirmRule,
                selectedAccountSize,
                maxDrawdown,
                profitTarget,
                realizedPnl,
                currentEquity,
                trailingPeak,
                executionProvider);

            double? minMargin = modeledMinMargin;
            double nativeThreshold = TryGetNativeLiquidationThreshold(account);
            bool allowNativeThreshold = selectedFirmRule == null || selectedFirmRule.UseNativeLiquidationThresholdWhenAvailable;
            if (allowNativeThreshold && nativeThreshold > 0)
            {
                double? normalizedNative = NormalizeNativeThreshold(
                    nativeThreshold,
                    effectiveBalance,
                    modeledMinMargin,
                    selectedAccountSize,
                    maxDrawdown);
                if (normalizedNative.HasValue)
                    minMargin = normalizedNative.Value;
            }
            // Derived metric: distance from live equity to liquidation threshold.
            double? bufferMargin = minMargin.HasValue ? effectiveBalance - minMargin.Value : (double?)null;
            double headroomRatioRaw = maxDrawdown > 0 && bufferMargin.HasValue ? bufferMargin.Value / maxDrawdown : double.NaN;
            double headroomSafeWidth = 0;
            double headroomUsedWidth = 0;
            double riskRatioRaw = double.NaN;
            if (!double.IsNaN(headroomRatioRaw) && !double.IsInfinity(headroomRatioRaw))
            {
                double clampedHeadroom = Math.Max(0, Math.Min(1, headroomRatioRaw));
                headroomSafeWidth = 110.0 * clampedHeadroom;
                headroomUsedWidth = 110.0 - headroomSafeWidth;
                riskRatioRaw = 1.0 - clampedHeadroom;
            }
            string bufferVsMaxDdSign = GetBufferVsMaxDdSign(bufferMargin, maxDrawdown);
            bool isIntraDdWarning = GetIntraDdWarning(unrealizedPnl, intratradeDrawdown);
            bool isNetLiqWarning = GetNetLiqWarning(bufferMargin, maxDrawdown);
            string equityVsSizeSign = GetEquityDisplaySign(isNetLiqWarning, isIntraDdWarning, effectiveBalance, selectedAccountSize, maxDrawdown, bufferMargin);
            double evalProfitTargetLockBalance =
                string.Equals(selectedStatus, "Eval", StringComparison.OrdinalIgnoreCase) &&
                selectedAccountSize > 0 &&
                profitTarget > 0
                    ? selectedAccountSize + profitTarget + Math.Max(0, selectedFirmRule?.EvalProfitTargetLockBuffer ?? 20.0)
                    : double.NaN;

            string propFirmDisplay = ToFirmDisplayName(selectedFirmId);
            var propFirmOptions = new List<string>(_propFirmDisplayOptions);
            if (!propFirmOptions.Contains(propFirmDisplay, StringComparer.OrdinalIgnoreCase))
                propFirmOptions.Add(propFirmDisplay);

            var accountSizeOptionDisplays = accountSizeChoices.Select(FormatAccountSize).ToList();
            if (accountSizeOptionDisplays.Count == 0)
                accountSizeOptionDisplays = _globalAccountSizeOptions.Select(FormatAccountSize).ToList();

            return new AccountGridRow
            {
                DisplayName = account.Name,
                AccountStatus = selectedStatus,
                PropFirmId = selectedFirmId,
                AccountStatusOptions = _accountStatusOptions,
                PropFirmDisplay = propFirmDisplay,
                PropFirmOptions = propFirmOptions,
                AccountSizeSelection = FormatAccountSize(selectedAccountSize),
                AccountSizeOptions = accountSizeOptionDisplays,
                CashValue = FormatCurrency(effectiveBalance),
                MaxDrawdown = FormatCurrencyOrDash(maxDrawdown > 0 ? (double?)maxDrawdown : null),
                IntratradeDrawdown = FormatCurrencyOrDash(intratradeDrawdown > 0 ? (double?)intratradeDrawdown : null),
                MinMargin = FormatCurrencyOrDash(minMargin),
                BufferMargin = FormatCurrencyOrDash(bufferMargin),
                AccountSizeRaw = selectedAccountSize,
                ProfitTargetRaw = profitTarget,
                DailyLossLimitRaw = dailyLossLimit > 0 ? dailyLossLimit : 0,
                EvalProfitTargetLockBalanceRaw = evalProfitTargetLockBalance,
                EquityRaw = effectiveBalance,
                NetLiqRaw = minMargin ?? double.NaN,
                BufferMarginRaw = bufferMargin ?? double.NaN,
                MaxDrawdownRaw = maxDrawdown > 0 ? maxDrawdown : 0,
                IntratradeDrawdownRaw = intratradeDrawdown > 0 ? intratradeDrawdown : 0,
                RiskDisplay = FormatPercentOrDash(riskRatioRaw),
                RiskSign = GetRiskSign(riskRatioRaw),
                HeadroomSafeWidth = headroomSafeWidth,
                HeadroomUsedWidth = headroomUsedWidth,
                MaxContractsRaw = maxContracts > 0 ? maxContracts : 0,
                MaxMicrosRaw = maxMicroContracts > 0 ? maxMicroContracts : 0,
                MaxContracts = FormatWholeNumberOrDash(maxContracts > 0 ? (double?)maxContracts : null),
                MicroContractRootRegex = microContractRootRegex,
                MicroContractMultiplier = effectiveMicroContractMultiplier,
                Position = position,
                EquityVsSizeSign = equityVsSizeSign,
                BufferVsMaxDdSign = bufferVsMaxDdSign,
                IsIntraDdWarning = isIntraDdWarning,
                IsNetLiqWarning = isNetLiqWarning,
                RealizedPnl = FormatCurrency(realizedPnl),
                UnrealizedPnl = FormatCurrency(unrealizedPnl),
                TotalPnl = FormatCurrency(totalPnl),
                RealizedPnlSign = GetPnlSign(realizedPnl),
                UnrealizedPnlSign = GetUnrealizedPnlDisplaySign(unrealizedPnl, intratradeDrawdown, isIntraDdWarning),
                TotalPnlSign = GetPnlSign(totalPnl),
                RealizedPnlRaw = realizedPnl,
                UnrealizedPnlRaw = unrealizedPnl,
                TotalPnlRaw = totalPnl,
                IsManualSelection = hasManualOverride,
                SnapshotKey = string.Join("|",
                    selectedStatus,
                    selectedFirmId,
                    selectedAccountSize.ToString("F0", CultureInfo.InvariantCulture),
                    FormatCurrencyOrDash(minMargin),
                    FormatCurrencyOrDash(bufferMargin),
                    FormatCurrency(effectiveBalance),
                    FormatCurrency(realizedPnl),
                    FormatCurrency(unrealizedPnl),
                    FormatCurrency(totalPnl),
                    FormatPercentOrDash(riskRatioRaw),
                    position,
                    FormatWholeNumberOrDash(maxContracts > 0 ? (double?)maxContracts : null),
                    FormatWholeNumberOrDash(maxMicroContracts > 0 ? (double?)maxMicroContracts : null),
                    microContractRootRegex,
                    effectiveMicroContractMultiplier.ToString("0.####", CultureInfo.InvariantCulture),
                    maxDrawdown.ToString("F2", CultureInfo.InvariantCulture),
                    intratradeDrawdown.ToString("F2", CultureInfo.InvariantCulture),
                    isIntraDdWarning ? "1" : "0",
                    isNetLiqWarning ? "1" : "0",
                    hasManualOverride ? "M" : "A")
            };
        }

        private static double TryGetAccountItem(Account account, params string[] itemNames)
        {
            if (account == null || itemNames == null || itemNames.Length == 0)
                return 0;

            foreach (var itemName in itemNames)
            {
                if (string.IsNullOrWhiteSpace(itemName))
                    continue;

                try
                {
                    if (Enum.TryParse(itemName, true, out AccountItem item))
                        return account.Get(item, Currency.UsDollar);
                }
                catch
                {
                }
            }

            return 0;
        }

        private static string GetAccountEffectivePositionDisplay(Account account)
        {
            if (account == null)
                return "0";

            try
            {
                double signedContracts = GetAccountSignedPositionContracts(account);
                signedContracts += GetTotalInFlightReplicationEntryDelta(account);
                return FormatSignedContracts(signedContracts);
            }
            catch
            {
            }

            return "0";
        }

        private static double GetAccountSignedPositionContracts(Account account)
        {
            if (account == null)
                return 0;

            try
            {
                double signedContracts = 0;
                foreach (Position position in account.Positions)
                {
                    if (position == null || position.MarketPosition == MarketPosition.Flat)
                        continue;

                    double quantity = Math.Abs(position.Quantity);
                    if (position.MarketPosition == MarketPosition.Long)
                        signedContracts += quantity;
                    else if (position.MarketPosition == MarketPosition.Short)
                        signedContracts -= quantity;
                }

                return signedContracts;
            }
            catch
            {
                return 0;
            }
        }

        private static string FormatSignedContracts(double contracts)
        {
            if (double.IsNaN(contracts) || double.IsInfinity(contracts))
                return "0";

            long rounded = (long)Math.Round(contracts, MidpointRounding.AwayFromZero);
            if (rounded > 0)
                return "+" + rounded.ToString(CultureInfo.CurrentCulture);
            if (rounded < 0)
                return rounded.ToString(CultureInfo.CurrentCulture);
            return "0";
        }

        private static string FormatCurrency(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return "-";

            string number = Math.Abs(value).ToString("N0", CultureInfo.CurrentCulture);
            return value < 0 ? "-$ " + number : "$ " + number;
        }

        private static string FormatAccountSize(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
                return "-";

            double inThousands = Math.Round(value / 1000.0, MidpointRounding.AwayFromZero);
            return inThousands.ToString("N0", CultureInfo.CurrentCulture) + "k";
        }

        private static string FormatWholeNumberOrDash(double? value)
        {
            if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value) || value.Value <= 0)
                return "-";

            return Math.Round(value.Value, MidpointRounding.AwayFromZero)
                .ToString("N0", CultureInfo.CurrentCulture);
        }

        private static double RoundToNearestStep(double value, double step)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0 || step <= 0)
                return 0;

            double rounded = Math.Round(value / step, MidpointRounding.AwayFromZero) * step;
            if (rounded < step)
                rounded = step;

            return rounded;
        }

        private static double ResolveMaxContractsLimit(FirmTierRule tierRule, double microContractMultiplier)
        {
            return GlitchComplianceEngine.ResolveMaxContractsLimit(
                tierRule?.MaxContracts ?? 0,
                tierRule?.MaxMicros ?? 0,
                microContractMultiplier);
        }

        private static double ResolveMaxMicroContractLimit(FirmTierRule tierRule, double microContractMultiplier)
        {
            return GlitchComplianceEngine.ResolveMaxMicrosLimit(
                tierRule?.MaxMicros ?? 0,
                tierRule?.MaxContracts ?? 0,
                microContractMultiplier);
        }

        private static double ResolveMicroContractMultiplier(FirmRuleMetadata firmRule)
        {
            double value = firmRule?.MicroContractMultiplier ?? 0;
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
                return DefaultMicroContractMultiplier;

            return value;
        }

        private static string ResolveMicroContractRootRegex(FirmRuleMetadata firmRule)
        {
            string pattern = firmRule?.MicroContractRootRegex;
            if (string.IsNullOrWhiteSpace(pattern))
                return DefaultMicroContractRootRegex;

            string trimmed = pattern.Trim();
            if (trimmed.Length > MaxMicroContractRegexLength)
                return DefaultMicroContractRootRegex;

            return trimmed;
        }

        private static double ResolveEffectiveMicroContractMultiplier(
            double maxContracts,
            double maxMicros,
            double configuredMicroContractMultiplier)
        {
            if (maxContracts > 0 && maxMicros > 0)
            {
                double derived = maxMicros / maxContracts;
                if (!double.IsNaN(derived) && !double.IsInfinity(derived) && derived > 0)
                    return derived;
            }

            if (!double.IsNaN(configuredMicroContractMultiplier) &&
                !double.IsInfinity(configuredMicroContractMultiplier) &&
                configuredMicroContractMultiplier > 0)
            {
                return configuredMicroContractMultiplier;
            }

            return DefaultMicroContractMultiplier;
        }

        private static string NormalizeAccountStatus(string status)
        {
            return GlitchComplianceEngine.NormalizeAccountStatus(status);
        }

        private string ToFirmDisplayName(string firmId)
        {
            if (string.IsNullOrWhiteSpace(firmId))
                return "None";

            if (_firmIdToDisplay.TryGetValue(firmId, out string displayName) && !string.IsNullOrWhiteSpace(displayName))
                return displayName;

            return "None";
        }

        private string ToFirmId(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
                return "None";

            string normalized = displayName.Trim();
            int discontinuedIndex = normalized.IndexOf("(discontinued)", StringComparison.OrdinalIgnoreCase);
            if (discontinuedIndex >= 0)
                normalized = normalized.Substring(0, discontinuedIndex).TrimEnd();

            if (_firmDisplayToId.TryGetValue(normalized, out string firmId))
                return firmId;

            return "None";
        }

        private static string InferPropFirmId(Account account, out string confidence)
        {
            return GlitchComplianceEngine.InferPropFirmId(account, out confidence);
        }

        private static string InferAccountStatus(Account account, string firmId, out string confidence)
        {
            return GlitchComplianceEngine.InferAccountStatus(account, firmId, out confidence);
        }

        private static string GetExecutionProviderHint(Account account)
        {
            return GlitchComplianceEngine.GetExecutionProviderHint(account);
        }

        private static string NormalizeMaxLossTracking(string maxLossTracking, string drawdownType)
        {
            return GlitchComplianceEngine.NormalizeMaxLossTracking(maxLossTracking, drawdownType);
        }

        private static string BuildPeakStateKey(string accountName, string maxLossTracking)
        {
            return GlitchComplianceEngine.BuildPeakStateKey(accountName, maxLossTracking);
        }

        private static double TryGetNativeLiquidationThreshold(Account account)
        {
            return GlitchComplianceEngine.TryGetNativeLiquidationThreshold(account);
        }

        private static double? NormalizeNativeThreshold(
            double nativeValue,
            double effectiveBalance,
            double? modeledThreshold,
            double accountSize,
            double maxDrawdown)
        {
            return GlitchComplianceEngine.NormalizeNativeThreshold(nativeValue, effectiveBalance, modeledThreshold, accountSize, maxDrawdown);
        }

        private static bool StatusMatchesFilter(string status, string filterCsv)
        {
            return GlitchComplianceEngine.StatusMatchesFilter(status, filterCsv);
        }

        private static string NormalizeFloorCapMode(string mode)
        {
            return GlitchComplianceEngine.NormalizeFloorCapMode(mode);
        }

        private static string NormalizeFloorCapTrigger(string trigger)
        {
            return GlitchComplianceEngine.NormalizeFloorCapTrigger(trigger);
        }

        private bool IsTradingLocked(string accountName)
        {
            if (string.IsNullOrWhiteSpace(accountName))
                return false;

            string normalized = accountName.Trim();
            return _riskLockedAccounts.Contains(normalized) || _evalTargetLockedAccounts.Contains(normalized);
        }

        private static string BuildTradingLockAckKey(string accountName, string warningType)
        {
            if (string.IsNullOrWhiteSpace(accountName))
                return string.Empty;

            string normalizedWarningType = string.IsNullOrWhiteSpace(warningType) ? "Generic" : warningType.Trim();
            return normalizedWarningType + "|" + accountName.Trim();
        }

        private static string ExtractTradingLockAckAccountName(string ackKey)
        {
            if (string.IsNullOrWhiteSpace(ackKey))
                return string.Empty;

            int separator = ackKey.LastIndexOf('|');
            if (separator < 0 || separator >= ackKey.Length - 1)
                return ackKey.Trim();

            return ackKey.Substring(separator + 1).Trim();
        }

        private static bool MatchesTokenFilter(string value, string filterCsv)
        {
            if (string.IsNullOrWhiteSpace(filterCsv))
                return true;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string normalizedValue = value.Trim();
            string[] tokens = filterCsv.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string token in tokens)
            {
                string normalizedToken = (token ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(normalizedToken))
                    continue;

                if (normalizedValue.IndexOf(normalizedToken, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private static FirmProviderRule ResolveProviderRule(FirmRuleMetadata firmRule, string executionProvider)
        {
            if (firmRule?.ProviderRules == null || firmRule.ProviderRules.Count == 0)
                return null;

            return firmRule.ProviderRules
                .Where(rule => rule != null && MatchesTokenFilter(executionProvider, rule.ProviderFilter))
                .OrderByDescending(rule => string.IsNullOrWhiteSpace(rule.ProviderFilter) ? 0 : rule.ProviderFilter.Length)
                .FirstOrDefault();
        }

        private static void ResolveEvalThresholdStopBehavior(
            FirmRuleMetadata firmRule,
            string executionProvider,
            out bool stopsAtProfitTarget,
            out double stopOffset)
        {
            stopsAtProfitTarget = false;
            stopOffset = 0;
            if (firmRule == null)
                return;

            FirmProviderRule providerRule = ResolveProviderRule(firmRule, executionProvider);
            if (providerRule != null && providerRule.EvalThresholdStopsAtProfitTarget.HasValue)
            {
                stopsAtProfitTarget = providerRule.EvalThresholdStopsAtProfitTarget.Value;
                stopOffset = providerRule.EvalThresholdStopOffset;
                return;
            }

            string provider = (executionProvider ?? string.Empty).Trim();
            if (provider.IndexOf("wealthcharts", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                stopsAtProfitTarget = firmRule.EvalWealthChartsThresholdStopsAtProfitTarget;
                stopOffset = firmRule.EvalWealthChartsThresholdStopOffset;
                return;
            }

            if (provider.IndexOf("rithmic", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                stopsAtProfitTarget = firmRule.EvalRithmicThresholdStopsAtProfitTarget;
                stopOffset = firmRule.EvalRithmicThresholdStopOffset;
                return;
            }

            if (provider.IndexOf("tradovate", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                stopsAtProfitTarget = firmRule.EvalTradovateThresholdStopsAtProfitTarget;
                stopOffset = firmRule.EvalTradovateThresholdStopOffset;
                return;
            }

            stopsAtProfitTarget = firmRule.EvalRithmicThresholdStopsAtProfitTarget;
            stopOffset = firmRule.EvalRithmicThresholdStopOffset;
        }

        private static double TryGetNestedPropertyValueAsDouble(object root, params string[] propertyPaths)
        {
            if (root == null || propertyPaths == null)
                return 0;

            foreach (string path in propertyPaths)
            {
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                object current = root;
                string[] segments = path.Split('.');
                bool failed = false;

                foreach (string segment in segments)
                {
                    if (current == null)
                    {
                        failed = true;
                        break;
                    }

                    PropertyInfo property = current.GetType().GetProperty(segment, BindingFlags.Public | BindingFlags.Instance);
                    if (property == null)
                    {
                        failed = true;
                        break;
                    }

                    current = property.GetValue(current, null);
                }

                if (failed || current == null)
                    continue;

                if (current is double d && !double.IsNaN(d) && !double.IsInfinity(d))
                    return d;
                if (current is decimal m)
                    return (double)m;
                if (current is float f && !float.IsNaN(f) && !float.IsInfinity(f))
                    return f;
                if (current is int i)
                    return i;
                if (current is long l)
                    return l;

                string text = current.ToString();
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) && parsed > 0)
                    return parsed;
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed) && parsed > 0)
                    return parsed;
            }

            return 0;
        }

        private static string TryGetNestedPropertyValueAsString(object root, params string[] propertyPaths)
        {
            if (root == null || propertyPaths == null)
                return null;

            foreach (string path in propertyPaths)
            {
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                object current = root;
                string[] segments = path.Split('.');
                bool failed = false;

                foreach (string segment in segments)
                {
                    if (current == null)
                    {
                        failed = true;
                        break;
                    }

                    PropertyInfo property = current.GetType().GetProperty(segment, BindingFlags.Public | BindingFlags.Instance);
                    if (property == null)
                    {
                        failed = true;
                        break;
                    }

                    current = property.GetValue(current, null);
                }

                if (!failed && current != null)
                {
                    string value = current.ToString();
                    if (!string.IsNullOrWhiteSpace(value))
                        return value.Trim();
                }
            }

            return null;
        }

        private List<double> GetAccountSizeOptionsForFirm(string firmId, string accountStatus, string executionProvider)
        {
            if (string.IsNullOrWhiteSpace(firmId) || string.Equals(firmId, "None", StringComparison.OrdinalIgnoreCase))
                return new List<double>(_globalAccountSizeOptions);

            if (_firmRules.TryGetValue(firmId, out FirmRuleMetadata firm))
            {
                var supportedSizes = firm.Tiers
                    .Where(t =>
                        t != null &&
                        t.AccountSize > 0 &&
                        (string.IsNullOrWhiteSpace(t.StatusFilter) || StatusMatchesFilter(accountStatus, t.StatusFilter)) &&
                        MatchesTokenFilter(executionProvider, t.ProviderFilter))
                    .Select(t => t.AccountSize)
                    .Distinct()
                    .OrderBy(v => v)
                    .ToList();

                if (supportedSizes.Count > 0)
                    return supportedSizes;
            }

            return new List<double>(_globalAccountSizeOptions);
        }

        private FirmTierRule GetRuleForFirmAndSize(
            string firmId,
            string accountStatus,
            string executionProvider,
            double accountSize,
            double accountProfit)
        {
            if (string.IsNullOrWhiteSpace(firmId) || string.Equals(firmId, "None", StringComparison.OrdinalIgnoreCase))
                return null;
            if (!_firmRules.TryGetValue(firmId, out FirmRuleMetadata firm))
                return null;
            if (firm.Tiers == null || firm.Tiers.Count == 0)
                return null;

            double normalizedProfit = accountProfit;
            if (double.IsNaN(normalizedProfit) || double.IsInfinity(normalizedProfit) || normalizedProfit < 0)
                normalizedProfit = 0;

            List<FirmTierRule> matchingContext = firm.Tiers
                .Where(t =>
                    t != null &&
                    t.AccountSize > 0 &&
                    (string.IsNullOrWhiteSpace(t.StatusFilter) || StatusMatchesFilter(accountStatus, t.StatusFilter)) &&
                    MatchesTokenFilter(executionProvider, t.ProviderFilter))
                .ToList();
            if (matchingContext.Count == 0)
                matchingContext = firm.Tiers.Where(t => t != null && t.AccountSize > 0).ToList();
            if (matchingContext.Count == 0)
                return null;

            IEnumerable<FirmTierRule> matchingSize = matchingContext;
            if (accountSize > 0)
            {
                var exactSize = matchingContext.Where(t => Math.Abs(t.AccountSize - accountSize) < 0.0001).ToList();
                if (exactSize.Count > 0)
                {
                    matchingSize = exactSize;
                }
                else
                {
                    double nearestSize = matchingContext
                        .Select(t => t.AccountSize)
                        .Distinct()
                        .OrderBy(size => Math.Abs(size - accountSize))
                        .ThenBy(size => size)
                        .First();
                    matchingSize = matchingContext.Where(t => Math.Abs(t.AccountSize - nearestSize) < 0.0001).ToList();
                }
            }

            List<FirmTierRule> tiersForSize = matchingSize.ToList();
            if (tiersForSize.Count == 0)
                tiersForSize = matchingContext.OrderBy(t => Math.Abs(t.AccountSize - accountSize)).ToList();

            FirmTierRule byProfit = tiersForSize
                .Where(t =>
                    normalizedProfit >= t.MinProfit &&
                    (t.MaxProfit <= 0 || normalizedProfit < t.MaxProfit))
                .OrderByDescending(t => t.MinProfit)
                .ThenBy(t => t.MaxProfit <= 0 ? double.MaxValue : t.MaxProfit)
                .FirstOrDefault();
            if (byProfit != null)
                return byProfit;

            return tiersForSize
                .OrderBy(t => t.MinProfit)
                .ThenBy(t => t.MaxProfit <= 0 ? double.MaxValue : t.MaxProfit)
                .FirstOrDefault();
        }

        private static double FindNearestAccountSize(double target, List<double> availableSizes)
        {
            if (availableSizes == null || availableSizes.Count == 0)
                return RoundToNearestStep(target, 25000);

            if (target <= 0)
                return availableSizes[0];

            return availableSizes
                .OrderBy(size => Math.Abs(size - target))
                .ThenBy(size => size)
                .First();
        }

        private static double? ParseAccountSize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            string normalized = value.Trim().ToLowerInvariant();
            if (normalized.EndsWith("k", StringComparison.Ordinal))
            {
                string kNumeric = normalized.Substring(0, normalized.Length - 1).Trim();
                if (double.TryParse(kNumeric, NumberStyles.Float, CultureInfo.InvariantCulture, out double kValue) && kValue > 0)
                    return kValue * 1000.0;
                if (double.TryParse(kNumeric, NumberStyles.Float, CultureInfo.CurrentCulture, out kValue) && kValue > 0)
                    return kValue * 1000.0;
            }

            string numeric = new string(value.Where(c => char.IsDigit(c) || c == '.' || c == ',' || c == '-').ToArray());
            if (string.IsNullOrWhiteSpace(numeric))
                return null;

            string invariant = numeric.Replace(",", string.Empty);
            if (double.TryParse(invariant, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) && result > 0)
                return result;

            if (double.TryParse(numeric, NumberStyles.Float, CultureInfo.CurrentCulture, out result) && result > 0)
                return result;

            return null;
        }

        private static double? InferAccountSizeFromName(string accountName)
        {
            if (string.IsNullOrWhiteSpace(accountName))
                return null;

            Match kMatch = Regex.Match(accountName, @"(?<!\d)(\d{2,3})(?:\s*)K\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (kMatch.Success &&
                double.TryParse(kMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out double thousands) &&
                thousands > 0)
            {
                return thousands * 1000.0;
            }

            Match fullMatch = Regex.Match(accountName, @"(?<!\d)(\d{5,6})(?!\d)", RegexOptions.CultureInvariant);
            if (fullMatch.Success &&
                double.TryParse(fullMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out double absolute) &&
                absolute >= 10000)
            {
                return absolute;
            }

            return null;
        }

        private static string GetPnlSign(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return "Neutral";
            if (value > 0)
                return "Positive";
            if (value < 0)
                return "Negative";
            return "Neutral";
        }

        private static string GetRiskSign(double ratio)
        {
            if (double.IsNaN(ratio) || double.IsInfinity(ratio))
                return "Neutral";

            if (ratio > 0.70)
                return "Negative";
            if (ratio < 0.70)
                return "Positive";
            return "Neutral";
        }

        private static string GetEquityDisplaySign(
            bool isNetLiqWarning,
            bool isIntraDdWarning,
            double equity,
            double accountSize,
            double maxDrawdown,
            double? bufferMargin)
        {
            if (isNetLiqWarning || isIntraDdWarning)
                return "Negative";

            if (maxDrawdown > 0 && bufferMargin.HasValue && !double.IsNaN(bufferMargin.Value) && !double.IsInfinity(bufferMargin.Value))
            {
                double bufferRatio = bufferMargin.Value / maxDrawdown;
                if (!double.IsNaN(bufferRatio) && !double.IsInfinity(bufferRatio) && bufferRatio < BufferCriticalLockThresholdRatio)
                    return "Negative";
            }

            if (!double.IsNaN(equity) && !double.IsInfinity(equity) &&
                !double.IsNaN(accountSize) && !double.IsInfinity(accountSize) && accountSize > 0)
            {
                const double tolerance = 0.5;
                if (equity > accountSize + tolerance)
                    return "Positive";
            }

            return "Neutral";
        }

        private static double GetUnrealizedEquityCandidate(double cashValue, double unrealizedPnl)
        {
            if (cashValue <= 0 || double.IsNaN(cashValue) || double.IsInfinity(cashValue))
                return 0;
            if (double.IsNaN(unrealizedPnl) || double.IsInfinity(unrealizedPnl))
                return 0;

            double candidate = cashValue + unrealizedPnl;
            return candidate > 0 ? candidate : 0;
        }

        private static string GetBufferVsMaxDdSign(double? bufferMargin, double maxDrawdown)
        {
            if (!bufferMargin.HasValue || maxDrawdown <= 0 || double.IsNaN(maxDrawdown) || double.IsInfinity(maxDrawdown))
                return "Neutral";

            double ratio = bufferMargin.Value / maxDrawdown;
            if (double.IsNaN(ratio) || double.IsInfinity(ratio))
                return "Neutral";

            if (ratio < 0.20)
                return "Negative"; // Orange
            if (ratio > 1.0)
                return "Positive"; // Teal

            return "Neutral"; // White for 20%-100%
        }

        private static bool GetIntraDdWarning(double unrealizedPnl, double intratradeDrawdown)
        {
            if (intratradeDrawdown <= 0 || double.IsNaN(intratradeDrawdown) || double.IsInfinity(intratradeDrawdown))
                return false;
            if (double.IsNaN(unrealizedPnl) || double.IsInfinity(unrealizedPnl))
                return false;

            double unrealizedLoss = -unrealizedPnl;
            if (unrealizedLoss <= 0)
                return false;

            return unrealizedLoss >= (0.7 * intratradeDrawdown);
        }

        private static bool GetNetLiqWarning(double? bufferMargin, double maxDrawdown)
        {
            if (!bufferMargin.HasValue || maxDrawdown <= 0 || double.IsNaN(maxDrawdown) || double.IsInfinity(maxDrawdown))
                return false;

            return bufferMargin.Value < (0.5 * maxDrawdown);
        }

        private static string GetUnrealizedPnlDisplaySign(double unrealizedPnl, double intratradeDrawdown, bool isIntraDdWarning)
        {
            if (isIntraDdWarning)
                return "Negative";

            if (double.IsNaN(unrealizedPnl) || double.IsInfinity(unrealizedPnl))
                return "Neutral";

            if (unrealizedPnl > 0)
                return "Positive";

            if (unrealizedPnl < 0)
            {
                if (intratradeDrawdown > 0 && !double.IsNaN(intratradeDrawdown) && !double.IsInfinity(intratradeDrawdown))
                {
                    double unrealizedLoss = -unrealizedPnl;
                    if (unrealizedLoss >= (0.5 * intratradeDrawdown))
                        return "Negative";
                }

                return "Neutral";
            }

            return "Neutral";
        }

        private static double GetCurrentEquity(Account account, double fallbackCashValue)
        {
            if (account != null)
            {
                double netLiq = TryGetAccountItem(account, "NetLiquidation", "NetLiquidationValue", "NetLiquidationAmount", "NetLiq");
                if (netLiq > 0)
                    return netLiq;
            }

            return fallbackCashValue > 0 ? fallbackCashValue : 0;
        }

        private double GetOrUpdateTrailingPeak(string accountName, double currentEquity)
        {
            if (string.IsNullOrWhiteSpace(accountName) || currentEquity <= 0)
                return currentEquity;
            accountName = accountName.Trim();

            UpdatePeakState(accountName, currentEquity);

            lock (_peakStateLock)
            {
                if (_peakStatesByAccount.TryGetValue(accountName, out PeakState state) && state != null && state.PeakEquity > 0)
                    return state.PeakEquity;
            }

            return currentEquity;
        }

        private void UpdatePeakState(string accountName, double currentEquity)
        {
            if (string.IsNullOrWhiteSpace(accountName) || currentEquity <= 0 || double.IsNaN(currentEquity) || double.IsInfinity(currentEquity))
                return;
            accountName = accountName.Trim();

            DateTime now = DateTime.UtcNow;
            lock (_peakStateLock)
            {
                if (!_peakStatesByAccount.TryGetValue(accountName, out PeakState state) || state == null)
                {
                    _peakStatesByAccount[accountName] = new PeakState
                    {
                        AccountName = accountName,
                        PeakEquity = currentEquity,
                        LastEquity = currentEquity,
                        UpdatedUtc = now
                    };
                    _hasPendingPeakStateWrite = true;
                    return;
                }

                state.LastEquity = currentEquity;
                bool peakChanged = false;
                if (currentEquity > state.PeakEquity)
                {
                    state.PeakEquity = currentEquity;
                    peakChanged = true;
                }

                state.UpdatedUtc = now;
                _peakStatesByAccount[accountName] = state;
                if (peakChanged)
                    _hasPendingPeakStateWrite = true;
            }
        }

        private static double? CalculateMinMargin(
            string accountStatus,
            FirmRuleMetadata firmRule,
            double accountSize,
            double maxDrawdown,
            double profitTarget,
            double realizedPnl,
            double currentEquity,
            double trailingPeak,
            string executionProvider)
        {
            ResolveEvalThresholdStopBehavior(
                firmRule,
                executionProvider,
                out bool evalThresholdStopsAtProfitTarget,
                out double evalThresholdStopOffset);

            return GlitchComplianceEngine.CalculateMinMargin(
                accountStatus,
                firmRule?.DrawdownType,
                firmRule?.MaxLossTracking,
                firmRule?.MaxLossFloorCapMode,
                firmRule?.MaxLossFloorCapOffset ?? 0,
                firmRule?.MaxLossFloorCapTrigger,
                firmRule?.MaxLossFloorCapStatuses,
                evalThresholdStopsAtProfitTarget,
                evalThresholdStopOffset,
                accountSize,
                maxDrawdown,
                profitTarget,
                realizedPnl,
                currentEquity,
                trailingPeak);
        }

        private static string FormatCurrencyOrDash(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return "-";
            return FormatCurrency(value);
        }

        private static string FormatCurrencyOrDash(double? value)
        {
            if (!value.HasValue)
                return "-";
            return FormatCurrencyOrDash(value.Value);
        }

        private static void ApplySkinResource(FrameworkElement element, DependencyProperty property, params string[] keys)
        {
            if (element == null || property == null || keys == null || keys.Length == 0)
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

        private static string FindSkinResourceKey(FrameworkElement context, params string[] keys)
        {
            if (context == null || keys == null || keys.Length == 0)
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

        private static void ApplySkinSetter(Style style, DependencyProperty property, FrameworkElement context, params string[] keys)
        {
            if (style == null || property == null || context == null || keys == null || keys.Length == 0)
                return;

            string key = FindSkinResourceKey(context, keys);
            if (string.IsNullOrWhiteSpace(key))
                return;

            style.Setters.Add(new Setter(property, new DynamicResourceExtension(key)));
        }

        private static Style FindSkinStyle(FrameworkElement context, Type targetType)
        {
            if (context == null || targetType == null)
                return null;

            object resource = context.TryFindResource(targetType) ?? Application.Current?.TryFindResource(targetType);
            return resource as Style;
        }

        private static object FindSkinResource(FrameworkElement context, string key)
        {
            if (context == null || string.IsNullOrWhiteSpace(key))
                return null;

            return context.TryFindResource(key) ?? Application.Current?.TryFindResource(key);
        }

        private static Brush FindSkinBrush(FrameworkElement context, params string[] keys)
        {
            if (context == null || keys == null)
                return null;

            foreach (string key in keys)
            {
                object resource = FindSkinResource(context, key);
                if (resource is Brush brush)
                    return brush;

                if (resource is Color color)
                    return new SolidColorBrush(color);
            }

            return null;
        }

        private static double? FindSkinDouble(FrameworkElement context, params string[] keys)
        {
            if (context == null || keys == null)
                return null;

            foreach (string key in keys)
            {
                object resource = FindSkinResource(context, key);
                if (resource is double d)
                    return d;
                if (resource is float f)
                    return f;
                if (resource is int i)
                    return i;
            }

            return null;
        }

        private sealed class JournalEntry
        {
            public DateTime TimestampUtc { get; set; }
            public string AccountName { get; set; }
            public string Category { get; set; }
            public string Message { get; set; }
            public string TimeText => TimestampUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);
        }

        private sealed class CriticalWarningEntry : INotifyPropertyChanged
        {
            private bool _isDismissed;
            private DateTime? _dismissedUtc;

            public event PropertyChangedEventHandler PropertyChanged;

            public DateTime TimestampUtc { get; set; }
            public string AccountName { get; set; }
            public string Message { get; set; }
            public string WarningKey { get; set; }
            public bool UnlocksTrading { get; set; }
            public WarningSeverity Severity { get; set; } = WarningSeverity.Operational;

            public DateTime? DismissedUtc
            {
                get => _dismissedUtc;
                set
                {
                    if (_dismissedUtc == value)
                        return;

                    _dismissedUtc = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DismissedUtc)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TimeText)));
                }
            }

            public bool IsDismissed
            {
                get => _isDismissed;
                set
                {
                    if (_isDismissed == value)
                        return;

                    _isDismissed = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDismissed)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusSign)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TimeText)));
                }
            }

            public string TimeText =>
                (DismissedUtc ?? TimestampUtc).ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);

            public string StatusText => IsDismissed ? "Dismissed" : (Severity == WarningSeverity.Critical ? "Active" : "Logged");
            public string StatusSign => IsDismissed || Severity != WarningSeverity.Critical ? "Neutral" : "Negative";
        }

    }
}


