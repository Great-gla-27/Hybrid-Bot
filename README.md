# HybridTrendBot (C# for cTrader)

An automated trading system built in **C#** for the **cTrader (cAlgo)** platform, packaged with Docker so it can run headless, 24/7, outside the cTrader Desktop app.

The bot implements a hybrid trend-following strategy with dynamic risk management and multi-indicator signal confirmation.

**Disclaimer:** This project is for educational and research purposes only. It is not financial advice. Do not use with a live account or real money without extensive independent testing.

---

## TL;DR

- **Core idea:** Trend-following bot with advanced risk controls.
- **Built with:** C# + cTrader Automate API, targeting net6.0.
- **Runs on:** Docker, using [cTrader CLI](https://help.ctrader.com/ctrader-algo/documentation/ctrader-cli/) - Spotware's official headless runtime for running cBots without the Desktop app.
- **CI/CD:** GitHub Actions restores, builds, and Docker-builds the project on every push.

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

## Requirements

- [Docker](https://docs.docker.com/get-docker/)
- A [cTrader ID](https://ctrader.com/) (cTID) with at least one trading account - a free demo account works fine for testing
- .NET 6 SDK, only if you want to build/edit outside Docker

## Why net6.0

Both the `cTrader.Automate` NuGet package and cTrader CLI only support .NET 6 algos as of this writing - anything newer fails cTrader's own build validation with a `CT0004` error. The project is deliberately pinned to net6.0 for this reason, even though .NET 6 itself is past its official support window.

## Running it yourself

### 1. Clone and build the image

```bash
git clone https://github.com/Great-gla-27/Hybrid-Bot.git
cd Hybrid-Bot
docker build -t hybrid-bot .
```

### 2. Store your cTrader password locally

Create a folder outside the repo and put your cTID password in a plain text file. This never gets committed and never leaves your machine - it's only mounted into the container at runtime.

```bash
mkdir -p ~/ctrader-secrets
printf '%s' 'your-ctrader-password' > ~/ctrader-secrets/ctrader-cli.pwd
```

### 3. Look up your account number, if you don't already know it

```bash
docker run --rm \
  --mount type=bind,src=$HOME/ctrader-secrets,dst=/mnt/secrets \
  hybrid-bot accounts --ctid='you@example.com' --pwd-file=/mnt/secrets/ctrader-cli.pwd
```

### 4. Run it live

Use a demo account first.

```bash
docker run --rm --name hybrid-bot \
  --mount type=bind,src=$HOME/ctrader-secrets,dst=/mnt/secrets \
  -e CTID='you@example.com' \
  -e PWD-FILE='/mnt/secrets/ctrader-cli.pwd' \
  -e ACCOUNT='your-account-number' \
  -e SYMBOL='EURUSD' -e PERIOD='H1' \
  hybrid-bot
```

Swap `--rm` for `-d` to run it detached in the background, and check on it with `docker logs -f hybrid-bot`.

### 5. Backtest instead of running live

Simulates a full year of trading in well under a minute, using historical data instead of waiting on live candles.

```bash
docker run --rm \
  --mount type=bind,src=$HOME/ctrader-secrets,dst=/mnt/secrets \
  -e CTID='you@example.com' \
  -e PWD-FILE='/mnt/secrets/ctrader-cli.pwd' \
  -e ACCOUNT='your-account-number' \
  -e SYMBOL='EURUSD' -e PERIOD='H1' \
  hybrid-bot backtest /app/MainBot.algo \
  --start='01/01/2025' --end='31/12/2025' \
  --data-mode=m1 --balance=1000 \
  --report=/mnt/secrets/report.html \
  --environment-variables
```

The HTML report lands in `~/ctrader-secrets/report.html` on your machine.

## CI/CD

`.github/workflows/cicd.yml` runs on every push/PR to `main`/`master`: restores dependencies, compiles the project, and builds the Docker image, so a broken build fails in CI before it ever reaches a deployment.

## License

MIT - see [LICENSE.md](LICENSE.md).
