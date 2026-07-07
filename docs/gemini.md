# Gemini AI

> **Read this when:** working on the Gemini AI features (display reading, scene/route description, model selection, retry/backoff).

## Gemini AI (display reading + scene/route description)

`Services/GeminiService.cs` powers the AI features (on-demand cockpit-display reading, scene description, and the Shift+E route briefing). Key points:
- **Model is USER-SELECTABLE, not a fallback chain.** The model used for every AI call is `UserSettings.GeminiModel` (default `gemini-flash-latest`, a rolling alias). The Gemini Settings dialog (`Forms/GeminiSettingsForm.cs`) populates a `DropDownList` from a LIVE fetch — `GeminiService.ListAvailableModelsAsync()` (`GET …/v1beta/models`, filtered to `generateContent`-capable text/vision models, newest-first) — with a curated fallback list (`gemini-flash-latest`, `gemini-3.5-flash`, `gemini-2.5-flash`) when offline / no key / fetch fails. Do NOT reinstate a silent multi-model fallback: it hid which model produced a response, which the user rejected.
- **Do NOT send `thinkingConfig`/`thinkingBudget`.** `thinkingBudget` is the Gemini 2.5 parameter; Gemini 3.x models use `thinking_level` and treat `thinkingBudget` as deprecated (and error if both are set). The default models (`gemini-3.5-flash`, `gemini-2.5-flash`) already enable dynamic thinking by default, so omitting `thinkingConfig` keeps thinking on for accuracy across every selectable model without sending a wrong/redundant parameter.
- **Reliability = per-request retry/backoff.** `SendRequestAsync` retries transient failures (429, 500/502/503/504, HTTP timeout, connection-level `HttpRequestException`) with backoff and fails fast on client errors (400/401/403/404, where 404 means "model unavailable — pick another"); on exhaustion it throws a clear "Gemini is busy or unavailable — please try again" message. The timeout catch must NOT gate on `ex.CancellationToken.IsCancellationRequested` (false on a modern .NET HttpClient timeout) — catch `TaskCanceledException` as the timeout (the `SimBriefService` pattern).

