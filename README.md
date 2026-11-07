# Journey Planning API (ASP.NET Core)

ASP.NET Core 8 port of the Flask [JourneyPlanningAPI](https://github.com/bilitixi/JourneyPlanningAPI).
Same endpoints, JSON shapes, status codes, error messages, JWT format and MySQL schema, so existing
clients (and tokens issued by the Flask app, given the same `JWT_SECRET_KEY`) keep working.

## Run

```bash
cp .env.example .env          # fill in values
dotnet run --project src/JourneyPlanningApi --urls http://0.0.0.0:5000
```

Tables (`users`, `journeys`, `api_usage_logs`) are created on startup (`EnsureCreated`).
Set `SKIP_DB_INIT=true` to skip that.

| Variable | Purpose | Default |
|---|---|---|
| `MYSQL_URL` (or `DATABASE_URL`) | MySQL connection as a URL, e.g. `mysql://user:pass@host:3306/journey_planning?ssl-mode=Required`. Takes precedence over the variables below | – |
| `MYSQL_HOST` / `MYSQL_PORT` / `MYSQL_USER` / `MYSQL_PASSWORD` / `MYSQL_DATABASE` | MySQL connection | `localhost` / `3306` / `root` / empty / `journey_planning` |
| `MYSQL_SSL_MODE` | `None`, `Preferred`, `Required`, `VerifyCA`, `VerifyFull` (most hosted MySQL needs `Required`) | driver default (`Preferred`) |
| `PORT` | Port to listen on (set automatically by Render/Heroku) | `5000` in Docker |
| `JWT_SECRET_KEY` | HS256 signing key | `test-secret-key` (change it!) |
| `OPENWEATHER_API_KEY` | Weather forecast endpoint | required for `/api/weather` |
| `OPENROUTER_API_KEY` | AI recommendations (falls back to canned data if unavailable) | optional |

Docker: `docker build -t journey-api-net . && docker run -p 5000:5000 --env-file .env journey-api-net`

If the database can't be reached at startup, the app logs which host it tried and exits with code 1
(transient errors are retried first).

### Deploying to Render

The container doesn't include MySQL, so `localhost` won't work there. Render has no managed MySQL, so either
use an external MySQL (Aiven, TiDB Cloud, Railway, DigitalOcean, ...) or run MySQL as a Render private
service with a disk. Then, in the web service's **Environment** settings, set `MYSQL_URL` (or the
`MYSQL_*` variables), plus `MYSQL_SSL_MODE=Required` if your provider needs TLS, and `JWT_SECRET_KEY`.
Allow inbound connections from Render on the database side if your provider uses an IP allowlist.

## Endpoints

| Method | Path | Auth | Description |
|---|---|---|---|
| GET | `/` | – | `Hello World!` |
| POST | `/api/auth/register` | – | `first_name`, `last_name`, `email`, `password` → 201 `{message, user_id}` |
| POST | `/api/auth/login` | – | `email`, `password` → `{access_token, user}` (24h token) |
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

- `budget` is serialized as a JSON number (Flask's `jsonify` emitted the `Decimal` as a string such as `"2500.00"`).
- `PUT /api/journeys/{id}` also updates `notes` (Flask ignored it).
- `/api/admin/logs` is actually routed here; in Flask the admin blueprint was never registered. Nothing writes
  to `api_usage_logs` yet (Flask's `middleware/request_logger.py` was an empty file).
- `POST /api/recommendations/{id}` looks the journey up by id only, exactly like Flask, so any
  authenticated user can request recommendations for any journey. Restrict to the owner if that is not intended.
