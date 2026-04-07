FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

COPY TradePlatform.BlazorClient/TradePlatform.BlazorClient.csproj TradePlatform.BlazorClient/
RUN dotnet restore TradePlatform.BlazorClient/TradePlatform.BlazorClient.csproj

COPY TradePlatform.BlazorClient/. TradePlatform.BlazorClient/
RUN dotnet publish TradePlatform.BlazorClient/TradePlatform.BlazorClient.csproj -c Release -o /app/publish

FROM nginx:alpine

COPY TradePlatform.BlazorClient/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/publish/wwwroot /usr/share/nginx/html

EXPOSE 80

CMD ["nginx", "-g", "daemon off;"]
