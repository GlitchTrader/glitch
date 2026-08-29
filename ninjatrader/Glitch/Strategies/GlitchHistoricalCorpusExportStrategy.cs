#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    /// <summary>
    /// No-order Strategy Analyzer host for the canonical Glitch analytics bridge.
    /// It exists only to drive deterministic historical feature export.
    /// </summary>
    public class GlitchHistoricalCorpusExportStrategy : Strategy
    {
        private GlitchAnalyticsBridge _bridge;
        private int _lastObservedExportCount;

        [NinjaScriptProperty]
        [Display(Name = "Enable Export", Order = 1, GroupName = "Export")]
        public bool EnableExport { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Export Directory", Order = 2, GroupName = "Export")]
        public string ExportDirectory { get; set; }

        [Browsable(false)]
        public int ExportedSnapshotCount
        {
            get { return _lastObservedExportCount; }
        }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "GlitchHistoricalCorpusExportStrategy";
                Description = "No-order host that exports canonical Glitch historical feature snapshots.";
                Calculate = Calculate.OnBarClose;
                IsInstantiatedOnEachOptimizationIteration = true;
                BarsRequiredToTrade = 0;
                IncludeTradeHistoryInBacktest = false;
                EnableExport = false;
                ExportDirectory = string.Empty;
            }
            else if (State == State.Configure)
            {
                AddDataSeries(BarsPeriodType.Minute, 5);
                AddDataSeries(BarsPeriodType.Minute, 15);
                AddDataSeries(BarsPeriodType.Minute, 60);
            }
            else if (State == State.DataLoaded)
            {
                _bridge = GlitchAnalyticsBridge(
                    Inputs[0],
                    0.01,
                    false,
                    false,
                    750,
                    false,
                    0.35,
                    0.03,
                    true,
                    false,
                    0.8,
                    EnableExport,
                    ExportDirectory ?? string.Empty);
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0 || _bridge == null || !EnableExport)
                return;

            _lastObservedExportCount = _bridge.HistoricalExportCount;
        }
    }
}
