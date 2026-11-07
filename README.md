# Journey Planning API (ASP.NET Core)

ASP.NET Core 8 port of the Flask [JourneyPlanningAPI](https://github.com/bilitixi/JourneyPlanningAPI).
Same endpoints, JSON shapes, status codes, error messages, JWT format and MySQL schema, so existing
clients (and tokens issued by the Flask app, given the same `JWT_SECRET_KEY`) keep working.

## Run

```bash
cp .env.example .env          # fill in values
dotnet run --project src/JourneyPlanningApi --urls http://0.0.0.0:5000
```

Tables (`users`, `journeys`, `api_usage_logs`) are created on startup (`EnsureCreated`). Columns added later (email
verification / password reset) are added to an existing `users` table on startup; accounts that existed before
that are marked as verified.
Set `SKIP_DB_INIT=true` to skip that.

| Variable | Purpose | Default |
|---|---|---|
| `DATABASE_URL` (or `MYSQL_URL`) | Database as a URL. `postgresql://user:pass@host:5432/db` uses PostgreSQL (e.g. a Render database); `mysql://user:pass@host:3306/journey_planning?ssl-mode=Required` uses MySQL. Takes precedence over the variables below | – |
| `MYSQL_HOST` / `MYSQL_PORT` / `MYSQL_USER` / `MYSQL_PASSWORD` / `MYSQL_DATABASE` | MySQL connection | `localhost` / `3306` / `root` / empty / `journey_planning` |
| `MYSQL_SSL_MODE` | `None`, `Preferred`, `Required`, `VerifyCA`, `VerifyFull` (most hosted MySQL needs `Required`) | driver default (`Preferred`) |
| `CORS_ORIGINS` | Comma-separated origins allowed to call the API from a browser. `localhost` = localhost/127.0.0.1 on any port, `*` = any origin | `https://journeyplanningapp.bilitixi.org,https://journeyplanninguidraft.vercel.app,localhost` |
| `PORT` | Port to listen on (set automatically by Render/Heroku) | `5000` in Docker |
| `JWT_SECRET_KEY` | HS256 signing key | `test-secret-key` (change it!) |
| `OPENWEATHER_API_KEY` | Weather forecast endpoint | required for `/api/weather` |
| `OPENROUTER_API_KEY` | AI recommendations (falls back to canned data if unavailable) | optional |
| `MAIL_SERVER` / `MAIL_PORT` / `MAIL_USE_TLS` | SMTP server for verification and reset emails (STARTTLS) | `smtp.gmail.com` / `587` / `true` |
| `MAIL_USERNAME` / `MAIL_PASSWORD` | SMTP login (for Gmail: your address and an [app password](https://myaccount.google.com/apppasswords)) | – |
| `MAIL_DEFAULT_SENDER` | From address | `MAIL_USERNAME` |
| `MAILGUN_API_KEY` / `MAILGUN_DOMAIN` | Send email through Mailgun's HTTPS API instead of SMTP (as the Flask app did before switching to SMTP). From address: `postmaster@MAILGUN_DOMAIN`. **Use this on Render**, which blocks outbound SMTP | – (SMTP is used) |
| `MAILGUN_API_URL` | Mailgun region: `https://api.eu.mailgun.net` for EU accounts | `https://api.mailgun.net` |
| `RESEND_API_KEY` / `RESEND_FROM` | Alternative HTTPS provider (Resend), used when Mailgun isn't configured | – / `onboarding@resend.dev` |
| `FRONTEND_URL` | Base URL for the links in emails (`/verify-email?token=`, `/reset-password?token=`) | `http://localhost:3000` |

Docker: `docker build -t journey-api-net . && docker run -p 5000:5000 --env-file .env journey-api-net`

If the database can't be reached at startup, the app logs which host it tried and exits with code 1
(transient errors are retried first).

### Deploying to Render

The container doesn't include a database, so `localhost` won't work there. Either:

- **Render PostgreSQL (simplest):** create a PostgreSQL database in Render, then in the web service's
  **Environment** add `DATABASE_URL` set to the database's *Internal Database URL* (or use "Add from database").
  The app picks PostgreSQL automatically from the `postgresql://` scheme and creates the tables on startup.
- **External MySQL** (Aiven, TiDB Cloud, Railway, ...): set `MYSQL_URL` or the `MYSQL_*` variables, plus
  `MYSQL_SSL_MODE=Required` if the provider needs TLS.

Also set `JWT_SECRET_KEY`, `FRONTEND_URL` (e.g. `https://journeyplanningapp.bilitixi.org`) and
`MAILGUN_API_KEY` + `MAILGUN_DOMAIN` so verification and reset emails can be sent. Render can't reach SMTP servers
(sending via `smtp.gmail.com` fails with "Network is unreachable"), so the `MAIL_*` SMTP settings only work
elsewhere. When an email can't be sent the request fails within 20 seconds and can be retried straight away. Render *Secret Files* are mounted at `/etc/secrets/`, where the app does not look for
`.env`, so use environment variables.

Email lookups (register, login, forgot password, ...) ignore case on every database, so `John@Example.com` and
`john@example.com` are the same account. The `email` column is a plain `varchar`, so databases created by the Flask
app work as they are; no PostgreSQL extensions are needed.

## Endpoints

| Method | Path | Auth | Description |
|---|---|---|---|
| GET | `/` | – | `Hello World!` |
| POST | `/api/auth/register` | – | `first_name`, `last_name`, `email`, `password` → 201 `{message, user_id}`; emails a verification link |
| POST | `/api/auth/login` | – | `email`, `password` → `{access_token, user}` (24h token); 403 until the email is verified |
| POST | `/api/auth/verify-email` | – | `token` from the verification email |
| POST | `/api/auth/resend-verification` | – | `email`; 429 while the previous link is still valid |
| POST | `/api/auth/forgot-password` | – | `email`; emails a reset link (429 while the previous one is still valid) |
| POST | `/api/auth/reset-password` | – | `token`, `new_password` |
| PUT | `/api/auth/me` | Bearer | Update own profile; `current_password` required to change email/password |
| DELETE | `/api/auth/me` | Bearer | Delete own account + journeys (`current_password` required) |
| GET | `/api/journeys` | Bearer | List own journeys → `{journeys: [...]}` |
| GET | `/api/journeys/{id}` | Bearer | Get own journey |
| POST | `/api/journeys` | Bearer | `destination`, `start_date`, `end_date` (`YYYY-MM-DD`), `budget`, `people`, optional `notes` |
| PUT | `/api/journeys/{id}` | Bearer | Partial update |
| DELETE | `/api/journeys/{id}` | Bearer | Delete |
| GET | `/api/weather/{id}` | Bearer | Daily forecast via OpenWeatherMap for the journey's dates |
| POST | `/api/recommendations/{id}` | Bearer | AI travel recommendations (OpenRouter) with fallback |
| GET | `/api/admin/logs` | Admin | API usage logs, newest first |

Errors are `{"error": "..."}` with 400/401/403/404/500/504 as in the Flask app.

## Tests

```bash
dotnet test
```

Integration tests run the full HTTP pipeline against in-memory SQLite with stubbed OpenWeather/OpenRouter calls.

## Differences from the Flask app

- Ported from the Flask `deploy` branch: PostgreSQL via `DATABASE_URL`, email verification (login blocked until
  verified) and password reset over SMTP, with the same messages, status codes and 15-minute links.

- CORS only allows the origins in `CORS_ORIGINS` (Flask allowed any origin). Set `CORS_ORIGINS=*` to restore that.

- `budget` is serialized as a JSON number (Flask's `jsonify` emitted the `Decimal` as a string such as `"2500.00"`).
- `PUT /api/journeys/{id}` also updates `notes` (Flask ignored it).
- `/api/admin/logs` is actually routed here; in Flask the admin blueprint was never registered. Nothing writes
  to `api_usage_logs` yet (Flask's `middleware/request_logger.py` was an empty file).
- `POST /api/recommendations/{id}` only works for the caller's own journeys (404 otherwise). Flask looked the
  journey up by id only, so any logged-in user could get recommendations (and the destination) for any journey.
