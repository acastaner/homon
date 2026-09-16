# Weather — module slot

**What it owns.** One widget on the dashboard: current conditions and a short forecast for
the household's location, which the administrator sets once.

**Shape.** No entities beyond a `WeatherSettings` singleton row (latitude, longitude,
optional place, units) and a short-lived server-side cache of the last provider answer, so
the SPA never talks to the provider directly and a page reload does not cost a provider
call.

**Entities and endpoints.**

- `WeatherSettings` — a singleton, always at a fixed id; see `WeatherSettings.SingletonId`.
- `GET/PUT/DELETE /weather/settings` — the administrator's admin-page CRUD for the location.
- `GET /weather` — the reader-facing cached forecast; `204` when unconfigured.

**Privacy.** Coordinates leave the server once, to Open-Meteo only, on a cache miss — never
from a reader's own browser, because nothing in the SPA is written to call the provider.

**Where the rest lands.** `Homon.Infrastructure/Weather/` (provider client behind an
`IWeatherProvider`, plus the cache), `Homon.Api/Endpoints/WeatherEndpoints.cs`. Provider:
Open-Meteo, which needs no API key — see `docs/MODULES.md`.
