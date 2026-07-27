FROM mcr.microsoft.com/dotnet/sdk:10.0.302 AS build
WORKDIR /src

COPY src/Toklong.Domain/Toklong.Domain.csproj src/Toklong.Domain/
COPY src/Toklong.Application/Toklong.Application.csproj src/Toklong.Application/
COPY src/Toklong.Infrastructure/Toklong.Infrastructure.csproj src/Toklong.Infrastructure/
COPY src/Toklong.Web/Toklong.Web.csproj src/Toklong.Web/
RUN dotnet restore src/Toklong.Web/Toklong.Web.csproj

COPY . .
RUN dotnet publish src/Toklong.Web/Toklong.Web.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.10 AS final
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
RUN mkdir -p \
        /var/lib/toklong/data-protection/api \
        /var/lib/toklong/data-protection/web \
        /var/lib/toklong/product-images \
    && chown -R "$APP_UID:$APP_UID" /var/lib/toklong
USER $APP_UID
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD curl --fail --silent --show-error http://127.0.0.1:8080/health/live || exit 1
ENTRYPOINT ["dotnet", "Toklong.Web.dll"]
