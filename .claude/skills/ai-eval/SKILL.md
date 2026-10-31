---
name: ai-eval
description: Measure the AI (graded --aitest) on the local models and get the score plus one line per failed case. Use when changing anything in the AI pipeline (prompts, AiSpecFix, AiApply, examples) or when the owner asks how well the AI does.
---

Ollama must be running with qwen2.5-coder:1.5b and :3b. A full run is ~1 h per model: use `run_in_background`.

```
powershell -ExecutionPolicy Bypass -File tools\AiEval.ps1 -Build                       # build, then 1.5b and 3b, all 123 cases
powershell -ExecutionPolicy Bypass -File tools\AiEval.ps1 -Models 3b -Only shop,combo   # rerun only some cases
powershell -ExecutionPolicy Bypass -File tools\AiEval.ps1 -Extra '--in pt' -Tag pt      # the panel cases in Portuguese (or '--in es')
```

- Output per model: `N/123 passed — failed: …`, then `case — WHY` for each failure. Full logs stay in `%TEMP%\ai_<tag>_<model>.log`; read a case's block there (`grep -n -A6 "\[case\]"`) only when the WHY isn't enough — it has the APPLIED change, the STATE and the model's reply.
- The run uses a copy of the scratch build, so you can keep building while it runs. Runs at below-normal priority with a 2.5 GB cap.
- Results vary run to run by 1–3 cases: before "fixing" a single failure, rerun it with `-Only` to see if it is stable.
- Fix in the shared pipeline (AGENTS.md *AI integration rules*), add a FullTest.AiFix test for the reply that failed, run `/test-all -Quick`.
