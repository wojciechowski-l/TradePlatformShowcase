FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["TradePlatform.Api/TradePlatform.Api.csproj", "TradePlatform.Api/"]
COPY ["TradePlatform.Core/TradePlatform.Core.csproj", "TradePlatform.Core/"]
COPY ["TradePlatform.Infrastructure/TradePlatform.Infrastructure.csproj", "TradePlatform.Infrastructure/"]
RUN dotnet restore "TradePlatform.Api/TradePlatform.Api.csproj"
COPY . .
WORKDIR "/src/TradePlatform.Api"
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080

ENTRYPOINT ["dotnet", "TradePlatform.Api.dll"]