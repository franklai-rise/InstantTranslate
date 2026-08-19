# Changelog

## 0.4.1 - 2026-08-19

### Changed

- Reduced the in-memory translation cache to 128 entries, about one million characters, and a 20-minute TTL while preserving instant repeat-translation hits.
- Replaced retained source text in cache keys with SHA-256 fingerprints, reducing memory retention and strengthening in-process privacy.
- Bounded multi-range UI Automation context reads to 3,000 characters in total and reused prepared glossary/mode/style data across cache and provider stages.
- Tuned pooled HTTP/2 connections and active-request keep-alive behavior for faster recovery after network or VPN changes.

### Fixed

- Explicitly release popup HWND hooks, rich-text documents, animations, and delayed feedback work when a popup closes, preventing avoidable memory growth during long sessions.

## 0.4.0 - 2026-08-19

### Added

- Added opt-in surrounding-paragraph context from UI Automation without using the clipboard.
- Added fast, balanced, and precise translation modes plus natural, formal, concise, academic, and technical writing styles.
- Added a local personal glossary with `source => preferred translation` entries; only terms matching the current selection are sent.
- Added cache isolation for context, mode, style, matching terminology, and prompt-version changes.
- Added separate first-content and streaming-idle timeouts within the existing total request budget.

### Changed

- Reworked DeepSeek input as structured, untrusted translation data so context and terminology remain separate from instructions.
- Delayed the initial popup until translated content arrives, removing the animated waiting state.
- Refined the popup into a quieter content surface with an on-demand text-size control and a unified source/translation copy group.
- Refined the neutral palette, spacing, corner relationships, accessibility names, and Settings presentation around a content-first hierarchy.

### Security

- Kept surrounding-context sharing disabled by default and documented exactly when extra text is sent.
- Continued to keep source text, translations, and context out of persistent history; glossary preferences are stored locally.
- Explicitly reject UI Automation password elements and native Edit controls carrying the Windows password style.

## 0.3.1 - 2026-08-13

### Changed

- Changed automatic selection reading to prefer UI Automation and direct Win32/RichEdit/Scintilla selection messages, so ordinary selections no longer modify the system clipboard.
- Made the WM_COPY compatibility fallback opt-in from Settings and documented its clipboard side effects for custom-rendered applications.

### Fixed

- Fixed automatic dragging/selection potentially replacing or altering content that the user had already copied.

## 0.3.0 - 2026-08-13

### Added

- Added an English-first interface with a one-click English / 中文 switch in Settings; the saved language also applies to popup prompts, tray commands, notifications, and validation messages.
- Added separate English and Chinese translation-font settings. English options include Times New Roman, Arial, Source Sans Pro, Georgia, Calibri, and Cambria; Chinese options include SimHei, Microsoft YaHei UI, SimSun, and KaiTi.
- Bundled Source Sans Pro under the SIL Open Font License so non-translation interface text is consistent even when the font is not installed on Windows.
- Added migration and localization coverage for old settings, invalid font values, aliases, and provider errors.

### Changed

- Made Times New Roman and SimHei the respective default English and Chinese translation fonts.
- Standardized popup controls on a 24×24 icon coordinate system with consistent 34×32 hit areas.
- Replaced ambiguous copy badges with readable source/translation S and T markers, and redrew the pin icon for clearer visual weight.
- Applied saved interface-language and translation-font changes to open popup windows after Settings is saved.

## 0.2.0 - 2026-08-13

### Added

- Added single-instance activation so repeated launches reuse the running app.
- Added `Ctrl+Shift+T` and a tray command to translate text the user copied manually.
- Added a process-local LRU translation cache with TTL and memory limits.
- Added a close button, visible copy feedback, saved default font size, a scrollable settings window, API connection testing, API-key clearing, version status, and an About entry.
- Added complete tests for cache, streaming throttling, single-instance coordination, secure Endpoint validation, UIA process boundaries, and selection fallback policies.

### Changed

- Coalesced streaming UI updates and incrementally appended translation runs to keep long output responsive.
- Refined the popup into a compact, solid action strip plus an opaque, light-gray, borderless reading surface with clearer interaction states.
- Reorganized settings into three restrained cards with consistent controls, a fixed save bar, and an independently scrollable content area.
- Applied the configured accent theme to popup actions, selection, feedback, and pin state without adding glass, shadows, or decorative effects.
- Kept an in-progress pinned translation independent from the next transient selection.
- Increased the translation timeout to 60 seconds and made the coordinator the single owner of request timeouts.
- Required HTTPS for remote API Endpoints while continuing to allow local HTTP development servers.
- Made settings writes use unique same-directory temporary files before atomic replacement.

### Fixed

- Fixed a timeout race that could leave the loading popup visible indefinitely.
- Fixed provider failures and unexpected cancellation causing a popup to disappear without an explanation.
- Fixed repeated full FlowDocument rebuilding and repeated popup positioning for every streamed token.
- Fixed read-only translation text showing an unusable Paste command.
- Fixed theme choices being saved but not reflected in the popup.
- Fixed pinned translations being canceled when a new selection began.
- Fixed duplicate launches creating multiple tray icons, hooks, requests, and potential API charges.
- Fixed manual popup resizing clipping long or large text instead of providing a bounded, vertically scrollable reading area.
- Fixed the no-activate window style preventing normal text focus, drag selection, selection highlighting, and keyboard copy after an intentional click.
- Fixed a fully opaque selection overlay hiding the selected glyphs; the highlight now remains clear while the text stays readable.

### Security

- Removed every automatic keyboard-copy path; the app never synthesizes `Ctrl+C`.
- Serialized automatic clipboard transactions, stopped clearing the clipboard before copy, limited `WM_COPY` to the hit window and its same-process root, and avoided overwriting third-party clipboard updates.
- Restricted UI Automation focused-element reads to the same process as the pointer hit target.
- Rejected selection gestures whose start and end points belong to different processes.

## 0.1.0 - 2026-08-10

- Added global mouse-selection translation through Windows UI Automation.
- Added low-latency DeepSeek streaming translation and automatic language routing.
- Added no-focus translation popups with copy, language switch, pin, drag, resize, and font controls.
- Added application, window, and system-tray branding.
- Added request cancellation, secure API-key storage, and automated tests.
