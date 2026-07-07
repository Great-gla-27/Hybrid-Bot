# Stage 1: Build Environment
# cTrader.Automate's build validation (and cTrader CLI itself) only supports
# net6.0 algos, so we compile with the .NET 6 SDK. Building the project with
# the cTrader.Automate package installed automatically produces a .algo file
# (cTrader's packaged algo format) alongside the regular build output.
FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /app

COPY src/*.sln ./src/
COPY src/*.csproj ./src/
RUN dotnet restore src/MainBot.csproj

# Copy the rest of your source code
COPY src/ ./src/

# Build the cBot. This generates src/bin/Release/net6.0/app.algo (the AlgoName
# MSBuild property defaults to "app", not the project name - confirmed by
# inspecting the actual build output).
RUN dotnet build src/MainBot.csproj -c Release --no-restore

# Stage 2: cTrader CLI runtime
# A cBot has no entry point of its own (no Main method) - it must be hosted by
# something that implements the cAlgo runtime. cTrader CLI is Spotware's
# official headless runtime for exactly this: running cBots 24/7 outside the
# cTrader Desktop app. See: https://github.com/spotware/ctrader-console-docker
FROM ghcr.io/spotware/ctrader-console:latest AS final
WORKDIR /app
COPY --from=build /app/src/bin/Release/net6.0/app.algo ./MainBot.algo

# Credentials and run parameters are supplied at container start via
# environment variables - never baked into the image. Required at `docker run`:
#   CTID       cTID username or email
#   PWD_FILE   path (inside the container) to a mounted file containing the cTID password
#   ACCOUNT    trading account number
#   SYMBOL     symbol to trade, e.g. EURUSD
#   PERIOD     chart period, e.g. H1
# Example:
#   docker run -d --name hybrid-bot \
#     --mount type=bind,src=/path/to/secrets,dst=/mnt/secrets \
#     -e CTID='you@example.com' -e PWD-FILE='/mnt/secrets/ctrader-cli.pwd' \
#     -e ACCOUNT='1234567' -e SYMBOL='EURUSD' -e PERIOD='H1' \
#     hybrid-bot
CMD ["run", "/app/MainBot.algo", "--environment-variables"]
