# Skill: Git Commit Workflow

Use this skill whenever staging, committing, or pushing changes to any repository.

Rules:

- before staging anything, list every file that will be included with a one-line description of what changed in each
- propose a commit message and show the full staged diff summary before asking permission
- always ask explicit user permission before running `git commit` — never commit silently
- always ask explicit user permission before running `git push` — never push silently
- never use `--no-verify`, `--force-with-lease`, `--force`, or any flag that bypasses hooks or overwrites remote history without explicit user instruction
- never amend a published commit without explicit user instruction
- if the pre-commit hook fails, report the failure and ask how to proceed — never bypass it
- keep commit messages in imperative present tense, focused on the why not the what
- one logical change per commit; if multiple unrelated changes are staged ask whether to split them
