@AGENTS.md

# Claude Code: Longevity World Cup

Work in this repository. Keep unrelated projects, personal files, and account settings outside the task unless the user explicitly expands its scope. Project permissions apply to this folder's Claude sessions; unrestricted shell execution is not an operating-system sandbox.

## Project orientation

- `LongevityWorldCup.sln` is the solution. `global.json` pins the .NET SDK; the website and tests target .NET 10.
- `LongevityWorldCup.Website` is an ASP.NET Core application with controllers, custom HTML injection, SQLite, and Quartz. Follow its existing architecture.
- `LongevityWorldCup.Website/Frontend` contains TypeScript and classic JavaScript source. `wwwroot/js` is ignored build output; never edit or commit it.
- `LongevityWorldCup.Tests` contains xUnit, ASP.NET Core integration tests, and Microsoft.Playwright browser tests. Reuse these tools.
- The helper projects cover application review, OAuth, and Markdown page generation. Inspect the owning project before changing generated pages or operational workflows.

Apply the `lwc-development` skill for development, debugging, testing, or review. Read `DESIGN.md` for UI/product copy and `UBIQUITOUS_LANGUAGE.md` for domain changes, as required by `AGENTS.md`. Keep user-approved wording and the product's personality.

## Development commands

Run these from the repository root unless indicated otherwise:

```sh
dotnet restore LongevityWorldCup.sln
dotnet build LongevityWorldCup.sln --no-restore
dotnet test LongevityWorldCup.Tests/LongevityWorldCup.Tests.csproj --no-build --filter "FullyQualifiedName~RelevantTestClass"
```

For frontend work, run `npm --prefix LongevityWorldCup.Website run check` and `npm --prefix LongevityWorldCup.Website run build`. Normal .NET builds also compile the frontend. Follow `.node-version`, `package.json`, and `Frontend/README.md`; use the lockfile rather than changing dependency versions to make a build pass.

The Desktop Browser preview uses `.claude/launch.json`. It runs locally in Development with scheduled jobs, startup badge refresh, and payment reconciliation disabled. Keep test submissions and operational actions within their authorized scope.

## Git and GitHub

- Use installed `git` and authenticated `gh` for repository, issue, pull-request, and CI operations. Do not copy credentials into instructions, settings, prompts, or tracked files.
- Inspect `git status`, the current branch, the remote, and the diff before editing or publishing. Preserve concurrent work and stage only your intended files or hunks. Never reset, stash, or overwrite another agent's edits; never force-push.
- In the usual workspace (`C:/Users/user/Desktop/LongevityWorldCup`), finish authorized repository changes with appropriate verification, a short commit, and a normal push to the configured remote's `master`. Verify the remote contains the commit. Do not create a PR for this workflow.
- If the user selects a separate checkout or worktree, use a new task branch there, commit and push that branch, and open a PR targeting `master` or the explicitly requested base. Verify the remote branch and PR. Do not merge or push that work directly to `master` without explicit instructions.
- For concurrent `master` updates, reconcile in an isolated checkout and retry a normal push without switching or disturbing the shared checkout. This does not change the direct-to-master workflow.
- A user instruction such as local-only overrides these publication defaults. Report a specific failure and preserve the work when verification or publication cannot complete.
- Use `Closes #number` when a PR fully resolves an issue; mention related issues. Keep review screenshots in ignored `.artifacts/` and attach them as review evidence rather than committing them.

## Verification and operations

Match checks to the change. Reproduce bugs, fix the underlying invariant, inspect similar implementations, and verify affected behavior. For ranking changes, check backend/frontend parity. For UI changes, verify desktop and mobile behavior with the existing browser tools.

Pushing a commit proves publication to GitHub. Claim production success only after checking the appropriate CI, deployed commit, `/health`, and affected live behavior. Read `LongevityWorldCup.Documentation/ServerDeployment.md` before SSH changes; use the existing `lwc-server` alias and its preservation rules.

Keep credentials, private athlete data, local configuration, and operational databases out of commits and logs. Do not send external messages, submit real applications, change live data, or manage accounts merely as a development check; follow the user's authorization for the actual task.

Machine-specific permissions, paths, and plugin enablement belong in ignored `.claude/settings.local.json`. Shared guidance and preview configuration belong in the repository.
