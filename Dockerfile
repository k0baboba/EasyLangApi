# syntax=docker/dockerfile:1

# --- сборка ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Сначала только проект: слой с пакетами NuGet кешируется, пока не меняется .csproj
COPY EasyLangApi.csproj ./
RUN dotnet restore EasyLangApi.csproj
COPY . .
RUN dotnet publish EasyLangApi.csproj -c Release -o /app --no-restore

# --- запуск ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update \
    && apt-get install -y --no-install-recommends tesseract-ocr tesseract-ocr-eng \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /data && chown app:app /data

WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_HTTP_PORTS=8080 \
    ConnectionStrings__Default="Data Source=/data/easylang.db" \
    Ocr__TesseractPath=tesseract

# База SQLite живёт в томе, чтобы переживать пересоздание контейнера
VOLUME /data
EXPOSE 8080
# Пользователь app (без root) есть в официальных образах .NET
USER app
ENTRYPOINT ["dotnet", "EasyLangApi.dll"]
