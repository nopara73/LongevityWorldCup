# nopara73 / Adam Ficsor written texts

Generated: 2026-07-05T12:40:45.625531+00:00

Public/discoverable written artifacts by nopara73 / Adam Ficsor that are connected to Longevity World Cup, longevity as a sport/game, Rejuvenation Olympics, biological-age clocks/ranking, LWC/RO athletes, or closely related longevity-competition discussion.

Records: 168

## 1. PhenoAge + Bortz Calculation Bug Disclosure: Missing U/J-Shaped Curves for Biomarkers

- Source: GitHub issue
- Link: https://github.com/nopara73/LongevityWorldCup/issues/136
- Date: 2025-01-02T07:22:24Z
- Notes: https://api.github.com/repos/nopara73/LongevityWorldCup

```text
With Dave Pascoe we’ve discovered that the PhenoAge + Bortz calculation method in the Longevity World Cup repository – as well as all existing online PhenoAge and Bortz calculators – seems to have a critical oversight. Specifically, the PhenoAge biomarkers (_albumin, creatinine, glucose, C-reactive protein, lymphocyte percentage, mean cell volume, red cell distribution width, white blood cell count, and alkaline phosphatase_) follow a U-shaped curve in reality (values that are either too high or too low are both associated with increased mortality), but the current linear regression model rewards pushing these values to unrealistic extremes. 

For instance, the algorithm will yield impossibly large negative ages if albumin is inflated to 100 g/dL or if glucose is taken down to extremely low, unhealthy levels. These “optimizations” don’t reflect better health but rather an artifact of the underlying linear model. 

Below is a snippet from our discussion:

> “Glucose, for example, would be near perfect biologically at 80, but PhenoAge will reward much more unhealthy people (e.g., glucose of 60, 40, or even 20) with much younger PhenoAges! That’s a big problem!”  
> — Dave Pascoe

We see similar distortions with albumin, C-reactive protein, and likely other markers. Simply capping values at a plausible upper or lower limit won’t solve it either, because some biomarkers legitimately have a U-shape (like glucose), and limiting them linearly doesn't capture the genuine mortality curve.  

**Question:**

How might we fix or improve the PhenoAge calculations to respect actual physiological realities (e.g., U-shaped relationships) rather than purely linear extrapolations? 

![image](https://github.com/user-attachments/assets/cd58c984-09a7-4024-9651-f879d90a403c)

Some ideas to consider:

1. **Clamp Out-of-Range Values:** Implement quick `min()` and `max()` constraints for each biomarker so extreme values won’t skew results. For example, cap albumin at 5.0 g/dL and glucose at 250 mg/dL to prevent nonsensical outputs.
2. **Add Simple Penalties for Out-of-Bounds Values:** If a biomarker is below or above a known physiologically healthy range, add a penalty to push the result toward typical U-shaped mortality curves. This can be a single conditional block per biomarker.
3. **Use a Two-Point Slope Adjustment:** For clearly U-shaped markers, define two slopes: one for the “low range” and one for the “high range.” This is still a quick linear approach but mimics a U-shape without a full-blown polynomial or spline.
4. **Piecewise Functions or Non-Linear Models:** Implement piecewise functions or spline-based approaches to model truly U-shaped biomarker relationships.
5. **Physiological Constraints:** Set upper and lower limits based on best-known population studies, ensuring that values beyond these bounds do not produce unrealistic results.

Let me know your thoughts and any other potential strategies!
```

## 2. PhenoAge Calculation Bug Disclosure: Missing U-Shaped Curves for Biomarkers

- Source: GitHub issue
- Link: https://github.com/ajsteele/bioage/issues/2
- Date: 2025-01-02T07:27:03Z
- Notes: https://api.github.com/repos/ajsteele/bioage

```text
It seems pheno age has a quite uncomfortable flaw. It's not a bug in your code, since the original Morgan Levine spreadsheet has this issue as well (and I suppose it comes from the paper itself.)

https://github.com/nopara73/LongevityWorldCup/issues/136
```

## 3. 0.090165 vs 0.09165

- Source: GitHub issue
- Link: https://github.com/ajsteele/bioage/issues/3
- Date: 2025-03-26T08:40:46Z
- Notes: https://api.github.com/repos/ajsteele/bioage

```text
[Michael Lustgarten](https://michaellustgarten.com/) notified me about a discrepancy in the [Longevity World Cup](http://longevityworldcup.com/)'s and his own pheno age calculation.

It seems your algorithm uses `0.090165` where a [later released correction](https://journals.plos.org/plosmedicine/article?id=10.1371/journal.pmed.1002760) to the pheno age formula uses `0.09165`:

https://github.com/ajsteele/bioage/blob/2675db11bafaa378dfd99ddea35cdc8a06570820/phenoage.js#L123

I wonder which is it?
```

## 4. PhenoAge Calculation Bug Disclosure: Missing U-Shaped Curves for Biomarkers - comment 2567370846

- Source: GitHub issue comment
- Link: https://github.com/ajsteele/bioage/issues/2#issuecomment-2567370846
- Date: 2025-01-02T07:27:39Z
- Notes: https://github.com/ajsteele/bioage/issues/2

```text
I'll try to remember to create a PR here as well if I decide on a fix.
```

## 5. PhenoAge + Bortz Calculation Bug Disclosure: Missing U/J-Shaped Curves for Biomarkers - comment 2591459073

- Source: GitHub issue comment
- Link: https://github.com/nopara73/LongevityWorldCup/issues/136#issuecomment-2591459073
- Date: 2025-01-15T01:43:09Z
- Notes: https://github.com/nopara73/LongevityWorldCup/issues/136

```text
I'm stuck on this issue. No solution really seems to be worth it.

1. If I do quick and dirty with a clean cutoff to correct for obvious problems at the end, it's very subjective without a proper research what score people should get. How fast am I worsening it? And it barely even solves the issue, just handles things that won't really happen.
2. A fairly correct result could be if I try to be more sophisticated and let's say I cut it off at the best possible value, the same subjectivity issue arises, but much worse because now people are expected to fall into these scores so it'd have real world consequences.

I guess sticking with the pheno age model, albeit it being flawed, might be the least bad solution?
```

## 6. PhenoAge Calculation Bug Disclosure: Missing U-Shaped Curves for Biomarkers - comment 2591461157

- Source: GitHub issue comment
- Link: https://github.com/ajsteele/bioage/issues/2#issuecomment-2591461157
- Date: 2025-01-15T01:45:26Z
- Notes: https://github.com/ajsteele/bioage/issues/2

```text
I don't think I'm gonna be able to fix this without doing a proper research on it. The best I could do is to rely on ChatGPT's intuitions, but that'd be very unscientific, worse probably even flawed.
https://github.com/nopara73/LongevityWorldCup/issues/136#issuecomment-2591459073
```

## 7. PhenoAge + Bortz Calculation Bug Disclosure: Missing U/J-Shaped Curves for Biomarkers - comment 2631388145

- Source: GitHub issue comment
- Link: https://github.com/nopara73/LongevityWorldCup/issues/136#issuecomment-2631388145
- Date: 2025-02-03T15:50:23Z
- Notes: https://github.com/nopara73/LongevityWorldCup/issues/136

```text
I'm gonna stop the improvement of the results at the best possible physiological values. Here I'm attempting to figure out what those should be. A quick and dirty AI questionnaire resulted in the following table:

| Model         | Albumin (g/L) | Creatinine (µmol/L) | Glucose (mmol/L) | C-Reactive Protein (mg/L) | Lymphocytes (%) | Mean Corpuscular Volume (fL) | Red Cell Distribution Width (%) | Alkaline Phosphatase (U/L) | White Blood Cell Count (1000 cells/μL) |
|--------------|--------------|---------------------|------------------|---------------------------|----------------|------------------------------|-------------------------------|--------------------------|------------------------------------|
| 4o          | 45.0         | 80                  | 4.8              | 0.0                        | 40             | 90                           | 12                            | 70                        | 6.50                               |
| o1          | 40.0         | 80                  | 5.0              | 1.0                        | 30             | 90                           | 13                            | 80                        | 7.00                               |
| o3-mini-high | 40.0         | 80                  | 5.0              | 1.0                        | 30             | 90                           | 13                            | 80                        | 7.00                               |
| deepseek    | 40.0         | 80                  | 5.0              | 3.0                        | 30             | 90                           | 12                            | 70                        | 7.00                               |
| claude      | 42.5         | 90                  | 5.0              | 2.5                        | 30             | 90                           | 13                            | 75                        | 7.75                               |

At this time o1 and o3-mini-high are considered to be the most advanced models. Coincidentally they are even in perfect agreement regarding the results, which is convenient, so I'll take their numbers as caps.
```

## 8. PhenoAge + Bortz Calculation Bug Disclosure: Missing U/J-Shaped Curves for Biomarkers - comment 2631552547

- Source: GitHub issue comment
- Link: https://github.com/nopara73/LongevityWorldCup/issues/136#issuecomment-2631552547
- Date: 2025-02-03T16:55:24Z
- Notes: https://github.com/nopara73/LongevityWorldCup/issues/136

```text
Or maybe I should go with reference ranges instead of best possible values? The reference ranges are less contentious than ideal values are. Although that'd have less optimal results, yet they are less debatable and stays more on the safe side.
```

## 9. PhenoAge + Bortz Calculation Bug Disclosure: Missing U/J-Shaped Curves for Biomarkers - comment 2631605086

- Source: GitHub issue comment
- Link: https://github.com/nopara73/LongevityWorldCup/issues/136#issuecomment-2631605086
- Date: 2025-02-03T17:17:29Z
- Notes: https://github.com/nopara73/LongevityWorldCup/issues/136

```text
Or maybe I can have the best of both wordls: I can go with ideal ranges instead of typical reference ranges?
```

## 10. PhenoAge + Bortz Calculation Bug Disclosure: Missing U/J-Shaped Curves for Biomarkers - comment 2631646628

- Source: GitHub issue comment
- Link: https://github.com/nopara73/LongevityWorldCup/issues/136#issuecomment-2631646628
- Date: 2025-02-03T17:36:32Z
- Notes: https://github.com/nopara73/LongevityWorldCup/issues/136

```text
Ok, this is what's happening. 

For age and crp there are no known lower caps, so not capping them.

For the rest based on o1 and o3 again, falling on the safer side when they disagree:

Albumin: Upper cap: 50 g/L
Creatinine: Lower cap: 60 µmol/L
Glucose: Lower cap: 4.0 mmol/L
White Blood Cell Count (WBC): Lower cap: 4.5 × 1000 cells/µL
Lymphocytes: Upper cap: 40 %
Mean Corpuscular Volume (MCV): Lower cap: 85 fL
Red Cell Distribution Width (RDW): Lower cap: 11.5 %
Alkaline Phosphatase (AP): Lower cap: 50 U/L
```

## 11. PhenoAge + Bortz Calculation Bug Disclosure: Missing U/J-Shaped Curves for Biomarkers - comment 2631696387

- Source: GitHub issue comment
- Link: https://github.com/nopara73/LongevityWorldCup/issues/136#issuecomment-2631696387
- Date: 2025-02-03T18:00:18Z
- Notes: https://github.com/nopara73/LongevityWorldCup/issues/136

```text
https://github.com/nopara73/LongevityWorldCup/commit/97dbdf8d37d53e95830d64e5f59902d8a61bc7ec
```

## 12. PhenoAge Calculation Bug Disclosure: Missing U-Shaped Curves for Biomarkers - comment 2631731131

- Source: GitHub issue comment
- Link: https://github.com/ajsteele/bioage/issues/2#issuecomment-2631731131
- Date: 2025-02-03T18:16:54Z
- Notes: https://github.com/ajsteele/bioage/issues/2

```text
Ultimately I decided to cap the unreasonable ends of the ranges: https://github.com/nopara73/LongevityWorldCup/issues/136#issuecomment-2631646628
```

## 13. PhenoAge + Bortz Calculation Bug Disclosure: Missing U/J-Shaped Curves for Biomarkers - comment 2707015360

- Source: GitHub issue comment
- Link: https://github.com/nopara73/LongevityWorldCup/issues/136#issuecomment-2707015360
- Date: 2025-03-07T17:29:00Z
- Notes: https://github.com/nopara73/LongevityWorldCup/issues/136

```text
Cap research  by [Michael Lustgarten](https://michaellustgarten.com/)

## Summary

Albumin: No upper cap
Creatinine: Lower cap: 44 µmol/L
Glucose: Lower cap: 4.44 mmol/L
CRP: No lower cap
White Blood Cell Count (WBC): Lower cap: 3.5 × 1000 cells/µL
Lymphocytes: Upper cap: 60 %
Mean Corpuscular Volume (MCV): No lower cap
Red Cell Distribution Width (RDW): Lower cap: 11.4 %
Alkaline Phosphatase (AP): No lower cap

![Image](https://github.com/user-attachments/assets/3e330762-60d0-4064-a460-15e33f9a3cc0)

![Image](https://github.com/user-attachments/assets/98e6d60e-4321-4e62-928a-4537d6bd8ac6)

![Image](https://github.com/user-attachments/assets/30a677e4-6b7b-4343-b9a2-d6570602bc4f)

![Image](https://github.com/user-attachments/assets/35060ec3-8d24-4bee-9d86-c744913646cd)

![Image](https://github.com/user-attachments/assets/46875edc-3b33-488d-8a9b-3387639ecb75)

![Image](https://github.com/user-attachments/assets/9c6c85fd-8cd4-4d14-b717-5c017a601728)

![Image](https://github.com/user-attachments/assets/26fe2ddc-c2dc-4259-846e-34f903e8b15c)
```

## 14. 0.090165 vs 0.09165 - comment 2753701419

- Source: GitHub issue comment
- Link: https://github.com/ajsteele/bioage/issues/3#issuecomment-2753701419
- Date: 2025-03-26T09:17:22Z
- Notes: https://github.com/ajsteele/bioage/issues/3

```text
# Issue: Discrepancy in PhenoAge Constant: 0.09165 vs 0.090165

## Description
There is a discrepancy between the constant used in the original PhenoAge algorithm and the one stated in the 2019 correction. The original publication used a constant of **0.090165**, while the correction published in PLOS Medicine specifies **0.09165**. Some implementations and subsequent studies continue to use the original value.

## Potential Issue
There is a possibility that the original authors might have inadvertently introduced an error in the correction itself. While the 2019 erratum indicates that **0.09165** is correct, further clarification from the original research team is advisable to conclusively resolve the discrepancy.

## Conclusion
- **Original Constant:** 0.090165 (as per the 2018 publication)
- **Corrected Constant:** 0.09165 (as per the 2019 correction)
- **Current Implementations:** Mixed usage is observed; some continue with 0.090165 while others have adopted 0.09165.
- **Action Needed:** Confirmation from the original authors is required to ensure consistency and correctness in future implementations.

Please review these findings and advise on which constant should be standardized in our implementation.

## References
- 2019 Correction: https://journals.plos.org/plosmedicine/article?id=10.1371/journal.pmed.1002760
- Original Publication: https://www.aging-us.com/article/101414/supplementary/SD1/0/aging-v10i4-101414-supplementary-material-SD1.pdf#:~:text=PhenotypicAgej%20%3D%20141,090165
```

## 15. PhenoAge + Bortz Calculation Bug Disclosure: Missing U/J-Shaped Curves for Biomarkers - comment 3901560157

- Source: GitHub issue comment
- Link: https://github.com/nopara73/LongevityWorldCup/issues/136#issuecomment-3901560157
- Date: 2026-02-14T09:25:37Z
- Notes: https://github.com/nopara73/LongevityWorldCup/issues/136

```text
# Bortz caps + Albumin for pheno age

**Note that previously Albumin wasn't capped, because we didn't think it needs to be. It was a mistake and capping it will change the results of pheno age, even in top 10.**

Cap research  by [Michael Lustgarten](https://michaellustgarten.com/)

[Feb 2026 data for Adam (1).pptx](https://github.com/user-attachments/files/25312726/Feb.2026.data.for.Adam.1.pptx)

## Provisional cap decisions:

| Biomarker | Cap Direction | Cap Value | Unit | Notes |
|---|---|---|---|---|
| Creatinine | Lower cap | 44 | µmol/L | |
| Glucose | Lower cap | 4.44 | mmol/L | |
| CRP | No lower cap | — | mg/L | |
| White Blood Cell Count (WBC) | Lower cap | 3.5 | ×10³ cells/µL | |
| Lymphocytes | Upper cap | 60 | % | |
| Mean Corpuscular Volume (MCV) | No lower cap | — | fL | |
| Red Cell Distribution Width (RDW) | Lower cap | 11.4 | % | |
| Alkaline Phosphatase (AP) | No lower cap | — | U/L | |

| Biomarker | Cap Direction | Cap Value | Unit | Notes |
|---|---|---|---|---|
| Albumin | Upper cap | 54 | g/L | |

| Biomarker | Cap Direction | Cap Value | Unit | Notes |
|---|---|---|---|---|
| Neutrophils | Lower cap | 2 | ×10⁹/L | |
| Monocytes | Lower cap | 0.3 | ×10⁹/L | |
| RBC | Upper cap | 5.77 | ×10¹²/L | |
| MCH | Lower cap | 25.7 | pg | |
| Urea | Upper cap | 9.3 | mmol/L | |
| Cystatin C | Lower cap | 0.38 | mg/L | |
| HbA1c | Lower cap | 26 | mmol/mol | |
| Total cholesterol | Upper cap | 7.58 | mmol/L | needs discussion |
| ApoA1 | Upper cap | 1.82 | g/L | |
| ALT | Upper cap | 29 | U/L | |
| GGT | No lower cap | — | U/L | |
| SHBG | No lower cap | — | nmol/L | needs discussion |
| Vitamin D | Upper cap | 112.6 | nmol/L | |
```

## 16. PhenoAge + Bortz Calculation Bug Disclosure: Missing U/J-Shaped Curves for Biomarkers - comment 3903336594

- Source: GitHub issue comment
- Link: https://github.com/nopara73/LongevityWorldCup/issues/136#issuecomment-3903336594
- Date: 2026-02-15T05:15:24Z
- Notes: https://github.com/nopara73/LongevityWorldCup/issues/136

```text
oh wow, thanks a lot for the longevity tools biomarker range research, didn't know about that!
```

## 17. PhenoAge + Bortz Calculation Bug Disclosure: Missing U/J-Shaped Curves for Biomarkers - comment 3903341599

- Source: GitHub issue comment
- Link: https://github.com/nopara73/LongevityWorldCup/issues/136#issuecomment-3903341599
- Date: 2026-02-15T05:19:16Z
- Notes: https://github.com/nopara73/LongevityWorldCup/issues/136

```text
BTW I wouldn't change anything regarding pheno competition that doesn't need to be changed for a very good reason. Albumin was screaming for a cap.
```

## 18. PhenoAge + Bortz Calculation Bug Disclosure: Missing U/J-Shaped Curves for Biomarkers - comment 3906810014

- Source: GitHub issue comment
- Link: https://github.com/nopara73/LongevityWorldCup/issues/136#issuecomment-3906810014
- Date: 2026-02-16T07:09:47Z
- Notes: https://github.com/nopara73/LongevityWorldCup/issues/136

```text
Goodhart's law, or value capture in C. Thi Nguyen's terminology is perhaps the most important threat for this sport, or at least that's what I identified when I originally started this endeavor in 2024. See my article: [Bodybuilding, Longevity and The Philosophy of Games](https://nopara73.medium.com/longevity-game-9a79a8645bd9)

The idea is to change aging clocks every year, so people don't have too much time to optimize for one metric.

Regarding improvement to past self, that'd be great, not only because I would had win the last season on that front (see redemption arc badge) but also because that'd be an objective way of measuring actual intervention results. The current compared to chronological aging flattens that out to sth like intervention over lifetime. The problem with that as @SubJunk alluded to is perverse incentives. I suppose I don't need to explain this further for a cryptogopher haha. Early on I had talks with Rejuvenation Olympics organizers and they've stopped doing that because of that.
```

## 19. PhenoAge + Bortz Calculation Bug Disclosure: Missing U/J-Shaped Curves for Biomarkers - comment 4148022371

- Source: GitHub issue comment
- Link: https://github.com/nopara73/LongevityWorldCup/issues/136#issuecomment-4148022371
- Date: 2026-03-28T12:48:50Z
- Notes: https://github.com/nopara73/LongevityWorldCup/issues/136

```text
> This may not work as well with the smaller data set that was used by the Levine paper (NHANES)

During my monkey coding around I've done it on the pheno age training dataset. It indeed didn't make much of a difference. (Although it made some.) But for LWC it should make because in this competition extremes dominate. In the pheno age dataset the best pheno age anyone achieved was -13 years, where in LWC it was -22 years. So curves are much more important for us, than for the population average.

For Bortz it's even more necessary because in Bortz many biomarkers are rewarded in the unintuitive direction.

> That's hard, manual work

There's no such thing as "hard manual work" anymore :)
```

## 20. PhenoAge + Bortz Calculation Bug Disclosure: Missing U/J-Shaped Curves for Biomarkers - comment 4149609647

- Source: GitHub issue comment
- Link: https://github.com/nopara73/LongevityWorldCup/issues/136#issuecomment-4149609647
- Date: 2026-03-29T07:17:04Z
- Notes: https://github.com/nopara73/LongevityWorldCup/issues/136

```text
Excellent work! We should replace the current capped linear model.

Please push it on your GitHub. No need to package it well like in the old times, because AI can now figure these things out.

I've got 2 questions:
1. How did you reproduce the exact same cohort count? Actually if it can be mined out of a github repo, then don't even bother with the explanation.
2. Which model do you believe to be the most appropriate to replace the capped linear model?
```

## 21. the longevity world cup has launched foss leaderboard for biological age reversal

- Source: LongevityBase forum
- Link: https://forum.longevitybase.org/t/the-longevity-world-cup-has-launched-foss-leaderboard-for-biological-age-reversal/402/1
- Date: 2025-09-22T08:49:17.758Z
- Notes: Discourse user activity

```text
What : Free, open-source leaderboard ranking on PhenoAge . The biological aging clock used changes every year. Think of Rejuvenation Olympics, but instead of a marketing funnel, it’s an independent project.

Check it out if it’s something that interests you: https://www.longevityworldcup.com/
```

## 22. About Longevity World Cup

- Source: LongevityWorldCup repository doc
- Link: https://github.com/nopara73/LongevityWorldCup/blob/master/LongevityWorldCup.Documentation/About.md
- Notes: LongevityWorldCup.Documentation/About.md

```text
# About Longevity World Cup

I grew up wanting to play football at the highest level. Like most sports, football gives you a brutal bargain: if you are lucky, you get a few good years, and then age starts pushing you out.

Longevity World Cup exists for the opposite reason. It is a sport for time. Instead of asking how fast you can run at twenty, it asks how much biological age you can take back at forty, fifty, sixty, or beyond.

![The silhouette of Michael Lustgarten, PhD, inaugural Longevity World Cup champion](../LongevityWorldCup.Website/wwwroot/assets/content-images/headshot.jpg)

*The silhouette of [Michael Lustgarten, PhD](/athlete/michael-lustgarten), the inaugural Longevity World Cup champion.*

## What it is

Longevity World Cup is an open competition where longevity athletes rank by improving biological age measures.

The scoreboard is built around biomarker data and biological aging clocks. Athletes submit result data, the competition calculates biological age, and the leaderboard ranks athletes by Age reduction: how far their biological age is below their chronological age.

That gives longevity practice a number, a season, rivals, and a public record. It turns private health optimization into longevity sport.

## Why I started it

Games are powerful because they turn difficult work into something people voluntarily come back to. If the rules are clear and the scoreboard matters, people train harder, compare notes, and improve together.

Longevity may be the most useful game we can build, because the scarce resource is time. Chronological age cannot be played. Biological age can be measured, compared, improved, and improved again as better clocks emerge.

Before starting the competition, I spent more than a year interviewing longevity athletes in long form. The [Immortal Combat podcast](https://www.youtube.com/playlist?list=PL4nqc85w185sO4i7eR3oUO_lMmlJ2K1cL) now has 60+ conversations with athletes, researchers, and builders trying to understand what actually moves the field forward.

## How it works

Longevity athletes compete through biological aging clocks such as [pheno age](https://pmc.ncbi.nlm.nih.gov/articles/PMC5940111/pdf/aging-10-101414.pdf) and [bortz age](https://www.nature.com/articles/s42003-023-05456-z). Each clock uses a defined set of biomarkers and converts them into a biological age.

The competition has tracks, seasons, leagues, and a public leaderboard. Some clocks are all-time competitions; others are seasonal. The exact submission requirements, tracks, ranking rules, and prize mechanics live in the [Ruleset](/ruleset).

## Built in public

The leaderboard is public. The [Ruleset](/ruleset) explains tracks, seasons, ranking, submissions, and prize mechanics. The Markdown behind About, History, and Ruleset is linked from each page. The code lives on [GitHub](https://github.com/nopara73/LongevityWorldCup).

The surrounding conversation is public too. The [Immortal Combat podcast](https://www.youtube.com/playlist?list=PL4nqc85w185sO4i7eR3oUO_lMmlJ2K1cL) has 60+ long-form conversations with longevity athletes, researchers, and builders.

Bitcoin donations fund the prize pool. The donation split and payout timing are in the [Ruleset](/ruleset).

## Who builds it

Longevity World Cup is a free and open-source project built with its community.

I am Adam Ficsor, and online I publish as nopara73. I started the competition after a year of long-form conversations with longevity athletes and researchers.

But the project is not just me. Klaus Townsend created the [Longevity World Cup merch store](https://merch.longevityworldcup.com/). Michael Lustgarten, PhD, the 2025 champion, created the [US blood panel](https://www.ultalabtests.com/partners/conqueragingordietrying/test/conquer-aging-or-die-trying-bortz-biological-age-panel) linked from the bortz age flow, covering the biomarkers for both bortz age and pheno age. Athletes, developers, donors, guests, and contributors keep pushing the sport forward.

## Where to go next

- [Leaderboard](/leaderboard): See the current competition standings.
- [Ruleset](/ruleset): Read the tracks, seasons, ranking rules, and prize mechanics.
- [History](/history): Follow how longevity became a sport.
- [Immortal Combat podcast](https://www.youtube.com/playlist?list=PL4nqc85w185sO4i7eR3oUO_lMmlJ2K1cL): Watch long-form conversations with longevity athletes.
- [GitHub](https://github.com/nopara73/LongevityWorldCup): Contribute to the open-source project.
```

## 23. Every commercially available biological aging clock in existence

- Source: LongevityWorldCup repository doc
- Link: https://github.com/nopara73/LongevityWorldCup/blob/master/LongevityWorldCup.Documentation/AgingClocks.md
- Notes: LongevityWorldCup.Documentation/AgingClocks.md

```text
# Every commercially available biological aging clock in existence

|Created|Clock|Type|Creators (only real humans allowed)|Sellers|Availability|
|-|-|-|-|-|-|
|1960s–1970s|VO₂max Age|cardiorespiratory fitness (max oxygen uptake)|Per-Olof Åstrand|any sports lab, fitness trackers|global|
|2004|Vascular Age|arterial stiffness (pulse wave velocity)|Thomas Münzel|smart scales, wearables, clinics|global|
|2009|Telomere Age|telomere length|Elizabeth Blackburn, María Blasco|SpectraCell, TeloYears, LifeUnlocked, TA65, Life Length|global|
|2013|Horvath|methylation|Steve Horvath|[myDNAge](https://www.mydnage.com/products/blood)|USA, Canada, Europe and Australia|
|2015|Garmin Fitness Age|wearable||[Garmin](https://www.garmin.com/)|global|
|2016|GlycanAge|glycomics|Gordan Lauc|[GlycanAge](https://glycanage.com/price-and-plans)|global|
|2016|Aging.AI|blood (deep learning model)|Polina Mamoshina, Kirill Kochetov, Evgeny Putin, Franco Cortese, Alexander Aliper, Won-Suk Lee, Sung-Min Ahn, Lee Uhn, Neil Skjodt, Olga Kovalchuk, Morten Scheibye-Knudsen, Alex Zhavoronkov|[Aging.AI](https://www.unhooked.co.uk/diversity-ai/aging/index.html)|global (online)|
|2016|TruMe|methylation (saliva)||[prohealth](https://www.prohealth.com/products/trume-at-home-dna-biological-age-test-tst100), [agelessrx](https://agelessrx.com/trume/), [EasyDNA](https://easydna.co.uk/knowyourbioage-test/)|Canada, US, UK|
|2017|EpiAge|methylation (saliva)||[Life Extension Europe](https://www.lifeextensioneurope.com/epiage-epigenetic-age-test), [BrainMarket](https://www.brainmarket.hu/hansen-epiage-biological-age-test-kits--test-k-urceni-biologickeho-veku/)|Europe|
|2018|Pheno Age|blood|Morgan E. Levine|any lab, [Longevity World Cup pheno age calculator](https://www.longevityworldcup.com/onboarding/pheno-age.html)|global|
|2018|AgeMeter Functional Age Test|functional biomarkers|Elliott Small|[AgeMeter](https://agemeter.com/)|global|
|2019|Elysium Index|methylation (saliva)||[Elysium Health](https://www.elysiumhealth.com/products/index)|US|
|2019|GrimAge|methylation|Lu AT, Quach A, Wilson JG, Reiner AP, Aviv A, Raj K, et al.|[Clock Foundation](https://clockfoundation.org/product/grimage-epigenetic-age-test-promo/)|United States, UK and Europe|
|2019|Chronomics|methylation (saliva)||[Amazon](https://www.amazon.com/Chronomics-Epigenetic-Biological-Age-Test/dp/B0CSHHKZJP),[OneADay](https://shop.oneaday.com/products/biological-age)|UK|
|2020|AnthropoAge|anthropometrics|Enrique F. Velázquez-Palacio, Omar Yaxmehen Bello-Chavolla|[BelloLab AnthropoAge Calculator](https://bellolab.shinyapps.io/anthropoage/)|global (online)|
|2020|ProHealth Biological Age Test|methylation (saliva)||[ProHealth](https://www.prohealth.com/collections/testing/products/prohealth-biological-age-test-tst101), [Ubuy](https://www.ubuy.fr/en/product/MC3YCU42G-at-home-biological-age-test-most-advanced-test-to-reveal-your-true-age-telomere-length-rate-of-aging-clinically-researched-epigenetic-markers)|USA|
|2020|Viome Biological Age|microbiome + transcriptomics|Momo Vuyisich, Vinay Gopu|[Viome](https://www.viome.com/products/full-body-intelligence)|USA + selected international|
|2021|InnerAge 2.0|blood|Gil Blander|[InsideTracker](https://store.insidetracker.com/products/innerage)|USA, Canada|
|2021|DunedinPACE|methylation|Daniel W. Belsky (Columbia), Avshalom Caspi, Terrie E. Moffitt|[TruDiagnostic](https://shop.trudiagnostic.com/products/truage-complete-epigenetic-collection), [Blueprint](https://blueprint.bryanjohnson.com/products/speed-of-aging)|all US territories and most countries|
|2021|StrideAge|methylation (blood)||[Stride](https://www.getstride.com/us/shop/strideone/)|USA|
|2021|Thorne Biological Age Health Panel|blood biomarkers||[Thorne](https://www.thorne.com/products/dp/biological-age)|USA|
|2022|DNAmFitAge|methylation|S. McCartney, Morgan Levine et al.|[neotes](https://neotes.com/en/produkt/neotes-bioage-test/)|Germany|
|2023|TallyAge|methylation||[Tally Health](https://tallyhealth.com/products/test-kit)|USA|
|2023|SystemAge (Generation Lab)|multi-omic||[Generation Lab](https://www.generationlab.com/the-systemage-test)|global|
|2023|OMICmAge|methylation + proteomics||[TruDiagnostic](https://shop.trudiagnostic.com/products/truage-complete-epigenetic-collection)|all US territories and most countries|
|2023|Physiological Age (PhysiAge)|blood glucose + blood pressure + step count||[Aging is Beautiful Calculator](https://agingisbeautiful.com/2023/08/18/a-simple-way-to-calculate-your-physiological-age/)|global (online)|
|2023|Bortz Age|blood|Jordan Bortz, Andrea Guariglia, Lucija Klaric, David Tang, Peter Ward, Michael Geer, Marc Chadeau-Hyam, Dragana Vuckovic & Peter K. Joshi |many labs, [longevity-tools bortz age calculator](https://www.longevity-tools.com/humanitys-bortz-blood-age)|global|
|2024|NOVOS Age|methylation (saliva)||[NOVOS](https://novoslabs.com/product/novos-age/)|USA, Canada|
|2024|LinAge|blood, BP, pulse rate, BMI, smoking status, medical history|[Sheng Fong et al.](https://pmc.ncbi.nlm.nih.gov/articles/PMC11333290/)|any lab|global|
|2024|BioAge DNA Test (DNA Labs India)|methylation (saliva)||[DNA Labs India](https://dnalabsindia.com/test/longevity-biological-age-bioage-dna-test)|India, international shipping|
|2024|WHOOP Age|wearable||[WHOOP](https://www.whoop.com/)|global|
|2025|LinAge2|blood, BP, pulse rate, BMI, smoking status, medical history|[Sheng Fong et al.](https://www.nature.com/articles/s41514-025-00221-4)|any lab|global|
|2025|Muhdo|methylation (saliva)|Chris Collins et al.|[muhdohub](https://muhdohub.com/products/dna-epigenetic-kit), [shop.muhdo](https://shop.muhdo.com/)|global|
|2025|Aeternum DNA Biological Age Test|methylation (saliva)|Oliver Foster|[Aeternum](https://aeternum.site/products/aeternum-biological-age-test-kit), [Aeternum EU](https://eu.aeternum.site/product/aeternum-biological-age-test-kit/)|global (domestic stock in USA, Canada, UK, Australia, Singapore, Hong Kong, & Europe)|
|2025|SYMPHONYAge|methylation||[TruDiagnostic](https://shop.trudiagnostic.com/products/truage-complete-epigenetic-collection), [Life Extension](https://www.lifeextension.com/lab-testing/itemlc900003/truage-complete-epigenetic-age-profile-finger-stick-test)|all US territories and most countries|
|2025|Medipredict Biological Age Test|methylation||[Medipredict](https://medipredict.com/en/products/biologiai-eletkor)|Hungary, EU|
|2025|AgeRate|methylation (blood)|Cole Kirschner, Guillaume Paré, Kevin Peters, Nathan Cawte|[AgeRate](https://agerate.com/), [Jinfinity](https://www.jinfiniti.com/product/biological-age-agerate-epigenetic-age/)|USA, Canada|
|2025|SRW DNA Age Test|methylation (saliva)||[NetPharmacy](https://www.netpharmacy.co.nz/products/srw-laboratories-dna-age-biological-age-test)|New Zealand, Australia|
|2025|The Clock|methylation (saliva)|Dr. Jan Gruber & Dr. Vincenzo Sorrentino|[ForYouth](https://foryouth.co/products/the-clock-biological-age-test)|global|
|2025|epiAge|methylation|David Cheishvili, Moshe Szyf et al.|[EpiMedTech](https://epimedtech.com/product/epiage/)|Singapore, Hong Kong, US, Canada|
|2025|Epi-Proteomics|proteomics (saliva)||[MoleQlar](https://moleqlar.com/en/products/epi-proteomics-test-en)|Europe|
|2025|BEspoke Age Test (BEAT™)|multi-domain|[Tiat Lim](https://www.instagram.com/deaging.guru/)|[BEAT™ Spreadsheet](https://onedrive.live.com/:x:/g/personal/43A80B3B028E9AE0/EeCajgI7C6gggENT6AUAAAABl2j0sLEo1_wgrV6AFeAwZw?resid=43A80B3B028E9AE0!387155&ithint=file%2Cxlsx&e=rhECw2&migratedtospo=true&redeem=aHR0cHM6Ly8xZHJ2Lm1zL3gvYy80M2E4MGIzYjAyOGU5YWUwL0VlQ2FqZ0k3QzZnZ2dFTlQ2QVVBQUFBQmwyajBzTEVvMV93Z3JWNkFGZUF3Wnc_ZT1yaEVDdzI)|global|
|2025|GetTested Biological Age|blood + methylation||[GetTested.io](https://gettested.io/product/biological-age-and-longevity-test)|more than 60 countries|
|2025|SevenAge|blood|[David H. Meyer et al.](https://www.nature.com/articles/s41598-025-27478-9)|any lab|global|
```

## 24. History of longevity as a sport

- Source: LongevityWorldCup repository doc
- Link: https://github.com/nopara73/LongevityWorldCup/blob/master/LongevityWorldCup.Documentation/LongevitySportHistory.md
- Notes: LongevityWorldCup.Documentation/LongevitySportHistory.md

```text
# History of longevity as a sport

The longevity sport industry is an emerging field where participants compete to reverse biological aging and optimize health, turning longevity into a [measurable and dynamic competition.](https://nopara73.medium.com/longevity-game-9a79a8645bd9)

## Timeline

### 2018 - Zolman leaderboards

[Dr. Oliver Zolman](https://www.youtube.com/watch?v=iRmZoCt3BWA), a medical doctor, healthcare consultant, and founder of 20one Consulting, created the first longevity leaderboards where both people and animals competed with each other for better biological aging clocks. Participants were required to publicly share biomarker evidence, adhering to a 15-point statistical algorithm known as the Zolman Biological Age Marker (Z-BAM) criteria. These leaderboards were carefully verified by third parties to prevent data manipulation and maintain scientific credibility.

![Zolman leaderboards](https://github.com/user-attachments/assets/1b22448f-438d-49cb-a409-0869b301c6e9)

Today, his [leaderboards](https://www.oliverzolman.com/leaderboards) are "Undergoing update".

### 2023 - Rejuvenation Olympics

Dr. Zolman, who was at the time also the Chief Scientist for [Bryan Johnson](https://en.wikipedia.org/wiki/Bryan_Johnson)—today's most prominent longevity athlete—and Bryan Johnson teamed up with [TruDiagnostic](https://www.trudiagnostic.com/), a company that sells epigenetic aging tests, to create the [Rejuvenation Olympics](https://www.rejuvenationolympics.com/). In this competition, people competed with each other to achieve better [DunedinPACE](https://elifesciences.org/articles/73420) scores, which aim to measure a person's pace of aging. Two leaderboards were launched: a relative one and an absolute one.

In the relative leaderboard, scores were given based on how much someone improved from their baseline, while in the absolute leaderboard, the absolute pace of aging reversal compared to chronological age was counted.

![Relative Leaderboard](https://github.com/user-attachments/assets/4112a04d-9e84-486a-9231-53929aba1820)

![Absolute Leaderboard](https://github.com/user-attachments/assets/d5382e05-b497-4368-9fa3-e7a1c546c417)

These leaderboards gave rise to pioneer longevity athletes like [Julie Gibson Clark](https://www.youtube.com/watch?v=fEq9_vKD74M) and [Dave Pascoe](https://www.youtube.com/watch?v=b3D1k1-w9K4).

### 2024 - Immortal Combat Podcast

[nopara73](https://www.youtube.com/user/nopara73), a software developer, privacy researcher, and Bitcoin pioneer, made a sudden career change and started a series of interviews with longevity athletes called [The Immortal Combat](https://www.youtube.com/playlist?list=PL4nqc85w185sO4i7eR3oUO_lMmlJ2K1cL).


### 2024 - Rejuvenation Olympics 2.0

TruDiagnostic launched the second version of the Rejuvenation Olympics. Major changes include the number of athletes now being unlimited, instead of being limited to 20 as previously. It now focuses on absolute pace of aging (DunedinPACE) instead of comparing improvements to baseline or factoring in chronological age.

![Rejuvenation Olympics 2.0](https://github.com/user-attachments/assets/06784db5-bac3-4cc4-bfc5-ef3376917c62)

### 2025 January - Longevity World Cup

nopara73 launched the [Longevity World Cup](https://www.longevityworldcup.com/) using [pheno age](https://pmc.ncbi.nlm.nih.gov/articles/PMC5940111/pdf/aging-10-101414.pdf), a biological aging clock which can be acquired through regular blood tests. In this competition, absolute age reversal relative to chronological age is counted. This competition introduced the concept of prize money and various leagues based on gender and age groups.

![Longevity World Cup](https://github.com/user-attachments/assets/1c498779-62c2-458a-918c-37fd7aa00515)

### 2025 March - Dr. Oliver Zolman's departure

In a private exchange, Dr. Zolman revealed his departure from the Rejuvenation Olympics. He attributed his choice to step away to concerns over the leaderboard's scientific integrity, stating, "Bryan and TruDiagnostic made the leaderboards unscientific and clinically meaningless."

### 2026 January 1 – First place finishes across major longevity leaderboards

On January 1, 2026, Bryan Johnson secured first place on the Rejuvenation Olympics leaderboard, while Michael Lustgarten, PhD ranked first on the Longevity World Cup leaderboard. This marked the first time different longevity competitions, using distinct biological aging clocks, simultaneously crowned clear category leaders—highlighting both the fragmentation and maturation of longevity as a competitive sport.

![610955655_17863402740557255_5447801270218589289_n](https://github.com/user-attachments/assets/407dba3f-777e-4048-b69f-2e524d90aedd)

### 2026 January 15 - Longevity World Cup season 2025 ended

The inaugural Longevity World Cup concluded. The top 20 athletes by age reduction are listed below:

| Rank | Athlete | Age reduction |
|------|---------|---------------|
| 1 | [Michael Lustgarten, PhD](https://www.longevityworldcup.com/athlete/michael-lustgarten) | -22.1 years |
| 2 | [Zdenek Sipek](https://www.longevityworldcup.com/athlete/zdenek-sipek) | -20.6 years |
| 3 | [Wen Z](https://www.longevityworldcup.com/athlete/wen-z) | -20.1 years |
| 4 | [Philipp Schmeing](https://www.longevityworldcup.com/athlete/philipp-schmeing) | -19.6 years |
| 5 | [Angela Buzzeo](https://www.longevityworldcup.com/athlete/angela-buzzeo) | -19.5 years |
| 6 | [deelicious](https://www.longevityworldcup.com/athlete/deelicious) | -18.9 years |
| 7 | [Juan Robalino](https://www.longevityworldcup.com/athlete/juan-robalino) | -18.3 years |
| 8 | [Max](https://www.longevityworldcup.com/athlete/max) | -18.3 years |
| 9 | [anicca](https://www.longevityworldcup.com/athlete/anicca) | -17.8 years |
| 10 | [Ilhui](https://www.longevityworldcup.com/athlete/ilhui) | -17.7 years |
| 11 | [Larsemann](https://www.longevityworldcup.com/athlete/larsemann) | -17.4 years |
| 12 | [Lauren](https://www.longevityworldcup.com/athlete/lauren) | -17.4 years |
| 13 | [John](https://www.longevityworldcup.com/athlete/john) | -17.4 years |
| 14 | [Maria Olenina](https://www.longevityworldcup.com/athlete/maria-olenina) | -17.4 years |
| 15 | [David X](https://www.longevityworldcup.com/athlete/david-x) | -17.0 years |
| 16 | [David Lo](https://www.longevityworldcup.com/athlete/david-lo) | -16.9 years |
| 17 | [Julie Jiang](https://www.longevityworldcup.com/athlete/julie-jiang) | -16.8 years |
| 18 | [QingqingZhuo](https://www.longevityworldcup.com/athlete/qingqingzhuo) | -16.7 years |
| 19 | [Dave Pascoe](https://www.longevityworldcup.com/athlete/dave-pascoe) | -16.6 years |
| 20 | [Keith Blondin](https://www.longevityworldcup.com/athlete/keith-blondin) | -16.4 years |

### 2026 February - Longevity World Cup season 2026 started

Season 2 of the Longevity World Cup was released with major competition-format upgrades:

- The competition introduced **two tracks**: **Amateur** and **Professional**.
- A new aging clock was added for the Pro track: [bortz age](https://www.nature.com/articles/s42003-023-05456-z), while [pheno age](https://pmc.ncbi.nlm.nih.gov/articles/PMC5940111/pdf/aging-10-101414.pdf) continued, shifting the competition into a multi-clock era.
- Athlete pages and the leaderboard began surfacing new derived performance views like pace of aging / pace rank.
- Entry evolved from an early “low-friction” model toward explicit pricing.

### 2026 November (announced) - Super Age Games

The [Super Age Games](https://games.superage.com/) announced an in-person longevity fitness competition for November 7, 2026 in New York City. Its format emphasizes functional healthspan markers, with eight trials spanning grip strength, aerobic capacity, agility, balance, functional strength, endurance under load, cognitive capacity under physical stress, and relational capacity.

### 2027 (announced) - Younger Contest

[Younger Contest 2027](https://luma.com/829lvr59), hosted by NeuroAge Therapeutics, frames longevity competition as a six-month attempt to become biologically younger across brain, body, and face. Its composite Younger Score combines biological age markers, brain age, face age, functional measures like VO2 max and grip strength, and optional ultra testing such as MRI, DEXA, and expanded biomarkers.

## Special mentions

Similar competitions are also emerging, such as [XPRIZE Health](https://www.xprize.org/domains/health), the now-offline VO2 Max Leaderboard by JoinZero, Favies (previously Goaly), and various fitness tracker-specific gamified leaderboards.

## Where to go next

- [About Longevity World Cup](/about)
- [Ruleset](/ruleset)
- [Leaderboard](/leaderboard)
```

## 25. Longevity World Cup

- Source: LongevityWorldCup repository doc
- Link: https://github.com/nopara73/LongevityWorldCup/blob/master/README.md
- Notes: README.md

```text
# Longevity World Cup
[![Watch the video](https://img.youtube.com/vi/Kq0VkLF3Z4Q/0.jpg)](https://www.youtube.com/shorts/Kq0VkLF3Z4Q)

Longevity World Cup is an open competition where longevity athletes rank by improving biological age measures. Athletes submit biomarker data, biological aging clocks turn those results into biological age, and the leaderboard ranks athletes by Age Reduction.

For more context, read the [project story](LongevityWorldCup.Documentation/About.md), the [competition rules](LongevityWorldCup.Documentation/Ruleset.md), and the [history of longevity as a sport](LongevityWorldCup.Documentation/LongevitySportHistory.md).

## Website

https://www.longevityworldcup.com/

## Roadmap
- [x] 2024 Sept: Inception
- [x] 2024 Oct: Design
- [x] 2024 Nov-Dec: Code
- [x] 2025 Jan - Sept: Beta Testing
- [x] 2025 Sept 16st: Launch
- [x] 2026 Jan 15st: Season 1 End
- [x] 2026 Feb: Season 2 Start
- [ ] 2027 Jan 15st: Season 2 End

## Build From Source Code

### Get The Requirements

1. Get Git: https://git-scm.com/downloads
2. Get .NET 10 SDK: https://dotnet.microsoft.com/download
3. Disable .NET's telemetry by executing in the terminal `export DOTNET_CLI_TELEMETRY_OPTOUT=1` on Linux and macOS or `setx DOTNET_CLI_TELEMETRY_OPTOUT 1` on Windows.
4. Get Visual Studio with ASP.NET web development installed: https://visualstudio.microsoft.com/

### Clone

```sh
git clone https://github.com/nopara73/LongevityWorldCup.git
```

### Run

Open the `.sln` file with Visual Studio & run the project.

### Update

```sh
git pull
```

## [Deployment](LongevityWorldCup.Documentation/ServerDeployment.md)
```

## 26. Longevity World Cup Game Nights

- Source: LongevityWorldCup repository doc
- Link: https://github.com/nopara73/LongevityWorldCup/blob/master/LongevityWorldCup.Documentation/GameNights.md
- Notes: LongevityWorldCup.Documentation/GameNights.md

```text
Update: there wasn't enough demand for game nights just yet. Perhaps in the future.

# Longevity World Cup Game Nights

We play games on video calls. Game nights are open to all obsessed longevity athletes.

## Why?

There seems to be demand from longevity athletes to get to know each other.

## How?

- When? Every Monday 16:00 UTC
- Where? https://riverside.fm/studio/lwc-game-nights?t=1960db416608f9a0b12a
- Games are sometimes recorded and published.
- Camera and microphone are required, otherwise it won't be fun.

## What?

What follows is an unorganized brainstorming of games we can play: **zero props, zero software, video‑call‑friendly.**

- Herd Mentality https://youtu.be/4vAcBW5146o
- Majority Stays https://youtu.be/uY0IkfyiPE8
- Did I Lie? https://youtu.be/uY0IkfyiPE8
- Many more ideas here: https://github.com/nopara73/LongevityWorldCup/issues/209
```

## 27. Ruleset

- Source: LongevityWorldCup repository doc
- Link: https://github.com/nopara73/LongevityWorldCup/blob/master/LongevityWorldCup.Documentation/Ruleset.md
- Notes: LongevityWorldCup.Documentation/Ruleset.md

```text
# Ruleset
The Longevity World Cup is a competition between longevity athletes. The goal is to improve the results of biological aging clocks.

## Seasons & schedule
- A **Season** is the yearly competition window for **seasonal clocks** (for example the Season 2026 [bortz age](https://www.nature.com/articles/s42003-023-05456-z) competition).
- Seasonal clocks typically start and end around mid-January, and **only accept results from the given calendar year** (January 1 to December 31). At **season close**, final rankings lock for that season.
- Some clocks may run as **all-time competitions** instead of seasonal competitions. **The all-time competition currently uses [pheno age](https://pmc.ncbi.nlm.nih.gov/articles/PMC5940111/pdf/aging-10-101414.pdf)**.

![image](https://github.com/user-attachments/assets/337ab8a6-935b-4986-8e63-28aa6f494582)

## Tracks
The Longevity World Cup has multiple tracks: **Amateur** and **Professional**. These use different clocks and rules.

- **Amateur**: designed for accessibility. In Season 2026 this track is centered around **[pheno age](https://pmc.ncbi.nlm.nih.gov/articles/PMC5940111/pdf/aging-10-101414.pdf) (all-time)** submissions.
- **Pro**: the flagship **seasonal** competition. In Season 2026 this track uses **[bortz age](https://www.nature.com/articles/s42003-023-05456-z)** as its seasonal clock and includes prize money.

## Point system (ranking)
- What counts is **biological age reduction**: the more your biological age is below your chronological age, the higher you rank.
- You can submit as many tests as you want.
  - For **all-time clocks** ([pheno age](https://pmc.ncbi.nlm.nih.gov/articles/PMC5940111/pdf/aging-10-101414.pdf)), the site uses your **best (lowest) biological age** across your full submission history.
  - For **seasonal clocks** ([bortz age](https://www.nature.com/articles/s42003-023-05456-z)), the site uses your **best (lowest) biological age** achieved during the 2026 season’s valid window.
  - **Partial or non-same-day submissions are not allowed for a given clock**. Required biomarkers must come from the same blood draw or report date.
- The competition also uses **leagues** (for example generation-based or other category-based rankings). You can rank globally and still compete within your generation league.

![image](https://github.com/user-attachments/assets/968fc0b2-3389-40a3-93e9-4a415f565b11)
## Prizes and payouts
- Prize money is awarded to the top three athletes in the Ultimate League.
- Bitcoin donations fund the prize money pool.
- 10% of donations covers operating costs, and 90% funds the prize money pool.
- Payouts are made in Bitcoin in mid-January. If you need a Bitcoin wallet, we'll help you set one up. We recommend [Green Wallet](https://blockstream.com/green/) for mobile or [Wasabi Wallet](https://wasabiwallet.io/) for desktop. Fun fact: Wasabi Wallet and the Longevity World Cup share the same creator.

![image](https://github.com/user-attachments/assets/9a41f400-92a1-496d-8553-b727186580b2)

## FAQ

### General questions
#### Who can participate in the Longevity World Cup?
Anyone interested in longevity and capable of submitting valid test results can participate.

#### How do I register for the competition?
Apply through the [Longevity World Cup website](https://www.longevityworldcup.com/).

![image](https://github.com/user-attachments/assets/38c545e9-13e5-4ba2-b2e0-d52bbf149207)

Need help with your application? Watch [this seven-minute video tutorial](https://www.youtube.com/watch?v=0mCIbqgfqq8) or [this one-minute video tutorial](https://www.youtube.com/shorts/yhMFZMPAoKQ).

#### Can I withdraw from the competition?
Yes. Email `hi@longevityworldcup.com`.

### About aging clocks and testing
#### What is [pheno age](https://pmc.ncbi.nlm.nih.gov/articles/PMC5940111/pdf/aging-10-101414.pdf)?
[Pheno age](https://pmc.ncbi.nlm.nih.gov/articles/PMC5940111/pdf/aging-10-101414.pdf) is based on clinical biomarkers like glucose and CRP. It reflects physiological aging, not just years lived, and helps assess health and disease risk.

#### What is [bortz age](https://www.nature.com/articles/s42003-023-05456-z)?
[Bortz age](https://www.nature.com/articles/s42003-023-05456-z) is based on blood biomarkers such as Cystatin C, HbA1c, and ApoA1. It reflects physiological aging, not just years lived, and helps assess health and disease risk.

#### From which biomarkers can I calculate my [pheno age](https://pmc.ncbi.nlm.nih.gov/articles/PMC5940111/pdf/aging-10-101414.pdf)?
- Albumin (Serum Albumin)
- Creatinine (Serum Creatinine)
- Glucose (Blood Sugar)
- C-Reactive Protein (CRP or hs-CRP)
- Lymphocyte Percentage (Lymphocyte % or Absolute Lymphocyte Count)
- Mean Corpuscular Volume (MCV or Average Red Blood Cell Size)
- Red Cell Distribution Width (RDW, RDW-CV)
- Alkaline Phosphatase (ALP, Alk Phos)
- White Blood Cell Count (WBC Count, Leukocyte Count)

#### From which biomarkers can I calculate my [bortz age](https://www.nature.com/articles/s42003-023-05456-z)?
- Albumin
- Alkaline Phosphatase (ALP)
- Urea
- Total Cholesterol
- Creatinine
- Cystatin C
- Hemoglobin A1c (HbA1c)
- C-Reactive Protein (CRP / hs-CRP)
- Gamma-Glutamyl Transferase (GGT)
- Red Blood Cell Count (RBC)
- Mean Corpuscular Volume (MCV)
- Red Cell Distribution Width (RDW)
- Monocytes
- Neutrophils
- Lymphocyte Percentage
- Alanine Aminotransferase (ALT)
- Sex Hormone-Binding Globulin (SHBG)
- Vitamin D (25-OH)
- Glucose
- Mean Corpuscular Hemoglobin (MCH)
- Apolipoprotein A1 (ApoA1)

![image](https://github.com/user-attachments/assets/4770485d-440c-4ce6-be6a-b547798696c3)

#### Can I use any laboratory for my tests?
Yes, as long as the lab provides the biomarkers required for that clock.

#### Why does my [pheno age](https://pmc.ncbi.nlm.nih.gov/articles/PMC5940111/pdf/aging-10-101414.pdf) result differ from other calculators?
The Longevity World Cup pheno age calculator is built for biological realism. Many online pheno age calculators use an [incorrect constant in the formula](https://github.com/ajsteele/bioage/issues/3), which originated from a typo in an update by the authors of pheno age. Even calculators with the correct constant can reward pheno age-optimizing hacks that reduce mortality risk in the model but increase real-world mortality risk. For example, pushing alkaline phosphatase or RDW to the extremes can lower your pheno age score even though real-world data shows U- or J-shaped mortality curves for those biomarkers. The Longevity World Cup calculator corrects this by enforcing biologically justified cutoffs, avoiding strategies that make the calculated age look better while your actual risk gets worse. See [pheno age calculation bug disclosure: Missing U-shaped curves for biomarkers](https://github.com/nopara73/LongevityWorldCup/issues/136).

#### Why does my [bortz age](https://www.nature.com/articles/s42003-023-05456-z) result differ from other calculators?
The Longevity World Cup bortz age calculator is built for biological realism. Other calculators can reward bortz age-optimizing hacks that reduce mortality risk in the model but increase real-world mortality risk. For example, pushing alkaline phosphatase or RDW to the extremes can lower your bortz age even though real-world data shows U- or J-shaped mortality curves for those biomarkers. The Longevity World Cup calculator corrects this by enforcing biologically justified cutoffs, avoiding strategies that make the calculated age look better while your actual risk gets worse. See [pheno age and bortz age calculation bug disclosure: Missing U-shaped curves for biomarkers](https://github.com/nopara73/LongevityWorldCup/issues/136).

### Competition mechanics
#### What happens if my results arrive late?
Each season closes in mid-January, giving your lab enough time to process a test from December 31.

#### What if there's a tie?
Ties break by older chronological age, then username alphabetically.

![image](https://github.com/user-attachments/assets/a13ec2f2-346e-4024-aba5-dd32e807a34e)

#### How is my score calculated if I submit multiple results?
If you submit multiple test results, the **best** result is used for your season standing for the relevant clock. This encourages incremental updates and fair comparisons between strategic and transparent participants.

#### How are lab detection limits handled in the competition?
When your lab reports a biomarker value below the detection limit, that limit is used in the calculation. This keeps comparisons fair for other participants. This most often affects CRP; when the limit is unknown, we default to 1 mg/L.

#### How can I cheat?
You can't.

#### How does the Longevity World Cup compare to the Rejuvenation Olympics?
- **Focus**: Longevity World Cup emphasizes **absolute age reversal**, while Rejuvenation Olympics measures the **pace of aging** regardless of chronological age.
- **Structure**: Longevity World Cup has **annual seasons** and may run **multiple clocks/tracks** over time.
- **Prizes**: Longevity World Cup offers **prize money** funded by Bitcoin donations.
- **Leagues**: Longevity World Cup includes **leagues** for generational and other category-based rankings.
- **Testing**: Longevity World Cup uses traditional blood-test-based biological age calculations; Rejuvenation Olympics uses the TruDiagnostic home test kit.

### Practical matters
#### How much can I edit my profile picture?
Your profile picture must show you facing the camera, but you can edit it freely, including as a drawing or AI-generated version. Choose a picture that represents you well and works for age estimation from a photo.

![image](https://github.com/user-attachments/assets/613afebb-4ec7-4b0d-a961-8a09e26391ab)

#### I'm already an athlete. How can I make changes?
Through the [Athlete Dashboard](https://www.longevityworldcup.com/select-athlete) or by sending us an email to `hi@longevityworldcup.com`.

#### What will sponsorships include?
Sponsorships are planned for future seasons. Future packages will let companies sponsor athletes for website visibility.

#### What if Bitcoin's value changes significantly?
[1 BTC = 1 BTC](https://old.reddit.com/r/Bitcoin/comments/w1di0k/please_understand_what_1_btc_1_btc_really_means/)

## Where to go next

- [About Longevity World Cup](/about)
- [History of Longevity as a Sport](/history)
- [Leaderboard](/leaderboard)
```

## 28. Bodybuilding, Longevity and The Philosophy of Games

- Source: Medium
- Link: https://nopara73.medium.com/longevity-game-9a79a8645bd9
- Notes: CC0/no-rights-reserved when exposed by Medium page/feed

```text
Bodybuilding, Longevity and The Philosophy of Games

nopara73

10 min read ·
Mar 2, 2024

--

1

Listen

Share

UPDATE : A year has passed since I wrote this and I decided to launch the Longevity World Cup . Let’s compete!
https://www.longevityworldcup.com/

At the end of the world, all we’re gonna do is play games. Bernard Suits, a pioneer of the philosophy of games, defines the playing of a game as a voluntary attempt to overcome unnecessary obstacles . Imagine a world where survival is guaranteed and every need is met. What’s left for us to do? In his book, The Grasshopper: Games, Life and Utopia, he argues that in such a world where scarcity has been solved, the only thing left for us to do is create unnecessary obstacles for ourselves to overcome. In other words, play games.
Press enter or click to view image in full size

But what is the nature of games? More recently, C. Thi Nguyen, an avid Suitsian philosopher of games, has synthesized the literature on computer games, board games, card games, party games, tabletop role-playing games, live-action role-playing games, and sports. In his book, Games: Agency as Art, he notes that game designers don’t just create environments and obstacles. They set our goals, our abilities and create the agency that we will inhabit in the game. Games create aesthetic experiences of acting and doing. Game designers design our agency. They tell us what to care about and what we ought to value . The heart of every game is the point system, the leaderboard, your aims, your goals, your money, the Bitcoin price, the number of likes, and the grades in school. They are all the same when conceptualized as games: THE NUMBER . And the number must go up.
By now you have an abstract understanding of games, the importance of the number , but you still have no idea where I’m going with all this. You’ll learn soon enough, but for now, let’s just be on the lookout for numbers and understand how they work while narrowing the scope of this essay toward my destination.
Bodybuilding
Bodybuilding vs Strength Training
The number of strength training is how strong you are, and the numbers of bodybuilding are fat and muscle size. Let’s focus on the latter for now. There’s something peculiar happening here. It turns out that oftentimes, you’re better off training for strength to achieve larger muscles than training for larger muscles. The reason is faster feedback loops. If you don’t gain strength within a few weeks, you can correct it, but you won’t notice any muscle gains for months. Thus, you ought to do the wrong things longer. Let’s say you want to gain muscle but do not care about gaining strength. You may still undertake strength training, nevertheless, and create an agency where all you care about is gaining strength, even though it’s all for gaining muscle. You undertake the difficulties that come with strength training for the sake of a higher-order goal . The agency you created for the game of strength training serves a higher-order agency that you created for the game of bodybuilding.
But what initially motivated you to pursue bodybuilding? I bet your life goal wasn’t to have bigger muscles. Big muscles were always a means to an end. Perhaps you wanted to attract girls. But at one point, you forgot about this higher-order agency of yours, and larger muscles became an end in itself. You’ve become “too big.” The number took over your life. Nguyen calls this value capture .
One way that games are satisfying: they let us inhabit a world that’s easier to make sense of, one in which the values are clearer, simpler, and easier to apply. Such games offer us are rare experience of clarity of purpose . They are an existential balm against the rest of our lives, which are full of a plurality of subtle and competing values — C. Thi Nguyen

The Golden Era of Bodybuilding
As a sport, professional bodybuilding has recognized the phenomenon of value capture. New categories like Men’s Physique and Classic Physique testify to their attempts to course correct.
The Men’s Physique division emphasizes a lean, fit body showcasing muscle tone without the extreme bulk seen in traditional bodybuilding. At the same time, Classic Physique bridges the gap between Men’s Physique and traditional bodybuilding, aiming for a physique reminiscent of bodybuilding’s golden era, spanning from the late 1960s to the early 1980s.
But why did Arnold Schwarzenegger pumping iron on the muscle beach in Venice, California, rocketed the sport of bodybuilding into the mainstream?
What happened was a revolution of the number . The invention and adoption of testosterone and Dianabol have pumped the number up to heights that were unheard of before. (Meaning: they got big muscles.)
Today, in the monster era of bodybuilding, value capture has broken the system, the best the game developers were able to come up with is a beauty contest, which is a suboptimal number to have and an indication of a soul-searching phase.
The Golden Era of Street Workout
To contrast, let’s take a quick look at a similar game that devolved into a beauty contest before the revolution of the number would have ever arrived. Street workout originated from African American ghettos and with the advent of YouTube it became popular in poor Eastern European and Russian countries because we had no money to spend on gym membership, but we had plenty of playgrounds. From 2008 to 2012, this game prospered; however, since then, it’s been in an identity crisis. Street workout as a sport has ended up resembling gymnastics: a beauty contest. It has never become mainstream because they didn’t find the right number like bodybuilding did. Their proponents are now doing calisthenics, primal movements, movement training and whatnot.
Bodybuilding vs Street Workout
What I liked about street workout is that unlike other activities I knew of at the time, its proponents rapidly built muscles, similarly to bodybuilders and strength trainees. What I like about street workout now is that unlike bodybuilding, strength training, its focus is on perpetually learning new skills, new movements, which makes you feel much better in your skin.
In fact, many decide to play the game of bodybuilding not to get girls, but for general health and longevity reasons. The higher-order agency is in pursuit of longevity, and you undertake a lower-order agency in pursuit of larger muscle sizes. But is there a value capture here? Would street workout like movement training games be more beneficial undertaking for longevity, your higher-order goal?
Mixed Martial Arts
So far, we’ve seen how a revolution of the number or the lack of it can make or break a game. But no discussion of games can be complete without highlighting the importance of the other constituent of a game: the rules . Fighting is a beautiful game. As we’ve seen so far with other systems, it also has a number of numbers: points and leaderboards, but the most important numbers were always the same: 0 and 1 . Can you kick your opponent’s ass or not? The game of physically overcoming an opponent has been popular since the beginning of human history itself. Its evolution was more about its rules rather than improving upon its fundamental number . The problem of fighting is how to do it in a way that enables fighters to attend subsequent competitions. If every fight were to the death, your champions would never achieve mastery, but a game is similarly broken if the opponents barely touch each other, like in a Taekwondo competition. At the time of writing, the state-of-the-art ruleset for a fighting game is the Ultimate Fighting Championship. Its history is the history of searching for the least number of necessary rules.
Longevity
What can I do with it?
Why are some games more popular than others? We can think of a well designed game with well designed goals and rules yet they still don’t take over the world by storm. The final ingredient is the usefulness of the game. Is the game teaching a practical agency that can be applied in your higher-order agencies? Since we aren’t using horses anymore, games with cars have taken over horse racing in popularity. Football teaches teamwork; fighting is quite a useful skill to possess. The number of bodybuilding: muscle size has a terrible feedback mechanism and ruleset compared to other sports, yet bodybuilding is immensely popular for a game. Same for street workout. Millions of people were doing that even though it has never even found its number . It was and still is very popular by many standards. Usefulness is key.
What’s the most useful skill one can conceivably learn? The one that overcomes the mother of all scarcities: time. The one that gives you time. The one that reverses the aging process.
Timing
Timing isn’t everything, but it’s the one thing that can make everything else irrelevant. Before going any further, I need to address it. Is the pursuit of longevity timely? Are we on the precipice of an age-reversal revolution?
I’ve been keeping an eye on this field for a while now and to be honest, the progress is disappointing… we can’t even double a mouse’s lifespan in a lab. But all’s not lost, otherwise I wouldn’t write this essay.
Longevity escape velocity is the point at which technology advances fast enough to extend life indefinitely, outrunning the aging process.
Exponential growth turns the improbable into the inevitable, quietly, then all at once.

Arguments are increasingly commonly made that mankind has already achieved longevity escape velocity. These rely on the exponential growth of technology. But did we really?
Well… “The best way to predict the future is to invent it.”
Clearly the only way we can have a chance of attaining agelessness if we direct immense amount of human resources towards this goal. This is where the motivational powers of games come in. Wouldn’t it be fun to play the rejuvenation game for a little while? We may get so good at it that we reach longevity escape velocity, reverse aging, achieve immortality, and then embark on an epic adventure on the planes of Sigil, seeking a way to die before our minds unravel, much like the Nameless One in Planescape: Torment.
Alright. I’m in; what’s the game? We have to work for it. Let’s put on our game designer hats and see if we can find one.
Pursuit of The Longevity Number
When talking of longevity, the game with the ultimate usefulness , what number can we design our game around?
Your chronological age is the answer to the question: how old are you? But this is not a very good number because unless we figure out a way to bend the spacetime continuum, we cannot do anything about it.
Another set of numbers can be found if we’re looking at proxies for general health , like grip strength, balance, body fat percentage, VO2 max, bloodwork or muscle mass. Here, we have the same problem street workout had. Unfortunately, we don’t have a single number to rule them all. However, it must be possible to come up with a package and distill them into a single number. Enter the race for developing biological clocks. The most sophisticated attempts to find that number.
Your biological age is supposed to be a number that tells you how old you are, not chronologically, but biologically. First and second-generation clocks have attempted to come up with this number.
Clocks estimating your pace of aging are branded as third-generation clocks, which are supposed to be better.
Although these clocks are highly criticized, the trend is clear, they are getting better. Already, these clocks should be “good enough” for a number of applications. But are they good enough for our purposes: to design a game around one of them? Enter Rejuvenation Olympics , which is just that.
Rejuvenation Olympics
The game is designed around DunedinPACE, which claims to be the state-of-the-art biological clock. RO is a proof of concept of the longevity game I envision. Will it be the game we play in the future, or will it be superseded by something better? There’s no telling just yet. By researching the topic online, cracks in the matrix can already be seen regarding the organization of the competition and its rules. However, those in the big scheme of things are just implementation details. The long-term worry is the robustness of the number and when we expect a large-scale value capture, similarly what we’ve seen in bodybuilding. When will the Dianabol of the state-of-the-art biological clock come out?
But until then, we can have a fun time with it. I believe the Rejuvenation Olympics is the earliest form of the longevity sports industry. It is poised to go mainstream. Attention, funds and other resources will be directed toward this market niche, which will be big. Even though biological clocks have as slow of a feedback mechanism as the numbers of bodybuilding have: muscle and body fat, it’s still poised to go viral, for the same reasons why bodybuilding and street workout did.
For decades, age reversal enthusiasts have been trying to figure out how to gather more resources and bring more brainpower to the field. Building a game or a sport is a novel approach and might just be the one we need to achieve longevity escape velocity.
Games Are Above The Rules
Other than the motivational power of games, there’s one more reason we should be optimistic about this approach. The health industry is in a state of regulatory capture. Governments all around the world are working hard to prevent entrepreneurs, the heroes of this age, from innovating, experimenting and creating value.
It is the case however that rules of games present a novel way to circumvent the rules of governments: bodybuilders enjoy an unofficial immunity from drug regulations. MMA fighters are not only allowed to punch each other in the face, but that’s the whole point. It’s even more fundamental than that: ethics of a game trumps ethics of life. Have you ever wondered why lying is called bluffing in poker?
Games transcend the rules and norms of society and open the door for experimentation that would be unimaginable otherwise. That makes a difference. And now…
Let the Tournament of Immortality begin!
Closing
There you have it. This philosophical article serves as a way to announce my plans to interview contenders for the Rejuvenation Olympics. At least the ones who are willing to talk to me. Subscribe to my channel if you’re interested: https://www.youtube.com/@nopara73/
```

## 29. Brute Forcing Biological Aging Clocks — AI Breakthrough in Longevity Research

- Source: Medium
- Link: https://nopara73.medium.com/brute-forcing-biological-aging-clocks-ai-breakthrough-in-longevity-research-389ef8a3deeb
- Notes: CC0/no-rights-reserved when exposed by Medium page/feed

```text
Brute Forcing Biological Aging Clocks — AI Breakthrough in Longevity Research

nopara73

16 min read ·
Mar 25, 2026

--

1

Listen

Share

My work on the Longevity World Cup led me to implement a number of biological aging clock algorithms, including PhenoAge by Morgan E. Levine et al. Some consider it to be the most rigorous biological aging clock research.
In this work I present my findings:
- PhenoAge is likely an information theoretically optimal aging clock solution on the database it was trained on,
- demonstrate a slightly more performant version of PhenoAge — same inputs, different algorithm — and
- demonstrate a handful of aging clocks that are more performant than PhenoAge on the same dataset, but less performant than my improved version of PhenoAge. Ultimately the performance differences of these clocks are negligible: 85–87%
- I have created clocks that are non-inferior to PhenoAge: at most 1% worse. Most impressively 19 of these biological aging clocks only used 2 biomarkers other than chronological age! The exact number of aging clocks per biomarker counts found at the time of writing: 2 biomarkers: 19 clocks, 3: 128, 4: 247, 5: 332, 6: 361, 7: 832, 8: 1539, 9: 2533
- I’ll also demonstrate thousands of aging clocks that outperform PhenoAge if we strip chronological age from its inputs. The best ones are outperforming the ageless PhenoAge by about 8%: 73% vs 81% the best I’ve found so far, but the search is still ongoing.
- It is my pet peeve that biological aging clocks shouldn’t contain chronological age as its input. To further generalize this belief I think the problem is immutability of inputs, but that’s beside the point. More importantly I have also found that chronological age alone predicts 85% of which individual outlives another. I can’t emphasize the importance of this finding, so I’ll elaborate: firstly we’re talking about aging related mortality, not all cause mortality. ( Your glucose levels don’t predict if you’ll get hit by a bus. Or do they? I think there’s some philosophical merit to that idea, but I digress. ) More precisely chronological age alone performs 85.9%, while PhenoAge performs at 87.7%. Meaning: the 9 biomarkers in PhenoAge only add 1.8% predictive power to aging related mortality on the database it was trained on.
7. A scientific breakthrough?
The findings I described in 1–6 were really aimed to raise some eyebrows in the scientific community. A scientist’s gut instinct should be extreme skepticism. Creating even a single biological aging clock requires lots of work, let alone the ones that are better and thousands that are non-inferior to PhenoAge. How is the author able to claim such unreal results? That’s where the possibility of a scientific breakthrough comes in: I stumbled upon a realization that the simplest machine learning model within a few seconds of training (in my PC on NHANESIII database) finds the best aging clock given an arbitrary biomarker input set.
To achieve all this I created 50 thousand aging clocks on the same dataset PhenoAge was trained on.
Unlike my prior works, this is an empirical data heavy one. My numbers are verifiable by asking questions from an AI agent — I recommend > GPT 5.4 with Cursor — on the bioageautoresearch branch of the following repo: https://github.com/nopara73/PhenoAge2/tree/bioageautoresearch
The reason why the repo is called PhenoAge2 was because my original goal was to beat PhenoAge without chronological age as its input on the same biomarker set just by creating a new formula. I miserably failed in that, but what I found was more important.
From here on the difficulty of creating biological aging clocks is largely reduced. It now is finding the database with reasonable biomarker measurements and mortality information.
This can bring about a new era of biological aging clock research, which, in my view, is the bottleneck of geroscience itself.
The failed quest to create PhenoAge 2.0
As I mentioned I have found PhenoAge to be a theoretically optimal algorithm for the database it was trained on. Unbeknownst to me, I had set out to create PhenoAge 2.0. While I have never reached my destination, it turned out the journey was the real treasure. And that’s what follows:
Recently Andrej Karpathy introduced Autoresearch , which makes an AI agent run in a self improving loop forever, finding ever better solutions to a problem. In biological aging clock research the evaluation of the solution is the risk of aging related mortality, while the solution is the biological aging clock itself.
The idea is therefore to let an agent run over and over again and test millions of hypotheses against a dataset to yield the most performant biological aging clock. It’s essentially a brute force algorithm for the scientific method. (It’s a different brute force from what the real breakthrough in this work is.)
To kick everything off, I created the PhenoAge2 repo and added the PhenoAge research paper to a new project: https://github.com/nopara73/PhenoAge2/commit/04fd065604ccb8ee5e4ab09ce28759459d79d7ba
PhenoAge derivation overview
Before going any further, let us familiarize ourselves with how the PhenoAge formula was derived:
- Derive a small set of blood biomarkers plus your actual age — since this work aims for a strict improvement upon pheno age, we won’t be changing the biomarkers that the original paper uses.
- Combine them into a single score that reflects how risky your body profile looks for aging related death, compared with other people.
- Translate that risk score into “age-like” units, so it reads as a biological age in years.
- If that number is higher than your real age, you are aging faster; if lower, slower.
The important part for us is the 2nd step. That’s what we’ll improve upon. Of course Autoresearch could be used to improve upon Step 1 as well, and that’s where the major gains of biological aging clock research lies. (Editor’s note: I was right. That’s where the breakthrough was.)
However the aim of this work is the demonstration of this new methodology, which is better served by keeping the original pheno age biomarkers intact:
- Albumin
- Creatinine
- Glucose
- C-reactive protein
- Lymphocyte percent
- Mean red cell volume
- Red cell distribution width
- Alkaline phosphatase
- White blood cell count
This puts us on the lookout for a dataset that contains age and these 9 biomarkers. This will be necessary to calculate PhenoAge and eventually compare its performance on our dataset against pheno age 2.0.
To age or not to age?
That is the question. My main pet peeve with most biological aging clocks is that they use chronological age as an input. This is cheating. As we’ll see later, PhenoAge gets the mortality-risk ordering right about 86% of the time, while only 75% if we strip chronological age from its inputs. For the sake of being true to the original pheno age, Pheno Age 2.0 will use the age input, but I urge future developers of biological aging clocks to not do that, even if their results won’t be that impressive.
For the record, when I’ve attempted making an ageless pheno age 2.0, I managed to improve 3% upon the ageless pheno age — from 75% to 78%.
Evaluation criteria
To evaluate the performance of the algorithms we need mortality follow-ups. More concretely, each record also needs:
- follow-up information on whether they died during the observation period
- the timing of death / survival follow-up
- the cause of death, so the model could focus specifically on aging-related mortality
Regarding this last criterion, the pheno age paper defines aging-related mortality as death from:
- heart disease
- malignant neoplasms
- chronic lower respiratory disease
- cerebrovascular disease
- Alzheimer’s disease
- diabetes mellitus
- nephritis / nephrotic syndrome / nephrosis
Reproducing PhenoAge: the database
PhenoAge used NHANES III, and so will we. I acquired the dataset and filtered it to participants with complete measurements for the required biomarkers and sufficient mortality follow-up information. The resulting database contains both participants who died from an aging-related cause and participants who did not, along with each participant’s follow-up duration and cause-of-death information: https://github.com/nopara73/PhenoAge2/commit/304529fc70e6b6ba5f75cedf862b06bc5971925f
Our initial NHANES III cohort included 15,954 participants, versus 9,926 in the original PhenoAge derivation sample. The PhenoAge supplementary material states that the NHANES III training sample consisted of participants aged 20 and over, and the public BioAge NHANES implementation calculates PhenoAge on NHANES III participants aged 20 to 84 with at least 8 hours of fasting. We therefore restricted our NHANES III cohort to fasting participants aged 20 and over, while also requiring complete measurements for age and the 9 PhenoAge biomarkers and sufficient mortality follow-up. This refined cohort contains 9,358 participants, bringing it much closer to the original 9,926-person derivation sample: https://github.com/nopara73/PhenoAge2/commit/30227007fd130653a48625058c022c4592326025
Reproducing PhenoAge: the formula
To reproduce PhenoAge, I first converted the NHANES III biomarkers into the units expected by the original model.
The model then combines the 9 biomarkers and chronological age into a linear predictor:

This is converted into a mortality score,

and then into biological age in years:

That gives us the original PhenoAge baseline that PhenoAge 2.0 is designed to improve upon: https://github.com/nopara73/PhenoAge2/commit/81d2fbd5a3dfbe1b302e409ced4810d0423bad99
Primary evaluation metric
Previously I alluded to not setting the evaluation metric into stone just yet. Now let me explain why. Here I intentionally deviate from the original PhenoAge setup. PhenoAge was derived using a Gompertz mortality model, but for PhenoAge 2.0 I needed a fixed benchmark that could fairly compare many candidate formulas generated by Autoresearch. I therefore chose the C-index as the primary evaluation metric: https://github.com/nopara73/PhenoAge2/commit/d98b0cf3f51515b33c0affee8b70499a7943c050
The reason is simple: this is a survival problem. We care not only about whether aging-related death occurs, but also about when it occurs. The C-index captures exactly that by measuring whether the model ranks earlier aging-related deaths as higher risk. This makes it a better metric than plain ROC AUC, which ignores survival time, and a more neutral comparison metric than a Gompertz-specific likelihood, which would favor one modeling family over another. Both PhenoAge and PhenoAge 2.0 are therefore judged on the same held-out test set using the same survival-aware metric.
Imagine you randomly pick two people:
- One dies earlier from an aging-related cause.
- The other survives longer.
A good clock should rank the first person as riskier. That is basically what C-index measures: how often the model gets that ordering right.
So the intuition is:
- Gompertz asks the model to conform to a specific mathematical form for mortality.
- C-index is about whether the ranking is right.
Since a biological aging clock is a vanity abstraction of aging related mortality, the ranking is often the more fundamental question: who’s gonna die sooner, you or me?
Overfitting protection
For future training I hid 20% from Autoresearch. I’ll only reveal that for the final comparison. This is to protect against overfitting: https://github.com/nopara73/PhenoAge2/commit/fae363e3e44af35630925470dc08a8bc8ee4e93f
Press enter or click to view image in full size

As we can see PhenoAge gets the mortality-risk ordering right about 86% of the time, while only 75% if we strip chronological age from its inputs.
Autoresearch
Finally I got Autoresearch and fed to it what we just designed: https://github.com/nopara73/PhenoAge2/commit/72412417ee0c19e7b5265c652cd6b87d2368b949
Press enter or click to view image in full size

I wish I could say the path forward was a walk in the park from here, but it wasn’t. I spent 2 days and 200 dollars of AI tokens, just to roll back everything to this point and start over with a simple neural network. The baseline result here was already performing as well as pheno age did:
Press enter or click to view image in full size

starting model After a bunch of autoresearch runs and machine learning optimizations I am sad to report I only managed to improve 0.1749% on the original pheno age formula. I totally failed.
Breakthrough
But the real breakthrough came now. Since the simplest machine learning model basically resulted in the same C-index as Pheno Age’s algorithm did, we just found a general model to rapidly test different inputs to the formula! So that’s what I’m going to test next.
Firstly I removed the ones with low availability and generally uncommon ones that would be stupid to put into a biological aging clock and the following 57 contenders remained:
variable,common_name,non_missing_rows,total_rows,completeness
ACP,serum alpha carotene,16285,17286,0.9420918662501446
AMP,albumin,16162,17286,0.9349762813837788
APPSI,alkaline phosphatase (SI),16160,17286,0.934860580816846
ASPSI,Aspartate aminotransferase (U/L) (SI units),16162,17286,0.9349762813837788
ATPSI,"Alanine aminotransferase: (SI, U/L) (SI units)",16162,17286,0.9349762813837788
BCP,serum beta carotene,16285,17286,0.9420918662501446
BUP,blood urea nitrogen (BUN),16162,17286,0.9349762813837788
BXP,serum beta cryptoxanthin,16284,17286,0.9420340159666782
C1P,serum c-peptide,15783,17286,0.9130510239500174
C3PSI,"Serum bicarbonate: (SI, mmol/L) (SI units)",16160,17286,0.934860580816846
CAPSI,"Serum total calcium: (SI, mmol/L) (SI units)",16246,17286,0.9398357051949554
CEP,creatinine,16161,17286,0.9349184311003124
CLPSI,"Serum chloride: (SI, mmol/L) (SI units)",16162,17286,0.9349762813837788
CRP,C-reactive protein,16257,17286,0.9404720583130858
DWP,platelet distribution width,16356,17286,0.9461992363762582
FEP,serum iron,16468,17286,0.952678468124494
FOP,serum folate,16441,17286,0.9511165104709012
FRP,serum ferritin,16435,17286,0.950769408770103
GHP,glycohemoglobin / HbA1c-related measure,16558,17286,0.9578849936364688
GRP,granulocyte number,16131,17286,0.9331829225963209
GRPPCNT,granulocyte percent,16131,17286,0.9331829225963209
HDP,HDL cholesterol,16265,17286,0.9409348605808168
HGP,hemoglobin,16441,17286,0.9511165104709012
HTP,hematocrit,16439,17286,0.9510008099039684
I1P,"serum insulin, first draw",15744,17286,0.910794862894828
LDPSI,"Serum lactate dehydrogenase: (SI, U/L) (SI units)",16161,17286,0.9349184311003124
LMP,lymphocyte number,16439,17286,0.9510008099039684
LMPPCNT,lymphocyte percent,16439,17286,0.9510008099039684
LUP,serum lutein/zeaxanthin,16285,17286,0.9420918662501446
LYP,serum lycopene,16285,17286,0.9420918662501446
MCPSI,mean corpuscular hemoglobin (MCH),16439,17286,0.9510008099039684
MHP,mean corpuscular hemoglobin concentration (MCHC),16439,17286,0.9510008099039684
MOP,mononuclear number,16131,17286,0.9331829225963209
MOPPCNT,mononuclear percent,16131,17286,0.9331829225963209
MVPSI,mean corpuscular volume (MCV),16440,17286,0.9510586601874348
NAPSI,"Serum sodium: (SI, mmol/L) (SI units)",16162,17286,0.9349762813837788
PBP,lead,16600,17286,0.9603147055420572
PLP,platelet count,16437,17286,0.9508851093370356
PSP,serum phosphorus,16162,17286,0.9349762813837788
PVPSI,mean platelet volume (MPV),16441,17286,0.9511165104709012
PXP,serum transferrin saturation,16422,17286,0.95001735508504
RCP,RBC count,16439,17286,0.9510008099039684
RWP,red cell distribution width (RDW),16441,17286,0.9511165104709012
SCP,serum total calcium,16161,17286,0.9349184311003124
SEP,serum selenium,16070,17286,0.929654055304871
SGP,serum glucose,16158,17286,0.9347448802499132
SKPSI,"Serum potassium: (SI, mmol/L) (SI units)",16162,17286,0.9349762813837788
TBP,total bilirubin,16162,17286,0.9349762813837788
TCP,total cholesterol,16380,17286,0.9475876431794517
TGP,triglycerides,16344,17286,0.9455050329746616
TIP,serum tibc,16441,17286,0.9511165104709012
TPP,total protein,16162,17286,0.9349762813837788
UAP,uric acid,16162,17286,0.9349762813837788
VAP,serum vitamin a,16285,17286,0.9420918662501446
VCP,serum vitamin c,15818,17286,0.9150757838713408
VEP,serum vitamin e,16285,17286,0.9420918662501446
WCP,WBC count,16441,17286,0.9511165104709012 Our new cohort looks like this
Fasting pair: 10414 participants
- Strict all-fields-required pair: 3432 participants
- Fasting all-cause deaths: 4116
- Fasting aging-related deaths: 2930
- Strict all-cause deaths: 3432
- Strict aging-related deaths: 2450
Sanity check that our naive MLP is as performant on our new cohort as the pheno age formula is and was in the pheno age cohort:
- Naive MLP with age: 0.876725
- PhenoAge formula with age: 0.874749
Without age as input:
- Naive MLP without age: 0.766521
- PhenoAge formula without age: 0.735001
As we can see, on our new cohort it even slightly outperforms PhenoAge with the naive unoptimized machine learning. The reason is because pheno age was trained on the pheno age cohort of course. So that’s a good sign that other biomarker combinations will approximate information theoretical ceilings as well. Now to the real question:
Given the frozen NHANES III and C-index benchmark and the fixed naive MLP scorer, which biomarker subset gives the best survival-ranking performance under a fair, benchmark-safe comparison?
I still didn’t run this thing overnight, but the results are already excellent.
On the frozen NHANES III aging-related mortality benchmark, the strongest benchmark-safe searched subset I have actually locked and evaluated on the hidden test split is age plus bicarbonate, total calcium, HDL cholesterol, lutein/zeaxanthin, mean cell hemoglobin concentration, total protein, uric acid, vitamin C, and white blood cell count, with a held-out C-index of 0.8729. On the development benchmark, the original PhenoAge formula scores 0.874749, while the same 9 biomarkers run through the fixed naive MLP score 0.876725.
Chronological age alone reaches 0.858635, so the original 9 biomarkers add only about 1.6 to 1.8 c-index points beyond age by itself.
In the current public search log, I can already verify 6,640 with-age clocks within 0.01 c-index of PhenoAge and 8,921 no-age clocks that outperform ageless PhenoAge.
Best ageless clock found so far
10 biomarkers, no age:
- albumin
- blood urea nitrogen
- folate
- glycated hemoglobin
- lycopene
- mean cell volume
- lead
- red blood cell count
- red cell distribution width
- cholesterol
validation / finalist c-index: 0.810356
Compared to ageless PhenoAge:
- 0.810356–0.735001 = 0.075355
So the best ageless searched clock is ahead by about 7.5 c-index points (7.5% predictive power gain.)
“Non-inferior to PhenoAge” counts
If we define “non-inferior” as within 0.01 absolute c-index of PhenoAge-with-age, that threshold is: 0.874749–0.010000 = 0.864749
- 2 biomarkers + age: 19 clocks
- 3 biomarkers + age: 137 clocks
- 4 biomarkers + age: 289 clocks
- 5 biomarkers + age: 437 clocks
- 6 biomarkers + age: 510 clocks
- 7 biomarkers + age: 1007 clocks
- 8 biomarkers + age: 1644 clocks
- 9 biomarkers + age: 2597 clocks
Total with-age non-inferior clocks: 6640
Ageless clocks above ageless PhenoAge
Threshold: 0.735001
Current counts of unique no-age search clocks above that threshold, grouped by biomarker count:
- 3 biomarkers: 16
- 4 biomarkers: 209
- 5 biomarkers: 388
- 6 biomarkers: 521
- 7 biomarkers: 552
- 8 biomarkers: 1175
- 9 biomarkers: 2740
- 10 biomarkers: 3213
- 11 biomarkers: 15
- 12 biomarkers: 54
- 13 biomarkers: 35
- 14 biomarkers: 1
- 15 biomarkers: 2
Total no-age clocks above 0.735001: 8921
Best by size aging clocks
Press enter or click to view image in full size

Press enter or click to view image in full size

Final insight: the with age curve is pretty flat from 6 to 10 inputs.
Discussion
Cross validation
While I’ve done a bunch of cross validations within NHANESIII database due to development (and even within the development split there’s another split) and test splits + the pheno age vs the later cohort that I have. That’s a ToDo. Ideally we can cross validate a bunch of aging clocks and see if the results are similar. I strongly suspect they are. And if they are, then we can be confident that non-cross-validated aging clocks are also likely valid. This latter point is very important not from the point of view of this research but the possibility of coming up with scientifically valid aging clocks from other databases based on other inputs those don’t just have to be labs, but grip strength and stuff.
All cause vs aging related mortality
It’s easy to make the case for aging related mortality, but I want to throw out the idea that perhaps we could aim for all cause mortality instead? I’m not actually making a claim, I did not examine this topic in details, but there’s a chance we might find real signal with all cause mortality. For example the chances of an accident or dying from covid, which isn’t aging related mortality, are much higher if you’re in bad health. Perhaps we’re losing valuable signal by only focusing on aging related mortality and not on all cause mortality?
In fact if we do accept all cause mortality as a valid thing to optimize for then there might be databases open to us that doesn’t record the reason for dying. This is very important, because coming up with a high quality biological aging clock is limited by what databases we have. One can imagine a biological aging clock that requires stuff like DEXA, spirometry, VO2Max, and some labs, yet good luck finding a database for that. Or perhaps we can somehow patch together a biological aging clock validation with incomplete data? There must be some mathematics that does that. Or perhaps there’s a better proxy than waiting for people to die?
Surprising medical results
The real surprise is that nutrition / micronutrient / exposure markers seem much more competitive than the canonical PhenoAge story would make you expect.
- Vitamin C is everywhere in the with_age search. It is the best single add-on to age, and it keeps surviving into larger winners. That is a bit surprising because classic mortality-risk clocks usually emphasize inflammatory, renal, and hematologic markers more than micronutrients. It may be acting as a strong proxy for overall nutritional status, smoking, inflammation, frailty, or broader health behavior.
- Carotenoids / lutein / vitamin-style markers show up repeatedly. alpha carotene, lutein/zeaxanthin, and vitamin C appearing this strongly suggests the model is picking up a real “nutritional reserve / oxidative stress / healthy-user” axis that the standard PhenoAge biomarker set underweights.
- Lead showing up in strong subsets is medically striking. That is plausible, but still eyebrow-raising. It suggests environmental toxic burden may carry survival-ordering signal that survives even after other markers are included.
- Mean cell hemoglobin concentration and mean cell volume are more prominent than many people would expect. These red-cell indices are not usually the first biomarkers people reach for conceptually, but they may be summarizing chronic disease, nutrition, marrow status, inflammation, or hidden confounding structure surprisingly well.
- CRP is not dominating the best searched subsets, even though inflammation is central to aging biology and CRP is in PhenoAge.
- Creatinine is not central in the best with-age search winners, even though kidney function is a classic mortality predictor.
- Lymphocyte percent and alkaline phosphatase, both important in PhenoAge, are not standing out as essential in the newer best searched combinations.
The without_age winners are actually pretty coherent medically:
glycated hemoglobin, blood urea nitrogen, albumin, red cell distribution width, glucose, creatinine, cholesterol, folate, mean cell volume, lycopene/vitamin E, lead
That cluster looks like a mix of:
- metabolic dysfunction
- renal stress
- nutritional status
- hematologic stress
- toxic exposure
That is biologically believable.
Could it be a scientific breakthrough?
Up to this point well performing biological aging clocks required diligent work and scientific intuition on the selection of biomarkers going into the coefficient optimizing equations. Scientist had little room to choose which biomarkers they would like to see in their clocks, because testing them was too expensive.
A silly example would be my best performing 2 biomarker biological aging clock: it only requires vitamin C and age as it’s input and it already performs as well as PhenoAge does.
Let’s assume we consider this a huge win and publish a study with a headline “Vitamin C + Age predicts biological age as well as PhenoAge does.”
What’s really going on here? Does that mean Vitamin C is the new miracle longevity supplement? No. All this means is that people in the study who ate more healthy lived longer. You see the problem here?
With this intuition in mind we can see how the same class error is probably hiding within biological aging clocks that have many biomarkers for inputs.
Even worse, for epigenetic or non-blood based clocks where we really have no idea about the individual data points going into them as inputs. Consider the Zhang BLUP clock (2019) having 319,607 CpGs as input. We really have no idea about the quality of these inputs. Are our interventions reverse time and prevent the murder or just cleanup the fingerprints?
With the methodology described in this study we can now enable scientists to select robust input sets that are hard to cheat (be it intentionally or unintentionally) and not worry about subpar results, because rapidly generating well performing biological aging clocks from even just a decent set of biomarkers is now possible.
We need not to produce bullshit clocks anymore. (Bullshit is used here in the Harry Frankfurt’s technical sense of the word.)
Bottom line
Coming up with high performing biological aging clocks just got real easy. But this isn’t limited to biological aging clocks, that’s just the passion of the author. Basically any endpoint given a database can be worked on with the same methodology described here.
If you happen to be working on a biological aging clock, say hi to me at adam.ficsor73@gmail.com
No rights reserved. Feel free to use anything here (or in any other content I’ve ever created) even without contribution.
```

## 30. Forget Football: The Longevity World Cup Has Launched

- Source: Medium
- Link: https://nopara73.medium.com/forget-football-the-longevity-world-cup-has-launched-9ac5de9991df
- Notes: CC0/no-rights-reserved when exposed by Medium page/feed

```text
Forget Football: The Longevity World Cup Has Launched

nopara73

4 min read ·
Sep 16, 2025

--

1

Listen

Share

Like every kid, I had a dream. Mine was to play football at the highest level right alongside Del Piero, David Beckham and Ronaldo.
Each day began with me knocking on doors across the neighborhood, dragging kids out to the pitch to play till dark. Before long, the grownups gave our little gang a name: pályalakók — “the ones who live on the football field.”
As you’ve probably guessed, life had other plans. I became a software developer — about as far from a pro athlete as one can get. But even for those who make it, the game ends by their 30s. And then — a lifetime still left to live.
Every new man-made system I mastered was thrown away; the human body is the only system worth studying for life — Dave Pascoe, #9 on the Longevity World Cup

As I turned 30 I also started to experience aging. Not in the form of a health scare, but like a frog that’s slowly being boiled. I shifted my focus towards health and fitness again, but this time something was off. The same passion and motivation from my youth was nowhere to be found without the competition aspect of it. Sure there are competitions for old people, but winning in your age group is not the same. There’s a certain kind of sadness to it. Not just because nobody cares, but even those who do, experience a mix of pity and discomfort. “This isn’t a real competition.” But what if there were a game where getting older wasn’t a disadvantage, but an advantage? Wouldn’t that be enough to tip the scales, to reignite your old passion?
In my case, longevity is not ten years, not twenty years more. At that point you’re already old. That’s stupid. For me, it’s literally forever. The goal is to actually stop aging — Juan Robalino, #6 on the Longevity World Cup

Enter the world of longevity. The absurd attempt of overcoming aging itself. These oddballs actually believe the body can grow younger. Some even went all scientific about it and said “Belief isn’t enough! We need proof!” Unfortunately randomized controlled trials on this would take lifetimes. Literally. Backed into a corner they came up with a game changing concept: The Biological Aging Clock . These clocks are their ambitious attempts to measure how old you are biologically, not just how many birthdays you’ve had.
Could this innovation become the ultimate objective of a game — a new kind of sport? Meet the Longevity World Cup: where age is an advantage, because the bigger the gap between your chronological and biological age, the higher you climb the leaderboard. The older you are, the more years you can reverse.
Expect to live long. Set a clear goal: I want to live to 100, 120, or further. Then identify with the goal — you are a longevity athlete. Set your protocol and go for it, no matter what. No matter the weather, no matter how you feel in the morning, that’s the plan — Zdeñek Sipek, #2 on the Longevity World Cup

How It Works
This game isn’t played on grass or courts, but in your biology. The metric is your biological age. Each year a new season starts with a different biological aging clock. This ensures better, more scientifically accurate results over time. The first year this aging clock is the Phenotypic Age , developed by Morgan E. Levine et al. in 2018.
To calculate PhenoAge, nine biomarkers are necessary from a standard blood test. Think of Albumin, Creatinine, Glucose and such. After submitting your results with the accompanying proofs, you’re then ranked on the Longevity World Cup leaderboard.
Press enter or click to view image in full size

Longevity World Cup Leaderboard 2025 Public Beta Just like traditional sports, there are men’s, women’s, and open divisions. But unlike traditional sports, getting older is an advantage — it gives you more room to improve. Because of this we also divide participants into generational leagues, like Millennials or Baby Boomers.
Who’s Winning
At this point many of you are likely curious about who’s currently leading the pack. The name is Mike Lustgarten, PhD , a well-known scientist and researcher in the longevity space. He’s gained a cyberspace following for meticulously tracking his own biomarkers, diet, and lifestyle interventions and regularly posting his experiments and data online.
What’s Next
Longevity as a sport is in its infancy. A leaderboard is a start, but to make it an exciting game on pair with the likes of football, a lot more needs to be built. Perhaps as it grows, blood based biological aging clocks will only serve as qualifier rounds to more serious testing rounds. The top 100 athletes may be required VO2Max, body fat %, spirometry and whatnot. And perhaps even that could only serve as a qualifier to the finals of the world cup where the top 10 athletes are flown to a remote biohacker house in south east Asia with cameras, doctors, and cutting-edge test equipment to live together and compete for the title of being #1 in the frames of a yearly Netflix documentary series.
Humanity’s Final Competition
The stakes are high, as top athletes are receiving prize money — funded by your Bitcoin donations. But the stakes are even higher for humanity. If we can compete at beating aging, maybe we can inspire the breakthroughs that let everyone win. And who knows perhaps we can even put David Beckham back on the football field?
To participate, visit LongevityWorldCup.com and click PLAY THE GAME
Longevity World Cup 2025 - Leaderboard
Too old for your sport? Not this one! In the Longevity World Cup, you don't age out - you age in. Reverse your age and…

www.longevityworldcup.com
```

## 31. LWC25: Point System Update

- Source: Medium
- Link: https://nopara73.medium.com/lwc25-point-system-update-daa6c92120ba
- Notes: CC0/no-rights-reserved when exposed by Medium page/feed

```text
LWC25: Point System Update

nopara73

3 min read ·
Mar 29, 2025

--

Listen

Share

Update: this change has been rolled back, as well as partial result submissions were disqualified: https://nopara73.medium.com/lwc25-point-system-update-2-0-587316241c3c
The heart of every game is the point system. Changes to it should not be taken lightly. This article argues for the necessity of tweaking the rules of the 2025 Longevity World Cup to gracefully handle partial data submission.
In the past your best pheno age counted from 2025. Pheno age is calculated from a set of blood biomarkers. When you submitted 3 different results from three different points in time from the year 2025, the best pheno age counted in the final leaderboard. However, an unforeseen need has arisen for people to submit a partial set of biomarkers as updates to their previous submissions. To manage this, we decided to always take the best biomarker submitted throughout the year to calculate the final pheno age.
Why is this not ideal?

Bryan Johnson has been widely criticized for cherry picking his best biomarkers from the past two years instead of publishing the most recent ones. This can be interpreted as misleading as explained by Joseph Everett, creator of the prominent What I’ve Learned YouTube channel.
Why is this necessary?
The key difference with this Longevity World Cup rule adjustment is that it does not result in your best biomarkers to be hidden. In fact this is needed to achieve exactly the opposite.
To level the playing field between the more honest athletes: those who submit all their subsequent measurements even if some biomarkers are worse than in their initial set of biomarkers were and the more strategic submissions where someone only decides to submit new biomarkers if they are strictly better than they were in previous measurements.
This way the competition is made more fair.
What are the immediate implications?
Although there are only a handful of athletes yet with multiple submissions, the point system update did lead to a few changes among those who submitted multiple times.
On the front page, HealthOptimizers’ score is improved from -15.0 to -15.9 years. His ranking stayed unchanged.
Press enter or click to view image in full size

This also puts Alan V’s, who was the first ever athlete enrolled to the competition and held the first place for many months, moved up from #12 to #8.
Press enter or click to view image in full size

Finally, and perhaps most awkwardly, my own ranking has improved 9 places.
Press enter or click to view image in full size

I do recognize and want to point out the conflict of interest in this and I’ll commit to removing myself from the competition as soon as this endeavor gets more serious and it starts to become a problem to ensure the neutrality of the competition. But for now I’d just like to be part of the fun.
Summary
The updated rule for the 2025 Longevity World Cup modifies how PhenoAge is calculated for the leaderboard. Previously, only full biomarker panels were considered, and the best complete result from 2025 was used. From now on, the final PhenoAge will be calculated by selecting the best individual value for each biomarker across all submissions throughout the year, even if some submissions are partial. This allows for incremental updates and ensures that athletes who report regularly and transparently aren’t penalized compared to those who only submit selectively when results are optimal.
Press enter or click to view image in full size

For more information on the competition rules please refer to our Ruleset document .
Longevity World Cup 2025 - Leaderboard
Too old for your sport? Not this one! In the Longevity World Cup, you don't age out - you age in. Reverse your age and…

www.longevityworldcup.com
```

## 32. LWC25: Point System Update 2.0

- Source: Medium
- Link: https://nopara73.medium.com/lwc25-point-system-update-2-0-587316241c3c
- Notes: CC0/no-rights-reserved when exposed by Medium page/feed

```text
LWC25: Point System Update 2.0

nopara73

Aug 20, 2025

--

Listen

Share

The competition’s official launch is about to happen on Sept. 16, 2025, within a month. While it’s still in public beta testing period, based on your feedback we decided to disqualify partial and non-same day results. From now on the lowest pheno age is taken across all submissions. Hopefully this will be the last time the point system must be modified.
To learn more about the dilemmas involved, refer to a previous point system update, which this rolls back: https://nopara73.medium.com/lwc25-point-system-update-daa6c92120ba
```

## 33. Longevity World Cup Season 2: The Game Evolves

- Source: Medium
- Link: https://nopara73.medium.com/longevity-world-cup-season-2-the-game-evolves-dcdf6b56f6ea
- Notes: CC0/no-rights-reserved when exposed by Medium page/feed

```text
Longevity World Cup Season 2: The Game Evolves

nopara73

2 min read ·
Feb 22, 2026

--

Listen

Share

It turns out a dedicated few, acting like athletes of their own biology , could measurably reverse their age on a public scoreboard. But a proof of concept is just that. A starting point. The real game starts now.
Press enter or click to view image in full size

In February 2026, we launched Season 2. And we changed the entire format.
First, we split the competition into two tracks: Amateur and Professional. Not everyone is ready to go to another country for a specific blood test. But everyone can start tracking. The Amateur league keeps the game accessible. The Pro league is for the obsessed.
Second, we’re rotating the clocks. PhenoAge , the clock from Season 1, isn’t gone. It’s now the “all-time” leaderboard. It’s the historical record. But for the 2026 Pro season, we’re using a new clock: Bortz . Why?
The science evolves, and so must the game.

Sticking to one clock forever is how you get gamed systems and scientific stagnation. By rotating clocks, we force ourselves to get robust, not just good at passing one test. We’re now a multi-clock competition.
This is more than a rules update. It’s a statement.
We are building a sport where the metrics get harder, the competition gets fiercer, and the prize is time itself. Season 1 was about proving it’s possible. Season 2 is about making it a sport.
Let the games begin.
Longevity World Cup | Reverse Your Biological Age
Too old for your sport? Not this one. Join the Longevity World Cup and rise on the leaderboard by improving your…

www.longevityworldcup.com
```

## 34. Two Insights Regarding the Development of Biological Aging Clocks

- Source: Medium
- Link: https://nopara73.medium.com/two-insights-regarding-the-development-of-biological-aging-clocks-121fc4f29f8a
- Notes: CC0/no-rights-reserved when exposed by Medium page/feed

```text
Two Insights Regarding the Development of Biological Aging Clocks

nopara73

5 min read ·
May 23, 2026

--

Listen

Share

You measure your weight. You do a physical assessment. You get a blood test. Based on the results you can work on these biomarkers to get them into the optimal range.
Press enter or click to view image in full size

Ultimately we lack a universal metric that would give us a general assessment of our overall health. Enter biological aging clocks:
There are blood-based clocks like PhenoAge , or Bortz , epigenetic clocks like Horvath and GrimAge , pace-of-aging epigenetic clocks like DunedinPACE , and newer proteomic , metabolomic , and transcriptomic clocks.
A brief history of biological aging clocks
Earlier attempts of biological aging clocks took a big database (like NHANES or UK Biobank ,) selected some biomarkers and from them they yielded us a biological age . If your biological age was 35, people in the database with similar biomarkers to yours tended to be 35 year old.
The next breakthrough came from the realization what’s most useful about these clocks are this: they provide a universal metric that would give us a general assessment of our overall health. The problem reformulated this way led us to come up with mortality based clocks. People dying is the hardest health outcome there is. Your chances of dying can then be converted into a biological age.
Why biological aging clocks are useful?
Let’s say I share with client of mine some of his biomarkers, and explain which are inside and outside of the optimal ranges, they will perfectly understand it.
But what if I also calculate a biological age from those biomarkers and I tell them them that: their biological age is 35. They suddenly have a whole new level of understanding, because they have encountered other human beings of all ages before. The pre-existing context they can call upon to really understand the meaning of that number is much greater than the original set of biomarkers were.
The great debate of chronological age
Much blood has been spilled on the battlefield of including chronological age as an input in biological aging clock algorithms. I myself strongly stand on the no-ager side of it, so it’s only fair if I present the pro-ager side of the argument as well as I can first:
One of the greatest question is: when will you die? I can give you decent prediction based on how old you are and what gender. Perhaps some exercise, smoking status or ethnicity can also help. These mortality clocks are great, but can we do better than them? Turns out we can! Tell me your chronological age, give me your bloodwork and I’ll do just that.
Or more precisely: a biological aging algorithm that also contains your chronological age as an input can do that.
This makes perfect sense, but I think we got way too caught up on answering the wrong question.
Press enter or click to view image in full size

For a long time I couldn’t quite make a coherent argument against it, other than “this fucking smells” when I’m talking to myself and “chronological age as an input into a biological aging clock algorithm is cheating” when I’m talking to someone else.
The day has finally came that I claim to have it all figured out!
Insight 1: Causality
Of course I want to tell you when you’re going to die, but it’d be so much better you can do something about it. If I predict your death from biomarkers you can change and chronological age, then there’s nothing you can do anything about it.
In a previous article of mine I presented “immutability” as the key property we’re missing at biomarker selection into biological aging clocks:
Brute Forcing Biological Aging Clocks — AI Breakthrough in Longevity Research
My work on the Longevity World Cup led me to implement a number of biological aging clock algorithms, including…

nopara73.medium.com

I was wrong: let’s take serum lycopene for instance. In that investigation I’ve seen this biomarker come up over and over again as an excellent contributor to longevity. This baffled me. Of course it’s important to have good lycopene levels, but how the heck can it be even more important than something like glucose? The solution will shred light to why causality is the goat. The property I’ve been missing: lycopene correlates to a good diet. People who have good lycopene levels in the database, had it because they generally ate healthily. They didn’t outlive the good glucose cohort because of lycopene. They did it because of diet. Lower glucose causes good health. Higher lycopene is a symptom of good health. Therefore I’m better off working on my glucose levels than on my lycopene levels:
In fact less casual biomarkers give us worse mortality predictions if we are explicitly working on them. Therefore we’ll want to build biological aging clocks from more causal biomarkers.
What’s the deal with chronological age though? Notice it is the ultimate least causal biomarker there is: measurement of time causes no ill health.
We need biological aging clocks from highly causal biomarkers.
Insight 2: Database Shortage
What are the most causal biomarkers though? A plausible set would be visceral fat, VO2Max and Apolipoprotein B. Great, now we just have to find a database that contains all three, along with decades of follow-up data on when these people died.
And there the problem lies. There exists no such database.
The XPRIZE Healthspan is a $101 million competition that has not chosen biological aging clocks as its endpoints, but muscle function, cognitive function, and immune function. Biological aging clocks are just not good enough.
Conclusion
Is this a fundamental flaw in the pursuit of biological aging clocks? Can we never have a universal metric that would give us a general assessment of our overall health? Can we never have clocks from the most causal set of biomarkers?
Firstly, understanding the importance of causality at biomarker selection into biological aging clocks will yield us better clocks, despite their survival indexes and other hard endpoint we use to validate these clocks will be lower.
Secondly, while the prospects look grim, I have faith that the war is not yet lost on ultimate aging clocks. There must exist an elegant mathematical trickery that lets us use the highest quality, most causal inputs and build a rigorous universal metric that would give us a general assessment of our overall health.
```

## 35. Why Isn’t Bryan Johnson Joining The Longevity World Cup?

- Source: Medium
- Link: https://nopara73.medium.com/why-isnt-bryan-johnson-joining-the-longevity-world-cup-521758472a97
- Notes: CC0/no-rights-reserved when exposed by Medium page/feed

```text
Why Isn’t Bryan Johnson Joining The Longevity World Cup?

nopara73

6 min read ·
Aug 10, 2025

--

Listen

Share

Press enter or click to view image in full size

Bryan Johnson Bryan Johnson, an American half-billionaire, sold his tech startup and embarked on a glorious mission of doing a series expensive longevity experiments on himself. Doing so he became the most prominent longevity influencer of our times. He ultimately popularized the idea of longevity competitions and today he identifies as a longevity athlete. For various philosophical reasons , I personally found these concepts revolutionary and ran with it. Two years ago, I started out by interviewing longevity athletes , then, because of some frustration about the lack of development of existing competitions, a year ago I started my own competition: The Longevity World Cup .
By now it has became the most advanced competition in the space, attracting every other prominent longevity athletes, but to my surprise not Bryan. I often wondered, as well as being asked the question: why is that? How come the person who basically created the field and his entire identity is tied to it has chosen to not compete in the best developed competition in the space?
Press enter or click to view image in full size

Longevity World Cup Possibilities
For a while I believed it’s just because he haven’t heard of it, but it has been 8 months since the competition is live, it already has a decent online presence and has already attracted just shy of a hundred athletes. Unless he is living in a cave and it has no murals of the topic of his expertise , he must have heard about it by now. A few months ago even an official invitation has been sent to him on multiple channels. So this is an unlikely explanation.
Could be that he just can’t be bothered to apply? That’s another unplausible theory considering someone who tied his own identity in being a longevity athlete and regularly boosts about his standing on the Rejuvenation Olympics , the other major competition.
Then it could be a problem with the methodology. Perhaps Bryan has an issue with how the competition ranks? Perhaps the biological aging clock used on the Longevity World Cup is too unscientific to his taste? However in its current season LWC uses traditional blood tests for biological aging clock calculation and experts generally consider this to be a more reliable method than home test kits are, which the other competition he competes in uses. (Note I might have a bone to pick on this point, but I digress.)
And that leads me to what prompted writing this article in the first place. I just received an email from David X , a new applicant to the competition, in which he claims he calculated Bryan’s Phenotypic Age based on his latest published blood test and the results surprised me.
Press enter or click to view image in full size

According to David’s calculations the difference between Bryan’s chronological and biological age is -6.67 years. If that’s true, that’d put Bryan to 70th place on the Longevity World Cup out of 86 competitors. Could this be the reason why he’s not rushing to compete? Would such a ranking jeopardize his public image, so he’d rather opt to not partake at all?
Verifying the Numbers
First things first, it’s quite hard to believe, so I need to verify these numbers.
Before that, I must note I tried to calculate his pheno age in the past, but I couldn’t find the data. At one point Dave Pascoe has claimed to calculate his pheno age, but I couldn’t dig up the data he worked on later on. But this time he finally published it:

He asked people to challenge him on his claim about the “World’s Best Biomarkers.” While that’s not the main purpose of this article, I suppose it can serve as an acceptance of that challenge as well.
What follows is the relevant pages of Bryan’s test results. Sidenote: I did not intentionally blur them, he uploaded it blurred to begin with. Nevertheless it’s still decipherable.
Press enter or click to view image in full size

Press enter or click to view image in full size

Press enter or click to view image in full size

Here are the pheno age relevant biomarkers collected out nicely.
- RDW : 12.1 %
- Glucose : 91 mg/dL
- hsCRP : 0.3 mg/L
- Creatinine : 1.21 mg/dL
- WBC : 3.1 × 1⁰³ cells/μL
- MCV : 101 fL
- Lymphocytes : 38 %
- Albumin : 4.5 g/dL
- ALP : 77 U/L
Next I plugged them into the LWC pheno age calculator — which is currently the most accurate one on the Internet due to a bug and an issue discovered and addressed with other calculators.)

He’s 47.0 years old, subtract that from 39.3 pheno age, which leaves us with -7.7 years.
As you can see David made a mistake reading the glucose number, as well as he used 47.5yo instead of 47.0yo, which explains the difference.
-7.7 years on the Longevity World Cup puts Bryan Johnson near the bottom, to 64th place out of 86 competitors : just right below you never fucking guess who! Me :)
Press enter or click to view image in full size

And that reveals my ulterior motive of even bothering writing this article: the bragging rights of beating Bryan Johnson in longevity🚀
The Real Reason Why Bryan Isn’t Competing
Flexing aside where does that leave us?
It leaves us staring at the uncomfortable reality that when the scoreboard doesn’t favor you, you’re tempted to not show up at all. Bryan has built a brand on being ahead of the pack and stepping into an arena where his ranking could appear mediocre might feel like unnecessary risk.
But that’s precisely why the greats in any field earn their respect — by showing up even when the outcome is uncertain. If we’re to make a sport out of longevity, the real test isn’t whether you can post your best numbers in a carefully curated way; it’s whether you’re willing to put them on the same scoreboard as everyone else and let the chips fall where they may .
It doesn’t matter whether you win or lose, what matters is that you play the game.
Longevity World Cup 2025 - Leaderboard
Too old for your sport? Not this one! In the Longevity World Cup, you don't age out - you age in. Reverse your age and…

www.longevityworldcup.com
```

## 36. longevity world cup 2025 crowdsourced

- Source: Rapamycin.news
- Link: https://www.rapamycin.news/t/longevity-world-cup-2025-crowdsourced/20646/2
- Date: 2025-07-15T12:08:48.782Z
- Notes: Discourse user activity

```text
Games indeed have the power to bring resources to the field of Longevity like nothing else. Creator of LWC here.

I started this project, because I am a huge fan of the Rejuvenation Olympics ( for deeper philosophical reasons ) but unfortunately I wasn’t able to properly get involved with that project and since I’m a seasoned software developer, I couldn’t unsee how much better that website can be built. So I launched my own competition.

But it’s not about me. I’m not any kind of software developer, but the FOSS kind: I write Free and Open Source Software for life! Thus the Longevity World Cup is also fully open source: GitHub - nopara73/LongevityWorldCup

All this is to say I’d love to have code contributions to the project, or you can even fork it and launch your own competition, I’ll be fully supporting you! If you have questions or suggestions about the structure of LWC, I’ll be also here to assist!

P.S.: wow what a great forum you have here!
```

## 37. longevity world cup 2025 crowdsourced

- Source: Rapamycin.news
- Link: https://www.rapamycin.news/t/longevity-world-cup-2025-crowdsourced/20646/3
- Date: 2025-07-15T12:09:56.094Z
- Notes: Discourse user activity

```text
Ohh I forgot to say you’re welcome to join the competition as well!

longevityworldcup.com

Longevity World Cup 2025 - Leaderboard

Too old for your sport? Not this one! In the Longevity World Cup, you don't age out - you age in. Reverse your age and rise on the leaderboard!
```

## 38. longevity world cup 2025 crowdsourced

- Source: Rapamycin.news
- Link: https://www.rapamycin.news/t/longevity-world-cup-2025-crowdsourced/20646/10
- Date: 2025-07-16T09:07:32.438Z
- Notes: Discourse user activity

```text
You can also take a look at the code: LongevityWorldCup/LongevityWorldCup.Website/wwwroot/js/pheno-age.js at master · nopara73/LongevityWorldCup · GitHub

Credit goes for Andrew Steele for the original code: GitHub - ajsteele/bioage: Simple HTML forms using JavaScript to calculate estimates of biological age.
```

## 39. longevity world cup 2025 crowdsourced

- Source: Rapamycin.news
- Link: https://www.rapamycin.news/t/longevity-world-cup-2025-crowdsourced/20646/11
- Date: 2025-07-16T09:09:19.767Z
- Notes: Discourse user activity

```text
Correct. TLDR:

Albumin: No upper cap

Creatinine: Lower cap: 44 µmol/L

Glucose: Lower cap: 4.44 mmol/L

CRP: No lower cap

White Blood Cell Count (WBC): Lower cap: 3.5 × 1000 cells/µL

Lymphocytes: Upper cap: 60 %

Mean Corpuscular Volume (MCV): No lower cap

Red Cell Distribution Width (RDW): Lower cap: 11.4 %

Alkaline Phosphatase (AP): No lower cap
```

## 40. longevity world cup 2025 crowdsourced

- Source: Rapamycin.news
- Link: https://www.rapamycin.news/t/longevity-world-cup-2025-crowdsourced/20646/12
- Date: 2025-07-16T09:13:08.490Z
- Notes: Discourse user activity

```text
FTR I setup the competition so that it restarts every year with a different biological aging clock. As the field progresses we should have better and better ones.

Edit: turns out I can only make 3 replies per topic as a new user, so I was suggested to edit existing replies instead. So I’ll add my reply to @Steve_Combi ’s post ( Longevity World Cup 2025 (Crowdsourced) - #4 by Steve_Combi ) here:

Definitely. I originally setup the competition so every year a different biological aging clock will be used. I don’t expect us selecting another traditional lab based aging clock going forward. Although there are things to be said about their reliability, their lack of standardization introduces too much noise.

The larger vision is to delegate these epigenetic aging clocks to serve as qualifiers rather than to be the competition themselves. Then a second phase aof the competition could start where only the top 100 would be eligible to compete in and that’d require more rigorous measurements.

If things go really well financially for the competition, perhaps we could even fly the top 10 to a biohacking house in some parts of the world. Setup a bunch of cameras and get an army of doctors to measure them and release a Netflix episode or sth
```

## 41. longevity world cup 2025 crowdsourced

- Source: Rapamycin.news
- Link: https://www.rapamycin.news/t/longevity-world-cup-2025-crowdsourced/20646/14
- Date: 2025-07-17T07:55:34.466Z
- Notes: Discourse user activity

```text
Yes, right now the competition can be gamed in many ways.

As we discussed, from next year by switching to epigenetic tests we’re going to eliminate many of such ways.

Furthermore if the competition is successful, I’ll likely be able to convince the company providing the tests to cryptographically sign their reports, so we can validate they were not altered.

That being said it’ll still not be perfect as that’ll open the possibility for a person to still send in someone else’s blood to the lab, but overall it’ll still reduce the wiggle room.

There are other softwer measures being utilized here. At signup one must provide their picture and agree to do a video interview. Not sure how much this helps, but one’s affinity of cheating is reduced when they have to do it publicly.

If cheating is noticed among top competitors I also have in mind of making the competition invite only. I’d prefer to not do that, but until proper solutions are found this should suffice to ensure survival.

Ultimately the multi phase system will provide a final solution. Unless the athletes are being tested in person by the organizers some form of cheating will still be a possibility. I’m hopeful we’ll be able to eventually get there, as pointed out in my previous reply.

BTW I’m now a memeber and I can post more than 3 comments wooho!
```

## 42. longevity world cup 2025 crowdsourced

- Source: Rapamycin.news
- Link: https://www.rapamycin.news/t/longevity-world-cup-2025-crowdsourced/20646/16
- Date: 2025-07-18T06:17:27.914Z
- Notes: Discourse user activity

```text
Right, that was an uncomfortable thing to realize. I worked a lot on this issue. Ultimately I commissioned a research on all cause mortality regarding the specific biomarkers and patched the formula by capping them.

Read more about it here: PhenoAge Calculation Bug Disclosure: Missing U-Shaped Curves for Biomarkers · Issue #136 · nopara73/LongevityWorldCup · GitHub
```

## 43. longevity world cup 2025 crowdsourced

- Source: Rapamycin.news
- Link: https://www.rapamycin.news/t/longevity-world-cup-2025-crowdsourced/20646/22
- Date: 2025-08-14T05:36:01.159Z
- Notes: Discourse user activity

```text
Yes it’s easier to game LWC. I hope eventually it becomes big enough so that I can convince the company providing the test to cryptographically sign their output, so at least this editing concern is gone.

(Note phenotypic age only plays in 2025 season, the aging clock used changes every year.)
```

## 44. longevity world cup 2025 crowdsourced

- Source: Rapamycin.news
- Link: https://www.rapamycin.news/t/longevity-world-cup-2025-crowdsourced/20646/24
- Date: 2025-08-15T06:08:32.859Z
- Notes: Discourse user activity

```text
This forum keeps surprising me. BTW you were not only #1 , but also the very first athlete ever : )

I hope you noticed you got the “Athlete Zero” badge? Hah
```

## 45. longevity world cup 2025 crowdsourced

- Source: Rapamycin.news
- Link: https://www.rapamycin.news/t/longevity-world-cup-2025-crowdsourced/20646/26
- Date: 2025-08-16T06:49:51.462Z
- Notes: Discourse user activity

```text
Dec 31 last blood sample to be taken and tested, you can still submit till around mid January because of that. BTW note I’m planning to disqualify partial and non-same day result, which might affect you slightly. As you recall it’s now best biomarker wins, but that turned out to be a bad decision based on the feedback that I received.
```

## 46. longevity world cup 2025 crowdsourced

- Source: Rapamycin.news
- Link: https://www.rapamycin.news/t/longevity-world-cup-2025-crowdsourced/20646/29
- Date: 2025-08-17T06:09:06.846Z
- Notes: Discourse user activity

```text
Surely the current season’s biological aging clock is imperfect and probably all future season’s clocks will be in some way, but if this is stupid then what does that tell you about throwing a ball into a basket?
```

## 47. longevity world cup 2025 crowdsourced

- Source: Rapamycin.news
- Link: https://www.rapamycin.news/t/longevity-world-cup-2025-crowdsourced/20646/32
- Date: 2025-08-17T13:43:08.623Z
- Notes: Discourse user activity

```text
For now. It’s still day one
```

## 48. rapamycin and acarbose effect on bone health

- Source: Rapamycin.news
- Link: https://www.rapamycin.news/t/rapamycin-and-acarbose-effect-on-bone-health/21075/40
- Date: 2025-08-25T06:58:01.407Z
- Notes: Discourse user activity

```text
Nice. Also make sure to double check it with the LWC calculator as well. Although it was originally a fork of Steele’s calculator, it added sanity checks to handle missing U-curves from pheno age. See my correspondence with Steele on GitHub about this: PhenoAge Calculation Bug Disclosure: Missing U-Shaped Curves for Biomarkers · Issue #2 · ajsteele/bioage · GitHub
```

## 49. longevity world cup 2025 crowdsourced

- Source: Rapamycin.news
- Link: https://www.rapamycin.news/t/longevity-world-cup-2025-crowdsourced/20646/35
- Date: 2026-01-20T06:36:59.130Z
- Notes: Discourse user activity

```text
Article by Klaus Townsend on longevity competitions

Klaus Townsend – 18 Jan 26

Longevity World Cup vs Rejuvenation Olympics + Biological Aging Clocks -...

The inaugural season of the Longevity World Cup (LWC) has officially concluded, and huge congratulations to ⁨Mike Lustgarten for taking first place overall, and Bryan Johnson for his first place in the Rejuvenation Olympics! Let's celebrate the...

Est. reading time: 10 minutes
```

## 50. longevity world cup 2025 crowdsourced

- Source: Rapamycin.news
- Link: https://www.rapamycin.news/t/longevity-world-cup-2025-crowdsourced/20646/39
- Date: 2026-03-23T05:46:47.973Z
- Notes: Discourse user activity

```text
I have reproduced pheno age’s training cohort (from NHANESIII) and calculated the people with the best pheno ages in them.

The person with the best pheno age: -13.9 years would score at the 50th place on the Longevity World Cup.

image 1167×232 44.2 KB
```

## 51. longevity world cup 2025 crowdsourced

- Source: Rapamycin.news
- Link: https://www.rapamycin.news/t/longevity-world-cup-2025-crowdsourced/20646/42
- Date: 2026-06-13T12:09:48.810Z
- Notes: Discourse user activity

```text
Siim’s entire life is online, so I’d be really surprised if that’d be the case: https://www.youtube.com/@SiimLand
```

## 52. ama im nopara73 creator of the opensource

- Source: Reddit comments
- Link: https://www.reddit.com/r/QuantifiedSelf/comments/1nk58y2/ama_im_nopara73_creator_of_the_opensource/nfk585k/
- Notes: r/QuantifiedSelf

```text
Title: ama im nopara73 creator of the opensource

Yes, biological aging clocks are not very accurate right now. We're still in day 1.

It is possible to game the system in many ways. We'll be continuously improving upon it. From next year on, we'll do only home test kits, which will harden the work of cheaters. There are however soft disincentives of lying about your results, which is requiring public facing and communication from the athletes.

For starter everyone must have their own photos. People are less likely to cheat with their face on it. Furthermore top athletes are required to give interviews, which makes lying less likely.

I also started doing 15 minute private interviews on higher levels before accepting someone on the board in which I ask them to show documents they cannot fake on the spot.

Ultimately, not unlike doping in traditional sports, it's gonna be a forever battle until the competition grows large enough to be able to conduct in person games.
```

## 53. ama im nopara73 creator of the opensource

- Source: Reddit comments
- Link: https://www.reddit.com/r/QuantifiedSelf/comments/1nk58y2/ama_im_nopara73_creator_of_the_opensource/nfdzsds/
- Notes: r/QuantifiedSelf

```text
Title: ama im nopara73 creator of the opensource

There is a growing scientific endeavor in trying to quantify someone's biological age as opposed to their chronological age.

To get the intuition, the classic example of cryopreserving a 30yo person for 100 years, in which case the person is chronologically 130 years old, but biologically just 30.

This is very important in geroscience aka aging research, because studies measuring how long humans live take lifetimes.

The idea of the longevity world cup is that as scientist come up with better and better biological aging clocks we can organize competitions around them and see what we learn from those who manage to "reverse their age" the most.
```

## 54. ama im nopara73 creator of the opensource

- Source: Reddit comments
- Link: https://www.reddit.com/r/Biohackers/comments/1nk6215/ama_im_nopara73_creator_of_the_opensource/nf1sbia/
- Notes: r/Biohackers

```text
Title: ama im nopara73 creator of the opensource

Looks is quite an interesting "biomarker." Since the guess my age game was implemented I was wondering how could it be done in a serious way. We need 2 things here:

- Each athlete's presentation must be standardized somehow, so they cannot mess with the lights or photoshop or makeup.

- The judges who vote should be unbiased to the extent that they should not know about the athletes at all.
```

## 55. ama im nopara73 creator of the opensource

- Source: Reddit comments
- Link: https://www.reddit.com/r/Biohackers/comments/1nk6215/ama_im_nopara73_creator_of_the_opensource/nf14pkx/
- Notes: r/Biohackers

```text
Title: ama im nopara73 creator of the opensource

Interesting. Since I implemented that little guess my age game I was wondering how that could work without being able to game the system. What we need is

(1) for everyone to present themselves under the same conditions

(2) for the judges to be fully unbiased (they should not know about the athletes at all)
```

## 56. ama im nopara73 creator of the opensource

- Source: Reddit comments
- Link: https://www.reddit.com/r/Biohackers/comments/1nk6215/ama_im_nopara73_creator_of_the_opensource/nex5vou/
- Notes: r/Biohackers

```text
Title: ama im nopara73 creator of the opensource

It does seem Alan V. aka Athlete Zero is everywhere :)
```

## 57. ama im nopara73 creator of the opensource

- Source: Reddit comments
- Link: https://www.reddit.com/r/blueprint_/comments/1nk58ue/ama_im_nopara73_creator_of_the_opensource/newioch/
- Notes: r/blueprint_

```text
Title: ama im nopara73 creator of the opensource

There are supplements that bodybuilders take. There are supplements that spiritual folks take. There are longevity supplements. There're the supplements from the beauty industry and all these subcultures.

The thing about longevity athletes is that they are the most well rounded people in the world. So they end up taking all the supplements from all the subcultures. Unfortunately my stack has inflated to near unmanageable numbers, but the core of it is multivitamin, fish oils, vitamin D and creatine. On top of that there's like 10 other supplements I believe in loosely. And on top of that there's like 50 other supplements that I'm experimenting with in any given day.

Funny story, I'm not sure about my current pace of aging, because I've submitted a DunedinPACE test and a TruHealth test at the same day with the same blood. The DunedinPACE came back. They have however made a mistake for the first TruHealth cohort so they upgraded it to do TruAge, which includes DunedinPACE and they came back as different numbers, despite coming from the exact same blood: 1.09 vs 0.96. To add more mystery somehow on RO, a 0.76 best pace appeared after my latest TruHealth test, but I don't recall ever receiving this in a report.

Regarding my pheno age, it's 3 years under my calendar age: https://www.longevityworldcup.com/athlete/nopara73

I'm by no mean close to any of the greats like Mike or Dave, but I lost 15kg the last year, so eventually I'll get there :P
```

## 58. ama im nopara73 creator of the opensource

- Source: Reddit comments
- Link: https://www.reddit.com/r/IAmA/comments/1nk58lm/ama_im_nopara73_creator_of_the_opensource/newdf3s/
- Notes: r/IAmA

```text
Title: ama im nopara73 creator of the opensource

Athletes compete against each other in "reversing their biological age." The larger the gap between their chronological and biological age is the better they rank.

The competition is not currently funded other than the prize money, which is coming from donations. It's my passion project and have been coding it for a year now. I do foresee many ways to monetize it though. Currently my best thinking is that I'll try to find a big sponsor for a year. If anyone's interested for 2026, DM me!

How long are you planning for?

Speaking "grifter-coded" language.. forever! No, seriously. Games are quite powerful tools. Game designers create your agency and your goals and then you pursue those until you put a ball in a basket or bash the head of the other person with your gloves while millions are cheering on you. I'm talking of boxing or any fighting sports for that matter. It's absolute madness, but it's the power of games. Now the question arises: can we apply this to aging itself? If we make a sport out of aging, can the motivational power of games be the final catalyst for us to get to longevity escape velocity? I believe so and I'm about to test this theory.

Is this an intentional stylistic choice?

I'm afraid not. I'm really this delusional :)

What do you see as key future milestones?

I created a Milestones doc a year ago.

We've achieved

- Creation of the main website

- Acquiring the first 10 athletes

- Launching the website officially (coming out of early access)

- Reaching the first 100 athletes

What's coming now is to finish up the first season at the end of the year, launch the next one, and find a monetization model to make the project sustainable.

On a longer timeframe we're also experimenting with a 1v1 format and trying to eventually bring together the top athletes to an in person world cup and create a documentary out of it. These guys are superhumans.
```

## 59. ama im nopara73 creator of the opensource

- Source: Reddit comments
- Link: https://www.reddit.com/r/blueprint_/comments/1nk58ue/ama_im_nopara73_creator_of_the_opensource/new93ym/
- Notes: r/blueprint_

```text
Title: ama im nopara73 creator of the opensource

C# is because that's what I'm expert in. Site's ugly now, young later.

On DunedinPACE vs PhenoAge. Interestingly at the time I made this decision while I believed DunedinPACE is a better metric, so using PhenoAge had nothing to do with clock quality, rather with another reason: it's the most popular clock.

But also DunedinPACE was categorically excluded, because of its output, which is a rate: speed of aging is a different concept from biological age, which has a huge consequence in the longevity athletes it yields:

- The difference between chronological age and biological age yields a result that favors chronologically older people, because they have more years to "reverse."

- In contrast a speed of aging competition favors the best athletes in absolute terms, which makes it more similar to today's sport competitions where you age out of.

The most interesting thing about ranking based on biological age is that it naturally complements all other competitive sports: age becomes an advantage, instead of the harbinger of the end of your sport career.
```

## 60. anyone seen a better whoop age than this

- Source: Reddit comments
- Link: https://www.reddit.com/r/whoop/comments/1n4vtwl/anyone_seen_a_better_whoop_age_than_this/nbsnvx1/
- Notes: r/whoop

```text
Title: anyone seen a better whoop age than this

Interesting to see that pheno ages reductions on LWC are similar to whoop age reduction numbers reported in this thread
```

## 61. bodybuilding longevity and the philosophy of games

- Source: Reddit comments
- Link: https://www.reddit.com/r/Biohackers/comments/1b4l875/bodybuilding_longevity_and_the_philosophy_of_games/kt4iu6b/
- Notes: r/Biohackers

```text
Title: bodybuilding longevity and the philosophy of games

I'm an evil capitalist. If I want to advertise, I can do much better than a short announcement at the end of a long, high-quality essay. For example, I could copypaste that part into this comment, like this:

This philosophical article serves as a way to announce my plans to interview contenders for the Rejuvenation Olympics. At least the ones who are willing to talk to me. Subscribe to my channel if you’re interested: https://www.youtube.com/@nopara73/
```

## 62. deleted by user

- Source: Reddit comments
- Link: https://www.reddit.com/r/blueprint_/comments/1o8kpzv/deleted_by_user/njxq0ih/
- Notes: r/blueprint_

```text
Title: deleted by user

I interviewed her a few months ago. She's doing all the basics it seems. Kinda like Julie Gibson Clark. BTW on Sunday I'm dropping a panel conversation drops between the two of them!
```

## 63. deleted by user

- Source: Reddit comments
- Link: https://www.reddit.com/r/blueprint_/comments/1j3qpfj/deleted_by_user/mg4zjo3/
- Notes: r/blueprint_

```text
Title: deleted by user

The old rules had 2 leaderboards. One is the relative, the other is the absolute. I'm pretty sure he would have won on the relative leaderboard with this. However on the absolute he would had no chance, because he's relatively young.

See Rejuvenation Olympics 2024 Update Explained
```

## 64. deleted by user

- Source: Reddit comments
- Link: https://www.reddit.com/r/longevity_protocol/comments/1ewvhoz/deleted_by_user/lj5yc9q/
- Notes: r/longevity_protocol

```text
Title: deleted by user

Congrats! Keep up the good leaderboarding! :)

P.S.: u/Same-Potential7413 DM me if you want come on my Rejuvenation Olympics podcast and talk about it? https://www.youtube.com/playlist?list=PL4nqc85w185sO4i7eR3oUO_lMmlJ2K1cL
```

## 65. deleted by user

- Source: Reddit comments
- Link: https://www.reddit.com/r/blueprint_/comments/1eman47/deleted_by_user/lh2s3x3/
- Notes: r/blueprint_

```text
Title: deleted by user

Now, I've been thinking myself of this for quite a while.

Firstly, we must note, before the update a month ago, there were 2 leaderboards. (See Rejuvenation Olympics 2024 Update Explained for more complete context.)

- improvement from baseline leaderboard

- improvement relative to age leaderboard

The first one was bad, because it incentivized people to try to do as bad for their very first test as they can.

The second one however was better than the current one IMO. You care more about a guy with 0.7 DunedinPACE who's 100 years old than a guy with 0.6 DunedinPACE who's 20 years old.

Fortunately the first concept was trashed and unfortunately the second one as well, and now what we have?

We have a leaderboard where "verified" users are the ones who did 3 tests altogether. (Maybe they must do 3 test every year or sth like that, idk)

And unverified ones who did not. However they are still on the list, you just have to scroll down to the end of the verified users and you can consider what you see there somewhat as a "leaderboard in itself." It's not a fix, but an imperfect workaround.

But there's more. There's SYMPHONYAge, which creates a leaderboard for every organ system. Unfortunately it's not live yet, but they accidentally made it live at the update, and I quickly cached the results muhahahaha, so you can check it out how that'll look like.

Overall the sport is still in its infancy and there're certainly things to improve. Your feedback is well received (at least by me, I'm not affiliated with the Rejuvenation Olympics, I just do podcasts with the athletes. )
```

## 66. does anyone on the rejuvenation olympics leader

- Source: Reddit comments
- Link: https://www.reddit.com/r/Biohackers/comments/1gdk9ql/does_anyone_on_the_rejuvenation_olympics_leader/lu51sp0/
- Notes: r/Biohackers

```text
Title: does anyone on the rejuvenation olympics leader

Sounds like the wrong way to think about it, nevertheless of those that I've interviewed (17 episodes so far), only Steven Schorr has a supplement company. He was #18 before the rules changed and older people were kicked out of prominent places of the leaderboard.

Of course, others have mentioned Bryan, who's selling stuff.

Julie Gibson Clark (who was #2 before rule changes) is singing praises of Novos.

I'm not entirely sure about this one, but I have a suspect Dave Pascoe's preference is Life Extension.
```

## 67. help me interpret my biomarkers 1200 ngdl

- Source: Reddit comments
- Link: https://www.reddit.com/r/Biohackers/comments/1qeeupp/help_me_interpret_my_biomarkers_1200_ngdl/o0fr9lu/
- Notes: r/Biohackers

```text
Title: help me interpret my biomarkers 1200 ngdl

Your RDW-CV is somewhat high, that must be driving your results.

Also you must have lots of muscle, right? Your creatinine is also high which is not good for pheno age, but your cystatin C is low which means your kidneys are fine so all the high creatinine means is high muscle mass.

Creatinine: 11.4 mg/L (~1.14 mg/dL)
Cystatin C: 0.66 mg/L
```

## 68. help me interpret my biomarkers 1200 ngdl

- Source: Reddit comments
- Link: https://www.reddit.com/r/Biohackers/comments/1qeeupp/help_me_interpret_my_biomarkers_1200_ngdl/o028sd3/
- Notes: r/Biohackers

```text
Title: help me interpret my biomarkers 1200 ngdl

Check out where you'd rank in LWC: https://www.longevityworldcup.com/pheno-age

Based on what I've seen the healthiest people max out at around -15 years of pheno age without value capture.
```

## 69. how accurate rejuvenation olympics are

- Source: Reddit comments
- Link: https://www.reddit.com/r/blueprint_/comments/1gnbv0p/how_accurate_rejuvenation_olympics_are/lwdsjh4/
- Notes: r/blueprint_

```text
Title: how accurate rejuvenation olympics are

I think most epigenetic biological ages are most correlated with inflammaging rather than other aging hallmarks. But I'm just guessing here from experience.
```

## 70. how are the rejuvenation olympics tests 500

- Source: Reddit comments
- Link: https://www.reddit.com/r/blueprint_/comments/1fvrido/how_are_the_rejuvenation_olympics_tests_500/lqf6k4s/
- Notes: r/blueprint_

```text
Title: how are the rejuvenation olympics tests 500

https://github.com/nopara73/LongevityWorldCup
```

## 71. how are the rejuvenation olympics tests 500

- Source: Reddit comments
- Link: https://www.reddit.com/r/blueprint_/comments/1fvrido/how_are_the_rejuvenation_olympics_tests_500/lq9h9wo/
- Notes: r/blueprint_

```text
Title: how are the rejuvenation olympics tests 500

Longevity Advantage calculated PhenoAge from blood lab data, while TD looks at epigenetics. It's anyone's guess what these numbers give, but if you're interested in competing with that, the 2025 Longevity World Cup will be based on PhenoAge.
```

## 72. i like rejuvenation olympics as a concept but

- Source: Reddit comments
- Link: https://www.reddit.com/r/blueprint_/comments/1i64hzx/i_like_rejuvenation_olympics_as_a_concept_but/m8jzww7/
- Notes: r/blueprint_

```text
Title: i like rejuvenation olympics as a concept but

Oh also, in a few months if you're still around I'd love to interview you about age rank on my podcast, which is about longevity competitions: https://www.youtube.com/playlist?list=PL4nqc85w185sO4i7eR3oUO_lMmlJ2K1cL
```

## 73. i like rejuvenation olympics as a concept but

- Source: Reddit comments
- Link: https://www.reddit.com/r/blueprint_/comments/1i64hzx/i_like_rejuvenation_olympics_as_a_concept_but/m8d2ypp/
- Notes: r/blueprint_

```text
Title: i like rejuvenation olympics as a concept but

Seems like we're thinking along the same lines in regards to a pheno age based leaderboard. Check out the Longevity World Cup: https://www.longevityworldcup.com/
```

## 74. i suspect that slower aging might be related to

- Source: Reddit comments
- Link: https://www.reddit.com/r/ScientificNutrition/comments/1gn9ibg/i_suspect_that_slower_aging_might_be_related_to/lwdst8d/
- Notes: r/ScientificNutrition

```text
Title: i suspect that slower aging might be related to

That's interesting. I would had guessed otherwise: since higher testosterone typically means less inflammation and less inflammation is probably the most correlative with lower epigenetic biological aging tests, it should be the opposite.
```

## 75. joe cohen a guy who claims hes beating bryans

- Source: Reddit comments
- Link: https://www.reddit.com/r/blueprint_/comments/1hp0mki/joe_cohen_a_guy_who_claims_hes_beating_bryans/m4hknj3/
- Notes: r/blueprint_

```text
Title: joe cohen a guy who claims hes beating bryans

Do you know why isn't Joe on RO?

BTW if you're interested in interviews with longevity athletes, check out my pod . I'll try to get Joe on board as well.

P.S.: There was some low ranking news that reported this guy as being the Aldo Britschgi we're looking for. I'm not confident in that though.
```

## 76. mike lustgarten phd is the 2025 longevity world

- Source: Reddit comments
- Link: https://www.reddit.com/r/Biohackers/comments/1qf5xqi/mike_lustgarten_phd_is_the_2025_longevity_world/o0mj43u/
- Notes: r/Biohackers

```text
Title: mike lustgarten phd is the 2025 longevity world

This isn't even the largest issue with pheno age. Check this other one out: https://github.com/nopara73/LongevityWorldCup/issues/136
```

## 77. mike lustgarten phd is the 2025 longevity world

- Source: Reddit comments
- Link: https://www.reddit.com/r/Biohackers/comments/1qf5xqi/mike_lustgarten_phd_is_the_2025_longevity_world/o0frtm9/
- Notes: r/Biohackers

```text
Title: mike lustgarten phd is the 2025 longevity world

Nobody and I even have data to back it up :) There's actually a crowd age entry on each athletes' page which tells you what people guessed their age is based on their pictures.
```

## 78. mike lustgarten phd is the 2025 longevity world

- Source: Reddit comments
- Link: https://www.reddit.com/r/Biohackers/comments/1qf5xqi/mike_lustgarten_phd_is_the_2025_longevity_world/o094xyu/
- Notes: r/Biohackers

```text
Title: mike lustgarten phd is the 2025 longevity world

yeah that's the true measurement. failing that we can estimate the likelihood of death as well, which would be called all cause mortality
```

## 79. mike lustgarten phd is the 2025 longevity world

- Source: Reddit comments
- Link: https://www.reddit.com/r/Biohackers/comments/1qf5xqi/mike_lustgarten_phd_is_the_2025_longevity_world/o094toj/
- Notes: r/Biohackers

```text
Title: mike lustgarten phd is the 2025 longevity world

yes they have been paid out already so removed money parts yesterday
```

## 80. mike lustgarten phd is the 2025 longevity world

- Source: Reddit comments
- Link: https://www.reddit.com/r/Biohackers/comments/1qf5xqi/mike_lustgarten_phd_is_the_2025_longevity_world/o094nvd/
- Notes: r/Biohackers

```text
Title: mike lustgarten phd is the 2025 longevity world

[the immortal combat has began]( https://www.youtube.com/watch?v=BxsNnUAyfd4 )
```

## 81. mike lustgarten phd is the 2025 longevity world

- Source: Reddit comments
- Link: https://www.reddit.com/r/Biohackers/comments/1qf5xqi/mike_lustgarten_phd_is_the_2025_longevity_world/o094j5d/
- Notes: r/Biohackers

```text
Title: mike lustgarten phd is the 2025 longevity world

There are 80 year old btw: https://www.longevityworldcup.com/league/silent-generation

Anyhow I'm kindof bittersweet on pheno age taking age as an input. If it wouldn't, older people would have much more chances of winning, because it wouldn't normalize for age (even though it doesn't do that perfectly, that's not its goal with the age input, but oh well, it kinda results in that.)

But what we need to find is old people who are doing well.
```

## 82. most cost effective blood test for phenoage

- Source: Reddit comments
- Link: https://www.reddit.com/r/LongevityWorldCup/comments/1rku1k6/most_cost_effective_blood_test_for_phenoage/o8qs39b/
- Notes: r/LongevityWorldCup

```text
Title: most cost effective blood test for phenoage

Yeah function health is one of the best, but also one of the most expensive, so basically anything else is more cost effective. BTW it very much depends on where you are. Some people from the US complain that they not only cannot get cheap blood tests that would cover pheno age (let alone bortz) but they can't even get a blood test like that done in the first place.
```

## 83. share your startup quarterly post

- Source: Reddit comments
- Link: https://www.reddit.com/r/startups/comments/1lxc97s/share_your_startup_quarterly_post/n5y0isq/
- Notes: r/startups

```text
Title: share your startup quarterly post

Longevity World Cup – https://www.longevityworldcup.com

Cyberspace

The Longevity World Cup is an annual competition where athletes compete to lower their biological age .

2025 season uses the PhenoAge blood‑test clock; leaderboard ranks by absolute age reversal. Bitcoin donations are collected for prize money. They go to the top contenders.

Stage
Beta – competition started in Jan 2025, 1.0 launch in two months

Role
Solo founder (ex‑Wasabi Wallet founder→ turned longevity nerd)

Tech
.NET backend, HTML + CSS + JS frontent. Not using typescript or anything like that, full custom. Open‑source, everything is on GitHub.

Immediate goals:

- Prepare for product launch

- Reach 100 athletes (less than 20 left)

How r/startups can help

Need revenue model ideas, also feel free to join if you want to compete in longevity

I'll be pinging this comment daily.
```

## 84. share your startup quarterly post

- Source: Reddit comments
- Link: https://www.reddit.com/r/startups/comments/1lxc97s/share_your_startup_quarterly_post/n5y0fth/
- Notes: r/startups

```text
Title: share your startup quarterly post

Longevity World Cup – https://www.longevityworldcup.com

Cyberspace

The Longevity World Cup is an annual competition where athletes compete to lower their biological age .

2025 season uses the PhenoAge blood‑test clock; leaderboard ranks by absolute age reversal. Bitcoin donations are collected for prize money. They go to the top contenders.

||
||
| Stage |Beta – competition started in Jan 2025, 1.0 launch in two months|
| Role |Solo founder (ex‑Wasabi Wallet founder→ turned longevity nerd)|
| Tech |.NET backend, HTML + CSS + JS frontent. Not using typescript or anything like that, full custom. Open‑source, everything is on GitHub.|

Immediate goals:

- Prepare for product launch

- Reach 100 athletes (less than 20 left)

How r/startups can help: need revenue model ideas, also feel free to join if you want to compete in longevity

No vanity metrics, no BS funnels—just a scoreboard, some code, and sats on the line.
```

## 85. share your startup quarterly post

- Source: Reddit comments
- Link: https://www.reddit.com/r/startups/comments/1lxc97s/share_your_startup_quarterly_post/n5y0f69/
- Notes: r/startups

```text
Title: share your startup quarterly post

Longevity World Cup – https://www.longevityworldcup.com

Cyberspace

The Longevity World Cup is an annual competition where athletes compete to lower their biological age .

2025 season uses the PhenoAge blood‑test clock; leaderboard ranks by absolute age reversal. Bitcoin donations are collected for prize money. They go to the top contenders.

||
||
| Stage |Beta – competition started in Jan 2025, 1.0 launch in two months|
| Role |Solo founder (ex‑Wasabi Wallet founder→ turned longevity nerd)|
| Tech |.NET backend, HTML + CSS + JS frontent. Not using typescript or anything like that, full custom. Open‑source, everything is on GitHub.|

Immediate goals:

- Prepare for product launch

- Reach 100 athletes (less than 20 left)

How r/startups can help: need revenue model ideas, also feel free to join if you want to compete in longevity

No vanity metrics, no BS funnels—just a scoreboard, some code, and sats on the line.
```

## 86. wasabi wallet founder update completely offtopic

- Source: Reddit comments
- Link: https://www.reddit.com/r/WasabiWallet/comments/1l295qp/wasabi_wallet_founder_update_completely_offtopic/mw4011q/
- Notes: r/WasabiWallet

```text
Title: wasabi wallet founder update completely offtopic

Yes, pheno age is a well known biological aging clock: https://github.com/nopara73/LongevityWorldCup/blob/master/LongevityWorldCup.Documentation/pheno-age.pdf

But it's far from perfect, like all other cocks are, so my bet is as technology improves, biological aging clocks get better and the more relevant the competition can get.
```

## 87. wasabi wallet founder update completely offtopic

- Source: Reddit comments
- Link: https://www.reddit.com/r/WasabiWallet/comments/1l295qp/wasabi_wallet_founder_update_completely_offtopic/mvwqny3/
- Notes: r/WasabiWallet

```text
Title: wasabi wallet founder update completely offtopic

Free and open source for life!

Right you got it. The first year (this year) we're using PhenoAge, which comes from 9 blood markers of a traditional blood test: albumin, CRP, RDW, etc...

But we'll change biological aging clocks every year.
```

## 88. whats one sport that is popular in your country

- Source: Reddit comments
- Link: https://www.reddit.com/r/AskTheWorld/comments/1rdvs2x/whats_one_sport_that_is_popular_in_your_country/o7a3y5w/
- Notes: r/AskTheWorld

```text
Title: whats one sport that is popular in your country

In Prospera, the Longevity World Cup and knife fighting are the most popular sports
```

## 89. Bodybuilding, Longevity and The Philosophy of Games

- Source: Reddit submitted
- Link: https://www.reddit.com/r/longevity/comments/1b4l7ur/bodybuilding_longevity_and_the_philosophy_of_games/
- Date: 2024-03-02T10:25:07+00:00
- Notes: r/longevity

```text
Title: Bodybuilding, Longevity and The Philosophy of Games
Linked URL: https://nopara73.medium.com/longevity-game-9a79a8645bd9
```

## 90. Bodybuilding, Longevity and The Philosophy of Games

- Source: Reddit submitted
- Link: https://www.reddit.com/r/bodybuilding/comments/1b4l829/bodybuilding_longevity_and_the_philosophy_of_games/
- Date: 2024-03-02T10:25:27+00:00
- Notes: r/bodybuilding

```text
Title: Bodybuilding, Longevity and The Philosophy of Games
Linked URL: https://nopara73.medium.com/longevity-game-9a79a8645bd9
```

## 91. Bodybuilding, Longevity and The Philosophy of Games

- Source: Reddit submitted
- Link: https://www.reddit.com/r/Biohackers/comments/1b4l875/bodybuilding_longevity_and_the_philosophy_of_games/
- Date: 2024-03-02T10:25:46+00:00
- Notes: r/Biohackers

```text
Title: Bodybuilding, Longevity and The Philosophy of Games
Linked URL: https://nopara73.medium.com/longevity-game-9a79a8645bd9
```

## 92. Bodybuilding, Longevity and The Philosophy of Games

- Source: Reddit submitted
- Link: https://www.reddit.com/r/powerlifting/comments/1b4la25/bodybuilding_longevity_and_the_philosophy_of_games/
- Date: 2024-03-02T10:29:19+00:00
- Notes: r/powerlifting

```text
Title: Bodybuilding, Longevity and The Philosophy of Games
Linked URL: https://nopara73.medium.com/longevity-game-9a79a8645bd9
```

## 93. Bodybuilding, Longevity and The Philosophy of Games

- Source: Reddit submitted
- Link: https://www.reddit.com/r/Calisthenic/comments/1b4lajb/bodybuilding_longevity_and_the_philosophy_of_games/
- Date: 2024-03-02T10:30:06+00:00
- Notes: r/Calisthenic

```text
Title: Bodybuilding, Longevity and The Philosophy of Games
Linked URL: https://nopara73.medium.com/longevity-game-9a79a8645bd9
```

## 94. Oliver Zolman, Organizer, Rejuvenation Olympics

- Source: Reddit submitted
- Link: https://www.reddit.com/r/blueprint_/comments/1c6dexc/oliver_zolman_organizer_rejuvenation_olympics/
- Date: 2024-04-17T15:54:26+00:00
- Notes: r/blueprint_

```text
Title: Oliver Zolman, Organizer, Rejuvenation Olympics
Linked URL: https://www.youtube.com/watch?v=iRmZoCt3BWA&ab_channel=nopara73
```

## 95. Rejuvenation Olympics - Introduction

- Source: Reddit submitted
- Link: https://www.reddit.com/r/RejuvenationOlympics/comments/1c6docf/rejuvenation_olympics_introduction/
- Date: 2024-04-17T16:04:20+00:00
- Notes: r/RejuvenationOlympics

```text
Title: Rejuvenation Olympics - Introduction
Linked URL: https://www.youtube.com/watch?v=7aQ2VdV_S_Y&list=PL4nqc85w185sO4i7eR3oUO_lMmlJ2K1cL&ab_channel=nopara73
```

## 96. Oliver Zolman, Organizer, Rejuvenation Olympics

- Source: Reddit submitted
- Link: https://www.reddit.com/r/RejuvenationOlympics/comments/1c6dovy/oliver_zolman_organizer_rejuvenation_olympics/
- Date: 2024-04-17T16:04:54+00:00
- Notes: r/RejuvenationOlympics

```text
Title: Oliver Zolman, Organizer, Rejuvenation Olympics
Linked URL: https://www.youtube.com/watch?v=iRmZoCt3BWA&ab_channel=nopara73
```

## 97. Steven Schorr, 18th Place, Rejuvenation Olympics

- Source: Reddit submitted
- Link: https://www.reddit.com/r/RejuvenationOlympics/comments/1ccvg3s/steven_schorr_18th_place_rejuvenation_olympics/
- Date: 2024-04-25T15:52:32+00:00
- Notes: r/RejuvenationOlympics

```text
Title: Steven Schorr, 18th Place, Rejuvenation Olympics
Linked URL: https://www.youtube.com/watch?v=BCxqoT_g_lw
```

## 98. Jeffrey Gladden, 20th Place, Rejuvenation Olympics

- Source: Reddit submitted
- Link: https://www.reddit.com/r/RejuvenationOlympics/comments/1cj9ghb/jeffrey_gladden_20th_place_rejuvenation_olympics/
- Date: 2024-05-03T14:02:33+00:00
- Notes: r/RejuvenationOlympics

```text
Title: Jeffrey Gladden, 20th Place, Rejuvenation Olympics
Linked URL: https://www.youtube.com/watch?v=96Ma-25HxTw
```

## 99. Dian Ginsberg, 4th Place, Rejuvenation Olympics

- Source: Reddit submitted
- Link: https://www.reddit.com/r/RejuvenationOlympics/comments/1cpcwaa/dian_ginsberg_4th_place_rejuvenation_olympics/
- Date: 2024-05-11T08:37:30+00:00
- Notes: r/RejuvenationOlympics

```text
Title: Dian Ginsberg, 4th Place, Rejuvenation Olympics
Linked URL: https://youtu.be/bEDwES3_jyg

This is an archived post. You won't be able to vote or comment.
Posts are automatically archived after 6 months.
```

## 100. Ryan Smith, Organizer, Rejuvenation Olympics

- Source: Reddit submitted
- Link: https://www.reddit.com/r/RejuvenationOlympics/comments/1czg3bs/ryan_smith_organizer_rejuvenation_olympics/
- Date: 2024-05-24T08:40:28+00:00
- Notes: r/RejuvenationOlympics

```text
Title: Ryan Smith, Organizer, Rejuvenation Olympics
Linked URL: https://www.youtube.com/watch?v=F77GrS8vG94
```

## 101. Rejuvenation Olympics 2024 Update Explained

- Source: Reddit submitted
- Link: https://www.reddit.com/r/RejuvenationOlympics/comments/1dpnak8/rejuvenation_olympics_2024_update_explained/
- Date: 2024-06-27T09:40:22+00:00
- Notes: r/RejuvenationOlympics

```text
Title: Rejuvenation Olympics 2024 Update Explained
Linked URL: https://youtu.be/1KK9twyFmCg
```

## 102. I took a shot at explaining the recent Rejuvenation Olympics update. I hope you find it valuable!

- Source: Reddit submitted
- Link: https://www.reddit.com/r/blueprint_/comments/1dpq9vw/i_took_a_shot_at_explaining_the_recent/
- Date: 2024-06-27T12:38:08+00:00
- Notes: r/blueprint_

```text
Title: I took a shot at explaining the recent Rejuvenation Olympics update. I hope you find it valuable!
Linked URL: https://www.youtube.com/watch?v=1KK9twyFmCg
```

## 103. Julie Gibson Clark, 2nd Place (Pre Rule Change), Rejuvenation Olympics

- Source: Reddit submitted
- Link: https://www.reddit.com/r/RejuvenationOlympics/comments/1dua78k/julie_gibson_clark_2nd_place_pre_rule_change/
- Date: 2024-07-03T09:29:48+00:00
- Notes: r/RejuvenationOlympics

```text
Title: Julie Gibson Clark, 2nd Place (Pre Rule Change), Rejuvenation Olympics
Linked URL: https://youtu.be/fEq9_vKD74M
```

## 104. Siim Land, 2nd Place, Rejuvenation Olympics

- Source: Reddit submitted
- Link: https://www.reddit.com/r/RejuvenationOlympics/comments/1dzcman/siim_land_2nd_place_rejuvenation_olympics/
- Date: 2024-07-09T20:27:52+00:00
- Notes: r/RejuvenationOlympics

```text
Title: Siim Land, 2nd Place, Rejuvenation Olympics
Linked URL: https://www.youtube.com/watch?v=FOsVRc-f2wY
```

## 105. Hannah Went, Organizer, Rejuvenation Olympics

- Source: Reddit submitted
- Link: https://www.reddit.com/r/RejuvenationOlympics/comments/1e5bm4o/hannah_went_organizer_rejuvenation_olympics/
- Date: 2024-07-17T06:52:03+00:00
- Notes: r/RejuvenationOlympics

```text
Title: Hannah Went, Organizer, Rejuvenation Olympics
Linked URL: https://www.youtube.com/watch?v=XCT1WCYZOpM
```

## 106. Michael Lustgarten, 15th Place, Rejuvenation Olympics

- Source: Reddit submitted
- Link: https://www.reddit.com/r/RejuvenationOlympics/comments/1er4v1n/michael_lustgarten_15th_place_rejuvenation/
- Date: 2024-08-13T10:57:50+00:00
- Notes: r/RejuvenationOlympics

```text
Title: Michael Lustgarten, 15th Place, Rejuvenation Olympics
Linked URL: https://www.youtube.com/watch?v=KFfGdf20-1g
```

## 107. Dave Pascoe, 11th Place, Rejuvenation Olympics

- Source: Reddit submitted
- Link: https://www.reddit.com/r/RejuvenationOlympics/comments/1ewq5d7/dave_pascoe_11th_place_rejuvenation_olympics/
- Date: 2024-08-20T08:15:20+00:00
- Notes: r/RejuvenationOlympics

```text
Title: Dave Pascoe, 11th Place, Rejuvenation Olympics
Linked URL: https://www.youtube.com/watch?v=b3D1k1-w9K4
```

## 108. ‬Martin Faulks, 27th Place, Rejuvenation Olympics

- Source: Reddit submitted
- Link: https://www.reddit.com/r/RejuvenationOlympics/comments/1f2cpft/martin_faulks_27th_place_rejuvenation_olympics/
- Date: 2024-08-27T09:22:52+00:00
- Notes: r/RejuvenationOlympics

```text
Title: ‬Martin Faulks, 27th Place, Rejuvenation Olympics
Linked URL: https://www.youtube.com/watch?v=PNP2XZmo5Sg
```

## 109. Derek Wright, 37th Place, Rejuvenation Olympics

- Source: Reddit submitted
- Link: https://www.reddit.com/r/RejuvenationOlympics/comments/1f7sw77/derek_wright_37th_place_rejuvenation_olympics/
- Date: 2024-09-03T06:13:49+00:00
- Notes: r/RejuvenationOlympics

```text
Title: Derek Wright, 37th Place, Rejuvenation Olympics
Linked URL: https://youtu.be/Iwno9u6AHxs
```

## 110. Julie Gibson Clark, Dave Pascoe, Siim Land - Immortal Combat Panel

- Source: Reddit submitted
- Link: https://www.reddit.com/r/RejuvenationOlympics/comments/1fmpabk/julie_gibson_clark_dave_pascoe_siim_land_immortal/
- Date: 2024-09-22T09:27:29+00:00
- Notes: r/RejuvenationOlympics

```text
Title: Julie Gibson Clark, Dave Pascoe, Siim Land - Immortal Combat Panel
Linked URL: https://www.youtube.com/watch?v=BrSVYGKmDmo
```

## 111. Richard Heck, 7th Place, Rejuvenation Olympics

- Source: Reddit submitted
- Link: https://www.reddit.com/r/RejuvenationOlympics/comments/1fo6xe4/richard_heck_7th_place_rejuvenation_olympics/
- Date: 2024-09-24T07:33:32+00:00
- Notes: r/RejuvenationOlympics

```text
Title: Richard Heck, 7th Place, Rejuvenation Olympics
Linked URL: https://www.youtube.com/watch?v=RaEUPU1Oej4
```

## 112. How I Grabbed the #1 Spot in the Rejuvenation Olympics and Reduced My Epigenetic Age by 6 years in 1 year

- Source: Reddit submitted
- Link: https://www.reddit.com/r/RejuvenationOlympics/comments/1fspfng/how_i_grabbed_the_1_spot_in_the_rejuvenation/
- Date: 2024-09-30T06:50:58+00:00
- Notes: r/RejuvenationOlympics

```text
Title: How I Grabbed the #1 Spot in the Rejuvenation Olympics and Reduced My Epigenetic Age by 6 years in 1 year
Linked URL: https://old.reddit.com/r/Supplements/comments/1fsj3bg/how_i_grabbed_the_1_spot_in_the_rejuvenation/
```

## 113. Cheryllynn Manson, 4th Lowest DunedinPACE of Women's Division, Rejuvenation Olympics

- Source: Reddit submitted
- Link: https://www.reddit.com/r/RejuvenationOlympics/comments/1gesxny/cheryllynn_manson_4th_lowest_dunedinpace_of/
- Date: 2024-10-29T12:04:20+00:00
- Notes: r/RejuvenationOlympics

```text
Title: Cheryllynn Manson, 4th Lowest DunedinPACE of Women's Division, Rejuvenation Olympics
Linked URL: https://www.youtube.com/watch?v=H0kWwC_z2v0&ab_channel=nopara73
```

## 114. We spoke to 10+ longevity athletes... here's what we learned

- Source: Reddit submitted
- Link: https://www.reddit.com/r/LongevityWorldCup/comments/1h6eujg/we_spoke_to_10_longevity_athletes_heres_what_we/
- Date: 2024-12-04T12:29:47+00:00
- Notes: r/LongevityWorldCup

```text
Title: We spoke to 10+ longevity athletes... here's what we learned
Linked URL: https://www.youtube.com/watch?v=_nPh34wv25U
```

## 115. We spoke to 10+ longevity athletes... here's what we learned

- Source: Reddit submitted
- Link: https://www.reddit.com/r/RejuvenationOlympics/comments/1h6eulm/we_spoke_to_10_longevity_athletes_heres_what_we/
- Date: 2024-12-04T12:29:52+00:00
- Notes: r/RejuvenationOlympics

```text
Title: We spoke to 10+ longevity athletes... here's what we learned
Linked URL: https://www.youtube.com/watch?v=_nPh34wv25U
```

## 116. We spoke to 10+ longevity athletes... here's what we learned

- Source: Reddit submitted
- Link: https://www.reddit.com/r/blueprint_/comments/1h6evmv/we_spoke_to_10_longevity_athletes_heres_what_we/
- Date: 2024-12-04T12:31:18+00:00
- Notes: r/blueprint_

```text
Title: We spoke to 10+ longevity athletes... here's what we learned
Linked URL: https://www.youtube.com/watch?v=_nPh34wv25U
```

## 117. What Is The Longevity World Cup?

- Source: Reddit submitted
- Link: https://www.reddit.com/r/LongevityWorldCup/comments/1hkn4z2/what_is_the_longevity_world_cup/
- Date: 2024-12-23T12:53:50+00:00
- Notes: r/LongevityWorldCup

```text
Title: What Is The Longevity World Cup?
Linked URL: https://www.youtube.com/watch?v=nb1bnZDXrJg
```

## 118. Pre-registration period of the Longevity World Cup has started! Head to the lab, and demand they draw your blood. Tell them it’s for science, for progress, for the twisted quest to know your biological age! Start here👉

- Source: Reddit submitted
- Link: https://www.reddit.com/r/LongevityWorldCup/comments/1hqzrpz/preregistration_period_of_the_longevity_world_cup/
- Date: 2025-01-01T09:36:37+00:00
- Notes: r/LongevityWorldCup

```text
Title: Pre-registration period of the Longevity World Cup has started! Head to the lab, and demand they draw your blood. Tell them it’s for science, for progress, for the twisted quest to know your biological age! Start here👉
Linked URL: https://www.longevityworldcup.com/onboarding/join-game.html
```

## 119. PhenoAge Calculation Bug Disclosure: Missing U-Shaped Curves for Biomarkers

- Source: Reddit submitted
- Link: https://www.reddit.com/r/LongevityWorldCup/comments/1hropt1/phenoage_calculation_bug_disclosure_missing/
- Date: 2025-01-02T07:50:12+00:00
- Notes: r/LongevityWorldCup

```text
Title: PhenoAge Calculation Bug Disclosure: Missing U-Shaped Curves for Biomarkers
Linked URL: https://github.com/nopara73/LongevityWorldCup/issues/136
```

## 120. PhenoAge Calculation Bug Disclosure: Missing U-Shaped Curves for Biomarkers

- Source: Reddit submitted
- Link: https://www.reddit.com/r/blueprint_/comments/1hroxg4/phenoage_calculation_bug_disclosure_missing/
- Date: 2025-01-02T08:05:23+00:00
- Notes: r/blueprint_

```text
Title: PhenoAge Calculation Bug Disclosure: Missing U-Shaped Curves for Biomarkers
Linked URL: /r/blueprint_/comments/1hroxg4/phenoage_calculation_bug_disclosure_missing/
```

## 121. Contrarian Takes of TOP Longevity Athletes

- Source: Reddit submitted
- Link: https://www.reddit.com/r/LongevityWorldCup/comments/1hwpitw/contrarian_takes_of_top_longevity_athletes/
- Date: 2025-01-08T17:22:25+00:00
- Notes: r/LongevityWorldCup

```text
Title: Contrarian Takes of TOP Longevity Athletes
Linked URL: https://www.youtube.com/watch?v=UskUNSnQaXE
```

## 122. Contrarian Takes of TOP Longevity Athletes

- Source: Reddit submitted
- Link: https://www.reddit.com/r/RejuvenationOlympics/comments/1hwpivd/contrarian_takes_of_top_longevity_athletes/
- Date: 2025-01-08T17:22:28+00:00
- Notes: r/RejuvenationOlympics

```text
Title: Contrarian Takes of TOP Longevity Athletes
Linked URL: https://www.youtube.com/watch?v=UskUNSnQaXE

This is an archived post. You won't be able to vote or comment.
Posts are automatically archived after 6 months.
```

## 123. The History of Longevity as a Sport

- Source: Reddit submitted
- Link: https://www.reddit.com/r/RejuvenationOlympics/comments/1hy8a1z/the_history_of_longevity_as_a_sport/
- Date: 2025-01-10T16:27:42+00:00
- Notes: r/RejuvenationOlympics

```text
Title: The History of Longevity as a Sport
Linked URL: https://github.com/nopara73/LongevityWorldCup/blob/master/LongevityWorldCup.Documentation/LongevitySportHistory.md
```

## 124. The History of Longevity as a Sport

- Source: Reddit submitted
- Link: https://www.reddit.com/r/LongevityWorldCup/comments/1hy8a5e/the_history_of_longevity_as_a_sport/
- Date: 2025-01-10T16:27:49+00:00
- Notes: r/LongevityWorldCup

```text
Title: The History of Longevity as a Sport
Linked URL: https://github.com/nopara73/LongevityWorldCup/blob/master/LongevityWorldCup.Documentation/LongevitySportHistory.md
```

## 125. Barry Robbins, 20th Place, Rejuvenation Olympics

- Source: Reddit submitted
- Link: https://www.reddit.com/r/RejuvenationOlympics/comments/1i1a0si/barry_robbins_20th_place_rejuvenation_olympics/
- Date: 2025-01-14T16:32:37+00:00
- Notes: r/RejuvenationOlympics

```text
Title: Barry Robbins, 20th Place, Rejuvenation Olympics
Linked URL: https://www.youtube.com/watch?v=BOxeRCK40kk
```

## 126. Al Brown, 17th Place, Rejuvenation Olympics

- Source: Reddit submitted
- Link: https://www.reddit.com/r/RejuvenationOlympics/comments/1in3olt/al_brown_17th_place_rejuvenation_olympics/
- Date: 2025-02-11T17:13:38+00:00
- Notes: r/RejuvenationOlympics

```text
Title: Al Brown, 17th Place, Rejuvenation Olympics
Linked URL: https://youtu.be/iA0gZwU76pI
```

## 127. The ONLY Sport Where You Don't Age Out - YOU AGE IN

- Source: Reddit submitted
- Link: https://www.reddit.com/r/LongevityWorldCup/comments/1j3bisv/the_only_sport_where_you_dont_age_out_you_age_in/
- Date: 2025-03-04T14:06:10+00:00
- Notes: r/LongevityWorldCup

```text
Title: The ONLY Sport Where You Don't Age Out - YOU AGE IN
Linked URL: https://www.youtube.com/watch?v=lAIqKXGUcs0
```

## 128. History of Longevity as a Sport - I thought I submit this record of history as I see many misconceptions about it lately

- Source: Reddit submitted
- Link: https://www.reddit.com/r/blueprint_/comments/1j5pmlo/history_of_longevity_as_a_sport_i_thought_i/
- Date: 2025-03-07T14:42:45+00:00
- Notes: r/blueprint_

```text
Title: History of Longevity as a Sport - I thought I submit this record of history as I see many misconceptions about it lately
Linked URL: https://github.com/nopara73/LongevityWorldCup/blob/master/LongevityWorldCup.Documentation/LongevitySportHistory.md
```

## 129. LWC25 Update

- Source: Reddit submitted
- Link: https://www.reddit.com/r/LongevityWorldCup/comments/1jewpzn/lwc25_update/
- Date: 2025-03-19T13:16:23+00:00
- Notes: r/LongevityWorldCup

```text
Title: LWC25 Update
Linked URL: https://youtu.be/Cn_i6eFyv5w
```

## 130. Daniel Lewis, 3rd Place, Rejuvenation Olympics

- Source: Reddit submitted
- Link: https://www.reddit.com/r/RejuvenationOlympics/comments/1jjfkih/daniel_lewis_3rd_place_rejuvenation_olympics/
- Date: 2025-03-25T09:42:19+00:00
- Notes: r/RejuvenationOlympics

```text
Title: Daniel Lewis, 3rd Place, Rejuvenation Olympics
Linked URL: https://youtu.be/Kh77J927oqQ
```

## 131. Meet Daniel Lewis, #3 on the Rejuvenation Olympics

- Source: Reddit submitted
- Link: https://www.reddit.com/r/blueprint_/comments/1jk6fk2/meet_daniel_lewis_3_on_the_rejuvenation_olympics/
- Date: 2025-03-26T07:42:59+00:00
- Notes: r/blueprint_

```text
Title: Meet Daniel Lewis, #3 on the Rejuvenation Olympics
Linked URL: https://www.youtube.com/watch?v=Kh77J927oqQ
```

## 132. LWC25: Point System Update

- Source: Reddit submitted
- Link: https://www.reddit.com/r/LongevityWorldCup/comments/1jmm0qw/lwc25_point_system_update/
- Date: 2025-03-29T13:14:30+00:00
- Notes: r/LongevityWorldCup

```text
Title: LWC25: Point System Update
Linked URL: https://nopara73.medium.com/lwc25-point-system-update-daa6c92120ba
```

## 133. Philosopher CHALLENGES Prominent Longevity Athletes

- Source: Reddit submitted
- Link: https://www.reddit.com/r/LongevityWorldCup/comments/1jvsqpq/philosopher_challenges_prominent_longevity/
- Date: 2025-04-10T08:12:11+00:00
- Notes: r/LongevityWorldCup

```text
Title: Philosopher CHALLENGES Prominent Longevity Athletes
Linked URL: https://www.youtube.com/watch?v=t5-B_awU-h8
```

## 134. Philosopher CHALLENGES Prominent Longevity Athletes

- Source: Reddit submitted
- Link: https://www.reddit.com/r/RejuvenationOlympics/comments/1jvsqrh/philosopher_challenges_prominent_longevity/
- Date: 2025-04-10T08:12:16+00:00
- Notes: r/RejuvenationOlympics

```text
Title: Philosopher CHALLENGES Prominent Longevity Athletes
Linked URL: https://www.youtube.com/watch?v=t5-B_awU-h8
```

## 135. Podcast: Philosopher CHALLENGES Prominent Longevity Athletes - Quite unique content, I though you guys might be interested in relation to the Don't Die philosophy

- Source: Reddit submitted
- Link: https://www.reddit.com/r/blueprint_/comments/1jvss7r/podcast_philosopher_challenges_prominent/
- Date: 2025-04-10T08:15:30+00:00
- Notes: r/blueprint_

```text
Title: Podcast: Philosopher CHALLENGES Prominent Longevity Athletes - Quite unique content, I though you guys might be interested in relation to the Don't Die philosophy
Linked URL: https://www.youtube.com/watch?v=t5-B_awU-h8
```

## 136. Melody Chong, 2nd in the Gen X Women's League, Longevity World Cup

- Source: Reddit submitted
- Link: https://www.reddit.com/r/LongevityWorldCup/comments/1kbbp8s/melody_chong_2nd_in_the_gen_x_womens_league/
- Date: 2025-04-30T08:45:55+00:00
- Notes: r/LongevityWorldCup

```text
Title: Melody Chong, 2nd in the Gen X Women's League, Longevity World Cup
Linked URL: https://www.youtube.com/watch?v=4hFzzMfV2To
```

## 137. Keith Blondin, 13th Place, Rejuvenation Olympics

- Source: Reddit submitted
- Link: https://www.reddit.com/r/RejuvenationOlympics/comments/1kfz740/keith_blondin_13th_place_rejuvenation_olympics/
- Date: 2025-05-06T08:11:55+00:00
- Notes: r/RejuvenationOlympics

```text
Title: Keith Blondin, 13th Place, Rejuvenation Olympics
Linked URL: https://youtu.be/hNpXAXH9bT0
```

## 138. Philipp Schmeing, 3rd Place, Longevity World Cup

- Source: Reddit submitted
- Link: https://www.reddit.com/r/LongevityWorldCup/comments/1kxfq73/philipp_schmeing_3rd_place_longevity_world_cup/
- Date: 2025-05-28T12:24:31+00:00
- Notes: r/LongevityWorldCup

```text
Title: Philipp Schmeing, 3rd Place, Longevity World Cup
Linked URL: https://www.youtube.com/watch?v=2V-TPK4Ni0g
```

## 139. Longevity World Cup Behind The Scenes

- Source: Reddit submitted
- Link: https://www.reddit.com/r/LongevityWorldCup/comments/1l25s2f/longevity_world_cup_behind_the_scenes/
- Date: 2025-06-03T07:29:11+00:00
- Notes: r/LongevityWorldCup

```text
Title: Longevity World Cup Behind The Scenes
Linked URL: https://www.youtube.com/watch?v=FC0emSQkRfI
```

## 140. Paul Ingraham (PainScience.com) vs Longevity Athletes

- Source: Reddit submitted
- Link: https://www.reddit.com/r/LongevityWorldCup/comments/1l271qq/paul_ingraham_painsciencecom_vs_longevity_athletes/
- Date: 2025-06-03T08:58:19+00:00
- Notes: r/LongevityWorldCup

```text
Title: Paul Ingraham (PainScience.com) vs Longevity Athletes
Linked URL: https://youtu.be/Su6fb0hGjQU
```

## 141. Juan Robalino, 7th Place, Longevity World Cup

- Source: Reddit submitted
- Link: https://www.reddit.com/r/LongevityWorldCup/comments/1lovkjx/juan_robalino_7th_place_longevity_world_cup/
- Date: 2025-07-01T07:34:45+00:00
- Notes: r/LongevityWorldCup

```text
Title: Juan Robalino, 7th Place, Longevity World Cup
Linked URL: https://youtu.be/mYi8JlEWDYI
```

## 142. Longevity as a Sport - LongevityDxTx Interview

- Source: Reddit submitted
- Link: https://www.reddit.com/r/LongevityWorldCup/comments/1mi3c8t/longevity_as_a_sport_longevitydxtx_interview/
- Date: 2025-08-05T08:11:05+00:00
- Notes: r/LongevityWorldCup

```text
Title: Longevity as a Sport - LongevityDxTx Interview
Linked URL: https://youtu.be/3aPTZFlK1JQ
```

## 143. Longevity as a Sport - LongevityDxTx Interview

- Source: Reddit submitted
- Link: https://www.reddit.com/r/RejuvenationOlympics/comments/1mi3c9d/longevity_as_a_sport_longevitydxtx_interview/
- Date: 2025-08-05T08:11:07+00:00
- Notes: r/RejuvenationOlympics

```text
Title: Longevity as a Sport - LongevityDxTx Interview
Linked URL: https://youtu.be/3aPTZFlK1JQ
```

## 144. Zdeñek Sipek, 2nd Place, Longevity World Cup

- Source: Reddit submitted
- Link: https://www.reddit.com/r/LongevityWorldCup/comments/1mo28fd/zdeñek_sipek_2nd_place_longevity_world_cup/
- Date: 2025-08-12T07:30:48+00:00
- Notes: r/LongevityWorldCup

```text
Title: Zdeñek Sipek, 2nd Place, Longevity World Cup
Linked URL: https://youtu.be/Ma13R7YRcho
```

## 145. From Czech Classroom to #2 in Longevity World Cup - Zdenek Sipek

- Source: Reddit submitted
- Link: https://www.reddit.com/r/LongevityWorldCup/comments/1mqtul5/from_czech_classroom_to_2_in_longevity_world_cup/
- Date: 2025-08-15T10:27:20+00:00
- Notes: r/LongevityWorldCup

```text
Title: From Czech Classroom to #2 in Longevity World Cup - Zdenek Sipek
Linked URL: https://youtu.be/LuHSmLFjWHU

This is an archived post. You won't be able to vote or comment.
Posts are automatically archived after 6 months.
```

## 146. Annie N, 2nd Place on RO

- Source: Reddit submitted
- Link: https://www.reddit.com/r/RejuvenationOlympics/comments/1mugzit/annie_n_2nd_place_on_ro/
- Date: 2025-08-19T12:27:13+00:00
- Notes: r/RejuvenationOlympics

```text
Title: Annie N, 2nd Place on RO
Linked URL: https://youtu.be/GUQ9sumtkJk
```

## 147. Forget Football: The Longevity World Cup Has Launched

- Source: Reddit submitted
- Link: https://www.reddit.com/r/LongevityWorldCup/comments/1nib99x/forget_football_the_longevity_world_cup_has/
- Date: 2025-09-16T07:50:49+00:00
- Notes: r/LongevityWorldCup

```text
Title: Forget Football: The Longevity World Cup Has Launched
Linked URL: https://nopara73.medium.com/longevity-world-cup-launch-9ac5de9991df
```

## 148. The Longevity World Cup Is Turning Aging Into a Competitive Sport - Marathon Handbook

- Source: Reddit submitted
- Link: https://www.reddit.com/r/LongevityWorldCup/comments/1nj7cvf/the_longevity_world_cup_is_turning_aging_into_a/
- Date: 2025-09-17T08:18:58+00:00
- Notes: r/LongevityWorldCup

```text
Title: The Longevity World Cup Is Turning Aging Into a Competitive Sport - Marathon Handbook
Linked URL: https://marathonhandbook.com/longevity-world-cup/
```

## 149. LONGEVITY ATHLETE | motivational video

- Source: Reddit submitted
- Link: https://www.reddit.com/r/LongevityWorldCup/comments/1nja9ia/longevity_athlete_motivational_video/
- Date: 2025-09-17T11:15:27+00:00
- Notes: r/LongevityWorldCup

```text
Title: LONGEVITY ATHLETE | motivational video
Linked URL: https://www.youtube.com/watch?v=WJvWCSM9JJg
```

## 150. [AMA] I'm nopara73, creator of the open-source Longevity World Cup.. Ask Me Anything

- Source: Reddit submitted
- Link: https://www.reddit.com/r/IAmA/comments/1nk58lm/ama_im_nopara73_creator_of_the_opensource/
- Date: 2025-09-18T11:04:17+00:00
- Notes: r/IAmA

```text
Title: [AMA] I'm nopara73, creator of the open-source Longevity World Cup.. Ask Me Anything
Linked URL: /r/IAmA/comments/1nk58lm/ama_im_nopara73_creator_of_the_opensource/
```

## 151. [AMA] I'm nopara73, creator of the open-source Longevity World Cup.. Ask Me Anything

- Source: Reddit submitted
- Link: https://www.reddit.com/r/Aging/comments/1nk58qo/ama_im_nopara73_creator_of_the_opensource/
- Date: 2025-09-18T11:04:30+00:00
- Notes: r/Aging

```text
Title: [AMA] I'm nopara73, creator of the open-source Longevity World Cup.. Ask Me Anything
Linked URL: /r/Aging/comments/1nk58qo/ama_im_nopara73_creator_of_the_opensource/
```

## 152. [AMA] I'm nopara73, creator of the open-source Longevity World Cup.. Ask Me Anything

- Source: Reddit submitted
- Link: https://www.reddit.com/r/QuantifiedSelf/comments/1nk58y2/ama_im_nopara73_creator_of_the_opensource/
- Date: 2025-09-18T11:04:49+00:00
- Notes: r/QuantifiedSelf

```text
Title: [AMA] I'm nopara73, creator of the open-source Longevity World Cup.. Ask Me Anything
Linked URL: /r/QuantifiedSelf/comments/1nk58y2/ama_im_nopara73_creator_of_the_opensource/
```

## 153. [AMA] I'm nopara73, creator of the open-source Longevity World Cup.. Ask Me Anything

- Source: Reddit submitted
- Link: https://www.reddit.com/r/Biohackers/comments/1nk6215/ama_im_nopara73_creator_of_the_opensource/
- Date: 2025-09-18T11:47:31+00:00
- Notes: r/Biohackers

```text
Title: [AMA] I'm nopara73, creator of the open-source Longevity World Cup.. Ask Me Anything
Linked URL: /r/Biohackers/comments/1nk6215/ama_im_nopara73_creator_of_the_opensource/
```

## 154. BE THE BEST | longevity motivation

- Source: Reddit submitted
- Link: https://www.reddit.com/r/LongevityWorldCup/comments/1nl994i/be_the_best_longevity_motivation/
- Date: 2025-09-19T17:05:35+00:00
- Notes: r/LongevityWorldCup

```text
Title: BE THE BEST | longevity motivation
Linked URL: https://www.youtube.com/shorts/Kq0VkLF3Z4Q
```

## 155. Angela Buzzeo & Devin Neko | Longevity World Cup 4th & 69th

- Source: Reddit submitted
- Link: https://www.reddit.com/r/LongevityWorldCup/comments/1oh7yrl/angela_buzzeo_devin_neko_longevity_world_cup_4th/
- Date: 2025-10-27T07:18:16+00:00
- Notes: r/LongevityWorldCup

```text
Title: Angela Buzzeo & Devin Neko | Longevity World Cup 4th & 69th
Linked URL: https://youtu.be/AY_ZgRqApCE
```

## 156. Rand Hindi, 42nd Place, Rejuvenation Olympics

- Source: Reddit submitted
- Link: https://www.reddit.com/r/RejuvenationOlympics/comments/1ose249/rand_hindi_42nd_place_rejuvenation_olympics/
- Date: 2025-11-09T08:22:51+00:00
- Notes: r/RejuvenationOlympics

```text
Title: Rand Hindi, 42nd Place, Rejuvenation Olympics
Linked URL: https://youtu.be/hSaHTciD6ow
```

## 157. Zsolt Szabó - Bortz Biological Age Clock

- Source: Reddit submitted
- Link: https://www.reddit.com/r/LongevityWorldCup/comments/1p3ozrz/zsolt_szabó_bortz_biological_age_clock/
- Date: 2025-11-22T09:23:13+00:00
- Notes: r/LongevityWorldCup

```text
Title: Zsolt Szabó - Bortz Biological Age Clock
Linked URL: https://youtu.be/4sPoryFIsZ8
```

## 158. Mike Lustgarten, PhD Is the 2025 Longevity World Cup Winner

- Source: Reddit submitted
- Link: https://www.reddit.com/r/Biohackers/comments/1qf5xqi/mike_lustgarten_phd_is_the_2025_longevity_world/
- Date: 2026-01-17T06:32:22+00:00
- Notes: r/Biohackers

```text
Title: Mike Lustgarten, PhD Is the 2025 Longevity World Cup Winner
Linked URL: https://i.redd.it/40o0xrxcsudg1.png
```

## 159. Longevity World Cup vs Rejuvenation Olympics + Biological Aging Clocks

- Source: Reddit submitted
- Link: https://www.reddit.com/r/LongevityWorldCup/comments/1qg9r6x/longevity_world_cup_vs_rejuvenation_olympics/
- Date: 2026-01-18T14:19:32+00:00
- Notes: r/LongevityWorldCup

```text
Title: Longevity World Cup vs Rejuvenation Olympics + Biological Aging Clocks
Linked URL: https://klaustownsend.com/longevity-world-cup-vs-rejuvenation-olympics-biological-aging-clocks/
```

## 160. Longevity as a Sport: From Rejuvenation Olympics to Longevity World Cup | Klaus Townsend

- Source: Reddit submitted
- Link: https://www.reddit.com/r/LongevityWorldCup/comments/1qy7eil/longevity_as_a_sport_from_rejuvenation_olympics/
- Date: 2026-02-07T07:20:40+00:00
- Notes: r/LongevityWorldCup

```text
Title: Longevity as a Sport: From Rejuvenation Olympics to Longevity World Cup | Klaus Townsend
Linked URL: https://www.youtube.com/watch?v=hdgapJX4r5M
```

## 161. The Longevity World Cup is now accepting Bitcoin

- Source: Reddit submitted
- Link: https://www.reddit.com/r/Bitcoin/comments/1rarid2/the_longevity_world_cup_is_now_accepting_bitcoin/
- Date: 2026-02-21T13:46:52+00:00
- Notes: r/Bitcoin

```text
Title: The Longevity World Cup is now accepting Bitcoin
Linked URL: /r/Bitcoin/comments/1rarid2/the_longevity_world_cup_is_now_accepting_bitcoin/
```

## 162. Longevity World Cup Season 2: The Game Evolves

- Source: Reddit submitted
- Link: https://www.reddit.com/r/LongevityWorldCup/comments/1rblx38/longevity_world_cup_season_2_the_game_evolves/
- Date: 2026-02-22T13:31:03+00:00
- Notes: r/LongevityWorldCup

```text
Title: Longevity World Cup Season 2: The Game Evolves
Linked URL: https://nopara73.medium.com/longevity-world-cup-season-2-the-game-evolves-dcdf6b56f6ea
```

## 163. Longevity World Cup Season 2 has begun

- Source: Reddit submitted
- Link: https://www.reddit.com/r/LongevityWorldCup/comments/1rg00or/longevity_world_cup_season_2_has_begun/
- Date: 2026-02-27T07:19:13+00:00
- Notes: r/LongevityWorldCup

```text
Title: Longevity World Cup Season 2 has begun
Linked URL: https://youtu.be/8V-h1QtvTzk
```

## 164. Meet the Top 3 Longevity Athletes on Earth

- Source: Reddit submitted
- Link: https://www.reddit.com/r/LongevityWorldCup/comments/1rw4voi/meet_the_top_3_longevity_athletes_on_earth/
- Date: 2026-03-17T12:16:37+00:00
- Notes: r/LongevityWorldCup

```text
Title: Meet the Top 3 Longevity Athletes on Earth
Linked URL: https://youtu.be/ylMQ4yLfEkc
```

## 165. Longevity World Cup Merch Store is now LIVE (by Klaus Townsend)

- Source: Reddit submitted
- Link: https://www.reddit.com/r/LongevityWorldCup/comments/1sgmyt3/longevity_world_cup_merch_store_is_now_live_by/
- Date: 2026-04-09T11:30:23+00:00
- Notes: r/LongevityWorldCup

```text
Title: Longevity World Cup Merch Store is now LIVE (by Klaus Townsend)
Linked URL: https://merch.longevityworldcup.com/
```

## 166. Interview with Ilhui Hernandez | finished 10th in 2025 with a biological age 17.7 years younger (Pheno Age)

- Source: Reddit submitted
- Link: https://www.reddit.com/r/LongevityWorldCup/comments/1t2dzaf/interview_with_ilhui_hernandez_finished_10th_in/
- Date: 2026-05-03T06:47:20+00:00
- Notes: r/LongevityWorldCup

```text
Title: Interview with Ilhui Hernandez | finished 10th in 2025 with a biological age 17.7 years younger (Pheno Age)
Linked URL: https://www.youtube.com/watch?v=qFWWghmFSCc
```

## 167. Interview with the host of the Longevity World Cup (that's me) on the Legacy and Longevity podcast

- Source: Reddit submitted
- Link: https://www.reddit.com/r/LongevityWorldCup/comments/1t2dzui/interview_with_the_host_of_the_longevity_world/
- Date: 2026-05-03T06:48:15+00:00
- Notes: r/LongevityWorldCup

```text
Title: Interview with the host of the Longevity World Cup (that's me) on the Legacy and Longevity podcast
Linked URL: https://www.youtube.com/watch?v=4LSSEAfCBsg
```

## 168. Interview with Inka Land, MSc (#14)

- Source: Reddit submitted
- Link: https://www.reddit.com/r/LongevityWorldCup/comments/1tjd3di/interview_with_inka_land_msc_14/
- Date: 2026-05-21T07:28:31+00:00
- Notes: r/LongevityWorldCup

```text
Title: Interview with Inka Land, MSc (#14)
Linked URL: https://www.youtube.com/watch?v=KP880OKbYrw
```
