"""Weighted Breslow Cox diagnostics with NHANES stratum/PSU sandwich variance.

These research fits never export a calculator. Spline knots are held fixed for
the sandwich variance. Participants, rather than integration draws, are units.
"""
import numpy as np
from scipy.optimize import minimize
from scipy.stats import t as student_t


def rcs(values, knots):
    """Linear term and K-2 restricted cubic terms, linear outside boundary knots."""
    x = np.asarray(values, dtype=float)
    k = np.asarray(knots, dtype=float)
    if len(k) < 3 or not np.all(np.diff(k) > 0):
        raise ValueError('At least three strictly increasing knots required')
    a, b = k[-2:]
    out = [x]
    for q in k[:-2]:
        out.append((np.maximum(x-q, 0)**3 - np.maximum(x-a, 0)**3*(b-q)/(b-a)
                    + np.maximum(x-b, 0)**3*(a-q)/(b-a)) / (k[-1]-k[0])**2)
    return np.column_stack(out)


class Cox:
    """Case-weighted partial likelihood; complete risk sets and Breslow ties."""
    def __init__(self, x, time, event, weights, penalty=0):
        x, time, event, weights = (np.asarray(v, float) for v in (x, time, event, weights))
        if (x.ndim != 2 or not len(x) or x.shape[1] == 0 or
                any(v.shape != (len(x),) for v in (time, event, weights))):
            raise ValueError('A nonempty design and one time/event/weight per participant required')
        if not np.isfinite(time).all() or not np.all(time > 0):
            raise ValueError('Finite positive follow-up required')
        if not np.isin(event, [0, 1]).all() or event.sum() == 0:
            raise ValueError('Binary events with at least one death required')
        self.order = np.argsort(time, kind='stable')
        self.x = np.asarray(x, float)[self.order]
        self.event = np.asarray(event, float)[self.order]
        self.w = np.asarray(weights, float)[self.order]
        self.w = self.w / self.w.mean()
        if not np.isfinite(self.x).all() or not np.all(self.w > 0):
            raise ValueError('Finite complete cases and positive weights required')
        self.times, self.starts, self.inverse = np.unique(
            np.asarray(time)[self.order], return_index=True, return_inverse=True)
        self.dw = np.bincount(self.inverse, weights=self.w*self.event)
        self.ex = np.sum(self.x*(self.w*self.event)[:, None], axis=0)
        self.penalty = np.broadcast_to(np.asarray(penalty, float), self.x.shape[1])
        if not np.isfinite(self.penalty).all() or (self.penalty < 0).any():
            raise ValueError('Finite nonnegative penalties required')

    def moments(self, beta):
        eta = self.x @ beta
        offset = eta.max()
        risk = self.w*np.exp(eta-offset)
        s0 = np.cumsum(risk[::-1])[::-1][self.starts]
        s1 = np.cumsum((self.x*risk[:, None])[::-1], axis=0)[::-1][self.starts]
        means = s1/s0[:, None]
        return eta, offset, risk, s0, means

    def objective(self, beta):
        eta, offset, _, s0, means = self.moments(beta)
        loss = -(np.dot(self.event*self.w, eta)-np.dot(self.dw, np.log(s0)+offset))
        gradient = -(self.ex-np.sum(self.dw[:, None]*means, axis=0))
        return loss + np.dot(self.penalty*beta, beta)/2, gradient+self.penalty*beta

    def information_and_scores(self, beta):
        _, _, risk, s0, means = self.moments(beta)
        p = self.x.shape[1]
        info = np.zeros((p, p))
        for j in range(p):
            s2 = np.cumsum((self.x*self.x[:, j, None]*risk[:, None])[::-1], axis=0)[::-1][self.starts]
            info[j] = np.sum(self.dw[:, None]*(s2/s0[:, None]-means[:, j, None]*means), axis=0)
        # Breslow counting-process score contributions, including censored people.
        increments = self.dw/s0
        h0 = np.cumsum(increments)[self.inverse]
        h1 = np.cumsum(increments[:, None]*means, axis=0)[self.inverse]
        scores = self.w[:, None]*self.event[:, None]*(self.x-means[self.inverse])
        scores -= risk[:, None]*(self.x*h0[:, None]-h1)
        return (info+info.T)/2+np.diag(self.penalty), scores

    def fit(self, frame, initial=None, design_units=None):
        result = minimize(self.objective, np.zeros(self.x.shape[1]) if initial is None else initial,
                          method='BFGS', jac=True, options={'gtol': 1e-6, 'maxiter': 250})
        info, scores = self.information_and_scores(result.x)
        bread = np.linalg.inv(info)
        f = frame.iloc[self.order]
        clusters = np.column_stack([f.stratum, f.psu])
        units = (np.unique(clusters, axis=0) if design_units is None else np.asarray(design_units))
        meat = np.zeros_like(info)
        df = 0
        for s in np.unique(units[:, 0]):
            psus = units[units[:, 0] == s, 1]
            if len(psus) < 2:
                raise ValueError('Lonely survey PSU: provide the complete design universe')
            u = np.array([scores[(clusters[:, 0] == s)&(clusters[:, 1] == p)].sum(axis=0) for p in psus])
            u -= u.mean(axis=0)
            meat += len(psus)/(len(psus)-1)*(u.T @ u)
            df += len(psus)-1
        # A numerical convergence test also handles BFGS precision-loss messages.
        gradient = self.objective(result.x)[1]
        return dict(beta=result.x, modelCovariance=bread, surveyCovariance=bread @ meat @ bread,
                    converged=bool(np.max(np.abs(gradient)) < 1e-4), message=str(result.message),
                    maxGradient=float(np.max(np.abs(gradient))), iterations=int(result.nit),
                    scoreSumError=float(np.max(np.abs(scores.sum(axis=0)+gradient-self.penalty*result.x))),
                    logLikelihood=float(-self.objective(result.x)[0]), designDf=int(df))


def contrast(fit, row, survey=True):
    delta = np.asarray(row)
    estimate = float(delta @ fit['beta'])
    cov = fit['surveyCovariance'] if survey else fit['modelCovariance']
    se = np.sqrt(max(float(delta @ cov @ delta), 0))
    q = student_t.ppf(.975, fit['designDf']) if survey else 1.959963984540054
    return dict(hr=float(np.exp(estimate)), low=float(np.exp(estimate-q*se)),
                high=float(np.exp(estimate+q*se)), logSE=float(se))
