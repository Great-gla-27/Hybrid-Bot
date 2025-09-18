// === HybridTrendBot.cs  v6.3 (Complete with Dynamic Position Sizing & Enhanced Logging) ===
// Corrected references: use VolumeInUnitsMin/Max/Step instead of lot-based methods.

using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class HybridTrendBot : Robot
    {
        // ───────────────────────── PARAMETERS ─────────────────────────
        [Parameter("Fixed Trade Volume (Lots)", DefaultValue = 0.10, MinValue = 0.01, Step = 0.01)]
        public double FixedLots { get; set; }

        [Parameter("Risk Per Trade %", DefaultValue = 1.0, MinValue = 0.1, MaxValue = 5.0, Step = 0.1)]
        public double RiskPerTradePercent { get; set; }

        [Parameter("RR (Risk:Reward)", DefaultValue = 2.0, MinValue = 1.0, Step = 0.1)]
        public double RR { get; set; }

        [Parameter("Daily Max Loss %", DefaultValue = 1.5, MinValue = 0.1, MaxValue = 10.0, Step = 0.1)]
        public double MaxLossDay { get; set; }

        [Parameter("Max Trades/Day", DefaultValue = 3, MinValue = 0)]
        public int MaxTradesDay { get; set; }

        [Parameter("Max Spread (pips)", DefaultValue = 3.0, MinValue = 0, Step = 0.1)]
        public double MaxSpread { get; set; }

        [Parameter("Trade Start UTC (Hour)", DefaultValue = 7, MinValue = 0, MaxValue = 23)]
        public int StartH { get; set; }

        [Parameter("Trade End UTC (Hour)", DefaultValue = 20, MinValue = 0, MaxValue = 23)]
        public int EndH { get; set; }

        [Parameter("Partial TP ×SL Mult", DefaultValue = 1.5, MinValue = 0.1, Step = 0.1)]
        public double PtpSlMultiplier { get; set; }

        [Parameter("Partial Close %", DefaultValue = 40, MinValue = 1, MaxValue = 99)]
        public int PtpPerc { get; set; }

        [Parameter("BE Trigger ×SL Mult", DefaultValue = 0.8, MinValue = 0.1, Step = 0.1)]
        public double BeSlMultiplier { get; set; }

        [Parameter("SL Padding (pips)", DefaultValue = 1.0, MinValue = 0, Step = 0.1)]
        public double SlPadPips { get; set; }

        [Parameter("Min Volatility (ATR/Stdev)", DefaultValue = 1.2, MinValue = 0.1, Step = 0.1)]
        public double MinVolatilityRatio { get; set; }

        [Parameter("RSI Exit Long", DefaultValue = 75, MinValue = 50, MaxValue = 100)]
        public double RsiExitLong { get; set; }

        [Parameter("RSI Exit Short", DefaultValue = 25, MinValue = 0, MaxValue = 50)]
        public double RsiExitShort { get; set; }

        [Parameter("Force Exit at Session End", DefaultValue = true)]
        public bool ForceExitAtSessionEnd { get; set; }

        [Parameter("Min ADX for Trend Strength", DefaultValue = 25, MinValue = 0)]
        public int MinAdx { get; set; }

        [Parameter("Max Bars in Trade (0=disabled)", DefaultValue = 0, MinValue = 0)]
        public int MaxBarsInTrade { get; set; }

        [Parameter("Min SL Distance (pips)", DefaultValue = 10.0, MinValue = 1.0, Step = 0.5)]
        public double MinSlPips { get; set; }

        // ───────────────────────── INDICATORS / FIELDS ─────────────────────────
        private ExponentialMovingAverage _ema50, _ema200;
        private RelativeStrengthIndex _rsi;
        private AverageTrueRange _atr;
        private StandardDeviation _stdDev;
        private DirectionalMovementSystem _dms;
        private DateTime _today;
        private int _tradesTodayCounter;
        private double _pnlToday;
        private bool _dailyLossLimitReachedToday;
        private double _slPipsInitial;
        private double? _tpPriceInitial;
        private bool _beApplied;
        private bool _partialTaken;
        private int _entryBarIndex = -1;
        private string Label => $"HybridTrend_{Symbol.Name}_{TimeFrame}";

        // ────────────────────────── LIFECYCLE ──────────────────────────
        protected override void OnStart()
        {
            Print($"=== HybridTrendBot v6.3 Started | Risk%: {RiskPerTradePercent:F1}% | RR: {RR:F1} ===");
            _ema50  = Indicators.ExponentialMovingAverage(Bars.ClosePrices, 50);
            _ema200 = Indicators.ExponentialMovingAverage(Bars.ClosePrices, 200);
            _rsi    = Indicators.RelativeStrengthIndex(Bars.ClosePrices, 14);
            _atr    = Indicators.AverageTrueRange(14, MovingAverageType.Simple);
            _stdDev = Indicators.StandardDeviation(Bars.ClosePrices, 14, MovingAverageType.Simple);
            _dms    = Indicators.DirectionalMovementSystem(14);
            _today  = Server.Time.Date;
            ResetDailyCountersAndFlags();
            ResetTradeStateFlags();
            Positions.Closed += OnPositionsClosed;
        }

        protected override void OnTick()
        {
            if (Server.Time.Date != _today)
            {
                _today = Server.Time.Date;
                ResetDailyCountersAndFlags();
            }
            if (_dailyLossLimitReachedToday)
            {
                ManageOpenPositions();
                return;
            }
            double maxLossValue = Account.Balance * (MaxLossDay / 100.0);
            if (MaxLossDay > 0 && _pnlToday <= -maxLossValue)
            {
                Print($"Daily max loss of {maxLossValue:F2} reached. No new trades.");
                _dailyLossLimitReachedToday = true;
                var pos = Positions.Find(Label);
                if (pos != null)
                {
                    Print($"Closing {pos.Id} due to daily max loss.");
                    ClosePosition(pos, "Daily Max Loss Hit");
                }
                return;
            }
            if (MaxTradesDay > 0 && _tradesTodayCounter >= MaxTradesDay)
            {
                ManageOpenPositions();
                return;
            }
            ManageOpenPositions();
            if (Positions.Find(Label) != null) return;
            if (!CanTradeNewPosition()) return;
            CheckTradingSignals();
        }

        protected override void OnStop()
        {
            Positions.Closed -= OnPositionsClosed;
            Print("=== HybridTrendBot v6.3 Stopped ===");
        }

        // ────────────────────────── SIGNAL LOGIC ──────────────────────────
        private void CheckTradingSignals()
        {
            if (_ema50.Result.Count == 0 || _ema200.Result.Count == 0 || _rsi.Result.Count == 0 || _dms.ADX.Count == 0)
            {
                Print("Indicators not yet ready.");
                return;
            }
            double close = Bars.ClosePrices.LastValue;
            double e50   = _ema50.Result.LastValue;
            double e200  = _ema200.Result.LastValue;
            double rv    = _rsi.Result.LastValue;
            bool longSig  = close > e50 && e50 > e200 && rv < 70;
            bool shortSig = close < e50 && e50 < e200 && rv > 30;
            if (!(_dms.ADX.LastValue >= MinAdx)) return;
            if (longSig) ExecuteTrade(TradeType.Buy);
            else if (shortSig) ExecuteTrade(TradeType.Sell);
        }

        private bool CanTradeNewPosition()
        {
            int hr = Server.Time.Hour;
            if (hr < StartH || hr >= EndH) return false;
            if (Symbol.Spread / Symbol.PipSize > MaxSpread && MaxSpread > 0) return false;
            if (_stdDev.Result.LastValue == 0) return false;
            if (_atr.Result.LastValue / _stdDev.Result.LastValue < MinVolatilityRatio) return false;
            return true;
        }

        // ────────────────────────── EXECUTION (Dynamic Sizing) ──────────────────────────
        private void ExecuteTrade(TradeType type)
        {
            // Step 1: Determine SL distance in pips
            double atr = _atr.Result.LastValue;
            double slPips = (atr * 2.0 / Symbol.PipSize) + SlPadPips;
            slPips = Math.Max(slPips, MinSlPips);
            if (slPips <= 0.0)
            {
                Print($"SL pips invalid ({slPips:F2}). ATR: {atr:F5}");
                return;
            }

            // Step 2: Calculate monetary risk amount
            double equity = Account.Equity;
            double riskAmount = equity * (RiskPerTradePercent / 100.0);

            // Step 3: Calculate pip value per unit
            double valuePerPipPerUnit = Symbol.PipValue;
            if (valuePerPipPerUnit <= 0)
            {
                Print("Value per pip per unit invalid. Check symbol/currency.");
                return;
            }

            // Step 4: Monetary risk per 1 unit with SL distance
            double monetaryRiskPerUnit = slPips * valuePerPipPerUnit;
            if (monetaryRiskPerUnit <= 0)
            {
                Print($"Monetary risk per unit invalid ({monetaryRiskPerUnit:F2}). SL pips: {slPips:F2}");
                return;
            }

            // Step 5: Calculate ideal volume in units
            double idealUnits = riskAmount / monetaryRiskPerUnit;

            // Step 6: Normalize to broker's volume step and min/max
            double minUnits  = Symbol.VolumeInUnitsMin;
            double maxUnits  = Symbol.VolumeInUnitsMax;
            double stepUnits = Symbol.VolumeInUnitsStep;
            long normalizedUnits = (long)(Math.Floor(idealUnits / stepUnits) * stepUnits);
            normalizedUnits = Math.Max((long)minUnits, Math.Min((long)maxUnits, normalizedUnits));

            if (normalizedUnits < (long)Symbol.VolumeInUnitsMin)
            {
                Print($"Calculated volume units ({normalizedUnits}) below min ({Symbol.VolumeInUnitsMin}). Skipping trade.");
                return;
            }

            double lotsEquivalent = Symbol.VolumeInUnitsToQuantity(normalizedUnits);
            Print($"Dynamic Volume: {lotsEquivalent:F2} lots ({normalizedUnits}u) | Risk: {RiskPerTradePercent:F1}% ({riskAmount:F2}) | SL: {slPips:F2} pips");

            // Step 7: Calculate price-based SL and TP
            double slPriceDist = slPips * Symbol.PipSize;
            double entry = type == TradeType.Buy ? Symbol.Ask : Symbol.Bid;
            double slPrice = type == TradeType.Buy ? entry - slPriceDist : entry + slPriceDist;
            double tpPrice = type == TradeType.Buy ? entry + slPriceDist * RR : entry - slPriceDist * RR;

            _slPipsInitial = slPips;
            var result = ExecuteMarketOrder(type, SymbolName, normalizedUnits, Label);
            if (!result.IsSuccessful)
            {
                Print($"EXEC ERROR: {result.Error}");
                return;
            }

            Print($" TRADE: {type} {lotsEquivalent:F2} lots @ {result.Position.EntryPrice:F5} | SL: {slPrice:F5} | TP: {tpPrice:F5}");
            _tradesTodayCounter++;
            _tpPriceInitial = tpPrice;
            _beApplied = false;
            _partialTaken = false;
            _entryBarIndex = Bars.Count - 1;

            // Step 8: Set SL and TP on the position
            if (!SetPositionSLTP(result.Position, slPrice, tpPrice))
            {
                Print("Failed to set initial SL/TP. Closing position.");
                ClosePosition(result.Position, "Init SL/TP fail");
            }
        }

        private bool SetPositionSLTP(Position position, double slPrice, double tpPrice)
        {
            var modifySL = position.ModifyStopLossPrice(slPrice);
            var modifyTP = position.ModifyTakeProfitPrice(tpPrice);
            return modifySL.IsSuccessful && modifyTP.IsSuccessful;
        }

        private void ManageOpenPositions()
        {
            var pos = Positions.Find(Label);
            if (pos == null) return;
            double rsiVal = _rsi.Result.LastValue;

            // RSI exit
            if ((pos.TradeType == TradeType.Buy && rsiVal >= RsiExitLong) ||
                (pos.TradeType == TradeType.Sell && rsiVal <= RsiExitShort))
            {
                Print($"Attempting to close {pos.Id} due to RSI exit. RSI={rsiVal:F1}");
                ClosePosition(pos, "RSI exit");
                return;
            }

            // Time-based stop
            if (MaxBarsInTrade > 0 && _entryBarIndex >= 0 && Bars.Count - 1 - _entryBarIndex >= MaxBarsInTrade)
            {
                Print($"Attempting to close {pos.Id} due to max bars in trade ({MaxBarsInTrade})");
                ClosePosition(pos, $"Max bars {MaxBarsInTrade}");
                return;
            }

            // Breakeven logic
            if (!_beApplied && pos.Pips >= _slPipsInitial * BeSlMultiplier)
            {
                double bePrice = pos.TradeType == TradeType.Buy
                                  ? pos.EntryPrice + SlPadPips * Symbol.PipSize
                                  : pos.EntryPrice - SlPadPips * Symbol.PipSize;
                if ((pos.TradeType == TradeType.Buy && bePrice < (pos.StopLoss ?? double.MaxValue)) ||
                    (pos.TradeType == TradeType.Sell && bePrice > (pos.StopLoss ?? double.MinValue)))
                {
                    if (pos.ModifyStopLossPrice(bePrice).IsSuccessful)
                    {
                        _beApplied = true;
                        Print($" BREAKEVEN applied. SL moved to {bePrice:F5}");
                    }
                }
            }

            // Partial TP logic
            if (!_partialTaken && PtpPerc > 0 && PtpPerc < 100 && pos.Pips >= _slPipsInitial * PtpSlMultiplier)
            {
                long initVol = (long)pos.VolumeInUnits;
                long toClose = (long)Symbol.NormalizeVolumeInUnits((long)(initVol * (PtpPerc / 100.0)), RoundingMode.Down);
                if (toClose >= Symbol.VolumeInUnitsMin)
                {
                    long rem = initVol - toClose;
                    if (rem >= Symbol.VolumeInUnitsMin)
                    {
                        var modRes = pos.ModifyVolume(rem);
                        if (modRes.IsSuccessful)
                        {
                            _partialTaken = true;
                            Print($"PARTIAL TP: Closed {toClose}u, Remaining {rem}u");
                        }
                        else
                        { Print($"Partial TP error: {modRes.Error}"); }
                    }
                    else
                    {
                        Print($"Attempting to close {pos.Id} fully due to partial TP leaving below min volume");
                        ClosePosition(pos, "Partial TP (full due to min volume)");
                        return;
                    }
                }
            }

            // Session end exit
            if (ForceExitAtSessionEnd && Server.Time.Hour >= EndH && Server.Time.Hour < 23)
            {
                Print($"Attempting to close {pos.Id} due to session end. Hour={Server.Time.Hour}");
                ClosePosition(pos, "Session end");
                return;
            }
        }

        // ────────────────────────── EVENTS ──────────────────────────
        private void OnPositionsClosed(PositionClosedEventArgs args)
        {
            var closed = args.Position;
            if (closed.Label != Label) return;
            var closingDeal = closed.Deals.Last(d => d.PositionImpact == DealPositionImpact.Closing);
            double closePrice = closingDeal.ExecutionPrice ?? closed.CurrentPrice;
            Print($"CLOSED: {closed.TradeType} {Symbol.VolumeInUnitsToQuantity(closed.VolumeInUnits):F2} lots ({closed.VolumeInUnits}u) {closed.SymbolName} | " +
                  $"Entry: {closed.EntryPrice:F5} | Close: {closePrice:F5} | Pips: {closed.Pips:F1} | PnL: {closed.NetProfit:F2} | Reason: {args.Reason}");
            if (args.Reason != PositionCloseReason.StopOut)
                _pnlToday += closed.NetProfit;
            if (Positions.Find(Label) == null)
                ResetTradeStateFlags();
        }

        // ────────────────────────── UTILITIES ──────────────────────────
        private void ClosePosition(Position position, string reason)
        {
            Print($"Closing {position.Id} due to: {reason}");
            var res = position.Close();
            if (!res.IsSuccessful)
                Print($"Close error: {res.Error}");
        }

        private void ResetTradeStateFlags()
        {
            _beApplied = false;
            _partialTaken = false;
            _slPipsInitial = 0;
            _tpPriceInitial = null;
            _entryBarIndex = -1;
            Print("Trade state flags reset.");
        }

        private void ResetDailyCountersAndFlags()
        {
            Print($"=== NEW DAY {Server.Time.Date:yyyy-MM-dd} ===");
            _tradesTodayCounter = 0;
            _pnlToday = 0;
            _dailyLossLimitReachedToday = false;
            Print("Daily counters and flags reset.");
        }
    }
}
