# Mortality age research experiment

Nine numeric measurements across eight domains, chronological age and recorded female/male sex. The full equation contains sex-dependent biomarker functions and a five-year mortality reference. Public NHANES never measures the entire panel together. The full combination is an **unvalidated integration experiment**, not an established biological aging clock. Its inverse ApoB association and weak fitness contribution cannot guide health interventions.

The calculator is `/mortality-age`; its report is `/research/mortality-age.pdf`. The page is unlisted/noindex and has no competition or application integration. The observed fitness panel's numeric result is withheld because temporal calibration was poor and only four test deaths occurred within five years. Other observed panels remain explicitly experimental.

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
.artifacts/mortality-age/venv/Scripts/python.exe LongevityWorldCup.Research/MortalityAge/export.py
.artifacts/mortality-age/venv/Scripts/python.exe LongevityWorldCup.Research/MortalityAge/report.py
.artifacts/mortality-age/venv/Scripts/python.exe LongevityWorldCup.Research/MortalityAge/test_model.py
node LongevityWorldCup.Research/MortalityAge/verify-browser.mjs
```

Report generation uses Arial on Windows and Helvetica elsewhere, so PDF bytes may differ by rendering platform. Model equations and numerical fixtures retain stored precision. PDF metadata also contains generation time. Original file checksums, retrieval UTC and the frozen plan checksum are in `artifacts/`. Raw respondent data and downloads remain under ignored `.artifacts/`.

`analysis-plan.md` was frozen after availability counts and before fitting. Review corrected early combined-cycle weight scaling and residual subsample weighting after the initial temporal results had been inspected. The entire analysis was regenerated with the same predefined choices and temporal split; this is not a new untouched validation sample. Development-only internal selection again chose the smooth model with ridge 0.003. All temporal performance, failures, fixed-nuisance bootstrap ranges, dependency scenarios, benchmark comparisons and time-form sensitivities derive from saved results. Diagnostic sensitivities never replace or retune the primary model. `diagnostics.py` resumes completed sections only when the data, fits and source checksums match; changed inputs automatically invalidate the cached diagnostics.

The mean/covariance transport assumptions and absent joint dependencies cannot be verified in these data. Gaussian latent-input integration is not validation on complete observed cases. Conditional bootstrap ranges omit nuisance-estimation, selection and structural uncertainty. Marginal 1st-99th support limits do not certify joint support. The initial report does not establish proportional hazards, external validity, wearable fitness calibration or causal rejuvenation.

The additional `transport-plan.md` specifies diagnostic checks after the primary freeze: two-year performance in the later 2015-2016 assay cycle and ten-year performance in 2003-2004. Models are kept fixed, horizons follow administrative coverage, and benchmarks use the exact same rows and weights. These checks do not isolate assay effects, provide untouched new validation, or change the five-year calculator/release status. The full marginal integration does not improve Brier error over either benchmark in these additional comparisons.

The browser evaluator is the only product runtime. All numeric inputs and drafts stay local; only the frozen public model is fetched. `verify-browser.mjs` compares it with Python survival/risk inversion fixtures. The model JSON and report are the final served artifacts; the model version hashes its exact coefficients and release configuration.
