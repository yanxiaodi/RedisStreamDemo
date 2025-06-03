FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["IngressService/IngressService.csproj", "IngressService/"]
RUN dotnet restore "IngressService/IngressService.csproj"
COPY . .
WORKDIR "/src/IngressService"
RUN dotnet build "IngressService.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "IngressService.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "IngressService.dll"]
