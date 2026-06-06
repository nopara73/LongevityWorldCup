# Ubiquitous Language

## Competition structure

| Term | Definition | Aliases to avoid |
| --- | --- | --- |
| **Longevity World Cup** | An open competition where longevity athletes rank by improving biological age measures. | the site, the game |
| **Longevity athlete** | A participant who submits biological age data to compete. | User, player, applicant after approval |
| **Season** | A yearly competition window for clocks that accept only results from that season's valid dates. | Year, campaign |
| **Season close** | The point when final rankings for a season are locked. | Wrap-up, end date |
| **Track** | A competition class defined by clock access and seriousness, currently **Pro** or **Amateur**. | League, tier |
| **Pro** | The track for athletes with an eligible bortz age result, ranked ahead of Amateur in the Ultimate League. **Professional** refers to the same track and is also acceptable public wording. | Bortz-only |
| **Amateur** | The accessible track for athletes without an eligible bortz age result, currently centered on pheno age. | Beginner, Pheno-only |
| **Ultimate League** | The primary overall leaderboard that includes Pro and Amateur athletes while always ordering Pro before Amateur. | Overall ranking, global ranking |
| **League** | A filtered ranking view over the competition field, such as Ultimate, Amateur, division, generation, exclusive, or crowd age. | Track, category |
| **Division** | A self-declared athlete grouping of Men's, Women's, or Open. | Gender category, league |
| **Generation league** | A league derived from birth year bands such as Baby Boomers, Gen X, Millennials, Gen Z, or Gen Alpha. | Age group, cohort |
| **Exclusive league** | A special league assigned by an athlete field, currently including Prosperan. | Private league, sponsor league |
| **Field** | The set of eligible athletes considered for a rank preview, league, clock, or badge. | Pool, cohort |
| **Placement** | A stored historical or current finishing position for an athlete. | Rank, place |

## Scoring and clocks

| Term | Definition | Aliases to avoid |
| --- | --- | --- |
| **Chronological age** | An athlete's age in years computed from date of birth at the relevant date. | Real age, actual age |
| **Biological age** | The age-like output of a biological aging clock. | Bio age, calculated age |
| **Biological aging clock** | A formula or model that converts biomarkers and related inputs into biological age or age acceleration. | Calculator, test |
| **Pheno Age** | The all-time Amateur biological aging clock based on chronological age and nine blood biomarkers. | PhenoAge in public copy, Phenotypic Age, pheno |
| **Bortz Age** | The seasonal Pro biological aging clock based on chronological age and a larger blood-biomarker panel. | Bortz, Bortz Blood Age |
| **Seasonal clock** | A clock whose eligible results are limited to the active season's valid window. | Yearly clock |
| **All-time clock** | A clock whose best eligible result is selected across the athlete's full history. | Historical clock |
| **Biological Age Difference** | The signed value `biological age - chronological age`; lower and more negative values rank higher within the same ordering class. | Reduction, score |
| **Age Reduction** | The user-facing competition score that represents a favorable negative Biological Age Difference. | Age difference, years reversed |
| **Effective Age Reduction** | The ranking value used in the Ultimate League: bortz age difference for Pro athletes, otherwise pheno age difference. | Current score, active score |
| **Lowest Pheno Age** | An athlete's best, lowest eligible pheno age result. | Best Pheno, Pheno score |
| **Lowest Bortz Age** | An athlete's best, lowest eligible bortz age result in the seasonal window. | Best Bortz, Bortz score |
| **Biological Age Acceleration** | The Bortz model's intermediate signed age offset before it is added to chronological age. | BAA, acceleration |
| **Pace of Aging** | A badge metric that compares biological age movement over time for a clock. | Rate of aging |
| **Best Improvement** | A badge metric based on the latest eligible result minus the baseline result for the same clock. | Progress, improvement |
| **Pheno Age Improvement** | The signed value `latest eligible pheno age - worst eligible pheno age`; lower and more negative values rank higher in the Improvement leaderboard. | Progress, Best Improvement |
| **Bortz Age Improvement** | The signed value `latest eligible bortz age - worst eligible bortz age`; lower and more negative values rank higher in the Bortz Improvement leaderboard. | Progress, Best Improvement |
| **Hypothetical rank** | A preview showing where a calculated result would rank if entered into the current field. | Rank preview, projected rank |

## Submissions and evidence

| Term | Definition | Aliases to avoid |
| --- | --- | --- |
| **Application** | The onboarding package submitted by a prospective athlete for review. | Registration, signup |
| **Applicant** | A person whose application has not yet become an athlete entry. | Athlete, user |
| **Result submission** | A new or updated set of clock-relevant measurements submitted by an athlete. | Application, test |
| **Biomarker record** | One dated set of biomarker values from a blood draw or report. | Biomarkers, lab result |
| **Biomarker** | A measured biological value used by one or more clocks. | Marker, metric |
| **Same-day submission** | A clock submission whose required biomarkers all come from the same blood draw or report date. | Complete result |
| **Partial submission** | A submission missing clock-required biomarkers or mixing dates; this is not eligible for ranking. | Incomplete result |
| **Proof** | Evidence asset supporting an athlete, profile, or result submission, usually an uploaded report or image. | Proof pic, report, screenshot |
| **Profile picture** | The public image representing the athlete on profiles, leaderboards, and share cards. | Avatar, character image |
| **Media contact** | A public contact URL or handle used for social mentions and athlete outreach. | Social link, X handle |
| **Application review** | The admin workflow that checks proofs, validates data, and replies to the applicant. | Verification, moderation |

## Leaderboards, badges, and community game

| Term | Definition | Aliases to avoid |
| --- | --- | --- |
| **Leaderboard** | A ranked display of athletes for a league, track, clock, or crowd view. | Ranking list, table |
| **Crowd Age** | The median realistic age guessed by visitors in Guess My Age. | Guessed age, perceived age |
| **Crowd Count** | The number of accepted realistic guesses behind an athlete's crowd age. | Vote count, guesses |
| **Crowd Age Difference** | The signed value `crowd age - chronological age`; lower and more negative values rank higher in crowd age ranking. | Crowd reduction, age gap |
| **Crowd Age leaderboard** | A separate leaderboard for athletes with enough accepted crowd guesses. | Crowd league, Guess My Age ranking |
| **Improvement leaderboard** | A separate public leaderboard for athletes with enough eligible pheno age submissions. Supporting copy may call it pheno improvement. | Progress leaderboard, Best Improvement leaderboard |
| **Bortz Improvement leaderboard** | A separate public leaderboard for athletes with enough eligible bortz age submissions. | Progress leaderboard, Best Improvement leaderboard |
| **Guess My Age** | The visitor game where people guess athlete ages and create crowd age data. | Crowd guessing, age guessing |
| **Badge** | A computed award attached to athletes for league placement, clock metrics, submissions, crowd metrics, or editorial status. | Award, achievement |
| **Age Reduction badge** | A top-three badge for ranking by the competition's age-reduction rules in a league scope. | League badge, ranking badge |
| **Domain badge** | A badge for the best clock subdomain score, such as liver, kidney, metabolic, immune, inflammation, or vitamin D. | Health-area badge |
| **Event** | A persisted public or social update such as joined, new rank, donation, milestone, badge award, custom event, season final result, or Longevitymaxxing Challenge result. | Update, notification |
| **Custom Event** | A manually designed event intended for the event board and social dispatch. | Announcement, post |
| **Social post** | A generated message for platforms such as X, Threads, Facebook, Slack, or future integrations. | Tweet, announcement |
| **Share preview** | The Open Graph or social-card representation of a page, athlete, or league link. | Thumbnail, card |
| **Lifestyle challenge** | A proposed side competition using lifestyle tracker or journal inputs such as sleep, nutrition, exercise, and vice tracking. | Habit challenge, side quest |
| **Longevitymaxxing Challenge** | A 14-day Lifestyle challenge where participants check in daily on sleep, exercise, nutrition, and vices on a public visual leaderboard. | Habit tracker, wellness app |

## Domain rule notes

- **Ultimate League** ranks **Pro** athletes before **Amateur** athletes, then applies **Effective Age Reduction** and tie breakers.
- **Crowd Age leaderboard** starts at 100 accepted guesses and orders by **Crowd Age Difference**, **Crowd Count**, date of birth, and name.
- **Guess My Age** applies server-side abuse protection before increasing **Crowd Count**: realistic guesses are accepted at most once per client IP and athlete during the configured short window.
- Improvement leaderboards require at least two eligible submissions and are separate from the **Ultimate League**. Pheno orders by **Pheno Age Improvement**, then pheno age reduction, date of birth, and name; Bortz uses the Bortz equivalents and is route/filter-only.
- **Longevitymaxxing Challenge** Day 1 counts for checked-in days, streak, completion, and consistency signals, but not habit points, category leader badges, or point tie-breaks.
- **Longevitymaxxing Challenge** call times may be selected before signup closes for 24-hour reminders.
- **Longevitymaxxing Challenge** final results and linked-athlete completions can appear as **Events**, but do not affect **Ultimate League** ranking, biological age placements, or athlete **Badges**.
- **Longevitymaxxing Challenge** profile pictures are challenge-only unless a linked **Longevity athlete** profile picture exists. Uploaded challenge images take priority over cached Gravatar fallbacks.

## Naming notes

- Use **Biological Age Difference** for signed implementation values and **Age Reduction** for user-facing favorable reductions.
- Use lowercase pheno age, bortz age, crowd age, age reduction, and effective age reduction in running sentences unless normal capitalization applies. Keep `PhenoAge`, `BortzAge`, and `CrowdAge` for code identifiers, serialized fields, external names, or quoted legacy data.
- Keep **Track** and **League** distinct: Track is Pro or Amateur; League is a ranking view.
- Do not collapse **clock**, **calculator**, and **result**; use **rank** for computed order and **placement** for stored or historical positions.
- **Pheno Age Improvement** and **Bortz Age Improvement** are worst-to-latest leaderboard metrics; **Best Improvement** is a baseline-to-latest badge metric.
- **Bortz sex input** is unresolved in the issue tracker; avoid treating current bortz age output as definitively aligned with the original paper until that decision is settled.
