---
name: adr
description: Write or update an Architecture Decision Record in docs/adr (project format) and link it from CLAUDE.md. Use when a design decision is made or an issue needs a contract before coding.
argument-hint: <title>
---

# ADR

1. Number: next free `NNNN` in `docs/adr/` (`ls docs/adr`). File: `docs/adr/NNNN-<kebab-title>.md`.
2. Language: English; identifiers, types, commands inside backticks.
3. Format (keep it short, decisions over prose):
   ```
   # NNNN. <Title>

   Status: accepted (issue #N)

   ## Context
   - <problem, observed behavior, why now>

   ## Decision
   ### <area>
   - <decision with exact type/method signatures and rules: errors, ordering, defaults>

   ## Open questions (default decisions)
   - <question> — <default chosen and why>

   ## Consequences
   - <what becomes possible / what changes for users>
   ```
4. Respect existing ADRs: if a decision supersedes one, say so in both (`Status: superseded by NNNN`).
5. Add the ADR to the list in `CLAUDE.md` → "Documentation" (`NNNN <short name>`), and to `docs/architecture.md`
   where the area is described, if behavior visible to users changed.
6. No code in this step; the ADR is the contract other agents implement.
