# Mortality age: frozen initial analysis plan

Frozen on 2026-10-09 after an availability/event-count audit, before fitting models or examining prediction performance. No pre-existing clock coefficients or clinical biomarker caps will be imported.

## Question and deliverable

Can a research calculator use eight measurement domains with sex-dependent nonlinear functions to estimate a mortality-equivalent age? The domains are clinic systolic/diastolic pressure, waist-to-height, HbA1c, ApoB, hs-CRP, cystatin C, maximum valid single-hand grip, and exercise-estimated VO2max. Recorded male/female sex and chronological age are required. This is mortality prediction, not a pace-of-aging or causal treatment model.

The website will use the existing LWC calculator appearance/interaction, remain unlisted, and link to an actual research report. The full-panel result is an integration experiment, with no observed complete eight-domain NHANES validation cohort. Supported observed-panel results must be distinguished from it. A full-panel failure must be reported, never replaced by invented coefficients or an unlabeled subset.

## Data and frozen split

Public NHANES cycles 1999-2000 through 2015-2016; 2019 public all-cause linkage; examination-based follow-up in months. Exclude ages below 18 or at least 80 (older ages are top-coded), ineligible/unlinked outcome records, nonpositive follow-up, unknown sex, and identified pregnancy. Keep causes of death unrestricted. The common core requires nonmissing BP, waist/height and HbA1c.

Development cycles: 1999-2000, 2001-2002, 2005-2006, 2007-2008, 2011-2012. Untouched temporal evaluation: 2003-2004, 2009-2010, 2013-2014. The 2015-2016 cycle is a separate assay/short-follow-up sensitivity, not a five-year validation cohort. Inside development, hold out respondents deterministically by a seeded hash for selection between the prespecified candidate specifications. No holdout-driven changes retain a confirmatory label.

Primary displayed horizon: five years. Fit right-censored survival likelihood over available follow-up, report hazard/time-form sensitivity, and evaluate five-year risk with censoring handled. The full exercise-fitness integration experiment is restricted to input ages 18-49; this does not permit extrapolating observed exercise fitness into older adults. The age/sex reference and supported nonfitness panels may use ages 18-79.

## Measurement preparation

BP is the mean of available nonzero readings 1-3 from one resting examination. Waist-to-height is measured waist cm divided by standing height cm. HbA1c is NGSP percent; conversion from IFCC mmol/mol uses the published master equation. CRP is mg/L (early mg/dL multiplied by 10); document assay detection substitution and changed later assays. Cystatin C is SSCARD_A, mg/L, with its revised weights. Grip is the largest reading among trials labeled maximal effort, across either hand, not MGDCGSZ's sum of both hands. Fitness is CVDESVO2, mL/kg/min; CVDVOMAX is not an exercise measurement. Never silently treat other fitness-estimation methods as interchangeable.

Use MEC examination weights for population models, with early four-year weights scaled consistently with two-year cycles. Use morning ApoB weights and revised stored-sample weights when learning those observed measurement distributions/panels. Bootstrap survey PSUs within strata for uncertainty; report Monte Carlo and structural uncertainty separately. Cache original downloads with URL/bytes/checksum/row/column records. No raw participant data will be committed.

## Prespecified models

Age/sex benchmark and common-core benchmark; observed blood (core + CRP + cystatin), lipid/inflammation (core + ApoB + CRP), strength (core + ApoB + grip), and fitness (core + CRP + cystatin + VO2) panels. Each is fit on genuinely observed rows with the appropriate weights. Compare linear/log terms with three-knot restricted cubic spline main effects. Component functions explicitly include sex interactions; shared curves plus more strongly regularized sex-specific deviations borrow information. For sparse panels, prespecified linear sex-dependent terms are preferable to unsupported independent nonlinear fits.

The full-panel experiment, if numerically estimable, uses a censored Gompertz survival likelihood, integrating unobserved measurements over declared conditional distributions learned from observed data. This is not stacking marginal hazard ratios or constructing a purportedly observed complete-case cohort. Estimate transformed measurement distributions conditional on age, sex and the common core. Preserve residual correlations for genuinely observed pairs; unknown pair relationships require declared assumptions and sensitivity analysis. The primary assumption is zero residual correlation for unobserved pairs after the common core. Compare moderate positive/negative dependence assumptions and stronger cross-panel dependence where covariance remains valid. Conditional transport of these distributions and outcome relationships across cycles and fitness eligibility is untestable; the full-panel model remains explicitly exploratory even if observed-data prediction is acceptable.

Candidates are linear/log and smooth models with stronger shrinkage of nonlinear/sex deviations. Tune a small fixed regularization grid using only the internal development split, then freeze and refit on development. Use deterministic antithetic Monte Carlo integration; repeat with more draws as a numerical sensitivity. Do not invent separate male/female curves when information is inadequate. No arbitrary clipping of biomarkers or age outputs.

## Evaluation and release criteria

Report participants/events/follow-up by cycle, sex and panel; observed pair/complete-case overlap; effective information; convergence and finite predictions. Evaluate temporal holdout calibration (observed versus expected, survival-calibration slope where estimable), discrimination, censoring-adjusted Brier error, and comparison with the age/sex benchmark. Report uncertainty and inability to estimate sparse strata explicitly. Assess five-year and available longer-time behavior, correct two-year landmark sensitivity, linear versus nonlinear specification, input definitions, and dependence assumptions. A numerically successful fit alone is not validation.

Freeze sex-specific reference survival and invert its five-year risk monotonically over ages 18-79. Match endpoint/horizon on both sides. Return unsupported when an input lies beyond declared observed support, inversion leaves the supported age reference, a measurement method/population is not supported, or numerical fitting fails. The full integration may be shown only as an explicit research experiment with its structural assumptions; it must never acquire a validated badge from partial-panel metrics. If it is too unstable to interpret, withhold its numeric age and show the supported panel estimates instead.

## Reproducibility and presentation

Save the frozen plan checksum before fitting. Save actual results, fitted transformations/knots, coefficients, baselines, reference maps, model versions, convergence, uncertainty/sensitivities, and test fixtures. Every report number/figure must derive from saved results. Primary-source references include NHANES component/codebook/analytic guidance and linked mortality documentation. Report historical population, selection, reverse causality, measurement error, assay resolution, sparse young/sex-specific deaths, and unobserved joint dependence. The report will show failed hypotheses as well as supported results.
