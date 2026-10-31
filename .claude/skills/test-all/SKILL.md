---
name: test-all
description: Build EasyEngine into the scratch folder and run the test suites, getting one summary line per suite (plus the failed lines). Use after any change, before a commit or release, or when the owner asks to "rodar os testes".
---

Run (PowerShell tool, timeout 600000; the full set takes ~15 min, so use `run_in_background` and wait for the notification):

```
powershell -ExecutionPolicy Bypass -File tools\Test-All.ps1            # every suite, selftest dark + light
powershell -ExecutionPolicy Bypass -File tools\Test-All.ps1 -Quick     # fulltest + e2e (enough for a runtime change)
powershell -ExecutionPolicy Bypass -File tools\Test-All.ps1 -Suites behtest,blocktest -NoBuild
```

- It builds to `%TEMP%\ee-dev-build2` (never the owner's `bin\Debug`), prints `BUILD FAILED` + the first errors if the build breaks, and deletes `%TEMP%\EE_*` at the end.
- Last line is `ALL SUITES GREEN` or `N suite(s) FAILED`. Only read a full log (`%TEMP%\easyengine_<suite>.log`) for a suite that failed, and only around the failed check (`grep -n -A4 FAIL`).
- Run selftest last or alone when the owner is using the PC: it opens real windows.
