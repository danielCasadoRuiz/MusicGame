"""Original WINDOWS v1.1 triangle-budget rule, parameterized per LOD."""
def triangle_budget_passed(actual, requested):
    return 0.9 * requested <= actual <= 1.1 * requested
