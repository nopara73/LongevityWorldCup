# Section-Level Wow Pass

Artifacts for this pass live under `.artifacts/section-wow/`.

## Media Kit

- Route/state: `/media`, ZIP preview loaded.
- Viewports: desktop `1440x1024`, mobile `390x844`.
- Before screenshots:
  - `.artifacts/section-wow/before/media-kit__desktop.png`
  - `.artifacts/section-wow/before/media-kit__mobile.png`
- After screenshots:
  - `.artifacts/section-wow/after/media-kit__desktop.png`
  - `.artifacts/section-wow/after/media-kit__mobile.png`
- What felt weak: the section worked like a raw ZIP directory. File paths were the primary content, image assets had no preview, and the page did not feel like an official press room for a public competition.
- What changed: the page now opens with a dark press-room header, keeps the full ZIP CTA prominent, and turns the ZIP contents into grouped asset cards for story/facts, hero images, logos, team, and other files. Image files render real previews from the ZIP; non-image files use compact format markers.
- Why it fits better: the media kit now feels official, inspectable, and press-ready while preserving the site's dark scoreboard chrome, light paper content, direct copy, and functional download behavior.

## Events Feed

- Route/state: `/events`, public feed loaded.
- Viewport: mobile `390x844`.
- Before screenshot:
  - `.artifacts/section-wow/before/events-pulse__mobile.png`
- After screenshot:
  - `.artifacts/section-wow/after/events-pulse__mobile.png`
- What felt weak: the event rows were useful, but the page immediately became a long stream. There was no section-level orientation for the public race activity before users started scanning dense chronology.
- What changed: the full events route now gets a data-driven public race pulse above the table. It summarizes the latest feed window with current date, movement count, badge count, and new athlete count. Embedded homepage and athlete event boards stay lean.
- Why it fits better: the section now behaves like a live competition feed instead of a plain log, while staying honest to the same event data rendered in the table.

## Longevitymaxxing Challenge Board

- Route/state: `/longevitymaxxing`, public challenge board with no check-ins yet.
- Viewport: desktop `1280x720`.
- Before screenshot:
  - `.artifacts/section-wow/before/longevitymaxxing-board__desktop.png`
- After screenshot:
  - `.artifacts/section-wow/after/longevitymaxxing-board__desktop.png`
- What felt weak: the board had the right mechanics, but the header and explanatory copy felt like an admin grid. It did not fully express a public challenge scoreboard.
- What changed: the board header now uses the site's dark scoreboard surface, display typography, compact score legend, and clearer challenge-board language. The meta copy now explains consistency ranking and point ramping in product terms.
- Why it fits better: the section keeps the grid dense and functional, but it now reads as a public competition board aligned with Longevity World Cup's sport-science identity.
