FROM mcr.microsoft.com/dotnet/sdk:10.0.302 AS build
WORKDIR /src

COPY src/Toklong.Domain/Toklong.Domain.csproj src/Toklong.Domain/
COPY src/Toklong.Application/Toklong.Application.csproj src/Toklong.Application/
COPY src/Toklong.Infrastructure/Toklong.Infrastructure.csproj src/Toklong.Infrastructure/
COPY src/Toklong.Worker/Toklong.Worker.csproj src/Toklong.Worker/
RUN dotnet restore src/Toklong.Worker/Toklong.Worker.csproj

COPY . .
RUN dotnet publish src/Toklong.Worker/Toklong.Worker.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0.10 AS final
RUN apt-get update \
    && apt-get install --yes --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "Toklong.Worker.dll"]
