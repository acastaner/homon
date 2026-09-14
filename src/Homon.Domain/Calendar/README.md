# Calendar — module slot

Not implemented yet. Plan 011.

**What it owns.** The family calendar widget: several sources (one per family member or
per shared calendar) merged into one view, each source with its own colour.

**Shape.** `CalendarSource` (Id, Name, Colour, Kind, Url, Credentials?) and a
server-side cache of fetched events. First phase reads public/private **ICS URLs**; CalDAV
is a later kind.

**Where the rest lands.** `Homon.Infrastructure/Calendar/` (fetchers behind an
`ICalendarSourceReader`), `Homon.Api/Endpoints/CalendarEndpoints.cs`, a `calendar` widget on
the dashboard. Credentials, when a source needs them, are encrypted the way probe secrets
are.
