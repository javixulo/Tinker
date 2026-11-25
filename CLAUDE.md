# Tinker

C# / .NET 10 MCP server (inventory of home devices + search in their manuals).

## Rules

- Everything is **C# on .NET 10**.
- The specs are in `docs/`. Read `docs/functional_spec.md` and `docs/technical_spec.md` before changing behaviour, and update them when a decision is made.
- The contract with the clients is the tool names, schemas and messages (technical spec), not the implementation. Do not change it without saying so.
- Messages in tool results are written for the AI that consumes them: what happened and what to do next. Never return a raw exception.
- Write documentation and code in English and communicate with the user in Spanish.

## Skills

The reusable skills and the agent come from the **`grimoire` plugin** ([github.com/javixulo/grimoire](https://github.com/javixulo/grimoire)); they are not copied into this repository. Install it once per machine: `claude plugin marketplace add javixulo/grimoire`, then `claude plugin install grimoire@grimoire`. Improve them in that repository, not here. A skill particular to Tinker would go in `.claude/skills/` of this repository.

- `grimoire:use-cases-and-criteria`: write the use cases and acceptance criteria of the functional spec.
- `grimoire:open-questions`: keep the table of open questions of each spec.
- `grimoire:mcp-tool-design`, `grimoire:writing-tests`, `grimoire:ci-pipeline`, `grimoire:containerize-app`, `grimoire:container-delivery`, `grimoire:app-telemetry`, `grimoire:versioning-and-changelog`: as in the other MCPs.

The agent `grimoire:spec-reviewer` reviews the specs; run it after writing or changing them.

## Commands

```bash
dotnet build
dotnet run --project src/Tinker.Mcp
```

## Git and secrets

- `.env` holds `GITHUB_TOKEN` and is git-ignored. Never print it, commit it or put it in a URL.
- Pushing uses the local credential helper in `.git/token-helper.sh`.
- Work directly on `main`. Do not create branches unless asked.
- Commit messages follow Conventional Commits: `type(scope): subject`, with the types `feat`, `fix`, `docs`, `test`, `refactor`, `build`, `ci` and `chore`. The subject is in the imperative and has no final period. The scope is optional.
- Never do any of these unless asked explicitly:
  - commit;
  - push (asking for a commit is not asking for a push);
  - rewrite history: force push, `reset --hard`, rebase or amend of published commits, `--no-verify`;
  - merge into `main`, close pull requests or delete branches.
