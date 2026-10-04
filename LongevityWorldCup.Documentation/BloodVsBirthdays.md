# Blood vs. Birthdays

The game is reachable at `/blood-vs-birthdays`. Its initial release is an unannounced review page: it has `noindex, nofollow` metadata and an `X-Robots-Tag` header, and is absent from navigation, the sitemap, IndexNow page discovery, and the public API catalog. Do not announce or add discovery links until the user approves the finished game.

`GET /api/blood-vs-birthdays` returns today's five matchups, the server's UTC time, and the next Singapore-midnight boundary. It uses `no-store`. Athlete scores come from the existing `PhenoStatsCalculator` and only eligible, non-future bortz results with public proofs enter the set. See the glossary for the scoring rule.

The service writes one atomic JSON snapshot per Singapore calendar day to the application data directory's `blood-vs-birthdays/yyyy-MM-dd.json`. On production this is `/var/www/.longevityworldcup/blood-vs-birthdays/`, outside the release tree. Preserve it during deploys and backups. Existing snapshots are authoritative through source edits and restarts; corrupt or incompatible files return a recoverable 503 rather than silently replacing players' questions. The current schema version is 1.

Browser storage under `lwc.blood-vs-birthdays.v1` holds answers, the chosen timer mode, absolute timer deadlines, and completed dates. Keep at most 90 daily entries. Reloading preserves the current reveal or unfinished round; a timed round's deadline continues through reloads. Storage failures show an in-page notice and allow the open game to finish. A new day's response offers an explicit switch while an older game is open, preserving that game's answers.

The Copy score control only copies a player's score and game URL. The game does not create Events or enqueue social posts. Automated maintenance needs no new scheduled job: the first request after Singapore midnight creates that day's saved set.

`BloodVsBirthdaysTests` covers the canonical scoring, date boundary, stable selection, durable snapshots, and candidate eligibility. `BloodVsBirthdaysBrowserTests` covers the full flow, keyboard/mobile play, saved progress, timer expiry, rollover, recovery, and absence from discovery surfaces.
