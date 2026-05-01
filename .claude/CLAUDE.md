# Claude Local Bootstrap

Follow `/AGENTS.md` first.

This repository uses `.github/` as the canonical instruction system.
Do not create or follow a separate competing rule set inside `.claude/`.

Primary contract:
- `/AGENTS.md`
- `/.github/copilot-instructions.md`
- `/.github/instructions/**/*.instructions.md`
- `/.github/skills/**/SKILL.md`
- `/.github/agents/*.agent.md`
- `/docs/architecture/**`

Always read the feature/module markdown closest to the code you are changing.

When implementing backend features:
- inspect the consuming frontend contract if available
- design APIs for multiple clients, not only Angular
- keep all outward contracts documented through OpenAPI
- keep all outward messages localization-ready
