# Weather — module slot

Not implemented yet. Plan 010.

**What it owns.** One widget on the dashboard: current conditions and a short forecast for
the household's location, which the administrator sets once.

**Shape.** No entities beyond a `WeatherSettings` row (latitude, longitude, units) and a
short-lived server-side cache of the last provider answer, so the SPA never talks to the
provider directly and a page reload does not cost a provider call.

**Where the rest lands.** `Homon.Infrastructure/Weather/` (provider client behind an
`IWeatherProvider`), `Homon.Api/Endpoints/WeatherEndpoints.cs`. Provider candidate:
Open-Meteo, which needs no API key — see `docs/MODULES.md`.
