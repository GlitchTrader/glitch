#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;

#endregion



#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		
		private GlitchAnalyticsBridge[] cacheGlitchAnalyticsBridge;

		
		public GlitchAnalyticsBridge GlitchAnalyticsBridge(double neutralBand, bool enableBarColoring, bool publishToGlitchUi, int publishIntervalMs, bool intraBarColoring, double predictiveBoost, double flipHysteresis, bool performanceMode, bool enableOrderFlowLayer, double orderFlowBlend)
		{
			return GlitchAnalyticsBridge(Input, neutralBand, enableBarColoring, publishToGlitchUi, publishIntervalMs, intraBarColoring, predictiveBoost, flipHysteresis, performanceMode, enableOrderFlowLayer, orderFlowBlend);
		}


		
		public GlitchAnalyticsBridge GlitchAnalyticsBridge(ISeries<double> input, double neutralBand, bool enableBarColoring, bool publishToGlitchUi, int publishIntervalMs, bool intraBarColoring, double predictiveBoost, double flipHysteresis, bool performanceMode, bool enableOrderFlowLayer, double orderFlowBlend)
		{
			if (cacheGlitchAnalyticsBridge != null)
				for (int idx = 0; idx < cacheGlitchAnalyticsBridge.Length; idx++)
					if (cacheGlitchAnalyticsBridge[idx].NeutralBand == neutralBand && cacheGlitchAnalyticsBridge[idx].EnableBarColoring == enableBarColoring && cacheGlitchAnalyticsBridge[idx].PublishToGlitchUi == publishToGlitchUi && cacheGlitchAnalyticsBridge[idx].PublishIntervalMs == publishIntervalMs && cacheGlitchAnalyticsBridge[idx].IntraBarColoring == intraBarColoring && cacheGlitchAnalyticsBridge[idx].PredictiveBoost == predictiveBoost && cacheGlitchAnalyticsBridge[idx].FlipHysteresis == flipHysteresis && cacheGlitchAnalyticsBridge[idx].PerformanceMode == performanceMode && cacheGlitchAnalyticsBridge[idx].EnableOrderFlowLayer == enableOrderFlowLayer && cacheGlitchAnalyticsBridge[idx].OrderFlowBlend == orderFlowBlend && cacheGlitchAnalyticsBridge[idx].EqualsInput(input))
						return cacheGlitchAnalyticsBridge[idx];
			return CacheIndicator<GlitchAnalyticsBridge>(new GlitchAnalyticsBridge(){ NeutralBand = neutralBand, EnableBarColoring = enableBarColoring, PublishToGlitchUi = publishToGlitchUi, PublishIntervalMs = publishIntervalMs, IntraBarColoring = intraBarColoring, PredictiveBoost = predictiveBoost, FlipHysteresis = flipHysteresis, PerformanceMode = performanceMode, EnableOrderFlowLayer = enableOrderFlowLayer, OrderFlowBlend = orderFlowBlend }, input, ref cacheGlitchAnalyticsBridge);
		}

	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		
		public Indicators.GlitchAnalyticsBridge GlitchAnalyticsBridge(double neutralBand, bool enableBarColoring, bool publishToGlitchUi, int publishIntervalMs, bool intraBarColoring, double predictiveBoost, double flipHysteresis, bool performanceMode, bool enableOrderFlowLayer, double orderFlowBlend)
		{
			return indicator.GlitchAnalyticsBridge(Input, neutralBand, enableBarColoring, publishToGlitchUi, publishIntervalMs, intraBarColoring, predictiveBoost, flipHysteresis, performanceMode, enableOrderFlowLayer, orderFlowBlend);
		}


		
		public Indicators.GlitchAnalyticsBridge GlitchAnalyticsBridge(ISeries<double> input , double neutralBand, bool enableBarColoring, bool publishToGlitchUi, int publishIntervalMs, bool intraBarColoring, double predictiveBoost, double flipHysteresis, bool performanceMode, bool enableOrderFlowLayer, double orderFlowBlend)
		{
			return indicator.GlitchAnalyticsBridge(input, neutralBand, enableBarColoring, publishToGlitchUi, publishIntervalMs, intraBarColoring, predictiveBoost, flipHysteresis, performanceMode, enableOrderFlowLayer, orderFlowBlend);
		}
	
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		
		public Indicators.GlitchAnalyticsBridge GlitchAnalyticsBridge(double neutralBand, bool enableBarColoring, bool publishToGlitchUi, int publishIntervalMs, bool intraBarColoring, double predictiveBoost, double flipHysteresis, bool performanceMode, bool enableOrderFlowLayer, double orderFlowBlend)
		{
			return indicator.GlitchAnalyticsBridge(Input, neutralBand, enableBarColoring, publishToGlitchUi, publishIntervalMs, intraBarColoring, predictiveBoost, flipHysteresis, performanceMode, enableOrderFlowLayer, orderFlowBlend);
		}


		
		public Indicators.GlitchAnalyticsBridge GlitchAnalyticsBridge(ISeries<double> input , double neutralBand, bool enableBarColoring, bool publishToGlitchUi, int publishIntervalMs, bool intraBarColoring, double predictiveBoost, double flipHysteresis, bool performanceMode, bool enableOrderFlowLayer, double orderFlowBlend)
		{
			return indicator.GlitchAnalyticsBridge(input, neutralBand, enableBarColoring, publishToGlitchUi, publishIntervalMs, intraBarColoring, predictiveBoost, flipHysteresis, performanceMode, enableOrderFlowLayer, orderFlowBlend);
		}

	}
}

#endregion
