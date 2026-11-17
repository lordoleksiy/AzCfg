---
name: issue-orchestrator
description: Resolve a GitHub issue of AzCfg end to end by orchestrating subagents - read the issue, write an ADR as the contract, build the shared Core foundation yourself, then run parallel worktree agents per layer, merge, verify with an end-to-end CLI scenario. Use when asked to solve/orchestrate an issue (e.g. "resolve #40", "be the orchestrator for issue N").
argument-hint: <issue number> [extra acceptance criteria]
---

# Issue → working CLI (orchestrated)

## 1. Understand (no agents yet)
- `gh issue view <N> --repo lordoleksiy/AzCfg` (always pass `--repo`; plain `gh issue view` may print nothing).
- Merge the issue with the user's own acceptance criteria from chat; the user's words win on conflicts.
- Read only: `CLAUDE.md`, the ADRs the issue touches, the Core types/ports on the path (`Model`, `Abstractions`,
  `Sync`, `UseCases`), the CLI commands involved and one CLI test as style sample.
- Baseline: `dotnet build AzCfg.slnx -v q --nologo` + `dotnet test AzCfg.slnx -v q --nologo`; note test counts.

## 2. Contract first
- Write `docs/adr/NNNN-<topic>.md` with `/adr`: decisions, new types with exact signatures, CLI surface,
  open questions with the default chosen. Agents read the ADR instead of re-deriving the design.
- Decide open questions of the issue yourself with the safest default and list them in the ADR and final report.

## 3. Foundation yourself
- Model/record changes every later step depends on (new properties, enums, warnings channel, port methods +
  fakes) — TDD in `tests/AzCfg.Core.Tests`, keep the solution compiling, commit locally (no push) so worktree
  agents branch from it.
- New list init-properties on result records default to `[]`; when empty, assign `Array.Empty<T>()` (a fresh
  empty `List` breaks record value equality in existing tests).
- Writing files from Bash heredocs can fail on quotes; use PowerShell `[IO.File]::WriteAllText` or the Write
  tool (the PostToolUse build hook reports red builds during TDD — expected).

## 4. Fan out
- One agent per layer/feature with **disjoint files** (`Agent`, `isolation: "worktree"`, run in parallel):
  Infrastructure adapter, Core use-case behavior, CLI commands. Two writers never share a file except
  `CliApp.cs` registrations (trivial merge).
- Each brief: ADR section to follow, exact signatures, files allowed/forbidden, tests expected, verify commands,
  commit message with the `Co-Authored-By` trailer, "do not push", short final report (branch, hash, API, counts).
- Interface additions break test stubs in `tests/AzCfg.Cli.Tests` — tell the agent to add members throwing
  `NotSupportedException`.
- CLI agents use `/add-cli-command`.

## 5. Merge and verify
- `git merge --no-ff <agent-branch>` one by one; resolve `CliApp.cs`/stub conflicts by keeping both sides.
- Full build + test after each merge; then the end-to-end scenario test (`/cli-scenario-test`) covering the
  user's workflow (init → pull → ignore → push modes) in every mode the issue touches.
- `dotnet format AzCfg.slnx --verify-no-changes`; fix with `dotnet format AzCfg.slnx`.

## 6. Report
- What works now (commands), decisions taken for open questions, what is not verified against real Azure.
- Offer commit/PR; never push without the user's yes.
