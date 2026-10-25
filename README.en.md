# CodexAutoResume — Auto-resume paused Codex desktop tasks

[Download latest](https://github.com/ColinKiiim/codex-auto-resume/releases/latest) · [All releases](https://github.com/ColinKiiim/codex-auto-resume/releases) · [Report an issue](https://github.com/ColinKiiim/codex-auto-resume/issues/new/choose)

[简体中文](README.md) · [Troubleshooting](docs/troubleshooting.en.md) · [Changelog](CHANGELOG.md) · [MIT License](LICENSE)

**Codex keeps showing “at capacity”, then waits for you to click Resume?** CodexAutoResume is a portable Windows utility that watches the Codex desktop composer, recognizes the right-pointing resume triangle, clicks it after stability checks, and confirms the running square. One EXE, no API key.

It helps when an interrupted task exposes a resume button. It does not change server capacity, account quotas, or model availability. Detection is based on button graphics, not the text or cause of an error.

## Codex at capacity: when does this tool help?

| What you see | What to do |
|---|---|
| Paused task with a right-pointing resume triangle | Enable automatic clicking to resume when the button is recognized |
| Usage limit or a reset time | Check your account usage; clicking cannot add quota |
| Persistent service errors without a resume button | Follow the client message and check service status |
| A question, permission request, or choice | Review it yourself; this utility does not answer or approve |
| Running square, send arrow, or voice waveform | The utility observes without clicking |

See the [at capacity / atcapacity FAQ](docs/troubleshooting.en.md) for troubleshooting and supported scenarios.

## Download and run

1. Open this repository’s **Releases** and download the latest `CodexAutoResume-vVERSION-windows-x64.exe` attachment.
2. Run the EXE and open Codex desktop. The utility discovers the target window and starts in observation mode.
3. Check the detected icon, then select **允许自动点击** (Allow automatic clicking). No hotkey or calibration is required.
4. Optionally select **后台模式** (Background mode) to work in another application while Codex is covered. Keep Codex unminimized.
5. Click **停止** (Stop) or close the utility to end monitoring.

**Requirements:** Windows 10 1903+ / Windows 11 x64 with system .NET Framework. The UI is currently in Chinese. Copy just the EXE to another computer; reference images, icon, and license are embedded. No Python, Node.js, API key, or downloaded recognition model is needed. Question-panel detection uses installed Windows OCR languages.

Source builds also create `release/CodexAutoResume.exe`. Download the executable attachment rather than the source ZIP if you only want to run the app.

## Behavior

- **Resume triangle:** repeated stable samples, an independent pre-click check, and at least 5 seconds between attempts.
- **Running square:** observation and confirmation of a successful resume.
- **Send arrow / voice waveform:** observation only.
- **Question panel:** records the waiting state and waits for user input.
- **Blue citation numbers / unknown graphics:** excluded from automatic button candidates.
- **Multiple genuine buttons:** keeps scanning; resumes automatically when one remains, or lets you choose a region manually.

Automatic localization follows window movement. Manual calibration remains available. Multiple target windows require a selection.

Background mode uses Windows Graphics Capture and window messages without moving the system pointer or activating Codex. Support depends on the target rendering fresh frames and accepting those messages. Minimized windows wait until restored. Stale frames are not used for clicks. The yellow capture outline is a Windows indicator.

## Monitoring and privacy

The UI reports a heartbeat every 1 or 5 minutes. Detailed local logs record sampling and capture activity every 10 seconds, with rotation. Open them using **打开日志** (Open logs), or visit `%LOCALAPPDATA%\CodexAutoResume\logs`.

Recognition and OCR run locally. The utility does not automatically upload images or record chat text, question text, full screenshots, or chat titles. A cropped diagnostic image is saved only when requested by the user.

## FAQ

### Does this fix “Selected model is at capacity” permanently?

It automates the resume-button interaction. A resumed request can still fail while the service is unavailable. Check the error message, service status, and account usage rather than assuming every interruption has the same cause. See [troubleshooting](docs/troubleshooting.en.md).

### Does it support the Codex CLI, a browser, macOS, or Linux?

The current implementation targets the Codex desktop window on Windows x64. It is not a CLI retry wrapper or browser extension.

### Does it send messages or approve requests?

Only a strictly recognized resume triangle triggers a click. Running, send, voice, and question states do not trigger clicks. Automatic clicking is off at startup.

### How can I report a detection problem?

Use this repository’s **Issues**. Include the utility version, Codex version, Windows version, display scaling, foreground/background mode, and relevant status records. A cropped icon image without conversation content helps diagnose recognition issues.

## Development

```powershell
.\build.ps1 -BuildOnly
```

The build uses the Windows .NET Framework compiler. See [development](docs/development.md), [contributing](CONTRIBUTING.md), and [release instructions](docs/releasing.md).

Version 1.1.7 passed 4,216 local checks, including numeric citation negatives and real button samples at multiple scales. GUI workflow, occluded-background, and isolated single-EXE checks are documented in the [validation record](docs/validation.md). Current version: [VERSION](VERSION). [Version notes](docs/releases/README.md).

Routine releases stay in **1.1.x** and increment the patch number. Moving to 1.2.x or a higher series requires explicit maintainer approval. Agents may suggest an upgrade but may not approve it themselves. See the [version policy](AGENTS.md).

Licensed under [MIT](LICENSE). Independent community project; not affiliated with OpenAI. Product-name and UI-reference details: [NOTICE](NOTICE).
