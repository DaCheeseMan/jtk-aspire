# Jonsereds TK – Tennis Court Booking

Web application for Jonsereds Tennisklubb built with .NET Aspire, ASP.NET Core, Keycloak, and React.

## Stack

| Layer | Technology |
|---|---|
| Orchestration | .NET Aspire 13 |
| Backend API | ASP.NET Core (.NET 10) + EF Core + PostgreSQL |
| Auth | Keycloak (OIDC / Authorization Code + PKCE) |
| Frontend | React 19 + Vite + TypeScript |
| Hosting | Azure Container Apps (via `azd`) |

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dot.net)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (for local Keycloak + Postgres containers)
- [Node.js 20+](https://nodejs.org)

### Run locally

```bash
cd JtK.AppHost
dotnet run
```

Aspire will start:
- **Keycloak** on http://localhost:8080 (admin: `admin` / `admin`)
- **PostgreSQL** for app data
- **API** (JtK.Server) proxied at `/api`
- **React frontend** (Vite dev server)

The realm `jtk` is imported automatically from `realms/jtk-realm.json` with two test users:

| Username | Password | Role |
|---|---|---|
| `member1` | `Password1!` | member |
| `admin1` | `Password1!` | member, admin |

### Deploy to Azure

```bash
# Install Azure Developer CLI
brew install azd
```

First-time deploy
```bash
# Log in to Azure, setup ACA and deploy
azd auth login --tenant-id eb31fcbb-d5a1-42b5-a28b-c8358bf94edf
azd up
```

`azd up` will:

1. Prompt for Azure subscription + region
2. Prompt for the secret parameters (KeycloakPassword, PostgresUser, PostgresPassword)
3. Provision all Azure resources (Container Apps Environment, PostgreSQL, Container Registry)
4. Build and push Docker images for the API + frontend
5. Deploy Keycloak + API + frontend as Container Apps

Subsequent deploys

```bash
azd deploy      # redeploy after code changes (skips infra provisioning)
```

One thing to update after first deploy

Once deployed, Keycloak will have a public URL. You'll need to add it to the realm's allowed redirect URIs. The easiest way is to update 
realms/jtk-realm.json with the actual ACA domain and redeploy — or do it via the Keycloak Admin Console at https://<keycloak-url>/admin.

Tear down
```bash
azd down        # deletes all provisioned Azure resources
```

## Project Structure

```
jtk-aspire/
├── JtK.AppHost/          # Aspire orchestration (AppHost.cs)
├── JtK.Server/           # ASP.NET Core Web API
│   ├── Models/           # Court, Booking entities
│   ├── Data/             # AppDbContext + EF migrations
│   └── Program.cs        # Minimal API endpoints + JWT auth
├── frontend/             # React (Vite + TypeScript)
│   └── src/
│       ├── api/          # Axios client (courts, bookings)
│       ├── components/   # Navbar, ProtectedRoute
│       └── pages/        # LandingPage, CourtsPage, BookingPage, MyBookingsPage
└── realms/               # Keycloak realm JSON (local dev import)
```

## API Endpoints

| Method | Path | Auth | Description |
|---|---|---|---|
| GET | `/api/courts` | Public | List all courts |
| GET | `/api/courts/{id}` | Public | Court details |
| GET | `/api/bookings` | ✅ JWT | My bookings |
| POST | `/api/bookings` | ✅ JWT | Create booking |
| DELETE | `/api/bookings/{id}` | ✅ JWT | Cancel booking |
