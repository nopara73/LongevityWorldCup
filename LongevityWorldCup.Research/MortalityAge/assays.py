"""NHANES assay harmonization in the source measurement units."""
import numpy as np


def harmonize_apob(values_mg_dl, cycle):
    """Map 2005–2006 BN100 ApoB to the 2007–2008 BN ProSpec scale.

    CDC's paired-sample Deming equation is ProSpec = 0.923 * BN100.
    Apply once to raw LBXAPB (mg/dL); subsequent cycles use ProSpec already.
    https://wwwn.cdc.gov/Nchs/Data/Nhanes/Public/2007/DataFiles/APOB_E.htm
    """
    return np.asarray(values_mg_dl, dtype=float)*np.where(np.asarray(cycle)==2005,.923,1.)
