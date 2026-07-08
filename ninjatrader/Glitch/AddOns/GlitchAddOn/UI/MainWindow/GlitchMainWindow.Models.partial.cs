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
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NinjaTrader.Cbi;

namespace Glitch.UI
{
    public partial class GlitchMainWindow
    {
        private sealed class AccountSelectionOverride
        {
            public string AccountStatus { get; set; }
            public string PropFirmId { get; set; }
            public double? AccountSize { get; set; }
            public bool IsManual { get; set; }
        }

        private sealed class PeakState
        {
            public string AccountName { get; set; }
            public double PeakEquity { get; set; }
            public double LastEquity { get; set; }
            public DateTime UpdatedUtc { get; set; }
        }

        private sealed class AccountGroupDefinition
        {
            public string GroupId { get; set; }
            public string MasterAccount { get; set; }
            public double MasterSize { get; set; }
            public ObservableCollection<AccountGroupMemberRow> Members { get; set; }
        }

        private sealed class AccountGroupMemberRow : INotifyPropertyChanged
        {
            private bool _isSelected;
            private bool _isEnabled;
            private string _masterAccount;
            private double _masterSize;
            private string _masterSizeDisplay;
            private string _followerAccount;
            private double _followerSize;
            private string _followerSizeDisplay;
            private double _ratio;
            private string _pnl;
            private string _pnlSign;
            private string _maxDd;
            private string _maxL;
            private string _maxContracts;
            private string _position;

            public event PropertyChangedEventHandler PropertyChanged;

            public bool IsSelected
            {
                get => _isSelected;
                set => SetField(ref _isSelected, value, nameof(IsSelected));
            }

            public bool IsEnabled
            {
                get => _isEnabled;
                set => SetField(ref _isEnabled, value, nameof(IsEnabled));
            }

            public string MasterAccount
            {
                get => _masterAccount;
                set => SetField(ref _masterAccount, value, nameof(MasterAccount));
            }

            public double MasterSize
            {
                get => _masterSize;
                set => SetField(ref _masterSize, value, nameof(MasterSize));
            }

            public string MasterSizeDisplay
            {
                get => _masterSizeDisplay;
                set => SetField(ref _masterSizeDisplay, value, nameof(MasterSizeDisplay));
            }

            public string FollowerAccount
            {
                get => _followerAccount;
                set => SetField(ref _followerAccount, value, nameof(FollowerAccount));
            }

            public double FollowerSize
            {
                get => _followerSize;
                set => SetField(ref _followerSize, value, nameof(FollowerSize));
            }

            public string FollowerSizeDisplay
            {
                get => _followerSizeDisplay;
                set => SetField(ref _followerSizeDisplay, value, nameof(FollowerSizeDisplay));
            }

            public double Ratio
            {
                get => _ratio;
                set => SetField(ref _ratio, value, nameof(Ratio));
            }

            public string Pnl
            {
                get => _pnl;
                set => SetField(ref _pnl, value, nameof(Pnl));
            }

            public string PnlSign
            {
                get => _pnlSign;
                set => SetField(ref _pnlSign, value, nameof(PnlSign));
            }

            public string MaxDd
            {
                get => _maxDd;
                set => SetField(ref _maxDd, value, nameof(MaxDd));
            }

            public string MaxL
            {
                get => _maxL;
                set => SetField(ref _maxL, value, nameof(MaxL));
            }

            public string MaxContracts
            {
                get => _maxContracts;
                set => SetField(ref _maxContracts, value, nameof(MaxContracts));
            }

            public string Position
            {
                get => _position;
                set => SetField(ref _position, value, nameof(Position));
            }

            private void SetField<T>(ref T backingField, T value, string propertyName)
            {
                if (EqualityComparer<T>.Default.Equals(backingField, value))
                    return;

                backingField = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        private sealed class AnalyticsDialVisual
        {
            public RotateTransform NeedleRotation { get; set; }
            public TextBlock ScoreText { get; set; }
            public TextBlock SignalText { get; set; }
        }

        private sealed class AnalyticsTimeframeMetricVisual
        {
            public TextBlock SignalText { get; set; }
            public TextBlock AveragePriceValueText { get; set; }
            public TextBlock AveragePriceHintText { get; set; }
            public TextBlock AtrValueText { get; set; }
            public TextBlock AtrHintText { get; set; }
            public TextBlock AdxValueText { get; set; }
            public TextBlock AdxHintText { get; set; }
            public AnalyticsFactorVisual EmaFactor { get; set; }
            public AnalyticsFactorVisual RawFactor { get; set; }
            public AnalyticsFactorVisual RsiFactor { get; set; }
            public AnalyticsFactorVisual StochFactor { get; set; }
            public AnalyticsFactorVisual ZFactor { get; set; }
            public AnalyticsFactorVisual OscillatorFactor { get; set; }
            public AnalyticsFactorVisual MaFactor { get; set; }
            public AnalyticsFactorVisual OrderFlowFactor { get; set; }
            public AnalyticsFactorVisual AgreementFactor { get; set; }
            public TextBlock BiasValueText { get; set; }
            public TextBlock SetupValueText { get; set; }
            public TextBlock QualityValueText { get; set; }
            public TextBlock GateValueText { get; set; }
        }

        private sealed class AnalyticsCardExpansionVisual
        {
            public Border Card { get; set; }
            public Button ToggleButton { get; set; }
            public Border DetailsHost { get; set; }
            public FrameworkElement DetailsContent { get; set; }
        }

        private sealed class AnalyticsFactorVisual
        {
            public FrameworkElement RowHost { get; set; }
            public Border LeftFill { get; set; }
            public Border RightFill { get; set; }
            public TextBlock ValueText { get; set; }
            public double HalfWidth { get; set; }
        }

        private sealed class AnalyticsUnifiedSignalState
        {
            public string BiasLabel { get; set; }
            public double BiasScore { get; set; }
            public string ExecutionSignalLabel { get; set; }
            public double ExecutionScore { get; set; }
            public string SetupLabel { get; set; }
            public int Confidence { get; set; }
            public int Tradeability { get; set; }
            public int Quality { get; set; }
            public double AgreementScore { get; set; }
            public string GateLabel { get; set; }
            public bool IsActionable { get; set; }
            public string RegimeLabel { get; set; }
            public string NoTradeReasons { get; set; }
        }

        private struct AnalyticsDecisionComponentScore
        {
            public double EffectiveScore { get; set; }
            public double TechnicalScore { get; set; }
            public double AgreementScore { get; set; }
            public double SignedAgreementScore { get; set; }
        }

        private enum ReplicationVetoReason
        {
            None = 0,
            MasterCapExceeded = 1,
            FollowerCapExceeded = 2,
            TradingLocked = 3,
            BurstDetected = 4,
            LocalComplianceBreach = 5,
            MissingMasterProtective = 6,
            Unknown = 7
        }

        private enum ComplianceBreachReason
        {
            None = 0,
            BufferCriticalLock = 1,
            EvalProfitTargetLock = 2,
            UnrealizedLossFlatten = 3,
            MaxContractsExceeded = 4,
            NoProtectionDetected = 5
        }

        private enum FlattenOrigin
        {
            Unknown = 0,
            StrategyRisk = 1,
            AddonGovernor = 2,
            ReplicationHardResync = 3,
            FallbackAccountFlatten = 4
        }

        private enum WarningSeverity
        {
            Critical = 0,
            Notice = 1,
            Operational = 2,
            Informational = 3
        }

        private sealed class ReplicationIntent
        {
            public string Key { get; set; }
            public Account MasterAccount { get; set; }
            public Account FollowerAccount { get; set; }
            public string InstrumentRoot { get; set; }
            public Instrument TradeInstrument { get; set; }
            public int TargetNetQty { get; set; }
            public bool EnforceStrategyCompliance { get; set; }
        }

        private sealed class ReplicationBurstState
        {
            public DateTime WindowStartUtc { get; set; }
            public int LastObservedQty { get; set; }
            public int QtyChangeCount { get; set; }
        }

        private sealed class ReplicationPendingSubmitState
        {
            public int TargetNetQty { get; set; }
            public int FollowerNetQtyAtSubmit { get; set; }
            public DateTime ExpiresUtc { get; set; }
        }

        private sealed class ProtectiveTemplate
        {
            public bool HasStop { get; set; }
            public double StopPrice { get; set; }
            public bool HasTarget { get; set; }
            public double TargetPrice { get; set; }
        }

        private sealed class EventBridgeSubscription
        {
            public Account Account { get; set; }
            public EventInfo EventInfo { get; set; }
            public Delegate Handler { get; set; }
        }

        private sealed class FirmRuleMetadata
        {
            public string FirmId { get; set; }
            public string UiDisplayName { get; set; }
            public string Status { get; set; }
            public string DrawdownType { get; set; }
            public string MaxLossTracking { get; set; }
            public string MaxLossThresholdUpdate { get; set; }
            public string MaxLossFloorCapMode { get; set; }
            public double MaxLossFloorCapOffset { get; set; }
            public string MaxLossFloorCapTrigger { get; set; }
            public string MaxLossFloorCapStatuses { get; set; }
            public bool HasDailyLossLimit { get; set; }
            public bool AllowSundayTrading { get; set; }
            public int MinimumTradingDays { get; set; }
            public double ConsistencyRulePercent { get; set; }
            public string TradingStartTime { get; set; }
            public string TradingEndTime { get; set; }
            public bool EvalRithmicThresholdStopsAtProfitTarget { get; set; }
            public bool EvalTradovateThresholdStopsAtProfitTarget { get; set; }
            public bool EvalWealthChartsThresholdStopsAtProfitTarget { get; set; }
            public double EvalRithmicThresholdStopOffset { get; set; }
            public double EvalTradovateThresholdStopOffset { get; set; }
            public double EvalWealthChartsThresholdStopOffset { get; set; }
            public double EvalProfitTargetLockBuffer { get; set; }
            public bool UseNativeLiquidationThresholdWhenAvailable { get; set; }
            public string MicroContractRootRegex { get; set; }
            public double MicroContractMultiplier { get; set; }
            public List<FirmProviderRule> ProviderRules { get; set; }
            public List<FirmTierRule> Tiers { get; set; }
            public CopyTradingPolicyMetadata CopyTradingPolicy { get; set; }
        }

        private sealed class CopyTradingPolicyMetadata
        {
            public string Allowed { get; set; }
            public bool SameOwnerOnly { get; set; }
            public int? MaxAccounts { get; set; }
            public string Notes { get; set; }
            public string SourceUrl { get; set; }
        }

        private sealed class FirmProviderRule
        {
            public string ProviderFilter { get; set; }
            public bool? EvalThresholdStopsAtProfitTarget { get; set; }
            public double EvalThresholdStopOffset { get; set; }
        }

        private sealed class FirmTierRule
        {
            public double AccountSize { get; set; }
            public double MaxContracts { get; set; }
            public double MaxMicros { get; set; }
            public double MaxDrawdown { get; set; }
            public double IntratradeDrawdown { get; set; }
            public double ProfitTarget { get; set; }
            public double DailyLossLimit { get; set; }
            public string StatusFilter { get; set; }
            public string ProviderFilter { get; set; }
            public double MinProfit { get; set; }
            public double MaxProfit { get; set; }
        }

        private sealed class SummaryMetricRow
        {
            public string Metric { get; set; }
            public string All { get; set; }
            public string Long { get; set; }
            public string Short { get; set; }
        }

        private sealed class SummaryReasonRow
        {
            public string Reason { get; set; }
            public string Trades { get; set; }
            public string WinRate { get; set; }
            public string AvgPoints { get; set; }
            public string AvgPointsSign { get; set; }
        }

        private sealed class SummaryTradeRow
        {
            public string OpenTime { get; set; }
            public string CloseTime { get; set; }
            public string Account { get; set; }
            public string Instrument { get; set; }
            public string Side { get; set; }
            public string Contracts { get; set; }
            public string PnlPoints { get; set; }
            public string PnlSign { get; set; }
            public string Duration { get; set; }
            public string Exit { get; set; }
            public string CloseReason { get; set; }
        }
    }
}

