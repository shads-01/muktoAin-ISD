# MuktoAin — Claude Code & AI Agent Instructions

See `AGENTS.md` for full project guidelines, clean architecture boundaries, and technology stack.

## Git Commit Policy

**NEVER commit, stage, push, or amend — ever.** Shads (the human) is the sole committer. Skip any plan/skill step that says `git commit` — leave changes in the working tree.

## Mandatory Task Progress Tracking Rule

**Every AI coding agent (Antigravity, Claude Code, OpenCode, Codex, Cursor, etc.) MUST strictly adhere to this workflow:**

1. **Check Dependencies:** Before starting any implementation task, check `plans/Dependency_plan.md` to verify that prerequisite tasks are marked completed (`[x]`).
2. **Auto-Update on Completion:** The moment you implement, verify, or complete ANY task defined in the project plans:
   - You **MUST automatically edit `plans/Dependency_plan.md`**.
   - Change the corresponding task checkbox from `- [ ]` to `- [x]`.
   - If completing the task satisfies a checkpoint exit gate, mark the corresponding exit gate `[x]` as well.
3. **No Unrecorded Work:** Never complete a task or prompt without recording completed progress in `plans/Dependency_plan.md`.
