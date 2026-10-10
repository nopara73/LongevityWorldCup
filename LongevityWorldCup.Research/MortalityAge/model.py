"""Survival likelihood, sex-dependent curves, and explicit missing-input integration.

The Monte Carlo rows are quadrature draws, never claimed to be observed people.
All follow-up is in years. Model coefficients describe associations, not effects.
"""
from dataclasses import dataclass
import numpy as np
from scipy.optimize import minimize
from scipy.special import logsumexp

FEATURES = ['sbp', 'dbp', 'whr', 'hba1c', 'apob', 'crp', 'cystatin', 'grip', 'vo2']
CORE = FEATURES[:4]
LOG_FEATURES = {'apob', 'crp', 'cystatin', 'grip', 'vo2'}
PANELS = {
    'core': CORE,
    'blood': CORE + ['crp', 'cystatin'],
    'lipid': CORE + ['apob', 'crp'],
    'strength': CORE + ['apob', 'grip'],
    'fitness': CORE + ['crp', 'cystatin', 'vo2'],
}


def weighted_km(time, event, weights, at):
    """Weighted Kaplan-Meier point estimates without a frequency-weight variance.

    Bootstrap supplies design uncertainty separately. Direct reverse cumulative
    weights avoid cancellation of large survey weights at the last event time.
    """
    times, inverse = np.unique(time, return_inverse=True)
    totals = np.bincount(inverse, weights=weights)
    deaths = np.bincount(inverse, weights=np.asarray(weights)*np.asarray(event))
    at_risk = np.cumsum(totals[::-1])[::-1]
    survival = np.cumprod(1-np.minimum(deaths/at_risk, 1))
    indices = np.searchsorted(times, at, side='right')-1
    return np.where(indices < 0, 1, survival[np.maximum(indices, 0)])


def weighted_quantile(x, weights, probabilities):
    order = np.argsort(x)
    x, weights = np.asarray(x)[order], np.asarray(weights)[order]
    return np.interp(probabilities, (np.cumsum(weights) - weights / 2) / weights.sum(), x)


def survey_weights(frame, marker=None):
    early = frame.cycle.isin([1999, 2001]).to_numpy()
    # CDC combined-cycle formula: 2/K * four-year weights versus 1/K *
    # two-year weights. The common K cancels in all normalized estimators.
    mec = np.where(early, 2 * frame.WTMEC4YR.to_numpy(), frame.WTMEC2YR.to_numpy())
    if marker == 'cystatin':
        special = np.where(early, 2 * frame.WTSSCB4Y.to_numpy(), frame.WTSSCB2Y.to_numpy())
        mec = np.where(np.isfinite(special) & (special > 0), special, 0)
    elif marker == 'apob':
        special = frame.WTSAF2YR.to_numpy()
        mec = np.where(np.isfinite(special) & (special > 0), special, 0)
    return np.where(np.isfinite(mec) & (mec > 0), mec, 0)


def transform(feature, values):
    values = np.asarray(values, dtype=float)
    if feature in LOG_FEATURES:
        return np.log(values)
    return values


def untransform(feature, values):
    return np.exp(values) if feature in LOG_FEATURES else values


def cubic(x, knots):
    a, b, c = knots
    return (np.maximum(x-a, 0)**3 - np.maximum(x-b, 0)**3 * (c-a)/(c-b)
            + np.maximum(x-c, 0)**3 * (b-a)/(c-b)) / (c-a)**2


def learn_curves(frame, features):
    curves = {}
    for feature in features:
        observed = frame[np.isfinite(frame[feature]) & (frame[feature] > 0)]
        marker = 'cystatin' if feature == 'cystatin' else 'apob' if feature == 'apob' else None
        weights = survey_weights(observed, marker)
        x = transform(feature, observed[feature])
        knots = weighted_quantile(x, weights, [0.1, 0.5, 0.9])
        center = np.average(x, weights=weights)
        scale = np.sqrt(np.average((x-center)**2, weights=weights))
        z = cubic(x, knots)
        ncenter = np.average(z, weights=weights)
        nscale = np.sqrt(np.average((z-ncenter)**2, weights=weights))
        support = {}
        for male in [0, 1]:
            subset = observed[observed.male == male]
            sw = survey_weights(subset, marker)
            lo, hi = weighted_quantile(subset[feature].to_numpy(), sw, [0.01, 0.99])
            support[str(male)] = [float(lo), float(hi)]
        curves[feature] = dict(log=feature in LOG_FEATURES, knots=knots.tolist(),
            center=float(center), scale=float(scale), nonlinearCenter=float(ncenter),
            nonlinearScale=float(max(nscale, 1e-8)), support=support, observedN=len(observed))
    return curves


def design(frame, values, features, curves, smooth):
    """values: n x quadrature_draws x features, already on the transformed scale."""
    n, draws, _ = values.shape
    male = np.broadcast_to(frame.male.to_numpy()[:, None], (n, draws))
    columns = [np.ones((n, draws)), male]
    names = ['intercept', 'male']
    penalty = [0, 0.05]
    for j, feature in enumerate(features):
        spec = curves[feature]
        z = (values[:, :, j]-spec['center']) / spec['scale']
        columns += [z]
        names += [feature + '_linear']
        penalty += [1]
        if smooth:
            nz = (cubic(values[:, :, j], spec['knots'])-spec['nonlinearCenter']) / spec['nonlinearScale']
            columns += [nz]
            names += [feature + '_nonlinear']
            penalty += [4]
        # Every component has a sex-specific slope, including fitness and grip.
        columns += [z*(male-0.5)]
        names += [feature + '_sex']
        penalty += [8]
    return np.stack(columns, axis=-1), names, np.asarray(penalty)


def reference_design(frame):
    """Historical risk-to-years ruler and research benchmark, never a user risk model."""
    male = frame.male.to_numpy()
    age = (frame.age.to_numpy() - 45) / 10
    values = np.column_stack([np.ones(len(frame)), male, age*(1-male), age*male])[:, None, :]
    return values, ['intercept', 'male', 'age_female', 'age_male'], np.array([0, .05, .02, .02])


def gompertz_integral(gamma, time):
    gamma, time = np.broadcast_arrays(gamma, time)
    small = np.abs(gamma) < 1e-5
    safe = np.where(small, 1, gamma)
    gt = gamma*time
    a = np.where(small, time + gamma*time**2/2 + gamma**2*time**3/6,
                 np.expm1(gt)/safe)
    derivative = np.where(small, time**2/2 + gamma*time**3/3 + gamma**2*time**4/8,
                          ((gt-1)*np.exp(gt)+1)/safe**2)
    return a, derivative


@dataclass
class Fit:
    coefficients: np.ndarray
    gamma: np.ndarray
    converged: bool
    message: str
    objective: float
    iterations: int

    def json(self, names):
        return dict(coefficients=dict(zip(names, self.coefficients.tolist())),
                    gamma=self.gamma.tolist(), converged=self.converged,
                    message=self.message, objective=self.objective, iterations=self.iterations)


def likelihood(theta, x, time, event, male, weights, penalties, ridge):
    n, draws, p = x.shape
    beta, gamma = theta[:p], theta[p:]
    eta = (x.reshape(-1, p) @ beta).reshape(n, draws)
    rate = np.exp(eta)
    g = gamma[male.astype(int)]
    a, da = gompertz_integral(g, time)
    cumulative = rate * a[:, None]
    logl = event[:, None]*(eta+g[:, None]*time[:, None])-cumulative
    normalizer = logsumexp(logl, axis=1)
    posterior = np.exp(logl-normalizer[:, None])
    normalized_weights = weights / weights.sum()
    objective = -np.dot(normalized_weights, normalizer - np.log(draws))
    objective += ridge * np.dot(penalties*beta, beta)/2
    score_eta = posterior*(event[:, None]-cumulative)*normalized_weights[:, None]
    gradient_beta = -x.reshape(-1, p).T @ score_eta.reshape(-1) + ridge*penalties*beta
    score_gamma = (posterior*(event[:, None]*time[:, None]-rate*da[:, None])).sum(axis=1)
    gradient_gamma = [-np.dot(normalized_weights*(male == s), score_gamma) for s in [0, 1]]
    return objective, np.r_[gradient_beta, gradient_gamma]


def fit_survival(x, frame, weights, penalties, ridge=0.001, initial=None, maxiter=300, fixed_gamma=None, age_columns=()):
    p = x.shape[-1]
    time, event, male = frame.time.to_numpy()/12, frame.event.to_numpy(), frame.male.to_numpy()
    if initial is None:
        initial = np.zeros(p+2)
        initial[0] = np.log(max(np.average(event, weights=weights)/np.average(time, weights=weights), 1e-5))
        for col in age_columns:
            initial[col] = 0.8
        initial[p:] = 0.06
    gamma_bounds = [(-0.1, 0.2)]*2 if fixed_gamma is None else [(fixed_gamma, fixed_gamma)]*2
    bounds = [(-15, 0), (-3, 3)] + [(None, None)]*(p-2)
    for col in age_columns:
        if not 2 <= col < p:
            raise ValueError('Invalid reference age column')
        bounds[col] = (0.01, 4)
    bounds += gamma_bounds
    result = minimize(likelihood, initial, args=(x, time, event, male, weights, penalties, ridge),
                      method='L-BFGS-B', jac=True, bounds=bounds,
                      options=dict(maxiter=maxiter, ftol=1e-10, gtol=2e-6, maxls=40))
    return Fit(result.x[:p], result.x[p:], bool(result.success), str(result.message),
               float(result.fun), int(result.nit))


def predict_risk(fit, x, male, horizon=5):
    eta = x @ fit.coefficients
    integral, _ = gompertz_integral(fit.gamma[np.asarray(male, dtype=int)], horizon)
    cumulative = np.exp(eta)*integral[:, None]
    return (-np.expm1(-cumulative)).mean(axis=1)


def conditional_predictors(frame, curves):
    male = frame.male.to_numpy()
    columns = [np.ones(len(frame)), male]
    for feature in CORE:
        spec = curves[feature]
        z = (transform(feature, frame[feature])-spec['center'])/spec['scale']
        columns += [z, z*(male-0.5)]
    return np.column_stack(columns)


def positive_covariance(cov, floor=0.05):
    """Shrink towards independent residuals only enough to obtain a valid covariance."""
    shrink = 0.0
    while np.linalg.eigvalsh((1-shrink)*cov + shrink*np.diag(np.diag(cov))).min() < floor:
        shrink += 0.01
        if shrink > 1:
            raise ValueError('Invalid residual variances')
    return (1-shrink)*cov + shrink*np.diag(np.diag(cov)), round(shrink, 2)


def learn_distributions(frame, curves):
    predictors = conditional_predictors(frame, curves)
    residuals, regressions = [], {}
    for feature in FEATURES[4:]:
        observed = np.isfinite(frame[feature]) & (frame[feature] > 0)
        weights = survey_weights(frame, 'cystatin' if feature == 'cystatin' else 'apob' if feature == 'apob' else None)
        observed &= weights > 0
        y = (transform(feature, frame[feature])-curves[feature]['center'])/curves[feature]['scale']
        z, w, yo = predictors[observed], weights[observed], y[observed]
        w = w / w.mean()
        coef = np.linalg.solve(z.T @ (z*w[:, None]) + np.eye(z.shape[1])*20, z.T @ (w*yo))
        resid = y.to_numpy()-predictors @ coef if hasattr(y, 'to_numpy') else y-predictors @ coef
        resid[~observed] = np.nan
        regressions[feature] = coef.tolist()
        residuals.append(resid)
    residuals = np.column_stack(residuals)
    covariance, pairs, shrinkage = {}, {}, {}
    for sex in [0, 1]:
        cov = np.zeros((5, 5))
        pairs[str(sex)] = {}
        for j in range(5):
            for k in range(j, 5):
                ok = (frame.male.to_numpy() == sex) & np.isfinite(residuals[:, j]) & np.isfinite(residuals[:, k])
                pairs[str(sex)][FEATURES[4+j]+'|'+FEATURES[4+k]] = int(ok.sum())
                if ok.sum() >= 50:
                    # Both diagonal variances and paired covariances must use
                    # the subsample weight that covers their measured markers.
                    pair = [FEATURES[4+j], FEATURES[4+k]]
                    marker = 'cystatin' if 'cystatin' in pair else 'apob' if 'apob' in pair else None
                    w = survey_weights(frame, marker)[ok]
                    rj, rk = residuals[ok, j], residuals[ok, k]
                    cov[j, k] = cov[k, j] = np.average((rj-np.average(rj, weights=w))*(rk-np.average(rk, weights=w)), weights=w)
        cov, shrink = positive_covariance(cov)
        covariance[str(sex)], shrinkage[str(sex)] = cov.tolist(), shrink
    return dict(regressions=regressions, covariance=covariance, observedPairs=pairs, shrinkage=shrinkage)


def dependency_scenario(distributions, correlation):
    import copy
    result = copy.deepcopy(distributions)
    for sex in [0, 1]:
        key = str(sex)
        cov = np.array(result['covariance'][key])
        for j in range(5):
            for k in range(j+1, 5):
                pair = FEATURES[4+j]+'|'+FEATURES[4+k]
                if result['observedPairs'][key][pair] < 50:
                    cov[j, k] = cov[k, j] = correlation*np.sqrt(cov[j, j]*cov[k, k])
        cov, shrink = positive_covariance(cov)
        result['covariance'][key] = cov.tolist()
        result['shrinkage'][key] = shrink
    return result


def integration_values(frame, curves, distributions, draws=32, seed=91237):
    if draws % 2:
        raise ValueError('Antithetic integration requires an even number of draws')
    n = len(frame)
    values = np.repeat(np.column_stack([transform(f, frame[f]) for f in FEATURES])[:, None, :], draws, axis=1)
    predictors = conditional_predictors(frame, curves)
    predicted = predictors @ np.array([distributions['regressions'][f] for f in FEATURES[4:]]).T
    measured = np.column_stack([(transform(f, frame[f])-curves[f]['center'])/curves[f]['scale'] for f in FEATURES[4:]])
    rng = np.random.default_rng(seed)
    half = rng.normal(size=(n, draws//2, 5))
    normal = np.concatenate([half, -half], axis=1)
    pattern = np.isfinite(measured).astype(int) @ (2**np.arange(5))
    for sex in [0, 1]:
        covariance = np.array(distributions['covariance'][str(sex)])
        for code in np.unique(pattern[frame.male.to_numpy() == sex]):
            rows = np.flatnonzero((pattern == code) & (frame.male.to_numpy() == sex))
            observed = np.flatnonzero(code & (2**np.arange(5)))
            missing = np.setdiff1d(np.arange(5), observed)
            if not len(missing):
                continue
            mean = predicted[rows][:, missing]
            vc = covariance[np.ix_(missing, missing)]
            if len(observed):
                cross = covariance[np.ix_(missing, observed)] @ np.linalg.inv(covariance[np.ix_(observed, observed)])
                mean += (measured[rows][:, observed]-predicted[rows][:, observed]) @ cross.T
                vc -= cross @ covariance[np.ix_(observed, missing)]
            sampled = mean[:, None, :] + normal[rows][:, :, missing] @ np.linalg.cholesky(vc).T
            for j, col in enumerate(missing):
                feature = FEATURES[col+4]
                values[rows, :, col+4] = sampled[:, :, j]*curves[feature]['scale']+curves[feature]['center']
    if not np.isfinite(values).all():
        raise ValueError('Nonfinite quadrature values')
    return values


def reference_age(risk, male, reference, limits=(18, 79)):
    male = np.asarray(male, dtype=int)
    beta = reference.coefficients
    integral, _ = gompertz_integral(reference.gamma[male], 5)
    age = 45 + 10*(np.log(-np.log1p(-risk))-np.log(integral)-beta[0]-beta[1]*male)/beta[2+male]
    return np.where((age >= limits[0]) & (age <= limits[1]), age, np.nan)
