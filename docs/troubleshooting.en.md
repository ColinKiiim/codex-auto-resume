# Codex at capacity, paused tasks, and automatic resume: FAQ

[Home](../README.en.md) · [简体中文](troubleshooting.md)

This guide covers Codex desktop interruptions described as `at capacity`, `atcapacity`, or `Selected model is at capacity`, and tasks that wait for a manual resume click. CodexAutoResume handles the visible resume button on Windows.

## Codex keeps saying at capacity. What should I check?

Read the full client message and check the current task state. The phrase alone does not establish every cause of an interruption.

1. Check service availability using the status link in the [official error documentation](https://developers.openai.com/api/docs/guides/error-codes).
2. If the client reports a usage limit or reset time, check your account’s usage page. Refer to [official usage information](https://learn.chatgpt.com/docs/pricing).
3. If the client recommends a different model, consider the available choices. For persistent problems, record the time, version, and reproduction steps and use [official troubleshooting and feedback](https://learn.chatgpt.com/docs/reference/troubleshooting).
4. If the task exposes a right-pointing resume triangle, CodexAutoResume can automate that interaction.

The utility does not alter server capacity or account access. A resume click can still be followed by another error.

## Is at capacity the same as running out of usage?

Treat them separately when investigating. Read the complete message and check account usage. OpenAI’s [API error reference](https://developers.openai.com/api/docs/guides/error-codes) distinguishes temporary overload from quota and billing errors; those categories do not diagnose every desktop message. Resume decisions use button graphics, while the footer reads quota through the local Codex component for reference.

## How can I automatically resume Codex instead of clicking every time?

Download the executable from this repository’s Releases, open Codex desktop, and verify the detected target. Enable **自动恢复** (Allow automatic clicking). The utility locates the button automatically, with no repeated hotkey or calibration step.

A stable resume triangle triggers a pre-click recheck, then a resume attempt. The running square confirms success. Attempts are at least 5 seconds apart. A recognized supported resume button must actually be present; error text alone does not trigger an action.

## Can it resume Codex while another window completely covers it?

Enable **后台监测** (Background monitoring) in Settings, keeping Codex unminimized. The target must supply fresh frames and accept window messages. Use F4 / **后台自测** (Background self-test) to check the local capture and input path, then verify your own Codex pause scenario. The self-test uses the utility’s own window, so it does not prove every target version accepts background input.

## What about minimized windows, sleep, or the lock screen?

Minimized targets wait until restored. Lock-screen operation is not a supported scenario, and monitoring cannot run while the computer is asleep. “Fully covered” means covered by another ordinary desktop window.

## Why is the running square unchanged for a long time?

The compact window keeps the last observation during normal static periods without an age-based warning. Four timestamped progress entries, refreshed every ten seconds, show that checks continue. Detailed diagnostic logs continue every 10 seconds. Sampling counts and native-frame times are available in Settings → Diagnostics. Reading cached pixels does not create new evidence: a fresh independent acquisition is required before clicking, and a post-click square frame is required for confirmation.

## Can citations, send arrows, or question panels trigger clicks?

Blue numeric citation badges and unknown glyphs are excluded from automatic candidates. Version 1.1.7 includes 1,800 numeric font/weight/scale cases. Send arrows, voice waveforms, running squares, and question panels do not trigger resume clicks. Answers, submissions, and permission approvals remain with the user.

If multiple genuine candidates remain, scanning continues. When one remains, normal monitoring resumes automatically. Manual region selection is available.

## Why is there a yellow outline?

It is the Windows capture indicator. The utility reuses capture sessions to reduce repeated starts; reconnecting or recreating a target may still change the outline.

## Does it need an API key or upload screenshots?

No. Recognition and OCR run locally, without account credentials or an API key. Logs do not include chat text, question text, titles, or full screenshots. Saving a cropped diagnostic image is a separate user action. Codex itself retains its normal account and network requirements.

## Does it work with the Codex CLI, a browser, macOS, or Linux?

The current implementation targets Windows x64 Codex desktop windows. It is not an API retry library, CLI wrapper, or browser extension. API developers should follow [official error-handling guidance](https://developers.openai.com/api/docs/guides/error-codes) for their own clients.

## Where should I report problems?

Use this repository’s Issues for recognition, localization, capture, or utility behavior. Include utility/Codex/Windows versions, display scale, mode, time, and reproduction steps. Review any cropped images or log excerpts before sharing. See [contributing](../CONTRIBUTING.md).

Model availability and account problems should go through OpenAI’s support or product feedback channels.

External product references checked on 2026-10-25; follow current client messages and official documentation.
