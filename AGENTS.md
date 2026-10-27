# Repository instructions

## Discussion tracking and implementation authorization

- Read `TODO.md` when continuing project work. It is the current list of unfinished work, not a transcript or a completed-task archive. Record actionable new requirements and unresolved decisions during the same turn, updating existing entries instead of duplicating them; distinguish user requirements from proposals.
- On 2026-10-27 the user clarified that “加入 TODO” means investigate the request and prepare an actionable preliminary modification plan, not merely transcribe it. Unless the user explicitly requests recording only, first attempt relevant code inspection, research and a focused reproduction/probe where practical; then record findings and evidence, the proposed changes, remaining uncertainties and acceptance criteria. If reproduction is unavailable, state what was inspected and the actual limitation, and still propose a concrete next approach rather than leaving a bare “待排查” or a list of unanswered questions. Do not label unimplemented work as fixed or unverified behavior as tested. This default authorizes the investigation and plan; follow the current user request to determine whether implementation or publication is also authorized.
- Number the current TODO consecutively from 1; renumber the remaining items after removal or reordering. Numbers are display order, not permanent task identifiers. Preserve titles and numbers in existing historical archives, and prefer task titles when referencing work across documents so renumbering does not create stale references. Each active item must state its current status, remaining work and acceptance criteria. Replace superseded proposals with the current decision; retain only the context needed to act correctly.
- When an item is completed and the required validation/delivery is finished, remove it from the active TODO in the same turn. Preserve a concise completion record and evidence in the relevant changelog, release/validation document or a dated file under `docs/history/`. Do not leave completed checklists or contradictory “待实施 / 已完成” descriptions in TODO.
- For partially completed items, archive the completed portion and keep only the outstanding follow-up in TODO. Code completion does not complete an explicitly required real-device verification; keep that verification open with its actual limitation.
- Before ending a turn that changes task status, check the active TODO for completed entries, obsolete “latest” instructions, duplicated plans and stale release status. If no work remains, say so briefly rather than retaining old tasks.
- Historical snapshots under `docs/history/` are reference evidence, not current instructions. Read them only when needed; current user instructions, this file and the active TODO determine the present scope. Do not overwrite an existing snapshot to rewrite the past.
- The earlier “把todo都做了” authorization concerned the tasks present at that time; the completed 1.1.8–1.1.12 work must not be replayed as new work. Recording a new proposal does not itself authorize implementing or publishing it; follow the user's current task scope without asking again for actions already authorized.

## Project records and local-only material

- Keep shared project rules (`AGENTS.md`), current work (`TODO.md`), useful design/decision history (`docs/history/`) and public validation/release summaries in Git. Internal maintenance purpose alone is not a reason to ignore a document needed by future contributors or devices.
- Put personal scratch notes, raw diagnostics, private screenshots and temporary agent output in the ignored root `.local/` or existing ignored output/log directories. Do not mix raw private material into tracked design/history documents; never include credentials or tokens.
- Shared ignore rules belong in `.gitignore`; developer-specific exclusions may use `.git/info/exclude` or a global Git ignore file. Adding an ignore rule does not remove files already tracked, and is not a privacy or access-control mechanism.
- Source ZIP selection is independent of `.gitignore`. Keep local-only material outside the allowlisted source directories; if adding private paths inside one of those directories, update the package exclusion and verification before publishing. The root `.local/` is outside the current source-package allowlist.

## Versioning — explicit user decision

- Default releases stay in **1.1.x**. Increase only the patch component for ordinary iterations, including features, fixes, refactors and documentation releases.
- Do not switch to 1.2.x, another minor series, or another major version without the human user's explicit permission for that change.
- An agent may recommend a higher series and explain why, but must keep working in 1.1.x until permission is given. A general request to continue, improve, release or publish is not permission to change series.
- Do not change `version-policy.json`, add an override, or bypass the version checks to approve a higher series yourself.
- `VERSION`, executable metadata, source package, release notes and Git tag must agree. Use `tools/assert-version-policy.ps1` through the existing build/package/publish entry points.
- The user approved retaining eight historical releases as 1.1.0–1.1.7 on 2026-10-25. Subsequent normal releases start at 1.1.8.

## UI copy

- Unless the human user explicitly requests an experimental label for a feature, do not add “实验性”, “实验” or “experimental” tags to feature names, UI or current usage documentation. The user set this default on 2026-10-26. Describe actual limitations and unfinished validation factually in the appropriate records; incomplete validation alone is not authorization to add a tag. Preserve historical release snapshots as evidence of their own versions.
- Short Chinese UI labels and explanations that are not prose paragraphs omit a final Chinese full stop (。). Keep meaningful punctuation inside paragraphs.
- Retain native system window chrome; bitmap preview nonclient rendering is not evidence that the actual system frame needs replacement.
- Describe validation by the behaviors and environments covered. If reporting a numeric test total, call it an assertion count and explain material parameterized repetition; do not present thousands of repeated assertions as thousands of independent scenarios or as coverage of untested real devices.

## Minimal necessary tests — explicit user decision

- Keep tests only where a failure can cause unintended clicks, ignore active user input, accept stale/wrong-target evidence, corrupt an update/release, mix account data, hang a query or leak owned processes. Keep one small end-to-end recovery smoke and focused OS/protocol/rollback checks where unit tests cannot cover the boundary.
- Use a few representative regression samples. Do not restore exhaustive font/digit/scale/offset matrices, cosmetic layout/copy/preview tests, long repetitive demo workflows or duplicate portability suites without an explicit need tied to a real defect or a user request.
- Remove obsolete test files together with their build flags and current documentation references. Preserve historical validation records as history, clearly distinguish prior coverage from tests maintained now, and do not delete or weaken an essential assertion merely to conceal a failure.

## Remote publishing — explicit user default

- On 2026-10-26, after reviewing the remaining real-device limitations, the user explicitly instructed “怎么没发布，请发布”. This supersedes the earlier two-stage release hold: publish the current implementation as the next 1.1.x patch, keep quota control opt-in and clearly mark its unverified real-Codex compatibility, and retain unfinished integration/device verification in TODO rather than blocking this release. Do not describe unverified behavior as tested or overwrite older release assets.
- On 2026-10-25 the user explicitly requested that completed releases default to committing and pushing source, creating the matching version tag, and publishing the complete GitHub Release with executable, source archive and SHA256 checksums. A local build/export alone does not finish release delivery. No repeated approval is required unless the user restricts remote changes in that task.
- Verify the published official latest-release endpoint and assets after publication; keep archived source and binaries immutable and make each tag match its own source snapshot.
- The occlusion detection fix, update discovery/delivery fix, update prompt redesign and 1.5-second idle-window behavior were implemented and released in 1.1.11–1.1.12; only the affected-device follow-up for that work recorded in the current TODO remains open. Preserve the 1.5-second idle-window behavior and do not introduce a manual-pause latch unless the user explicitly changes that requirement.
