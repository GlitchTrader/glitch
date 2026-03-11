#region Using declarations
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.MarketAnalyzerColumns;
using NinjaTrader.NinjaScript.Strategies;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class BotGlitchStrategyV2 : Strategy
    {
        #region Variáveis de Controle
        private bool longSignal = false;
        private bool shortSignal = false;
        private bool block1Entered = false;
        private bool block2Entered = false;
        private bool block3Entered = false;
        private bool block1Filled = false;
        private bool block2Filled = false;
        private bool block3Filled = false;
        private double entryPrice = 0;
        private bool target1Hit = false;
        private bool target2Hit = false;
        private bool target3Hit = false;
        private bool stopsSet = false;
        private bool strategyInitialized = false;
        private bool indicatorsReady = false;
        private bool indicatorsInitializationAttempted = false; // Flag para evitar chamadas repetidas
        private bool realmenteProcessouBarras = false; // Flag para rastrear se realmente processou barras
        private int totalQuantityExpected = 0;
        private int totalQuantityFilled = 0;
        private int tradeCounter = 0;
        private int tradesToday = 0;
        private DateTime lastTradeDay = DateTime.MinValue;
        private DateTime lastHeartbeat = DateTime.MinValue;
        private DateTime lastLimitLog = DateTime.MinValue;
        private DateTime lastStopSetAttempt = DateTime.MinValue;
        private DateTime lastReadyCheck = DateTime.MinValue;

        // Controle de limites de perda/ganho
        private double dailyPnLStart = 0;
        private bool dailyPnLInitialized = false;
        private DateTime lastPnLCheck = DateTime.MinValue;
        private bool limitsActive = false;

        // Controle de horário de funcionamento
        private DateTime sessionStartTime = DateTime.MinValue;
        private DateTime sessionEndTime = DateTime.MinValue;
        private bool sessionActive = false;
        #endregion

        #region Sistema de Rastreamento de Stops TICK-BY-TICK
        private double lastKnownStopPriceBlock1 = 0;
        private double lastKnownStopPriceBlock2 = 0;
        private double lastKnownStopPriceBlock3 = 0;
        private double highestPriceSinceEntry = 0;
        private double lowestPriceSinceEntry = 0;
        private bool trailingStopAtivado = false;
        private double trailingStopPrice = 0;
        private double lastProcessedTickPrice = 0;
        private DateTime lastTickTime = DateTime.MinValue;
        #endregion

        #region Variáveis do Sistema de Pesos
        private double totalScore = 0;
        private double scoreBase = 0;
        private double longThresholdDynamic = 2.0;
        private double shortThresholdDynamic = -2.0;
        #endregion

        #region Variáveis Locais para SL/TP Dinâmico
        private int dynamicBlock1StopLossTicks = 0;
        private int dynamicBlock1ProfitTargetTicks = 0;
        private int dynamicBlock2StopLossTicks = 0;
        private int dynamicBlock2ProfitTargetTicks = 0;
        private int dynamicBlock3StopLossTicks = 0;
        private int dynamicBlock3ProfitTargetTicks = 0;
        #endregion

        #region Variáveis de Direção
        private MarketPosition tradeDirection = MarketPosition.Flat;
        #endregion

        #region Variáveis de Segurança
        private bool isProcessingTrade = false;
        private DateTime lastTradeTime = DateTime.MinValue;
        private readonly object tradeLock = new object();
        private Dictionary<string, DateTime> orderSubmissionTimes = new Dictionary<string, DateTime>();
        private Dictionary<string, int> blockOrderQuantities = new Dictionary<string, int>();
        private Dictionary<string, int> blockFilledQuantities = new Dictionary<string, int>();
        private const int ORDER_TIMEOUT_SECONDS = 30;
        private const int HEARTBEAT_INTERVAL_MINUTES = 5;
        private const int STOP_SET_RETRY_INTERVAL_MS = 1000;
        private const int READY_CHECK_INTERVAL_MS = 1000;
        private const int PNL_CHECK_INTERVAL_SECONDS = 2;
        #endregion

        #region Sistema de Logging Estruturado
        private StringBuilder logBuilder = new StringBuilder();
        private DateTime lastLogFlush = DateTime.Now;
        private DateTime lastScoreLog = DateTime.MinValue;
        private const int MAX_LOG_SIZE = 100000;
        private const int LOG_RETAIN_SIZE = 5000;
        #endregion

        #region Indicadores
        private GlitchIndicator glitch;
        private EMA emaRapida;
        private EMA emaLenta;
        private EMA emaTendencia;
        private EMA emaSuperLenta;
        private RSI rsi;
        private MACD macd;
        private SMA volumeMA;
        private Stochastics stochastics;
        private DonchianChannel donchian;
        private StdDev stdDev;
        private EMA vwma;
        private SMA sma20;
        private SMA sma50;
        #endregion

        #region Variáveis para Sistema de Volume Avançado
        private VolumeProfileAnalyzer volumeProfile;
        private double obvValue = 0;
        private List<double> obvHistory;
        private double vwapValue = 0;
        private double cumulativeTPV = 0;
        private double cumulativeVolume = 0;
        private bool usarVolumeAvancado = true;
        private DateTime lastVwapReset = DateTime.MinValue;
        private const int VWAP_RESET_HOUR = 8;
        private const int VWAP_RESET_MINUTE = 30;
        #endregion

        #region Variáveis para Confirmação Multi-Timeframe
        private Series<double> closePrimary;
        private Series<double> closeSecondary;
        private bool multiTimeframeConfirmacao = false;
        private DateTime lastMultiTimeframeUpdate = DateTime.MinValue;
        #endregion

        #region Variáveis para Sistema de Breakout
        private bool breakoutDetectado = false;
        private double breakoutLevel = 0;
        private DateTime breakoutTime = DateTime.MinValue;
        private int squeezeCount = 0;
        private double previousBBWidth = 0;
        #endregion

        #region Variáveis para Machine Learning Básico
        private List<TradeStatistic> tradeStatistics;
        private Dictionary<string, IndicatorPerformance> indicatorPerformance;
        private bool mlTreinamentoAtivo = false;
        private int totalTradesColetados = 0;
        private const int MIN_TRADES_TREINAMENTO = 50;
        private const int INTERVALO_ATUALIZACAO_ML = 20;
        private const string NOME_ARQUIVO_APRENDIZADO = "BotGlitchStrategyV2_ML_Learning.dat";
        #endregion

        #region Variáveis para Sistema de Saída Inteligente
        private bool saidaParcialAtiva = false;
        private int quantidadeSaidaParcial = 0;
        private DateTime ultimaVerificacaoSaida = DateTime.MinValue;
        private bool reversaoMomentumDetectada = false;
        private List<double> momentumHistory;
        #endregion

        #region Variáveis para Análise de Correlação
        private Dictionary<int, TradeRecord> tradeRecords;
        private DateTime lastCorrelationAnalysis = DateTime.MinValue;
        private const int INTERVALO_ANALISE_CORRELACAO = 30;
        #endregion

        #region Variáveis para Sistema de Filtros de Qualidade V4
        private int scoreQualidadeTrade = 0;
        private bool ultimoTradeBloqueadoPorFiltro = false;
        private string motivoBloqueioUltimoTrade = "";
        #endregion

        #region Classe Volume Profile Analyzer
        public class VolumeProfileAnalyzer
        {
            private Dictionary<double, double> volumePorPreco;
            private List<Tuple<double, double>> priceVolumeList;
            private int lookbackPeriod;
            private int maxPricePoints;
            private double maxVolume = 0;
            private double maxVolumePrice = 0;
            private DateTime lastCleanup = DateTime.Now;

            public VolumeProfileAnalyzer(int period = 20)
            {
                volumePorPreco = new Dictionary<double, double>();
                priceVolumeList = new List<Tuple<double, double>>();
                lookbackPeriod = period;
                maxPricePoints = period * 10;
            }

            public void Update(double high, double low, double volume, DateTime timestamp)
            {
                if (high == low) return;

                double range = high - low;
                double incremento = range / 3;

                for (int i = 0; i <= 3; i++)
                {
                    double priceLevel = Math.Round(low + (incremento * i), 4);
                    double volumeAllocation = volume / 4;

                    var node = new Tuple<double, double>(priceLevel, volumeAllocation);
                    priceVolumeList.Add(node);

                    if (volumePorPreco.ContainsKey(priceLevel))
                        volumePorPreco[priceLevel] += volumeAllocation;
                    else
                        volumePorPreco[priceLevel] = volumeAllocation;

                    if (volumePorPreco[priceLevel] > maxVolume)
                    {
                        maxVolume = volumePorPreco[priceLevel];
                        maxVolumePrice = priceLevel;
                    }
                }

                if (priceVolumeList.Count > maxPricePoints ||
                    (DateTime.Now - lastCleanup).TotalMinutes >= 5)
                {
                    CleanupOldData();
                    lastCleanup = DateTime.Now;
                }
            }

            private void CleanupOldData()
            {
                int toRemove = priceVolumeList.Count - maxPricePoints;
                if (toRemove > 0)
                {
                    for (int i = 0; i < toRemove; i++)
                    {
                        var oldest = priceVolumeList[i];
                        double oldPrice = oldest.Item1;
                        double oldVolume = oldest.Item2;

                        if (volumePorPreco.ContainsKey(oldPrice))
                        {
                            volumePorPreco[oldPrice] -= oldVolume;
                            if (volumePorPreco[oldPrice] <= 0)
                            {
                                volumePorPreco.Remove(oldPrice);
                                if (Math.Abs(oldPrice - maxVolumePrice) < 0.0001)
                                    RecalculateMaxVolume();
                            }
                        }
                    }
                    priceVolumeList.RemoveRange(0, toRemove);
                }
            }

            private void RecalculateMaxVolume()
            {
                maxVolume = 0;
                maxVolumePrice = 0;

                foreach (var kvp in volumePorPreco)
                {
                    if (kvp.Value > maxVolume)
                    {
                        maxVolume = kvp.Value;
                        maxVolumePrice = kvp.Key;
                    }
                }
            }

            public double GetVolumeAtPrice(double price)
            {
                double priceRounded = Math.Round(price, 4);
                return volumePorPreco.ContainsKey(priceRounded) ? volumePorPreco[priceRounded] : 0;
            }

            public double GetHighVolumeNode()
            {
                return maxVolumePrice > 0 ? maxVolumePrice : 0;
            }

            public double GetLowVolumeNode()
            {
                if (volumePorPreco.Count == 0) return 0;
                return volumePorPreco.OrderBy(kvp => kvp.Value).First().Key;
            }

            public Dictionary<double, double> GetVolumeProfile()
            {
                return new Dictionary<double, double>(volumePorPreco);
            }

            public double GetVolumePercentile(double percentile)
            {
                if (volumePorPreco.Count == 0) return 0;

                var volumes = volumePorPreco.Values.ToList();
                volumes.Sort();

                int index = (int)Math.Ceiling(percentile * volumes.Count / 100) - 1;
                index = Math.Max(0, Math.Min(volumes.Count - 1, index));

                return volumes[index];
            }
        }
        #endregion

        #region Classes para Machine Learning
        public class TradeStatistic
        {
            public DateTime Timestamp { get; set; }
            public double ScoreTendencia { get; set; }
            public double ScoreMomentum { get; set; }
            public double ScoreVolume { get; set; }
            public double ScoreVolatilidade { get; set; }
            public double ScoreBreakout { get; set; }
            public double TotalScore { get; set; }
            public double PnL { get; set; }
            public MarketPosition Direction { get; set; }
            public bool IsWinner { get; set; }
        }

        public class IndicatorPerformance
        {
            public string IndicatorName { get; set; }
            public int TotalTrades { get; set; }
            public int WinningTrades { get; set; }
            public double TotalContribution { get; set; }
            public double CurrentWeight { get; set; }
            public double AdjustedWeight { get; set; }
            public DateTime LastUpdate { get; set; }

            public double WinRate => TotalTrades > 0 ? (double)WinningTrades / TotalTrades : 0;
            public double AverageContribution => TotalTrades > 0 ? TotalContribution / TotalTrades : 0;
        }

        public class TradeRecord
        {
            public int TradeId { get; set; }
            public DateTime EntryTime { get; set; }
            public DateTime ExitTime { get; set; }
            public double EntryPrice { get; set; }
            public double ExitPrice { get; set; }
            public MarketPosition Direction { get; set; }
            public double PnL { get; set; }
            public Dictionary<string, double> IndicatorValues { get; set; }
            public Dictionary<string, double> ScoreContributions { get; set; }
        }
        #endregion

        #region Métodos Auxiliares para Cálculos
        private int CalculateATRTicks()
        {
            try
            {
                // CORREÇÃO: Verificar se glitch tem dados antes de usar
                if (glitch == null || glitch.Count == 0 || !IsIndicatorValid(glitch, 0) || TickSize <= 0)
                    return Block1StopLossTicks;

                double bandwidth = glitch.BandWidth[0];
                double sigma = bandwidth / 2.0;
                
                if (sigma <= 0) return Block1StopLossTicks;

                int atrTicks = (int)Math.Round(sigma / TickSize);
                return Math.Max(5, atrTicks);
            }
            catch (Exception ex)
            {
                LogError("CalculateATRTicks", ex);
                return Block1StopLossTicks;
            }
        }
        
        /// <summary>
        /// Obtém valor do VWAP do GlitchIndicator
        /// </summary>
        private double GetGlitchVWAP()
        {
            try
            {
                // CORREÇÃO: Verificar se glitch tem dados antes de usar
                if (glitch == null || glitch.Count == 0 || !IsIndicatorValid(glitch, 0))
                {
                    // Fallback: usar Close[0] como VWAP aproximado
                    return Close[0];
                }
                
                // Tentar usar Values[2] se disponível (VWAP)
                try
                {
                    return glitch.Values[2][0];
                }
                catch
                {
                    return Close[0];
                }
            }
            catch (Exception ex)
            {
                LogError("GetGlitchVWAP", ex);
                return Close[0];
            }
        }
        
        /// <summary>
        /// Obtém valor do ATR usando Bandwidth do GlitchIndicator
        /// </summary>
        private double GetGlitchATR()
        {
            try
            {
                // CORREÇÃO: Verificar se glitch tem dados antes de usar
                if (glitch == null || glitch.Count == 0 || !IsIndicatorValid(glitch, 0))
                {
                    // Fallback: usar um valor padrão baseado no preço atual
                    // Aproximação: 0.5% do preço atual como ATR
                    return Close[0] * 0.005;
                }
                
                // Tentar usar BandWidth se disponível
                try
                {
                    return glitch.BandWidth[0] / 2.0;
                }
                catch
                {
                    // Fallback se BandWidth não estiver disponível
                    return Close[0] * 0.005;
                }
            }
            catch (Exception ex)
            {
                LogError("GetGlitchATR", ex);
                // Fallback em caso de erro
                return Close[0] * 0.005;
            }
        }
        
        /// <summary>
        /// Verifica se preço está acima da banda superior
        /// </summary>
        private bool IsAboveBollingerUpper()
        {
            try
            {
                if (glitch == null || !IsIndicatorValid(glitch, 0))
                    return false;
                return glitch.ZScore[0] > 2.0;
            }
            catch (Exception ex)
            {
                LogError("IsAboveBollingerUpper", ex);
                return false;
            }
        }
        
        /// <summary>
        /// Verifica se preço está abaixo da banda inferior
        /// </summary>
        private bool IsBelowBollingerLower()
        {
            try
            {
                if (glitch == null || !IsIndicatorValid(glitch, 0))
                    return false;
                return glitch.ZScore[0] < -2.0;
            }
            catch (Exception ex)
            {
                LogError("IsBelowBollingerLower", ex);
                return false;
            }
        }
        
        /// <summary>
        /// Obtém valor do ADX usando Regime e Confidence do GlitchIndicator
        /// </summary>
        private double GetGlitchADX()
        {
            try
            {
                // CORREÇÃO: Verificar se glitch tem dados antes de usar
                if (glitch == null || glitch.Count == 0 || !IsIndicatorValid(glitch, 0))
                    return 0;
                int regime = (int)glitch.Regime[0];
                double confidence = glitch.ConfidenceSmoothed[0];
                
                if (regime == 1)
                    return confidence;
                else if (regime == -1)
                    return confidence * 0.5;
                else
                    return confidence * 0.7;
            }
            catch (Exception ex)
            {
                LogError("GetGlitchADX", ex);
                return 0;
            }
        }

        private double GetVWAPValue()
        {
            return GetVWAPValue(0);
        }

        private double GetVWAPValue(int barsAgo)
        {
            try
            {
                // CORREÇÃO: Verificar se glitch tem dados antes de usar
                if (glitch == null || glitch.Count == 0 || !IsIndicatorValid(glitch, barsAgo))
                    return Close[barsAgo]; // Fallback
                return glitch.Values[2][barsAgo]; // Plot VWAP (índice 2)
            }
            catch (Exception ex)
            {
                LogError("GetVWAPValue", ex);
                return Close[barsAgo];
            }
        }

        // Método auxiliar para obter valor do Donchian Upper
        private double GetDonchianUpperValue()
        {
            try
            {
                if (donchian == null || !IsValidDataPoint(0))
                    return High[0];

                return donchian.Upper[0];
            }
            catch (Exception ex)
            {
                LogError("GetDonchianUpperValue", ex);
                return High[0];
            }
        }

        // Método auxiliar para obter valor do Donchian Lower
        private double GetDonchianLowerValue()
        {
            try
            {
                if (donchian == null || !IsValidDataPoint(0))
                    return Low[0];

                return donchian.Lower[0];
            }
            catch (Exception ex)
            {
                LogError("GetDonchianLowerValue", ex);
                return Low[0];
            }
        }
        #endregion

        #region Métodos do Estado
        protected override void OnStateChange()
        {
            try
            {
                if (State == State.SetDefaults)
                {
                    LogInfo("Estado", "📋 State.SetDefaults - Chamando SetDefaults()");
                    SetDefaults();
                }
                else if (State == State.Configure)
                {
                    LogInfo("Estado", 
                        $"⚙️ State.Configure - Chamando ConfigureStrategy()\n" +
                        $"glitch antes de ConfigureStrategy: {glitch != null}");
                    ConfigureStrategy();
                    LogInfo("Estado", 
                        $"✅ ConfigureStrategy() concluído\n" +
                        $"glitch após ConfigureStrategy: {glitch != null}");
                }
                else if (State == State.DataLoaded)
                {
                    LogInfo("Estado", 
                        $"📊 State.DataLoaded - Chamando InitializeIndicators()\n" +
                        $"glitch antes de InitializeIndicators: {glitch != null}");
                    InitializeIndicators();
                    LogInfo("Estado", 
                        $"✅ InitializeIndicators() concluído\n" +
                        $"glitch após InitializeIndicators: {glitch != null}");
                }
                else if (State == State.Historical)
                {
                    LogInfo("Estado", "Modo histórico ativado - Processando dados históricos");
                    // Em histórico, não inicializamos indicadores aqui
                    // Eles serão inicializados no OnBarUpdate quando houver barras suficientes
                    // AddChartIndicator não pode ser chamado neste estado
                }
                else if (State == State.Realtime)
                {
                    LogInfo("Estado", "✅ Modo REAL-TIME ativado - Pronto para trading");
                    InitializeRealtime();
                }
                else if (State == State.Terminated)
                {
                    LogInfo("🔴 TERMINAÇÃO", 
                        $"Estratégia sendo finalizada - CurrentBar: {CurrentBar}, Bars.Count: {Bars?.Count ?? 0}\n" +
                        $"strategyInitialized: {strategyInitialized}, indicatorsReady: {indicatorsReady}\n" +
                        $"realmenteProcessouBarras: {realmenteProcessouBarras}");
                    TerminateStrategy();
                }
            }
            catch (Exception ex)
            {
                LogError("OnStateChange", ex);
            }
        }

        private void SetDefaults()
        {
            try
            {
                Description = @"BotGlitchStrategyV2 - Sistema multifatorial avançado integrado com GlitchIndicator (VWAP session-anchored, regime detection, confidence scoring) - Versão com suporte completo a backtest";
                Name = "BotGlitchStrategyV2";

                // CONFIGURAÇÃO TICK-BY-TICK
                Calculate = Calculate.OnEachTick;

                // Configurações de execução
                EntriesPerDirection = 3;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = false;
                IsFillLimitOnTouch = true;
                MaximumBarsLookBack = MaximumBarsLookBack.Infinite;
                OrderFillResolution = OrderFillResolution.High;
                Slippage = SlippageTicks; // Usar propriedade configurável
                StartBehavior = StartBehavior.WaitUntilFlat;
                TimeInForce = TimeInForce.Gtc;
                TraceOrders = true;
                RealtimeErrorHandling = RealtimeErrorHandling.StopCancelCloseIgnoreRejects;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade = 15;        // Mais dados necessários para 1min
                IsInstantiatedOnEachOptimizationIteration = true;
                IsUnmanaged = false;

                // ===== PARÂMETROS DOS BLOCOS =====
                Block1Quantity = 2;
                Block2Quantity = 1;
                Block3Quantity = 1;

                // Stops/Targets ajustados para timeframe 1min
                Block1StopLossTicks = 35;         // $17.50 risco
                Block1ProfitTargetTicks = 45;    // $22.50 alvo (1.29:1)
                Block2StopLossTicks = 35;         // $17.50 risco
                Block2ProfitTargetTicks = 70;     // $35 alvo (2:1)
                Block3StopLossTicks = 35;         // $17.50 risco
                Block3ProfitTargetTicks = 150;    // $75 alvo (4.29:1)

                // ===== SISTEMA DE GERENCIAMENTO =====
                SLBufferTicks = 2;
                MNQMarginPerContract = 1500;

                // ===== LIMITES DE PERDA E GANHO =====
                AtivarLimitesPnL = true;
                LimitePerdaDiaria = -500;
                LimiteGanhoDiario = 500;
                ResetLimitesPorDia = true;

                // ===== HORÁRIO DE FUNCIONAMENTO =====
                AtivarHorarioFuncionamento = false;
                HoraInicioFuncionamento = 900;
                HoraFimFuncionamento = 1600;
                FecharPosicoesNoFimHorario = true;
                PermitirSessaoMultidia = true;

                // ===== SISTEMA DE PESOS - REBALANCEADO (30/30/20/20) =====
                AtivarSistemaTendencia = true;
                AtivarMomentum = true;
                AtivarVolume = true;
                AtivarVolatilidade = true;

                SlippageTicks = 1; // Slippage padrão: 1 tick (conforme diretrizes)
                
                // ===== AJUSTES OTIMIZADOS PARA TIMEFRAME 1 MINUTO =====
                // Indicadores ajustados para timeframe 1min (preset GlitchIndicator otimizado)
                EmaRapidaPeriod = 8;      // Otimizado para 1min
                EmaLentaPeriod = 15;      // Otimizado para 1min
                EmaTendenciaPeriod = 30;  // Otimizado para 1min
                EmaSuperLentaPeriod = 60; // Otimizado para 1min
                PesoTendencia = 1.5;      // 30%

                RsiPeriod = 10;           // Otimizado para 1min
                RsiSobrevendido = 25;     // Sensível para 1min
                RsiSobrecomprado = 75;    // Sensível para 1min
                MacdFast = 10;            // Otimizado para 1min
                MacdSlow = 21;           // Otimizado para 1min
                MacdSignal = 7;           // Otimizado para 1min
                PesoMomentum = 1.5;       // 30%

                VolumeMALength = 12;      // Otimizado para 1min
                VolumeThreshold = 1.3;    // Sensível para 1min
                PesoVolume = 1.0;         // 20%

                UsarVolumeAvancado = true;
                VolumeProfilePeriod = 12; // Otimizado para 1min

                AtrLength = 10;           // Mantido (não usado, substituído por GlitchIndicator)
                BollingerLength = 12;     // Mantido (não usado, substituído por GlitchIndicator)
                BollingerStdDev = 2;
                PesoVolatilidade = 1.0;    // 20%

                // ===== THRESHOLDS DINÂMICOS =====
                // CORREÇÃO CRÍTICA: DESABILITADO thresholds dinâmicos - estavam gerando valores muito altos (2.0+)
                // Usando apenas valores base (0.8/-0.8) que são muito mais permissivos
                UsarThresholdsDinamicos = false; // DESABILITADO - usar apenas LongThreshold/ShortThreshold base
                SensibilidadeThreshold = 5; // Mantido para caso seja reativado no futuro
                MaxScoreTeorico = 5.0;

                // ===== SISTEMA DE BREAKOUT =====
                AtivarSistemaBreakout = true;
                PesoBreakout = 0.8;
                BreakoutLookbackPeriod = 15; // Otimizado para 1min
                SqueezeThreshold = 0.15;     // Sensível para 1min
                MinBreakoutVolumeMultiplier = 1.3; // Sensível para 1min

                // ===== SISTEMA DE VOLUME APRIMORADO =====
                UsarVolumeClimax = true;
                VolumePercentilClimax = 90;
                UsarVWAPBands = true;
                VWAPBandStdDev = 1.0;
                UsarVolumeWeightedMACD = true;

                // ===== CONFIRMAÇÃO MULTI-TIMEFRAME =====
                // CORREÇÃO: Desativado para permitir mais trades em 1min (~50 trades/dia)
                // A confirmação multi-timeframe estava bloqueando muitos trades
                UsarMultiTimeframe = false;   // DESATIVADO para permitir mais trades
                SecondaryTimeframeValue = 5; // Timeframe secundário: 5 minutos (não usado se UsarMultiTimeframe=false)
                SecondaryTimeframeType = BarsPeriodType.Minute;
                RequerirConfirmacaoMultiTimeframe = false; // Confirmação não obrigatória - permite mais trades

                // ===== SISTEMA DE SL/TP DINÂMICO =====
                // CORREÇÃO CRÍTICA LOG5: Ajustado para melhor R/R e qualidade de trades
                // Análise mostrou R/R de 1.02:1 muito baixo e TP1 muito pequeno (23 ticks vs SL 49 ticks)
                UsarSistemaATR = true;
                SlAtrMultiplier = 1.5;          // Aumentado de 1.4 para 1.5 - SL = ~53 ticks (melhor proteção)
                TpAtrMultiplierBlock1 = 1.0;   // Aumentado de 0.65 para 1.0 - TP1 = ~35 ticks (R/R melhor)
                TpAtrMultiplierBlock2 = 1.5;   // Aumentado de 1.4 para 1.5 - TP2 = ~53 ticks (break-even)
                TpAtrMultiplierBlock3 = 3.5;   // Aumentado de 3.0 para 3.5 - TP3 = ~123 ticks (R/R excelente)
                MinimoRewardRisk = 0.8; // Aumentado de 0.5 para 0.8 - garante R/R mínimo adequado e reduz over-trading

                // CORREÇÃO CRÍTICA LOG5: Thresholds aumentados para melhor qualidade de trades
                // Análise mostrou muitos trades de baixa qualidade (Qualidade Score 2-3/10)
                // Thresholds mais altos focam em sinais mais fortes e reduzem over-trading
                LongThreshold = 0.8;            // Aumentado de 0.5 para 0.8 - foca em sinais mais fortes
                ShortThreshold = -0.8;          // Aumentado de -0.5 para -0.8 - foca em sinais mais fortes
                AtivarShortTrading = true;

                // ===== FILTRO ADX =====
                UsarFiltroADX = true;
                ADXPeriod = 10;             // Mantido (não usado diretamente, via GlitchIndicator)
                ADXMaximo = 25;             // Mantido (convertido para Confidence no GlitchIndicator)

                // ===== SISTEMA DE TRAILING STOP TICK-BY-TICK =====
                // Ajustado para timeframe 1min
                UsarTrailingStop = true;
                TrailingStopActivationTicks = 7;    // Otimizado para 1min
                TrailingStopDistanceTicks = 25;     // Otimizado para 1min
                MinTickMovementForTrailing = 1;
                EnableAggressiveTrailing = true;

                // ===== MACHINE LEARNING BÁSICO =====
                AtivarMLBasico = true;
                AjusteAutomaticoPesos = true;
                MaxAjustePeso = 0.2;
                IntervaloAtualizacaoML = 20;

                // ===== SISTEMA DE SAÍDA INTELIGENTE =====
                UsarSaidaInteligente = true;
                DetectarReversaoMomentum = true;
                SaidaParcialAtiva = false;
                PercentualSaidaParcial = 0.5;
                VolumeExtremoPercentil = 95;

                // ===== ANÁLISE DE CORRELAÇÃO =====
                AtivarAnaliseCorrelacao = true;
                ColetarDadosPerformance = true;
                IntervaloAnaliseCorrelacao = 30;

                // ===== PARÂMETROS DE SEGURANÇA =====
                // CORREÇÃO CRÍTICA LOG5: Limite reduzido para evitar over-trading
                // Análise mostrou limite de 50 trades/dia sendo atingido rapidamente, causando over-trading
                MaxTradesPerDay = 35;            // Reduzido de 50 para 35 - foca em trades melhores e reduz over-trading
                // CORREÇÃO CRÍTICA: Reduzido de 1 para 0 minutos - elimina bloqueio de tempo mínimo entre trades
                // Muitos trades estavam sendo bloqueados por MinTimePassed: False
                MinTimeBetweenTrades = 0;        // Reduzido de 1 para 0 minutos - sem tempo mínimo para 1min

                // ===== LOGGING =====
                EnableStructuredLogging = true;
                LogFlushIntervalMinutes = 5;
                EnableTickLogging = false;
                EnableReadyLogging = true;
                EnableScoreLogging = true;
                ScoreLogIntervalSeconds = 30;
                EnableMLStatsLogging = true;
            }
            catch (Exception ex)
            {
                LogError("SetDefaults", ex);
            }
        }

        private void ConfigureStrategy()
        {
            LogInfo("Configuração", 
                $"🔧 ConfigureStrategy() chamado - State: {State}\n" +
                $"glitch antes de ResetStrategyState: {glitch != null}");
            
            ResetStrategyState();
            
            LogInfo("Configuração", 
                $"✅ ResetStrategyState() concluído\n" +
                $"glitch após ResetStrategyState: {glitch != null}");
            
            // ===== GLITCH INDICATOR - DEVE SER CRIADO E ADICIONADO EM STATE.CONFIGURE =====
            // AddChartIndicator() só pode ser chamado em State.Configure ou State.SetDefaults
            // IMPORTANTE: Criar o indicador ANTES de chamar AddChartIndicator()
            // para garantir que a referência seja mantida corretamente
            LogInfo("Configuração", "📊 Criando GlitchIndicator em State.Configure...");
            
            glitch = new GlitchIndicator()
            {
                EnableVWAP = true,
                PriceSourceMode = 2,
                PlotBand1 = true,
                PlotBand2 = true,
                SlopeLookback = 25,
                BandwidthLookback = 25,
                ShowStateLabel = false,
                VisualMode = 0,
                ShowVisualMarkers = false,
                MarkerCooldownBars = 0,
                UseTimeCooldown = false,
                CooldownMinutes = 0,
                ConfidenceSmoothingPeriod = 12,
                VisualConfidenceThreshold = 67,
                VisualBiasThreshold = 0.20,
                LabelOffsetTicks = 40,
                UseLeaderLine = true,
                UseArrowMarker = true,
                LabelPlacement = 0,
                TrendSlopeThreshold = 0.13,
                MeanRevertZThreshold = 1.10,
                MinSigmaTicks = 4.0,
                ZScoreClamp = 6.0,
                RegimeDecisionThreshold = 0.28,
                RegimeStickyBars = 3,
                RegimeSwitchMargin = 0.10,
                ChopBiasCap = 0.12,
                ADXTrendThreshold = 23.0,
                ADXChopThreshold = 15.0,
                ChopEntryZ = 1.0,
                BandwidthSpikeThreshold = 0.5,
                ZScoreJumpThreshold = 1.5,
                EmaFastPeriod = 8,
                EmaSlowPeriod = 21,
                EnableCsvLogging = false,
                CsvFileName = ""
            };
            
            // Adicionar ao gráfico - isso garante que o indicador passe pelo ciclo de vida completo
            AddChartIndicator(glitch);
            
            // Verificar se glitch foi criado corretamente
            if (glitch == null)
            {
                LogError("Configuração", "❌ ERRO CRÍTICO: Falha ao criar GlitchIndicator em ConfigureStrategy()!");
                throw new Exception("Falha ao criar GlitchIndicator - a estratégia não pode prosseguir sem ele.");
            }
            
            LogInfo("Configuração", 
                $"✅ GlitchIndicator criado e adicionado ao gráfico em State.Configure\n" +
                $"glitch != null: {glitch != null}\n" +
                $"O indicador passará pelo ciclo de vida completo do NinjaTrader (SetDefaults -> Configure -> DataLoaded -> Historical/Realtime)");
        }

        // CORREÇÃO: Inicialização do VWAP correta
        private void InitializeIndicators()
        {
            try
            {
                // Verificar se indicadores realmente foram criados (não apenas se tentou inicializar)
                bool indicadoresNaoCriados = (glitch == null || emaRapida == null || emaLenta == null || 
                                              emaTendencia == null || emaSuperLenta == null || rsi == null || macd == null);
                
                // Evitar chamadas repetidas desnecessárias - mas apenas se indicadores foram realmente criados
                if (indicatorsInitializationAttempted && strategyInitialized && !indicadoresNaoCriados)
                {
                    return; // Já foi inicializado com sucesso e indicadores existem
                }

                // Se já tentou inicializar mas indicadores não foram criados, forçar nova tentativa
                if (indicatorsInitializationAttempted && indicadoresNaoCriados)
                {
                    LogWarning("Inicialização", 
                        $"⚠️ Indicadores não foram criados na tentativa anterior - forçando nova inicialização\n" +
                        $"glitch: {glitch != null}, emaRapida: {emaRapida != null}, emaLenta: {emaLenta != null}\n" +
                        $"CurrentBar: {CurrentBar}, State: {State}");
                    // Resetar flag para permitir nova tentativa completa
                    indicatorsInitializationAttempted = false;
                    // Continuar para criar os indicadores abaixo
                }
                // Se já tentou inicializar mas não está pronto (indicadores existem mas não estão prontos)
                else if (indicatorsInitializationAttempted && !strategyInitialized && !indicadoresNaoCriados)
                {
                    // Verificar apenas a cada 100 barras, SEM LOGS, para evitar spam
                    if (CurrentBar % 100 != 0)
                    {
                        return;
                    }
                    // Se chegou aqui, está na verificação periódica - fazer check silencioso
                    indicatorsReady = CheckIndicatorsReady();
                    if (indicatorsReady)
                    {
                        strategyInitialized = true;
                        LogInfo("✅ INDICADORES PRONTOS", 
                            $"Indicadores ficaram prontos em verificação periódica - CurrentBar: {CurrentBar}");
                    }
                    return; // Não logar novamente
                }

                // Primeira tentativa de inicialização ou tentativa forçada - marcar como tentada
                indicatorsInitializationAttempted = true;

                LogInfo("Inicialização",
                    $"State: {State}, CurrentBar: {CurrentBar}, Bars Count: {Bars?.Count ?? 0}");

                if (Bars == null || Bars.Instrument == null)
                {
                    LogWarning("Inicialização", "Objeto Bars não está disponível");
                    return;
                }

                // CORREÇÃO: Não retornar aqui - os indicadores podem ser criados mesmo com CurrentBar baixo
                // Eles só precisarão de dados suficientes depois para calcular valores
                // A verificação de barras suficientes será feita em CheckIndicatorsReady()
                int minBarsRequired = Math.Max(BarsRequiredToTrade,
                    Math.Max(EmaSuperLentaPeriod, Math.Max(EmaTendenciaPeriod,
                    Math.Max(EmaLentaPeriod, Math.Max(EmaRapidaPeriod, RsiPeriod)))));

                if (Bars.Count < minBarsRequired || CurrentBar < minBarsRequired)
                {
                    LogInfo("Aguardando",
                        $"⏳ Aguardando dados: Bars={Bars.Count}, CurrentBar={CurrentBar}, Necessário={minBarsRequired}\n" +
                        $"Mas continuando para criar indicadores - eles só precisarão de dados depois para calcular valores.");
                    // NÃO retornar aqui - continuar criando os indicadores
                }

                // CORREÇÃO: Não retornar aqui se CurrentBar é baixo - os indicadores podem ser criados mesmo assim
                // Eles só precisarão de dados válidos depois para calcular valores
                if (!IsValidDataPoint(0) && CurrentBar >= 0)
                {
                    LogWarning("Inicialização", 
                        $"Dados de preço não disponíveis para a barra atual (CurrentBar={CurrentBar})\n" +
                        $"Mas continuando para criar indicadores - eles só precisarão de dados válidos depois.");
                    // NÃO retornar aqui se CurrentBar é negativo ou baixo - continuar criando os indicadores
                }
                else if (CurrentBar < 0)
                {
                    LogInfo("Inicialização", 
                        $"CurrentBar={CurrentBar} (negativo) - continuando para criar indicadores mesmo assim");
                }

                LogInfo("Inicialização",
                    $"⚡ Iniciando inicialização BotGlitchStrategyV2...\n" +
                    $"Instrumento: {Instrument.FullName}\n" +
                    $"Timeframe: {BarsPeriod.Value}{BarsPeriod.BarsPeriodType}\n" +
                    $"Barras disponíveis: {Bars.Count}\n" +
                    $"CurrentBar: {CurrentBar}\n" +
                    $"Mínimo necessário: {minBarsRequired}");

                // ===== GLITCH INDICATOR - VERIFICAR SE JÁ FOI CRIADO EM CONFIGURE =====
                // O indicador já foi criado e adicionado em ConfigureStrategy() em State.Configure
                // Aqui apenas verificamos se está disponível e fazemos diagnóstico
                if (glitch == null)
                {
                    LogError("Inicialização", 
                        "❌ ERRO CRÍTICO: GlitchIndicator é null em InitializeIndicators()!\n" +
                        "O indicador deveria ter sido criado em ConfigureStrategy() durante State.Configure.\n" +
                        $"State atual: {State}, CurrentBar: {CurrentBar}, Bars.Count: {Bars?.Count ?? 0}\n" +
                        "Verifique se ConfigureStrategy() foi executado corretamente.\n" +
                        "A estratégia não pode prosseguir sem o GlitchIndicator.");
                    return;
                }
                
                // Diagnóstico do GlitchIndicator
                LogInfo("Inicialização", 
                    $"🔍 GlitchIndicator Status:\n" +
                    $"glitch != null: {glitch != null}\n" +
                    $"glitch.Count: {glitch.Count}\n" +
                    $"glitch.BarsRequiredToPlot: {glitch.BarsRequiredToPlot}\n" +
                    $"glitch.Instrument: {glitch.Instrument?.FullName ?? "null"}\n" +
                    $"CurrentBar: {CurrentBar}, State: {State}\n" +
                    $"O indicador foi criado em ConfigureStrategy() e deve estar sendo inicializado pelo NinjaTrader.\n" +
                    $"Se Instrument é null, o indicador ainda não passou por State.DataLoaded do próprio indicador.");

                LogInfo("Inicialização", "📊 Criando indicadores padrão do NinjaTrader...");
                
                emaRapida = EMA(Close, EmaRapidaPeriod);
                emaLenta = EMA(Close, EmaLentaPeriod);
                emaTendencia = EMA(Close, EmaTendenciaPeriod);
                emaSuperLenta = EMA(Close, EmaSuperLentaPeriod);
                rsi = RSI(Close, RsiPeriod, 1);
                macd = MACD(Close, MacdFast, MacdSlow, MacdSignal);
                stochastics = Stochastics(BarsArray[0], 10, 3, 3); // Otimizado para 1min
                volumeMA = SMA(Volume, VolumeMALength);
                
                LogInfo("Inicialização", 
                    $"✅ Indicadores padrão criados:\n" +
                    $"emaRapida: {emaRapida != null}, emaLenta: {emaLenta != null}\n" +
                    $"emaTendencia: {emaTendencia != null}, emaSuperLenta: {emaSuperLenta != null}\n" +
                    $"rsi: {rsi != null}, macd: {macd != null}\n" +
                    $"stochastics: {stochastics != null}, volumeMA: {volumeMA != null}");

                donchian = DonchianChannel(BreakoutLookbackPeriod);
                stdDev = StdDev(Close, 20);

                // V4: Indicadores para análise multi-timeframe e filtros de qualidade
                sma20 = SMA(Close, 20);
                sma50 = SMA(Close, 50);

                // Volume Weighted Moving Average - CORREÇÃO: usar abordagem simplificada
                try
                {
                    // Criar série volume-weighted manualmente
                    vwma = EMA(Close, 20); // Usar EMA normal como fallback
                }
                catch (Exception ex)
                {
                    LogError("VWMA Initialization", ex);
                    vwma = EMA(Close, 20); // Fallback para EMA normal
                }

                // ===== VERIFICAR INICIALIZAÇÃO =====
                bool allIndicatorsInitialized = true;
                string failedIndicators = "";

                // Verificar GlitchIndicator primeiro
                if (glitch == null) { allIndicatorsInitialized = false; failedIndicators += "GlitchIndicator, "; }
                
                if (emaRapida == null) { allIndicatorsInitialized = false; failedIndicators += "EMA Rápida, "; }
                if (emaLenta == null) { allIndicatorsInitialized = false; failedIndicators += "EMA Lenta, "; }
                if (emaTendencia == null) { allIndicatorsInitialized = false; failedIndicators += "EMA Tendência, "; }
                if (emaSuperLenta == null) { allIndicatorsInitialized = false; failedIndicators += "EMA Super Lenta, "; }
                if (rsi == null) { allIndicatorsInitialized = false; failedIndicators += "RSI, "; }
                if (macd == null) { allIndicatorsInitialized = false; failedIndicators += "MACD, "; }
                if (volumeMA == null) { allIndicatorsInitialized = false; failedIndicators += "Volume MA, "; }

                if (!allIndicatorsInitialized)
                {
                    throw new Exception($"Falha ao inicializar indicadores: {failedIndicators.TrimEnd(',', ' ')}");
                }

                // ===== SISTEMA DE VOLUME AVANÇADO =====
                volumeProfile = new VolumeProfileAnalyzer(VolumeProfilePeriod);
                usarVolumeAvancado = UsarVolumeAvancado;
                obvHistory = new List<double>();

                // ===== INICIALIZAR SISTEMA DE ML =====
                // Só inicializar se ainda não foi inicializado
                if (tradeStatistics == null)
                {
                    tradeStatistics = new List<TradeStatistic>();
                }
                if (indicatorPerformance == null || indicatorPerformance.Count == 0)
                {
                    indicatorPerformance = new Dictionary<string, IndicatorPerformance>
                    {
                        { "Tendencia", new IndicatorPerformance { IndicatorName = "Tendencia", CurrentWeight = PesoTendencia } },
                        { "Momentum", new IndicatorPerformance { IndicatorName = "Momentum", CurrentWeight = PesoMomentum } },
                        { "Volume", new IndicatorPerformance { IndicatorName = "Volume", CurrentWeight = PesoVolume } },
                        { "Volatilidade", new IndicatorPerformance { IndicatorName = "Volatilidade", CurrentWeight = PesoVolatilidade } },
                        { "Breakout", new IndicatorPerformance { IndicatorName = "Breakout", CurrentWeight = PesoBreakout } }
                    };
                }
                // NOTA: CarregarAprendizadoDeArquivo() foi movido para State.DataLoaded para ser executado apenas uma vez

                // ===== INICIALIZAR ANÁLISE DE CORRELAÇÃO =====
                tradeRecords = new Dictionary<int, TradeRecord>();
                momentumHistory = new List<double>();

                // ===== VERIFICAR SE INDICADORES ESTÃO PRONTOS =====
                indicatorsReady = CheckIndicatorsReady();

                if (indicatorsReady)
                {
                    LogInfo("✅ INDICADORES PRONTOS V4",
                        $"⚡ INICIALIZAÇÃO COMPLETA EM {DateTime.Now:HH:mm:ss.fff}!\n" +
                        $"=========================================\n" +
                        $"• EMA Rápida ({EmaRapidaPeriod}): {GetIndicatorValue(emaRapida, 0, 0):F2}\n" +
                        $"• EMA Lenta ({EmaLentaPeriod}): {GetIndicatorValue(emaLenta, 0, 0):F2}\n" +
                        $"• EMA Tendência ({EmaTendenciaPeriod}): {GetIndicatorValue(emaTendencia, 0, 0):F2}\n" +
                        $"• EMA Super Lenta ({EmaSuperLentaPeriod}): {GetIndicatorValue(emaSuperLenta, 0, 0):F2}\n" +
                        $"• RSI ({RsiPeriod}): {GetIndicatorValue(rsi, 0, 50):F1}\n" +
                        $"• Glitch Bandwidth: {glitch?.BandWidth[0]:F2}\n" +
                        $"• Glitch Regime: {glitch?.Regime[0]:F0} (Conf: {glitch?.ConfidenceSmoothed[0]:F1})\n" +
                        $"=========================================\n" +
                        $"📊 CurrentBar: {CurrentBar}, State: {State}\n" +
                        $"🤖 ML Ativo: {AtivarMLBasico}\n" +
                        $"🎯 Pronto para trading!");

                    strategyInitialized = true;
                }
                else
                {
                    // Não logar aqui - já foi logado na primeira tentativa
                    // Os indicadores serão verificados periodicamente de forma silenciosa
                }
            }
            catch (Exception ex)
            {
                LogError("Inicialização de indicadores", ex);
                indicatorsReady = false;
                strategyInitialized = false;
            }
        }


        private void InitializeRealtime()
        {
            try
            {
                LogInfo("Realtime",
                    $"🔄 Inicializando modo tempo real V3...\n" +
                    $"CurrentBar: {CurrentBar}, Bars: {Bars?.Count ?? 0}");

                InitializeIndicators();

                if (indicatorsReady)
                {
                    strategyInitialized = true;
                    ResetDailyCounters();

                    InitializePnLLimits();
                    InitializeTradingSession();

                    // Calcular thresholds dinâmicos iniciais
                    CalcularThresholdsDinamicos();

                    LogInfo("✅ ESTRATÉGIA PRONTA V4",
                        $"=== BotSwiftAlgoStrategyV4 INICIADO ===\n" +
                        $"Data/Hora: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\n" +
                        $"Conta: {Account?.Name ?? "N/A"}\n" +
                        $"Instrumento: {Instrument.FullName}\n" +
                        $"Modo: TICK-BY-TICK AVANÇADO\n" +
                        $"Limites PnL: {(AtivarLimitesPnL ? $"Perda=${LimitePerdaDiaria}, Ganho=${LimiteGanhoDiario}" : "Desativados")}\n" +
                        $"Horário: {(AtivarHorarioFuncionamento ? $"{HoraInicioFuncionamento:0000}-{HoraFimFuncionamento:0000}" : "24h")}\n" +
                        $"ML Básico: {(AtivarMLBasico ? "Ativo" : "Desativado")}\n" +
                        $"Thresholds Dinâmicos: {(UsarThresholdsDinamicos ? "Ativo" : "Fixo")}\n" +
                        $"=== ESTADO INICIAL ===\n" +
                        $"Trades hoje: {tradesToday}/{MaxTradesPerDay}\n" +
                        $"Thresholds: Long>{longThresholdDynamic:F2}, Short<{shortThresholdDynamic:F2}");
                }
                else
                {
                    LogInfo("Aguardando",
                        "⏳ Indicadores ainda não prontos...\n" +
                        "A estratégia iniciará automaticamente quando pronta.");
                }
            }
            catch (Exception ex)
            {
                LogError("InitializeRealtime", ex);
            }
        }

        private void TerminateStrategy()
        {
            try
            {
                // Só processar terminação se a estratégia foi realmente executada
                // Verificação rigorosa: usar flag realmenteProcessouBarras que é definida quando processa barras
                // Isso é mais confiável que CurrentBar que pode não estar disponível durante Terminated
                bool realmenteExecutada = strategyInitialized && indicatorsReady && realmenteProcessouBarras;
                
                if (!realmenteExecutada && !indicatorsInitializationAttempted)
                {
                    // Estratégia nunca foi inicializada - apenas limpar recursos básicos sem logs
                    return;
                }
                
                // Se tentou inicializar mas não completou, também não logar
                if (!realmenteExecutada)
                {
                    // Limpar recursos silenciosamente sem logs
                    glitch = null;
                    emaRapida = null;
                    emaLenta = null;
                    emaTendencia = null;
                    emaSuperLenta = null;
                    rsi = null;
                    macd = null;
                    volumeMA = null;
                    stochastics = null;
                    donchian = null;
                    stdDev = null;
                    vwma = null;
                    return;
                }

                // Salvar estatísticas de ML e persistir aprendizado em arquivo
                // Só salvar se a estratégia foi realmente executada (já verificado acima)
                if (AtivarMLBasico)
                {
                    if (tradeStatistics != null && tradeStatistics.Count > 0)
                    {
                        SalvarEstatisticasML();
                    }
                    
                    // Salvar aprendizado em arquivo para persistência entre sessões
                    SalvarAprendizadoEmArquivo();
                }

                // ===== LIMPEZA DE RECURSOS (CONFORMIDADE COM DIRETRIZES) =====
                // Limpar referências de indicadores (NinjaTrader gerencia automaticamente)
                // No NT8, indicadores são limpos automaticamente, apenas removemos referências
                glitch = null;
                emaRapida = null;
                emaLenta = null;
                emaTendencia = null;
                emaSuperLenta = null;
                rsi = null;
                macd = null;
                volumeMA = null;
                stochastics = null;
                donchian = null;
                stdDev = null;
                vwma = null;

                // Limpar estruturas de dados
                if (momentumHistory != null) { momentumHistory.Clear(); momentumHistory = null; }
                if (obvHistory != null) { obvHistory.Clear(); obvHistory = null; }
                if (tradeStatistics != null) { tradeStatistics.Clear(); tradeStatistics = null; }
                if (indicatorPerformance != null) { indicatorPerformance.Clear(); indicatorPerformance = null; }
                if (tradeRecords != null) { tradeRecords.Clear(); tradeRecords = null; }
                if (orderSubmissionTimes != null) { orderSubmissionTimes.Clear(); orderSubmissionTimes = null; }
                if (blockOrderQuantities != null) { blockOrderQuantities.Clear(); blockOrderQuantities = null; }
                if (blockFilledQuantities != null) { blockFilledQuantities.Clear(); blockFilledQuantities = null; }
                if (volumeProfile != null) { volumeProfile = null; }

                // Limpar log builder e mostrar apenas se houver conteúdo relevante E se a estratégia foi executada
                // Usar realmenteExecutada que já foi verificada acima
                if (realmenteExecutada && logBuilder != null && logBuilder.Length > 0)
                {
                    Print($"=== LOG FINAL DA ESTRATÉGIA: {Name} (BotGlitchStrategyV2) ===");
                    Print(logBuilder.ToString());
                    logBuilder.Clear();
                }

                // Log apenas se a estratégia foi realmente executada
                if (realmenteExecutada)
                {
                    LogInfo("Terminação", $"✅ Estratégia '{Name}' (BotGlitchStrategyV2) finalizada com sucesso - Todos os recursos liberados");
                }
            }
            catch (Exception ex)
            {
                Print($"*** ERRO na terminação da estratégia '{Name}': {ex.Message} ***");
                LogError("TerminateStrategy", ex);
            }
        }
        #endregion

        #region Métodos de Inicialização Avançados
        private void InitializePnLLimits()
        {
            try
            {
                if (AtivarLimitesPnL)
                {
                    dailyPnLStart = GetTotalAccountPnL();
                    dailyPnLInitialized = true;
                    limitsActive = true;

                    LogInfo("Limites PnL",
                        $"✅ Sistema de limites PnL inicializado V3\n" +
                        $"PnL total inicial: ${dailyPnLStart:F2}\n" +
                        $"Limite Perda: ${LimitePerdaDiaria}\n" +
                        $"Limite Ganho: ${LimiteGanhoDiario}\n" +
                        $"Reset por dia: {(ResetLimitesPorDia ? "Sim" : "Não")}");
                }
            }
            catch (Exception ex)
            {
                LogError("InitializePnLLimits", ex);
            }
        }

        private void InitializeTradingSession()
        {
            try
            {
                if (!AtivarHorarioFuncionamento)
                {
                    sessionActive = true;
                    return;
                }

                DateTime now = DateTime.Now;

                int startHour = HoraInicioFuncionamento / 100;
                int startMinute = HoraInicioFuncionamento % 100;
                int endHour = HoraFimFuncionamento / 100;
                int endMinute = HoraFimFuncionamento % 100;

                sessionStartTime = new DateTime(now.Year, now.Month, now.Day, startHour, startMinute, 0);
                sessionEndTime = new DateTime(now.Year, now.Month, now.Day, endHour, endMinute, 0);

                if (PermitirSessaoMultidia && HoraFimFuncionamento <= HoraInicioFuncionamento)
                {
                    sessionEndTime = sessionEndTime.AddDays(1);
                }

                sessionActive = now >= sessionStartTime && now <= sessionEndTime;

                LogInfo("Sessão Trading V3",
                    $"✅ Horário de funcionamento configurado\n" +
                    $"Início: {sessionStartTime:HH:mm}\n" +
                    $"Fim: {sessionEndTime:HH:mm}\n" +
                    $"Ativo agora: {sessionActive}\n" +
                    $"Fechar posições no fim: {FecharPosicoesNoFimHorario}\n" +
                    $"Sessão multidia: {PermitirSessaoMultidia}");
            }
            catch (Exception ex)
            {
                LogError("InitializeTradingSession", ex);
            }
        }
        #endregion

        #region Sistema de Logging Estruturado
        private void LogInfo(string category, string message)
        {
            string logEntry = $"{DateTime.Now:HH:mm:ss.fff} [INFO] [{category}] {message}";

            lock (logBuilder)
            {
                logBuilder.AppendLine(logEntry);
            }

            if (EnableStructuredLogging)
            {
                Print(logEntry);
                FlushLogIfNeeded();
            }
        }

        private void LogWarning(string category, string message)
        {
            string logEntry = $"{DateTime.Now:HH:mm:ss.fff} [WARN] [{category}] {message}";

            lock (logBuilder)
            {
                logBuilder.AppendLine(logEntry);
            }

            Print($"*** {logEntry} ***");
            FlushLogIfNeeded();
        }

        private void LogError(string category, Exception ex)
        {
            string logEntry = $"{DateTime.Now:HH:mm:ss.fff} [ERROR] [{category}] {ex.Message}\n{ex.StackTrace}";

            lock (logBuilder)
            {
                logBuilder.AppendLine(logEntry);
            }

            Print($"*** ERRO: {category}: {ex.Message} ***");
            FlushLogIfNeeded();
        }

        private void LogError(string category, string message)
        {
            string logEntry = $"{DateTime.Now:HH:mm:ss.fff} [ERROR] [{category}] {message}";

            lock (logBuilder)
            {
                logBuilder.AppendLine(logEntry);
            }

            Print($"*** ERRO: {category}: {message} ***");
            FlushLogIfNeeded();
        }

        private void LogTrade(string action, string details)
        {
            string logEntry = $"{DateTime.Now:HH:mm:ss.fff} [TRADE] [{action}] {details}";

            lock (logBuilder)
            {
                logBuilder.AppendLine(logEntry);
            }

            Print($"=== {logEntry} ===");
            FlushLogIfNeeded();
        }

        private void LogMLStats()
        {
            if (!EnableMLStatsLogging) return;

            string stats = "🤖 ESTATÍSTICAS ML:\n";

            foreach (var perf in indicatorPerformance.Values)
            {
                stats += $"{perf.IndicatorName}: W={perf.CurrentWeight:F2}, WR={perf.WinRate:P1}, Trades={perf.TotalTrades}\n";
            }

            LogInfo("ML Stats", stats);
        }

        private void FlushLogIfNeeded()
        {
            try
            {
                lock (logBuilder)
                {
                    if (logBuilder.Length > MAX_LOG_SIZE)
                    {
                        string recentLogs = logBuilder.ToString();
                        int startIndex = Math.Max(0, recentLogs.Length - LOG_RETAIN_SIZE);
                        string retainedLogs = recentLogs.Substring(startIndex);

                        logBuilder.Clear();
                        logBuilder.Append("...\n[Log truncado para economizar memória]\n...\n");
                        logBuilder.Append(retainedLogs);

                        LogWarning("Logging", $"Log truncado para {LOG_RETAIN_SIZE} caracteres");
                    }

                    if ((DateTime.Now - lastLogFlush).TotalMinutes >= LogFlushIntervalMinutes)
                    {
                        lastLogFlush = DateTime.Now;
                    }
                }
            }
            catch (Exception ex)
            {
                Print($"*** ERRO em FlushLogIfNeeded: {ex.Message} ***");
            }
        }
        #endregion

        #region Sistema de Thresholds Dinâmicos
        private void CalcularThresholdsDinamicos()
        {
            if (!UsarThresholdsDinamicos) return;

            try
            {
                // CORREÇÃO CRÍTICA: Usar MaxScoreTeorico configurado (5.0) em vez de calcular dinamicamente
                // O cálculo dinâmico estava gerando valores muito altos (40+) resultando em thresholds de 2.0+
                // Com MaxScoreTeorico=5.0 e SensibilidadeThreshold=5%, teremos threshold de 0.25
                // IMPORTANTE: Usar Math.Min para garantir que thresholds dinâmicos sejam SEMPRE <= valores base (mais permissivos)
                double maxScoreTeoricoCalculado = MaxScoreTeorico; // Usar valor configurado (5.0)

                // Aplicar sensibilidade (5% do score máximo por padrão após correção)
                double sensibilidade = SensibilidadeThreshold / 100.0;
                double thresholdCalculado = maxScoreTeoricoCalculado * sensibilidade; // 5.0 * 0.05 = 0.25

                // CORREÇÃO CRÍTICA: Usar Math.Min para garantir que thresholds dinâmicos sejam SEMPRE <= valores base
                // Isso garante que os thresholds dinâmicos sejam mais permissivos, não mais restritivos
                longThresholdDynamic = Math.Min(LongThreshold, thresholdCalculado); // Min entre 0.8 e 0.25 = 0.25
                shortThresholdDynamic = Math.Max(ShortThreshold, -thresholdCalculado); // Max entre -0.8 e -0.25 = -0.25

                LogInfo("Thresholds Dinâmicos",
                    $"⚡ Thresholds ajustados: Long > {longThresholdDynamic:F2}, Short < {shortThresholdDynamic:F2}\n" +
                    $"Score máximo teórico: {maxScoreTeoricoCalculado:F2}\n" +
                    $"Sensibilidade: {SensibilidadeThreshold}%");
            }
            catch (Exception ex)
            {
                LogError("CalcularThresholdsDinamicos", ex);
                longThresholdDynamic = LongThreshold;
                shortThresholdDynamic = ShortThreshold;
            }
        }
        #endregion

        #region Sistema de Breakout Avançado
        // CORREÇÃO: Método CalcularScoreBreakout ajustado
        private double CalcularScoreBreakout()
        {
            if (!AtivarSistemaBreakout || !IsValidDataPoint(0)) return 0;

            double breakoutScore = 0;
            double currentPrice = Close[0];
            double currentVolume = Volume[0];

            // CORREÇÃO: Verificar se glitch tem dados antes de usar
            if (glitch != null && glitch.Count > 0 && IsIndicatorValid(glitch, 0) && CurrentBar > 0)
            {
                double bandwidth = glitch.BandWidth[0];
                double bandwidthPrev = glitch.BandWidth[1];
                double bandwidthPercent = bandwidth / currentPrice * 100;

                if (previousBBWidth > 0)
                {
                    // Squeeze detectado quando a largura das bandas diminui significativamente
                    if (bandwidth < previousBBWidth * (1 - SqueezeThreshold))
                    {
                        squeezeCount++;
                        breakoutScore += 0.2;

                        if (squeezeCount >= 3)
                        {
                            breakoutScore += 0.3;
                            LogInfo("Breakout", $"🔍 Squeeze detectado ({squeezeCount} períodos)");
                        }
                    }
                    else
                    {
                        squeezeCount = 0;
                    }
                }
                previousBBWidth = bandwidth;
            }

            // 2. Breakout de Donchian Channel
            // CORREÇÃO: Variáveis movidas para escopo correto
            double donchianUpper = 0;
            double donchianLower = 0;
            
            if (IsValidDataPoint(0))
            {
                donchianUpper = GetDonchianUpperValue();
                donchianLower = GetDonchianLowerValue();
                double volumeMAVal = volumeMA[0];

                // Breakout para cima
                if (currentPrice >= donchianUpper && currentVolume > volumeMAVal * MinBreakoutVolumeMultiplier)
                {
                    breakoutDetectado = true;
                    breakoutLevel = donchianUpper;
                    breakoutTime = Time[0];
                    breakoutScore += 0.4;
                    LogInfo("Breakout", $"🚀 Breakout UP detectado @ {currentPrice:F2}");
                }
                // Breakout para baixo
                else if (currentPrice <= donchianLower && currentVolume > volumeMAVal * MinBreakoutVolumeMultiplier)
                {
                    breakoutDetectado = true;
                    breakoutLevel = donchianLower;
                    breakoutTime = Time[0];
                    breakoutScore -= 0.4; // CORREÇÃO: Sinal bearish (era +0.4)
                    LogInfo("Breakout", $"📉 Breakout DOWN detectado @ {currentPrice:F2}");
                }

                // Manter força do breakout por alguns períodos - CORREÇÃO: direcional
                if (breakoutDetectado && (Time[0] - breakoutTime).TotalMinutes <= 10)
                {
                    // Recuperar valores atualizados do Donchian
                    donchianUpper = GetDonchianUpperValue();
                    donchianLower = GetDonchianLowerValue();
                    
                    if (currentPrice >= donchianUpper)
                        breakoutScore += 0.2; // Breakout UP continua
                    else if (currentPrice <= donchianLower)
                        breakoutScore -= 0.2; // CORREÇÃO: Breakout DOWN continua (era +)
                }
            }

            // 3. Confirmação de Volume - CORREÇÃO: direcional baseado na direção do preço
            if (currentVolume > volumeMA[0] * 2.0)
            {
                if (Close[0] > Open[0])
                    breakoutScore += 0.2; // Volume alto com barra bullish
                else if (Close[0] < Open[0])
                    breakoutScore -= 0.2; // CORREÇÃO: Volume alto com barra bearish (era sempre +)
            }

            // 4. Direção alinhada com breakout - CORREÇÃO: Removida dependência circular de totalScore
            if (breakoutDetectado && IsValidDataPoint(0))
            {
                // Recuperar valores atualizados do Donchian
                donchianUpper = GetDonchianUpperValue();
                donchianLower = GetDonchianLowerValue();
                
                if (currentPrice >= donchianUpper)
                {
                    breakoutScore += 0.1; // Confirmação bullish
                }
                else if (currentPrice <= donchianLower)
                {
                    breakoutScore -= 0.1; // CORREÇÃO: Confirmação bearish (era +)
                }
            }

            return breakoutScore * PesoBreakout;
        }

        // CORREÇÃO: Método CalcularScoreVolumeAvancado atualizado para VWAP
        private double CalcularScoreVolumeAvancado()
        {
            if (!AtivarVolume || !usarVolumeAvancado || !IsValidDataPoint(0))
                return 0;

            double score = 0;
            double currentVolume = Volume[0];
            double volumeMAVal = GetIndicatorValue(volumeMA, 0, currentVolume);

            // 1. Volume Climax Detection
            // CORREÇÃO: Volume climax deve ser direcional baseado na direção da barra
            if (UsarVolumeClimax && volumeMAVal > 0)
            {
                double volumePercentile = volumeProfile.GetVolumePercentile(VolumePercentilClimax);

                if (currentVolume >= volumePercentile)
                {
                    // Volume extremo com direção: bullish se fechamento > abertura, bearish se <
                    if (Close[0] > Open[0])
                    {
                        score += 0.3; // Volume climax bullish
                        LogInfo("Volume", $"📈 Volume CLIMAX BULLISH detectado: {currentVolume:N0} (Percentil {VolumePercentilClimax})");
                    }
                    else if (Close[0] < Open[0])
                    {
                        score -= 0.3; // CORREÇÃO: Volume climax bearish (era sempre +0.3)
                        LogInfo("Volume", $"📉 Volume CLIMAX BEARISH detectado: {currentVolume:N0} (Percentil {VolumePercentilClimax})");
                    }
                    // Se Close == Open (doji), não adiciona score
                }
            }

            // 2. VWAP Bands simplificado - sem VWAP problemático
            // CORREÇÃO: Removida dependência circular de totalScore - usar apenas direção do preço
            if (UsarVWAPBands && IsValidDataPoint(0))
            {
                try
                {
                    // Usar média móvel como aproximação do VWAP
                    double avgPrice = (High[0] + Low[0] + Close[0]) / 3;
                    double priceStdDev = Math.Abs(Close[0] - avgPrice);

                    double vwapUpper = avgPrice + (priceStdDev * VWAPBandStdDev);
                    double vwapLower = avgPrice - (priceStdDev * VWAPBandStdDev);

                    // CORREÇÃO: Usar direção do preço, não totalScore anterior
                    if (Close[0] > vwapUpper)
                    {
                        score += 0.2; // Sinal bullish
                    }
                    else if (Close[0] < vwapLower)
                    {
                        score -= 0.2; // Sinal bearish (CORRIGIDO: era score += 0.2)
                    }
                }
                catch (Exception ex)
                {
                    LogError("VWAP Bands Calculation", ex);
                }
            }

            // 3. Volume Profile tradicional
            try
            {
                volumeProfile.Update(High[0], Low[0], currentVolume, Time[0]);
                double highVolumeNode = volumeProfile.GetHighVolumeNode();

                if (highVolumeNode > 0)
                {
                    double distanciaPercentual = Math.Abs(Close[0] - highVolumeNode) / Close[0];
                    if (distanciaPercentual < 0.002)
                    {
                        score += 0.2;
                    }
                }
            }
            catch (Exception ex)
            {
                LogError("Volume Profile Update", ex);
            }

            // 4. OBV e divergências
            // CORREÇÃO: Removida dependência circular de totalScore
            CalcularOBV();

            if (DetectarDivergenciaBullishOBV())
            {
                score += 0.3; // Sinal bullish
                LogInfo("Volume", "Divergência Bullish OBV detectada");
            }
            else if (DetectarDivergenciaBearishOBV())
            {
                score -= 0.3; // Sinal bearish (CORRIGIDO: era score += 0.3)
                LogInfo("Volume", "Divergência Bearish OBV detectada");
            }

            return score * PesoVolume;
        }

        #endregion

        #region Sistema de Volume Aprimorado

        private void CalcularOBV()
        {
            if (!IsValidDataPoint(1)) return;

            double currentClose = Close[0];
            double previousClose = Close[1];
            double currentVolume = Volume[0];

            if (currentClose > previousClose)
                obvValue += currentVolume;
            else if (currentClose < previousClose)
                obvValue -= currentVolume;

            obvHistory.Add(obvValue);

            if (obvHistory.Count > 100)
                obvHistory.RemoveAt(0);
        }

        private bool DetectarDivergenciaBullishOBV()
        {
            if (obvHistory.Count < 10) return false;

            int lookback = Math.Min(10, obvHistory.Count);

            double lowestPrice = double.MaxValue;
            int lowestPriceIndex = 0;
            for (int i = 0; i < lookback; i++)
            {
                if (IsValidDataPoint(i) && Low[i] < lowestPrice)
                {
                    lowestPrice = Low[i];
                    lowestPriceIndex = i;
                }
            }

            double lowestOBV = double.MaxValue;
            int lowestOBVIndex = 0;
            for (int i = 0; i < lookback; i++)
            {
                if (i < obvHistory.Count && obvHistory[i] < lowestOBV)
                {
                    lowestOBV = obvHistory[i];
                    lowestOBVIndex = i;
                }
            }

            return lowestPriceIndex >= lookback - 3 &&
                   lowestOBVIndex < lowestPriceIndex &&
                   lowestPriceIndex < obvHistory.Count &&
                   obvHistory[lowestPriceIndex] > lowestOBV;
        }

        private bool DetectarDivergenciaBearishOBV()
        {
            if (obvHistory.Count < 10) return false;

            int lookback = Math.Min(10, obvHistory.Count);

            double highestPrice = double.MinValue;
            int highestPriceIndex = 0;
            for (int i = 0; i < lookback; i++)
            {
                if (IsValidDataPoint(i) && High[i] > highestPrice)
                {
                    highestPrice = High[i];
                    highestPriceIndex = i;
                }
            }

            double highestOBV = double.MinValue;
            int highestOBVIndex = 0;
            for (int i = 0; i < lookback; i++)
            {
                if (i < obvHistory.Count && obvHistory[i] > highestOBV)
                {
                    highestOBV = obvHistory[i];
                    highestOBVIndex = i;
                }
            }

            return highestPriceIndex >= lookback - 3 &&
                   highestOBVIndex < highestPriceIndex &&
                   highestPriceIndex < obvHistory.Count &&
                   obvHistory[highestPriceIndex] < highestOBV;
        }
        #endregion

        #region Confirmação Multi-Timeframe V4
        private bool VerificarConfirmacaoMultiTimeframe()
        {
            if (!UsarMultiTimeframe || !RequerirConfirmacaoMultiTimeframe) return true;

            try
            {
                // V4: Implementação melhorada usando SMA20 e SMA50 para análise de tendência de médio prazo
                if (CurrentBar < 50) return false;

                if (sma20 == null || sma50 == null || !IsIndicatorValid(sma20, 0) || !IsIndicatorValid(sma50, 0))
                    return true; // Se indicadores não estão prontos, permitir trade

                double media20 = GetIndicatorValue(sma20, 0, Close[0]);
                double media50 = GetIndicatorValue(sma50, 0, Close[0]);
                double precoAtual = Close[0];

                bool tendenciaAlta = media20 > media50 && precoAtual > media20;
                bool tendenciaBaixa = media20 < media50 && precoAtual < media20;

                // Confirmar direção do trade com tendência de maior prazo
                if (tradeDirection == MarketPosition.Long && !tendenciaAlta)
                {
                    LogInfo("Multi-Timeframe", $"⏳ Long bloqueado: tendência de médio prazo não confirma (SMA20: {media20:F2}, SMA50: {media50:F2})");
                    return false;
                }

                if (tradeDirection == MarketPosition.Short && !tendenciaBaixa)
                {
                    LogInfo("Multi-Timeframe", $"⏳ Short bloqueado: tendência de médio prazo não confirma (SMA20: {media20:F2}, SMA50: {media50:F2})");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                LogError("VerificarConfirmacaoMultiTimeframe", ex);
                return true; // Retorna true em caso de erro para não bloquear trades
            }
        }
        #endregion

        #region Sistema de Filtros de Qualidade V4
        /// <summary>
        /// V4: Detecta se o mercado está em condição de choppiness (lateral/range)
        /// </summary>
        private bool EstaEmChoppiness()
        {
            try
            {
                // Usar Regime do GlitchIndicator diretamente
                // CORREÇÃO: Verificar se glitch tem dados antes de usar
                if (glitch == null || glitch.Count == 0 || !IsIndicatorValid(glitch, 0)) 
                {
                    // Fallback: verificar EMAs se GlitchIndicator não disponível
                    if (IsIndicatorValid(emaRapida, 0) && IsIndicatorValid(emaLenta, 0))
                    {
                        double distanciaEMA = Math.Abs(emaRapida[0] - emaLenta[0]) / Close[0] * 100;
                        if (distanciaEMA < 0.1) return true;
                    }
                    // Se GlitchIndicator não tem dados, não considerar como choppy
                    // Retornar false para permitir trading mesmo sem GlitchIndicator
                    return false;
                }
                
                int regime = (int)glitch.Regime[0];
                double confidence = glitch.ConfidenceSmoothed[0];
                
                // Regime Chop (-1) = mercado lateral
                if (regime == -1)
                {
                    // Se confiança é baixa em Chop, é realmente choppy
                    if (confidence < 70)
                        return true;
                }
                
                // Verificar se EMAs estão muito próximas (mercado lateral) - validação adicional
                if (IsIndicatorValid(emaRapida, 0) && IsIndicatorValid(emaLenta, 0))
                {
                    double distanciaEMA = Math.Abs(emaRapida[0] - emaLenta[0]) / Close[0] * 100;
                    if (distanciaEMA < 0.1) // EMAs muito próximas = choppy
                    {
                        return true;
                    }
                }

                // Verificar se preço está oscilando dentro de range pequeno
                if (CurrentBar >= 20)
                {
                    double high20 = MAX(High, 20)[0];
                    double low20 = MIN(Low, 20)[0];
                    double rangePercent = (high20 - low20) / Close[0] * 100;

                    if (rangePercent < 0.5) // Range muito pequeno = choppy
                    {
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                LogError("EstaEmChoppiness", ex);
                return false; // Em caso de erro, não bloquear
            }
        }

        /// <summary>
        /// V4: Valida se múltiplos indicadores estão alinhados na mesma direção
        /// </summary>
        private bool IndicadoresAlinhados()
        {
            try
            {
                int indicadoresFavoraveis = 0;
                int totalIndicadores = 0;

                // EMA Rápida vs Lenta
                if (IsIndicatorValid(emaRapida, 0) && IsIndicatorValid(emaLenta, 0))
                {
                    totalIndicadores++;
                    bool emaFavoravel = (emaRapida[0] > emaLenta[0] && tradeDirection == MarketPosition.Long) ||
                                       (emaRapida[0] < emaLenta[0] && tradeDirection == MarketPosition.Short);
                    if (emaFavoravel) indicadoresFavoraveis++;
                }

                // MACD
                if (IsIndicatorValid(macd, 0) && CurrentBar > 1)
                {
                    totalIndicadores++;
                    double macdVal = GetIndicatorValue(macd.Default, 0, 0);
                    double macdSignal = GetIndicatorValue(macd.Avg, 0, 0);
                    bool macdFavoravel = (macdVal > macdSignal && tradeDirection == MarketPosition.Long) ||
                                         (macdVal < macdSignal && tradeDirection == MarketPosition.Short);
                    if (macdFavoravel) indicadoresFavoraveis++;
                }

                // RSI
                // CORREÇÃO CRÍTICA: Ajustado de >50/<50 para >40/<60 - muito mais flexível para timeframe 1min
                // Em 1min, RSI oscila muito e raramente fica >50 durante movimentos de alta
                // Com >40, considera favorável quando está em zona neutra-alta (mais apropriado para 1min)
                if (IsIndicatorValid(rsi, 0))
                {
                    totalIndicadores++;
                    double rsiVal = GetIndicatorValue(rsi, 0, 50);
                    bool rsiFavoravel = (rsiVal > 40 && tradeDirection == MarketPosition.Long) ||  // Zona neutra-alta (40-100)
                                       (rsiVal < 60 && tradeDirection == MarketPosition.Short);   // Zona neutra-baixa (0-60)
                    if (rsiFavoravel) indicadoresFavoraveis++;
                }

                // Stochastics
                // CORREÇÃO: Adicionada tolerância de 5 pontos - em timeframe 1min, valores podem oscilar muito próximos
                // Permite considerar favorável mesmo quando stochK e stochD estão muito próximos
                if (IsIndicatorValid(stochastics, 0))
                {
                    totalIndicadores++;
                    double stochK = GetIndicatorValue(stochastics.K, 0, 50);
                    double stochD = GetIndicatorValue(stochastics.D, 0, 50);
                    bool stochFavoravel = (stochK > stochD - 5 && tradeDirection == MarketPosition.Long) ||  // Tolerância de 5 pontos
                                          (stochK < stochD + 5 && tradeDirection == MarketPosition.Short);
                    if (stochFavoravel) indicadoresFavoraveis++;
                }

                // CORREÇÃO CRÍTICA: Com critérios RSI (>40 em vez de >50) e Stochastics (tolerância de 5) ajustados,
                // esperamos que mais indicadores sejam considerados favoráveis (1-2/4 em média)
                // Mantemos threshold de 5% (muito baixo) para ainda validar alinhamento básico sem ser muito restritivo
                // Com 5%, se houver 4 indicadores, precisa de pelo menos 0.2 indicadores (arredondado para 1)
                bool resultado = totalIndicadores >= 1 && (indicadoresFavoraveis / (double)totalIndicadores) >= 0.05; // 5% = muito permissivo mas ainda valida alinhamento

                if (!resultado && totalIndicadores >= 3)
                {
                    LogInfo("Filtro Alinhamento", $"⏳ Indicadores não alinhados: {indicadoresFavoraveis}/{totalIndicadores} favoráveis");
                }

                return resultado;
            }
            catch (Exception ex)
            {
                LogError("IndicadoresAlinhados", ex);
                return true; // Em caso de erro, não bloquear
            }
        }

        /// <summary>
        /// V4: Valida distância até suporte/resistência próximo
        /// </summary>
        private bool ValidarDistanciaSuporteResistencia()
        {
            try
            {
                if (CurrentBar < 20) return true;

                double high20 = MAX(High, 20)[0];
                double low20 = MIN(Low, 20)[0];
                double currentPrice = Close[0];

                double distanciaResistencia = (high20 - currentPrice) / currentPrice * 100;
                double distanciaSuporte = (currentPrice - low20) / currentPrice * 100;

                // Para Long: garantir distância mínima até resistência
                // CORREÇÃO CRÍTICA LOG5: Aumentado de 0.001% para 0.01% - corrige bloqueios incorretos com 0.00%
                // Análise mostrou muitos bloqueios com distância 0.00%, indicando problema no cálculo
                // Com 0.01%, permite trades válidos mas ainda protege contra trades muito próximos de S/R
                if (tradeDirection == MarketPosition.Long)
                {
                    if (distanciaResistencia < 0.01) // Aumentado de 0.001% para 0.01% - corrige bloqueios incorretos
                    {
                        LogWarning("Filtro S/R", $"Long bloqueado: muito próximo da resistência ({distanciaResistencia:F2}%)");
                        return false;
                    }
                }

                // Para Short: garantir distância mínima até suporte
                // CORREÇÃO CRÍTICA LOG5: Aumentado de 0.001% para 0.01% - corrige bloqueios incorretos com 0.00%
                // Análise mostrou muitos bloqueios com distância 0.00%, indicando problema no cálculo
                if (tradeDirection == MarketPosition.Short)
                {
                    if (distanciaSuporte < 0.01) // Aumentado de 0.001% para 0.01% - corrige bloqueios incorretos
                    {
                        LogWarning("Filtro S/R", $"Short bloqueado: muito próximo do suporte ({distanciaSuporte:F2}%)");
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                LogError("ValidarDistanciaSuporteResistencia", ex);
                return true; // Em caso de erro, não bloquear
            }
        }

        /// <summary>
        /// V4: Valida se volatilidade está em nível ideal para trading
        /// </summary>
        private bool ValidarVolatilidadeIdeal()
        {
            try
            {
                // CORREÇÃO: Verificar se glitch tem dados antes de usar
                if (glitch == null || glitch.Count == 0 || !IsIndicatorValid(glitch, 0)) return true;

                double bandwidth = glitch.BandWidth[0];
                double sigma = bandwidth / 2.0;
                double sigmaPercent = sigma / Close[0] * 100;
                // CORREÇÃO CRÍTICA: Reduzido de 0.05% para 0.01% e aumentado de 10.0% para 20.0%
                // Range muito mais amplo para permitir mais trades em timeframe 1min
                if (sigmaPercent < 0.01) // Reduzido de 0.05% para 0.01%
                {
                    LogWarning("Filtro Volatilidade", $"Volatilidade muito baixa: {sigmaPercent:F2}%");
                    return false;
                }

                // CORREÇÃO CRÍTICA: Aumentado de 10.0% para 20.0% - muito mais permissivo
                if (sigmaPercent > 20.0) // Aumentado de 10.0% para 20.0%
                {
                    LogWarning("Filtro Volatilidade", $"Volatilidade muito alta: {sigmaPercent:F2}%");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                LogError("ValidarVolatilidadeIdeal", ex);
                return true; // Em caso de erro, não bloquear
            }
        }

        /// <summary>
        /// V4: Valida volume e liquidez antes de entrar
        /// </summary>
        private bool ValidarVolumeLiquidez()
        {
            try
            {
                // CORREÇÃO CRÍTICA: Reduzido de 0.3 (30%) para 0.2 (20%) - muito mais permissivo para timeframe 1min
                // Em períodos de baixo volume, o filtro estava bloqueando muitos trades válidos
                // Volume atual vs média
                if (Volume[0] < volumeMA[0] * 0.2) // Volume muito abaixo da média (reduzido de 30% para 20%)
                {
                    LogWarning("Filtro Volume", $"Volume insuficiente: {Volume[0]:N0} vs média {volumeMA[0]:N0} (Threshold: 20%)");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                LogError("ValidarVolumeLiquidez", ex);
                return true; // Em caso de erro, não bloquear
            }
        }

        /// <summary>
        /// V4: Valida horário otimizado (horários de maior probabilidade)
        /// </summary>
        private bool ValidarHorarioOtimizado()
        {
            try
            {
                int hora = Time[0].Hour;
                int minuto = Time[0].Minute;

                // Horários de maior probabilidade (ajustar conforme mercado)
                // Exemplo para mercado americano: abertura (9-11) e fechamento (14-15)
                bool horarioBom = (hora >= 9 && hora <= 11) ||  // Abertura
                                  (hora >= 14 && hora <= 15);    // Fechamento

                // Em horários ruins, ainda permitir mas com threshold mais alto (será ajustado dinamicamente)
                return true; // Sempre retornar true, mas ajustar threshold dinamicamente se necessário
            }
            catch (Exception ex)
            {
                LogError("ValidarHorarioOtimizado", ex);
                return true; // Em caso de erro, não bloquear
            }
        }

        /// <summary>
        /// V4: Calcula score de qualidade do trade (0-10 pontos)
        /// </summary>
        private int CalcularScoreQualidade()
        {
            try
            {
                int score = 0;

                // ADX forte (1-3 pontos)
                // CORREÇÃO: Verificar se glitch tem dados antes de usar
                if (glitch != null && glitch.Count > 0 && IsIndicatorValid(glitch, 0))
                {
                    int regime = (int)glitch.Regime[0];
                    double confidence = glitch.ConfidenceSmoothed[0];
                    
                    if (regime == 1 && confidence >= 75) score += 3;
                    else if (regime == 1 && confidence >= 65) score += 1;
                    else if (regime == 0 && confidence >= 70) score += 1;
                }

                // CORREÇÃO CRÍTICA: Removida contribuição de IndicadoresAlinhados() no score de qualidade
                // Os indicadores já estão incluídos no cálculo do score total (tendência + momentum)
                // Adicionar novamente aqui cria redundância e pode inflacionar o score de qualidade
                // if (IndicadoresAlinhados()) score += 2; // REMOVIDO - redundante

                // Volume adequado (1 ponto)
                if (Volume[0] > volumeMA[0]) score += 1;

                // CORREÇÃO CRÍTICA: Reduzido de 2 para 1 ponto - choppiness estava reduzindo muito o score
                // Em timeframe de 1min, mercado pode estar em choppiness frequentemente
                // Reduzir peso para não bloquear muitos trades válidos
                if (!EstaEmChoppiness()) score += 1; // Reduzido de 2 para 1 ponto

                // Volatilidade ideal (1 ponto)
                if (ValidarVolatilidadeIdeal()) score += 1;

                // Distância de S/R adequada (1 ponto)
                if (ValidarDistanciaSuporteResistencia()) score += 1;

                scoreQualidadeTrade = score;
                return score;
            }
            catch (Exception ex)
            {
                LogError("CalcularScoreQualidade", ex);
                return 5; // Score médio em caso de erro
            }
        }

        /// <summary>
        /// V4: Valida qualidade geral do trade antes de entrar
        /// </summary>
        private bool ValidarQualidadeTrade()
        {
            try
            {
                int qualidadeScore = CalcularScoreQualidade();
                
                // CORREÇÃO CRÍTICA: Threshold dinâmico baseado no score total
                // Se score total está muito acima do threshold, qualidade mínima é menos crítica
                double thresholdLong = CalcularThresholdDinamicoComQualidade();
                double thresholdShort = UsarThresholdsDinamicos ? shortThresholdDynamic : ShortThreshold;
                
                // CORREÇÃO CRÍTICA LOG5: Qualidade mínima aumentada para melhor seleção de trades
                // Análise mostrou Qualidade Score baixo (2-3/10) sendo aceito frequentemente
                // Threshold dinâmico ajustado para ser mais seletivo
                int qualidadeMinima = 3; // Padrão: mínimo 3 pontos (aumentado de 1 para 3)
                
                // Se score está 2x acima do threshold, reduzir qualidade mínima para 1
                if ((tradeDirection == MarketPosition.Long && totalScore >= thresholdLong * 2.0) ||
                    (tradeDirection == MarketPosition.Short && totalScore <= thresholdShort * 2.0))
                {
                    qualidadeMinima = 1; // Reduzido de 0 para 1 - ainda requer qualidade mínima mesmo para scores altos
                    LogInfo("Qualidade Ajustada", $"Score muito alto ({totalScore:F2}) - Qualidade mínima reduzida para 1");
                }
                // Se score está 1.5x acima do threshold, reduzir qualidade mínima para 2
                else if ((tradeDirection == MarketPosition.Long && totalScore >= thresholdLong * 1.5) ||
                         (tradeDirection == MarketPosition.Short && totalScore <= thresholdShort * 1.5))
                {
                    qualidadeMinima = 2; // Reduzido de 0 para 2 - requer qualidade mínima adequada
                    LogInfo("Qualidade Ajustada", $"Score alto ({totalScore:F2}) - Qualidade mínima reduzida para 2");
                }

                // CORREÇÃO CRÍTICA: Threshold dinâmico - muito mais permissivo
                // Mínimo 0-1 ponto de qualidade dependendo do score total
                bool qualidadeOk = qualidadeScore >= qualidadeMinima;

                if (!qualidadeOk)
                {
                    ultimoTradeBloqueadoPorFiltro = true;
                    motivoBloqueioUltimoTrade = $"Score de qualidade insuficiente: {qualidadeScore}/10";
                    LogWarning("Filtro Qualidade", $"⏳ Trade bloqueado: {motivoBloqueioUltimoTrade}");
                }
                else
                {
                    ultimoTradeBloqueadoPorFiltro = false;
                    motivoBloqueioUltimoTrade = "";
                }

                return qualidadeOk;
            }
            catch (Exception ex)
            {
                LogError("ValidarQualidadeTrade", ex);
                return true; // Em caso de erro, não bloquear
            }
        }

        /// <summary>
        /// V4: Calcula threshold dinâmico baseado em qualidade do setup
        /// </summary>
        private double CalcularThresholdDinamicoComQualidade()
        {
            try
            {
                double thresholdBase = UsarThresholdsDinamicos ? longThresholdDynamic : LongThreshold;

                // CORREÇÃO: Ajuste mais permissivo para aumentar número de trades
                // Ajustar threshold baseado em qualidade do setup
                int qualidadeScore = CalcularScoreQualidade();

                // Se qualidade é alta (>=6), reduzir threshold significativamente
                // Se qualidade é baixa (<4), apenas aumentar ligeiramente (menos rigoroso)
                if (qualidadeScore >= 6)
                {
                    return thresholdBase * 0.85; // 15% mais permissivo (aumentado de 10%)
                }
                else if (qualidadeScore < 4)
                {
                    return thresholdBase * 1.1; // Apenas 10% mais rigoroso (reduzido de 20%)
                }

                return thresholdBase;
            }
            catch (Exception ex)
            {
                LogError("CalcularThresholdDinamicoComQualidade", ex);
                return UsarThresholdsDinamicos ? longThresholdDynamic : LongThreshold;
            }
        }
        #endregion

        #region Sistema de Scoring Aprimorado
        private void CalcularSistemaScoring()
        {
            double scoreAntes = totalScore;
            scoreBase = 0;

            // Armazenar contribuições individuais para ML
            double scoreTendencia = 0;
            double scoreMomentum = 0;
            double scoreVolume = 0;
            double scoreVolatilidade = 0;
            double scoreBreakout = 0;

            // 1. SISTEMA DE TENDÊNCIA (30%)
            if (AtivarSistemaTendencia)
            {
                scoreTendencia = CalcularScoreTendencia();
                scoreBase += scoreTendencia * PesoTendencia;
            }

            // 2. SISTEMA DE MOMENTUM NORMALIZADO (30%)
            if (AtivarMomentum)
            {
                scoreMomentum = CalcularScoreMomentumNormalizado();
                scoreBase += scoreMomentum * PesoMomentum;
            }

            // 3. SISTEMA DE VOLATILIDADE (20%)
            if (AtivarVolatilidade)
            {
                scoreVolatilidade = CalcularScoreVolatilidade();
                scoreBase += scoreVolatilidade * PesoVolatilidade;
            }

            // 4. VOLUME (20%)
            if (AtivarVolume)
            {
                scoreVolume = CalcularScoreVolumeAvancado();
                scoreBase += scoreVolume;
            }

            // 5. SISTEMA DE BREAKOUT (Adicional)
            if (AtivarSistemaBreakout)
            {
                scoreBreakout = CalcularScoreBreakout();
                scoreBase += scoreBreakout;
            }

            // ⭐⭐ FÓRMULA FINAL COM NORMALIZAÇÃO ⭐⭐
            totalScore = scoreBase;

            // Limitar score entre -5 e +5
            totalScore = Math.Max(-5.0, Math.Min(5.0, totalScore));
            
            // Log diagnóstico em backtest quando score muda significativamente
            if (State == State.Historical && CurrentBar % 200 == 0 && CurrentBar > 0)
            {
                LogInfo("Score Calculado", 
                    $"Bar: {CurrentBar}, Score Base: {scoreBase:F2}, Score Final: {totalScore:F2}, " +
                    $"Score anterior: {scoreAntes:F2}");
            }

            // Armazenar para análise de ML
            if (AtivarMLBasico)
            {
                ArmazenarScoreParaML(scoreTendencia, scoreMomentum, scoreVolume, scoreVolatilidade, scoreBreakout);
            }

            // Atualizar thresholds dinâmicos periodicamente
            if (UsarThresholdsDinamicos && DateTime.Now.Minute % 15 == 0)
            {
                CalcularThresholdsDinamicos();
            }
        }

        private double CalcularScoreTendencia()
        {
            double tendenciaScore = 0;

            if (IsIndicatorValid(emaTendencia, 0) && IsIndicatorValid(emaSuperLenta, 0) &&
                IsIndicatorValid(emaRapida, 0) && IsIndicatorValid(emaLenta, 0))
            {
                double emaRapidaVal = GetIndicatorValue(emaRapida, 0, Close[0]);
                double emaLentaVal = GetIndicatorValue(emaLenta, 0, Close[0]);
                double emaTendenciaVal = GetIndicatorValue(emaTendencia, 0, Close[0]);
                double emaSuperLentaVal = GetIndicatorValue(emaSuperLenta, 0, Close[0]);
                double precoAtual = Close[0];

                int scoreMultiplo = 0;
                int maxScore = 5;

                if (precoAtual > emaTendenciaVal) scoreMultiplo += 1;
                if (precoAtual > emaSuperLentaVal) scoreMultiplo += 1;
                if (emaRapidaVal > emaLentaVal) scoreMultiplo += 1;
                if (emaLentaVal > emaTendenciaVal) scoreMultiplo += 1;
                if (emaTendenciaVal > emaSuperLentaVal) scoreMultiplo += 1;

                // Para score negativo (SHORT)
                if (precoAtual < emaTendenciaVal) scoreMultiplo -= 1;
                if (precoAtual < emaSuperLentaVal) scoreMultiplo -= 1;
                if (emaRapidaVal < emaLentaVal) scoreMultiplo -= 1;
                if (emaLentaVal < emaTendenciaVal) scoreMultiplo -= 1;
                if (emaTendenciaVal < emaSuperLentaVal) scoreMultiplo -= 1;

                // Normalizar para -1.0 a +1.0
                tendenciaScore = (double)scoreMultiplo / maxScore;
            }

            return tendenciaScore;
        }

        private double CalcularScoreMomentumNormalizado()
        {
            double momentumScore = 0;
            double maxPoints = 0;

            // RSI Normalizado (0.25 pontos)
            if (IsIndicatorValid(rsi, 0))
            {
                double rsiVal = GetIndicatorValue(rsi, 0, 50);
                double rsiScore = 0;

                if (rsiVal > 70) rsiScore = -0.1;
                else if (rsiVal < 30) rsiScore = 0.1;
                else if (rsiVal > 50) rsiScore = 0.05;
                else if (rsiVal < 50) rsiScore = -0.05;

                momentumScore += rsiScore * 0.25;
                maxPoints += 0.25;
            }

            // MACD Aprimorado (0.25 pontos)
            if (CurrentBar > 1 && IsIndicatorValid(macd, 0) && IsIndicatorValid(macd, 1))
            {
                double macdVal = GetIndicatorValue(macd.Default, 0, 0);
                double macdSignal = GetIndicatorValue(macd.Avg, 0, 0);
                double macdHist = macdVal - macdSignal;

                double macdValPrev = GetIndicatorValue(macd.Default, 1, 0);
                double macdSignalPrev = GetIndicatorValue(macd.Avg, 1, 0);
                double macdHistPrev = macdValPrev - macdSignalPrev;

                if (macdHist > 0 && macdHistPrev <= 0)
                {
                    momentumScore += 0.15;
                }
                else if (macdHist < 0 && macdHistPrev >= 0)
                {
                    momentumScore -= 0.15;
                }
                else if (macdHist > macdHistPrev && macdHist > 0)
                {
                    momentumScore += 0.05;
                }
                else if (macdHist < macdHistPrev && macdHist < 0)
                {
                    momentumScore -= 0.05;
                }

                maxPoints += 0.25;
            }

            // Stochastics com Crossover (0.25 pontos)
            if (IsIndicatorValid(stochastics, 0) && IsIndicatorValid(stochastics, 1))
            {
                double stochK = GetIndicatorValue(stochastics.K, 0, 50);
                double stochD = GetIndicatorValue(stochastics.D, 0, 50);
                double stochKPrev = GetIndicatorValue(stochastics.K, 1, 50);
                double stochDPrev = GetIndicatorValue(stochastics.D, 1, 50);

                // Crossover Bullish
                if (stochK > stochD && stochKPrev <= stochDPrev)
                {
                    momentumScore += 0.15;
                }
                // Crossover Bearish
                else if (stochK < stochD && stochKPrev >= stochDPrev)
                {
                    momentumScore -= 0.15;
                }

                // Overbought/Oversold
                if (stochK > 80) momentumScore -= 0.05;
                else if (stochK < 20) momentumScore += 0.05;

                maxPoints += 0.25;
            }

            // CORREÇÃO: Verificar se glitch tem dados antes de usar
            if (glitch != null && glitch.Count > 0 && IsIndicatorValid(glitch, 0))
            {
                double confidence = glitch.ConfidenceSmoothed[0];
                double adxScore = Math.Min(0.1, confidence / 100.0);

                momentumScore += adxScore;
                maxPoints += 0.25;
            }

            // Normalizar se necessário
            if (maxPoints > 0)
            {
                momentumScore = momentumScore / maxPoints;
            }

            return momentumScore;
        }

        private double CalcularScoreVolatilidade()
        {
            double volatilidadeScore = 0;

            if (glitch != null && IsIndicatorValid(glitch, 0))
            {
                double zScore = glitch.ZScore[0];
                double distanciaBanda = Math.Max(-1.0, Math.Min(1.0, zScore / 3.0));
                volatilidadeScore += distanciaBanda * 0.5;
            }

            // CORREÇÃO: Verificar se glitch tem dados antes de usar
            if (glitch != null && glitch.Count > 0 && IsIndicatorValid(glitch, 0) && Close[0] > 0)
            {
                double bandwidth = glitch.BandWidth[0];
                double sigma = bandwidth / 2.0; // sigma ≈ ATR normalizado
                double sigmaPercent = sigma / Close[0] * 100;

                if (sigmaPercent >= 0.5 && sigmaPercent <= 2.0)
                {
                    volatilidadeScore += 0.3;
                }
                else if (sigmaPercent > 2.0)
                {
                    volatilidadeScore -= 0.2;
                }
            }

            return volatilidadeScore;
        }
        #endregion

        #region Machine Learning Básico
        private void ArmazenarScoreParaML(double scoreTendencia, double scoreMomentum,
                                         double scoreVolume, double scoreVolatilidade, double scoreBreakout)
        {
            try
            {
                // Armazenar apenas quando há trade ativo
                if (Position == null || Position.MarketPosition == MarketPosition.Flat)
                    return;

                // Criar registro após trade ser fechado
                if (!tradeRecords.ContainsKey(tradeCounter))
                {
                    var tradeRecord = new TradeRecord
                    {
                        TradeId = tradeCounter,
                        EntryTime = DateTime.Now,
                        EntryPrice = entryPrice,
                        Direction = tradeDirection,
                        IndicatorValues = new Dictionary<string, double>(),
                        ScoreContributions = new Dictionary<string, double>()
                    };

                    tradeRecord.ScoreContributions["Tendencia"] = scoreTendencia;
                    tradeRecord.ScoreContributions["Momentum"] = scoreMomentum;
                    tradeRecord.ScoreContributions["Volume"] = scoreVolume;
                    tradeRecord.ScoreContributions["Volatilidade"] = scoreVolatilidade;
                    tradeRecord.ScoreContributions["Breakout"] = scoreBreakout;

                    tradeRecords[tradeCounter] = tradeRecord;
                }
            }
            catch (Exception ex)
            {
                LogError("ArmazenarScoreParaML", ex);
            }
        }

        private void AtualizarPerformanceML(int tradeId, double pnl, bool isWinner)
        {
            try
            {
                if (!AtivarMLBasico || !tradeRecords.ContainsKey(tradeId))
                    return;

                var tradeRecord = tradeRecords[tradeId];
                tradeRecord.ExitTime = DateTime.Now;
                tradeRecord.ExitPrice = GetCurrentMarketPrice();
                tradeRecord.PnL = pnl;

                // Atualizar performance por indicador
                foreach (var contribution in tradeRecord.ScoreContributions)
                {
                    string indicatorName = contribution.Key;
                    double contributionValue = contribution.Value;

                    if (indicatorPerformance.ContainsKey(indicatorName))
                    {
                        var perf = indicatorPerformance[indicatorName];
                        perf.TotalTrades++;

                        if (isWinner)
                        {
                            perf.WinningTrades++;
                            perf.TotalContribution += Math.Abs(contributionValue);
                        }
                        else
                        {
                            perf.TotalContribution -= Math.Abs(contributionValue);
                        }

                        perf.LastUpdate = DateTime.Now;
                    }
                }

                totalTradesColetados++;

                // Verificar se é hora de ajustar pesos
                if (AjusteAutomaticoPesos && totalTradesColetados >= MIN_TRADES_TREINAMENTO &&
                    totalTradesColetados % IntervaloAtualizacaoML == 0)
                {
                    AjustarPesosAutomaticamente();
                }

                LogMLStats();
            }
            catch (Exception ex)
            {
                LogError("AtualizarPerformanceML", ex);
            }
        }

        private void AjustarPesosAutomaticamente()
        {
            try
            {
                LogInfo("ML", "🤖 Iniciando ajuste automático de pesos...");

                double totalWinRate = 0;
                int indicatorsWithTrades = 0;

                // Calcular win rate total
                foreach (var perf in indicatorPerformance.Values)
                {
                    if (perf.TotalTrades > MIN_TRADES_TREINAMENTO / 5)
                    {
                        totalWinRate += perf.WinRate;
                        indicatorsWithTrades++;
                    }
                }

                if (indicatorsWithTrades == 0) return;

                double avgWinRate = totalWinRate / indicatorsWithTrades;

                // Ajustar pesos baseado no desempenho relativo
                foreach (var perf in indicatorPerformance.Values)
                {
                    if (perf.TotalTrades > MIN_TRADES_TREINAMENTO / 5)
                    {
                        double performanceRatio = perf.WinRate / avgWinRate;
                        double adjustment = (performanceRatio - 1.0) * MaxAjustePeso;

                        // Limitar ajuste
                        adjustment = Math.Max(-MaxAjustePeso, Math.Min(MaxAjustePeso, adjustment));

                        perf.AdjustedWeight = perf.CurrentWeight * (1 + adjustment);
                        perf.AdjustedWeight = Math.Max(0.1, Math.Min(3.0, perf.AdjustedWeight));
                    }
                    else
                    {
                        perf.AdjustedWeight = perf.CurrentWeight;
                    }
                }

                // Aplicar pesos ajustados
                if (AjusteAutomaticoPesos)
                {
                    PesoTendencia = (float)indicatorPerformance["Tendencia"].AdjustedWeight;
                    PesoMomentum = (float)indicatorPerformance["Momentum"].AdjustedWeight;
                    PesoVolume = (float)indicatorPerformance["Volume"].AdjustedWeight;
                    PesoVolatilidade = (float)indicatorPerformance["Volatilidade"].AdjustedWeight;
                    PesoBreakout = (float)indicatorPerformance["Breakout"].AdjustedWeight;

                    LogInfo("ML",
                        $"✅ Pesos ajustados:\n" +
                        $"Tendência: {PesoTendencia:F2}\n" +
                        $"Momentum: {PesoMomentum:F2}\n" +
                        $"Volume: {PesoVolume:F2}\n" +
                        $"Volatilidade: {PesoVolatilidade:F2}\n" +
                        $"Breakout: {PesoBreakout:F2}");
                    
                    // Salvar aprendizado imediatamente após ajuste de pesos
                    SalvarAprendizadoEmArquivo();
                }
            }
            catch (Exception ex)
            {
                LogError("AjustarPesosAutomaticamente", ex);
            }
        }

        private void SalvarEstatisticasML()
        {
            try
            {
                StringBuilder stats = new StringBuilder();
                stats.AppendLine("=== ESTATÍSTICAS ML BÁSICO ===");
                stats.AppendLine($"Total de trades analisados: {totalTradesColetados}");
                stats.AppendLine($"Data: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                stats.AppendLine();

                foreach (var perf in indicatorPerformance.Values.OrderByDescending(p => p.WinRate))
                {
                    stats.AppendLine($"{perf.IndicatorName}:");
                    stats.AppendLine($"  Trades: {perf.TotalTrades}");
                    stats.AppendLine($"  Win Rate: {perf.WinRate:P1}");
                    stats.AppendLine($"  Contribuição média: {perf.AverageContribution:F3}");
                    stats.AppendLine($"  Peso atual: {perf.CurrentWeight:F2}");
                    stats.AppendLine($"  Peso ajustado: {perf.AdjustedWeight:F2}");
                    stats.AppendLine();
                }

                LogInfo("ML Stats Final", stats.ToString());
            }
            catch (Exception ex)
            {
                LogError("SalvarEstatisticasML", ex);
            }
        }

        /// <summary>
        /// Obtém o caminho completo do arquivo de aprendizado
        /// </summary>
        private string ObterCaminhoArquivoAprendizado()
        {
            try
            {
                // Usar a pasta Documents/NinjaTrader 8 como base
                string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string ninjaTraderPath = Path.Combine(documentsPath, "NinjaTrader 8");
                string mlDataPath = Path.Combine(ninjaTraderPath, "ML_Data");
                
                // Criar diretório se não existir
                if (!Directory.Exists(mlDataPath))
                {
                    Directory.CreateDirectory(mlDataPath);
                }
                
                return Path.Combine(mlDataPath, NOME_ARQUIVO_APRENDIZADO);
            }
            catch (Exception ex)
            {
                LogError("ObterCaminhoArquivoAprendizado", ex);
                // Fallback: usar diretório temporário
                return Path.Combine(Path.GetTempPath(), NOME_ARQUIVO_APRENDIZADO);
            }
        }

        /// <summary>
        /// Salva os dados de aprendizado em arquivo para persistência entre sessões
        /// </summary>
        private void SalvarAprendizadoEmArquivo()
        {
            try
            {
                if (!AtivarMLBasico || indicatorPerformance == null || indicatorPerformance.Count == 0)
                {
                    LogInfo("ML Persistência", "Nenhum dado de aprendizado para salvar");
                    return;
                }

                string arquivoPath = ObterCaminhoArquivoAprendizado();
                StringBuilder dados = new StringBuilder();
                
                // Cabeçalho
                dados.AppendLine("# BotSwiftAlgoStrategyV4 - Dados de Aprendizado ML");
                dados.AppendLine($"# Gerado em: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                dados.AppendLine($"# Total de trades coletados: {totalTradesColetados}");
                dados.AppendLine();
                
                // Salvar performance de cada indicador
                dados.AppendLine("[INDICATOR_PERFORMANCE]");
                foreach (var perf in indicatorPerformance.Values)
                {
                    dados.AppendLine($"INDICATOR={perf.IndicatorName}");
                    dados.AppendLine($"TOTAL_TRADES={perf.TotalTrades}");
                    dados.AppendLine($"WINNING_TRADES={perf.WinningTrades}");
                    dados.AppendLine($"TOTAL_CONTRIBUTION={perf.TotalContribution:F6}");
                    dados.AppendLine($"CURRENT_WEIGHT={perf.CurrentWeight:F6}");
                    dados.AppendLine($"ADJUSTED_WEIGHT={perf.AdjustedWeight:F6}");
                    dados.AppendLine($"LAST_UPDATE={perf.LastUpdate:yyyy-MM-dd HH:mm:ss}");
                    dados.AppendLine("---");
                }
                
                // Salvar total de trades coletados
                dados.AppendLine();
                dados.AppendLine("[METADATA]");
                dados.AppendLine($"TOTAL_TRADES_COLETADOS={totalTradesColetados}");
                dados.AppendLine($"ULTIMA_ATUALIZACAO={DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                
                // Escrever arquivo
                File.WriteAllText(arquivoPath, dados.ToString(), Encoding.UTF8);
                
                LogInfo("ML Persistência", 
                    $"✅ Dados de aprendizado salvos com sucesso!\n" +
                    $"   Arquivo: {arquivoPath}\n" +
                    $"   Indicadores: {indicatorPerformance.Count}\n" +
                    $"   Total trades: {totalTradesColetados}");
            }
            catch (Exception ex)
            {
                LogError("SalvarAprendizadoEmArquivo", ex);
            }
        }

        /// <summary>
        /// Carrega os dados de aprendizado do arquivo para continuar aprendendo entre sessões
        /// </summary>
        private void CarregarAprendizadoDeArquivo()
        {
            try
            {
                if (!AtivarMLBasico)
                {
                    return;
                }

                string arquivoPath = ObterCaminhoArquivoAprendizado();
                
                if (!File.Exists(arquivoPath))
                {
                    LogInfo("ML Persistência", "Arquivo de aprendizado não encontrado - iniciando aprendizado do zero");
                    return;
                }

                string[] linhas = File.ReadAllLines(arquivoPath, Encoding.UTF8);
                bool emSecaoPerformance = false;
                string indicadorAtual = null;
                int indicadoresCarregados = 0;
                int tradesCarregados = 0;

                foreach (string linha in linhas)
                {
                    // Ignorar comentários e linhas vazias
                    if (string.IsNullOrWhiteSpace(linha) || linha.TrimStart().StartsWith("#"))
                        continue;

                    // Detectar seção
                    if (linha.Trim() == "[INDICATOR_PERFORMANCE]")
                    {
                        emSecaoPerformance = true;
                        continue;
                    }
                    else if (linha.Trim() == "[METADATA]")
                    {
                        emSecaoPerformance = false;
                        continue;
                    }

                    if (emSecaoPerformance)
                    {
                        if (linha.StartsWith("INDICATOR="))
                        {
                            indicadorAtual = linha.Substring("INDICATOR=".Length).Trim();
                        }
                        else if (linha == "---")
                        {
                            // Finalizar indicador atual
                            indicadorAtual = null;
                        }
                        else if (!string.IsNullOrEmpty(indicadorAtual) && indicatorPerformance.ContainsKey(indicadorAtual))
                        {
                            var perf = indicatorPerformance[indicadorAtual];
                            
                            if (linha.StartsWith("TOTAL_TRADES="))
                            {
                                if (int.TryParse(linha.Substring("TOTAL_TRADES=".Length).Trim(), out int total))
                                    perf.TotalTrades = total;
                            }
                            else if (linha.StartsWith("WINNING_TRADES="))
                            {
                                if (int.TryParse(linha.Substring("WINNING_TRADES=".Length).Trim(), out int wins))
                                    perf.WinningTrades = wins;
                            }
                            else if (linha.StartsWith("TOTAL_CONTRIBUTION="))
                            {
                                if (double.TryParse(linha.Substring("TOTAL_CONTRIBUTION=".Length).Trim(), out double contrib))
                                    perf.TotalContribution = contrib;
                            }
                            else if (linha.StartsWith("CURRENT_WEIGHT="))
                            {
                                if (double.TryParse(linha.Substring("CURRENT_WEIGHT=".Length).Trim(), out double peso))
                                    perf.CurrentWeight = peso;
                            }
                            else if (linha.StartsWith("ADJUSTED_WEIGHT="))
                            {
                                if (double.TryParse(linha.Substring("ADJUSTED_WEIGHT=".Length).Trim(), out double pesoAjustado))
                                {
                                    perf.AdjustedWeight = pesoAjustado;
                                    indicadoresCarregados++;
                                }
                            }
                            else if (linha.StartsWith("LAST_UPDATE="))
                            {
                                if (DateTime.TryParse(linha.Substring("LAST_UPDATE=".Length).Trim(), out DateTime lastUpdate))
                                    perf.LastUpdate = lastUpdate;
                            }
                        }
                    }
                    else if (linha.StartsWith("TOTAL_TRADES_COLETADOS="))
                    {
                        if (int.TryParse(linha.Substring("TOTAL_TRADES_COLETADOS=".Length).Trim(), out int total))
                        {
                            totalTradesColetados = total;
                            tradesCarregados = total;
                        }
                    }
                }

                // Aplicar pesos ajustados carregados aos pesos da estratégia
                if (indicadoresCarregados > 0)
                {
                    PesoTendencia = (float)indicatorPerformance["Tendencia"].AdjustedWeight;
                    PesoMomentum = (float)indicatorPerformance["Momentum"].AdjustedWeight;
                    PesoVolume = (float)indicatorPerformance["Volume"].AdjustedWeight;
                    PesoVolatilidade = (float)indicatorPerformance["Volatilidade"].AdjustedWeight;
                    PesoBreakout = (float)indicatorPerformance["Breakout"].AdjustedWeight;

                    LogInfo("ML Persistência",
                        $"✅ Dados de aprendizado carregados com sucesso!\n" +
                        $"   Arquivo: {arquivoPath}\n" +
                        $"   Indicadores carregados: {indicadoresCarregados}\n" +
                        $"   Total trades anteriores: {tradesCarregados}\n" +
                        $"   Pesos aplicados:\n" +
                        $"     Tendência: {PesoTendencia:F2}\n" +
                        $"     Momentum: {PesoMomentum:F2}\n" +
                        $"     Volume: {PesoVolume:F2}\n" +
                        $"     Volatilidade: {PesoVolatilidade:F2}\n" +
                        $"     Breakout: {PesoBreakout:F2}");
                }
                else
                {
                    LogInfo("ML Persistência", "Arquivo encontrado mas nenhum dado válido carregado");
                }
            }
            catch (Exception ex)
            {
                LogError("CarregarAprendizadoDeArquivo", ex);
                LogInfo("ML Persistência", "Erro ao carregar aprendizado - continuando do zero");
            }
        }
        #endregion

        #region Sistema de Saída Inteligente
        private void VerificarSaidaInteligente()
        {
            if (!UsarSaidaInteligente || Position == null || Position.MarketPosition == MarketPosition.Flat)
                return;

            try
            {
                DateTime now = DateTime.Now;

                // Verificar apenas a cada 30 segundos
                if ((now - ultimaVerificacaoSaida).TotalSeconds < 30)
                    return;

                ultimaVerificacaoSaida = now;

                // 1. Detecção de Reversão de Momentum
                if (DetectarReversaoMomentum)
                {
                    if (VerificarReversaoMomentum())
                    {
                        reversaoMomentumDetectada = true;
                        ExecutarSaidaParcial("Reversão de momentum detectada");
                    }
                }

                // 2. Volume Extremo (possível reversão)
                if (Volume[0] > volumeProfile.GetVolumePercentile(VolumeExtremoPercentil))
                {
                    LogInfo("Saída", $"⚠️ Volume extremo detectado: {Volume[0]:N0}");

                    // Se volume extremo contra a posição, considerar saída
                    if ((tradeDirection == MarketPosition.Long && Close[0] < Open[0]) ||
                        (tradeDirection == MarketPosition.Short && Close[0] > Open[0]))
                    {
                        ExecutarSaidaParcial("Volume extremo contra a posição");
                    }
                }

                // CORREÇÃO: Verificar se glitch tem dados antes de usar
                if (glitch != null && glitch.Count > 0 && IsIndicatorValid(glitch, 0))
                {
                    int regime = (int)glitch.Regime[0];
                    double confidence = glitch.ConfidenceSmoothed[0];
                    if ((regime == -1 || confidence < 60) && Position.Quantity > Block1Quantity)
                    {
                        ExecutarSaidaParcial($"Tendência fraca (Regime: {regime}, Conf: {confidence:F1})");
                    }
                }
            }
            catch (Exception ex)
            {
                LogError("VerificarSaidaInteligente", ex);
            }
        }

        private bool VerificarReversaoMomentum()
        {
            try
            {
                if (!IsValidDataPoint(3)) return false;

                // Coletar dados de momentum recentes
                momentumHistory.Add(totalScore);
                if (momentumHistory.Count > 10)
                    momentumHistory.RemoveAt(0);

                if (momentumHistory.Count < 5) return false;

                // Verificar mudança significativa no momentum
                double momentumAtual = momentumHistory.Last();
                double momentumMedio = momentumHistory.Average();
                double desvioPadrao = CalcularDesvioPadrao(momentumHistory);

                // Se o momentum atual diverge significativamente da média
                if (Math.Abs(momentumAtual - momentumMedio) > desvioPadrao * 2)
                {
                    // Verificar se a divergência é contra a posição atual
                    if ((tradeDirection == MarketPosition.Long && momentumAtual < -1) ||
                        (tradeDirection == MarketPosition.Short && momentumAtual > 1))
                    {
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                LogError("VerificarReversaoMomentum", ex);
                return false;
            }
        }

        private double CalcularDesvioPadrao(List<double> values)
        {
            if (values.Count < 2) return 0;

            double mean = values.Average();
            double sumSquares = values.Sum(v => Math.Pow(v - mean, 2));
            return Math.Sqrt(sumSquares / (values.Count - 1));
        }

        private void ExecutarSaidaParcial(string motivo)
        {
            try
            {
                if (Position == null || Position.MarketPosition == MarketPosition.Flat)
                    return;

                int quantidadeAtual = Position.Quantity;
                if (quantidadeAtual <= Block1Quantity) return;

                // Calcular quantidade para saída parcial
                int quantidadeSaida = (int)(quantidadeAtual * PercentualSaidaParcial);
                quantidadeSaida = Math.Max(1, Math.Min(quantidadeAtual - 1, quantidadeSaida));

                // Executar saída parcial
                if (tradeDirection == MarketPosition.Long)
                {
                    ExitLong(quantidadeSaida, $"SaidaParcial_{tradeCounter}", $"SaidaParcial_{tradeCounter}");
                }
                else if (tradeDirection == MarketPosition.Short)
                {
                    ExitShort(quantidadeSaida, $"SaidaParcial_{tradeCounter}", $"SaidaParcial_{tradeCounter}");
                }

                LogTrade("Saída Inteligente",
                    $"🔄 Saída parcial executada: {quantidadeSaida} contratos\n" +
                    $"Motivo: {motivo}\n" +
                    $"Quantidade restante: {quantidadeAtual - quantidadeSaida}");
            }
            catch (Exception ex)
            {
                LogError("ExecutarSaidaParcial", ex);
            }
        }
        #endregion

        #region Análise de Correlação
        private void AnalisarCorrelacao()
        {
            if (!AtivarAnaliseCorrelacao) return;

            try
            {
                DateTime now = DateTime.Now;

                // Analisar a cada 30 minutos
                if ((now - lastCorrelationAnalysis).TotalMinutes < INTERVALO_ANALISE_CORRELACAO)
                    return;

                lastCorrelationAnalysis = now;

                if (tradeRecords.Count < 10)
                {
                    LogInfo("Correlação", "Dados insuficientes para análise de correlação");
                    return;
                }

                // Coletar dados para análise
                var tendenciaScores = new List<double>();
                var momentumScores = new List<double>();
                var volumeScores = new List<double>();
                var volatilidadeScores = new List<double>();
                var breakoutScores = new List<double>();
                var pnls = new List<double>();

                foreach (var record in tradeRecords.Values)
                {
                    if (record.ScoreContributions.ContainsKey("Tendencia"))
                        tendenciaScores.Add(record.ScoreContributions["Tendencia"]);
                    if (record.ScoreContributions.ContainsKey("Momentum"))
                        momentumScores.Add(record.ScoreContributions["Momentum"]);
                    if (record.ScoreContributions.ContainsKey("Volume"))
                        volumeScores.Add(record.ScoreContributions["Volume"]);
                    if (record.ScoreContributions.ContainsKey("Volatilidade"))
                        volatilidadeScores.Add(record.ScoreContributions["Volatilidade"]);
                    if (record.ScoreContributions.ContainsKey("Breakout"))
                        breakoutScores.Add(record.ScoreContributions["Breakout"]);

                    pnls.Add(record.PnL);
                }

                // Calcular correlações
                double correlacaoTendencia = CalcularCorrelacao(tendenciaScores, pnls);
                double correlacaoMomentum = CalcularCorrelacao(momentumScores, pnls);
                double correlacaoVolume = CalcularCorrelacao(volumeScores, pnls);
                double correlacaoVolatilidade = CalcularCorrelacao(volatilidadeScores, pnls);
                double correlacaoBreakout = CalcularCorrelacao(breakoutScores, pnls);

                // Logar resultados
                LogInfo("Análise Correlação V3",
                    $"📊 CORRELAÇÃO COM PnL (Base: {tradeRecords.Count} trades):\n" +
                    $"Tendência: {correlacaoTendencia:F3}\n" +
                    $"Momentum: {correlacaoMomentum:F3}\n" +
                    $"Volume: {correlacaoVolume:F3}\n" +
                    $"Volatilidade: {correlacaoVolatilidade:F3}\n" +
                    $"Breakout: {correlacaoBreakout:F3}");

                // Sugerir ajustes baseados na correlação
                SugerirAjustesPesos(correlacaoTendencia, correlacaoMomentum, correlacaoVolume,
                                   correlacaoVolatilidade, correlacaoBreakout);
            }
            catch (Exception ex)
            {
                LogError("AnalisarCorrelacao", ex);
            }
        }

        private double CalcularCorrelacao(List<double> x, List<double> y)
        {
            if (x.Count != y.Count || x.Count < 2) return 0;

            double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0, sumY2 = 0;
            int n = x.Count;

            for (int i = 0; i < n; i++)
            {
                sumX += x[i];
                sumY += y[i];
                sumXY += x[i] * y[i];
                sumX2 += x[i] * x[i];
                sumY2 += y[i] * y[i];
            }

            double numerator = n * sumXY - sumX * sumY;
            double denominator = Math.Sqrt((n * sumX2 - sumX * sumX) * (n * sumY2 - sumY * sumY));

            if (denominator == 0) return 0;

            return numerator / denominator;
        }

        private void SugerirAjustesPesos(double correlTendencia, double correlMomentum,
                                       double correlVolume, double correlVolatilidade, double correlBreakout)
        {
            try
            {
                // Normalizar correlações (valores absolutos)
                double[] correlacoes = { Math.Abs(correlTendencia), Math.Abs(correlMomentum),
                                       Math.Abs(correlVolume), Math.Abs(correlVolatilidade),
                                       Math.Abs(correlBreakout) };

                double maxCorrel = correlacoes.Max();
                if (maxCorrel == 0) return;

                // Calcular proporções
                double propTendencia = Math.Abs(correlTendencia) / maxCorrel;
                double propMomentum = Math.Abs(correlMomentum) / maxCorrel;
                double propVolume = Math.Abs(correlVolume) / maxCorrel;
                double propVolatilidade = Math.Abs(correlVolatilidade) / maxCorrel;
                double propBreakout = Math.Abs(correlBreakout) / maxCorrel;

                LogInfo("Sugestões Peso V3",
                    $"🤖 SUGESTÕES baseadas em correlação:\n" +
                    $"Tendência: {propTendencia:P0} do peso máximo\n" +
                    $"Momentum: {propMomentum:P0}\n" +
                    $"Volume: {propVolume:P0}\n" +
                    $"Volatilidade: {propVolatilidade:P0}\n" +
                    $"Breakout: {propBreakout:P0}");
            }
            catch (Exception ex)
            {
                LogError("SugerirAjustesPesos", ex);
            }
        }
        #endregion

        #region MÉTODO PRINCIPAL TICK-BY-TICK
        protected override void OnMarketData(MarketDataEventArgs marketDataUpdate)
        {
            try
            {
                if (marketDataUpdate.MarketDataType != MarketDataType.Last)
                    return;

                double currentTickPrice = marketDataUpdate.Price;
                lastProcessedTickPrice = currentTickPrice;
                lastTickTime = marketDataUpdate.Time;

                // CORREÇÃO: Não controlar isProcessingTrade aqui - ProcessarEntradaTrade já controla
                // A flag é gerenciada apenas quando há processamento de entrada de trade
                ProcessTickByTickLogic(currentTickPrice);
            }
            catch (Exception ex)
            {
                LogError("OnMarketData", ex);
                // CORREÇÃO: Não resetar flag aqui - ProcessarEntradaTrade controla a flag
                // Apenas resetar se realmente necessário (erro crítico)
            }
        }

        /// <summary>
        /// Processa lógica de trading em modo histórico (backtest)
        /// </summary>
        private void ProcessHistoricalTradingLogic()
        {
            try
            {
                // Log diagnóstico a cada 100 barras
                if (CurrentBar % 100 == 0)
                {
                    LogInfo("Backtest Status", 
                        $"Bar: {CurrentBar}, Ready: {IsReadyForTrading()}, " +
                        $"Initialized: {strategyInitialized}, IndicatorsReady: {indicatorsReady}, " +
                        $"BarsRequired: {BarsRequiredToTrade}, Position: {Position?.MarketPosition ?? MarketPosition.Flat}");
                }

                // Verificar se está pronto para trading
                if (!IsReadyForTrading())
                {
                    return;
                }

                if (!CheckDailyLimits())
                {
                    if (CurrentBar % 100 == 0)
                    {
                        LogInfo("Backtest Debug", $"⏳ Limites diários bloqueando - Trades hoje: {tradesToday}/{MaxTradesPerDay}");
                    }
                    return;
                }

                // CORREÇÃO: Verificar confirmação multi-timeframe foi movido para dentro de ProcessarDecisaoEntrada()
                // para que possa verificar tradeDirection após ser definido

                // Calcular score com todas as funcionalidades
                CalcularSistemaScoring();

                // Processar decisão de entrada
                ProcessarDecisaoEntrada();

                // Verificação para setar stops se necessário
                if (Position != null && Position.MarketPosition != MarketPosition.Flat && !stopsSet)
                {
                    CheckAndSetStopsIfNeeded();
                }

                // Sistema de saída inteligente
                VerificarSaidaInteligente();

                // Análise de correlação periódica
                AnalisarCorrelacao();
            }
            catch (Exception ex)
            {
                LogError("ProcessHistoricalTradingLogic", ex);
            }
        }

        private void ProcessTickByTickLogic(double currentTickPrice)
        {
            // Atualizar sessão de trading (apenas em Realtime)
            if (State == State.Realtime && AtivarHorarioFuncionamento)
            {
                CheckAndUpdateTradingSession();
            }

            // Verificar limites de PnL (apenas em Realtime)
            if (State == State.Realtime && AtivarLimitesPnL)
            {
                CheckPnLLimits();
            }

            // Verificar se está pronto para trading
            if (!IsReadyForTrading())
            {
                if (EnableReadyLogging && (DateTime.Now - lastReadyCheck).TotalSeconds >= 10)
                {
                    LogInfo("Aguardando",
                        $"⌛ Aguardando condições: Ready={IsReadyForTrading()}, State={State}, CurrentBar={CurrentBar}, " +
                        $"Limites={(limitsActive ? "OK" : "Bloqueado")}, Sessão={(sessionActive ? "OK" : "Inativa")}");
                    lastReadyCheck = DateTime.Now;
                }
                return;
            }

            if (!CheckDailyLimits())
                return;

            // CORREÇÃO: HasMinimumTimePassed() agora é verificado em ProcessarDecisaoEntrada()
            // para garantir que lastTradeTime seja atualizado quando o sinal é gerado

            // CORREÇÃO: Verificar confirmação multi-timeframe foi movido para dentro de ProcessarDecisaoEntrada()
            // para que possa verificar tradeDirection após ser definido

            // Calcular score com todas as funcionalidades
            CalcularSistemaScoring();

            // Processar decisão de entrada
            ProcessarDecisaoEntrada();

            // Gerenciamento de stops TICK-BY-TICK (apenas em Realtime)
            if (State == State.Realtime && Position != null && Position.MarketPosition != MarketPosition.Flat && stopsSet)
            {
                CheckAndMoveStopsTickByTick(currentTickPrice);
            }

            // Verificação para setar stops se necessário
            if (Position != null && Position.MarketPosition != MarketPosition.Flat && !stopsSet)
            {
                CheckAndSetStopsIfNeeded();
            }

            // Sistema de saída inteligente
            VerificarSaidaInteligente();

            // Análise de correlação periódica
            AnalisarCorrelacao();

            CleanupExpiredOrders();
            SendHeartbeat();
        }

        private void ProcessarDecisaoEntrada()
        {
            longSignal = false;
            shortSignal = false;

            // Log diagnóstico em backtest - MAIS FREQUENTE para debug
            if (State == State.Historical && CurrentBar % 50 == 0 && CurrentBar > 100)
            {
                double thresholdLong = CalcularThresholdDinamicoComQualidade();
                double thresholdShort = UsarThresholdsDinamicos ? shortThresholdDynamic : ShortThreshold;
                
                LogInfo("🔍 DIAGNÓSTICO ENTRADA", 
                    $"Bar: {CurrentBar}, Score: {totalScore:F2}\n" +
                    $"Thresholds: Long>={thresholdLong:F2}, Short<={thresholdShort:F2}\n" +
                    $"Position: {Position?.MarketPosition ?? MarketPosition.Flat}\n" +
                    $"isProcessingTrade: {isProcessingTrade}\n" +
                    $"indicatorsReady: {indicatorsReady}\n" +
                    $"strategyInitialized: {strategyInitialized}\n" +
                    $"ReadyForTrading: {IsReadyForTrading()}\n" +
                    $"DailyLimits OK: {CheckDailyLimits()}\n" +
                    $"MinTimePassed: {HasMinimumTimePassed()}\n" +
                    $"Trades hoje: {tradesToday}/{MaxTradesPerDay}");
            }

            // CORREÇÃO: Verificar se já está processando um trade
            lock (tradeLock)
            {
                if (isProcessingTrade)
                {
                    // CORREÇÃO: Verificar timeout - se flag está true mas não há ordens pendentes nem posição,
                    // resetar a flag (pode ter ficado travada)
                    bool hasPendingOrders = false;
                    if (Position == null || Position.MarketPosition == MarketPosition.Flat)
                    {
                        var ordersArray = Orders.ToArray();
                        foreach (var order in ordersArray)
                        {
                            if (order != null && 
                                order.Name.Contains("Block") &&
                                order.Name.Contains(tradeCounter.ToString()) &&
                                (order.OrderState == OrderState.Accepted ||
                                 order.OrderState == OrderState.Working ||
                                 order.OrderState == OrderState.Submitted))
                            {
                                hasPendingOrders = true;
                                break;
                            }
                        }
                        
                        // Se não há ordens pendentes e flag está true há mais de 60 segundos, resetar
                        if (!hasPendingOrders)
                        {
                            // Verificar se há ordens antigas no orderSubmissionTimes
                            bool hasOldOrders = false;
                            foreach (var kvp in orderSubmissionTimes)
                            {
                                if (kvp.Key.Contains("Block") && kvp.Key.Contains(tradeCounter.ToString()))
                                {
                                    if ((DateTime.Now - kvp.Value).TotalSeconds < ORDER_TIMEOUT_SECONDS * 2)
                                    {
                                        hasOldOrders = true;
                                        break;
                                    }
                                }
                            }
                            
                            if (!hasOldOrders)
                            {
                                // Flag travada - resetar
                                isProcessingTrade = false;
                                LogWarning("Entrada", "Flag isProcessingTrade estava travada - Resetada automaticamente");
                            }
                        }
                    }
                    
                    if (isProcessingTrade)
                    {
                        if (EnableReadyLogging && (DateTime.Now - lastReadyCheck).TotalSeconds >= 5)
                        {
                            LogInfo("Entrada", "⏳ Trade já em processamento, aguardando...");
                            lastReadyCheck = DateTime.Now;
                        }
                        return;
                    }
                }
            }

            // CORREÇÃO: Verificar se já existe posição ativa
            if (Position != null && Position.MarketPosition != MarketPosition.Flat)
            {
                return; // Já existe posição, não processar novo sinal
            }

            // CORREÇÃO CRÍTICA: Se MinTimeBetweenTrades = 0, não verificar tempo mínimo
            // Isso elimina bloqueios desnecessários quando não há restrição de tempo
            if (MinTimeBetweenTrades > 0 && !HasMinimumTimePassed())
            {
                DateTime currentTime = (State == State.Historical && CurrentBar >= 0) ? Time[0] : DateTime.Now;
                TimeSpan timeSinceLastTrade = currentTime - lastTradeTime;
                if (EnableReadyLogging && (currentTime - lastReadyCheck).TotalSeconds >= 5)
                {
                    LogInfo("Tempo Mínimo", 
                        $"⏳ Aguardando tempo mínimo: {timeSinceLastTrade.TotalMinutes:F1}/{MinTimeBetweenTrades} min");
                    lastReadyCheck = currentTime;
                }
                return;
            }

            if (Position != null && Position.MarketPosition == MarketPosition.Flat)
            {
                ResetTradeState();

                // V4: Calcular threshold dinâmico com qualidade
                double thresholdLong = CalcularThresholdDinamicoComQualidade();
                double thresholdShort = UsarThresholdsDinamicos ? shortThresholdDynamic : ShortThreshold;
                
                // CORREÇÃO: Ajuste mais permissivo para aumentar número de trades
                // Ajustar threshold Short também baseado em qualidade
                if (UsarThresholdsDinamicos)
                {
                    int qualidadeScore = CalcularScoreQualidade();
                    if (qualidadeScore >= 6)
                        thresholdShort = shortThresholdDynamic * 0.85; // 15% mais permissivo (aumentado de 10%)
                    else if (qualidadeScore < 4)
                        thresholdShort = shortThresholdDynamic * 1.1; // Apenas 10% mais rigoroso (reduzido de 20%)
                }

                // Log diagnóstico em backtest quando score está próximo do threshold OU quando não há trades
                if (State == State.Historical && (Math.Abs(totalScore - thresholdLong) < 0.5 || Math.Abs(totalScore - thresholdShort) < 0.5 || CurrentBar % 100 == 0))
                {
                    bool scoreLongOK = totalScore >= thresholdLong;
                    bool scoreShortOK = totalScore <= thresholdShort;
                    
                    LogInfo("📊 SCORE vs THRESHOLDS", 
                        $"Bar: {CurrentBar}, Score: {totalScore:F2}\n" +
                        $"Threshold Long: {thresholdLong:F2} → Score OK: {scoreLongOK}\n" +
                        $"Threshold Short: {thresholdShort:F2} → Score OK: {scoreShortOK}\n" +
                        $"Position: {Position?.MarketPosition ?? MarketPosition.Flat}\n" +
                        $"Short Trading Ativo: {AtivarShortTrading}");
                }

                if (totalScore >= thresholdLong)
                {
                    longSignal = true;
                    tradeDirection = MarketPosition.Long;
                    
                    // Log em backtest quando sinal é gerado
                    if (State == State.Historical)
                    {
                        LogInfo("Sinal Long Gerado", 
                            $"Bar: {CurrentBar}, Score: {totalScore:F2} >= Threshold: {thresholdLong:F2}");
                    }
                    
                    // CORREÇÃO: Verificar confirmação multi-timeframe APÓS definir tradeDirection
                    if (!VerificarConfirmacaoMultiTimeframe())
                    {
                        if (State == State.Historical)
                        {
                            LogInfo("🚫 BLOQUEIO Multi-Timeframe", 
                                $"⏳ Long bloqueado: tendência de médio prazo não confirma\n" +
                                $"UsarMultiTimeframe: {UsarMultiTimeframe}, RequerirConfirmacao: {RequerirConfirmacaoMultiTimeframe}");
                        }
                        ResetSignals();
                        return;
                    }
                    
                    // V4: VALIDAÇÕES DE QUALIDADE ANTES DE ENTRAR
                    // CORREÇÃO: Adicionar log detalhado ANTES de validar para diagnóstico
                    int qualidadeScoreAntes = CalcularScoreQualidade();
                    if (State == State.Historical)
                    {
                        LogInfo("🔍 DIAGNÓSTICO FILTROS", 
                            $"Bar: {CurrentBar}, Score: {totalScore:F2}, Threshold: {thresholdLong:F2}\n" +
                            $"Qualidade Score: {qualidadeScoreAntes}/10 (Mínimo: 2)\n" +
                            $"Multi-Timeframe: {VerificarConfirmacaoMultiTimeframe()}\n" +
                            $"S/R: {ValidarDistanciaSuporteResistencia()}\n" +
                            $"Volatilidade: {ValidarVolatilidadeIdeal()}\n" +
                            $"Volume: {ValidarVolumeLiquidez()}");
                    }
                    
                    if (!ValidarQualidadeTrade())
                    {
                        if (State == State.Historical)
                        {
                            LogInfo("🚫 BLOQUEIO Qualidade", 
                                $"⏳ Long bloqueado por filtros de qualidade\n" +
                                $"Score Qualidade: {scoreQualidadeTrade}/10 (Mínimo: 2)");
                        }
                        ResetSignals();
                        return;
                    }

                    // CORREÇÃO: Filtro de choppiness desativado temporariamente para permitir mais trades
                    // Comentado para aumentar número de trades em 1min
                    /*
                    if (EstaEmChoppiness())
                    {
                        if (State == State.Historical)
                        {
                            LogInfo("🚫 BLOQUEIO Choppiness", $"⏳ Long bloqueado: mercado em choppiness");
                        }
                        ResetSignals();
                        return;
                    }
                    */

                    // CORREÇÃO CRÍTICA: Filtro IndicadoresAlinhados() REMOVIDO
                    // Os indicadores (EMA, MACD, RSI, Stochastics) já estão incluídos no cálculo do score
                    // O filtro estava criando redundância e bloqueando trades válidos onde o score já havia passado do threshold
                    // Se o score passou do threshold usando esses indicadores, não há necessidade de verificar novamente
                    /*
                    if (!IndicadoresAlinhados())
                    {
                        if (State == State.Historical)
                        {
                            LogInfo("🚫 BLOQUEIO Alinhamento", $"⏳ Long bloqueado: indicadores não alinhados");
                        }
                        ResetSignals();
                        return;
                    }
                    */

                    if (!ValidarDistanciaSuporteResistencia())
                    {
                        if (State == State.Historical)
                        {
                            LogInfo("🚫 BLOQUEIO S/R", $"⏳ Long bloqueado: distância suporte/resistência inválida");
                        }
                        ResetSignals();
                        return;
                    }

                    if (!ValidarVolatilidadeIdeal())
                    {
                        if (State == State.Historical)
                        {
                            LogInfo("🚫 BLOQUEIO Volatilidade", $"⏳ Long bloqueado: volatilidade não ideal");
                        }
                        ResetSignals();
                        return;
                    }

                    if (!ValidarVolumeLiquidez())
                    {
                        if (State == State.Historical)
                        {
                            LogInfo("🚫 BLOQUEIO Volume", $"⏳ Long bloqueado: volume/liquidez insuficiente");
                        }
                        ResetSignals();
                        return;
                    }

                    // Melhoria: Validar momentumHistory antes de usar no log
                    string momentumStr = momentumHistory != null && momentumHistory.Count > 0 
                        ? momentumHistory.Last().ToString("F2") 
                        : "N/A";
                    LogTrade("Sinal",
                        $"✅ LONG confirmado V4! Score: {totalScore:F2} >= {thresholdLong:F2}\n" +
                        $"Qualidade: {scoreQualidadeTrade}/10\n" +
                        $"Breakout: {breakoutDetectado}, Momentum: {momentumStr}");

                    // CORREÇÃO: Atualizar lastTradeTime quando sinal é gerado (usar Time[0] em histórico)
                    if (State == State.Historical && CurrentBar >= 0)
                        lastTradeTime = Time[0];
                    else
                        lastTradeTime = DateTime.Now;
                    
                    ProcessarEntradaTrade();
                }
                else if (AtivarShortTrading && totalScore <= thresholdShort)
                {
                    shortSignal = true;
                    tradeDirection = MarketPosition.Short;
                    
                    // CORREÇÃO: Verificar confirmação multi-timeframe APÓS definir tradeDirection
                    if (!VerificarConfirmacaoMultiTimeframe())
                    {
                        LogInfo("Multi-Timeframe", "⏳ Short bloqueado: tendência de médio prazo não confirma");
                        ResetSignals();
                        return;
                    }
                    
                    // V4: VALIDAÇÕES DE QUALIDADE ANTES DE ENTRAR
                    // CORREÇÃO: Adicionar log detalhado ANTES de validar para diagnóstico
                    int qualidadeScoreAntesShort = CalcularScoreQualidade();
                    if (State == State.Historical)
                    {
                        LogInfo("🔍 DIAGNÓSTICO FILTROS SHORT", 
                            $"Bar: {CurrentBar}, Score: {totalScore:F2}, Threshold: {thresholdShort:F2}\n" +
                            $"Qualidade Score: {qualidadeScoreAntesShort}/10 (Mínimo: 1)\n" +
                            $"Multi-Timeframe: {VerificarConfirmacaoMultiTimeframe()}\n" +
                            $"S/R: {ValidarDistanciaSuporteResistencia()}\n" +
                            $"Volatilidade: {ValidarVolatilidadeIdeal()}\n" +
                            $"Volume: {ValidarVolumeLiquidez()}");
                    }
                    
                    if (!ValidarQualidadeTrade())
                    {
                        LogInfo("Filtro V4", 
                            $"⏳ Short bloqueado por filtros de qualidade\n" +
                            $"Score Qualidade: {scoreQualidadeTrade}/10 (Mínimo: 1)");
                        ResetSignals();
                        return;
                    }

                    // CORREÇÃO: Filtro de choppiness desativado temporariamente para permitir mais trades
                    // Comentado para aumentar número de trades em 1min
                    /*
                    if (EstaEmChoppiness())
                    {
                        LogInfo("Filtro V4", $"⏳ Short bloqueado: mercado em choppiness");
                        ResetSignals();
                        return;
                    }
                    */

                    // CORREÇÃO CRÍTICA: Filtro IndicadoresAlinhados() REMOVIDO
                    // Os indicadores (EMA, MACD, RSI, Stochastics) já estão incluídos no cálculo do score
                    // O filtro estava criando redundância e bloqueando trades válidos onde o score já havia passado do threshold
                    /*
                    if (!IndicadoresAlinhados())
                    {
                        LogInfo("Filtro V4", $"⏳ Short bloqueado: indicadores não alinhados");
                        ResetSignals();
                        return;
                    }
                    */

                    if (!ValidarDistanciaSuporteResistencia())
                    {
                        ResetSignals();
                        return;
                    }

                    if (!ValidarVolatilidadeIdeal())
                    {
                        ResetSignals();
                        return;
                    }

                    if (!ValidarVolumeLiquidez())
                    {
                        ResetSignals();
                        return;
                    }

                    // Melhoria: Validar momentumHistory antes de usar no log
                    string momentumStr = momentumHistory != null && momentumHistory.Count > 0 
                        ? momentumHistory.Last().ToString("F2") 
                        : "N/A";
                    LogTrade("Sinal",
                        $"✅ SHORT confirmado V4! Score: {totalScore:F2} <= {thresholdShort:F2}\n" +
                        $"Qualidade: {scoreQualidadeTrade}/10\n" +
                        $"Breakout: {breakoutDetectado}, Momentum: {momentumStr}");

                    // CORREÇÃO: Atualizar lastTradeTime quando sinal é gerado (usar Time[0] em histórico)
                    if (State == State.Historical && CurrentBar >= 0)
                        lastTradeTime = Time[0];
                    else
                        lastTradeTime = DateTime.Now;
                    
                    ProcessarEntradaTrade();
                }
            }
        }
        #endregion

        #region Métodos de Validação e Gerenciamento (V4 com Filtros de Qualidade)

        // CORREÇÃO: Método CheckIndicatorsReady atualizado com diagnóstico melhorado
        private bool CheckIndicatorsReady()
        {
            try
            {
                // Verificar período máximo necessário (V4: incluir SMA50 que precisa de 50 barras)
                // IMPORTANTE: Incluir BarsRequiredToPlot do GlitchIndicator se ele existir
                int glitchBarsRequired = (glitch != null) ? glitch.BarsRequiredToPlot : 0;
                int maxPeriodRequired = Math.Max(
                    Math.Max(Math.Max(EmaTendenciaPeriod, EmaSuperLentaPeriod), EmaLentaPeriod),
                    Math.Max(Math.Max(RsiPeriod, 50), glitchBarsRequired) // Incluir requisitos do GlitchIndicator
                );

                // Em tempo real, CurrentBar pode ser negativo inicialmente
                // Em histórico, CurrentBar começa em 0 e incrementa
                if (State == State.Realtime && CurrentBar < 0)
                {
                    if (EnableReadyLogging && (DateTime.Now - lastReadyCheck).TotalSeconds >= 5)
                    {
                        LogInfo("Indicadores", $"⏳ CurrentBar negativo: {CurrentBar}");
                        lastReadyCheck = DateTime.Now;
                    }
                    return false;
                }

                // Em histórico, CurrentBar pode ser -1 inicialmente, mas depois deve ser >= 0
                if (State == State.Historical && CurrentBar < 0)
                {
                    // Log apenas nas primeiras verificações para não poluir
                    if (EnableReadyLogging && (DateTime.Now - lastReadyCheck).TotalSeconds >= 5)
                    {
                        LogInfo("Indicadores", $"⏳ CurrentBar ainda negativo em histórico: {CurrentBar}");
                        lastReadyCheck = DateTime.Now;
                    }
                    return false;
                }

                if (CurrentBar < maxPeriodRequired)
                {
                    // Log apenas a cada 10 barras para não poluir (nas primeiras 100 barras)
                    // Depois logar apenas periodicamente
                    if ((CurrentBar < 100 && CurrentBar % 10 == 0) || (CurrentBar >= 100 && CurrentBar % 50 == 0 && EnableReadyLogging && (DateTime.Now - lastReadyCheck).TotalSeconds >= 5))
                    {
                        LogInfo("Indicadores",
                            $"⏳ Aguardando barras: {CurrentBar}/{maxPeriodRequired}\n" +
                            $"State: {State}, Bars.Count: {Bars?.Count ?? 0}");
                        lastReadyCheck = DateTime.Now;
                    }
                    return false;
                }

                // Verificar se indicadores foram inicializados
                if (glitch == null || emaRapida == null || emaLenta == null || emaTendencia == null || emaSuperLenta == null ||
                    rsi == null || macd == null)
                {
                    // Log mais frequente nas primeiras barras para debug
                    if (CurrentBar < 100 || (EnableReadyLogging && (DateTime.Now - lastReadyCheck).TotalSeconds >= 5))
                    {
                        string missingIndicators = "";
                        if (glitch == null) missingIndicators += "GlitchIndicator, ";
                        if (emaRapida == null) missingIndicators += "EMA Rápida, ";
                        if (emaLenta == null) missingIndicators += "EMA Lenta, ";
                        if (emaTendencia == null) missingIndicators += "EMA Tendência, ";
                        if (emaSuperLenta == null) missingIndicators += "EMA Super Lenta, ";
                        if (rsi == null) missingIndicators += "RSI, ";
                        if (macd == null) missingIndicators += "MACD, ";
                        
                        LogInfo("🚫 Indicadores Não Inicializados",
                            $"⏳ Indicadores não inicializados: {missingIndicators.TrimEnd(',', ' ')}\n" +
                            $"CurrentBar: {CurrentBar}, State: {State}");
                        lastReadyCheck = DateTime.Now;
                    }
                    return false;
                }

                // Verificar se indicadores têm dados suficientes antes de validar valores
                // CORREÇÃO: Não bloquear se apenas o GlitchIndicator não tem dados
                // Os outros indicadores são essenciais, mas o GlitchIndicator pode ser opcional temporariamente
                bool essentialIndicatorsHaveData = emaRapida.Count > 0 && emaLenta.Count > 0 && emaTendencia.Count > 0 && 
                                                   emaSuperLenta.Count > 0 && rsi.Count > 0;
                
                if (!essentialIndicatorsHaveData)
                {
                    // Log mais frequente nas primeiras barras para debug
                    if (CurrentBar < 100 || (EnableReadyLogging && (DateTime.Now - lastReadyCheck).TotalSeconds >= 5))
                    {
                        string emptyIndicators = "";
                        if (emaRapida.Count == 0) emptyIndicators += $"EMA Rápida(Count={emaRapida?.Count ?? 0}), ";
                        if (emaLenta.Count == 0) emptyIndicators += $"EMA Lenta(Count={emaLenta?.Count ?? 0}), ";
                        if (emaTendencia.Count == 0) emptyIndicators += $"EMA Tendência(Count={emaTendencia?.Count ?? 0}), ";
                        if (emaSuperLenta.Count == 0) emptyIndicators += $"EMA Super Lenta(Count={emaSuperLenta?.Count ?? 0}), ";
                        if (rsi.Count == 0) emptyIndicators += $"RSI(Count={rsi?.Count ?? 0}), ";
                        
                        LogInfo("🚫 Indicadores Sem Dados",
                            $"⏳ Indicadores essenciais sem dados: {emptyIndicators.TrimEnd(',', ' ')}\n" +
                            $"CurrentBar: {CurrentBar}, State: {State}, Aguardando cálculo dos indicadores...");
                        lastReadyCheck = DateTime.Now;
                    }
                    return false;
                }
                
                // Log se GlitchIndicator não tem dados, mas não bloquear
                if (glitch != null && glitch.Count == 0)
                {
                    if (CurrentBar < 100 || (EnableReadyLogging && (DateTime.Now - lastReadyCheck).TotalSeconds >= 10))
                    {
                        LogInfo("⚠️ GlitchIndicator Sem Dados",
                            $"⏳ GlitchIndicator ainda não tem dados (Count=0), mas continuando com outros indicadores\n" +
                            $"glitch.Instrument: {glitch.Instrument?.FullName ?? "null"}, CurrentBar: {CurrentBar}");
                        lastReadyCheck = DateTime.Now;
                    }
                    // NÃO retornar false aqui - continuar mesmo sem GlitchIndicator
                }

                // Verificar se indicadores essenciais têm barras suficientes para plotar
                // CORREÇÃO: Não bloquear se apenas o GlitchIndicator não tem barras suficientes
                bool essentialIndicatorsHaveBars = CurrentBars[0] >= emaRapida.BarsRequiredToPlot && 
                                                   CurrentBars[0] >= emaLenta.BarsRequiredToPlot &&
                                                   CurrentBars[0] >= emaTendencia.BarsRequiredToPlot &&
                                                   CurrentBars[0] >= emaSuperLenta.BarsRequiredToPlot &&
                                                   CurrentBars[0] >= rsi.BarsRequiredToPlot;
                
                if (!essentialIndicatorsHaveBars)
                {
                    if (EnableReadyLogging && (DateTime.Now - lastReadyCheck).TotalSeconds >= 5)
                    {
                        LogInfo("Indicadores",
                            $"⏳ Aguardando BarsRequiredToPlot: CurrentBars={CurrentBars[0]}, " +
                            $"EMA Rápida precisa {emaRapida.BarsRequiredToPlot}, " +
                            $"EMA Super Lenta precisa {emaSuperLenta.BarsRequiredToPlot}");
                        lastReadyCheck = DateTime.Now;
                    }
                    return false;
                }
                
                // Verificar GlitchIndicator separadamente (não bloquear se não tiver barras suficientes)
                if (glitch != null && CurrentBars[0] < glitch.BarsRequiredToPlot)
                {
                    if (EnableReadyLogging && (DateTime.Now - lastReadyCheck).TotalSeconds >= 10)
                    {
                        LogInfo("Indicadores",
                            $"⚠️ GlitchIndicator ainda não tem barras suficientes: CurrentBars={CurrentBars[0]}, " +
                            $"GlitchIndicator precisa {glitch.BarsRequiredToPlot}, mas continuando com outros indicadores");
                        lastReadyCheck = DateTime.Now;
                    }
                    // NÃO retornar false aqui - continuar mesmo sem GlitchIndicator
                }

                // Verificar valores válidos com diagnóstico detalhado
                double emaRapidaVal = GetIndicatorValue(emaRapida, 0, double.NaN);
                double emaLentaVal = GetIndicatorValue(emaLenta, 0, double.NaN);
                double emaTendenciaVal = GetIndicatorValue(emaTendencia, 0, double.NaN);
                double emaSuperLentaVal = GetIndicatorValue(emaSuperLenta, 0, double.NaN);
                double rsiVal = GetIndicatorValue(rsi, 0, double.NaN);
                double glitchBandwidth = (glitch != null && IsIndicatorValid(glitch, 0)) ? glitch.BandWidth[0] : double.NaN;
                double glitchConfidence = (glitch != null && IsIndicatorValid(glitch, 0)) ? glitch.ConfidenceSmoothed[0] : double.NaN;
                double glitchATR = (glitch != null && IsIndicatorValid(glitch, 0)) ? GetGlitchATR() : double.NaN;

                // Diagnóstico detalhado de valores inválidos
                bool indicatorsValid = true;
                string invalidIndicators = "";

                if (double.IsNaN(emaRapidaVal) || emaRapidaVal <= 0)
                {
                    indicatorsValid = false;
                    invalidIndicators += $"EMA Rápida={emaRapidaVal}, ";
                }
                if (double.IsNaN(emaLentaVal) || emaLentaVal <= 0)
                {
                    indicatorsValid = false;
                    invalidIndicators += $"EMA Lenta={emaLentaVal}, ";
                }
                if (double.IsNaN(emaTendenciaVal) || emaTendenciaVal <= 0)
                {
                    indicatorsValid = false;
                    invalidIndicators += $"EMA Tendência={emaTendenciaVal}, ";
                }
                if (double.IsNaN(emaSuperLentaVal) || emaSuperLentaVal <= 0)
                {
                    indicatorsValid = false;
                    invalidIndicators += $"EMA Super Lenta={emaSuperLentaVal}, ";
                }
                if (double.IsNaN(rsiVal) || rsiVal < 0)
                {
                    indicatorsValid = false;
                    invalidIndicators += $"RSI={rsiVal}, ";
                }
                // CORREÇÃO: GlitchIndicator é opcional - não bloquear se não tiver dados
                // Usar valores padrão se GlitchIndicator não estiver disponível
                if (double.IsNaN(glitchATR) || glitchATR < 0)
                {
                    // Não bloquear - GlitchIndicator pode não estar disponível ainda
                    if (glitch != null && glitch.Count > 0)
                    {
                        // Só logar se o indicador existe mas retornou valor inválido
                        LogWarning("CheckIndicatorsReady", $"GlitchATR inválido: {glitchATR}");
                    }
                    // Usar valor padrão ou continuar sem GlitchATR
                }
                if (double.IsNaN(glitchConfidence) || glitchConfidence < 0)
                {
                    // Não bloquear - GlitchIndicator pode não estar disponível ainda
                    if (glitch != null && glitch.Count > 0)
                    {
                        // Só logar se o indicador existe mas retornou valor inválido
                        LogWarning("CheckIndicatorsReady", $"GlitchConfidence inválido: {glitchConfidence}");
                    }
                    // Usar valor padrão ou continuar sem GlitchConfidence
                }

                if (UsarVWAPBands && glitch != null)
                {
                    double vwapVal = GetGlitchVWAP();
                    if (double.IsNaN(vwapVal) || vwapVal <= 0)
                    {
                        // Não bloquear se VWAP ainda não tem valor - é normal nas primeiras barras
                        if (EnableReadyLogging && (DateTime.Now - lastReadyCheck).TotalSeconds >= 10)
                        {
                            LogInfo("VWAP", $"⏳ VWAP ainda não tem valor (normal nas primeiras barras)");
                            lastReadyCheck = DateTime.Now;
                        }
                        // Não retornar false apenas por causa do VWAP
                    }
                }

                if (!indicatorsValid && EnableReadyLogging && (DateTime.Now - lastReadyCheck).TotalSeconds >= 5)
                {
                    LogInfo("Indicadores",
                        $"⏳ Indicadores com valores inválidos: {invalidIndicators.TrimEnd(',', ' ')}\n" +
                        $"   CurrentBar: {CurrentBar}, CurrentBars[0]: {CurrentBars[0]}, MaxPeriod: {maxPeriodRequired}\n" +
                        $"   EMA Rápida: Count={emaRapida.Count}, Value={emaRapidaVal}, BarsRequired={emaRapida.BarsRequiredToPlot}\n" +
                        $"   EMA Lenta: Count={emaLenta.Count}, Value={emaLentaVal}, BarsRequired={emaLenta.BarsRequiredToPlot}\n" +
                        $"   RSI: Count={rsi.Count}, Value={rsiVal}, BarsRequired={rsi.BarsRequiredToPlot}\n" +
                        $"   GlitchIndicator: Count={glitch.Count}, Bandwidth={glitchBandwidth}, Confidence={glitchConfidence}, BarsRequired={glitch.BarsRequiredToPlot}");
                    lastReadyCheck = DateTime.Now;
                }

                return indicatorsValid;
            }
            catch (Exception ex)
            {
                LogError("CheckIndicatorsReady", ex);
                return false;
            }
        }


        private void CheckAndSetStopsIfNeeded()
        {
            try
            {
                if ((DateTime.Now - lastStopSetAttempt).TotalMilliseconds < STOP_SET_RETRY_INTERVAL_MS)
                    return;

                lastStopSetAttempt = DateTime.Now;

                // Verificar se todos os blocos foram preenchidos
                bool allBlocksFilled = CheckAllBlocksFilled();

                if (allBlocksFilled && !stopsSet)
                {
                    LogInfo("Stops Check",
                        $"✅ TODOS BLOCOS PREENCHIDOS! Filled: {totalQuantityFilled}, Expected: {totalQuantityExpected}\n" +
                        $"Setando stops agora...");
                    SetStopsAndTargets();
                }
                else if (!allBlocksFilled)
                {
                    LogInfo("Stops Check",
                        $"⏳ Aguardando fills completos: {totalQuantityFilled}/{totalQuantityExpected}");
                }
            }
            catch (Exception ex)
            {
                LogError("CheckAndSetStopsIfNeeded", ex);
            }
        }

        private bool CheckAllBlocksFilled()
        {
            try
            {
                if (Position == null) return false;

                // Contar quantidade preenchida por ordem
                int totalFilledFromOrders = 0;
                var ordersArray = Orders.ToArray();

                foreach (var order in ordersArray)
                {
                    if (order != null &&
                        order.Name.Contains($"_{tradeCounter}") &&
                        (order.OrderState == OrderState.Filled ||
                         order.OrderState == OrderState.PartFilled))
                    {
                        if (blockFilledQuantities.ContainsKey(order.Name))
                            totalFilledFromOrders += blockFilledQuantities[order.Name];
                        else
                            totalFilledFromOrders += order.Filled;
                    }
                }

                // Atualizar quantidade total preenchida
                totalQuantityFilled = Math.Max(Position.Quantity, totalFilledFromOrders);

                // VERIFICAR SE A QUANTIDADE TOTAL PREENCHIDA CORRESPONDE
                bool quantityMatches = totalQuantityFilled >= totalQuantityExpected;

                // VERIFICAR CADA BLOCO INDIVIDUALMENTE
                bool block1Ok = !block1Entered || (block1Entered && block1Filled);
                bool block2Ok = !block2Entered || (block2Entered && block2Filled);
                bool block3Ok = !block3Entered || (block3Entered && block3Filled);

                bool allBlocksOk = block1Ok && block2Ok && block3Ok && quantityMatches;

                if (!allBlocksOk)
                {
                    LogInfo("Blocks Check V3",
                        $"Filled: {totalQuantityFilled}/{totalQuantityExpected}\n" +
                        $"From Orders: {totalFilledFromOrders}\n" +
                        $"Position Qty: {Position.Quantity}\n" +
                        $"B1: Entered={block1Entered}, Filled={block1Filled}\n" +
                        $"B2: Entered={block2Entered}, Filled={block2Filled}\n" +
                        $"B3: Entered={block3Entered}, Filled={block3Filled}");
                }

                return allBlocksOk;
            }
            catch (Exception ex)
            {
                LogError("CheckAllBlocksFilled", ex);
                return false;
            }
        }

        private void SetStopsAndTargets()
        {
            try
            {
                if (Position == null)
                {
                    LogError("Stops", "Position é null");
                    return;
                }

                if (Position.MarketPosition == MarketPosition.Flat)
                {
                    LogInfo("Stops", "Posição Flat - Pulando");
                    return;
                }

                if (Position.MarketPosition != tradeDirection)
                {
                    LogWarning("Direção", $"Inconsistência! Position={Position.MarketPosition}, tradeDirection={tradeDirection}");
                    tradeDirection = Position.MarketPosition;
                }

                if (Position.Quantity == 0)
                {
                    LogInfo("Stops", "Quantity=0 - Pulando");
                    return;
                }

                if (stopsSet)
                {
                    LogInfo("Stops", "Stops já setados");
                    return;
                }

                // VERIFICAÇÃO CRÍTICA: Todos os blocos foram preenchidos?
                if (!CheckAllBlocksFilled())
                {
                    LogWarning("Stops",
                        $"🚨 NÃO SETAR STOPS! Fills incompletos: {totalQuantityFilled}/{totalQuantityExpected}");
                    return;
                }

                double avgEntryPrice = Position.AveragePrice;

                // Usar entryPrice se AveragePrice for inválido
                if (avgEntryPrice <= 0 || double.IsNaN(avgEntryPrice))
                {
                    if (entryPrice > 0)
                        avgEntryPrice = entryPrice;
                    else
                        avgEntryPrice = GetCurrentMarketPrice();

                    LogWarning("Entry Price", $"AveragePrice inválido - Usando: {avgEntryPrice:F2}");
                }

                entryPrice = avgEntryPrice;
                highestPriceSinceEntry = entryPrice;
                lowestPriceSinceEntry = entryPrice;

                LogInfo("Debug Stops V3",
                    $"entryPrice={entryPrice:F2}, TickSize={TickSize:F4}\n" +
                    $"Blocks Filled: B1={block1Filled}, B2={block2Filled}, B3={block3Filled}");

                double block1SLPrice, block2SLPrice, block3SLPrice;

                if (tradeDirection == MarketPosition.Long)
                {
                    block1SLPrice = entryPrice - ((dynamicBlock1StopLossTicks + SLBufferTicks) * TickSize);
                    block2SLPrice = entryPrice - ((dynamicBlock2StopLossTicks + SLBufferTicks) * TickSize);
                    block3SLPrice = entryPrice - ((dynamicBlock3StopLossTicks + SLBufferTicks) * TickSize);
                }
                else
                {
                    block1SLPrice = entryPrice + ((dynamicBlock1StopLossTicks + SLBufferTicks) * TickSize);
                    block2SLPrice = entryPrice + ((dynamicBlock2StopLossTicks + SLBufferTicks) * TickSize);
                    block3SLPrice = entryPrice + ((dynamicBlock3StopLossTicks + SLBufferTicks) * TickSize);
                }

                // SETAR STOPS APENAS PARA BLOCOS PREENCHIDOS
                if (block1Filled)
                {
                    string block1Name = "Block1_" + tradeCounter;
                    SetStopLoss(block1Name, CalculationMode.Price, block1SLPrice, false);
                    SetProfitTarget(block1Name, CalculationMode.Ticks, dynamicBlock1ProfitTargetTicks);
                    lastKnownStopPriceBlock1 = block1SLPrice;
                    LogInfo("Stop/TP", $"{block1Name}: SL={block1SLPrice:F2}, TP={dynamicBlock1ProfitTargetTicks} ticks");
                }

                if (block2Filled)
                {
                    string block2Name = "Block2_" + tradeCounter;
                    SetStopLoss(block2Name, CalculationMode.Price, block2SLPrice, false);
                    SetProfitTarget(block2Name, CalculationMode.Ticks, dynamicBlock2ProfitTargetTicks);
                    lastKnownStopPriceBlock2 = block2SLPrice;
                    LogInfo("Stop/TP", $"{block2Name}: SL={block2SLPrice:F2}, TP={dynamicBlock2ProfitTargetTicks} ticks");
                }

                if (block3Filled)
                {
                    string block3Name = "Block3_" + tradeCounter;
                    SetStopLoss(block3Name, CalculationMode.Price, block3SLPrice, false);
                    SetProfitTarget(block3Name, CalculationMode.Ticks, dynamicBlock3ProfitTargetTicks);
                    lastKnownStopPriceBlock3 = block3SLPrice;
                    LogInfo("Stop/TP", $"{block3Name}: SL={block3SLPrice:F2}, TP={dynamicBlock3ProfitTargetTicks} ticks");
                }

                stopsSet = true;
                LogTrade("Stops Setados V3",
                    $"✅ Stops e targets {tradeDirection} enviados (Trade #{tradeCounter})\n" +
                    $"Preço de entrada: {entryPrice:F2}\n" +
                    $"Quantidades: B1={Block1Quantity}({block1Filled}), B2={Block2Quantity}({block2Filled}), B3={Block3Quantity}({block3Filled})\n" +
                    $"Total Filled: {totalQuantityFilled}/{totalQuantityExpected}\n" +
                    $"Stops: {dynamicBlock1StopLossTicks} ticks\n" +
                    $"Targets: TP1={dynamicBlock1ProfitTargetTicks} ticks, TP2={dynamicBlock2ProfitTargetTicks} ticks, TP3={dynamicBlock3ProfitTargetTicks} ticks");

                if (UsarSistemaATR)
                {
                    double finalWeightedRR = CalculateWeightedRewardRiskRatio();
                    LogInfo("R/R Final V3", $"R/R Ponderado: {finalWeightedRR:F2}:1");
                }
            }
            catch (Exception ex)
            {
                LogError("SetStopsAndTargets", ex);
            }
        }

        // CORREÇÃO: Método EnterTrades ajustado para usar conversões explícitas
        private void EnterTrades()
        {
            if (Position != null && Position.MarketPosition != MarketPosition.Flat)
            {
                LogWarning("Entrada", "Tentativa de entrada com posição ativa");
                ResetSignals();
                return;
            }

            // CORREÇÃO: Verificar se glitch tem dados antes de usar
            if (UsarFiltroADX && glitch != null && glitch.Count > 0 && IsIndicatorValid(glitch, 0))
            {
                double confidence = glitch.ConfidenceSmoothed[0];
                double confidenceThreshold = ADXMaximo * 3.0;
                if (confidence >= confidenceThreshold)
                {
                    LogWarning("ADX", $"Filtro bloqueou entrada: Confidence={confidence:F1} >= {confidenceThreshold:F1}");
                    ResetSignals();
                    return;
                }
            }

            // VERIFICAÇÃO FINAL de limites diários
            DateTime today = DateTime.Now.Date;

            if (today != lastTradeDay.Date)
            {
                ResetDailyCounters();
            }

            if (tradesToday >= MaxTradesPerDay)
            {
                LogWarning("Daily Limit",
                    $"Tentativa de entrada bloqueada: Limite diário {MaxTradesPerDay} já atingido");
                ResetSignals();
                return;
            }

            tradeCounter++;
            string tradeId = tradeCounter.ToString();
            string directionStr = (tradeDirection == MarketPosition.Long) ? "LONG" : "SHORT";

            // Incrementar APÓS todas as verificações
            tradesToday++;

            LogTrade("Início V3",
                $"Iniciando entradas {directionStr} (Trade #{tradeId}, " +
                $"Hoje: {tradesToday}/{MaxTradesPerDay}, Data: {today:yyyy-MM-dd})");

            ResetTradeState();
            totalQuantityExpected = Block1Quantity + Block2Quantity + Block3Quantity;
            totalQuantityFilled = 0;

            // ===== CALCULAR STOP LOSS E TAKE PROFIT =====
            if (UsarSistemaATR)
            {
                // CORREÇÃO: Usar método auxiliar para calcular ATR em ticks
                int atrTicks = CalculateATRTicks();

                LogInfo("ATR Dinâmico V3", $"ATR: {atrTicks} ticks");

                // CORREÇÃO: Converter double para int explicitamente
                dynamicBlock1StopLossTicks = Math.Max(5, (int)Math.Round(atrTicks * SlAtrMultiplier));
                dynamicBlock1ProfitTargetTicks = Math.Max(5, (int)Math.Round(atrTicks * TpAtrMultiplierBlock1));
                dynamicBlock2StopLossTicks = dynamicBlock1StopLossTicks;
                dynamicBlock2ProfitTargetTicks = Math.Max(5, (int)Math.Round(atrTicks * TpAtrMultiplierBlock2));
                dynamicBlock3StopLossTicks = dynamicBlock1StopLossTicks;
                dynamicBlock3ProfitTargetTicks = Math.Max(5, (int)Math.Round(atrTicks * TpAtrMultiplierBlock3));

                CalculateTradePotential();

                double weightedRR = CalculateWeightedRewardRiskRatio();
                LogInfo("Risk/Reward V3", $"R/R Ratio: {weightedRR:F2} (Mínimo: {MinimoRewardRisk})");

                // CORREÇÃO CRÍTICA: Log detalhado ANTES de bloquear por R/R
                LogInfo("🔍 DIAGNÓSTICO R/R", 
                    $"Bar: {CurrentBar}, Score: {totalScore:F2}\n" +
                    $"ATR Ticks: {atrTicks}\n" +
                    $"SL Ticks: {dynamicBlock1StopLossTicks}\n" +
                    $"TP1/TP2/TP3 Ticks: {dynamicBlock1ProfitTargetTicks}/{dynamicBlock2ProfitTargetTicks}/{dynamicBlock3ProfitTargetTicks}\n" +
                    $"Quantidades: B1={Block1Quantity}, B2={Block2Quantity}, B3={Block3Quantity}\n" +
                    $"R/R Ponderado: {weightedRR:F2} (Mínimo: {MinimoRewardRisk})");
                
                // CORREÇÃO CRÍTICA: Reduzir MinimoRewardRisk de 0.5 para 0.3 ou tornar opcional para scores altos
                // Em timeframe de 1min, movimentos pequenos podem gerar R/R baixo mesmo com setups válidos
                // CORREÇÃO: Calcular threshold dinâmico baseado no tradeDirection
                double thresholdAtual = (tradeDirection == MarketPosition.Long) 
                    ? CalcularThresholdDinamicoComQualidade() 
                    : (UsarThresholdsDinamicos ? shortThresholdDynamic : ShortThreshold);
                
                double rrThreshold = MinimoRewardRisk;
                if (totalScore >= thresholdAtual * 1.5) // Se score está 50% acima do threshold
                {
                    rrThreshold = MinimoRewardRisk * 0.6; // Reduzir threshold em 40% para scores altos
                    LogInfo("R/R Ajustado", $"Score alto ({totalScore:F2}) >= {thresholdAtual:F2} * 1.5 - R/R threshold reduzido para {rrThreshold:F2}");
                }
                
                if (weightedRR < rrThreshold)
                {
                    LogWarning("Risk/Reward", 
                        $"R/R insuficiente: {weightedRR:F2} < {rrThreshold:F2} (Mínimo padrão: {MinimoRewardRisk}) - BLOQUEANDO ENTRADA\n" +
                        $"ATR: {atrTicks} ticks, SL: {dynamicBlock1StopLossTicks} ticks, TPs: {dynamicBlock1ProfitTargetTicks}/{dynamicBlock2ProfitTargetTicks}/{dynamicBlock3ProfitTargetTicks} ticks");
                    ResetSignals();
                    tradesToday = Math.Max(0, tradesToday - 1); // Reverter incremento
                    tradeCounter = Math.Max(0, tradeCounter - 1); // Reverter contador
                    // CORREÇÃO: Resetar flag de processamento quando R/R bloqueia
                    lock (tradeLock)
                    {
                        isProcessingTrade = false;
                    }
                    return;
                }
            }
            else
            {
                LogInfo("SL/TP", "Usando sistema de ticks fixos");

                dynamicBlock1StopLossTicks = Block1StopLossTicks;
                dynamicBlock1ProfitTargetTicks = Block1ProfitTargetTicks;
                dynamicBlock2StopLossTicks = Block2StopLossTicks;
                dynamicBlock2ProfitTargetTicks = Block2ProfitTargetTicks;
                dynamicBlock3StopLossTicks = Block3StopLossTicks;
                dynamicBlock3ProfitTargetTicks = Block3ProfitTargetTicks;
            }

            LogInfo("Parâmetros V3",
                $"SL: {dynamicBlock1StopLossTicks} ticks\n" +
                $"TPs: {dynamicBlock1ProfitTargetTicks}/{dynamicBlock2ProfitTargetTicks}/{dynamicBlock3ProfitTargetTicks} ticks");

            // VERIFICAÇÃO DE MARGEM
            double approxMarginPerContract = MNQMarginPerContract;
            double requiredMargin = totalQuantityExpected * approxMarginPerContract;
            LogInfo("Margem", $"Requerida: ${requiredMargin:F2}");

            // ENTRAR COM OS 3 BLOCOS
            bool entrySuccess = true;
            List<string> enteredBlocks = new List<string>();

            string block1Name = "Block1_" + tradeId;
            if (Block1Quantity > 0)
            {
                try
                {
                    if (tradeDirection == MarketPosition.Long)
                        EnterLong(Block1Quantity, block1Name);
                    else
                        EnterShort(Block1Quantity, block1Name);

                    block1Entered = true;
                    block1Filled = false;
                    orderSubmissionTimes[block1Name] = DateTime.Now;
                    blockOrderQuantities[block1Name] = Block1Quantity;
                    enteredBlocks.Add(block1Name);
                    LogInfo("Entrada", $"✅ {block1Name} ENVIADA: {Block1Quantity} contratos {directionStr}");
                }
                catch (Exception ex)
                {
                    LogError($"Entrada {block1Name}", ex);
                    entrySuccess = false;
                }
            }

            string block2Name = "Block2_" + tradeId;
            if (Block2Quantity > 0 && entrySuccess)
            {
                try
                {
                    if (tradeDirection == MarketPosition.Long)
                        EnterLong(Block2Quantity, block2Name);
                    else
                        EnterShort(Block2Quantity, block2Name);

                    block2Entered = true;
                    block2Filled = false;
                    orderSubmissionTimes[block2Name] = DateTime.Now;
                    blockOrderQuantities[block2Name] = Block2Quantity;
                    enteredBlocks.Add(block2Name);
                    LogInfo("Entrada", $"✅ {block2Name} ENVIADA: {Block2Quantity} contratos {directionStr}");
                }
                catch (Exception ex)
                {
                    LogError($"Entrada {block2Name}", ex);
                    entrySuccess = false;
                }
            }

            string block3Name = "Block3_" + tradeId;
            if (Block3Quantity > 0 && entrySuccess)
            {
                try
                {
                    if (tradeDirection == MarketPosition.Long)
                        EnterLong(Block3Quantity, block3Name);
                    else
                        EnterShort(Block3Quantity, block3Name);

                    block3Entered = true;
                    block3Filled = false;
                    orderSubmissionTimes[block3Name] = DateTime.Now;
                    blockOrderQuantities[block3Name] = Block3Quantity;
                    enteredBlocks.Add(block3Name);
                    LogInfo("Entrada", $"✅ {block3Name} ENVIADA: {Block3Quantity} contratos {directionStr}");
                }
                catch (Exception ex)
                {
                    LogError($"Entrada {block3Name}", ex);
                    entrySuccess = false;
                }
            }

            if (!entrySuccess)
            {
                LogError("Entrada", $"Falha nas entradas. Cancelando trade #{tradeId}");

                // Cancelar apenas blocos que foram enviados
                foreach (var blockName in enteredBlocks)
                {
                    CancelStopOrder(blockName);
                }

                ResetSignals();
                tradesToday = Math.Max(0, tradesToday - 1); // Reverter incremento
                
                // CORREÇÃO: Resetar flag de processamento quando entrada falha
                lock (tradeLock)
                {
                    isProcessingTrade = false;
                }
                
                return;
            }

            entryPrice = GetCurrentMarketPrice();
            highestPriceSinceEntry = entryPrice;
            lowestPriceSinceEntry = entryPrice;
            lastTradeTime = DateTime.Now;

            // Registrar para análise ML
            if (AtivarMLBasico)
            {
                var tradeRecord = new TradeRecord
                {
                    TradeId = tradeCounter,
                    EntryTime = DateTime.Now,
                    EntryPrice = entryPrice,
                    Direction = tradeDirection,
                    IndicatorValues = new Dictionary<string, double>(),
                    ScoreContributions = new Dictionary<string, double>()
                };
                tradeRecords[tradeCounter] = tradeRecord;
            }

            LogTrade("Sucesso V3",
                $"✅ Entradas {directionStr} ENVIADAS (Trade #{tradeId})\n" +
                $"Entry Price: {entryPrice:F2}, Score: {totalScore:F2}\n" +
                $"TP1: {CalculateTargetPrice(dynamicBlock1ProfitTargetTicks):F2} (+{dynamicBlock1ProfitTargetTicks} ticks)\n" +
                $"TP2: {CalculateTargetPrice(dynamicBlock2ProfitTargetTicks):F2} (+{dynamicBlock2ProfitTargetTicks} ticks)\n" +
                $"Trades hoje: {tradesToday}/{MaxTradesPerDay}");
        }

        #endregion

        #region Métodos de Processamento de Trade
        private void ProcessarEntradaTrade()
        {
            // CORREÇÃO: Verificar se já está processando um trade
            lock (tradeLock)
            {
                if (isProcessingTrade)
                {
                    LogWarning("Entrada", "Tentativa de processar entrada enquanto outro trade está em processamento");
                    ResetSignals();
                    return;
                }

                // CORREÇÃO: Verificar se já existe posição ativa
                if (Position != null && Position.MarketPosition != MarketPosition.Flat)
                {
                    LogWarning("Entrada", $"Tentativa de entrada com posição ativa: {Position.MarketPosition}");
                    ResetSignals();
                    return;
                }

                isProcessingTrade = true;
            }

            bool ordersSubmitted = false;
            
            try
            {
                EnterTrades();
                ordersSubmitted = true; // Ordens foram enviadas com sucesso
            }
            catch (Exception ex)
            {
                LogError("EnterTrades", ex);
                ResetSignals();
                // CORREÇÃO: Resetar flag apenas se houver erro na submissão
                lock (tradeLock)
                {
                    isProcessingTrade = false;
                }
            }
            finally
            {
                // CORREÇÃO: NÃO resetar flag se ordens foram enviadas com sucesso
                // A flag será resetada em OnPositionUpdate quando posição for estabelecida,
                // ou em OnExecutionUpdate se todas as ordens forem canceladas/rejeitadas
                // Isso evita que novos sinais sejam processados enquanto ordens estão pendentes
                if (!ordersSubmitted)
                {
                    lock (tradeLock)
                    {
                        isProcessingTrade = false;
                    }
                }
            }
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price,
            int quantity, MarketPosition marketPosition, string orderId, DateTime time)
        {
            try
            {
                if (execution.Order == null || execution.Order.Name == null)
                    return;

                string orderName = execution.Order.Name;

                if (orderName.Contains("Block") && orderName.Contains(tradeCounter.ToString()))
                {
                    if (execution.Order.OrderState == OrderState.Filled ||
                        execution.Order.OrderState == OrderState.PartFilled)
                    {
                        // Atualizar quantidade preenchida por bloco
                        if (orderName.Contains("Block1"))
                        {
                            block1Filled = true;
                            if (!blockFilledQuantities.ContainsKey(orderName))
                                blockFilledQuantities[orderName] = 0;
                            blockFilledQuantities[orderName] += quantity;
                            LogInfo("Fill", $"✅ BLOCO 1 PREENCHIDO: {quantity} contratos @ {price:F2}");
                        }
                        else if (orderName.Contains("Block2"))
                        {
                            block2Filled = true;
                            if (!blockFilledQuantities.ContainsKey(orderName))
                                blockFilledQuantities[orderName] = 0;
                            blockFilledQuantities[orderName] += quantity;
                            LogInfo("Fill", $"✅ BLOCO 2 PREENCHIDO: {quantity} contratos @ {price:F2}");
                        }
                        else if (orderName.Contains("Block3"))
                        {
                            block3Filled = true;
                            if (!blockFilledQuantities.ContainsKey(orderName))
                                blockFilledQuantities[orderName] = 0;
                            blockFilledQuantities[orderName] += quantity;
                            LogInfo("Fill", $"✅ BLOCO 3 PREENCHIDO: {quantity} contratos @ {price:F2}");
                        }

                        // Atualizar contador total
                        totalQuantityFilled = Position?.Quantity ?? 0;

                        LogInfo("Execution Update V3",
                            $"Order: {orderName}, Qty: {quantity}, Price: {price:F2}\n" +
                            $"Total Filled: {totalQuantityFilled}/{totalQuantityExpected}");
                    }
                }
                else if (orderName.Contains("ProfitTarget") || orderName.Contains("StopLoss"))
                {
                    // Atualizar ML quando trade for fechado (stop ou target)
                    if (execution.Order.OrderState == OrderState.Filled)
                    {
                        // Melhoria: Validação defensiva de entryPrice antes de calcular P&L
                        double validEntryPrice = entryPrice;
                        
                        if (validEntryPrice <= 0)
                        {
                            // Usar Position.AveragePrice como fallback
                            validEntryPrice = Position?.AveragePrice ?? price;
                            
                            if (validEntryPrice <= 0)
                            {
                                LogWarning("P&L ML", 
                                    $"entryPrice inválido ({entryPrice}) - P&L não calculado para ML. " +
                                    $"Order: {orderName}, Price: {price:F2}, Position.AvgPrice: {Position?.AveragePrice ?? 0:F2}");
                                return;
                            }
                            
                            // Atualizar entryPrice se foi corrigido
                            entryPrice = validEntryPrice;
                        }

                        double pnl = (marketPosition == MarketPosition.Long) ?
                            (price - validEntryPrice) * quantity * Instrument.MasterInstrument.PointValue :
                            (validEntryPrice - price) * quantity * Instrument.MasterInstrument.PointValue;

                        bool isWinner = pnl > 0;

                        AtualizarPerformanceML(tradeCounter, pnl, isWinner);
                    }
                }
            }
            catch (Exception ex)
            {
                LogError("OnExecutionUpdate", ex);
            }
        }

        protected override void OnPositionUpdate(Position position, double averagePrice, int quantity, MarketPosition marketPosition)
        {
            try
            {
                LogInfo("Position Update V3",
                    $"MarketPosition={marketPosition}, Quantity={quantity}, AveragePrice={averagePrice:F2}\n" +
                    $"Total Expected: {totalQuantityExpected}, StopsSet: {stopsSet}");

                if (marketPosition == MarketPosition.Flat)
                {
                    // Cancelar TODAS as ordens pendentes deste trade
                    var ordersArray = Orders.ToArray();
                    foreach (var order in ordersArray)
                    {
                        if (order != null &&
                            order.Name.Contains(tradeCounter.ToString()) &&
                            (order.OrderState == OrderState.Accepted ||
                             order.OrderState == OrderState.Working ||
                             order.OrderState == OrderState.Submitted ||
                             order.OrderState == OrderState.PartFilled))
                        {
                            CancelOrder(order);
                            LogInfo("Cancelamento", $"Ordem pendente cancelada: {order.Name}");
                            orderSubmissionTimes.Remove(order.Name);
                        }
                    }

                    ResetTradeStateFull();
                    // CORREÇÃO: ResetTradeStateFull já reseta isProcessingTrade, mas garantir aqui também
                    lock (tradeLock)
                    {
                        isProcessingTrade = false;
                    }
                    LogInfo("Reset", "Posição fechada - Resetando todas as flags");
                }
                else if (marketPosition != MarketPosition.Flat && quantity > 0)
                {
                    // Atualizar quantidade preenchida
                    totalQuantityFilled = quantity;

                    LogInfo("Position Update",
                        $"Posição {marketPosition} atualizada: {quantity}/{totalQuantityExpected} contratos");

                    // CORREÇÃO: Resetar flag de processamento quando posição é estabelecida
                    // (ordens foram executadas, pode processar novos sinais)
                    lock (tradeLock)
                    {
                        isProcessingTrade = false;
                    }

                    // Verificar se todos os blocos foram preenchidos E stops não foram setados
                    if (CheckAllBlocksFilled() && !stopsSet)
                    {
                        LogTrade("Fills Completos V3",
                            $"✅ TODOS BLOCOS PREENCHIDOS! ({quantity}/{totalQuantityExpected})");
                        SetStopsAndTargets();
                    }
                }
            }
            catch (Exception ex)
            {
                LogError("OnPositionUpdate", ex);
            }
        }

        private void CheckPnLLimits()
        {
            try
            {
                if (!AtivarLimitesPnL || !limitsActive || (DateTime.Now - lastPnLCheck).TotalSeconds < PNL_CHECK_INTERVAL_SECONDS)
                    return;

                lastPnLCheck = DateTime.Now;

                // Obter PnL TOTAL da conta
                double currentPnL = GetTotalAccountPnL();

                if (!dailyPnLInitialized)
                {
                    dailyPnLStart = currentPnL;
                    dailyPnLInitialized = true;
                    LogInfo("PnL", $"PnL diário inicializado: ${dailyPnLStart:F2}");
                    return;
                }

                // Calcular PnL do dia
                double dailyPnL = currentPnL - dailyPnLStart;

                // Verificar limite de perda
                if (LimitePerdaDiaria < 0 && dailyPnL <= LimitePerdaDiaria)
                {
                    LogWarning("LIMITE PnL",
                        $"🚨 LIMITE DE PERDA ATINGIDO!\n" +
                        $"PnL diário: ${dailyPnL:F2}\n" +
                        $"Limite: ${LimitePerdaDiaria}\n" +
                        $"Fechando todas as operações...");

                    CloseAllPositions();
                    limitsActive = false;

                    CancelAllPendingOrders();

                    LogTrade("Limite Perda", $"Todas as operações fechadas - PnL diário: ${dailyPnL:F2}");
                    return;
                }

                // Verificar limite de ganho
                if (LimiteGanhoDiario > 0 && dailyPnL >= LimiteGanhoDiario)
                {
                    LogWarning("LIMITE PnL",
                        $"🎯 LIMITE DE GANHO ATINGIDO!\n" +
                        $"PnL diário: ${dailyPnL:F2}\n" +
                        $"Limite: ${LimiteGanhoDiario}\n" +
                        $"Fechando todas as operações...");

                    CloseAllPositions();
                    limitsActive = false;

                    CancelAllPendingOrders();

                    LogTrade("Limite Ganho", $"Todas as operações fechadas - PnL diário: ${dailyPnL:F2}");
                    return;
                }

                // Log periódico do PnL
                if ((DateTime.Now - lastLimitLog).TotalMinutes >= 5)
                {
                    LogInfo("PnL Status V3",
                        $"PnL diário: ${dailyPnL:F2} | " +
                        $"PnL Total: ${currentPnL:F2} | " +
                        $"Limites: Perda=${LimitePerdaDiaria}, Ganho=${LimiteGanhoDiario}");
                    lastLimitLog = DateTime.Now;
                }
            }
            catch (Exception ex)
            {
                LogError("CheckPnLLimits", ex);
            }
        }

        private double GetTotalAccountPnL()
        {
            try
            {
                if (Account == null)
                    return 0;

                // PnL Realizado + Não Realizado
                double realized = Account.Get(AccountItem.RealizedProfitLoss, Currency.UsDollar);
                double unrealized = Account.Get(AccountItem.UnrealizedProfitLoss, Currency.UsDollar);
                return realized + unrealized;
            }
            catch (Exception ex)
            {
                LogError("GetTotalAccountPnL", ex);
                return 0;
            }
        }

        private bool CheckDailyLimits()
        {
            try
            {
                // CORREÇÃO: Em modo histórico, usar Time[0] em vez de DateTime.Now
                DateTime currentTime;
                if (State == State.Historical && CurrentBar >= 0 && Bars != null && Bars.Count > 0)
                    currentTime = Time[0];
                else
                    currentTime = DateTime.Now;
                
                DateTime today = currentTime.Date;

                // CORREÇÃO: Se lastTradeDay nunca foi inicializado (DateTime.MinValue), inicializar agora
                if (lastTradeDay == DateTime.MinValue)
                {
                    lastTradeDay = today;
                    tradesToday = 0;
                    LogInfo("Verificação Diária", $"Inicializando contadores diários - Data: {today:yyyy-MM-dd}");
                }

                if (today != lastTradeDay.Date)
                {
                    LogInfo("Verificação Diária V3", $"Dia mudou! Último: {lastTradeDay:yyyy-MM-dd}, Hoje: {today:yyyy-MM-dd}");
                    ResetDailyCounters();
                    return true;
                }

                if (tradesToday >= MaxTradesPerDay)
                {
                    if ((currentTime - lastLimitLog).TotalMinutes >= 5)
                    {
                        LogWarning("Daily Limit",
                            $"⚠️ LIMITE DIÁRIO ATINGIDO: {tradesToday}/{MaxTradesPerDay}");
                        lastLimitLog = currentTime;
                    }
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                LogError("CheckDailyLimits", ex);
                return false;
            }
        }

        private bool IsReadyForTrading()
        {
            try
            {
                // Verificar estado básico - permitir tanto Historical quanto Realtime
                if (State != State.Realtime && State != State.Historical)
                    return false;

                // Verificar dados
                if (Bars == null || Bars.Count == 0)
                    return false;

                // Verificar inicialização
                if (!strategyInitialized || !indicatorsReady)
                    return false;

                // Verificação de barras
                int minBars = Math.Max(1, BarsRequiredToTrade);
                if (CurrentBar < minBars)
                {
                    if (EnableReadyLogging && (DateTime.Now - lastReadyCheck).TotalSeconds >= 5)
                    {
                        LogInfo("Prontidão", $"⏳ Aguardando barras: {CurrentBar}/{minBars}");
                        lastReadyCheck = DateTime.Now;
                    }
                    return false;
                }

                // Verificar se limites PnL estão ativos (apenas em Realtime)
                if (State == State.Realtime && AtivarLimitesPnL && !limitsActive)
                {
                    if (EnableReadyLogging && (DateTime.Now - lastReadyCheck).TotalSeconds >= 5)
                    {
                        LogInfo("Prontidão", "⛔ Limites PnL atingidos - Trading bloqueado");
                        lastReadyCheck = DateTime.Now;
                    }
                    return false;
                }

                // Verificar se está dentro do horário de funcionamento (apenas em Realtime)
                if (State == State.Realtime && AtivarHorarioFuncionamento && !sessionActive)
                {
                    if (EnableReadyLogging && (DateTime.Now - lastReadyCheck).TotalSeconds >= 5)
                    {
                        LogInfo("Prontidão",
                            $"⏰ Fora do horário de trading: {sessionStartTime:HH:mm} - {sessionEndTime:HH:mm}");
                        lastReadyCheck = DateTime.Now;
                    }
                    return false;
                }

                // ✅ TUDO PRONTO!
                if (EnableReadyLogging && (DateTime.Now - lastReadyCheck).TotalSeconds >= 30)
                {
                    double currentPnL = GetTotalAccountPnL();
                    double dailyPnL = dailyPnLInitialized ? currentPnL - dailyPnLStart : 0;

                    LogInfo("✅ PRONTO V3",
                        $"🎯 Estratégia PRONTA para trading!\n" +
                        $"CurrentBar: {CurrentBar}, Score: {totalScore:F2}\n" +
                        $"Posição: {Position?.MarketPosition ?? MarketPosition.Flat}\n" +
                        $"PnL Diário: ${dailyPnL:F2}\n" +
                        $"PnL Total: ${currentPnL:F2}\n" +
                        $"Limites PnL: {(limitsActive ? "Ativos" : "Bloqueados")}\n" +
                        $"Sessão: {(sessionActive ? "Ativa" : "Inativa")}\n" +
                        $"ML Ativo: {(AtivarMLBasico ? "Sim" : "Não")}");
                    lastReadyCheck = DateTime.Now;
                }

                return true;
            }
            catch (Exception ex)
            {
                LogError("IsReadyForTrading", ex);
                return false;
            }
        }

        private void CheckAndUpdateTradingSession()
        {
            try
            {
                if (!AtivarHorarioFuncionamento)
                {
                    sessionActive = true;
                    return;
                }

                DateTime now = DateTime.Now;

                // Verificar se a sessão expirou
                if (sessionActive && now > sessionEndTime)
                {
                    sessionActive = false;
                    LogInfo("Sessão", $"⏰ Sessão de trading encerrada às {sessionEndTime:HH:mm}");

                    // Fechar todas as posições se configurado
                    if (FecharPosicoesNoFimHorario && Position != null && Position.MarketPosition != MarketPosition.Flat)
                    {
                        CloseAllPositions();
                        LogTrade("Fim Sessão", "Todas as posições fechadas - Fim do horário de trading");
                    }
                }

                // Verificar se é hora de iniciar nova sessão
                if (!sessionActive)
                {
                    // Se permitir sessão multidia, calcular próximo período
                    if (PermitirSessaoMultidia)
                    {
                        DateTime nextStart = sessionStartTime;
                        DateTime nextEnd = sessionEndTime;

                        // Encontrar próximo período de sessão
                        while (now > nextEnd)
                        {
                            nextStart = nextStart.AddDays(1);
                            nextEnd = nextEnd.AddDays(1);
                        }

                        if (now >= nextStart && now <= nextEnd)
                        {
                            sessionStartTime = nextStart;
                            sessionEndTime = nextEnd;
                            sessionActive = true;
                            LogInfo("Sessão", $"✅ Nova sessão iniciada: {sessionStartTime:HH:mm} - {sessionEndTime:HH:mm}");
                        }
                    }
                    else
                    {
                        // Sessão única por dia - verificar amanhã
                        DateTime tomorrow = now.Date.AddDays(1);
                        sessionStartTime = new DateTime(tomorrow.Year, tomorrow.Month, tomorrow.Day,
                            sessionStartTime.Hour, sessionStartTime.Minute, 0);
                        sessionEndTime = new DateTime(tomorrow.Year, tomorrow.Month, tomorrow.Day,
                            sessionEndTime.Hour, sessionEndTime.Minute, 0);
                    }
                }
            }
            catch (Exception ex)
            {
                LogError("CheckAndUpdateTradingSession", ex);
            }
        }

        private void ResetStrategyState()
        {
            try
            {
                lock (tradeLock)
                {
                    strategyInitialized = false;
                    indicatorsReady = false;
                    indicatorsInitializationAttempted = false;
                    tradeCounter = 0;
                    tradesToday = 0;
                    tradeDirection = MarketPosition.Flat;
                    isProcessingTrade = false;
                    lastTradeTime = DateTime.MinValue;
                    lastTradeDay = DateTime.MinValue;
                    trailingStopAtivado = false;
                    trailingStopPrice = 0;
                    lastProcessedTickPrice = 0;
                    lastTickTime = DateTime.MinValue;
                    lastStopSetAttempt = DateTime.MinValue;
                    lastReadyCheck = DateTime.MinValue;

                    // Resetar variáveis de limites PnL
                    dailyPnLStart = 0;
                    dailyPnLInitialized = false;
                    limitsActive = true;

                    // Resetar variáveis de sessão
                    sessionActive = false;

                    // Resetar sistema de breakout
                    breakoutDetectado = false;
                    breakoutLevel = 0;
                    breakoutTime = DateTime.MinValue;
                    squeezeCount = 0;
                    previousBBWidth = 0;

                    // Resetar sistema de saída inteligente
                    saidaParcialAtiva = false;
                    quantidadeSaidaParcial = 0;
                    ultimaVerificacaoSaida = DateTime.MinValue;
                    reversaoMomentumDetectada = false;
                    momentumHistory?.Clear();

                    // Manter dados ML (não resetar para preservar aprendizado)
                    // tradeStatistics e indicatorPerformance mantidos

                    // Resetar variáveis de volume
                    obvValue = 0;
                    obvHistory = new List<double>();
                    vwapValue = 0;
                    cumulativeTPV = 0;
                    cumulativeVolume = 0;

                    ResetStopTracking();

                    orderSubmissionTimes.Clear();
                    blockOrderQuantities.Clear();
                    blockFilledQuantities.Clear();

                    logBuilder.Clear();
                    lastLogFlush = DateTime.Now;
                    lastHeartbeat = DateTime.MinValue;
                    lastLimitLog = DateTime.MinValue;
                    lastScoreLog = DateTime.MinValue;

                    longSignal = false;
                    shortSignal = false;
                    block1Entered = false;
                    block2Entered = false;
                    block3Entered = false;
                    block1Filled = false;
                    block2Filled = false;
                    block3Filled = false;
                    entryPrice = 0;
                    target1Hit = false;
                    target2Hit = false;
                    target3Hit = false;
                    stopsSet = false;
                    totalQuantityExpected = 0;
                    totalQuantityFilled = 0;
                    totalScore = 0;
                    scoreBase = 0;

                    LogInfo("Reset", "Todos os estados resetados - Modo V3 com ML");
                }
            }
            catch (Exception ex)
            {
                LogError("ResetStrategyState", ex);
            }
        }

        private void ResetDailyCounters()
        {
            try
            {
                lock (tradeLock)
                {
                    // CORREÇÃO: Em modo histórico, usar Time[0] em vez de DateTime.Now
                    DateTime currentTime;
                    if (State == State.Historical && CurrentBar >= 0 && Bars != null && Bars.Count > 0)
                        currentTime = Time[0];
                    else
                        currentTime = DateTime.Now;
                    
                    DateTime today = currentTime.Date;

                    if (today != lastTradeDay.Date)
                    {
                        lastTradeDay = today;
                        tradesToday = 0;

                        // Resetar limites PnL se configurado
                        if (ResetLimitesPorDia && AtivarLimitesPnL)
                        {
                            dailyPnLStart = GetTotalAccountPnL();
                            dailyPnLInitialized = true;
                            limitsActive = true;
                            LogInfo("Reset Diário", $"Limites PnL resetados - Base: ${dailyPnLStart:F2}");
                        }

                        LogInfo("Reset Diário V3",
                            $"✅ Contadores ZERADOS\n" +
                            $"Data: {today:yyyy-MM-dd}\n" +
                            $"Trades hoje: {tradesToday}/{MaxTradesPerDay}\n" +
                            $"Limites PnL ativos: {limitsActive}\n" +
                            $"Hora: {currentTime:HH:mm:ss.fff}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogError("ResetDailyCounters", ex);
            }
        }

        private void ResetTradeState()
        {
            lock (tradeLock)
            {
                target1Hit = false;
                target2Hit = false;
                target3Hit = false;
                stopsSet = false;
                entryPrice = 0;
                trailingStopAtivado = false;
                trailingStopPrice = 0;
                lastProcessedTickPrice = 0;
                lastStopSetAttempt = DateTime.MinValue;
                ResetStopTracking();
                block1Entered = false;
                block2Entered = false;
                block3Entered = false;
                block1Filled = false;
                block2Filled = false;
                block3Filled = false;
                totalQuantityFilled = 0;
                blockFilledQuantities.Clear();
            }
        }

        private void ResetSignals()
        {
            lock (tradeLock)
            {
                longSignal = false;
                shortSignal = false;
            }
        }

        private void ResetStopTracking()
        {
            lastKnownStopPriceBlock1 = 0;
            lastKnownStopPriceBlock2 = 0;
            lastKnownStopPriceBlock3 = 0;
            highestPriceSinceEntry = 0;
            lowestPriceSinceEntry = 0;
            trailingStopAtivado = false;
            trailingStopPrice = 0;
            lastProcessedTickPrice = 0;
        }

        private void ResetTradeStateFull()
        {
            ResetTradeState();
            ResetSignals();
            tradeDirection = MarketPosition.Flat;
            isProcessingTrade = false;
            totalQuantityExpected = 0;
            totalQuantityFilled = 0;
            orderSubmissionTimes.Clear();
            blockOrderQuantities.Clear();
            blockFilledQuantities.Clear();
        }

        private void CheckAndMoveStopsTickByTick(double currentTickPrice)
        {
            if (entryPrice <= 0 || !stopsSet || Position == null) return;
            if (Position.MarketPosition == MarketPosition.Flat) return;

            double target1Price = CalculateTargetPrice(dynamicBlock1ProfitTargetTicks);

            // ATUALIZAR PREÇOS ALTOS/BAIXOS TICK-BY-TICK
            if (tradeDirection == MarketPosition.Long)
            {
                if (highestPriceSinceEntry == 0 || currentTickPrice > highestPriceSinceEntry)
                {
                    highestPriceSinceEntry = currentTickPrice;
                }
            }
            else // SHORT
            {
                if (lowestPriceSinceEntry == 0 || currentTickPrice < lowestPriceSinceEntry)
                {
                    lowestPriceSinceEntry = currentTickPrice;
                }
            }

            // ===== 1. VERIFICAÇÃO DE TARGET 1 TICK-BY-TICK =====
            if (!target1Hit)
            {
                bool target1Reached = (tradeDirection == MarketPosition.Long) ?
                    (currentTickPrice >= target1Price) : (currentTickPrice <= target1Price);

                if (target1Reached)
                {
                    target1Hit = true;
                    LogTrade("Target", $"🎯 Target 1 atingido TICK-BY-TICK @ {currentTickPrice:F2} (Target: {target1Price:F2})");

                    // ATIVAR TRAILING STOP APENAS SE BLOCO 2 OU 3 ESTIVEREM PREENCHIDOS
                    if (UsarTrailingStop && target1Hit)
                    {
                        // Verificar quantidade REAL nos blocos 2 e 3
                        int block2and3Quantity = GetQuantityInBlocks2And3();

                        if (block2and3Quantity > 0)
                        {
                            trailingStopAtivado = true;
                            LogInfo("Trailing Stop V3",
                                $"🚀 ATIVADO para {block2and3Quantity} contratos nos Blocos 2 e 3");
                        }
                    }
                }
            }

            // ===== 2. APLICAR TRAILING STOP TICK-BY-TICK =====
            if (UsarTrailingStop && target1Hit && trailingStopAtivado)
            {
                ApplyTrailingStopTickByTick(currentTickPrice);
            }

            // ===== 3. VERIFICAÇÃO DE TARGET 2 TICK-BY-TICK =====
            if (!target2Hit && target1Hit)
            {
                double target2Price = CalculateTargetPrice(dynamicBlock2ProfitTargetTicks);
                bool target2Reached = (tradeDirection == MarketPosition.Long) ?
                    (currentTickPrice >= target2Price) : (currentTickPrice <= target2Price);

                if (target2Reached)
                {
                    target2Hit = true;
                    LogTrade("Target", $"🎯🎯 Target 2 atingido TICK-BY-TICK @ {currentTickPrice:F2}");
                }
            }

            // ===== 4. VERIFICAÇÃO DE TARGET 3 TICK-BY-TICK =====
            if (!target3Hit && target2Hit)
            {
                double target3Price = CalculateTargetPrice(dynamicBlock3ProfitTargetTicks);
                bool target3Reached = (tradeDirection == MarketPosition.Long) ?
                    (currentTickPrice >= target3Price) : (currentTickPrice <= target3Price);

                if (target3Reached)
                {
                    target3Hit = true;
                    LogTrade("Target", $"🎯🎯🎯 Target 3 atingido TICK-BY-TICK @ {currentTickPrice:F2}");
                }
            }
        }

        private int GetQuantityInBlocks2And3()
        {
            try
            {
                int quantity = 0;

                // Usar quantidades preenchidas reais
                if (block2Filled && block2Entered)
                {
                    string block2Key = $"Block2_{tradeCounter}";
                    if (blockFilledQuantities.ContainsKey(block2Key))
                        quantity += blockFilledQuantities[block2Key];
                    else
                        quantity += Block2Quantity;
                }

                if (block3Filled && block3Entered)
                {
                    string block3Key = $"Block3_{tradeCounter}";
                    if (blockFilledQuantities.ContainsKey(block3Key))
                        quantity += blockFilledQuantities[block3Key];
                    else
                        quantity += Block3Quantity;
                }

                return quantity;
            }
            catch (Exception ex)
            {
                LogError("GetQuantityInBlocks2And3", ex);
                return 0;
            }
        }

        private void ApplyTrailingStopTickByTick(double currentTickPrice)
        {
            // Verificar se há quantidade REAL nos blocos 2 e 3
            bool aplicarParaBlock2 = block2Filled && block2Entered;
            bool aplicarParaBlock3 = block3Filled && block3Entered;

            if (!aplicarParaBlock2 && !aplicarParaBlock3)
            {
                if (trailingStopAtivado)
                {
                    trailingStopAtivado = false;
                    LogInfo("Trailing Stop", "Desativado - Nenhum bloco 2 ou 3 preenchido");
                }
                return;
            }

            if (tradeDirection == MarketPosition.Long)
            {
                // Se é a PRIMEIRA vez que aplica o trailing, inicializar
                if (trailingStopPrice == 0)
                {
                    trailingStopPrice = currentTickPrice - (TrailingStopDistanceTicks * TickSize);
                    LogTrade("Trailing Stop", $"📌 INICIADO @ {currentTickPrice:F2}, stop inicial: {trailingStopPrice:F2}");
                }

                // Calcular preço de ativação
                double trailActivationPrice = entryPrice + (TrailingStopActivationTicks * TickSize);

                if (currentTickPrice >= trailActivationPrice)
                {
                    double newTrailStop = currentTickPrice - (TrailingStopDistanceTicks * TickSize);

                    // Só move se for melhor que o stop atual
                    if (newTrailStop > trailingStopPrice + (MinTickMovementForTrailing * TickSize))
                    {
                        trailingStopPrice = newTrailStop;
                        LogTick("Trailing", currentTickPrice, $"Atualizado stop para {newTrailStop:F2}");

                        // APLICAR TRAILING STOP aos blocos preenchidos
                        if (aplicarParaBlock2)
                        {
                            string block2Name = "Block2_" + tradeCounter;
                            UpdateStopForBlockTickByTick(block2Name, newTrailStop);
                        }

                        if (aplicarParaBlock3)
                        {
                            string block3Name = "Block3_" + tradeCounter;
                            UpdateStopForBlockTickByTick(block3Name, newTrailStop);
                        }
                    }
                }
            }
            else // SHORT
            {
                // Se é a PRIMEIRA vez que aplica o trailing, inicializar
                if (trailingStopPrice == 0)
                {
                    trailingStopPrice = currentTickPrice + (TrailingStopDistanceTicks * TickSize);
                    LogTrade("Trailing Stop", $"📌 INICIADO @ {currentTickPrice:F2}, stop inicial: {trailingStopPrice:F2}");
                }

                double trailActivationPrice = entryPrice - (TrailingStopActivationTicks * TickSize);

                if (currentTickPrice <= trailActivationPrice)
                {
                    double newTrailStop = currentTickPrice + (TrailingStopDistanceTicks * TickSize);

                    // Para short, só move se for menor
                    if (newTrailStop < trailingStopPrice - (MinTickMovementForTrailing * TickSize))
                    {
                        trailingStopPrice = newTrailStop;
                        LogTick("Trailing", currentTickPrice, $"Atualizado stop para {newTrailStop:F2}");

                        // APLICAR TRAILING STOP aos blocos preenchidos
                        if (aplicarParaBlock2)
                        {
                            string block2Name = "Block2_" + tradeCounter;
                            UpdateStopForBlockTickByTick(block2Name, newTrailStop);
                        }

                        if (aplicarParaBlock3)
                        {
                            string block3Name = "Block3_" + tradeCounter;
                            UpdateStopForBlockTickByTick(block3Name, newTrailStop);
                        }
                    }
                }
            }
        }

        private bool UpdateStopForBlockTickByTick(string blockName, double newStopPrice)
        {
            try
            {
                // Buscar a ordem de stop mais recente
                var stopOrder = Orders.FirstOrDefault(o =>
                    o != null &&
                    o.Name == blockName &&
                    (o.OrderType == OrderType.StopMarket || o.OrderType == OrderType.StopLimit) &&
                    (o.OrderState == OrderState.Accepted ||
                     o.OrderState == OrderState.Working ||
                     o.OrderState == OrderState.Submitted));

                if (stopOrder == null)
                {
                    // Se não encontrou, criar nova ordem de stop
                    SetStopLoss(blockName, CalculationMode.Price, newStopPrice, false);
                    LogTick("Stop", newStopPrice, $"Novo stop criado para {blockName}");
                    return true;
                }

                // Verificar se precisa atualizar
                double priceDifference = Math.Abs(stopOrder.StopPrice - newStopPrice);
                if (priceDifference >= (MinTickMovementForTrailing * TickSize))
                {
                    // Modificar ordem existente
                    ChangeOrder(stopOrder, stopOrder.Quantity, newStopPrice, stopOrder.LimitPrice);

                    // Atualizar preços conhecidos
                    if (blockName.Contains("Block1"))
                        lastKnownStopPriceBlock1 = newStopPrice;
                    else if (blockName.Contains("Block2"))
                        lastKnownStopPriceBlock2 = newStopPrice;
                    else if (blockName.Contains("Block3"))
                        lastKnownStopPriceBlock3 = newStopPrice;

                    LogTick("Stop", newStopPrice, $"Stop {blockName} atualizado");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                LogError($"UpdateStopForBlockTickByTick {blockName}", ex);
                return false;
            }
        }

        private bool IsValidDataPoint(int barsAgo)
        {
            try
            {
                if (Bars == null || Bars.Count <= barsAgo || barsAgo < 0)
                    return false;

                if (barsAgo >= Bars.Count)
                    return false;

                double close = Close[barsAgo];
                double open = Open[barsAgo];
                double high = High[barsAgo];
                double low = Low[barsAgo];

                return !double.IsNaN(close) && !double.IsInfinity(close) &&
                       !double.IsNaN(open) && !double.IsInfinity(open) &&
                       !double.IsNaN(high) && !double.IsInfinity(high) &&
                       !double.IsNaN(low) && !double.IsInfinity(low) &&
                       close > 0 && open > 0 && high > 0 && low > 0 &&
                       high >= low && high >= close && high >= open &&
                       low <= close && low <= open;
            }
            catch
            {
                return false;
            }
        }

        // CORREÇÃO: Método IsIndicatorValid atualizado
        private bool IsIndicatorValid(object indicator, int barsAgo = 0)
        {
            if (indicator == null) return false;

            try
            {
                // CORREÇÃO: Verificar se o indicador tem dados (Count > 0) antes de tentar acessar valores
                if (indicator is IndicatorBase indicatorBase)
                {
                    if (indicatorBase.Count == 0)
                        return false; // Indicador não tem dados ainda
                    
                    if (CurrentBars[0] < indicatorBase.BarsRequiredToPlot)
                        return false; // Indicador ainda não tem barras suficientes
                }

                if (CurrentBar < barsAgo)
                    return false;

                double value = double.NaN;

                // CORREÇÃO: Usar operadores 'is' e 'as' em vez de pattern matching complexo
                if (indicator is EMA ema)
                    value = ema[barsAgo];
                else if (indicator is RSI rsiInd)
                    value = rsiInd[barsAgo];
                else if (indicator is MACD macdInd)
                    value = macdInd.Default[barsAgo];
                else if (indicator is ATR atrInd)
                    value = atrInd[barsAgo];
                else if (indicator is Bollinger bbInd)
                    value = bbInd.Upper[barsAgo];
                else if (indicator is SMA smaInd)
                    value = smaInd[barsAgo];
                else if (indicator is Stochastics stochInd)
                    value = stochInd.K[barsAgo];
                else if (indicator is ADX adxInd)
                    value = adxInd[barsAgo];
                else if (indicator is NinjaTrader.NinjaScript.Indicators.VWAP vwapInd)
                    value = vwapInd[barsAgo];
                else if (indicator is DonchianChannel donchianInd)
                    value = donchianInd.Upper[barsAgo];
                else if (indicator is StdDev stdDevInd)
                    value = stdDevInd[barsAgo];
                else if (indicator is GlitchIndicator glitchInd)
                {
                    // CORREÇÃO: Verificar se GlitchIndicator tem dados antes de acessar
                    if (glitchInd.Count == 0)
                        return false;
                    // Tentar acessar BandWidth como verificação
                    try
                    {
                        value = glitchInd.BandWidth[barsAgo];
                    }
                    catch
                    {
                        return false;
                    }
                }
                else
                    return false;

                return !double.IsNaN(value) && !double.IsInfinity(value);
            }
            catch
            {
                return false;
            }
        }

        // CORREÇÃO: Método GetIndicatorValue atualizado
        // CORREÇÃO: Método GetIndicatorValue simplificado sem VWAP problemático
        private double GetIndicatorValue(object indicator, int barsAgo, double defaultValue = 0)
        {
            try
            {
                if (indicator == null)
                    return defaultValue;

                if (CurrentBar < barsAgo)
                    return defaultValue;

                double value = double.NaN;

                // Usar operadores 'is' e cast simples
                if (indicator is EMA ema)
                    value = ema[barsAgo];
                else if (indicator is RSI rsiInd)
                    value = rsiInd[barsAgo];
                else if (indicator is MACD macdInd)
                    value = macdInd.Default[barsAgo];
                else if (indicator is GlitchIndicator glitchInd)
                    value = glitchInd.Values[2][barsAgo]; // VWAP plot
                else if (indicator is SMA smaInd)
                    value = smaInd[barsAgo];
                else if (indicator is Stochastics stochInd)
                    value = stochInd.K[barsAgo];
                else if (indicator is DonchianChannel donchianInd)
                    value = donchianInd.Upper[barsAgo];
                else if (indicator is StdDev stdDevInd)
                    value = stdDevInd[barsAgo];
                else
                    return defaultValue;

                return !double.IsNaN(value) && !double.IsInfinity(value) ? value : defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        private double GetCurrentMarketPrice()
        {
            return lastProcessedTickPrice > 0 ? lastProcessedTickPrice : Close[0];
        }

        private double CalculateTargetPrice(int targetTicks)
        {
            if (entryPrice <= 0) return 0;

            if (tradeDirection == MarketPosition.Long)
                return entryPrice + (targetTicks * TickSize);
            else
                return entryPrice - (targetTicks * TickSize);
        }

        private void CancelStopOrder(string signalName)
        {
            try
            {
                var ordersArray = Orders.ToArray();
                foreach (Order order in ordersArray)
                {
                    if (order != null &&
                        order.Name == signalName &&
                        (order.OrderType == OrderType.StopMarket || order.OrderType == OrderType.StopLimit) &&
                        (order.OrderState == OrderState.Working ||
                         order.OrderState == OrderState.Accepted ||
                         order.OrderState == OrderState.Submitted))
                    {
                        CancelOrder(order);
                        LogInfo("Ordem", $"Stop cancelado: {signalName}");
                        orderSubmissionTimes.Remove(signalName);
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"Cancelar stop {signalName}", ex);
            }
        }

        private bool HasMinimumTimePassed()
        {
            // CORREÇÃO CRÍTICA: Se MinTimeBetweenTrades = 0, sempre retornar true
            // Isso evita problemas de comparação quando não há restrição de tempo
            // Em backtest histórico, isso é especialmente importante pois todas as barras são processadas rapidamente
            if (MinTimeBetweenTrades == 0)
            {
                // Log diagnóstico em backtest para confirmar que está retornando true
                if (State == State.Historical && CurrentBar % 1000 == 0 && EnableReadyLogging)
                {
                    LogInfo("Tempo Mínimo", $"✅ MinTimeBetweenTrades = 0 → Sempre retorna true (Bar: {CurrentBar})");
                }
                return true;
            }
            
            // CORREÇÃO CRÍTICA: Se nunca houve trade, sempre permitir
            if (lastTradeTime == DateTime.MinValue) return true;
            
            // CORREÇÃO CRÍTICA: Em modo histórico (backtest), usar Time[0] em vez de DateTime.Now
            // Em backtest, todas as barras são processadas muito rapidamente (milissegundos),
            // mas cada barra tem um Time[0] diferente (representando o tempo real da barra)
            DateTime currentTime;
            if (State == State.Historical && CurrentBar >= 0 && Bars != null && Bars.Count > 0)
            {
                currentTime = Time[0]; // Usar tempo da barra atual em backtest
            }
            else
            {
                currentTime = DateTime.Now; // Usar tempo real apenas em Realtime
            }
            
            TimeSpan timeSinceLastTrade = currentTime - lastTradeTime;
            
            // CORREÇÃO CRÍTICA: Verificar se timeSinceLastTrade é negativo (pode acontecer em backtest)
            // Isso pode ocorrer se lastTradeTime foi setado incorretamente ou se há problema com Time[0]
            if (timeSinceLastTrade.TotalMinutes < 0)
            {
                // Em backtest, se timeSinceLastTrade é negativo, pode ser problema de inicialização
                // Retornar true para não bloquear trades
                LogWarning("Tempo Mínimo", $"TimeSinceLastTrade negativo em backtest: {timeSinceLastTrade.TotalMinutes:F2} min - Permitindo trade");
                return true;
            }
            
            bool passed = timeSinceLastTrade.TotalMinutes >= MinTimeBetweenTrades;
            
            // Log diagnóstico em backtest quando bloqueia
            if (State == State.Historical && !passed && EnableReadyLogging)
            {
                LogInfo("Tempo Mínimo", 
                    $"⏳ Backtest: Aguardando tempo mínimo - TimeSinceLastTrade: {timeSinceLastTrade.TotalMinutes:F2} min, " +
                    $"Required: {MinTimeBetweenTrades} min, CurrentBar: {CurrentBar}, CurrentTime: {currentTime:HH:mm:ss}, " +
                    $"LastTradeTime: {lastTradeTime:HH:mm:ss}");
            }
            
            return passed;
        }

        private double CalculateWeightedRewardRiskRatio()
        {
            if (Block1Quantity == 0 && Block2Quantity == 0 && Block3Quantity == 0)
                return 0;

            double totalPotentialProfit =
                (Block1Quantity * dynamicBlock1ProfitTargetTicks * TickSize) +
                (Block2Quantity * dynamicBlock2ProfitTargetTicks * TickSize) +
                (Block3Quantity * dynamicBlock3ProfitTargetTicks * TickSize);

            double totalQuantity = Block1Quantity + Block2Quantity + Block3Quantity;

            if (totalQuantity == 0)
                return 0;

            double totalPotentialLoss =
                (Block1Quantity * dynamicBlock1StopLossTicks * TickSize) +
                (Block2Quantity * dynamicBlock2StopLossTicks * TickSize) +
                (Block3Quantity * dynamicBlock3StopLossTicks * TickSize);

            double averageRewardTicks = totalPotentialProfit / totalQuantity / TickSize;
            double averageRiskTicks = totalPotentialLoss / totalQuantity / TickSize;

            if (averageRiskTicks == 0)
                return double.MaxValue;

            return averageRewardTicks / averageRiskTicks;
        }

        private void CalculateTradePotential()
        {
            if (Block1Quantity == 0 && Block2Quantity == 0 && Block3Quantity == 0)
                return;

            double totalQuantity = Block1Quantity + Block2Quantity + Block3Quantity;

            double totalProfitTicks =
                (Block1Quantity * dynamicBlock1ProfitTargetTicks) +
                (Block2Quantity * dynamicBlock2ProfitTargetTicks) +
                (Block3Quantity * dynamicBlock3ProfitTargetTicks);

            double totalLossTicks =
                (Block1Quantity * dynamicBlock1StopLossTicks) +
                (Block2Quantity * dynamicBlock2StopLossTicks) +
                (Block3Quantity * dynamicBlock3StopLossTicks);

            double tickValue = Instrument.MasterInstrument.PointValue * TickSize;
            double potentialProfitValue = totalProfitTicks * tickValue;
            double potentialLossValue = totalLossTicks * tickValue;

            LogTrade("Análise V3",
                $"Quantidades: {Block1Quantity}+{Block2Quantity}+{Block3Quantity} = {totalQuantity} contratos\n" +
                $"SL: {dynamicBlock1StopLossTicks} ticks\n" +
                $"TPs: {dynamicBlock1ProfitTargetTicks}/{dynamicBlock2ProfitTargetTicks}/{dynamicBlock3ProfitTargetTicks} ticks\n" +
                $"Trailing Stop: Ativa após TP1 (+{TrailingStopActivationTicks} ticks)\n" +
                $"Potencial: Profit=${potentialProfitValue:F2}, Loss=${potentialLossValue:F2}");
        }

        private void CleanupExpiredOrders()
        {
            try
            {
                List<string> keysToRemove = new List<string>();
                List<Order> ordersToCancel = new List<Order>();

                lock (tradeLock)
                {
                    var ordersArray = Orders.ToArray();
                    foreach (var order in ordersArray)
                    {
                        if (order != null &&
                            order.Name.Contains("Block") &&
                            IsOrderTimedOut(order.Name) &&
                            (order.OrderState == OrderState.Accepted ||
                             order.OrderState == OrderState.Working))
                        {
                            ordersToCancel.Add(order);
                            keysToRemove.Add(order.Name);
                        }
                    }
                }

                foreach (var order in ordersToCancel)
                {
                    try
                    {
                        CancelOrder(order);
                        LogInfo("Ordem Expirada", $"Cancelando ordem: {order.Name}");
                    }
                    catch (Exception ex)
                    {
                        LogError($"CancelOrder {order.Name}", ex);
                    }
                }

                lock (tradeLock)
                {
                    foreach (string key in keysToRemove)
                    {
                        if (orderSubmissionTimes.ContainsKey(key))
                            orderSubmissionTimes.Remove(key);
                    }
                    
                    // CORREÇÃO: Resetar flag se não há mais ordens pendentes e não há posição
                    if (keysToRemove.Count > 0)
                    {
                        // Verificar se ainda há ordens pendentes deste trade
                        bool hasPendingOrders = false;
                        var ordersArray = Orders.ToArray();
                        foreach (var order in ordersArray)
                        {
                            if (order != null && 
                                order.Name.Contains("Block") &&
                                order.Name.Contains(tradeCounter.ToString()) &&
                                (order.OrderState == OrderState.Accepted ||
                                 order.OrderState == OrderState.Working ||
                                 order.OrderState == OrderState.Submitted))
                            {
                                hasPendingOrders = true;
                                break;
                            }
                        }
                        
                        // Se não há ordens pendentes e não há posição, resetar flag
                        if (!hasPendingOrders && (Position == null || Position.MarketPosition == MarketPosition.Flat))
                        {
                            isProcessingTrade = false;
                            LogInfo("Cleanup", "Flag isProcessingTrade resetada - Todas as ordens foram canceladas/expiradas");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogError("CleanupExpiredOrders", ex);
            }
        }

        private bool IsOrderModifiable(Order order)
        {
            return order != null &&
                   (order.OrderState == OrderState.Accepted ||
                    order.OrderState == OrderState.Working) &&
                   order.OrderState != OrderState.ChangePending &&
                   order.OrderState != OrderState.PartFilled;
        }

        private bool IsOrderTimedOut(string signalName)
        {
            if (orderSubmissionTimes.ContainsKey(signalName))
            {
                return (DateTime.Now - orderSubmissionTimes[signalName]).TotalSeconds > ORDER_TIMEOUT_SECONDS;
            }
            return false;
        }

        private void SendHeartbeat()
        {
            if ((DateTime.Now - lastHeartbeat).TotalMinutes >= HEARTBEAT_INTERVAL_MINUTES)
            {
                double currentPnL = GetTotalAccountPnL();
                double dailyPnL = dailyPnLInitialized ? currentPnL - dailyPnLStart : 0;

                LogInfo("Heartbeat V3",
                    $"Modo: V3 AVANÇADO, Estado: {State}, " +
                    $"Posição: {Position?.MarketPosition ?? MarketPosition.Flat}, " +
                    $"Quantidade: {Position?.Quantity ?? 0}, " +
                    $"Trades Hoje: {tradesToday}/{MaxTradesPerDay}, " +
                    $"CurrentBar: {CurrentBar}, " +
                    $"Score: {totalScore:F2}, " +
                    $"PnL Diário: ${dailyPnL:F2}, " +
                    $"PnL Total: ${currentPnL:F2}, " +
                    $"Limites: {(limitsActive ? "Ativos" : "Bloqueados")}, " +
                    $"Sessão: {(sessionActive ? "Ativa" : "Inativa")}, " +
                    $"ML: {tradeStatistics.Count} trades coletados");
                lastHeartbeat = DateTime.Now;
            }
        }

        private void CloseAllPositions()
        {
            try
            {
                if (Position == null || Position.MarketPosition == MarketPosition.Flat)
                    return;

                if (Position.MarketPosition == MarketPosition.Long)
                {
                    ExitLong();
                }
                else if (Position.MarketPosition == MarketPosition.Short)
                {
                    ExitShort();
                }

                LogInfo("CloseAll", $"Posição {Position?.MarketPosition} fechada");
            }
            catch (Exception ex)
            {
                LogError("CloseAllPositions", ex);
            }
        }

        private void CancelAllPendingOrders()
        {
            try
            {
                var ordersArray = Orders.ToArray();
                int canceledCount = 0;

                foreach (var order in ordersArray)
                {
                    if (order != null &&
                        (order.OrderState == OrderState.Accepted ||
                         order.OrderState == OrderState.Working ||
                         order.OrderState == OrderState.Submitted))
                    {
                        CancelOrder(order);
                        canceledCount++;
                    }
                }

                if (canceledCount > 0)
                {
                    LogInfo("CancelAll", $"{canceledCount} ordens pendentes canceladas");
                }
            }
            catch (Exception ex)
            {
                LogError("CancelAllPendingOrders", ex);
            }
        }

        private void CheckAndRecoverStrategy()
        {
            try
            {
                var orphanedOrders = Orders.Where(o =>
                    o != null &&
                    o.OrderState == OrderState.Working &&
                    (DateTime.Now - o.Time).TotalMinutes > 5 &&
                    !orderSubmissionTimes.ContainsKey(o.Name)).ToArray();

                if (orphanedOrders.Length > 0)
                {
                    LogWarning("Recovery", $"Encontradas {orphanedOrders.Length} ordens órfãs");

                    foreach (var order in orphanedOrders)
                    {
                        CancelOrder(order);
                        LogInfo("Recovery", $"Cancelada ordem órfã: {order.Name}");
                    }
                }

                if (Position != null && Position.MarketPosition != MarketPosition.Flat)
                {
                    if (!stopsSet && Position.Quantity > 0)
                    {
                        LogWarning("Recovery", "Posição ativa sem stops setados - Recuperando");
                        SetStopsAndTargets();
                    }
                }
                else if (stopsSet)
                {
                    LogWarning("Recovery", "Estado inconsistente: Flat mas stopsSet=true");
                    ResetTradeStateFull();
                }
            }
            catch (Exception ex)
            {
                LogError("CheckAndRecoverStrategy", ex);
            }
        }

        private void LogTick(string action, double price, string details = "")
        {
            if (!EnableTickLogging) return;

            string logEntry = $"{DateTime.Now:HH:mm:ss.fff} [TICK] [{action}] @{price:F2} {details}";

            lock (logBuilder)
            {
                logBuilder.AppendLine(logEntry);
            }

            if (EnableStructuredLogging && logBuilder.Length % 100 == 0)
            {
                Print(logEntry);
            }
            FlushLogIfNeeded();
        }

        private void LogScoreDetalhado()
        {
            if (!EnableScoreLogging) return;

            // Limitar frequência dos logs de score
            if ((DateTime.Now - lastScoreLog).TotalSeconds < ScoreLogIntervalSeconds)
                return;

            lastScoreLog = DateTime.Now;

            string logEntry = $"{DateTime.Now:HH:mm:ss.fff} [SCORE] Total: {totalScore:F2} | Base: {scoreBase:F2} | " +
                             $"Long>{(UsarThresholdsDinamicos ? longThresholdDynamic.ToString("F2") : LongThreshold.ToString())} | " +
                             $"Short<{(UsarThresholdsDinamicos ? shortThresholdDynamic.ToString("F2") : ShortThreshold.ToString())}";

            lock (logBuilder)
            {
                logBuilder.AppendLine(logEntry);
            }

            Print($"📊 {logEntry}");
            FlushLogIfNeeded();
        }

        // CORREÇÃO: Adicionar método OnBarUpdate para suporte a indicadores
        // CORREÇÃO: Método OnBarUpdate atualizado
        protected override void OnBarUpdate()
        {
            try
            {
                // Log diagnóstico nas primeiras barras para entender o fluxo
                if (State == State.Historical && CurrentBar >= 0 && CurrentBar < 10)
                {
                    LogInfo("🔍 OnBarUpdate Chamado", 
                        $"Bar: {CurrentBar}, State: {State}, Bars.Count: {Bars?.Count ?? 0}\n" +
                        $"strategyInitialized: {strategyInitialized}, indicatorsReady: {indicatorsReady}\n" +
                        $"indicatorsInitializationAttempted: {indicatorsInitializationAttempted}");
                }
                
                // Base - manter funcionalidade básica
                base.OnBarUpdate();

                // Inicializar indicadores se necessário (tanto em Realtime quanto Historical)
                // CORREÇÃO CRÍTICA: Se indicadores não foram inicializados (null), chamar InitializeIndicators()
                // mesmo que indicatorsInitializationAttempted seja true (pode ter tentado quando CurrentBar era -1)
                if (!strategyInitialized && (State == State.Realtime || State == State.Historical) && CurrentBar >= 0)
                {
                    // Verificar se indicadores realmente foram criados
                    bool indicadoresNaoCriados = (glitch == null || emaRapida == null || emaLenta == null || 
                                                  emaTendencia == null || emaSuperLenta == null || rsi == null || macd == null);
                    
                    if (indicadoresNaoCriados)
                    {
                        // Indicadores não foram criados - chamar InitializeIndicators() novamente
                        LogInfo("⚠️ Reinicializando Indicadores", 
                            $"Indicadores não foram criados anteriormente - chamando InitializeIndicators() novamente\n" +
                            $"CurrentBar: {CurrentBar}, indicatorsInitializationAttempted: {indicatorsInitializationAttempted}");
                        InitializeIndicators();
                    }
                    else if (CurrentBar < 100)
                    {
                        // Nas primeiras 100 barras, verificar a cada barra se está pronto
                        indicatorsReady = CheckIndicatorsReady();
                        if (indicatorsReady)
                        {
                            strategyInitialized = true;
                            LogInfo("✅ INDICADORES PRONTOS", 
                                $"Indicadores ficaram prontos em OnBarUpdate - CurrentBar: {CurrentBar}, State: {State}");
                        }
                    }
                    else if (CurrentBar % 100 == 0)
                    {
                        // Após 100 barras, verificação periódica silenciosa
                        indicatorsReady = CheckIndicatorsReady();
                        if (indicatorsReady)
                        {
                            strategyInitialized = true;
                            LogInfo("✅ INDICADORES PRONTOS", 
                                $"Indicadores ficaram prontos em verificação periódica - CurrentBar: {CurrentBar}");
                        }
                    }
                }

                // Verificar e atualizar status de indicadores periodicamente
                // CORREÇÃO: Verificar desde CurrentBar >= 0, não apenas >= 15
                // REMOVIDO: Este bloco duplicava a verificação acima, causando logs duplicados
                // A verificação já é feita no bloco anterior quando CurrentBar < 100

                // Em modo histórico, processar lógica de trading no OnBarUpdate
                // (em Realtime, isso é feito no OnMarketData via ProcessTickByTickLogic)
                if (State == State.Historical && indicatorsReady && IsValidDataPoint(0))
                {
                    // Marcar que realmente processou barras
                    if (CurrentBar > 10)
                    {
                        realmenteProcessouBarras = true;
                    }
                    
                    // Processar lógica de trading em modo histórico
                    // Log periódico para debug em backtest
                    if (CurrentBar % 100 == 0)
                    {
                        LogInfo("Backtest", 
                            $"Processando barra {CurrentBar}: Ready={IsReadyForTrading()}, Score={totalScore:F2}, " +
                            $"Position={Position?.MarketPosition ?? MarketPosition.Flat}");
                    }
                    ProcessHistoricalTradingLogic();
                }

                // Atualizar cálculos de indicadores dependentes de barras
                if (IsValidDataPoint(0) && indicatorsReady)
                {
                    // Atualizar cálculos de volume
                    if (AtivarVolume && usarVolumeAvancado)
                    {
                        // Atualizar Volume Profile
                        try
                        {
                            if (volumeProfile != null)
                            {
                                volumeProfile.Update(High[0], Low[0], Volume[0], Time[0]);
                            }
                        }
                        catch (Exception ex)
                        {
                            LogError("Volume Profile Update", ex);
                        }
                    }

                    // Atualizar thresholds dinâmicos periodicamente (apenas em Realtime)
                    // Em histórico, thresholds são calculados quando necessário
                    if (State == State.Realtime && UsarThresholdsDinamicos && DateTime.Now.Minute % 15 == 0 && DateTime.Now.Second == 0)
                    {
                        CalcularThresholdsDinamicos();
                    }
                }
            }
            catch (Exception ex)
            {
                LogError("OnBarUpdate", ex);
            }
        }

        #endregion

        #region Propriedades - TODAS AS NOVAS
        // ===== PARÂMETROS EXISTENTES (mantidos da V2) =====
        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "Block 1 Quantity", Order = 1, GroupName = "1. Blocos de Entrada")]
        public int Block1Quantity { get; set; }

        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "Block 2 Quantity", Order = 2, GroupName = "1. Blocos de Entrada")]
        public int Block2Quantity { get; set; }

        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "Block 3 Quantity", Order = 3, GroupName = "1. Blocos de Entrada")]
        public int Block3Quantity { get; set; }

        [NinjaScriptProperty]
        [Range(5, 1000)]
        [Display(Name = "Block 1 Stop Loss Ticks", Order = 4, GroupName = "1. Blocos de Entrada")]
        public int Block1StopLossTicks { get; set; }

        [NinjaScriptProperty]
        [Range(5, 1000)]
        [Display(Name = "Block 1 Profit Target Ticks", Order = 5, GroupName = "1. Blocos de Entrada")]
        public int Block1ProfitTargetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(5, 1000)]
        [Display(Name = "Block 2 Stop Loss Ticks", Order = 6, GroupName = "1. Blocos de Entrada")]
        public int Block2StopLossTicks { get; set; }

        [NinjaScriptProperty]
        [Range(5, 1000)]
        [Display(Name = "Block 2 Profit Target Ticks", Order = 7, GroupName = "1. Blocos de Entrada")]
        public int Block2ProfitTargetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(5, 1000)]
        [Display(Name = "Block 3 Stop Loss Ticks", Order = 8, GroupName = "1. Blocos de Entrada")]
        public int Block3StopLossTicks { get; set; }

        [NinjaScriptProperty]
        [Range(5, 1000)]
        [Display(Name = "Block 3 Profit Target Ticks", Order = 9, GroupName = "1. Blocos de Entrada")]
        public int Block3ProfitTargetTicks { get; set; }

        // ===== GERENCIAMENTO DE RISCO =====
        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "SL Buffer Ticks", Order = 10, GroupName = "2. Gerenciamento de Risco")]
        public int SLBufferTicks { get; set; }

        [NinjaScriptProperty]
        [Range(500, 5000)]
        [Display(Name = "Margem por Contrato MNQ (USD)", Order = 11, GroupName = "2. Gerenciamento de Risco")]
        public int MNQMarginPerContract { get; set; }

        // ===== LIMITES DE PERDA E GANHO =====
        [NinjaScriptProperty]
        [Display(Name = "Ativar Limites PnL", Order = 12, GroupName = "2.1 Limites PnL")]
        public bool AtivarLimitesPnL { get; set; }

        [NinjaScriptProperty]
        [Range(-10000, 0)]
        [Display(Name = "Limite Perda Diária (USD)", Order = 13, GroupName = "2.1 Limites PnL")]
        public double LimitePerdaDiaria { get; set; }

        [NinjaScriptProperty]
        [Range(0, 10000)]
        [Display(Name = "Limite Ganho Diário (USD)", Order = 14, GroupName = "2.1 Limites PnL")]
        public double LimiteGanhoDiario { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Reset Limites por Dia", Order = 15, GroupName = "2.1 Limites PnL")]
        public bool ResetLimitesPorDia { get; set; }

        // ===== HORÁRIO DE FUNCIONAMENTO =====
        [NinjaScriptProperty]
        [Display(Name = "Ativar Horário Funcionamento", Order = 16, GroupName = "2.2 Horário Trading")]
        public bool AtivarHorarioFuncionamento { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "Hora Início (HHmm)", Order = 17, GroupName = "2.2 Horário Trading")]
        public int HoraInicioFuncionamento { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "Hora Fim (HHmm)", Order = 18, GroupName = "2.2 Horário Trading")]
        public int HoraFimFuncionamento { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Fechar Posições no Fim", Order = 19, GroupName = "2.2 Horário Trading")]
        public bool FecharPosicoesNoFimHorario { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Permitir Sessão Multidia", Order = 20, GroupName = "2.2 Horário Trading")]
        public bool PermitirSessaoMultidia { get; set; }

        // ===== SISTEMA DE PESOS - TENDÊNCIA (30%) =====
        [NinjaScriptProperty]
        [Display(Name = "Ativar Sistema Tendência", Order = 21, GroupName = "3.1 Tendência (30%)")]
        public bool AtivarSistemaTendencia { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "EMA Rápida", Order = 22, GroupName = "3.1 Tendência (30%)")]
        public int EmaRapidaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "EMA Lenta", Order = 23, GroupName = "3.1 Tendência (30%)")]
        public int EmaLentaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "EMA Tendência", Order = 24, GroupName = "3.1 Tendência (30%)")]
        public int EmaTendenciaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "EMA Super Lenta", Order = 25, GroupName = "3.1 Tendência (30%)")]
        public int EmaSuperLentaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 5.0)]
        [Display(Name = "Peso Tendência", Order = 26, GroupName = "3.1 Tendência (30%)")]
        public double PesoTendencia { get; set; }

        // ===== SISTEMA DE PESOS - MOMENTUM NORMALIZADO (30%) =====
        [NinjaScriptProperty]
        [Display(Name = "Ativar Momentum", Order = 27, GroupName = "3.2 Momentum (30%)")]
        public bool AtivarMomentum { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "RSI Período", Order = 28, GroupName = "3.2 Momentum (30%)")]
        public int RsiPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "RSI Oversold", Order = 29, GroupName = "3.2 Momentum (30%)")]
        public int RsiSobrevendido { get; set; }

        [NinjaScriptProperty]
        [Range(50, 100)]
        [Display(Name = "RSI Overbought", Order = 30, GroupName = "3.2 Momentum (30%)")]
        public int RsiSobrecomprado { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "MACD Fast", Order = 31, GroupName = "3.2 Momentum (30%)")]
        public int MacdFast { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "MACD Slow", Order = 32, GroupName = "3.2 Momentum (30%)")]
        public int MacdSlow { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "MACD Signal", Order = 33, GroupName = "3.2 Momentum (30%)")]
        public int MacdSignal { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 5.0)]
        [Display(Name = "Peso Momentum", Order = 34, GroupName = "3.2 Momentum (30%)")]
        public double PesoMomentum { get; set; }

        // ===== SISTEMA DE PESOS - VOLUME (20%) =====
        [NinjaScriptProperty]
        [Display(Name = "Ativar Volume", Order = 35, GroupName = "3.3 Volume (20%)")]
        public bool AtivarVolume { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Volume MA Length", Order = 36, GroupName = "3.3 Volume (20%)")]
        public int VolumeMALength { get; set; }

        [NinjaScriptProperty]
        [Range(0.5, 3.0)]
        [Display(Name = "Volume Threshold", Order = 37, GroupName = "3.3 Volume (20%)")]
        public double VolumeThreshold { get; set; } = 1.2;

        [NinjaScriptProperty]
        [Range(0.1, 5.0)]
        [Display(Name = "Peso Volume", Order = 38, GroupName = "3.3 Volume (20%)")]
        public double PesoVolume { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Usar Volume Avançado", Order = 39, GroupName = "3.3 Volume (20%)")]
        public bool UsarVolumeAvancado { get; set; }

        [NinjaScriptProperty]
        [Range(5, 100)]
        [Display(Name = "Volume Profile Period", Order = 40, GroupName = "3.3 Volume (20%)")]
        public int VolumeProfilePeriod { get; set; }

        // ===== SISTEMA DE PESOS - VOLATILIDADE (20%) =====
        [NinjaScriptProperty]
        [Display(Name = "Ativar Volatilidade", Order = 41, GroupName = "3.4 Volatilidade (20%)")]
        public bool AtivarVolatilidade { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "ATR Length", Order = 42, GroupName = "3.4 Volatilidade (20%)")]
        public int AtrLength { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Bollinger Length", Order = 43, GroupName = "3.4 Volatilidade (20%)")]
        public int BollingerLength { get; set; }

        [NinjaScriptProperty]
        [Range(1, 5)]
        [Display(Name = "BB Std Dev", Order = 44, GroupName = "3.4 Volatilidade (20%)")]
        public int BollingerStdDev { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 5.0)]
        [Display(Name = "Peso Volatilidade", Order = 45, GroupName = "3.4 Volatilidade (20%)")]
        public double PesoVolatilidade { get; set; }

        // ===== THRESHOLDS DINÂMICOS =====
        [NinjaScriptProperty]
        [Display(Name = "Usar Thresholds Dinâmicos", Order = 46, GroupName = "4.1 Thresholds Dinâmicos")]
        public bool UsarThresholdsDinamicos { get; set; }

        [NinjaScriptProperty]
        // CORREÇÃO: Range ajustado de [10, 50] para [1, 50] para permitir valores menores (5) para timeframe 1min
        [Range(1, 50)]
        [Display(Name = "Sensibilidade Threshold (%)", Order = 47, GroupName = "4.1 Thresholds Dinâmicos")]
        public int SensibilidadeThreshold { get; set; }

        [NinjaScriptProperty]
        [Range(2.0, 10.0)]
        [Display(Name = "Max Score Teórico", Order = 48, GroupName = "4.1 Thresholds Dinâmicos")]
        public double MaxScoreTeorico { get; set; }

        // ===== SISTEMA DE BREAKOUT =====
        [NinjaScriptProperty]
        [Display(Name = "Ativar Sistema Breakout", Order = 49, GroupName = "4.2 Sistema Breakout")]
        public bool AtivarSistemaBreakout { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 2.0)]
        [Display(Name = "Peso Breakout", Order = 50, GroupName = "4.2 Sistema Breakout")]
        public double PesoBreakout { get; set; }

        [NinjaScriptProperty]
        [Range(10, 50)]
        [Display(Name = "Breakout Lookback Period", Order = 51, GroupName = "4.2 Sistema Breakout")]
        public int BreakoutLookbackPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 0.5)]
        [Display(Name = "Squeeze Threshold", Order = 52, GroupName = "4.2 Sistema Breakout")]
        public double SqueezeThreshold { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 3.0)]
        [Display(Name = "Min Breakout Volume Multiplier", Order = 53, GroupName = "4.2 Sistema Breakout")]
        public double MinBreakoutVolumeMultiplier { get; set; }

        // ===== SISTEMA DE VOLUME APRIMORADO =====
        [NinjaScriptProperty]
        [Display(Name = "Usar Volume Climax", Order = 54, GroupName = "4.3 Volume Aprimorado")]
        public bool UsarVolumeClimax { get; set; }

        [NinjaScriptProperty]
        [Range(80, 99)]
        [Display(Name = "Volume Percentil Climax", Order = 55, GroupName = "4.3 Volume Aprimorado")]
        public int VolumePercentilClimax { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Usar VWAP Bands", Order = 56, GroupName = "4.3 Volume Aprimorado")]
        public bool UsarVWAPBands { get; set; }

        [NinjaScriptProperty]
        [Range(0.5, 2.0)]
        [Display(Name = "VWAP Band Std Dev", Order = 57, GroupName = "4.3 Volume Aprimorado")]
        public double VWAPBandStdDev { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Usar Volume Weighted MACD", Order = 58, GroupName = "4.3 Volume Aprimorado")]
        public bool UsarVolumeWeightedMACD { get; set; }

        // ===== CONFIRMAÇÃO MULTI-TIMEFRAME =====
        [NinjaScriptProperty]
        [Display(Name = "Usar Multi-Timeframe", Order = 59, GroupName = "4.4 Confirmação Multi-Timeframe")]
        public bool UsarMultiTimeframe { get; set; }

        [NinjaScriptProperty]
        [Range(1, 60)]
        [Display(Name = "Secondary Timeframe Value", Order = 60, GroupName = "4.4 Confirmação Multi-Timeframe")]
        public int SecondaryTimeframeValue { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Secondary Timeframe Type", Order = 61, GroupName = "4.4 Confirmação Multi-Timeframe")]
        public BarsPeriodType SecondaryTimeframeType { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Requerir Confirmação Multi-Timeframe", Order = 62, GroupName = "4.4 Confirmação Multi-Timeframe")]
        public bool RequerirConfirmacaoMultiTimeframe { get; set; }

        // ===== SISTEMA DE SL/TP DINÂMICO =====
        [NinjaScriptProperty]
        [Display(Name = "Usar Sistema ATR", Order = 63, GroupName = "5. SL/TP Dinâmico")]
        public bool UsarSistemaATR { get; set; }

        [NinjaScriptProperty]
        [Range(0.5, 5.0)]
        [Display(Name = "SL (xATR)", Order = 64, GroupName = "5. SL/TP Dinâmico")]
        public double SlAtrMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0.5, 10.0)]
        [Display(Name = "TP Block1 (xATR)", Order = 65, GroupName = "5. SL/TP Dinâmico")]
        public double TpAtrMultiplierBlock1 { get; set; }

        [NinjaScriptProperty]
        [Range(0.5, 10.0)]
        [Display(Name = "TP Block2 (xATR)", Order = 66, GroupName = "5. SL/TP Dinâmico")]
        public double TpAtrMultiplierBlock2 { get; set; }

        [NinjaScriptProperty]
        [Range(0.5, 10.0)]
        [Display(Name = "TP Block3 (xATR)", Order = 67, GroupName = "5. SL/TP Dinâmico")]
        public double TpAtrMultiplierBlock3 { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 5.0)] // CORREÇÃO: Reduzido mínimo de 1.0 para 0.1 para permitir R/R de 0.5
        [Display(Name = "Mínimo Reward/Risk", Order = 68, GroupName = "5. SL/TP Dinâmico")]
        public double MinimoRewardRisk { get; set; }

        // ===== SISTEMA DE TRADING =====
        [NinjaScriptProperty]
        [Range(0.5, 10.0)]
        [Display(Name = "Long Threshold (Base)", Order = 69, GroupName = "6. Sistema de Trading")]
        public double LongThreshold { get; set; }

        [NinjaScriptProperty]
        [Range(-10.0, -0.5)]
        [Display(Name = "Short Threshold (Base)", Order = 70, GroupName = "6. Sistema de Trading")]
        public double ShortThreshold { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Ativar Short Trading", Order = 71, GroupName = "6. Sistema de Trading")]
        public bool AtivarShortTrading { get; set; }

        // ===== CONFIGURAÇÕES DE EXECUÇÃO =====
        [NinjaScriptProperty]
        [Range(0, 10)]
        [Display(Name = "Slippage (Ticks)", Description = "Slippage em ticks para backtesting realista", Order = 72, GroupName = "6.1 Execução")]
        public int SlippageTicks { get; set; }

        // ===== FILTRO ADX =====
        [NinjaScriptProperty]
        [Display(Name = "Usar Filtro ADX", Order = 73, GroupName = "7. Filtro ADX")]
        public bool UsarFiltroADX { get; set; }

        [NinjaScriptProperty]
        [Range(5, 50)]
        [Display(Name = "ADX Período", Order = 74, GroupName = "7. Filtro ADX")]
        public int ADXPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(20, 100)]
        [Display(Name = "ADX Máximo", Order = 75, GroupName = "7. Filtro ADX")]
        public int ADXMaximo { get; set; }

        // ===== TRAILING STOP TICK-BY-TICK =====
        [NinjaScriptProperty]
        [Display(Name = "Usar Trailing Stop", Order = 76, GroupName = "8. Trailing Stop TICK-BY-TICK")]
        public bool UsarTrailingStop { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "Trailing Stop Activation Ticks", Order = 77, GroupName = "8. Trailing Stop TICK-BY-TICK")]
        public int TrailingStopActivationTicks { get; set; }

        [NinjaScriptProperty]
        [Range(5, 100)]
        [Display(Name = "Trailing Stop Distance Ticks", Order = 78, GroupName = "8. Trailing Stop TICK-BY-TICK")]
        public int TrailingStopDistanceTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Min Tick Movement For Trailing", Order = 79, GroupName = "8. Trailing Stop TICK-BY-TICK")]
        public int MinTickMovementForTrailing { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Aggressive Trailing", Order = 80, GroupName = "8. Trailing Stop TICK-BY-TICK")]
        public bool EnableAggressiveTrailing { get; set; }

        // ===== MACHINE LEARNING BÁSICO =====
        [NinjaScriptProperty]
        [Display(Name = "Ativar ML Básico", Order = 81, GroupName = "9. Machine Learning Básico")]
        public bool AtivarMLBasico { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Ajuste Automático de Pesos", Order = 81, GroupName = "9. Machine Learning Básico")]
        public bool AjusteAutomaticoPesos { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 0.5)]
        [Display(Name = "Max Ajuste Peso", Order = 82, GroupName = "9. Machine Learning Básico")]
        public double MaxAjustePeso { get; set; }

        [NinjaScriptProperty]
        [Range(10, 100)]
        [Display(Name = "Intervalo Atualização ML", Order = 83, GroupName = "9. Machine Learning Básico")]
        public int IntervaloAtualizacaoML { get; set; }

        // ===== SISTEMA DE SAÍDA INTELIGENTE =====
        [NinjaScriptProperty]
        [Display(Name = "Usar Saída Inteligente", Order = 84, GroupName = "10. Sistema de Saída Inteligente")]
        public bool UsarSaidaInteligente { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Detectar Reversão de Momentum", Order = 85, GroupName = "10. Sistema de Saída Inteligente")]
        public bool DetectarReversaoMomentum { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Saída Parcial Ativa", Order = 86, GroupName = "10. Sistema de Saída Inteligente")]
        public bool SaidaParcialAtiva { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 0.8)]
        [Display(Name = "Percentual Saída Parcial", Order = 87, GroupName = "10. Sistema de Saída Inteligente")]
        public double PercentualSaidaParcial { get; set; }

        [NinjaScriptProperty]
        [Range(90, 99)]
        [Display(Name = "Volume Extremo Percentil", Order = 88, GroupName = "10. Sistema de Saída Inteligente")]
        public int VolumeExtremoPercentil { get; set; }

        // ===== ANÁLISE DE CORRELAÇÃO =====
        [NinjaScriptProperty]
        [Display(Name = "Ativar Análise de Correlação", Order = 89, GroupName = "11. Análise de Correlação")]
        public bool AtivarAnaliseCorrelacao { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Coletar Dados Performance", Order = 90, GroupName = "11. Análise de Correlação")]
        public bool ColetarDadosPerformance { get; set; }

        [NinjaScriptProperty]
        [Range(10, 60)]
        [Display(Name = "Intervalo Análise Correlação (min)", Order = 91, GroupName = "11. Análise de Correlação")]
        public int IntervaloAnaliseCorrelacao { get; set; }

        // ===== PARÂMETROS DE SEGURANÇA =====
        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Max Trades Per Day", Order = 92, GroupName = "12. Segurança")]
        public int MaxTradesPerDay { get; set; }

        [NinjaScriptProperty]
        [Range(0, 60)]
        [Display(Name = "Min Time Between Trades (min)", Order = 93, GroupName = "12. Segurança")]
        public int MinTimeBetweenTrades { get; set; }


        // ===== LOGGING =====
        [NinjaScriptProperty]
        [Display(Name = "Enable Structured Logging", Order = 97, GroupName = "13. Logging")]
        public bool EnableStructuredLogging { get; set; }

        [NinjaScriptProperty]
        [Range(1, 60)]
        [Display(Name = "Log Flush Interval (min)", Order = 98, GroupName = "13. Logging")]
        public int LogFlushIntervalMinutes { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Tick Logging", Order = 99, GroupName = "13. Logging")]
        public bool EnableTickLogging { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Ready Logging", Order = 100, GroupName = "13. Logging")]
        public bool EnableReadyLogging { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Score Logging", Order = 101, GroupName = "13. Logging")]
        public bool EnableScoreLogging { get; set; }

        [NinjaScriptProperty]
        [Range(5, 300)]
        [Display(Name = "Score Log Interval (seconds)", Order = 102, GroupName = "13. Logging")]
        public int ScoreLogIntervalSeconds { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable ML Stats Logging", Order = 103, GroupName = "13. Logging")]
        public bool EnableMLStatsLogging { get; set; }
        #endregion
    }
}