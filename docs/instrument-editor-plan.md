# Instrument Editor Plan

## Goal and Scope

Add a shared .NET MAUI instrument/tuning editor available on Windows and Android. Open it from a clearly visible button on the tuner screen. It edits the catalog as a working draft and only persists after Save. Cancel discards all draft edits.

Include JSON import and export on both platforms. Import supports Replace and Merge modes. This plan is for implementation; no editor code has been added yet.

## Catalog Concepts

- One top-level editor row represents one catalog tuning entry, not a grouping of all tunings for an instrument. Examples: `Guitar · Standard`, `Mountain dulcimer · DAA · 3 string`, and `Mountain dulcimer · DAd · 4 string`.
- `displayName` is the friendly name shown and edited in the row.
- Keep `instrumentName` and `tuningName` as distinct catalog identity/metadata fields. Require them when creating an entry; they may be edited in an instrument details form if the UI exposes those fields.
- A tuning entry owns its individual string rows. Each string has a label and one pitch source (`frequency`, `note`, or `midiNote`), consistent with the JSON Schema.
- The note format accepts Unicode accidental symbols only: U+266F sharp (+100 cents), U+266D flat (-100 cents), U+1D132 quarter-tone sharp (+50 cents), U+1D133 quarter-tone flat (-50 cents), U+1D134 three-quarter-tone sharp (+150 cents), and U+1D135 three-quarter-tone flat (-150 cents). ASCII `#` and `b` are unsupported. Note spelling is retained for display.

## Editor Layout and Interaction

- The tuner screen gets an `Instruments`/`Edit tunings` button that navigates to the editor.
- Show the catalog as a dynamic vertical list of compact top-level rows. Each row shows the friendly `displayName` and has edit and delete actions plus an expander.
- Expanded rows show the tuning's strings as dynamic subrows. Each subrow edits a string label and tone, with add/delete actions.
- Add instrument creates a draft tuning entry after collecting `displayName`, `instrumentName`, and `tuningName`. Add string creates a draft string with a default label and valid starter pitch.
- Keep Save and Cancel as explicit, persistent editor actions. Cancel discards the complete draft. Save validates the complete catalog before writing.
- If the user attempts to leave with unsaved edits, ask whether to discard or return to editing.
- Entering the editor stops active audio modes so the editor does not leave microphone capture or the reference tone running behind it.

## Tone Editing and Precision Conversion

Expose a tone-kind selector with three modes:

1. Frequency: an exact positive frequency in Hz; highest precision.
2. Note: note letter/octave with the supported Unicode accidental set, in 50-cent increments; middle precision.
3. MIDI: integer note number; semitone precision.

Changing the mode converts the current draft value immediately and lossy conversion is intentional:

- Frequency to Note: choose the nearest quarter-tone pitch relative to the configured equal-tempered A4 reference, then spell it using the supported Unicode accidental notation.
- Frequency to MIDI: choose the nearest semitone MIDI pitch relative to the configured A4 reference.
- Note to MIDI: choose the nearest semitone MIDI pitch, dropping the note accidental's cents offset.
- MIDI to Note/Frequency and Note to Frequency preserve their represented pitch as closely/exactly as that target format permits.
- At an exact halfway point, round away from zero relative to the semitone anchor. Make this behavior consistent in conversion code and tests.
- When a pitch edit changes its resolved frequency, immediately resort that tuning's strings from lowest frequency to highest. Keep equal-pitch strings stable in their previous relative order; a newly added tied string follows existing ties. Persist `order` to match the resulting displayed order.
- Sorting is based on resolved frequency, including cents offsets and the active A4 reference for note/MIDI modes. Explicit Hz values remain absolute and do not shift when A4 changes.
- Maintain editing focus/selection sensibly when a row moves due to live resorting.

## Validation

Validate during editing and again on Save/import:

- Friendly name, instrument name, tuning name, and string labels must not be blank.
- Frequencies must be finite, positive, and within the supported pitch/MIDI range.
- Notes must parse according to the Unicode-only note grammar and MIDI values must be in range.
- A string has exactly one pitch representation.
- String order is unique, nonnegative, and normalized to current sorted order before saving.
- Empty string lists may exist temporarily as drafts after deletion, but Save rejects a tuning with no strings. The catalog must contain at least one tuning.
- Display inline errors and block Save rather than silently coercing invalid text.

## Save, Cancel, and Persistence

- Work on a deep draft copy; never mutate the active in-memory catalog while editing.
- Save serializes strict JSON (no comments or trailing commas), validates against the catalog rules/schema, writes a temporary file in app data, flushes/closes it, and atomically replaces the app-data override. Do not overwrite the packaged resource.
- Use the same app-data override on Windows and Android. Update `JsonInstrumentCatalogProvider` lookup priority so the app-data catalog is checked before the Windows executable sidecar and packaged Android catalog.
- After a successful save, replace the active catalog in memory and return to the tuner with selection restored where possible. Report write/validation errors without losing the draft.
- Cancel simply discards the draft; it never touches the saved JSON.

## Import and Export

- Use platform-native MAUI file selection/sharing so the same workflow is available on Windows and Android.
- Import reads a user-selected strict JSON catalog, validates the whole document before changing the draft, and shows a preview/count before apply.
- Replace swaps the editor draft catalog for the imported catalog.
- Merge uses the case-insensitive pair `(instrumentName, tuningName)` as the key. New pairs are added. For a matching pair, show a conflict choice (keep current or use imported) before applying. Never partially apply an invalid import.
- Export serializes the current draft or saved catalog as strict, indented JSON in the existing schema and uses the native save/share UI. Export must not modify the active catalog.

## Implementation Outline

1. Extract catalog cloning, serialization, validation, pitch conversions, and merge behavior into testable non-UI services.
2. Add/extend domain edit models to keep pitch kind/value and resolve exact frequency while preserving Unicode note spelling.
3. Update catalog loading precedence to app-data override, then Windows sidecar, then packaged Android JSON.
4. Implement editor ViewModel with draft isolation, commands, live pitch sorting, validation, import, export, Save, and Cancel.
5. Add the MAUI editor page and navigation button; implement responsive dynamic instrument rows and expandable string subrows.
6. Add atomic cross-platform app-data persistence and file picker/export/share integration.
7. Add unit tests for precision conversions, all accidental symbols, tie rounding, live sort stability, validation, import Replace/Merge conflicts, strict JSON, and persistence failure behavior.
8. Build Windows and Android, run Core tests, verify import/export paths on both platforms, then manually test add/edit/delete/reorder/save/cancel on Windows and an Android device.

## Acceptance Criteria

- Editor is reachable from the tuner on Windows and Android.
- Instruments/tunings and strings can be added, edited, and deleted as draft data.
- String rows sort live by resolved frequency, lowest first, with stable tie ordering.
- Switching pitch kinds applies documented lossy rounding without mutating the saved catalog until Save.
- Quarter-tone and three-quarter-tone Unicode accidentals resolve to exact ±50¢ and ±150¢ offsets and render with their original symbols.
- Cancel leaves the saved catalog unchanged. Save writes valid strict JSON atomically and the loader uses that override on both platforms.
- Import Replace, Merge, conflict choice, validation preview, and export work on both platforms.
- Existing instrument/tuning templates survive round-trip without data loss.
