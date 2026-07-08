//
// Account grid row: INotifyPropertyChanged + ApplyFrom to avoid ObservableCollection row replacement churn.
//

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace Glitch.UI
{
    public partial class GlitchMainWindow
    {
        private sealed partial class AccountGridRow : INotifyPropertyChanged
        {
            private string _displayName;
            private string _accountStatus;
            private string _propFirmId;
            private List<string> _accountStatusOptions;
            private string _propFirmDisplay;
            private List<string> _propFirmOptions;
            private string _accountSizeSelection;
            private List<string> _accountSizeOptions;
            private string _cashValue;
            private string _maxDrawdown;
            private string _intratradeDrawdown;
            private string _minMargin;
            private string _bufferMargin;
            private double _accountSizeRaw;
            private double _profitTargetRaw;
            private double _dailyLossLimitRaw;
            private double _evalProfitTargetLockBalanceRaw;
            private double _equityRaw;
            private double _netLiqRaw;
            private double _bufferMarginRaw;
            private double _maxDrawdownRaw;
            private double _intratradeDrawdownRaw;
            private string _riskDisplay;
            private string _riskSign;
            private double _headroomSafeWidth;
            private double _headroomUsedWidth;
            private double _maxContractsRaw;
            private double _maxMicrosRaw;
            private string _maxContracts;
            private string _microContractRootRegex;
            private double _microContractMultiplier;
            private string _position;
            private string _equityVsSizeSign;
            private string _bufferVsMaxDdSign;
            private bool _isIntraDdWarning;
            private bool _isNetLiqWarning;
            private string _realizedPnl;
            private string _unrealizedPnl;
            private string _totalPnl;
            private string _realizedPnlSign;
            private string _unrealizedPnlSign;
            private string _totalPnlSign;
            private double _realizedPnlRaw;
            private double _unrealizedPnlRaw;
            private double _totalPnlRaw;
            private bool _isManualSelection;
            private string _snapshotKey;

            public event PropertyChangedEventHandler PropertyChanged;

            public string DisplayName { get => _displayName; set => SetField(ref _displayName, value); }
            public string AccountStatus { get => _accountStatus; set => SetField(ref _accountStatus, value); }
            public string PropFirmId { get => _propFirmId; set => SetField(ref _propFirmId, value); }
            public List<string> AccountStatusOptions { get => _accountStatusOptions; set => SetListField(ref _accountStatusOptions, value); }
            public string PropFirmDisplay { get => _propFirmDisplay; set => SetField(ref _propFirmDisplay, value); }
            public List<string> PropFirmOptions { get => _propFirmOptions; set => SetListField(ref _propFirmOptions, value); }
            public string AccountSizeSelection { get => _accountSizeSelection; set => SetField(ref _accountSizeSelection, value); }
            public List<string> AccountSizeOptions { get => _accountSizeOptions; set => SetListField(ref _accountSizeOptions, value); }
            public string CashValue { get => _cashValue; set => SetField(ref _cashValue, value); }
            public string MaxDrawdown { get => _maxDrawdown; set => SetField(ref _maxDrawdown, value); }
            public string IntratradeDrawdown { get => _intratradeDrawdown; set => SetField(ref _intratradeDrawdown, value); }
            public string MinMargin { get => _minMargin; set => SetField(ref _minMargin, value); }
            public string BufferMargin { get => _bufferMargin; set => SetField(ref _bufferMargin, value); }
            public double AccountSizeRaw { get => _accountSizeRaw; set => SetField(ref _accountSizeRaw, value); }
            public double ProfitTargetRaw { get => _profitTargetRaw; set => SetField(ref _profitTargetRaw, value); }
            public double DailyLossLimitRaw { get => _dailyLossLimitRaw; set => SetField(ref _dailyLossLimitRaw, value); }
            public double EvalProfitTargetLockBalanceRaw { get => _evalProfitTargetLockBalanceRaw; set => SetField(ref _evalProfitTargetLockBalanceRaw, value); }
            public double EquityRaw { get => _equityRaw; set => SetField(ref _equityRaw, value); }
            public double NetLiqRaw { get => _netLiqRaw; set => SetField(ref _netLiqRaw, value); }
            public double BufferMarginRaw { get => _bufferMarginRaw; set => SetField(ref _bufferMarginRaw, value); }
            public double MaxDrawdownRaw { get => _maxDrawdownRaw; set => SetField(ref _maxDrawdownRaw, value); }
            public double IntratradeDrawdownRaw { get => _intratradeDrawdownRaw; set => SetField(ref _intratradeDrawdownRaw, value); }
            public string RiskDisplay { get => _riskDisplay; set => SetField(ref _riskDisplay, value); }
            public string RiskSign { get => _riskSign; set => SetField(ref _riskSign, value); }
            public double HeadroomSafeWidth { get => _headroomSafeWidth; set => SetField(ref _headroomSafeWidth, value); }
            public double HeadroomUsedWidth { get => _headroomUsedWidth; set => SetField(ref _headroomUsedWidth, value); }
            public double MaxContractsRaw { get => _maxContractsRaw; set => SetField(ref _maxContractsRaw, value); }
            public double MaxMicrosRaw { get => _maxMicrosRaw; set => SetField(ref _maxMicrosRaw, value); }
            public string MaxContracts { get => _maxContracts; set => SetField(ref _maxContracts, value); }
            public string MicroContractRootRegex { get => _microContractRootRegex; set => SetField(ref _microContractRootRegex, value); }
            public double MicroContractMultiplier { get => _microContractMultiplier; set => SetField(ref _microContractMultiplier, value); }
            public string Position { get => _position; set => SetField(ref _position, value); }
            public string EquityVsSizeSign { get => _equityVsSizeSign; set => SetField(ref _equityVsSizeSign, value); }
            public string BufferVsMaxDdSign { get => _bufferVsMaxDdSign; set => SetField(ref _bufferVsMaxDdSign, value); }
            public bool IsIntraDdWarning { get => _isIntraDdWarning; set => SetField(ref _isIntraDdWarning, value); }
            public bool IsNetLiqWarning { get => _isNetLiqWarning; set => SetField(ref _isNetLiqWarning, value); }
            public string RealizedPnl { get => _realizedPnl; set => SetField(ref _realizedPnl, value); }
            public string UnrealizedPnl { get => _unrealizedPnl; set => SetField(ref _unrealizedPnl, value); }
            public string TotalPnl { get => _totalPnl; set => SetField(ref _totalPnl, value); }
            public string RealizedPnlSign { get => _realizedPnlSign; set => SetField(ref _realizedPnlSign, value); }
            public string UnrealizedPnlSign { get => _unrealizedPnlSign; set => SetField(ref _unrealizedPnlSign, value); }
            public string TotalPnlSign { get => _totalPnlSign; set => SetField(ref _totalPnlSign, value); }
            public double RealizedPnlRaw { get => _realizedPnlRaw; set => SetField(ref _realizedPnlRaw, value); }
            public double UnrealizedPnlRaw { get => _unrealizedPnlRaw; set => SetField(ref _unrealizedPnlRaw, value); }
            public double TotalPnlRaw { get => _totalPnlRaw; set => SetField(ref _totalPnlRaw, value); }
            public bool IsManualSelection { get => _isManualSelection; set => SetField(ref _isManualSelection, value); }
            public string SnapshotKey { get => _snapshotKey; set => SetField(ref _snapshotKey, value); }

            public void ApplyFrom(AccountGridRow source)
            {
                if (source == null)
                    return;

                DisplayName = source.DisplayName;
                AccountStatus = source.AccountStatus;
                PropFirmId = source.PropFirmId;
                AccountStatusOptions = source.AccountStatusOptions;
                PropFirmDisplay = source.PropFirmDisplay;
                PropFirmOptions = source.PropFirmOptions;
                AccountSizeSelection = source.AccountSizeSelection;
                AccountSizeOptions = source.AccountSizeOptions;
                CashValue = source.CashValue;
                MaxDrawdown = source.MaxDrawdown;
                IntratradeDrawdown = source.IntratradeDrawdown;
                MinMargin = source.MinMargin;
                BufferMargin = source.BufferMargin;
                AccountSizeRaw = source.AccountSizeRaw;
                ProfitTargetRaw = source.ProfitTargetRaw;
                DailyLossLimitRaw = source.DailyLossLimitRaw;
                EvalProfitTargetLockBalanceRaw = source.EvalProfitTargetLockBalanceRaw;
                EquityRaw = source.EquityRaw;
                NetLiqRaw = source.NetLiqRaw;
                BufferMarginRaw = source.BufferMarginRaw;
                MaxDrawdownRaw = source.MaxDrawdownRaw;
                IntratradeDrawdownRaw = source.IntratradeDrawdownRaw;
                RiskDisplay = source.RiskDisplay;
                RiskSign = source.RiskSign;
                HeadroomSafeWidth = source.HeadroomSafeWidth;
                HeadroomUsedWidth = source.HeadroomUsedWidth;
                MaxContractsRaw = source.MaxContractsRaw;
                MaxMicrosRaw = source.MaxMicrosRaw;
                MaxContracts = source.MaxContracts;
                MicroContractRootRegex = source.MicroContractRootRegex;
                MicroContractMultiplier = source.MicroContractMultiplier;
                Position = source.Position;
                EquityVsSizeSign = source.EquityVsSizeSign;
                BufferVsMaxDdSign = source.BufferVsMaxDdSign;
                IsIntraDdWarning = source.IsIntraDdWarning;
                IsNetLiqWarning = source.IsNetLiqWarning;
                RealizedPnl = source.RealizedPnl;
                UnrealizedPnl = source.UnrealizedPnl;
                TotalPnl = source.TotalPnl;
                RealizedPnlSign = source.RealizedPnlSign;
                UnrealizedPnlSign = source.UnrealizedPnlSign;
                TotalPnlSign = source.TotalPnlSign;
                RealizedPnlRaw = source.RealizedPnlRaw;
                UnrealizedPnlRaw = source.UnrealizedPnlRaw;
                TotalPnlRaw = source.TotalPnlRaw;
                IsManualSelection = source.IsManualSelection;
                SnapshotKey = source.SnapshotKey;
            }

            private void SetField<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
            {
                if (Equals(field, value))
                    return;

                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }

            private void SetListField(ref List<string> field, List<string> value, [System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
            {
                if (ReferenceEquals(field, value))
                    return;

                if (field != null && value != null && field.SequenceEqual(value, StringComparer.Ordinal))
                    return;

                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
