# Hybrid Trend-Following Bot for cTrader (cAlgo)

This is a fully-automated trading robot developed in C# for the cTrader platform. The bot implements a hybrid trend-following strategy designed to operate on EUR/USD. It combines multiple indicators for signal confirmation with advanced risk and trade management features.

## Key Features

This bot was designed to be robust and versatile. Its core features include:

* Dynamic Position Sizing: Automatically calculates trade volume based on a fixed percentage of account equity and the real-time volatility (ATR-based stop-loss).

* Multi-Indicator Signal Confirmation:
  * Trend: Uses a dual EMA (50/200) crossover system to define the primary trend.
  * Strength: Employs ADX to filter out low-momentum, choppy market conditions.
  * Entry: Uses RSI pullbacks for more precise entries within an established trend.

* Advanced Trade Management:
  * Multi-Stage Exits: Positions can be exited via Take Profit, Stop Loss, a time-based stop (MaxBarsInTrade), an RSI-based signal, or at the end of the defined trading session.
  * Breakeven Function: Automatically moves the stop-loss to entry price (+padding) to protect capital once a trade reaches a specified profit target.
  * Partial Take-Profit: Secures a percentage of the position's profit at a preliminary target, allowing the remainder to run.

* Robust Risk Controls:
  * Hard Daily Limits: Implements a daily maximum loss percentage and a maximum number of trades per day to prevent catastrophic losses.
  * Pre-Trade Filters: Includes filters for maximum allowable spread and a time-of-day session window to avoid unfavorable trading conditions.

## Technology Stack

* Language: C#
* Platform: cTrader (cAlgo API)
* IDE: Visual Studio / cTrader Code Editor

## Configurable Parameters

The bot is highly configurable via the cAlgo interface. Key parameters include:

* RiskPerTradePercent: The percentage of equity to risk on a single trade.
* RR: The risk-to-reward ratio for setting the initial take-profit.
* MaxLossDay: The maximum percentage of account balance that can be lost in a single day before the bot stops trading.
* MinAdx: The minimum ADX value required to confirm a trend.
* BeSlMultiplier: Multiple of the initial SL distance used to trigger breakeven.
* PtpSlMultiplier: Multiple of the initial SL distance used to trigger partial take-profit.
* FixedLotSize: Does nothing in the current version; however, the code can be modified to re-enable it.

## How to Run

* Clone the repository

  git clone https://github.com/Great-gla-27/Hybrid-Bot.git

* Open in Visual Studio
  * Launch Visual Studio or the cTrader Code Editor.
  * Open the HybridTrendBot project.

* Build the project
  * Restore any necessary NuGet packages.
  * Build the solution.

* Load to cTrader
  * Copy the compiled .algo file into your cTrader Automate folder (usually located at Documents/cAlgo/Sources/Robots).
  * In cTrader, go to the Automate tab. The HybridTrendBot should appear in your list of cBots.

* Load Settings (Optional)
  * To replicate the backtest, load the optimization file:
    src/cTraderBot/NewBot3.1 - 31.05 2300
  * And the parameter file:
    src/cTraderBot/NewBot3.1, EURUSD h12.cbotset
  * Test period: 13/01/22 to 15/02/23.

## Performance & Analysis

To view the performance section with embedded backtesting images, please visit the formatted README at:

This:https://github.com/Great-gla-27/Hybrid-Bot/blob/master/.github/workflows/README.md

This was a really fun personal project. I’m amazed by how much there is to explore in algorithmic ttrading Although the backtest was run on the 12h timeframe, the parameters shown (e.g., RiskPerTradePercent = 1.7%, RR = 1.2, MaxLossDay = 4.4%) are not the only ones that can yield profit. They simply fit the trading style I prefer — moderate risk with a balanced reward ratio and a strong emphasis on trend confirmation.

The biggest challenge was finding optimization parameters that work well outside the tested period. A grid optimization over all parameter combinations would take my laptop an estimated 2,332 days of CPU time. Even with the genetic algorithm optimization I ran, results were sensitive to market regime shifts, indicating that further adaptive techniques (like walk-forward analysis) are necessary for a truly robust strategy.

While this is a sophisticated bot, I do not believe it qualifies as a “good” bot in a live trading context. Drawdowns reached up to 5.6% of equity, and short trades drove most of the gains (net profit: $1,611 for shorts vs. –$49 for longs). This imbalance suggests the strategy may be overfitted to bearish conditions. I’d need to refine the logic, add regime filters, and perform rigorous out-of-sample testing before considering live deployment.

Finally, I had a lot of fun learning how to integrate multiple indicators, manage trade lifecycle events, and handle performance analysis. I’m excited to explore more advanced techniques — such as machine learning–driven parameter selection and real-time adaptive risk management — in future projects.

## Acknowledgments & Learning Resources

The development of this project was aided by several excellent learning resources.

The foundational structure and understanding of the cTrader API were developed through tutorials including "Learn How to Code a cTrader cBot." The core logic for the trend-following strategy was adapted from concepts in tutorials like "How to Build a cTrader Simple Moving Average Strategy."

Implementation details and best practices were referenced from the official Spotware ctrader-algo-samples repository and other open-source projects.

Generative AI was used as a tool for debugging specific functions and exploring alternative C# syntax. While these resources were instrumental in the learning process, the final architecture, trading logic, and implementation are my own.
