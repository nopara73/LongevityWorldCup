"""Numerical checks for the new likelihood and age conversion."""
import unittest
import numpy as np
import pandas as pd
from scipy.optimize._numdiff import approx_derivative
from model import Fit, FEATURES, CORE, likelihood, gompertz_integral, predict_risk, reference_age, cubic, weighted_km, survey_weights, learn_distributions
from transport import evaluate_horizon
from uncertainty import paired_brier_improvement, interval


class ModelChecks(unittest.TestCase):
    def test_paired_error_uses_same_censoring_weights_and_keeps_sign(self):
        frame = pd.DataFrame(dict(time=[12., 18., 36.], event=[1, 0, 0], male=[0, 1, 0]))
        # Main error .33; benchmark error (.8²+.4²/.5)/3 = .32.
        gain = paired_brier_improvement(frame, [.1, .2, .3], [.2, .3, .4], np.ones(3), 2)
        self.assertAlmostEqual(gain, -.01)
        self.assertAlmostEqual(paired_brier_improvement(frame, [.1, .2, .3], [.1, .2, .3], np.ones(3), 2), 0)
        self.assertIsNone(interval([1., None, None, None], 4)['range80'])
        self.assertEqual(interval([1., 2., 3., None], 4)['estimableReplicates'], 3)

    def test_diagnostic_horizon_has_known_event_and_censoring_errors(self):
        frame = pd.DataFrame(dict(time=[12., 18., 36.], event=[1, 0, 0], male=[0, 1, 0]))
        # By two years: one death in three, censoring survival 1/2. The
        # censored middle record contributes zero; IPCW error is (.9²+.3²/.5)/3.
        result = evaluate_horizon(frame, np.array([.1, .2, .3]), np.ones(3), 2)
        self.assertAlmostEqual(result['weightedObservedRisk'], 1/3)
        self.assertAlmostEqual(result['observedExpected'], 5/3)
        self.assertAlmostEqual(result['ipcwBrier'], .33)
        self.assertEqual(result['horizonDeaths'], 1)
        self.assertIsNone(result['calibrationSlope'])
        self.assertEqual(result['bySex']['1']['horizonDeaths'], 0)

    def test_combined_cycle_weights_match_cdc_six_year_formula(self):
        frame = pd.DataFrame(dict(cycle=[1999, 2001, 2003], WTMEC4YR=[100., 100., np.nan],
            WTMEC2YR=[500., 500., 200.], WTSSCB4Y=[10., 20., np.nan],
            WTSSCB2Y=[np.nan, np.nan, 90.], WTSAF2YR=[30., 40., 50.]))
        # Dividing the returned numerators by three reproduces the CDC's
        # 2/3 * WTMEC4YR and 1/3 * WTMEC2YR, including subsample weights.
        np.testing.assert_allclose(survey_weights(frame)/3, [200/3, 200/3, 200/3])
        np.testing.assert_allclose(survey_weights(frame, 'cystatin')/3, [20/3, 40/3, 30])
        np.testing.assert_allclose(survey_weights(frame, 'apob'), [30, 40, 50])

    def test_latent_variances_respect_subsample_selection_weights(self):
        # Synthetic separate ApoB and cystatin subsamples. Their transformed
        # values are 0, 1, 2 with unequal inclusion weights: weighted variance
        # is 0.41, while the full MEC sample's variance is 2/3.
        pattern = np.tile([0., 1., 2.], 80)
        apob_sample = np.tile(np.r_[np.ones(60, dtype=bool), np.zeros(60, dtype=bool)], 2)
        frame = pd.DataFrame({f: np.ones(240) for f in CORE})
        frame['age'], frame['male'] = 35., np.repeat([0, 1], 120)
        frame['cycle'] = np.where(apob_sample, 2005, 2003)
        frame['WTMEC4YR'], frame['WTMEC2YR'] = np.nan, 1.
        frame['WTSSCB4Y'] = np.nan
        frame['WTSAF2YR'] = np.where(apob_sample, np.tile([1., 1., 8.], 80), np.nan)
        frame['WTSSCB2Y'] = np.where(~apob_sample, np.tile([8., 1., 1.], 80), np.nan)
        for f in FEATURES[4:]: frame[f] = np.exp(pattern)
        frame.loc[~apob_sample, 'apob'] = np.nan
        frame.loc[apob_sample, 'cystatin'] = np.nan
        curves = {f: dict(center=0., scale=1.) for f in FEATURES}
        fitted = learn_distributions(frame, curves)
        for sex in ['0', '1']:
            covariance = np.array(fitted['covariance'][sex])
            np.testing.assert_allclose(np.diag(covariance), [.41, 2/3, .41, 2/3, 2/3], atol=1e-12)
            self.assertEqual(fitted['observedPairs'][sex]['apob|cystatin'], 0)

    def test_weighted_km_preserves_risk_set_with_censoring_and_ties(self):
        survival = weighted_km(np.array([1, 2, 3, 4, 4]), np.array([1, 0, 1, 0, 0]), np.ones(5), np.array([0, 1, 2, 3, 5]))
        np.testing.assert_allclose(survival, [1, .8, .8, .8*2/3, .8*2/3], atol=1e-12)
        # Scaling survey weights must not change the point estimate.
        scaled = weighted_km(np.array([1, 2, 3, 4, 4]), np.array([1, 0, 1, 0, 0]), np.ones(5)*15372.8, np.array([0, 1, 2, 3, 5]))
        np.testing.assert_allclose(survival, scaled, atol=1e-12)
    def test_marginal_likelihood_gradient_matches_finite_differences(self):
        rng = np.random.default_rng(2026)
        x = rng.normal(size=(11, 8, 7))
        x[:, :, 0] = 1
        theta = np.r_[[-5, .3, .6, .7, -.2, .15, -.1], [0.09, 0.05]]
        args = (x, np.linspace(.1, 15, 11), np.array([1, 0, 0, 1, 0, 1, 0, 1, 0, 0, 1]),
                np.arange(11) % 2, np.linspace(1, 3, 11), np.ones(7), .002)
        _, analytic = likelihood(theta, *args)
        numeric = approx_derivative(lambda v: likelihood(v, *args)[0], theta).ravel()
        np.testing.assert_allclose(analytic, numeric, rtol=2e-5, atol=2e-7)

    def test_zero_time_slope_has_exponential_limit(self):
        a, derivative = gompertz_integral(np.array([0, 1e-8, -1e-8]), 5)
        np.testing.assert_allclose(a, 5, atol=2e-6)
        np.testing.assert_allclose(derivative, 12.5, atol=2e-6)

    def test_repeating_observed_profiles_does_not_change_likelihood(self):
        x = np.array([[[1, 0, -.5, 0]], [[1, 1, 0, .7]]], dtype=float)
        theta = np.array([-5, .4, .8, .9, .1, .08])
        args = (np.array([7., 10.]), np.array([1, 0]), np.array([0, 1]), np.array([1., 2.]), np.ones(4), 0.)
        one = likelihood(theta, x, *args)
        many = likelihood(theta, np.repeat(x, 32, axis=1), *args)
        np.testing.assert_allclose(one[0], many[0], atol=1e-12)
        np.testing.assert_allclose(one[1], many[1], atol=1e-12)

    def test_risk_age_reference_roundtrip_for_both_sexes(self):
        ages = np.array([18., 40., 60., 78., 18., 40., 60., 78.])
        male = np.array([0, 0, 0, 0, 1, 1, 1, 1])
        x = np.column_stack([np.ones(8), male, (ages-45)/10*(1-male), (ages-45)/10*male])[:, None, :]
        fit = Fit(np.array([-6.5, .4, .8, .7]), np.array([.1, .08]), True, '', 0, 0)
        risk = predict_risk(fit, x, male)
        np.testing.assert_allclose(reference_age(risk, male, fit), ages, atol=1e-10)
        self.assertTrue(np.isnan(reference_age(np.array([.99]), np.array([0]), fit)[0]))

    def test_restricted_cubic_basis_has_linear_tails(self):
        x = np.array([-3., -2., -1., 4., 5., 6.])
        z = cubic(x, [0., 1., 2.])
        self.assertAlmostEqual(z[0]-2*z[1]+z[2], 0)
        self.assertAlmostEqual(z[3]-2*z[4]+z[5], 0)


if __name__ == '__main__':
    unittest.main()
