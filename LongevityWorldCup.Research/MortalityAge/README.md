# Mortality age research experiment

Nine numeric measurements across eight domains and recorded female/male sex. Version 0.2 removes chronological age from **all** measurement-to-risk models and missing-measurement regressions, with complete refitting. No default or inferred personal age is substituted. Sex-dependent biomarker functions predict five-year mortality; a separate fixed historical age/sex reference converts that risk into years without using the calculator user's age. Public NHANES never measures the entire panel together. The full combination is an **unvalidated integration experiment**, not an established biological aging clock; its fitted associations cannot guide health interventions.

The calculator is `/mortality-age`; its report is `/research/mortality-age.pdf`. It is unlisted/noindex with no competition/application integration. The previous fitness-panel withholding decision is retained: only four test deaths occurred within five years. Other panels remain experimental. Birth-date fields and the chronological-age result comparison are removed; existing drafts retain measurements and discard birth-date values. Measurement date is context only, never a predictor.

## Reproduce

Use Python 3.14.7 and the versions in `requirements.txt`. From the repository root, create an isolated environment and run:

```powershell
python -m venv .artifacts/mortality-age/venv
.artifacts/mortality-age/venv/Scripts/python.exe -m pip install -r LongevityWorldCup.Research/MortalityAge/requirements.txt
.artifacts/mortality-age/venv/Scripts/python.exe LongevityWorldCup.Research/MortalityAge/prepare.py
.artifacts/mortality-age/venv/Scripts/python.exe LongevityWorldCup.Research/MortalityAge/train.py
.artifacts/mortality-age/venv/Scripts/python.exe LongevityWorldCup.Research/MortalityAge/diagnostics.py
.artifacts/mortality-age/venv/Scripts/python.exe LongevityWorldCup.Research/MortalityAge/ph-diagnostics.py
.artifacts/mortality-age/venv/Scripts/python.exe LongevityWorldCup.Research/MortalityAge/transport.py
.artifacts/mortality-age/venv/Scripts/python.exe LongevityWorldCup.Research/MortalityAge/uncertainty.py
.artifacts/mortality-age/venv/Scripts/python.exe LongevityWorldCup.Research/MortalityAge/export.py
.artifacts/mortality-age/venv/Scripts/python.exe LongevityWorldCup.Research/MortalityAge/report.py
.artifacts/mortality-age/venv/Scripts/python.exe LongevityWorldCup.Research/MortalityAge/test_model.py
node LongevityWorldCup.Research/MortalityAge/verify-browser.mjs
```

Report generation uses Arial on Windows and Helvetica elsewhere, so PDF bytes may differ by rendering platform. Model equations and numerical fixtures retain stored precision. PDF metadata also contains generation time. Original file checksums, retrieval UTC and the frozen plan checksum are in `artifacts/`. Raw respondent data and downloads remain under ignored `.artifacts/`.

`analysis-plan.md` preceded version 0.1 fitting. Review corrected early combined-cycle and latent residual subsample weights after the initial temporal results were inspected. `age-free-plan.md` was saved before version 0.2 refitting, retaining the same data, weights, development cycles, hash split, candidate grid and seeds. Model selection uses development only. All temporal cohorts and diagnostic endpoints have been inspected previously; their reuse is **exploratory diagnostic evaluation, not new independent validation**. Saved results contain every selection candidate, temporal metric, failure, fixed-nuisance bootstrap, dependency/landmark/time-form sensitivity and benchmark comparison. Diagnostics never retune the primary model. `diagnostics.py` resumes only when data, fits and source checksums match.

Research age remains available for eligibility, historical-reference construction and subgroup/benchmark diagnostics, never prediction or missing-input conditioning. Full/fitness cohorts retain ages 18-49; other panels retain 18-79. These are training-population metadata, not required calculator inputs. Removing the age field does not validate use in older fitness populations. Reference inversion is limited to equivalent ages 18-79. Age-free biomarker associations can include age-related differences and remain noncausal.

The mean/covariance transport assumptions and absent joint dependencies cannot be verified here. Gaussian integration is not validation on complete observed cases. Conditional bootstrap ranges omit nuisance, selection and structural uncertainty. Marginal 1st-99th input limits do not certify joint support. The report does not establish proportional hazards, external validity, wearable fitness calibration or causal rejuvenation.

Historical `transport-plan.md` and `uncertainty-plan.md` introduced the two-year 2015 assay check, ten-year 2003 check, conditional 128-PSU metric ranges and paired Brier differences in version 0.1. The age-free plan repeats them using the refitted predictors, kept fixed during evaluation. Benchmarks use identical rows/weights. Their actual results are in the regenerated artifacts/report; these reused checks do not supply fresh validation or change the five-year release status.

The browser evaluator is the only product runtime. Inputs and drafts stay local; only the public model is fetched. Schema 2 declares `requiresChronologicalAge: false`, exact `riskInputs` and separate `trainingAgeRange` metadata. Old age-dependent schema 1 is rejected. `verify-browser.mjs` checks Python/browser parity and age invariance in every panel, bootstrap and dependence refit. Python checks prove risk design and missing-input conditioning work with the age column deleted. The model version hashes its exact coefficients and release configuration.
