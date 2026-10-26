# CodexAutoResume — Auto-resume paused Codex desktop tasks

[Download latest](https://github.com/ColinKiiim/codex-auto-resume/releases/latest) · [All releases](https://github.com/ColinKiiim/codex-auto-resume/releases) · [Report an issue](https://github.com/ColinKiiim/codex-auto-resume/issues/new/choose)

[简体中文](README.md) · [Troubleshooting](docs/troubleshooting.en.md) · [Changelog](CHANGELOG.md) · [MIT License](LICENSE)

**Codex keeps showing “at capacity”, then waits for Resume?** CodexAutoResume is a portable Windows utility that recognizes the resume triangle, clicks after stability checks, and confirms the running square. Compact footer bars also show the current login's remaining quota and reset times. One EXE, no API key.

> Version 1.1.15 includes opt-in quota control. Real Codex pause/input end-to-end acceptance and the original occlusion check remain incomplete; an unverifiable original conversation waits for manual handling. See the [version notes](docs/releases/v1.1.15.md) for tested scope and limitations.

## Pause on low quota, continue after recovery

Compact footer bars read the current local login about every 10 seconds. Hover for precise local 5h / weekly reset times. No account import or token manager is required. Quota control is in Settings, off by default, and also requires Auto-resume in the main window.

Select 5h, weekly, or both and set a separate remaining-percentage threshold for each (both default to 10%). Any selected quota at or below the threshold triggers an attempt to verify the conversation and pause the running square. While waiting, screen sampling drops to once a minute; quota queries remain independent. All selected quotas must rise above their own thresholds before continuing. A freshly verified resume triangle in the original conversation takes priority. If it disappears, the helper may submit a customizable continuation to that verified conversation's empty editor. The default Chinese text asks Codex to continue from the interruption point.

Input first tries an already available local debugging endpoint, then Windows UI Automation with foreground focus. It never enables debugging or restarts Codex automatically. Unverifiable conversations, unsupported interfaces, Windows activation denial, existing drafts/attachments/questions/voice mode, or uncertain input outcomes require manual handling; there is no repeated submission. Integration with the current local Codex build still needs real-app validation; see the [stage-two record](docs/history/2026-10-26-stage-two.md).

**An early reset for all users is a target scenario.** If actual quota recovers before the previous countdown, CAR uses that fresh quota instead of waiting for the old reset time. This includes the Tibo-wide-reset scenario requested by the user. Polling normally takes about 10 seconds; network delays, failure backoff, input protection and UI availability can delay continuation. An earlier request reduces idle time, but the server determines when the weekly window starts. CAR cannot control that timer or promise instant continuation.

Changing quota settings or continuation text cancels the old transaction; if CAR already paused the task, changing settings also disables Auto-resume to prevent an unexpected continuation. A detected manual continuation cancels that pause cycle until quota recovers and rearms it. Pausing monitoring, changing targets or restarting never replays an old continuation. Outstanding real-app integration and device checks remain in [TODO](TODO.md).

## Recover after a specified time

Choose a full local date and time in Settings → General → Scheduled recovery, set the one-time recovery, and enable Auto-resume. Both ordinary triangle recovery and quota recovery wait until that time. Enabled quota periods must also exceed their individual thresholds, the target must be verified, and input must remain idle for 1.5 seconds. Scheduling can also be used without quota control.

A schedule does not pause a running task or create messages. Confirmed recovery consumes it. Stopping monitoring, disabling Auto-resume, changing targets or exiting cancels it. Replacing or canceling an active plan disables Auto-resume until you enable it again. Plans are never replayed after restart; updates with an active plan return to observation mode. Sleep does not wake the PC; after waking, fresh checks precede any action. Clock or timezone changes cancel the plan for review.

Auxiliary windows open centered near their owner and stay within the monitor work area. Settings can scroll to the final control.

It helps when an interrupted task exposes a resume button. It does not change server capacity, account quotas, or model availability. Detection is based on button graphics, not the text or cause of an error.

## Codex at capacity: when does this tool help?

| What you see | What to do |
|---|---|
| Paused task with a right-pointing resume triangle | Enable automatic clicking to resume when the button is recognized |
| Usage limit or a reset time | Check footer bars and precise reset times; opt-in quota control is available, but clicks cannot add quota |
| Persistent service errors without a resume button | Follow the client message and check service status |
| A question, permission request, or choice | Review it yourself; this utility does not answer or approve |
| Running square, send arrow, or voice waveform | Ordinary auto-resume observes; quota actions require this cycle's verified original conversation |

See the [at capacity / atcapacity FAQ](docs/troubleshooting.en.md) for troubleshooting and supported scenarios.

## Download and run

1. Open this repository’s **Releases** and download the latest `CodexAutoResume-vVERSION-windows-x64.exe` attachment.
2. Run the EXE and open Codex desktop. The utility discovers the target window and starts in observation mode.
3. Check the detected icon, then enable **自动恢复** (Auto-resume). No hotkey or calibration is required.
4. Background monitoring is enabled by default for new users. Codex can be covered while you work elsewhere; keep it unminimized. Change the mode and UI language in Settings.
5. Click **暂停** (Pause) to pause monitoring, or close the utility to exit.

**Requirements:** Windows 10 1903+ / Windows 11 x64 with system .NET Framework. Choose Chinese or English in Settings; your selection is saved. Copy just the EXE to another computer; reference images, icon, and license are embedded. No Python, Node.js, API key, or downloaded recognition model is needed. Question-panel detection uses installed Windows OCR languages.

Source builds also create `release/CodexAutoResume.exe`. Download the executable attachment rather than the source ZIP if you only want to run the app.

## Behavior

- **Resume triangle:** wait at least 1.5 seconds after detection and 1.5 seconds without mouse/keyboard input; new input restarts the quiet period. Both modes use window messages without moving the pointer. Independent pre-click evidence and a five-second attempt interval remain.
- **Running square:** confirms a resume; quota control may pause it when selected quota is low and the original conversation can be verified.
- **Send arrow:** ordinary auto-resume observes; only the owned quota-recovery cycle may submit a continuation to a verified empty editor.
- **Voice waveform / close cross:** the buttons themselves are observed, never clicked automatically.
- **Question panel:** records the waiting state and waits for user input.
- **Blue citation numbers / unknown graphics:** excluded from automatic button candidates.
- **Multiple genuine buttons:** keeps scanning; resumes automatically when one remains, or lets you choose a region manually.

Automatic localization follows window movement. Manual calibration remains available. Multiple target windows require a selection.

Background mode uses Windows Graphics Capture and window messages without moving the system pointer or activating Codex. Support depends on the target rendering fresh frames and accepting those messages. Minimized windows wait until restored. Static content retains the last observation without a timeout warning. Actions require an independent fresh acquisition; only a post-click square frame confirms recovery. The yellow capture outline is a Windows indicator.

## Monitoring and privacy

The compact window shows monitoring status, a tightly cropped real preview, and scrollable timestamped progress for the current session. New entries follow the bottom until you scroll back; reading older entries keeps your position. A short progress entry is refreshed every ten seconds; repeated progress is not added to the full activity history. Activity history contains significant events; diagnostics record sampling and capture activity every 10 seconds, with rotation. Open logs from **Settings → Diagnostics**, or visit `%LOCALAPPDATA%\CodexAutoResume\logs`.

Updates are checked on startup and every six hours. A new stable release prompts for confirmation, then downloads, verifies, replaces, and restarts the EXE. Failed replacement or startup keeps or restores the old version. Update checks can be disabled in Settings. Existing versions without this feature need one manual upgrade.

Recognition and OCR run locally. The utility does not automatically upload images or record chat text, question text, full screenshots, or chat titles. A cropped diagnostic image is saved only when requested by the user.

## FAQ

### Does this fix “Selected model is at capacity” permanently?

It automates the resume-button interaction. A resumed request can still fail while the service is unavailable. Check the error message, service status, and account usage rather than assuming every interruption has the same cause. See [troubleshooting](docs/troubleshooting.en.md).

### Does it support the Codex CLI, a browser, macOS, or Linux?

The current implementation targets the Codex desktop window on Windows x64. It is not a CLI retry wrapper or browser extension.

### Can it show remaining Codex usage?

The footer normally refreshes about every 10 seconds. Hover for precise reset times, or click for account/source details and refresh. It reuses the target installation's bundled App Server without importing accounts or managing tokens. Missing quota leaves ordinary auto-resume available when quota control is off; enabling quota control requires valid usage before actions. The user confirmed quota reading on a second physical PC; custom login environments still need their own checks. See the [usage guide](docs/user-guide.md).

### Does it send messages or approve requests?

Ordinary auto-resume clicks only a strictly recognized resume triangle. Opt-in quota control can pause a verified running conversation and, within its own recovery cycle, submit a customizable continuation to a verified empty editor. Drafts, unverifiable contexts and uncertain outcomes require manual handling. It never answers questions or approves permissions. Ordinary startup observes without clicking.

### How can I report a detection problem?

Use this repository’s **Issues**. Include the utility version, Codex version, Windows version, display scaling, foreground/background mode, and relevant status records. A cropped icon image without conversation content helps diagnose recognition issues.

## Development

```powershell
.\build.ps1 -BuildOnly
```

The build uses the Windows .NET Framework compiler. See [development](docs/development.md), [contributing](CONTRIBUTING.md), and [release instructions](docs/releasing.md).

Version 1.1.11 adds a shared input inactivity window, readable localized update summaries, and low-frequency independent capture verification with local comparison export. Covered-window behavior still depends on the target renderer and needs verification on affected systems. Recognition, update transaction, GUI workflow, occluded-background, and isolated single-EXE results are documented in the [validation record](docs/validation.md). Current version: [VERSION](VERSION). [Version notes](docs/releases/README.md).

Routine releases stay in **1.1.x** and increment the patch number. Moving to 1.2.x or a higher series requires explicit maintainer approval. Agents may suggest an upgrade but may not approve it themselves. See the [version policy](AGENTS.md).

Licensed under [MIT](LICENSE). Independent community project; not affiliated with OpenAI. Product-name and UI-reference details: [NOTICE](NOTICE).
