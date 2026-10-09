"""Frozen development/temporal evaluation pipeline. Run after prepare.py.

Only derived aggregates/coefficients are exported; respondent-level rows stay ignored.
"""
import os
os.environ.setdefault('OPENBLAS_NUM_THREADS', '2')
os.environ.setdefault('OMP_NUM_THREADS', '2')
from pathlib import Path
import hashlib
import json
from datetime import datetime, timezone
import numpy as np
import pandas as pd
from lifelines import KaplanMeierFitter, CoxPHFitter
from lifelines.utils import concordance_index
from model import (FEATURES, CORE, PANELS, Fit, survey_weights, transform, learn_curves,
                   design, fit_survival, likelihood, predict_risk, learn_distributions,
                   integration_values, dependency_scenario, reference_age)

REPO = Path(__file__).resolve().parents[2]
ROOT = REPO / '.artifacts' / 'mortality-age'
OUT = ROOT / 'results'
OUT.mkdir(parents=True, exist_ok=True)
CONFIG = dict(seed=91237, development=[1999, 2001, 2005, 2007, 2011],
              evaluation=[2003, 2009, 2013], horizonYears=5, primaryDraws=32,
              extraDraws=64, bootstrapReplicates=32,
              candidateGrid=[dict(smooth=False, ridge=r) for r in [0.0003, 0.001, 0.003]]
                            + [dict(smooth=True, ridge=r) for r in [0.001, 0.003]],
              dependenceCorrelations=[-0.3, 0.3, 0.6],
              supportQuantiles=[0.01, 0.99], bootstrapPercentiles=[10, 90])
PLAN_HASH = hashlib.sha256((Path(__file__).parent / 'analysis-plan.md').read_bytes()).hexdigest()
CONFIG['planSha256'] = PLAN_HASH


def save(name, result):
    (OUT / name).write_text(json.dumps(result, indent=2, allow_nan=False))


def say(message):
    print(message, flush=True)


def clean_number(value):
    return float(value) if np.isfinite(value) else None


def count(frame):
    result = dict(n=len(frame), deaths=int(frame.event.sum()),
                  fiveYearDeaths=int(((frame.time <= 60) & (frame.event == 1)).sum()),
                  medianFollowupYears=float(frame.time.median()/12))
    result['bySex'] = {str(s): dict(n=int((frame.male == s).sum()),
        deaths=int(frame.loc[frame.male == s, 'event'].sum()),
        fiveYearDeaths=int(((frame.male == s) & (frame.time <= 60) & (frame.event == 1)).sum())) for s in [0, 1]}
    return result


def internal_mask(frame):
    # Fixed respondent hash, independent of outcomes. 20% used only for internal selection.
    ids = frame.SEQN.to_numpy(dtype=np.uint64)
    return ((ids * np.uint64(2654435761) + np.uint64(CONFIG['seed'])) % np.uint64(10)) >= 2


def observed_design(frame, features, curves, smooth):
    values = (np.column_stack([transform(f, frame[f]) for f in features])[:, None, :]
              if features else np.empty((len(frame), 1, 0)))
    return design(frame, values, features, curves, smooth)


def unpenalized_loss(fit, x, frame, weights):
    theta = np.r_[fit.coefficients, fit.gamma]
    return float(likelihood(theta, x, frame.time.to_numpy()/12, frame.event.to_numpy(),
        frame.male.to_numpy(), weights, np.zeros(x.shape[-1]), 0)[0])


def evaluate(frame, risk, weights, benchmark_risk=None):
    """IPCW five-year Brier and weighted KM calibration; no outcome recoding."""
    t, d = frame.time.to_numpy()/12, frame.event.to_numpy()
    risk = np.asarray(risk)
    km = KaplanMeierFitter().fit(t, d, weights=weights)
    observed = 1-float(km.predict(5))
    censor = KaplanMeierFitter().fit(t, 1-d, weights=weights)
    # Censoring survival immediately before a death; an event at 5 is counted by 5.
    before = np.maximum(t-1e-8, 0)
    gt = censor.predict(before).to_numpy()
    g5 = float(censor.predict(5))
    died = (d == 1) & (t <= 5)
    survived = t > 5
    if g5 <= 0 or np.any(gt[died] <= 0):
        brier = None
    else:
        contribution = np.zeros(len(frame))
        contribution[died] = (1-risk[died])**2/gt[died]
        contribution[survived] = risk[survived]**2/g5
        brier = float(np.average(contribution, weights=weights))
    expected = float(np.average(risk, weights=weights))
    try:
        cindex = float(concordance_index(t, -risk, d))
    except ZeroDivisionError:
        cindex = None
    slope = None
    # Five-year horizon-specific recalibration slope. Avoid fitting sparse strata.
    if died.sum() >= 30:
        slope_frame = pd.DataFrame(dict(t=np.minimum(t, 5), d=died.astype(int),
            logCumulative=np.log(-np.log1p(-risk)), weight=weights/weights.mean()))
        try:
            cf = CoxPHFitter().fit(slope_frame, 't', 'd', weights_col='weight')
            slope = clean_number(cf.params_.iloc[0])
        except Exception:
            slope = None
    result = dict(**count(frame), weightedObserved5y=observed, weightedExpected5y=expected,
                  observedExpected=observed/expected, ipcwBrier5y=brier,
                  unweightedHarrellC=cindex, fiveYearCalibrationSlope=slope,
                  effectiveWeightN=float(weights.sum()**2/np.sum(weights**2)))
    if benchmark_risk is not None:
        base = evaluate(frame, benchmark_risk, weights)
        result['ageSexBenchmark'] = base
        result['brierImprovement'] = None if brier is None else base['ipcwBrier5y']-brier
    result['bySex'] = {}
    for s in [0, 1]:
        ok = frame.male.to_numpy() == s
        if ok.any():
            # Avoid recursive subgroup evaluation.
            sub = frame.iloc[np.flatnonzero(ok)]
            ks = KaplanMeierFitter().fit(t[ok], d[ok], weights=weights[ok])
            obs = 1-float(ks.predict(5))
            exp = float(np.average(risk[ok], weights=weights[ok]))
            result['bySex'][str(s)] = dict(n=len(sub), deaths=int(d[ok].sum()),
                fiveYearDeaths=int(died[ok].sum()), weightedObserved5y=obs,
                weightedExpected5y=exp, observedExpected=obs/exp)
    return result


def benchmark(frame, reference):
    x, _, _ = observed_design(frame, [], {}, False)
    return predict_risk(reference, x, frame.male.to_numpy())


def example_inputs(frame, features, curves):
    result = []
    for s in [0, 1]:
        subset = frame[frame.male == s]
        vals = {}
        for f in features:
            spec = curves[f]
            lo, hi = spec['support'][str(s)]
            median = float(subset[f].median())
            vals[f] = float(min(max(median, lo), hi))
        # Use measured medians only to illustrate an artificial profile, not an observed person.
        result.append(dict(age=40., male=float(s), **vals))
    return result


def model_export(fit, names, features, curves, smooth, ridge, age_max, reference):
    return dict(**fit.json(names), features=features, curves=curves, smooth=smooth,
                ridge=ridge, ageRange=[18, age_max], horizonYears=5,
                reference=reference.json(['intercept', 'male', 'age_female', 'age_male']))


def full_design(frame, curves, distributions, smooth, draws=32):
    return design(frame, integration_values(frame, curves, distributions, draws, CONFIG['seed']),
                  FEATURES, curves, smooth)


def main():
    (OUT / 'frozen-config.json').write_text(json.dumps(CONFIG, indent=2))
    all_data = pd.read_csv(ROOT / 'harmonized.csv')
    data = all_data.dropna(subset=CORE).copy()
    data = data[survey_weights(data) > 0].reset_index(drop=True)
    development = data[data.cycle.isin(CONFIG['development'])].reset_index(drop=True)
    holdout = data[data.cycle.isin(CONFIG['evaluation'])].reset_index(drop=True)
    say(f'Development {count(development)}; holdout allocation frozen, not yet evaluated')
    weights = survey_weights(development)
    xr, names, penalty = observed_design(development, [], {}, False)
    reference = fit_survival(xr, development, weights, penalty, 0)
    if not reference.converged:
        raise RuntimeError('Age/sex reference did not converge')
    say(f'Reference fit {reference.json(names)}')
    reference_json = reference.json(names)
    save('reference.json', reference_json)

    young = development[development.age <= 49].reset_index(drop=True)
    fitmask = internal_mask(young)
    inner, inner_test = young[fitmask].reset_index(drop=True), young[~fitmask].reset_index(drop=True)
    inner_curves = learn_curves(inner, FEATURES)
    inner_distributions = learn_distributions(inner, inner_curves)
    vi = integration_values(inner, inner_curves, inner_distributions, 32, CONFIG['seed'])
    vt = integration_values(inner_test, inner_curves, inner_distributions, 32, CONFIG['seed'])
    selection = []
    for candidate in CONFIG['candidateGrid']:
        x, names, pen = design(inner, vi, FEATURES, inner_curves, candidate['smooth'])
        xt, _, _ = design(inner_test, vt, FEATURES, inner_curves, candidate['smooth'])
        fit = fit_survival(x, inner, survey_weights(inner), pen, candidate['ridge'])
        validation_loss = unpenalized_loss(fit, xt, inner_test, survey_weights(inner_test))
        record = dict(**candidate, validationLoss=validation_loss, fit=fit.json(names))
        selection.append(record)
        save('selection.json', selection)
        say(f'Internal selection: {candidate}, loss={validation_loss:.6f}, convergence={fit.converged}')
    eligible = [r for r in selection if r['fit']['converged']]
    if not eligible:
        raise RuntimeError('No full candidate converged; preserve failures and withhold full prediction')
    selected = min(eligible, key=lambda r: r['validationLoss'])
    smooth, ridge = selected['smooth'], selected['ridge']
    curves = learn_curves(young, FEATURES)
    distributions = learn_distributions(young, curves)
    x, names, pen = full_design(young, curves, distributions, smooth)
    full = fit_survival(x, young, survey_weights(young), pen, ridge)
    if not full.converged:
        raise RuntimeError('Final full fit did not converge; preserve failures and withhold full prediction')
    full_export = model_export(full, names, FEATURES, curves, smooth, ridge, 49, reference)
    full_export['status'] = 'experimental-integration'
    full_export['distributions'] = distributions
    full_export['development'] = count(young)
    full_export['observedCompleteCases'] = 0
    save('full-model.json', full_export)
    say(f'Frozen full candidate: smooth={smooth}, ridge={ridge}; {count(young)}')

    # Fit genuinely observed panels with internal selection before any temporal evaluation.
    panel_models, panel_results = {}, {}
    for label, features in PANELS.items():
        train = development.dropna(subset=features).copy()
        if label == 'fitness':
            train = train[train.age <= 49]
        marker = 'cystatin' if label in ['blood', 'fitness'] else 'apob' if label in ['lipid', 'strength'] else None
        train = train[survey_weights(train, marker) > 0].reset_index(drop=True)
        imask = internal_mask(train)
        it, iv = train[imask].reset_index(drop=True), train[~imask].reset_index(drop=True)
        cs = learn_curves(it, features)
        candidates = []
        for sm in [False, True]:
            xx, nn, pp = observed_design(it, features, cs, sm)
            xv, _, _ = observed_design(iv, features, cs, sm)
            fitted = fit_survival(xx, it, survey_weights(it, marker), pp, 0.001)
            loss = unpenalized_loss(fitted, xv, iv, survey_weights(iv, marker))
            candidates.append(dict(smooth=sm, loss=loss, converged=fitted.converged))
        valid = [c for c in candidates if c['converged']]
        if not valid:
            panel_results[label] = dict(status='fit-failed', selection=candidates)
            continue
        sm = min(valid, key=lambda r: r['loss'])['smooth']
        cs = learn_curves(train, features)
        xx, nn, pp = observed_design(train, features, cs, sm)
        fitted = fit_survival(xx, train, survey_weights(train, marker), pp, 0.001)
        if not fitted.converged:
            panel_results[label] = dict(status='fit-failed', selection=candidates)
            continue
        exported = model_export(fitted, nn, features, cs, sm, 0.001, 49 if label == 'fitness' else 79, reference)
        exported['development'] = count(train)
        exported['status'] = 'observed-panel'
        panel_models[label] = exported
        panel_results[label] = dict(selection=candidates, development=count(train))
        say(f'Observed panel frozen {label}, smooth={sm}, n={len(train)}, deaths={int(train.event.sum())}')
    save('panel-models.json', panel_models)

    # Frozen temporal evaluation starts here; no selection/retraining based on these outputs.
    yt = holdout[holdout.age <= 49].reset_index(drop=True)
    xt, _, _ = full_design(yt, curves, distributions, smooth)
    primary_risk = predict_risk(full, xt, yt.male.to_numpy())
    full_results = dict(development=count(young), temporalEvaluation=evaluate(yt, primary_risk,
        survey_weights(yt), benchmark(yt, reference)),
        evaluationLabel='Missing-input marginal predictions only; not joint eight-domain validation')
    for label, exported in panel_models.items():
        features, cs, sm = exported['features'], exported['curves'], exported['smooth']
        test = holdout.dropna(subset=features).copy()
        test = test[(test.age >= exported['ageRange'][0]) & (test.age <= exported['ageRange'][1])]
        marker = 'cystatin' if label in ['blood', 'fitness'] else 'apob' if label in ['lipid', 'strength'] else None
        test = test[survey_weights(test, marker) > 0].reset_index(drop=True)
        fitted = Fit(np.array(list(exported['coefficients'].values())), np.array(exported['gamma']), True, '', 0, 0)
        xx, _, _ = observed_design(test, features, cs, sm)
        risk = predict_risk(fitted, xx, test.male.to_numpy())
        panel_results[label]['temporalEvaluation'] = evaluate(test, risk, survey_weights(test, marker), benchmark(test, reference))
        # Full integration evaluated on this observed panel, other measurements still marginalized.
        young_test = test[test.age <= 49].reset_index(drop=True)
        if len(young_test):
            fx, _, _ = full_design(young_test, curves, distributions, smooth)
            fr = predict_risk(full, fx, young_test.male.to_numpy())
            panel_results[label]['integratedMarginalUnder50'] = evaluate(young_test, fr, survey_weights(young_test, marker))
    save('panel-results.json', panel_results)
    save('full-results.json', full_results)
    say('Temporal evaluation saved; full predictions remain explicitly exploratory')

    examples = pd.DataFrame(example_inputs(young, FEATURES, curves))
    ex, _, _ = observed_design(examples, FEATURES, curves, smooth)
    erisk = predict_risk(full, ex, examples.male.to_numpy())
    ages = reference_age(erisk, examples.male.to_numpy(), reference)
    profiles = [dict(inputs=row, risk5=float(r), age=clean_number(a))
                for row, r, a in zip(examples.to_dict('records'), erisk, ages)]
    sensitivity = []
    theta = np.r_[full.coefficients, full.gamma]
    for corr in CONFIG['dependenceCorrelations']:
        scenario = dependency_scenario(distributions, corr)
        sx, _, _ = full_design(young, curves, scenario, smooth)
        fitted = fit_survival(sx, young, survey_weights(young), pen, ridge, theta)
        rr = predict_risk(fitted, ex, examples.male.to_numpy())
        aa = reference_age(rr, examples.male.to_numpy(), reference)
        sensitivity.append(dict(correlation=corr, shrinkage=scenario['shrinkage'], fit=fitted.json(names),
            exampleRisk5=rr.tolist(), exampleAge=[clean_number(a) for a in aa]))
        say(f'Dependence sensitivity {corr}: ages {aa.tolist()}, converged={fitted.converged}')
    sx, _, _ = full_design(young, curves, distributions, smooth, 64)
    doubled = fit_survival(sx, young, survey_weights(young), pen, ridge, theta)
    dr = predict_risk(doubled, ex, examples.male.to_numpy())
    da = reference_age(dr, examples.male.to_numpy(), reference)
    more_draws = dict(draws=64, fit=doubled.json(names), exampleRisk5=dr.tolist(), exampleAge=[clean_number(a) for a in da])
    say(f'Doubled integration: ages {da.tolist()}, converged={doubled.converged}')
    save('sensitivity.json', dict(dependence=sensitivity, moreDraws=more_draws, exampleProfiles=profiles))

    # Survey-cluster bootstrap with fixed curves and conditional distributions.
    # Its interval omits nuisance-estimation and structural uncertainty; report explicitly.
    rng = np.random.default_rng(CONFIG['seed']+7)
    bootstrap_fits, bootstrap_profiles = [], []
    base_weights = survey_weights(young)
    for replicate in range(CONFIG['bootstrapReplicates']):
        multipliers = np.zeros(len(young))
        for stratum in young.stratum.unique():
            rows = np.flatnonzero(young.stratum.to_numpy() == stratum)
            units = young.iloc[rows].psu.unique()
            sampled = rng.choice(units, size=len(units), replace=True)
            for unit in units:
                multipliers[rows[young.iloc[rows].psu.to_numpy() == unit]] = np.sum(sampled == unit)
        fit = fit_survival(x, young, base_weights*multipliers, pen, ridge, theta, maxiter=200)
        rr = predict_risk(fit, ex, examples.male.to_numpy())
        aa = reference_age(rr, examples.male.to_numpy(), reference)
        bootstrap_fits.append(dict(replicate=replicate, fit=fit.json(names)))
        bootstrap_profiles.append([clean_number(a) for a in aa])
        save('bootstrap.json', dict(fits=bootstrap_fits, exampleAges=bootstrap_profiles,
            intervalPercentiles=CONFIG['bootstrapPercentiles'],
            conditionalOn='Fixed curves, conditional measurement distributions, selected model and age reference'))
        say(f'Cluster bootstrap {replicate+1}/{CONFIG["bootstrapReplicates"]}, converged={fit.converged}')

    # Correct two-year survivor landmark: start the clock anew at age+2, conditional on survival.
    landmark = young[young.time > 24].copy().reset_index(drop=True)
    landmark['time'] -= 24
    landmark['age'] += 2
    lx, ln, lp = full_design(landmark, curves, distributions, smooth)
    fitted = fit_survival(lx, landmark, survey_weights(landmark), lp, ridge, theta)
    lr = predict_risk(fitted, ex, examples.male.to_numpy())
    la = reference_age(lr, examples.male.to_numpy(), reference)
    save('landmark.json', dict(development=count(landmark), fit=fitted.json(ln),
        exampleRisk5=lr.tolist(), exampleAge=[clean_number(a) for a in la],
        caveat='Baseline biomarkers are two years old; selection and reverse causality are not removed'))
    say(f'Landmark sensitivity ages={la.tolist()}, converged={fitted.converged}')

    # Actual component curves and profile sensitivities for report and calculator fixtures.
    grid_results = {}
    for s in [0, 1]:
        profile = examples.iloc[[s]]
        for f in FEATURES:
            lo, hi = curves[f]['support'][str(s)]
            grid = np.linspace(lo, hi, 81)
            temp = pd.concat([profile]*len(grid), ignore_index=True)
            temp[f] = grid
            gx, _, _ = observed_design(temp, FEATURES, curves, smooth)
            rr = predict_risk(full, gx, temp.male.to_numpy())
            aa = reference_age(rr, temp.male.to_numpy(), reference)
            grid_results[f'{f}_{s}'] = dict(input=grid.tolist(), risk5=rr.tolist(),
                age=[clean_number(a) for a in aa])
    save('curves.json', grid_results)
    save('fixtures.json', dict(profiles=profiles, curves=grid_results))
    save('run.json', dict(completedUtc=datetime.now(timezone.utc).isoformat(), config=CONFIG,
        name='Mortality age', fullPanelStatus='exploratory; no observed joint validation',
        panelModels=list(panel_models), fullFit=full.json(names)))
    say('Training, sensitivity analysis, and evaluation completed')


if __name__ == '__main__':
    import warnings
    from lifelines.exceptions import StatisticalWarning
    warnings.filterwarnings('ignore', category=StatisticalWarning)
    main()
