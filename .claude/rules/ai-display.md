---
paths:
  - "MSFSBlindAssist/Services/GeminiService.cs"
  - "MSFSBlindAssist/Services/ClaudeService.cs"
  - "MSFSBlindAssist/Services/Screenshot*.cs"
  - "MSFSBlindAssist/Services/DisplayReadGate.cs"
  - "MSFSBlindAssist/Services/AiProviderFactory.cs"
  - "MSFSBlindAssist/Services/InstrumentView*.cs"
  - "MSFSBlindAssist/Services/CameraHome*.cs"
  - "MSFSBlindAssist/SimConnect/SimConnectManager.Camera.cs"
  - "MSFSBlindAssist/Aircraft/AiDisplayRead.cs"
  - "MSFSBlindAssist/SimConnect/CameraReadWaiters.cs"
  - "tests/MSFSBlindAssist.Tests/**/*AiDisplay*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*AiProvider*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*DisplayRead*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*DisplayPrompt*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*Gemini*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*ClaudeService*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*Camera*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*Screenshot*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*InstrumentView*.cs"
  - "MSFSBlindAssist/Aircraft/BaseAircraftDefinition.cs"
  - "MSFSBlindAssist/Services/AiModelInfo.cs"
  - "MSFSBlindAssist/Services/IAiProvider.cs"
  - "MSFSBlindAssist/Settings/AiProvider.cs"
  - "MSFSBlindAssist/Forms/DisplayReadingResultForm.cs"
  - "MSFSBlindAssist/Forms/Settings/AiSettingsPanel.cs"
---
# AI display reads, the camera and screenshots rules

Loaded when Claude reads matching code. Background: docs/gemini.md. Full text of each rule: docs/invariants/ai-display.md.

- [AI-1] ONE AI capture at a time app-wide (`Services/DisplayReadGate.Shared`): `ReadDisplay` AND `MainForm.DescribeSceneAsync` take the same gate, and the holder must RELEASE it before any modal dialog, or every later read answers "already in progress". Full: docs/invariants/ai-display.md#ai-1
- [AI-2] `ScreenshotService` captures with `PrintWindow(PW_RENDERFULLCONTENT)` first, BitBlt fallback only behind `ScreenshotFrame.LooksBlank` (dark AND uniform, sampled inside the client rect); never send a frame it rejected to the AI. `CaptureAsync` runs on its own thread, bounded at 10 s. Full: docs/invariants/ai-display.md#ai-2
- [AI-3] Camera switches go through `InstrumentViewRequest` (index 0-based, TYPE then INDEX, refuse a non-cockpit camera out loud); the restore writes neutral index, type, index on SEPARATE frames and confirms after a settle (`ConfirmHeldAsync`). Never verify on the first matching poll or gate on `MAX` (more: see full). Full: docs/invariants/ai-display.md#ai-3
- [AI-4] Never reinstate a silent multi-model fallback for Gemini calls — the model used must be exactly `UserSettings.GeminiModel`; a silent fallback hides which model produced a response. Full: docs/invariants/ai-display.md#ai-4
- [AI-5] Do NOT send `thinkingConfig`/`thinkingBudget` to Gemini — `thinkingBudget` is deprecated/invalid on Gemini 3.x models (`thinking_level` is used instead) and can error if both are set. Full: docs/invariants/ai-display.md#ai-5
- [AI-6] The Gemini HTTP timeout catch must NOT gate on `ex.CancellationToken.IsCancellationRequested` (false on a modern .NET HttpClient timeout) — catch `TaskCanceledException` as the timeout instead. Full: docs/invariants/ai-display.md#ai-6
- [AI-7] `ReadDisplay` restores the camera in a `finally` around the capture, BEFORE `AnalyzeDisplayAsync`, never after the AI round-trip; a false `RestoreAsync` speaks "Could not return to your previous view." once, and an unverified switch says "Could not confirm the cockpit view switch; reading what is on screen.", never "could not switch". Full: docs/invariants/ai-display.md#ai-7
- [AI-8] Each camera read takes its OWN request id from the rotating 341–348 range (`REQUEST_CAMERA_VIEW` + `CameraReadIdCount`, `CameraReadWaiters`) and only the answer to that id completes it: never one shared id or waiter, or an abandoned read's late reply completes the next read. The range is reserved in [SIM-6]. Full: docs/invariants/ai-display.md#ai-8
- [AI-9] The ENTRY keeps its one back-to-back `ICameraViewIo.Set` (it moves INTO the instrument type, both registers in range, measured on two aircraft): never "harmonise" it with the restore's spaced neutral/type/index writes, whose failure is specific to LEAVING. Full: docs/invariants/ai-display.md#ai-9
