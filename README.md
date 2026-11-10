<div align="center">

# 🌊 SchemaFlow.ai
### Developer-First Database Schema Documentation & Interactive ER Diagrams

[![.NET 9](https://img.shields.io/badge/.NET-9.0-purple.svg?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![Docker](https://img.shields.io/badge/Docker-Ready-2496ED.svg?style=flat-square&logo=docker)](https://www.docker.com/)
[![GitHub Action](https://img.shields.io/badge/GitHub%20Action-v1-black.svg?style=flat-square&logo=githubactions)](https://github.com/marketplace)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg?style=flat-square)](LICENSE)

**Veritabanı şifresi yok. Canlı bağlantı riski yok. Sıfır konfigürasyon.**  
SQL ve migration dosyalarınızı saniyeler içinde zengin Markdown veri sözlüklerine ve interaktif Mermaid.js ER diyagramlarına dönüştürün.

[Özellikler](#-özellikler) • [Hızlı Başlangıç (CLI)](#-hızlı-başlangıç-cli) • [GitHub Action](#-github-action-entegrasyonu) • [Web Stüdyosu](#-interaktif-web-stüdyosu) • [Fiyatlandırma](#-fiyatlandırma)

---

</div>

## 💡 Neden SchemaFlow?

Geleneksel veritabanı dokümantasyon araçları veritabanınızın canlı IP'sini ve şifresini talep eder ya da her değişiklikte elle şema çizmenizi bekler. **SchemaFlow bu paradigmayı değiştirir:**

* 🔒 **Sıfır Bilgi Güvenliği (Zero-Knowledge Security):** Veritabanı kimlik bilgisi gerektirmez. Migration DDL dosyalarınızı yerel makinenizde veya CI/CD içinde çevrimdışı (offline) parse eder.
* 🌐 **Evrensel Ekosistem Desteği:**
  * 🐘 **PostgreSQL & Supabase (`.sql`)**
  * 🐬 **MySQL & MariaDB (`.sql`)**
  * 🟣 **.NET / C# Entity Framework Core (`.cs`)** — Dünyada ilk kez doğrudan C# migration okuma!
  * ▲ **TypeScript / Node.js Prisma (`.prisma`)** — Next.js & NestJS projeleri için yerel destek.
  * ☕ **Java & Spring Boot Liquibase (`.xml`) & Flyway (`.sql`)** — Kurumsal enterprise projeler.
  * 🐍 **Python Django & Wagtail (`.py`)** — Django migration dosyalarından doğrudan şema çıkarma.
  * 💎 **Ruby on Rails (`.rb`)** — Rails `schema.rb` dosyalarını anında görselleştirme.
  * 🐹 **Go / Golang Migrations (`.sql`)** — Goose, Golang-Migrate, SQLC desteği.
* 🤖 **GitHub PR Botu & CI/CD Native:** Pull Request açıldığında değişen tabloları otomatik analiz eder ve PR altına görsel ER diyagramı farkını yorum olarak ekler.
* 📁 **Temiz & Müstakil Dokümantasyon:** `README.md` dosyanızı yüzlerce satırlık tabloyla kirletmez; doğrudan `docs/database.md` altında tertemiz bir mimari harita oluşturur.
* ⚡ **Ultra Hafif & Hızlı:** .NET 9 Native gücüyle yüzlerce tabloluk kurumsal şemaları milisaniyeler içinde işler.

---

## 🚀 Hızlı Başlangıç (CLI)

### 1. Şema Dokümantasyonu & ER Diyagramı Üretme (`generate`)
SQL, C#, Prisma, Java XML, Python veya Ruby dosyanızı hedef göstererek dokümanınızı oluşturun:

```bash
# 1. PostgreSQL / MySQL / Java Flyway / Go (.sql)
schemaflow generate -i ./database/migrations -o ./docs/database.md

# 2. .NET / C# Entity Framework Core (.cs otomatik algılanır!)
schemaflow generate -i ./Migrations/InitialCreate.cs -o ./docs/database.md

# 3. TypeScript / Node.js Prisma (.prisma otomatik algılanır!)
schemaflow generate -i ./prisma/schema.prisma -o ./docs/database.md

# 4. Java Liquibase XML (.xml otomatik algılanır!)
schemaflow generate -i ./src/main/resources/changelog.xml -o ./docs/database.md

# 5. Python Django (.py otomatik algılanır!)
schemaflow generate -i ./core/migrations/0001_initial.py -o ./docs/database.md

# 6. Ruby on Rails (.rb otomatik algılanır!)
schemaflow generate -i ./db/schema.rb -o ./docs/database.md
```

### 2. Akıllı Şema Karşılaştırma & Breaking Change Dedektörü (`diff`)
İki migration dosyasını veya iki sürümü karşılaştırarak canlı sistemi çökertebilecek tehlikeli değişiklikleri anında yakalayın:

```bash
# İki şemayı karşılaştır ve terminalde renkli rapor al
schemaflow diff -b ./migrations/v1_old.sql -t ./migrations/v2_new.sql

# CI/CD içinde tehlikeli bir değişiklik varsa işlemi durdur (exit code 1)
schemaflow diff -b ./v1.sql -t ./v2.sql --fail-on-breaking

# Raporu Markdown olarak kaydet (PR yorumu için)
schemaflow diff -b ./old_schema.prisma -t ./new_schema.prisma -o ./diff_report.md
```

### 3. Komut Seçenekleri
| Komut | Parametre | Açıklama |
| :--- | :--- | :--- |
| `generate` | `-i, --input` | Ayrıştırılacak migration dosyası/klasörü (Zorunlu) |
| `generate` | `-o, --output` | Çıktının kaydedileceği hedef dosya (Varsayılan: `./docs/database.md`) |
| `generate` | `-f, --format` | Çıktı formatı: `markdown` veya `mermaid` |
| `diff` | `-b, --base` | Karşılaştırılacak temel (eski) şema dosyası |
| `diff` | `-t, --target` | Karşılaştırılacak hedef (yeni) şema dosyası |
| `diff` | `--fail-on-breaking` | Kritik Breaking Change varsa exit code 1 döner (CI/CD koruması) |

---

## 🤖 GitHub Action Entegrasyonu

Takım arkadaşlarınız yeni bir migration eklediğinde dokümantasyonun kendiliğinden güncellenmesi için projenize `.github/workflows/schemaflow.yml` ekleyin:

```yaml
name: "SchemaFlow Database Docs"

on:
  push:
    branches: [ main ]
    paths:
      - 'migrations/**'
      - 'database/**'

jobs:
  document-schema:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Generate Database Documentation
        uses: schemaflow/schemaflow-action@v1
        with:
          input: './migrations'
          output: './README.md'
          inject: 'true'

      - name: Commit Updated Docs
        run: |
          git config --global user.name "SchemaFlow Bot"
          git config --global user.email "bot@schemaflow.ai"
          git commit -am "docs(db): otomatik şema dokümantasyonu güncellendi" || echo "Değişiklik yok"
          git push
```

---

## 🎨 İnteraktif Web Stüdyosu

Tarayıcı üzerinden görsel pan & zoom özellikli ER diyagramı deneyimi yaşamak için Web API'yi başlatın:

```bash
cd src/SchemaFlow.Api
dotnet run
```
Tarayıcınızda `http://localhost:5285/` adresini açarak SQL'inizi yapıştırın ve şemanızı anında interaktif bir stüdyo tuvalinde inceleyin!

---

## 💎 Fiyatlandırma (Pricing)

SchemaFlow **Open-Core** modelini benimser. Yerel geliştirme her zaman ücretsizdir; otomasyon ve bulut özellikleri abonelikle sunulur.

| Özellik | Free (Developer) | Pro (Solo Builder) | Team (Ajans & Ekipler) |
| :--- | :---: | :---: | :---: |
| **Aylık Fiyat** | **$0** | **$9 / ay** *(veya $79/yıl)* | **$29 / ay** *(veya $249/yıl)* |
| **Yerel CLI Kullanımı** | Sınırsız & Çevrimdışı | Sınırsız | Sınırsız |
| **Mermaid ER & Markdown** | Dahil | Dahil | Dahil |
| **Web Stüdyosu İnceleme** | Dahil | Dahil | Dahil |
| **GitHub Action PR Botu** | Ayda 10 PR | **Sınırsız PR** | **Sınırsız PR** |
| **Breaking Change Tespiti** | — | **Dahil** (Kolon silinme alarmı) | **Dahil** |
| **Paylaşılabilir Canlı Link** | — | **Dahil** (Müşteri sunum linki) | **Dahil** |
| **Dışa Aktarma (Export)** | Markdown | PDF, SVG, Yüksek Kalite PNG | PDF, SVG, PNG |
| **White-Label (Kendi Logosu)** | — | — | **Dahil** (Markasız) |
| **Kullanıcı Sayısı** | 1 Kişi | 1 Kişi | **5 Lisans** |

---

## 🛠️ Canlıya Alma ve Deployment

Projenizi kendi bulut sunucunuza (Fly.io, Render, Railway, Supabase ve Lemon Squeezy) deploy etmek için adım adım hazırladığımız kılavuzu inceleyin:

👉 [**Deployment Guide (DEPLOYMENT.md)**](DEPLOYMENT.md)

---

## 📄 Lisans
Bu proje [MIT Lisansı](LICENSE) kapsamında açık kaynak olarak geliştirilmiştir.
