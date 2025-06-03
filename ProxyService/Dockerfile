FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["ProxyService/ProxyService.csproj", "ProxyService/"]
RUN dotnet restore "ProxyService/ProxyService.csproj"
COPY . .
WORKDIR "/src/ProxyService"
RUN dotnet build "ProxyService.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "ProxyService.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ProxyService.dll"]
