FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /work
COPY global.json Directory.Build.props ./
COPY src/Inventory.Api/Inventory.Api.csproj src/Inventory.Api/packages.lock.json ./src/Inventory.Api/
RUN dotnet restore src/Inventory.Api/Inventory.Api.csproj --locked-mode
COPY src ./src
RUN dotnet publish src/Inventory.Api/Inventory.Api.csproj -c Release --no-restore -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_HTTP_PORTS=8080 DOTNET_EnableDiagnostics=0
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Inventory.Api.dll"]
