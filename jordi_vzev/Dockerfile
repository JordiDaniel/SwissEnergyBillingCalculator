ARG BUILDPLATFORM
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY app/ ./
RUN dotnet restore Jordi.vzev.csproj
RUN dotnet publish Jordi.vzev.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8082
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ZEVEBIX_DATA_DIR=/data
ENV JORDI_VZEV_PORT=8082
ENV HA_URL=http://homeassistant:8123
ENV HA_TOKEN=

COPY --from=build /app/publish ./

EXPOSE 8082
ENTRYPOINT ["dotnet", "Jordi.vzev.dll"]
