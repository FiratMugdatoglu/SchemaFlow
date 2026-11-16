<div align="center">

<img src="icon.png" width="128" height="128" alt="SchemaFlow Logo" />

# 🌊 SchemaFlow
### Database Migration Safety & Static Analysis Engine for .NET & Entity Framework Core

[![.NET 9](https://img.shields.io/badge/.NET-9.0-purple.svg?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![NuGet](https://img.shields.io/nuget/v/SchemaFlow.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/SchemaFlow)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg?style=flat-square)](LICENSE)
[![Tests](https://img.shields.io/badge/Tests-38%20Passed-brightgreen.svg?style=flat-square)]()

**Prevent accidental production data loss and breaking schema drift before merging Pull Requests.**  
Powered by Microsoft Roslyn Compiler AST analysis and a Stateful SQL Lexer. 100% offline, zero database credentials required.

[The Problem](#-the-problem) • [How SchemaFlow Works](#-how-schemaflow-works) • [Installation](#-installation) • [CLI Quick Start](#-cli-usage--quick-start) • [GitHub Actions CI](#-github-actions-cicd) • [Community & Feedback](#-community--feedback)

---

</div>

## 💥 The Problem

In Entity Framework Core, renaming an entity property often generates a destructive migration:

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    // ⚠️ CRITICAL: Drops existing column and destroys ALL production user emails!
    migrationBuilder.DropColumn(name: "Email", table: "Users");

    // Creates an empty new column
    migrationBuilder.AddColumn<string>(name: "EmailAddress", table: "Users", nullable: true);
}
```

When this runs in production with millions of active users:
- ❌ All email addresses are permanently deleted.
- ❌ Older versions of your running backend crash immediately.
- ❌ Standard CI/CD passes because the C# code compiles and unit tests pass!

---

## 🛡️ How SchemaFlow Works

SchemaFlow acts as a **specialized linter and safety analyzer** for your migration files:

1. **Roslyn Compiler AST Analysis:** Parses your `Migration.cs` files directly into typed syntax trees without compiling your whole project or connecting to a live database.
2. **Stateful SQL Lexer:** Analyzes custom `migrationBuilder.Sql(...)` calls, distinguishing comments, string literals, and isolating destructive commands (`TRUNCATE`, `DROP`) from genuine backfill scripts.
3. **Expand & Contract Validation:** Verifies that column transitions follow zero-downtime best practices (`AddColumn` ➔ `Data Backfill SQL` ➔ `DropColumn`).
4. **Instant Actionable Feedback:** Flags risks directly in your terminal or GitHub Pull Request before bad migrations hit production.

```
┌─────────────────────────┐
│ EF Core Migration (.cs) │
└────────────┬────────────┘
             │
             ▼
┌─────────────────────────┐
│   Roslyn C# AST Parser  │  ──> Extracts DropColumn, AddColumn, AlterColumn, Sql
└────────────┬────────────┘
             │
             ▼
┌─────────────────────────┐
│    Stateful SQL Lexer   │  ──> Verifies backfill SQL & isolates hidden TRUNCATE/DROP
└────────────┬────────────┘
             │
             ▼
┌─────────────────────────┐
│  Migration Risk Engine  │  ──> Evaluates execution order, data loss, breaking changes
└────────────┬────────────┘
             │
             ▼
   🛑 Actionable Warnings (Terminal / CI / PR Comments)
```

---

## 📦 Installation

Install SchemaFlow globally as a .NET Tool with a single command:

```bash
dotnet tool install -g SchemaFlow
```

*To update an existing installation:*
```bash
dotnet tool update -g SchemaFlow
```

*Prerequisite: [.NET 9 SDK or Runtime](https://dotnet.microsoft.com/download).*

---

## 🚀 CLI Usage & Quick Start

| Command | Purpose | Primary Output |
| :--- | :--- | :--- |
| **`schemaflow diff`** | Detects breaking changes, dropped columns & risky migrations | Terminal audit table + Markdown report |
| **`schemaflow lint`** | Checks schemas for missing Primary Keys, unindexed Foreign Keys | Health Score (0-100) + SQL Quick Fixes |
| **`schemaflow generate`** | Generates visual Mermaid ER diagrams and Markdown docs | `docs/database.md` + Interactive HTML |
| **`schemaflow seed`** | Generates topologically-sorted relational test seed SQL | Executable `seed.sql` |

---

### 1. Detect Breaking Changes & Data Loss Risks (`diff`)

Analyze your migration files or SQL DDLs to verify safety:

```bash
# Compare migration files or SQL schemas
schemaflow diff -b ./Migrations/20261110_V1.cs -t ./Migrations/20261115_V2.cs

# Fail CI/CD build if breaking changes or unmitigated data loss risks are detected
schemaflow diff -b ./v1.sql -t ./v2.sql --fail-on-breaking
```

---

### 2. Schema Health & Anti-Pattern Linter (`lint`)

Audit your database schemas in under 50ms:

```bash
# Audit schema and open visual Health Dashboard in your browser
schemaflow lint -i ./schema.sql --open

# Fail CI/CD if critical architectural errors are found
schemaflow lint -i ./schema.sql --fail-on-errors
```

**Built-in Rules:**
* 🔴 **SF001:** Missing Primary Key detection.
* 🟡 **SF002:** Unindexed Foreign Key detection (prevents deadlocks during cascading updates/deletes).
* 🟡 **SF003:** SQL reserved keyword collisions (`Order`, `User`, `Group`, etc.).
* 🔴 **SF004:** Nullable Primary Key column warning.
* 🔵 **SF005:** Unbounded text type indexed warning (`TEXT`, `VARCHAR(MAX)`).

---

### 3. Generate Visual Docs & Mermaid ER Diagrams (`generate`)

Turn migration files into clean Markdown documentation and view diagrams locally:

```bash
# Auto-detects EF Core .cs migrations or SQL DDL
schemaflow generate -i ./Migrations/InitialCreate.cs -o ./docs/database.md --open
```

---

### 4. Smart Relational Mock Seed Data (`seed`)

Generate valid test SQL insert scripts respecting Foreign Key dependency graphs:

```bash
# Generate 10 mock rows per table
schemaflow seed -i ./Migrations/InitialCreate.cs -n 10 -o seed.sql
```

---

## 🤖 GitHub Actions (CI/CD)

Catch dangerous migrations automatically on every Pull Request. Add `.github/workflows/migration-check.yml`:

```yaml
name: "Database Migration Safety Check"

on:
  pull_request:
    paths:
      - '**/Migrations/**'
      - '**/*.sql'

jobs:
  safety-check:
    runs-on: ubuntu-latest
    steps:
      - name: Checkout code
        uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.0.x'

      - name: Install SchemaFlow
        run: dotnet tool install -g SchemaFlow

      - name: Audit Migration Safety
        run: schemaflow diff -b ./old_schema.sql -t ./new_schema.sql --fail-on-breaking
```

---

## 💬 Community & Feedback

SchemaFlow is under active open-source development. We want to make EF Core migrations safer for teams of all sizes!

- 🐛 **Found a bug or false-positive?** [Open an issue](https://github.com/FiratMugdatoglu/SchemaFlow/issues)
- 💡 **Have an idea or rule suggestion?** [Start a discussion](https://github.com/FiratMugdatoglu/SchemaFlow/discussions)
- ⭐ **Finding it useful?** Star the repository to show support!

---

## 📄 License
This project is licensed under the [MIT License](LICENSE) — free for personal and commercial use.
