# ADR-005 — EF Core Migrations over Fluent Migrator / DbUp

**Status:** Accepted  
**Date:** 2026-06-06

## Decision

Use EF Core code-first migrations with the Npgsql provider (`Npgsql.EntityFrameworkCore.PostgreSQL`). Migrations run automatically on startup via `dbContext.Database.MigrateAsync()` in `Program.cs`.

## Reasons

- Models, queries, and migrations all in C# within the same project; no context-switching
- Built-in seeding support for default comfort question templates
- Well-supported PostgreSQL provider
- No manual migration steps after deployment

## Rejected Alternatives

**Fluent Migrator / DbUp** — SQL-centric; lose C# integration and built-in seeding.
