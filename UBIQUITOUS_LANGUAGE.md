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
| **Clock rotation** | The practice of changing seasonal clocks so the competition cannot optimize around one static test forever. | Formula change, rules churn |
| **Multi-clock competition** | A competition model that keeps distinct clocks active for different tracks or seasons. | Mixed calculator setup |
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
| **Crowd Count** | The number of accepted realistic guesses behind an athlete's crowd age; repeated realistic guesses from the same client IP for the same athlete are accepted only once per short server-side window. | Vote count, guesses |
| **Crowd Age Difference** | The signed value `crowd age - chronological age`; lower and more negative values rank higher in crowd age ranking. | Crowd reduction, age gap |
| **Crowd Age leaderboard** | A separate leaderboard for athletes with at least 100 crowd guesses, ranked by Crowd Age Difference, Crowd Count, date of birth, and name. | Crowd league, Guess My Age ranking |
| **Improvement leaderboard** | A separate public leaderboard for athletes with at least two eligible pheno age submissions, ranked by Pheno Age Improvement, then pheno age reduction, date of birth, and name. It is available through filters and routes; supporting copy may call it pheno improvement. | Progress leaderboard, Best Improvement leaderboard |
| **Bortz Improvement leaderboard** | A separate public leaderboard for athletes with at least two eligible bortz age submissions, ranked by Bortz Age Improvement, then bortz age reduction, date of birth, and name. It is available through filters and routes, not the top-row leaderboard switcher. | Progress leaderboard, Best Improvement leaderboard |
| **Guess My Age** | The visitor game where people guess athlete ages and create crowd age data. | Crowd guessing, age guessing |
| **Badge** | A computed award attached to athletes for league placement, clock metrics, submissions, crowd metrics, or editorial status. | Award, achievement |
| **Age Reduction badge** | A top-three badge for ranking by the competition's age-reduction rules in a league scope. | League badge, ranking badge |
| **Domain badge** | A badge for the best clock subdomain score, such as liver, kidney, metabolic, immune, inflammation, or vitamin D. | Health-area badge |
| **Event** | A persisted public or social update such as joined, new rank, donation, milestone, badge award, custom event, season final result, or Longevitymaxxing Challenge result. | Update, notification |
| **Custom Event** | A manually designed event intended for the event board and social dispatch. | Announcement, post |
| **Social post** | A generated message for platforms such as X, Threads, Facebook, Slack, or future integrations. | Tweet, announcement |
| **Share preview** | The Open Graph or social-card representation of a page, athlete, or league link. | Thumbnail, card |
| **Athlete 1v1 challenge** | A proposed community-game format where athletes challenge each other and rankings emerge from matchups. | Matchup league, challenge league |
| **Lifestyle challenge** | A proposed side competition using lifestyle tracker or journal inputs such as sleep, nutrition, exercise, and vice tracking. | Habit challenge, side quest |
| **Longevitymaxxing Challenge** | A 14-day Lifestyle challenge where participants check in daily on sleep, exercise, nutrition, and vices to build momentum on a public visual leaderboard. The Day 1 check-in is practice: it counts for checked-in days, streak, completion, and other consistency signals, but not habit points, category leader badges, or point tie-breaks. | Habit tracker, wellness app |

## Money and public positioning

| Term | Definition | Aliases to avoid |
| --- | --- | --- |
| **Prize pool** | The Bitcoin-funded money reserved for top Ultimate League finishers. | Payout pot, donation goal |
| **Bitcoin donation** | A contribution that funds the prize pool and organizational costs. | Payment, sponsor money |
| **Sponsorship package** | A future monetization offer for companies that want visibility through the competition. | Sponsor tier, ad package |
| **Longevity sport** | The public framing of longevity practice as a measurable competition with seasons, athletes, and leaderboards. | Health app, biohacking site |
| **Biological age research** | Research into clocks, biomarkers, mortality-risk ordering, and datasets that can improve competition metrics. | Calculator work, science content |
| **Aging-related mortality** | Death risk from age-associated causes used when evaluating biological aging clocks. | All-cause death, health risk |
| **Mortality-risk ordering** | A model's ability to rank who is likely to die earlier from aging-related causes. | Prediction accuracy |
| **C-index** | The survival-analysis metric used in nopara73's clock research to evaluate mortality-risk ordering. | AUC, accuracy |
| **Ageless clock** | A biological aging clock that does not use chronological age as an input. | Age-free clock |

## Relationships

- A **Longevity athlete** belongs to zero or more **Leagues** through track, division, generation, exclusive league, or crowd eligibility.
- An **Application** can become one **Longevity athlete** after **Application review**.
- A **Result submission** contains one or more **Biomarker records** and must provide **Proof**.
- A **Biomarker record** can feed pheno age, bortz age, or both, depending on which required biomarkers are present.
- The **Amateur** all-time path is currently defined by pheno age; the **Pro** seasonal path is currently defined by bortz age.
- The **Ultimate League** ranks **Pro** athletes before **Amateur** athletes, then applies **Effective Age Reduction** and tie breakers.
- The crowd age leaderboard is not the **Ultimate League** and only includes athletes with at least 100 accepted guesses.
- The **Improvement leaderboard** is not the **Ultimate League**. It compares each athlete's latest eligible pheno age with their worst eligible pheno age and only includes athletes with at least two eligible pheno age submissions.
- The **Bortz Improvement leaderboard** is not the **Ultimate League**. It compares each athlete's latest eligible bortz age with their worst eligible bortz age and only includes athletes with at least two eligible bortz age submissions.
- **Guess My Age** applies server-side abuse protection before increasing **Crowd Count**: realistic guesses are accepted at most once per client IP and athlete during the configured short window.
- **Badges** are derived from leaderboard positions, clock metrics, submission behavior, crowd metrics, and editorial rules.
- **Events** announce athlete joins, rank changes, donations, milestones, badge awards, custom events, and season final results.
- **Longevitymaxxing Challenge** final results and linked-athlete completions can appear as **Events**, but they do not affect **Ultimate League** ranking, biological age placements, or athlete **Badges**.
- **Social posts** can be generated from **Events**, **Badges**, athlete rankings, and league context.
- The **Longevitymaxxing Challenge** is separate from the **Ultimate League** and does not affect biological age rankings, placements, or badges.
- A **Longevitymaxxing Challenge** participant can have a challenge-only **Profile picture** for challenge surfaces; it does not create or update an athlete profile picture.
- A challenge-only **Profile picture** can come from an uploaded challenge image or, if none exists, a cached Gravatar fallback found by email or public display-name profile slug; linked **Longevity athlete** profile pictures remain the display priority when present, and uploaded challenge images take priority over Gravatar.

## Example dialogue

> **Dev:** "If an athlete enters a Bortz result, do they move into the **Pro** track?"
>
> **Domain expert:** "Yes. Their **Effective Age Reduction** becomes their bortz age difference, and in the **Ultimate League** they rank ahead of **Amateur** athletes."
>
> **Dev:** "So a huge pheno age improvement cannot outrank a weak Pro result in the Ultimate League?"
>
> **Domain expert:** "Correct. **Pro** before **Amateur** is part of the domain rule, not a display choice."
>
> **Dev:** "For **Guess My Age**, should I reuse Ultimate League ordering?"
>
> **Domain expert:** "No. The crowd age leaderboard is separate. It needs at least 100 guesses and ranks by **Crowd Age Difference**, then **Crowd Count**, date of birth, and name."

## Flagged ambiguities

- **Age Reduction** is semantically positive in public copy but implemented as a signed difference where more negative values are better; use **Biological Age Difference** when discussing code or sort direction, and **Age Reduction** for user-facing competition copy.
- **Age Reduction** and **Effective Age Reduction** are title case as standalone labels, table headings, badge names, and schema terms. In running sentences, use lowercase **age reduction** and **effective age reduction** unless normal sentence-start capitalization applies.
- **Track** and **League** are often used interchangeably, but they are distinct: **Track** is Pro or Amateur, while **League** is a ranking view such as Ultimate, division, generation, exclusive, Amateur, or crowd age.
- **Pro** and **Professional** are both acceptable public names for the same track. Use whichever fits the immediate context best; do not treat either as a copy issue by itself.
- **Pheno Age**, **Bortz Age**, and **Crowd Age** are standalone public labels for chips, headings, tabs, table labels, badge labels, and other non-sentence UI. In running sentences, use lowercase **pheno age**, **bortz age**, and **crowd age**, except when normal sentence-start capitalization applies. If the clock name naturally starts a sentence, write **Pheno age**, **Bortz age**, or **Crowd age**; do not rewrite the sentence into "This pheno age..." or "The bortz age..." just to keep the term lowercase. Keep `PhenoAge`, `BortzAge`, and `CrowdAge` only for code identifiers, serialized fields, external names that require them, or quoted legacy data.
- **Clock**, **calculator**, and **result** should not be collapsed: the **clock** is the formula, the **calculator** is the UI/tool, and the **result** is an athlete's computed biological age.
- **Proof**, "proof picture", "report", and "screenshot" overlap in conversation; use **Proof** for the evidence asset and name the asset type only when validation rules depend on it.
- **Event** can mean a stored system update, a public timeline item, or a manually designed **Custom Event**; use **Custom Event** for admin-created announcements and **Event** for the persisted event model.
- **Ranking**, **rank**, and **placement** overlap; use **rank** for computed current order and **placement** for stored or historical finishing positions.
- **Crowd Age Difference** follows the same signed convention as biological age differences but is separate from Ultimate League ranking.
- **Pheno Age Improvement** and **Bortz Age Improvement** are distinct from the **Best Improvement** badge metric: improvement leaderboards use worst-to-latest biological age, while the badge metric uses baseline-to-latest for the same clock.
- **Bortz sex input** is unresolved in the issue tracker; avoid treating current bortz age output as definitively aligned with the original paper until that decision is settled.

## Source notes

- Local sources scanned: `README.md`, `AGENTS.md`, `LongevityWorldCup.Documentation/Ruleset.md`, `LongevityWorldCup.Documentation/AgingClocks.md`, athlete JSON, ranking, badge, event, application, clock-helper, league, and test code.
- Public sources checked: nopara73 Medium articles on launch, Season 2, point system updates, and biological aging clock research; search-indexed YouTube/podcast entries for Longevity World Cup interviews; search-indexed X/Twitter profile content; recent GitHub issues for core product, growth, athlete experience, community game, monetization, and admin workflow language.
