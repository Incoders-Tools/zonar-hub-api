# Skill: Anti-Corruption Layer

Use this skill for all external systems.

Rules:

- isolate external DTOs/contracts
- map provider contracts into internal application/domain contracts
- prevent direct propagation of provider-specific semantics
- keep breakage isolated when provider contracts change
