<div align="center">

# 🌊 SchemaFlow.ai
### Developer-First Universal Database Schema Documentation, Diagramming, Linter & Diff Engine

[![.NET 9](https://img.shields.io/badge/.NET-9.0-purple.svg?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![NuGet](https://img.shields.io/nuget/v/SchemaFlow.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/SchemaFlow)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg?style=flat-square)](LICENSE)

**No database credentials required. Zero risk of live connection breaches. 100% offline.**  
Transform your SQL and ORM migration files in milliseconds into rich Markdown data dictionaries, interactive Mermaid.js ER diagrams, schema health audits, and smart seed test data.

[Features](#-features) • [Installation](#-installation) • [CLI Usage](#-cli-usage) • [GitHub Action](#-github-action-ci-cd) • [Web Studio](#-interactive-web-studio) • [Pricing](#-pricing)

---

</div>

## 💡 Why SchemaFlow?

Traditional database documentation and auditing tools require live database connection strings, opening ports, and granting read permissions to your production databases. **SchemaFlow completely redefines this workflow:**

* 🔒 **Zero-Knowledge Security:** No database host, username, or password required. Parse migration DDLs completely offline inside your local environment or CI/CD pipelines.
* 🌐 **Universal Multi-Ecosystem Support:**
  * 🐘 **PostgreSQL & Supabase (`.sql`)**
  * 🐬 **MySQL & MariaDB (`.sql`)**
  * 🟣 **.NET / C# Entity Framework Core (`.cs`)** — World's first tool parsing C# migration files directly!
  * ▲ **TypeScript / Node.js Prisma (`schema.prisma`)** — Native schema support for Next.js & NestJS.
  * ☕ **Java & Spring Boot Liquibase (`changelog.xml`) & Flyway (`.sql`)**
  * 🐍 **Python Django & Wagtail (`.py`)** — Automatic parsing of Django migrations.
  * 💎 **Ruby on Rails (`schema.rb`)** — Instant visual schemas from Rails schema dumps.
  * 🐹 **Go Migrations (`.sql`)** — Goose, Golang-Migrate, SQLC compatible.
* 🛡️ **Breaking Change Prevention:** Detect dangerous schema drift (dropped columns, narrowing types, non-null additions without defaults) before merging Pull Requests.
* 🔍 **Database Health & Anti-Pattern Linter:** Catch missing Primary Keys, unindexed Foreign Keys, and reserved SQL keyword collisions in under 50ms.
* 🌱 **Smart Relational Mock Data Generator:** Topological graph ordering generates valid relational fake test seed SQL without foreign key constraint violations.
* 📁 **Clean & Modular Documentation:** Generates standalone `docs/database.md` with responsive Mermaid ER diagrams.

---

## 📦 Installation

Install SchemaFlow globally on your machine with one command:

```bash
dotnet tool install -g SchemaFlow
```

*Prerequisite: [.NET 9 SDK or Runtime](https://dotnet.microsoft.com/download) installed on your system.*

---

## 🚀 CLI Usage

### 1. Generate Schema Docs & Mermaid ER Diagram (`generate`)
Point to any migration directory or schema file:

```bash
# PostgreSQL / MySQL / Flyway / Go (.sql)
schemaflow generate -i ./database/schema.sql -o ./docs/database.md

# .NET Entity Framework Core (.cs is auto-detected!)
schemaflow generate -i ./Migrations/InitialCreate.cs -o ./docs/database.md

# TypeScript Prisma (.prisma is auto-detected!)
schemaflow generate -i ./prisma/schema.prisma -o ./docs/database.md

# Python Django (.py is auto-detected!)
schemaflow generate -i ./core/migrations/0001_initial.py -o ./docs/database.md

# Java Liquibase (.xml is auto-detected!)
schemaflow generate -i ./src/main/resources/changelog.xml -o ./docs/database.md

# Ruby on Rails (.rb is auto-detected!)
schemaflow generate -i ./db/schema.rb -o ./docs/database.md
```

---

### 2. Smart Schema Diff & Breaking Change Detection (`diff`)
Compare two schema versions and catch breaking changes before deploying to production:

```bash
# Compare two versions with color-coded CLI report
schemaflow diff -b ./migrations/v1.sql -t ./migrations/v2.sql

# Fail CI/CD pipeline if destructive breaking changes are found (exit code 1)
schemaflow diff -b ./v1.sql -t ./v2.sql --fail-on-breaking

# Export markdown report for GitHub PR comments
schemaflow diff -b ./old_schema.prisma -t ./new_schema.prisma -o ./diff_report.md
```

---

### 3. Database Health & Anti-Pattern Linter (`lint`)
Audit your schemas against critical architectural rules and performance bottlenecks:

```bash
schemaflow lint -i ./schema.sql

# Fail CI/CD if warnings or errors are present
schemaflow lint -i ./schema.prisma --fail-on-warnings
```

**Built-in Rules:**
* 🔴 **SF001:** Missing Primary Key detection.
* 🟡 **SF002:** Unindexed Foreign Key detection (prevents table lockouts during JOINs and CASCADE operations).
* 🟡 **SF003:** Reserved SQL keyword collision detection (`user`, `order`, `group`, etc.).
* 🔴 **SF004:** Nullable Primary Key column warning.
* 🔵 **SF005:** Unbounded text types indexed warning (`TEXT`, `VARCHAR(MAX)`).

---

### 4. Smart Relational Mock Seed Generator (`seed`)
Generate foreign-key aligned mock data for test environments:

```bash
schemaflow seed -i ./schema.prisma --count 25 --output seed.sql
```

SchemaFlow resolves the topological dependency tree (parent tables first, child tables second) and automatically populates realistic mock values (emails, names, dates, amounts, status flags).

---

## 🤖 GitHub Action (CI / CD)

Add SchemaFlow to your GitHub Actions workflow (`.github/workflows/schemaflow.yml`):

```yaml
name: "SchemaFlow Database CI"

on:
  pull_request:
    paths:
      - '**/*.sql'
      - '**/*.prisma'
      - '**/Migrations/**'

jobs:
  schema-check:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.0.x'

      - name: Install SchemaFlow
        run: dotnet tool install -g SchemaFlow

      - name: Audit Schema
        run: schemaflow lint -i ./database/schema.sql --fail-on-errors

      - name: Update Documentation
        run: schemaflow generate -i ./database/schema.sql -o ./docs/database.md
```

---

## 🎨 Interactive Web Studio

Launch the local interactive Web Studio with responsive canvas, pan & zoom:

```bash
cd src/SchemaFlow.Api
dotnet run
```
Open `http://localhost:5285/` in your browser to paste your SQL and explore your database schema visually.

---

## 💎 Pricing

SchemaFlow follows the **Open-Core** philosophy. Local CLI usage is 100% free; automated team and cloud collaboration features are available via subscription.

| Feature | Free (Developer) | Pro (Solo Builder) | Team (Agencies & Teams) |
| :--- | :---: | :---: | :---: |
| **Monthly Price** | **$0** | **$9 / month** | **$29 / month** |
| **Local CLI Engine** | Unlimited & Offline | Unlimited | Unlimited |
| **Mermaid ER & Markdown** | Included | Included | Included |
| **Schema Linter & Diff** | Included | Included | Included |
| **Smart Seed Generator** | Included | Included | Included |
| **GitHub Action PR Bot** | Up to 10 PRs / mo | **Unlimited PRs** | **Unlimited PRs** |
| **Breaking Change Alerts** | — | **Included** | **Included** |
| **Shareable Cloud Studio** | — | **Included** | **Included** |
| **Export Formats** | Markdown | PDF, SVG, Hi-Res PNG | PDF, SVG, PNG |
| **Seats** | 1 Developer | 1 Developer | **5 Seats** |

---

## 📄 License
This project is licensed under the [MIT License](LICENSE).
