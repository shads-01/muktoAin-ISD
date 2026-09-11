# MuktoAin — Claude Code & AI Agent Instructions

See `AGENTS.md` for full project guidelines, clean architecture boundaries, and technology stack.

## Mandatory Task Progress Tracking Rule

**Every AI coding agent (Antigravity, Claude Code, OpenCode, Codex, Cursor, etc.) MUST strictly adhere to this workflow:**

1. **Check Dependencies:** Before starting any implementation task, check `plans/Dependency_plan.md` to verify that prerequisite tasks are marked completed (`[x]`).
2. **Auto-Update on Completion:** The moment you implement, verify, or complete ANY task defined in the project plans:
   - You **MUST automatically edit `plans/Dependency_plan.md`**.
   - Change the corresponding task checkbox from `- [ ]` to `- [x]`.
   - If completing the task satisfies a checkpoint exit gate, mark the corresponding exit gate `[x]` as well.
3. **No Unrecorded Work:** Never complete a task or prompt without recording completed progress in `plans/Dependency_plan.md`.

## Git/GitHub Attribution Rule

**Claude must never add itself as a contributor, co-author, or committer on this project's git/GitHub history.**

- Do not add `Co-Authored-By: Claude ...` (or any similar AI attribution) trailers to commit messages.
- Do not add "Generated with Claude Code" or similar footers to commit messages or pull request descriptions.
- Do not set commit author/committer identity to Claude or any AI tool.
- Commits and PRs should be attributed solely to the human contributor driving the work.
