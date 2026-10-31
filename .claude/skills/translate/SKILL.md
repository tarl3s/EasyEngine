---
name: translate
description: Add the translations for new or changed editor texts to all 8 Lang/*.json files without reading those 140 KB files. Use after adding or changing any visible text (L.T / L.F), before the fulltest's language check.
---

1. Build first (`/test-all` or `dotnet build -p:OutDir=%TEMP%/ee-dev-build2/`), then:
   `powershell -ExecutionPolicy Bypass -File tools\Strings.ps1 -Missing`
   It runs `--strings` (rewrites `Lang/en.json`), prints how many texts each language lacks and writes them to `%TEMP%\ee_missing.json`. Read only that file.
2. Write one JSON with every language — codes `de es fr ja pt-BR pt-PT zh-Hans zh-Hant` — to the scratchpad:
   `{"pt-BR": {"English": "tradução", …}, "es": {…}, …}`. Keep `{0}` placeholders, emoji and leading symbols exactly; names of effects in the audio editor stay English. Skip values that aren't text (colours like `#FFFFFF`, versions like `v0.15.4`).
3. `powershell -ExecutionPolicy Bypass -File tools\Strings.ps1 -Add <that file>` — appends only what each language lacks, UTF-8 without BOM, never touches existing entries.
4. Run `--fulltest` (it fails if a language falls under 97% or breaks a `{0}`).
