# Stage 1: Build
# cTrader.Automate only supports net6.0 algos, so I build with the .NET 6 SDK.
# Installing the package generates a .algo file alongside the normal build output.
FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /app

COPY src/*.sln ./src/
COPY src/*.csproj ./src/
RUN dotnet restore src/MainBot.csproj

# Copy the rest of my source code
COPY src/ ./src/

# Builds to src/bin/Release/net6.0/app.algo - AlgoName defaults to "app", not
# the project name.
RUN dotnet build src/MainBot.csproj -c Release --no-restore

# Stage 2: cTrader CLI runtime
# A cBot has no entry point of its own, so it needs to run inside cTrader CLI,
# Spotware's headless runtime for running cBots 24/7 without cTrader Desktop.
FROM ghcr.io/spotware/ctrader-console:latest AS final
WORKDIR /app
COPY --from=build /app/src/bin/Release/net6.0/app.algo ./MainBot.algo

# Credentials and run parameters get passed in as env vars at container start,
# never baked into the image:
#   CTID       cTID username or email
#   PWD_FILE   path to a mounted file containing the cTID password
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
