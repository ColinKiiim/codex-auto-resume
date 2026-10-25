# Repository instructions

## Discussion tracking and implementation authorization

- Record actionable discussion outcomes, reported problems, proposed solutions, and unresolved decisions in `TODO.md` during the same turn when useful. Update existing entries rather than duplicating them; distinguish user requirements from proposals that have not been agreed.
- Read `TODO.md` when continuing project work so pending decisions do not depend on chat context. Keep task status and decisions current as work progresses.
- On 2026-10-25 the user explicitly authorized implementing all current TODO items (“把todo都做了”), ending the previous discussion-only phase. Continue recording new discussion outcomes; a future discussion-only restriction must still be respected.

## Versioning — explicit user decision

- Default releases stay in **1.1.x**. Increase only the patch component for ordinary iterations, including features, fixes, refactors and documentation releases.
- Do not switch to 1.2.x, another minor series, or another major version without the human user's explicit permission for that change.
- An agent may recommend a higher series and explain why, but must keep working in 1.1.x until permission is given. A general request to continue, improve, release or publish is not permission to change series.
- Do not change `version-policy.json`, add an override, or bypass the version checks to approve a higher series yourself.
- `VERSION`, executable metadata, source package, release notes and Git tag must agree. Use `tools/assert-version-policy.ps1` through the existing build/package/publish entry points.
- The user approved retaining eight historical releases as 1.1.0–1.1.7 on 2026-10-25. Subsequent normal releases start at 1.1.8.
