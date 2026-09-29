"""Shared strict validation for the frozen TrailGuard v2 numeric feature contract."""

from __future__ import annotations

import numpy as np
import pandas as pd

from .constants import NUMERIC_CONSTRAINTS


def numeric_validation_errors(series: pd.Series, column: str) -> list[str]:
    """Return all contract violations for one numeric feature without changing it."""
    constraint = NUMERIC_CONSTRAINTS[column]
    errors: list[str] = []
    if series.isna().any():
        errors.append(f"{column} must not contain missing values.")

    bool_values = series.map(lambda value: isinstance(value, (bool, np.bool_)))
    if bool_values.any():
        errors.append(f"{column} must be numeric; boolean values are not allowed.")

    if not pd.api.types.is_numeric_dtype(series) or pd.api.types.is_bool_dtype(series) or pd.api.types.is_complex_dtype(series):
        errors.append(f"{column} must have a real numeric type, not {series.dtype}.")
        return errors

    values = series.to_numpy(dtype=float, na_value=np.nan)
    finite = np.isfinite(values)
    if not finite.all():
        errors.append(f"{column} must contain only finite numeric values.")
    finite_values = values[finite]
    if not len(finite_values):
        return errors

    if constraint["integer"] and not np.equal(np.mod(finite_values, 1), 0).all():
        errors.append(f"{column} must contain whole numbers.")
    if "minimum" in constraint and (finite_values < constraint["minimum"]).any():
        errors.append(f"{column} must be at least {constraint['minimum']}.")
    if "exclusive_minimum" in constraint and (finite_values <= constraint["exclusive_minimum"]).any():
        errors.append(f"{column} must be greater than {constraint['exclusive_minimum']}.")
    if (finite_values > constraint["maximum"]).any():
        errors.append(f"{column} must be at most {constraint['maximum']}.")
    return errors
