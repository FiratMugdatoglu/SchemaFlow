<div align="center">

# 🌊 SchemaFlow.ai
### Developer-First Universal Database Schema Documentation, Diagramming, Linter & Diff Engine

[![.NET 9](https://img.shields.io/badge/.NET-9.0-purple.svg?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![NuGet](https://img.shields.io/nuget/v/SchemaFlow.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/SchemaFlow)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg?style=flat-square)](LICENSE)

**No database credentials required. Zero risk of live connection breaches. 100% offline.**  
Transform your SQL and ORM migration files in milliseconds into rich Markdown data dictionaries, interactive Mermaid.js ER diagrams, schema health audits, and smart seed test data.

[Why SchemaFlow?](#-why-schemaflow) • [Installation](#-installation) • [CLI Usage](#-cli-usage) • [GitHub Action](#-github-action-ci--cd) • [Web Studio](#-interactive-web-studio)

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

## 🚀 CLI Usage & Quick Start

| Command | Input | Primary Output | `--open` Browser Experience |
| :--- | :--- | :--- | :--- |
| **`schemaflow generate`** | 1 schema file | `docs/database.md` | Interactive Pan/Zoom ERD + Searchable Data Dictionary |
| **`schemaflow diff`** | 2 schema files | `diff_report.md` | Side-by-Side (Before vs After) visual diagrams |
| **`schemaflow lint`** | 1 schema file | `lint_report.md` | 100-pt Health Score + Live ERD + 1-Click SQL Quick Fixes |
| **`schemaflow seed`** | 1 schema file | `seed.sql` | Executable relational mock SQL insert script |

---

### 1. Generate Schema Docs & Interactive ER Diagram (`generate`)
Transforms your database migration or DDL files into professional Markdown and launches an interactive HTML dashboard:

```bash
# .NET Entity Framework Core (.cs is auto-detected!) - Opens browser with ER diagram & Data Dictionary
schemaflow generate -i ./Migrations/InitialCreate.cs -o ./docs/database.md --open

# TypeScript Prisma (.prisma is auto-detected!)
schemaflow generate -i ./prisma/schema.prisma -o ./docs/database.md --open

# PostgreSQL / MySQL / Supabase (.sql)
schemaflow generate -i ./database/schema.sql -o ./docs/database.md --open

# Python Django (.py is auto-detected!)
schemaflow generate -i ./core/migrations/0001_initial.py -o ./docs/database.md --open

# Java Liquibase (.xml is auto-detected!)
schemaflow generate -i ./src/main/resources/changelog.xml -o ./docs/database.md --open

# Ruby on Rails (.rb is auto-detected!)
schemaflow generate -i ./db/schema.rb -o ./docs/database.md --open
```

**Outputs:**
* 🌐 **Browser (`--open`):** Interactive canvas with zoom/pan ER diagram and tabbed searchable Data Dictionary.
* 📄 **File (`-o`):** Beautiful GitHub/GitLab-ready Markdown with Mermaid.js diagram.

---

### 2. Smart Schema Diff & Breaking Change Detector (`diff`)
Compare two schema versions, catch breaking changes, and visualize structural changes side-by-side:

```bash
# Compare versions and launch side-by-side Before/After visual diagrams in browser
schemaflow diff -b ./v1_schema.sql -t ./v2_schema.sql -o ./diff_report.md --open

# Block CI/CD deployments if destructive breaking changes are detected (exit code 1)
schemaflow diff -b ./v1.sql -t ./v2.sql --fail-on-breaking
```

**Outputs:**
* 💻 **Terminal:** Color-coded table of 🔴 BREAKING and 🟢 SAFE schema modifications.
* 🌐 **Browser (`--open`):** Dual-column visual diagram comparing the legacy vs new architecture.
* 📄 **File (`-o`):** Complete Markdown diff report with Before/After Mermaid diagrams.

---

### 3. Database Health & Anti-Pattern Linter (`lint`)
Audits your schemas for missing Primary Keys, unindexed Foreign Keys, and reserved keyword collisions in under 50ms:

```bash
# Audit schema and launch visual Health Dashboard with instant SQL fixes
schemaflow lint -i ./schema.sql --open

# Fail CI/CD pipelines if critical architectural errors are found
schemaflow lint -i ./schema.sql --fail-on-errors
```

**Outputs:**
* 🌐 **Browser (`--open`):** Overall Health Score (0-100), visual ER diagram, risk cards, and **ready-to-run SQL Quick-Fix snippets**.
* 💻 **Terminal:** Severity-ranked audit table (`🔴 ERROR`, `🟡 WARN`, `🔵 INFO`).

**Built-in Rules:**
* 🔴 **SF001:** Missing Primary Key detection (uniquely addresses data integrity).
* 🟡 **SF002:** Unindexed Foreign Key detection (prevents catastrophic table locks during JOINs and CASCADE operations).
* 🟡 **SF003:** Reserved SQL keyword collisions (`order`, `user`, `group`, etc.).
* 🔴 **SF004:** Nullable Primary Key column warning.
* 🔵 **SF005:** Unbounded text type indexed warning (`TEXT`, `VARCHAR(MAX)`).

---

### 4. Smart Relational Mock Seed Generator (`seed`)
Generates valid, relational fake test data respecting Foreign Key hierarchies:

```bash
# Generate 10 relational mock rows per table
schemaflow seed -i ./Migrations/InitialCreate.cs -n 10 -o seed.sql
```

**Outputs:**
* 📄 **`seed.sql`:** Ready-to-execute SQL file populated with valid emails, currency values, dates, and matched Foreign Key references. Ready to pipe directly into `psql` or `mysql`.

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

## 📄 License
This project is licensed under the [MIT License](LICENSE) - free for personal and commercial use.
