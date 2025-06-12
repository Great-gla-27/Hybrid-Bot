// === HybridTrendBot.cs (MIT, free to modify) v6.0 ===
// Added ADX trend‑strength filter using DirectionalMovementSystem indicator to improve trade selection.
// Keeps all original risk‑management logic untouched.

using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class HybridTrendBot : Robot
    {
        // ───────────────────────── PARAMETERS ─────────────────────────
        [Parameter("Risk % (Ignored, Use Fixed Volume)", DefaultValue = 1.0, MinValue = 0.1)] public double RiskPct { get; set; }
        [Parameter("Fixed Trade Volume (Units)", DefaultValue = 1000, MinValue = 1)] public int FixedTradeVolumeUnits { get; set; }

        [Parameter("RR", DefaultValue = 2.0, MinValue = 1)] public double RR { get; set; }
        [Parameter("Daily Max Loss %", DefaultValue = 5.0, MinValue = 0.5)] public double MaxLossDay { get; set; }
        [Parameter("Max Trades/Day", DefaultValue = 4, MinValue = 1)] public int MaxTradesDay { get; set; }
        [Parameter("Max Spread (p)", DefaultValue = 3.0, MinValue = 0)] public double MaxSpread { get; set; }
        [Parameter("Trade Start UTC", DefaultValue = 7)] public int StartH { get; set; }
        [Parameter("Trade End UTC", DefaultValue = 20)] public int EndH { get; set; }
        [Parameter("Partial TP ×SL Multiplier", DefaultValue = 1.5, MinValue = 0.1)] public double PtpSlMultiplier { get; set; }
        [Parameter("Partial Close %", DefaultValue = 50, MinValue = 1, MaxValue = 99)] public int PtpPerc { get; set; }
        [Parameter("BE Trigger ×SL Multiplier", DefaultValue = 1.0, MinValue = 0.1)] public double BeSlMultiplier { get; set; }
        [Parameter("SL Padding (pips)", DefaultValue = 1.0, MinValue = 0)] public double SlPadPips { get; set; }
        [Parameter("News Buffer Minutes", DefaultValue = 2)] public int NewsWindowMins { get; set; }
        [Parameter("Min Volatility (ATR / Stdev)", DefaultValue = 1.2, MinValue = 0.5)] public double MinVolatilityRatio { get; set; }
        [Parameter("RSI Exit Long", DefaultValue = 75)] public double RsiExitLong { get; set; }
        [Parameter("RSI Exit Short", DefaultValue = 25)] public double RsiExitShort { get; set; }
        [Parameter("Force Exit at Session End", DefaultValue = true)] public bool ForceExitAtSessionEnd { get; set; }

        // NEW
        [Parameter("Min ADX", DefaultValue = 25, MinValue = 5)] public int MinAdx { get; set; }

        // ───────────────────────── FIELDS / INDICATORS ─────────────────────────
        private ExponentialMovingAverage _ema50, _ema200;
        private RelativeStrengthIndex _rsi;
        private AverageTrueRange _atr;
        private StandardDeviation _stdDev;
        private DirectionalMovementSystem _dms; // ADX comes from this indicator

        private DateTime _today;
        private int _tradesTodayCounter;
        private int _closedTradesToday;
        private double _pnlToday;

        private double _slPipsInitial;
        private double? _tpPriceInitial;
        private bool _beApplied;
        private bool _partialTaken;

        private bool IsInNewsWindow => false; // TODO: implement real news filter
        private string Label => $"HybridTrend_{Symbol.Name}_{TimeFrame}";

        // ───────────────────────── LIFECYCLE ─────────────────────────
        protected override void OnStart()
        {
            Print("=== HybridTrendBot v6.0 Started (ADX filter) ===");
            try
            {
                if (Symbol == null)
                {
                    Print("ERROR: Symbol object is not available on start.");
                    Stop();
                    return;
                }

                Print($"Symbol: {Symbol.Name}. Fixed Volume: {FixedTradeVolumeUnits} units.");

                _ema50 = Indicators.ExponentialMovingAverage(Bars.ClosePrices, 50);
                _ema200 = Indicators.ExponentialMovingAverage(Bars.ClosePrices, 200);
                _rsi = Indicators.RelativeStrengthIndex(Bars.ClosePrices, 14);
                _atr = Indicators.AverageTrueRange(14, MovingAverageType.Simple);
                _stdDev = Indicators.StandardDeviation(Bars.ClosePrices, 14, MovingAverageType.Simple);
                _dms = Indicators.DirectionalMovementSystem(14); // provides ADX
            }
            catch (Exception e)
            {
                Print($"Error initializing: {e.Message} - {e.StackTrace}");
                Stop();
                return;
            }

            _today = Server.Time.Date;
            _tradesTodayCounter = 0;
            _closedTradesToday = 0;
            _pnlToday = 0;

            ResetTradeStateFlags();

            Positions.Closed += OnPositionsClosed;
            Print("OnStart completed.");
        }

        private void ResetTradeStateFlags()
        {
            _beApplied = false;
            _partialTaken = false;
            _slPipsInitial = 0;
            _tpPriceInitial = null;
        }

        protected override void OnTick()
        {
            try
            {
                // New day rollover
                if (Server.Time.Date != _today)
                {
                    _today = Server.Time.Date;
                    _tradesTodayCounter = 0;
                    _closedTradesToday = 0;
                    _pnlToday = 0;
                    Print($"=== NEW DAY: {_today:yyyy-MM-dd} ===");
                }

                // Daily limits
                if (_tradesTodayCounter >= MaxTradesDay && MaxTradesDay > 0) return;
                double maxLossAmount = Account.Balance * (MaxLossDay / 100.0);
                if (_pnlToday <= -maxLossAmount && MaxLossDay > 0) return;

                // Manage open position
                ManagePositions();

                if (Positions.Find(Label) != null) return; // already in a trade
                if (!CanTrade()) return;

                CheckTradingSignals();
            }
            catch (Exception e)
            {
                Print($"ERROR in OnTick: {e.Message} | StackTrace: {e.StackTrace}");
            }
        }

        // ───────────────────────── HELPER METHODS ─────────────────────────
        private bool TrendIsStrong()
        {
            return _dms != null && _dms.ADX.Count > 0 && _dms.ADX.LastValue >= MinAdx;
        }

        private bool CanTrade()
        {
            var h = Server.Time.Hour;
            if (h < StartH || h >= EndH) return false;

            double spread = Symbol.Spread / Symbol.PipSize;
            if (spread > MaxSpread && MaxSpread > 0) return false;
            if (IsInNewsWindow) return false;

            if (_atr.Result.Count == 0 || _stdDev.Result.Count == 0) return false;
            double atr = _atr.Result.LastValue;
            double sd = _stdDev.Result.LastValue;
            if (sd == 0 || (atr / sd) < MinVolatilityRatio) return false;

            if (!TrendIsStrong()) return false;
            return true;
        }

        private void CheckTradingSignals()
        {
            if (Positions.Find(Label) != null) return;
            if (_ema50.Result.Count == 0 || _ema200.Result.Count == 0 || _rsi.Result.Count == 0 || Bars.ClosePrices.Count == 0) return;

            double ema50 = _ema50.Result.LastValue;
            double ema200 = _ema200.Result.LastValue;
            double rsi = _rsi.Result.LastValue;
            double close = Bars.ClosePrices.LastValue;

            bool longSignal = close > ema50 && ema50 > ema200 && rsi > 30 && rsi < 70;
            bool shortSignal = close < ema50 && ema50 < ema200 && rsi < 70 && rsi > 30;

            if (!TrendIsStrong()) return;

            if (longSignal)
                ExecuteTrade(TradeType.Buy);
            else if (shortSignal)
                ExecuteTrade(TradeType.Sell);
        }

        // ───────────────────────── TRADE EXECUTION & MANAGEMENT ─────────────────────────
        private void ExecuteTrade(TradeType tradeType)
        {
            if (_atr.Result.Count == 0) { Print("ATR not ready"); return; }
            double atr = _atr.Result.LastValue;
            double slDist = (atr * 2) + (SlPadPips * Symbol.PipSize);
            if (slDist <= 0) { Print("SL distance invalid"); return; }

            double entry = tradeType == TradeType.Buy ? Symbol.Ask : Symbol.Bid;
            double sl = tradeType == TradeType.Buy ? entry - slDist : entry + slDist;
            double tp = tradeType == TradeType.Buy ? entry + (slDist * RR) : entry - (slDist * RR);
            double slPips = Math.Abs(entry - sl) / Symbol.PipSize;
            if (slPips < 0.1) { Print("SL pips too small"); return; }

            long vol = FixedTradeVolumeUnits;
            if (vol <= 0) { Print("Invalid volume"); return; }

            Print($"🚀 Order prep — Entry:{entry:F5}, SL:{sl:F5}, TP:{tp:F5}, Vol:{vol}");
            var res = ExecuteMarketOrder(tradeType, SymbolName, vol, Label);
            if (!res.IsSuccessful) { Print($"❌ Execution failed:{res.Error}"); return; }

            _tradesTodayCounter++;
            _slPipsInitial = slPips;
            _tpPriceInitial = tp;
            _beApplied = false;
            _partialTaken = false;

            if (!SetPositionSLTP(res.Position, sl, tp))
            {
                Print("Failed to set initial SL/TP → closing position");
                ClosePosition(res.Position, "Init SL/TP fail");
            }
        }

        private void ManagePositions()
        {
            var pos = Positions.Find(Label);
            if (pos == null) return;
            if (_rsi.Result.Count == 0) return;

            double rsi = _rsi.Result.LastValue;
            if ((pos.TradeType == TradeType.Buy && rsi >= RsiExitLong) ||
                (pos.TradeType == TradeType.Sell && rsi <= RsiExitShort))
            {
                ClosePosition(pos, "RSI exit");
                return;
            }

            if (_slPipsInitial <= 0) return;

            // Breakeven
            if (!_beApplied && pos.Pips >= _slPipsInitial * BeSlMultiplier)
            {
                double be = pos.TradeType == TradeType.Buy ? pos.EntryPrice + (SlPadPips * Symbol.PipSize) : pos.EntryPrice - (SlPadPips * Symbol.PipSize);
                if (SetPositionSLTP(pos, be, _tpPriceInitial))
                {
                    _beApplied = true;
                    Print($"✅ Breakeven moved to {be:F5}");
                }
            }

            // Partial close
            if (!_partialTaken && PtpPerc > 0 && pos.Pips >= _slPipsInitial * PtpSlMultiplier && pos.VolumeInUnits > 1)
            {
                long closeUnits = (long)(pos.VolumeInUnits * (PtpPerc / 100.0));
                closeUnits = Math.Max(1, closeUnits);
                if (closeUnits < pos.VolumeInUnits)
                {
                    var pr = ExecuteMarketOrder(pos.TradeType == TradeType.Buy ? TradeType.Sell : TradeType.Buy, pos.SymbolName, closeUnits, Label + "_Partial");
                    if (pr.IsSuccessful) { _partialTaken = true; Print($"✅ Partial close {closeUnits} units"); }
                    else Print($"❌ Partial close error:{pr.Error}");
                }
            }

            // Session end
            if (ForceExitAtSessionEnd && Server.Time.Hour >= EndH && (EndH < 23 || Server.Time.Hour < 23))
                ClosePosition(pos, "Session end");
        }

        // ───────────────────────── SL / TP HANDLING ─────────────────────────
        private bool SetPositionSLTP(Position p, double? slPrice, double? tpPrice)
        {
            if (p == null) return false;
            bool slOk = true, tpOk = true;
            if (slPrice.HasValue)
            {
                var r = p.ModifyStopLossPrice(slPrice.Value);
                slOk = r.IsSuccessful;
                if (!slOk) Print($"SL modify fail:{r.Error}");
            }
            if (tpPrice.HasValue)
            {
                var r = p.ModifyTakeProfitPrice(tpPrice.Value);
                tpOk = r.IsSuccessful;
                if (!tpOk) Print($"TP modify fail:{r.Error}");
            }
            return slOk && tpOk;
        }

        private void ClosePosition(Position p, string reason)
        {
            if (p == null) return;
            Print($"Closing pos ID:{p.Id} — {reason}");
            var r = p.Close();
            if (!r.IsSuccessful) Print($"Close error:{r.Error}");
        }

        // ───────────────────────── EVENTS ─────────────────────────
        private void OnPositionsClosed(PositionClosedEventArgs args)
        {
            var p = args.Position;
            if (p.Label != Label) return;
            _pnlToday += p.NetProfit;
            _closedTradesToday++;
            Print($"=== CLOSED {p.TradeType} {p.VolumeInUnits}u |PnL:{p.NetProfit:F2}|Pips:{p.Pips:F1} ===");
            Print($"Day PnL:{_pnlToday:F2} | Closed:{_closedTradesToday} / Exec:{_tradesTodayCounter}");
            if (Positions.Find(Label) == null) ResetTradeStateFlags();
        }

        protected override void OnStop()
        {
            Print("=== HybridTrendBot v6.0 Stopped ===");
            ResetTradeStateFlags();
            Positions.Closed -= OnPositionsClosed;
        }

        // CalcVolume retained for compatibility (fixed-volume mode)
        private long CalcVolume(double slPips)
        {
            Print("CalcVolume called (fixed volume mode)");
            return FixedTradeVolumeUnits <= 0 ? 1 : FixedTradeVolumeUnits;
        }
    }
}
