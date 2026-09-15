# Design files

The design pass's picture (plan 012). Documentation only: nothing here is imported by the
build. The rules the implementation follows — tokens, type, components, states — are
written out in `docs/design-brief.md` under "Design guidelines"; these artboards show them.

## Final design: Status board, dark by default

Chosen by the maintainer on 15 September 2026 and frozen for implementation.

| File | What it shows |
| --- | --- |
| `dashboard/Main.dc.html` | The dashboard at 1440px |
| `dashboard/BoardPhone.dc.html` | The dashboard at 412px |
| `dashboard/BoardEmpty.dc.html` | A fresh installation at 412px, every empty state |

The canvas they build is published at
<https://claude.ai/artifact/RGtus5ECBes1M6odeaYBDw> (first page "Status board").

The three directions not chosen (`Tiles*`, `Home*`, `Console*`) stay on the canvas's second
page as a record of what was compared. They are not a source for anything.

Where an artboard and the written guidelines disagree, the guidelines win; fix the artboard.

## Conventions inside an artboard

- An artboard is a self-contained HTML page in the Design Components format the Claude
  Design canvas renders. `canvas.json` holds the layout, pages and sticky notes.
- Both colour schemes are CSS custom properties on `.root`. The bare `.root` block is the
  default scheme and also carries the page background, text colour and font; the other
  block (`.root.light` or `.root.dark`) redefines variables only. The single `dark` tweak
  binds the root class, so the canvas can flip every artboard.
- Status is never colour alone: a glyph and the word sit beside every state.
- Sample data is generic (Internet, Storage, Media, ...). Uptime always has two decimals;
  `—` when there is nothing to compute.
- Fonts load from Google Fonts on the canvas only. The app self-hosts the same faces from
  Fontsource, because its CSP is `default-src 'self'`.

## Previewing without the canvas

A file opened directly renders its default scheme, because the `{{themeClass}}` hole stays
unresolved. Substitute the class to see the other one:

```bash
cd src/Homon.Web
sed 's/class="root {{themeClass}}"/class="root light"/' \
  ../../docs/design/dashboard/Main.dc.html > /tmp/main-light.html
npx playwright screenshot --browser=chromium --viewport-size=1440,900 --full-page \
  file:///tmp/main-light.html /tmp/main-light.png
```

## Rebuilding the canvas

The canvas is assembled from these files with the `design` skill in Claude Code
(`/design`). It seeds a fresh copy of its editor with every `*.dc.html` here plus
`canvas.json`, and republishes to the same artifact. Edit the files here, never the
published page; a save made in the canvas itself can be read back into a fresh directory
with the same skill.
