# Land Acquisition Cell Platform — Phase 1

Modular-monolith foundation for a Delhi Land Acquisition Cell. The browser only calls the ASP.NET API; PostgreSQL is accessed only by `LAC.Api` through `LAC.Infrastructure`.

## Layout

- `src/LAC.Domain` — records, relationships, status vocabulary, Khasra normalization.
- `src/LAC.Infrastructure` — EF Core PostgreSQL mappings, demo seed data, local document-storage abstraction.
- `src/LAC.Api` — REST endpoints, Swagger, validation and ProblemDetails foundation.
- `src/LAC.Web` — React/Vite canonical detail-page UI.
- `tests/LAC.Tests` — critical relationship and normalization tests.

## Development setup

1. Install the .NET 10 SDK and PostgreSQL. Copy `.env.example` into your local environment; do not commit a connection string.
2. Run `dotnet restore`, `dotnet ef migrations add InitialCreate --project src/LAC.Infrastructure --startup-project src/LAC.Api`, then `dotnet run --project src/LAC.Api`.
3. Run `npm install` and `npm run dev` in `src/LAC.Web`.

The development seed is fictional: South West Delhi → Demo Sub-Division → Galibpur, with demo Khasras and acquisition records. It is not government data.

## Intentional Phase-1 boundaries

No compensation, NM, ENM, ownership, possession, litigation, OCR, external AI, or production document storage is present. Source LR text and its structured interpretation remain separate by design.
