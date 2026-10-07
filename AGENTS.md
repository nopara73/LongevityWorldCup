# LongevityWorldCup Agent Notes

- Stay within the user's requested scope. Use [DESIGN.md](DESIGN.md) for visual principles and [UBIQUITOUS_LANGUAGE.md](UBIQUITOUS_LANGUAGE.md) for domain terms; consult source and tests for detailed behavior.
- Preserve the product's humor, personality, established names, and user-approved wording. Keep "Questions, concerns, or signs of aging? Reply to this email." and "Update profile request..." exactly unless asked to change them. Cleanup does not authorize rewriting neighboring copy.
- Add or rewrite agent instructions, design guidance, or the glossary only when the user explicitly requests documentation changes. Keep routine implementation detail in source and tests.
- Fix the underlying issue and check affected backend/frontend paths. Use existing tests and browser tools; verify desktop and mobile UI when affected.
- Frontend source is `LongevityWorldCup.Website/Frontend`; never commit generated `wwwroot/js`. Read [Frontend/README.md](LongevityWorldCup.Website/Frontend/README.md) for loading or publishing changes. Keep temporary outputs and review screenshots in ignored `.artifacts/`.
- Do not adopt ImageSharp v4+ or ImageSharp.Drawing v3+ until the project adopts their licensing path or removes those dependencies.
- Usual workspace: verify changes, commit intended files or hunks, push normally to the configured remote's `master`, and verify the remote contains the commit. No PR unless requested; explicit local-only instructions win.
- User-selected separate checkout/worktree: use a task branch, commit and push it, open a PR to `master` or the requested base, and verify both. Do not merge or push directly to `master` without explicit instructions.
- Preserve concurrent work and coordinate shared publication. Never stage unrelated edits, reset or stash another agent's work, switch a shared checkout, or force-push. Reconcile concurrent `master` updates in an isolated checkout.
- Before production SSH changes, read [ServerDeployment.md](LongevityWorldCup.Documentation/ServerDeployment.md) and use `ssh lwc-server`. Keep social-token expiry/refresh metadata and runtime sidecars consistent. Verify the deployed commit, `/health`, and affected behavior before claiming production success.
