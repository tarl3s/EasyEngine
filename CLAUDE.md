@AGENTS.md

# Claude Code specifics

Everything in `AGENTS.md` applies. This adds what only matters inside Claude Code.

- **Language**: answer the owner in Brazilian Portuguese; write code, commits and docs in English.
- **Shells**: Windows. The Bash tool is Git Bash (use it for `sed`/`grep` edits). In the PowerShell tool, never pipe
  source files through `Get-Content | Set-Content` (UTF-8 damage, see AGENTS.md). Python isn't installed.
- **`gh`** is installed but not on PATH: `"C:\Program Files\GitHub CLI\gh.exe"`. Plain `git push` works (origin is set).
- **Commits/tags**: commit and push when the owner asks or at the end of a delivery they asked to be shipped; every
  version bump gets its annotated `vX.Y.Z` tag pushed. End commit messages with the `Co-Authored-By` line from the
  session's attribution reminder.
- **Big deliveries** (several features at once): the owner asked for subagents, with effort scaled to the task
  (medium for simple, high for complex — not max on everything).
  1. Write the shared base first (hooks, per-feature files, contracts) and commit it.
  2. Run one agent per feature on its own worktree/branch (`feat/<name>`). `isolation: "worktree"` should work now that
     the session runs inside the repo; the fallback is manual `git worktree add C:\Users\tarle\ee-worktrees\<name>`.
  3. Give each agent its own `TEMP`/`TMP` dir so suites don't fight over logs and `EE_*` folders.
  4. Never `taskkill` EasyEngine.exe while agents are running suites.
  5. Merge, run every suite, review (`/code-review`, `/simplify`), then release.
- **Long unattended runs**: the owner sometimes leaves work running overnight ("revise, simplifique, reuse, testes
  unitários e E2E, code review, commit e push"). Finish with all suites green, temp projects cleaned, and a short
  PT-BR status: version, test totals, where to try it (launcher → Recent), what was deferred.
- **Memory**: this project's auto-memory lives in `~/.claude/projects/c--Users-tarle-OneDrive--rea-de-Trabalho-Jogos-engine/memory/`.
  Older sessions ran from `C:\Users\tarle\learn`; their memory and transcripts are in
  `~/.claude/projects/c--Users-tarle-learn/` (the 41 MB `29c428a0-….jsonl` is the main history up to v0.10.2).
