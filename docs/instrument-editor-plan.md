# Instrument Editor Plan

## Goal and Scope

Status: implemented as a shared .NET MAUI editor on Windows and Android. Open it from the tuner screen's Edit button. It edits the catalog as a working draft and only persists after Save. Cancel discards all draft edits.

JSON import and export are implemented on both platforms. Import supports Replace and Merge modes.

## Catalog Concepts

- One top-level editor row represents one catalog tuning entry, not a grouping of all tunings for an instrument. Examples: `Guitar · Standard`, `Mountain dulcimer · DAA · 3 string`, and `Mountain dulcimer · DAd · 4 string`.
- `displayName` is the friendly name shown and edited in the row.
- Keep `instrumentName` and `tuningName` as distinct catalog identity/metadata fields. Require them when creating an entry; they may be edited in an instrument details form if the UI exposes those fields.
- A tuning entry owns its individual string rows. Each string has a label and one pitch source (`frequency`, `note`, or `midiNote`), consistent with the JSON Schema.
- Committed note format uses Unicode accidental symbols: U+266F sharp (+100 cents), U+266D flat (-100 cents), U+1D132 quarter-tone sharp (+50 cents), U+1D133 quarter-tone flat (-50 cents), U+1D134 three-quarter-tone sharp (+150 cents), and U+1D135 three-quarter-tone flat (-150 cents). The editor accepts ASCII `#` and `b` while typing, strips whitespace, then normalizes to Unicode on commit. Accidental notes are committed as enharmonic pairs where an alternative exists, such as `A♯3/B♭3`; the reader fills a missing pair side and validates that both sides resolve to the same pitch. Natural notes are preferred over redundant spellings: B-sharp 3 becomes C4, not `B♯3/C4`.

## Editor Layout and Interaction

- The tuner screen gets an `Instruments`/`Edit tunings` button that navigates to the editor.
- Show the catalog as a dynamic vertical list of compact top-level rows. Each row shows the friendly `displayName` and has edit and delete actions plus an expander.
- Expanded rows show the tuning's strings as dynamic subrows. Each subrow edits a string label and tone, with add/delete actions.
- Add instrument creates a draft tuning entry after collecting `displayName`, `instrumentName`, and `tuningName`. Add string creates a draft string with a default label and valid starter pitch.
- Keep Save and Cancel as explicit, persistent editor actions. Cancel discards the complete draft. Save validates the complete catalog before writing.
- If the user attempts to leave with unsaved edits, ask whether to discard or return to editing.
- Entering the editor stops active audio modes so the editor does not leave microphone capture or the reference tone running behind it.
- Switching a string to Note mode scrolls the accidental-symbol controls fully into view without changing keyboard focus. The scroll uses the accidental strip's measured position and only adjusts the viewport enough to reveal the full strip above the fixed Save/Cancel footer.

## Tone Editing and Precision Conversion

Expose a tone-kind selector with three modes:

1. Frequency: a positive frequency in Hz, committed and saved to two decimal places.
2. Note: note letter/octave with the supported Unicode accidental set, in 50-cent increments; middle precision.
3. MIDI: integer note number; semitone precision.

Changing the mode converts the current draft value immediately and lossy conversion is intentional:

- Frequency to Note: choose the nearest quarter-tone pitch relative to the configured equal-tempered A4 reference, then spell it using the supported Unicode accidental notation and generate its enharmonic pair where available.
- Frequency to MIDI: choose the nearest semitone MIDI pitch relative to the configured A4 reference.
- Note to MIDI: choose the nearest semitone MIDI pitch, dropping the note accidental's cents offset.
- MIDI to Note/Frequency and Note to Frequency preserve their represented pitch as closely/exactly as that target format permits.
- At an exact halfway point, round away from zero relative to the semitone anchor. Make this behavior consistent in conversion code and tests.
- When a pitch edit changes its resolved frequency, immediately resort that tuning's strings from lowest frequency to highest. Keep equal-pitch strings stable in their previous relative order; a newly added tied string follows existing ties. Persist `order` to match the resulting displayed order.
- Sorting is based on resolved frequency, including cents offsets and the active A4 reference for note/MIDI modes. Explicit Hz values remain absolute and do not shift when A4 changes.
- Maintain editing focus/selection sensibly when a row moves due to live resorting.
- Keep the note-tone cell visibly red and show an inline validation message for invalid notes or mismatched enharmonic pairs. Do not clear focus as a side effect of scrolling.
- An accidental note entered as a single spelling is completed to an enharmonic slash pair at commit/load when a partner exists. If either spelling is a natural, store the natural spelling alone (for example, `B♯3` becomes `C4`).

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

## Implementation and Verification

Implemented: catalog editing UI, pitch conversion and live sorting, atomic app-data persistence, import Replace/Merge, share-based export, Unicode enharmonic slash pairs, and note validation. Windows and Android Release/Debug targets have built successfully; the Core suite has 33 passing tests. The Windows editor has been manually exercised, including invalid-pitch feedback and the accidental-strip scroll. Android installation has been verified; native file-picker/share behavior and accidental-strip scrolling should still be checked on-device.

## Acceptance Criteria

- Editor is reachable from the tuner on Windows and Android.
- Instruments/tunings and strings can be added, edited, and deleted as draft data.
- String rows sort live by resolved frequency, lowest first, with stable tie ordering.
- Switching pitch kinds applies documented lossy rounding without mutating the saved catalog until Save.
- Quarter-tone and three-quarter-tone Unicode accidentals resolve to exact ±50¢ and ±150¢ offsets and render with their original symbols.
- Cancel leaves the saved catalog unchanged. Save writes valid strict JSON atomically and the loader uses that override on both platforms.
- Import Replace, Merge, conflict choice, validation preview, and export work on both platforms.
- Existing instrument/tuning templates survive round-trip without data loss.
