# CLAUDE.md — Restaurant CRM (root)

Monorepo for a mobile-first web restaurant management system targeting the Armenian market.

## Working principle — research before implementing

Before writing code for any non-trivial feature, do a short research pass:

1. **Identify the canonical / industry-standard approach** for this class of problem (real-time messaging, soft delete, RBAC, payment flows, etc.). What do mature systems in the same domain do?
2. **Compare 2–3 alternatives** and pick the one that fits this project's constraints (mobile-first, tenant isolation, .NET 9 + PostgreSQL + React 19).
3. **Name the trade-off in one line** — what's being optimized for (latency, maintainability, scale, dev-velocity) and what's being given up.

Apply this even to features that look familiar. "How the industry does this" beats "how I always do this." Skipping the research step is how you end up with a stale pattern or a third-best solution. State the chosen approach and the rejected alternatives briefly before writing the first file — one line each.

## Layout

```
Restaurant  CRM/
├── Backend/src/          ← ASP.NET Core 9 + PostgreSQL (see Backend/src/CLAUDE.md)
├── Frontend/             ← Vite + React 19 + TypeScript, mobile-first PWA-style (see Frontend/CLAUDE.md)
└── Restaurant management system.docx  ← original requirements
```

**Invoke Claude Code from this directory** (not from `Backend/src/`) so both projects are in scope. The two are tightly coupled — DTO changes on one side need mirrored changes on the other.

## End-to-end run (local dev)

```bash
# Terminal 1 — backend
cd "Backend/src" && dotnet run --project RestaurantCRM.API
# → http://localhost:5293, OpenAPI at /openapi/v1.json

# Terminal 2 — frontend (choose ONE)
cd "Frontend" && npm run dev      # dev server with HMR (port 5173) — use while coding
cd "Frontend" && npm run start    # build + preview (port 5173) — bundled, use for phone testing
```

**Why `npm run start` for testing on a real phone:** dev mode serves hundreds of unbundled ES modules over HTTP, so a phone on the LAN makes 50–100+ requests per navigation. `npm run start` runs `vite build && vite preview` — one bundled ~130 KB gzipped file. Instant page transitions.

## API proxy (important)

Vite (dev or preview) proxies all `/api/*` requests to `http://localhost:5293` on the server side, so the browser only ever talks to one origin (no CORS, no mixed-content). This mirrors the production nginx setup. `VITE_API_BASE_URL` is intentionally empty in `.env`; never set it to a localhost URL.

## Contract sync

When backend DTOs change:
1. Update the C# request DTO + matching `AbstractValidator<T>` in `RestaurantCRM.Application`
2. From `Frontend/`: `npm run gen:api` (writes `src/lib/api-types.ts` from `/openapi/v1.json`)
3. Update Zod schemas in the relevant page to mirror new validation thresholds

## Deployment & Infrastructure

Containerized; deployed by a GitHub Actions pipeline. Full step-by-step in
[`DEPLOYMENT.md`](DEPLOYMENT.md). Key files and their roles:

| File | Role |
|---|---|
| `Backend/src/Dockerfile` | Multi-stage API image (sdk → aspnet runtime, non-root, `/health` probe) |
| `Frontend/Dockerfile` | Multi-stage web image (node build → nginx serving `dist/` + proxying `/api`,`/hubs`) |
| `Frontend/nginx.conf` | SPA fallback, `/api` + `/hubs` proxy to `api:8080`, cache + security headers |
| `docker-compose.yml` | **Local** full-stack run — builds both images, exposes web on `:8080`, no TLS |
| `docker-compose.prod.yml` | **Server** stack — pulls prebuilt images + adds Caddy for HTTPS |
| `Caddyfile` | Reverse proxy; auto Let's Encrypt TLS → forwards to `web:8080` |
| `.github/workflows/deploy.yml` | CI: build+push images (latest + `sha-` tags), sync compose/Caddyfile to server, `compose pull && up -d` |
| `.env.example` | Template for the server `.env` (Postgres + JWT secrets, `IMAGE_TAG` for rollback) |

Runtime flow: `caddy (443, TLS) → web (nginx) → api (:8080) → postgres`. Only
Caddy is internet-facing. The API runs **EF migrations on startup**
(`Database.Migrate()`), so deploys need no separate migration step. Rollback by
setting `IMAGE_TAG=sha-<commit>` in the server `.env` and re-running compose up.

Secrets: never in the repo or images. Server holds them in `~/restaurant-crm/.env`;
CI holds `DOCKER_USERNAME`, `DOCKER_PASSWORD`, `SERVER_HOST`, `SERVER_SSH_KEY` as
GitHub Actions secrets.

## What is implemented (as of now)

### Backend — endpoints
| Area | Endpoints |
|---|---|
| Auth | `POST /api/auth/register`, `/login`, `/change-password` |
| Restaurant | `GET /api/restaurants/me`, `PUT /api/restaurants/me` |
| Staff | `GET /api/staff`, `GET /api/staff/roles`, `GET /api/staff/{id}`, `POST /api/staff`, `PUT /api/staff/{id}`, `PUT /api/staff/{id}/permissions`, `DELETE /api/staff/{id}` |
| Menu | `GET /api/menu`, `POST/PUT/DELETE /api/menu/categories`, `POST/PUT/DELETE /api/menu/items`, `PATCH /api/menu/items/{id}/toggle`, `GET/PUT /api/menu/items/{id}/recipe` |
| Tables | `GET/POST/PUT/DELETE /api/tables`, `PATCH /api/tables/{id}/status` |
| Orders | `GET /api/orders`, `GET/POST /api/orders/{id}`, `POST/DELETE /api/orders/{id}/items[/{itemId}]`, `PATCH /api/orders/{id}/status`, `PATCH /api/orders/{id}/cancel`, `PATCH /api/orders/{id}/items/{itemId}/status`, `PATCH /api/orders/{id}/client`, `GET /api/orders/{id}/bill` |
| Kitchen (KDS) | `GET /api/kitchen/queue` (station-tagged items of open orders), `POST /api/kitchen/orders/{id}/bump` (atomic ticket bump → Ready/Served, optional station scope), `POST /api/kitchen/orders/{id}/recall` (undo: named Served items → Ready) |
| Clients | `GET /api/clients`, `GET /api/clients/{id}`, `POST /api/clients`, `PUT /api/clients/{id}`, `DELETE /api/clients/{id}`, `GET /api/clients/{id}/transactions`, `POST /api/clients/{id}/deposit`, `POST /api/clients/{id}/withdraw` |
| Products (Warehouse) | `GET /api/products`, `GET /api/products/categories`, `GET /api/products/{id}`, `POST /api/products`, `PUT /api/products/{id}`, `DELETE /api/products/{id}`, `GET/POST /api/products/{id}/movements` |
| Reservations | `GET /api/reservations`, `GET /api/reservations/{id}`, `POST /api/reservations`, `PUT /api/reservations/{id}`, `PATCH /api/reservations/{id}/status`, `DELETE /api/reservations/{id}` |
| Schedule | `GET /api/schedule`, `GET /api/schedule/{id}`, `POST /api/schedule`, `PUT /api/schedule/{id}`, `DELETE /api/schedule/{id}` |
| Cash Register | `GET /api/cash-register/summary`, `GET /api/cash-register/transactions`, `POST /api/cash-register/manual` |
| Reports | `GET /api/reports/summary`, `/top-items`, `/top-servers`, `/revenue-trend`, `/hourly-breakdown` |
| Activity Log | `GET /api/activity-log` |

### Frontend — implemented routes
| Route | Status |
|---|---|
| `/register` | Restaurant + admin self-signup (split into 2 visual sections) |
| `/login` | Email + password → JWT |
| `/dashboard` | Avatar header + permission-filtered nav cards with icons |
| `/staff` | Staff list with status badges |
| `/staff/new`, `/staff/:id/edit` | Create + edit staff |
| `/menu` | Category cards (click to drill in) |
| `/menu/categories/:id` | Items in a category, add/edit/toggle/delete per item |
| `/orders` | Order list with status filter tabs |
| `/orders/new` | Two-step create flow (table → items) |
| `/orders/:id` | Detail with add/close/cancel + item-status cycling |
| `/kitchen` | KDS board: tickets grouped per order, station filter (Kitchen/Bar, persisted), all-day production counts, bump + undo-recall snackbar, new-item chime (mutable), screen wake-lock, refetch on SignalR reconnect |
| `/settings` | Restaurant profile form (name, currency, address, phone) |
| `/schedule` | Placeholder |
| `/change-password` | Set new password (first login flow) |

### Auth & permissions
- **Short-lived access JWT (60 min) + rotating refresh token (14 days).** Access token includes the `permissions` claim (comma-separated `PermissionType` values). On a 401 the frontend silently calls `POST /api/auth/refresh` (single-flight) and replays the request; refresh tokens rotate on every use, reuse of a rotated token revokes the whole family, and logout revokes server-side.
- Login/Register response includes `permissions: string[]`, `restaurantName`, and `currency` — all stored in localStorage session.
- `usePermissions()` hook: `has(...perms)`, `hasAny(...perms)`.
- `RequireAuth` (frontend) checks localStorage session before render.
- `[RequirePermission(PermissionType.X)]` (backend, in `RestaurantCRM.API/Auth/`) checks the JWT claim and returns 403 if missing — so the API enforces permissions even if a bad client bypasses the frontend gate.
- Global 401 handler (frontend `api.ts`): if a request returns 401 *and* a token was sent, clear session and redirect to `/login`. Skips redirect on the login endpoint itself.

### Validation & error handling
- **FluentValidation** — every request DTO has an `AbstractValidator<T>` in the Application layer, auto-discovered via `AddValidatorsFromAssemblyContaining<T>`.
- Validation errors return `{"error": "first failure message"}` matching the rest of the API error envelope. The `InvalidModelStateResponseFactory` is configured *after* `AddControllers()` so it wins the options-pattern race.
- **Global exception handler** (`API/GlobalExceptionHandler.cs`, implements `IExceptionHandler`) maps:
  - `KeyNotFoundException` → 404
  - `InvalidOperationException` → 409
  - `UnauthorizedAccessException` → 401
  - `ArgumentException` → 400
  - anything else → 500 (logged with method + path)

  No controller has try/catch any more.

### Logging — Serilog
- `Program.cs` boots Serilog with a bootstrap logger first (catches startup failures), then swaps to the configured logger.
- Console sink + rolling file sink at `logs/log-{date}.txt` (kept 7 days, ignored in `.gitignore`).
- ASP.NET request noise filtered to Warning+; EF SQL filtered to Warning+.

### Real-time (SignalR) — implemented
- `OrderHub` (`/hubs/orders`) groups connections per tenant; JWT passed as `?access_token=` on the hub URL.
- `IRealtimeNotifier` emits id-only events (`orderChanged`, `tableChanged`, `reservationChanged`, `productChanged`, `menuItemChanged`, `scheduleChanged`) from the services after each mutation. Clients refetch via REST on receipt — payloads never carry data, so auth filters stay authoritative.
- Frontend: `lib/realtime.ts` (singleton connection, opened in `RequireAuth`, closed on logout) + `useRealtimeEvent(name, handler)` hook. Subscribed on Dashboard, Orders, OrderDetail, Tables, Reservations, CashRegister, Schedule, Reports, Warehouse, and the order-create flow.

### Not yet implemented
- (refresh tokens shipped — rotating, reuse-detecting, 60-min access + 14-day refresh)

> The feature tables above are kept high-level. The authoritative, always-current
> endpoint list is the controllers in `Backend/src/RestaurantCRM.API/Controllers/`
> and the routes in `Frontend/src/App.tsx`. See `Backend/src/CLAUDE.md` and
> `Frontend/CLAUDE.md` for the per-area breakdown.

## Test accounts (local DB)

| Email | Password | Role |
|---|---|---|
| `aro@mail.ru` | `secret123` | Admin — full permissions |
| `lili@mail.ru` | `lili123` | Waiter — limited permissions |

## Auth model

- `POST /api/auth/register` — atomically creates Restaurant + 6 default Roles + Admin User. Returns JWT + permissions + `restaurantName` + `currency`.
- `POST /api/auth/login` — single query that joins User → Restaurant → Role → RolePermissions (no N+1).
- `POST /api/auth/change-password` — `[Authorize]`, verifies current password, sets status → `Active`.

## Multi-tenancy

- JWT carries `restaurantId` → `ITenantContext` → EF Core global query filter on every `ITenantEntity`. You can never accidentally read another restaurant's data through the DbContext.
- **For inserts** (creating new `ITenantEntity` rows), always use `tenant.RestaurantId` — never derive from another entity's `RestaurantId` and never read `db.Restaurants.FirstOrDefault()` (that table has no tenant filter and may return the wrong restaurant).

## Common pitfalls when extending the project

1. **New endpoint must have a `[RequirePermission(...)]`** unless it's auth-only (`/api/auth/*`) or returns only the caller's own data (`/api/restaurants/me`).
2. **New service that creates `ITenantEntity`** must inject `ITenantContext` and set `RestaurantId = tenant.RestaurantId` on every new entity.
3. **New money field** must be `decimal` with `HasPrecision(18, 2)` in EF config — never `double` or `float`.
4. **New DTO that goes over HTTP** needs an `AbstractValidator<T>` next to it. The validator's max-length rules must match the EF Core entity config.
5. **New migration** belongs in `RestaurantCRM.Infrastructure/Persistence/Migrations/` (NOT the old `Migrations/` folder — that was consolidated).
