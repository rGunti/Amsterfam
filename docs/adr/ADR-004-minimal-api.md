# ADR-004 — .NET Minimal API over MVC Controllers

**Status:** Accepted  
**Date:** 2026-06-06

## Decision

Use .NET 10 Minimal API. Endpoints are organised into feature-grouped extension methods:

```csharp
app.MapAvailabilityEndpoints();
app.MapAccommodationEndpoints();
app.MapActivityEndpoints();
// ...
```

Each group lives in its own file; `Program.cs` stays clean.

## Reasons

- Less boilerplate; endpoints are plain functions, no controller classes or attributes
- Aligns with the direction Microsoft is actively pushing in .NET 10
- Naturally encourages feature-grouped organisation over resource-grouped controllers
- Marginally better performance (less MVC middleware overhead)

## Rejected Alternative

**MVC Controllers** — more boilerplate, attribute-heavy, and less idiomatic for .NET 10.
