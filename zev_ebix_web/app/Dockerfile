# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Jordi.EbixAbrechnung.slnx ./
COPY ZevEbixWeb/ZevEbixWeb.csproj ZevEbixWeb/
RUN dotnet restore ZevEbixWeb/ZevEbixWeb.csproj

COPY . .
RUN dotnet publish ZevEbixWeb/ZevEbixWeb.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

COPY --from=build /app/publish ./

EXPOSE 8080
ENTRYPOINT ["dotnet", "ZevEbixWeb.dll"]
