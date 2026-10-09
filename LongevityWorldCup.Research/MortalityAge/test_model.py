"""Numerical checks for the new likelihood and age conversion."""
import unittest
import numpy as np
from scipy.optimize._numdiff import approx_derivative
from model import Fit, likelihood, gompertz_integral, predict_risk, reference_age, cubic, weighted_km


class ModelChecks(unittest.TestCase):
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
