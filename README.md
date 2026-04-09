# JobIntelligence

A job market intelligence platform that aggregates job postings from multiple applicant tracking systems, enriches company data, and surfaces trends through a dashboard with AI-powered chat.

## What it does

- Collects job postings daily from Greenhouse, Lever, Ashby, SmartRecruiters, and Workday
- Tracks hiring trends over time per company (active jobs, new postings, removals)
- Enriches company profiles with employee count, headquarters, founding year via Wikidata
- Tags job postings with skills using a keyword-matched skill taxonomy
- Provides a dashboard with aggregate stats: remote vs onsite, seniority distribution, top departments, top hiring companies
- AI chat powered by Claude (Anthropic) for natural language job market queries
- Filters for US-only vs global postings

## Architecture

```
JobIntelligence/
├── backend/
│   ├── JobIntelligence.Core/           # Domain entities and service interfaces
│   ├── JobIntelligence.Infrastructure/ # EF Core, collectors, services, persistence
│   ├── JobIntelligence.API/            # ASP.NET Core controllers, DI composition root
│   └── JobIntelligence.Worker/         # (unused — collection is triggered externally)
└── frontend/                           # Angular 17+ SPA
```

The backend follows Clean Architecture: `Core` has no external dependencies, `Infrastructure` implements Core interfaces, and `API` wires everything together.

## Tech stack

| Layer | Technology |
|-------|-----------|
| Frontend | Angular 17, Angular Material |
| Backend | ASP.NET Core 8, C# |
| ORM | Entity Framework Core + Npgsql |
| Database | PostgreSQL (AWS RDS) |
| AI | Anthropic Claude API |
| Hosting | AWS Elastic Beanstalk (Linux) |
| CI/CD | AWS CodeBuild |
| Secrets | AWS Secrets Manager + SSM Parameter Store |
| Collection trigger | AWS Lambda (scheduled) |

## Local development

### Prerequisites

- .NET 8 SDK
- Node.js 18+
- PostgreSQL (or access to the RDS instance)

### Backend

Add `appsettings.Development.json` to `backend/JobIntelligence.API/` with:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=...;Port=5432;Database=JobIntelligence;Username=postgres;Password=...;SSL Mode=Require;Trust Server Certificate=true"
  },
  "Anthropic": {
    "ApiKey": "sk-ant-..."
  },
  "AdminKey": "...",
  "BraveSearch": {
    "ApiKey": "..."
  }
}
```

Then run:

```bash
cd backend/JobIntelligence.API
dotnet run
```

EF Core migrations are applied automatically on startup in development.

### Frontend

```bash
cd frontend
npm install
npm start
```

The Angular dev server runs on `http://localhost:4200` and proxies `/api` requests to the backend at `http://localhost:5178`.

## Collection

Job collection is triggered by an AWS Lambda on a schedule. It can also be triggered manually via the internal API (requires the `AdminKey` header):

```bash
# Trigger collection for all sources
curl -X POST https://<host>/internal/collection/trigger \
  -H "X-Admin-Key: <key>"

# Trigger for a specific source
curl -X POST "https://<host>/internal/collection/trigger?source=greenhouse" \
  -H "X-Admin-Key: <key>"

# Check status
curl https://<host>/internal/collection/status \
  -H "X-Admin-Key: <key>"

# Cancel a running collection
curl -X POST https://<host>/internal/collection/cancel \
  -H "X-Admin-Key: <key>"
```

Supported source names: `greenhouse`, `lever`, `ashby`, `smartrecruiters`, `workday`

After collection completes, dashboard snapshots are written to the `dashboard_snapshots` table so the dashboard reads pre-computed stats rather than running aggregation queries on demand.

## Deployment

Deployments are handled by AWS CodeBuild using `buildspec.yml` at the repo root. On each build:

1. Angular is built for production
2. The .NET API is published
3. The Angular dist is copied into `wwwroot/` of the publish output
4. The artifact is deployed to Elastic Beanstalk

The app serves the Angular SPA as static files and falls back to `index.html` for client-side routing.

In production, secrets are loaded from AWS SSM Parameter Store under the `/jobintelligence/` path prefix. No secrets are stored in `appsettings.json`.

## Database migrations

```bash
cd backend
dotnet ef migrations add <MigrationName> --project JobIntelligence.Infrastructure --startup-project JobIntelligence.API
dotnet ef database update --project JobIntelligence.Infrastructure --startup-project JobIntelligence.API
```
