# HybridTrendBot (C# for cTrader)

An automated trading system built in **C#** for the **cTrader (cAlgo)** platform.  
The bot implements a hybrid trend-following strategy with **dynamic risk management** and **multi-indicator signal confirmation**.

**Disclaimer:** This project is for **educational and research purposes only**. It is not financial advice. Do not use with live accounts or real money without extensive independent testing.

---

## TL;DR
- **Core idea:** Trend-following bot with advanced risk controls.  
- **Built with:** C# + cTrader API.  
- **Demo:** Load into cTrader backtesting and run against EUR/USD historical data.  
- **Outcome:** Demonstrates dynamic position sizing, partial exits, breakeven logic, and strict daily risk limits.

---

## Features
- **Dynamic Position Sizing**  
  Calculates trade volume automatically based on account equity, ATR stop-loss, and broker limits.

- **Multi-Indicator Signal Confirmation**  
  - Trend: EMA(50/200) crossover  
  - Strength: ADX filter  
  - Entry/Exit: RSI pullback + volatility checks  

- **Advanced Trade Management**  
  - Daily loss cap  
  - Max trades/day  
  - Partial take-profit  
  - Breakeven stop adjustment  
  - Session end forced exit  

- **Risk Controls**  
  - All trades capped at configurable % of account equity  
  - Hard floor on stop-loss distance  
  - Automatic disable after daily max loss

---

## Demo Instructions
1. Clone the repo:
   ```bash
   git clone https://github.com/<your-username>/HybridTrendBot.git
