ARG BUILDPLATFORM
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY app/ ./
RUN dotnet restore ZevEbixWeb.csproj
RUN dotnet publish ZevEbixWeb.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ZEVEBIX_DATA_DIR=/data
ENV ZEV_INTERNAL_PORT=8080
ENV ZEV_PUBLIC_PORT=8081

COPY --from=build /app/publish ./

EXPOSE 8080
EXPOSE 8081
ENTRYPOINT ["dotnet", "ZevEbixWeb.dll"]
