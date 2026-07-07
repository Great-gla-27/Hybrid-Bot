# Stage 1: Build Environment
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

COPY src/*.sln ./src/
COPY src/*.csproj ./src/
RUN dotnet restore src/MainBot.csproj

# Copy the rest of your source code
COPY src/ ./src/

# Compile the bot into a final release package
RUN dotnet publish src/MainBot.csproj -c Release -o /app/publish --no-restore

# Stage 2: Minimal Runtime Environment
FROM mcr.microsoft.com/dotnet/runtime:8.0-alpine AS final
WORKDIR /app
COPY --from=build /app/publish .

# Tell the container to run your specific compiled bot
ENTRYPOINT ["dotnet", "MainBot.dll"]

