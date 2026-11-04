FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY WebCrawler/WebCrawler.csproj WebCrawler/
COPY WebCrawler.Data/WebCrawler.Data.csproj WebCrawler.Data/
RUN dotnet restore WebCrawler/WebCrawler.csproj
COPY WebCrawler/ WebCrawler/
COPY WebCrawler.Data/ WebCrawler.Data/
RUN dotnet publish WebCrawler/WebCrawler.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "WebCrawler.dll"]
