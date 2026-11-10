# ==========================================
# 1. Aşama: Derleme & Paketleme (Build Stage)
# ==========================================
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Docker önbelleğini (cache) verimli kullanmak için önce sadece proje dosyalarını kopyalayıp restore ediyoruz
COPY SchemaFlow.sln ./
COPY src/SchemaFlow.Core/SchemaFlow.Core.csproj src/SchemaFlow.Core/
COPY src/SchemaFlow.Cli/SchemaFlow.Cli.csproj src/SchemaFlow.Cli/

RUN dotnet restore src/SchemaFlow.Cli/SchemaFlow.Cli.csproj

# Tüm kaynak kodları kopyala ve optimize edilmiş Release sürümünü yayınla
COPY src/ src/
RUN dotnet publish src/SchemaFlow.Cli/SchemaFlow.Cli.csproj -c Release -o /app/publish --no-restore

# ==========================================
# 2. Aşama: Çalışma Zamanı (Runtime Stage - Hafif & Güvenli)
# ==========================================
FROM mcr.microsoft.com/dotnet/runtime:9.0 AS runtime
WORKDIR /workspace

# Sadece derlenmiş saf dosyaları alıyoruz (SDK'yı çöpe atarak boyutu 1 GB'tan ~180 MB'a düşürüyoruz)
COPY --from=build /app/publish /app

# Konteyner başlatıldığında SchemaFlow CLI aracımızı çalıştır
ENTRYPOINT ["dotnet", "/app/SchemaFlow.Cli.dll"]

# Varsayılan argüman olarak kılavuzu göster
CMD ["--help"]
