FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["TradePlatform.Worker/TradePlatform.Worker.csproj", "TradePlatform.Worker/"]
COPY ["TradePlatform.Core/TradePlatform.Core.csproj", "TradePlatform.Core/"]
COPY ["TradePlatform.Infrastructure/TradePlatform.Infrastructure.csproj", "TradePlatform.Infrastructure/"]
RUN dotnet restore "TradePlatform.Worker/TradePlatform.Worker.csproj"
COPY . .
WORKDIR "/src/TradePlatform.Worker"
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "TradePlatform.Worker.dll"]