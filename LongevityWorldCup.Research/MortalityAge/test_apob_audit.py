"""Numerical and risk-set checks for the independent ApoB audit."""
import unittest
import numpy as np
import pandas as pd
from lifelines import CoxPHFitter
from scipy.optimize._numdiff import approx_derivative
from apob_cox import Cox, rcs
from apob_audit import landmark
from assays import harmonize_apob


class ApoBAuditTests(unittest.TestCase):
    def test_apob_calibration_preserves_units_and_other_cycles(self):
        raw=np.array([100.,100.,100.,np.nan])
        np.testing.assert_allclose(harmonize_apob(raw,[2005,2007,2013,2005]),[92.3,100.,100.,np.nan])
        np.testing.assert_allclose(raw,[100.,100.,100.,np.nan])
        self.assertAlmostEqual(harmonize_apob(100.,2005),92.3)

    def test_weighted_breslow_matches_explicit_risk_sets(self):
        x=np.array([[1.,2.],[-1.,.4],[.2,-.5],[.8,1.1],[1.3,-1.]])
        time=np.array([3.,1.,3.,2.,4.])
        event=np.array([1.,1.,0.,1.,0.])
        weights=np.array([2.,.3,1.4,1.,.9])
        beta=np.array([.3,-.2])
        fitter=Cox(x,time,event,weights)
        w=weights/weights.mean()
        eta=x@beta
        expected=0.
        for t in np.unique(time[event==1]):
            d=(time==t)&(event==1)
            expected-=np.dot(w[d],eta[d])-w[d].sum()*np.log(np.sum(w[time>=t]*np.exp(eta[time>=t])))
        self.assertAlmostEqual(fitter.objective(beta)[0],expected,places=12)
        numeric=approx_derivative(lambda b:np.array([fitter.objective(b)[0]]),beta).ravel()
        np.testing.assert_allclose(fitter.objective(beta)[1],numeric,atol=1e-8)
        info,scores=fitter.information_and_scores(beta)
        numeric_info=approx_derivative(lambda b:fitter.objective(b)[1],beta)
        np.testing.assert_allclose(info,numeric_info,atol=1e-8)
        np.testing.assert_allclose(scores.sum(axis=0),-fitter.objective(beta)[1],atol=1e-12)
        np.testing.assert_allclose(Cox(x,time,event,weights*17).objective(beta)[1],numeric,atol=1e-8)

    def test_no_ties_agrees_with_independent_lifelines_fit(self):
        rng=np.random.default_rng(470)
        x=rng.normal(size=(500,2))
        death=rng.exponential(size=500)/np.exp(x@np.array([.45,-.2]))
        censor=rng.exponential(2,size=500)
        frame=pd.DataFrame(dict(x0=x[:,0],x1=x[:,1],time=np.minimum(death,censor),
            event=(death<=censor).astype(int),stratum=np.repeat(np.arange(10),50),
            psu=np.tile(np.repeat([1,2],25),10)))
        fit=Cox(x,frame.time,frame.event,np.ones(500)).fit(frame)
        reference=CoxPHFitter().fit(frame[['x0','x1','time','event']],duration_col='time',event_col='event')
        self.assertTrue(fit['converged'])
        np.testing.assert_allclose(fit['beta'],reference.params_.to_numpy(),atol=2e-6)
        np.testing.assert_allclose(fit['modelCovariance'],reference.variance_matrix_.to_numpy(),atol=2e-7)
        self.assertLess(fit['scoreSumError'],1e-10)
        self.assertEqual(fit['designDf'],10)

    def test_landmark_removes_all_pre_landmark_time(self):
        f=pd.DataFrame(dict(time=[11.,12.,13.,36.,80.],event=[1,0,1,1,0],SEQN=[1,2,3,4,5]))
        one=landmark(f,1)
        self.assertEqual(one.SEQN.tolist(),[3,4,5])
        self.assertEqual(one.time.tolist(),[1.,24.,68.])
        five=landmark(f,5)
        self.assertEqual(five.SEQN.tolist(),[5])
        self.assertEqual(five.time.tolist(),[20.])
        self.assertEqual(f.time.tolist(),[11.,12.,13.,36.,80.])

    def test_spline_has_linear_tails(self):
        basis=rcs(np.array([-3.,-2.,-1.,4.,5.,6.]),[0.,1.,3.])
        np.testing.assert_allclose(np.diff(basis[:3],n=2,axis=0),0,atol=1e-12)
        np.testing.assert_allclose(np.diff(basis[3:],n=2,axis=0),0,atol=1e-12)

    def test_invalid_followup_and_events_fail(self):
        for time,event in [([0.,2.],[1,0]),([1.,np.nan],[1,0]),([1.,2.],[0,0]),([1.,2.],[1,.5])]:
            with self.assertRaises(ValueError):Cox([[1.],[2.]],time,event,[1.,1.])


if __name__=='__main__':unittest.main()
