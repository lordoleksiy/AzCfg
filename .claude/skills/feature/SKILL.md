---
name: feature
description: End-to-end feature flow with minimal tokens - branch, failing tests first, code, verify, commit, push, open PR into dev, return PR link. Use when asked to implement a feature/step and ship it as a pull request.
argument-hint: <feature description>
---

# Feature → PR

Be terse. Read only files needed for the feature (target folder + nearest sibling and its test as style sample).
Don't re-read files after editing. No summaries of tool output.

1. **Branch**: `git switch dev && git pull --ff-only` (skip pull if no `origin/dev`), then
   `git switch -c feature/<kebab-name>`. If the working tree is dirty — stop and ask.
2. **Tests first**: `tests/<Project>.Tests/<Folder>/<Type>Tests.cs`. Cover happy path, ordering/edge cases,
   invalid input (null, blank, conflicts). Confirm red: build fails or tests fail.
3. **Code**: one type per file, file-scoped namespace, XML `<summary>` stating behavior and error rules.
   Respect layer rules from `CLAUDE.md` (Core: BCL + ILogger only). No logic beyond the requested step.
4. **Verify** (quiet output only):
   ```
   dotnet build AzCfg.slnx -v q --nologo
   dotnet test AzCfg.slnx -v q --nologo
   ```
   Fix until green. Max 3 fix attempts — then stop and report the failure, no commit.
5. **Commit** (only when green): `git add` only the feature files;
   message `Add <Type>: <one-line behavior>` + `Co-Authored-By` trailer.
6. **Push & PR**:
   ```
   git ls-remote --exit-code --heads origin dev || git push -u origin dev
   git push -u origin feature/<kebab-name>
   gh pr create --base dev --title "<commit subject>" --body-file <scratchpad>/pr.md
   ```
   `pr.md` (short, no fluff):
   ```
   ## What
   - <type/method and what it does, 1–3 bullets>
   ## Decisions
   - <non-obvious rules: errors thrown, ordering, what is out of scope>
   ## Tests
   - <N new tests: what they cover>; `dotnet test` green (<total> passed)

   🤖 Generated with [Claude Code](https://claude.com/claude-code)
   ```
7. **Output**: PR URL + the `## What` bullets. Nothing else.
