# Skill: Clean Architecture Evolution

Use this skill when the repository is still small but expected to grow.

Rules:

- implement changes in a way that supports later extraction into Api/Application/Domain/Infrastructure
- keep boundaries explicit even in a single-project phase
- avoid choices that create coupling debt early
