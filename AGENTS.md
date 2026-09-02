# Repository Guidelines

## Project Structure & Module Organization

`HRMS.slnx` contains the .NET 10 backend. `Backend/HRMS.Domain` owns entities, enums, and permission constants; `HRMS.Application` contains DTOs, validators, and business services; `HRMS.Infrastructure` contains EF Core contexts, configurations, migrations, seeding, authentication, and tenant sharding; `HRMS.API` exposes controllers and middleware. Backend tests live in `Backend/HRMS.Tests`.

The React 19/TypeScript frontend is in `Frontend/HRMS.Web`. Place API clients in `src/api`, authentication code in `src/auth`, reusable UI in `src/components`, and route-level features in `src/pages`. Static assets belong in `public` or `src/assets`.

## Build, Test, and Development Commands

Run backend commands from the repository root:

```powershell
dotnet restore HRMS.slnx
dotnet build HRMS.slnx
dotnet run --project Backend/HRMS.API
dotnet test Backend/HRMS.Tests/HRMS.Tests.csproj
```

Run frontend commands from `Frontend/HRMS.Web`:

```powershell
npm ci                 # install locked dependencies
npm run dev            # Vite development server
npm run build          # type-check and production build
npm run lint           # oxlint
npm run test:run       # Vitest once
```

## Coding Style & Naming Conventions

Follow surrounding code: C# uses four-space indentation, file-scoped namespaces, PascalCase public members, camelCase locals, and `Async` suffixes for asynchronous methods. Keep controllers thin and business rules in Application services.

TypeScript uses two-space indentation, single quotes, and semicolon-free formatting. Name React components and files in PascalCase, hooks with `use...`, and functions/variables in camelCase. Do not weaken strict TypeScript settings or tenant filters to silence errors.

## Testing Guidelines

Backend tests use xUnit, SQLite in-memory databases, and `WebApplicationFactory`; name files `*Tests.cs` and tests after observable behavior. Frontend tests use Vitest and Testing Library with `.test.ts` or `.test.tsx` names. New tenant-aware features should test authorization, cross-tenant isolation, validation, and failure cases. Run both suites before opening a PR.

## Commit & Pull Request Guidelines

History currently contains only a checkpoint commit, so no stable convention is established. Use short, imperative, scoped messages, for example `employees: validate employment hierarchy`. PRs should describe behavior and risks, link the issue, list commands run, call out schema/configuration changes, and include screenshots for UI changes.

## Security & Configuration

Never commit tokens, login payloads, connection strings, or real employee data. Keep secrets in local configuration or environment variables. Treat EF migrations, global query filters, host-based tenant resolution, and permission policies as security-sensitive changes requiring explicit review and data-preservation planning.
