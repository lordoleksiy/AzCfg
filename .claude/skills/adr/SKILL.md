---
name: adr
description: Write or update an Architecture Decision Record in docs/adr (Ukrainian, project format) and link it from CLAUDE.md. Use when a design decision is made or an issue needs a contract before coding.
argument-hint: <title>
---

# ADR

1. Number: next free `NNNN` in `docs/adr/` (`ls docs/adr`). File: `docs/adr/NNNN-<kebab-title>.md`.
2. Language: Ukrainian prose; identifiers, types, commands in English inside backticks.
3. Format (keep it short, decisions over prose):
   ```
   # NNNN. <Title>

   Status: accepted (issue #N)

   ## Контекст
   - <problem, observed behavior, why now>

   ## Рішення
   ### <area>
   - <decision with exact type/method signatures and rules: errors, ordering, defaults>

   ## Відкриті питання (рішення за замовчуванням)
   - <question> — <default chosen and why>

   ## Наслідки
   - <what becomes possible / what changes for users>
   ```
4. Respect existing ADRs: if a decision supersedes one, say so in both (`Status: superseded by NNNN`).
5. Add the ADR to the list in `CLAUDE.md` → "Документація" (`NNNN <short name>`), and to `docs/architecture.md`
   where the area is described, if behavior visible to users changed.
6. No code in this step; the ADR is the contract other agents implement.
