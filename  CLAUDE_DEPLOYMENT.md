# Deployment Guide — Restaurant CRM

How to deploy this app to a fresh Linux server and how the CI/CD pipeline works.
Commands target **Amazon Linux 2023 on AWS EC2** (the pipeline's default SSH user
is `ec2-user`). For Ubuntu/Debian, swap `dnf` → `apt` and the user → `ubuntu`.

---

## 1. Architecture

```
            Internet (HTTPS :443)
                  │
            ┌─────▼─────┐   TLS termination + Let's Encrypt
            │   caddy   │   (auto-renewing cert in a volume)
            └─────┬─────┘
                  │ http :8080
            ┌─────▼─────┐   nginx: serves the React SPA,
            │    web    │   proxies /api and /hubs → api
            └─────┬─────┘
                  │ http :8080
            ┌─────▼─────┐   ASP.NET Core API.
            │    api    │   Runs EF migrations on boot.
            └─────┬─────┘
                  │ tcp :5432
            ┌─────▼─────┐   PostgreSQL 17.
            │ postgres  │   Data in the crm-pgdata volume.
            └───────────┘
```

Four containers, one Docker network (`crm-net`). Only Caddy is exposed to the
public internet. Persistent state lives in named volumes (`crm-pgdata`,
`crm-caddy-data`, `crm-api-logs`) that survive `docker compose down` and restarts.

---

## 2. How the CI/CD pipeline works

File: [`.github/workflows/deploy.yml`](.github/workflows/deploy.yml). Triggered on
every push to `main` (or manually from the Actions tab).

1. **Build** the API and Web Docker images (with a GitHub-hosted layer cache, so
   unchanged dependencies aren't rebuilt).
2. **Push** each image to Docker Hub with two tags: `:latest` and `:sha-<commit>`.
3. **Copy** `docker-compose.prod.yml` and `Caddyfile` to the server over SSH
   (these aren't baked into the images, so they ship separately).
4. **SSH** into the server and run `docker compose pull && up -d`, then prune old
   images to reclaim disk.

Database migrations are **not** a pipeline step — the API applies pending EF Core
migrations itself on startup (`Database.Migrate()` in `Program.cs`).

---

## 3. One-time server setup

### 3.1 Provision a server

Rent a Linux VPS with a public IP (EC2, Hetzner, DigitalOcean…). Note the IP.

### 3.2 Open firewall ports

In the cloud provider's security group / firewall, allow inbound:

| Port | Why |
|------|-----|
| 22   | SSH (you + the deploy pipeline) |
| 80   | HTTP — Let's Encrypt's ACME challenge needs it to issue the cert |
| 443  | HTTPS — actual app traffic (open UDP 443 too for HTTP/3) |

### 3.3 Install Docker + Compose

SSH in (`ssh -i your-key.pem ec2-user@YOUR_IP`) and run:

```bash
sudo dnf install -y docker
sudo systemctl enable --now docker
sudo usermod -aG docker ec2-user
```

Log out and back in so the group membership applies (lets you run `docker`
without `sudo`). The Compose plugin ships with the `docker` package on Amazon
Linux 2023 — verify:

```bash
docker compose version
```

If it's missing, install the plugin manually:

```bash
sudo mkdir -p /usr/local/lib/docker/cli-plugins
sudo curl -SL https://github.com/docker/compose/releases/latest/download/docker-compose-linux-x86_64 \
  -o /usr/local/lib/docker/cli-plugins/docker-compose
sudo chmod +x /usr/local/lib/docker/cli-plugins/docker-compose
```

### 3.4 Create the deploy directory + secrets

The pipeline copies the compose file into `/home/ec2-user/restaurant-crm`. Create
it and add a `.env` holding the secrets (this file is **never** committed or sent
through CI — it lives only on the server):

```bash
mkdir -p ~/restaurant-crm && cd ~/restaurant-crm

cat > .env <<EOF
POSTGRES_DB=RestaurantDB
POSTGRES_USER=postgres
POSTGRES_PASSWORD=$(openssl rand -base64 32)
JWT_SECRET=$(openssl rand -base64 48)
JWT_ISSUER=RestaurantCRM
JWT_AUDIENCE=RestaurantCRM
JWT_EXPIRY_HOURS=12
EOF

chmod 600 .env
```

> The compose file uses `${POSTGRES_PASSWORD:?...}` syntax — Compose **refuses to
> start** with a clear error if these are missing, so a typo can't silently boot
> an insecure DB. See [`.env.example`](.env.example) for the full key list.

### 3.5 Point your domain at the server

- **With a real domain:** create a DNS `A` record → your server IP. Then edit
  [`Caddyfile`](Caddyfile), replace `51.20.86.52.nip.io` with your domain, commit
  & push (CI re-syncs it). Caddy fetches the cert automatically on next start.
- **Without a domain (testing):** use `nip.io` — `<your-ip>.nip.io` resolves to
  your IP automatically. Put that in the `Caddyfile`. You still get a real
  HTTPS cert. The current value is `51.20.86.52.nip.io`; change the IP to yours.

### 3.6 Add GitHub Actions secrets

Repo → **Settings → Secrets and variables → Actions → New repository secret**:

| Secret | Value |
|--------|-------|
| `DOCKER_USERNAME` | Docker Hub username (`aronaa`) |
| `DOCKER_PASSWORD` | Docker Hub **access token** (Account Settings → Security → New Access Token — safer than your password) |
| `SERVER_HOST` | server public IP, e.g. `51.20.86.52` |
| `SERVER_SSH_KEY` | the **private** SSH key text (`-----BEGIN … KEY-----` … `-----END … KEY-----`) whose public half is in the server's `~/.ssh/authorized_keys` |

> If your server's SSH user isn't `ec2-user`, change it in `deploy.yml` (the
> `username:` fields and the `DEPLOY_DIR` path).

---

## 4. First deploy

Just push to `main`:

```bash
git push origin main
```

Watch it in the repo's **Actions** tab. On success, open `https://<your-host>`.
The first request may take a few seconds while Caddy obtains the TLS cert.

To trigger a deploy without a code change, use **Actions → Build and Deploy →
Run workflow** (the `workflow_dispatch` trigger).

---

## 5. Day-2 operations (run on the server)

```bash
cd ~/restaurant-crm

# Status + health of all containers
docker compose -f docker-compose.prod.yml ps

# Live logs (one service or all)
docker compose -f docker-compose.prod.yml logs -f api
docker compose -f docker-compose.prod.yml logs -f

# Restart one service
docker compose -f docker-compose.prod.yml restart caddy

# Stop everything (volumes/data preserved)
docker compose -f docker-compose.prod.yml down
```

### Roll back to a previous build

Every CI build is tagged `sha-<commit>`. Pin it in `.env` and re-up:

```bash
echo "IMAGE_TAG=sha-1a2b3c4" >> ~/restaurant-crm/.env   # use a real SHA tag
docker compose -f docker-compose.prod.yml up -d
```

Find available tags on Docker Hub, or from a commit's short SHA. Remove the
`IMAGE_TAG` line (or set it back to `latest`) to return to the newest build.

### Back up the database

```bash
docker exec crm-postgres pg_dump -U postgres RestaurantDB | gzip > backup-$(date +%F).sql.gz
```

Restore:

```bash
gunzip -c backup-2026-05-31.sql.gz | docker exec -i crm-postgres psql -U postgres -d RestaurantDB
```

---

## 6. Test the production build locally (optional)

Before pushing, you can run the full containerized stack on your Mac — same
images, minus Caddy/TLS — via [`docker-compose.yml`](docker-compose.yml):

```bash
cp .env.example .env   # fill POSTGRES_PASSWORD and JWT_SECRET
docker compose up --build
# → http://localhost:8080
```

---

## 7. Troubleshooting

| Symptom | Likely cause / fix |
|---------|--------------------|
| Actions fails at "Copy … to server" or "Deploy" | Bad `SERVER_HOST` / `SERVER_SSH_KEY`, or port 22 closed. Test `ssh -i key.pem ec2-user@IP` manually. |
| `docker compose` fails with `POSTGRES_PASSWORD … must be set` | `.env` missing or incomplete in `~/restaurant-crm`. See §3.4. |
| Site loads but `/api` calls 502 | API container unhealthy. `docker compose -f docker-compose.prod.yml logs api` — usually a DB connection or migration error. |
| HTTPS cert never issues | Port 80 closed (ACME needs it), or DNS not pointing at the server yet. Check `docker compose -f docker-compose.prod.yml logs caddy`. |
| Disk filling up | Old images. `docker image prune -f` (the pipeline already does this each deploy) and `docker system df` to inspect. |
| Changed `Caddyfile`/compose but nothing happened | Those sync on **push to main**. Re-push, or scp them manually and `restart`. |
