# Matt's Guitar Flashcards

An independent .NET MAUI chord practice app for Windows and Android. The tuner does not reference this app or its core library. `GuitarFlashcards.Core` contains the platform-independent fingering, note derivation, chord identification, configuration, persistence, synthesis, and practice-loop logic.

## Chord identification

The six strings are ordered low E to high E in standard tuning. Each string is open, muted, or fretted; fingering notes are derived from MIDI note numbers before identification. The identifier compares the actual pitch-class set against chord interval definitions, not named guitar shapes.

The quality table covers all 23 requested forms: major, minor, diminished, augmented, dominant/minor/half-diminished/fully diminished/major sevenths, sus2/sus4, major/minor sixths, dominant/minor/major ninths, elevenths, and thirteenths, plus power chords and the existing add-tone forms. Half-diminished uses `ø7`; fully diminished uses `°7`. The engine checks all twelve roots and accepts practical incomplete/rootless voicings only when defining seventh/extension tones provide evidence. Candidate ranking prioritizes bass-root agreement, a present root, defining-tone evidence, and fewer omitted tones, then prefers the least-qualified compatible symbol and the shortest conventional spelling. A primary name is returned consistently; equally ranked alternatives are retained and shown as “also known as” in the editor, library, and practice display. Names use sharp pitch-class spelling (`C#`, `D#`, etc.); slash bass notation marks inversions.

The fully open standard guitar yields pitch classes E, G, B, D, and A. The identifier names this as `Em7(add11)` rather than leaving the five-note set unidentified.

`BarreInference` spans from the first to the last string sharing a finger number and fret, even when intervening strings have another assignment. The fingering diagram draws the connector between the matching finger markers. This is a visual heuristic only; it does not add barre objects or affect note derivation/chord naming.

The fingering diagram contains frets 1–24 with a five-fret viewport. The editor and library preview scroll manually with the mouse wheel; the editor preserves its scroll position while the fingering changes. During practice, the viewport follows each chord's fretted range and keeps low positions anchored at fret 1. The editor also restores the selected finger when a marker is cleared, advances to the next finger after placing a note, and wraps from finger 4 to 1.

Settings are disabled while a practice run is active and available again after stopping. Practice settings include a next-chord preview (on by default) and Timed/Manual mode (Timed by default). Manual mode holds each chord until Space advances; the preview is also shown during the countdown.

## Configuration

Schema version 1 stores stable chord IDs, display names, six complete string states, derived metadata, enabled flags, fractional minimum/maximum durations, strum interval, next-chord preview, practice mode, and audio/display settings. New settings default to next-chord preview on and Timed mode when loading older version-1 files. Missing required fields, unsupported versions, invalid settings/fingerings, duplicate IDs, and chord metadata inconsistent with the fingering are rejected before import can replace local configuration. Writes use a temporary file followed by replacement. Local settings live in the platform app-data directory; import uses MAUI file picking and export uses the native MAUI save UI.

## Audio

`ChordWaveformSynthesizer` independently excites a Karplus–Strong delay line for every sounding string and mixes mono PCM. Low E begins first and high E last across a configurable 25 ms strum. Each string also gets a deterministic, high-passed 2.5 ms noise pick transient. Delay-line averaging naturally damps upper partials rapidly while feedback controls each string's decay, avoiding the fixed, phase-coherent additive chord that sounded organ-like. The tuner reference still uses its original fundamental plus restrained second through fourth harmonics; that balance is intentionally not imposed on the guitar-string sustain. The persisted strum interval is adjustable from 0.25 to 4.0 seconds in quarter-second increments, defaulting to 1.0 second. Each string's decay approaches the retrigger threshold by the configured next-strum interval; a fresh independent low-to-high excitation is then injected. The resulting phrase loops for continuous Play/Pause; stop and cancellation fade the active player. Synthesis runs off the UI thread with bounded delay-line and output buffers. Windows/Android hardware playback still needs listening evaluation.

## Build and tests

```powershell
dotnet test StringTuner.sln
dotnet publish GuitarFlashcards\GuitarFlashcards.csproj -f net10.0-windows10.0.19041.0 -c Release -p:WindowsPackageType=None
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" Packaging\GuitarFlashcards.iss
$env:ANDROID_HOME = 'C:\Program Files (x86)\Android\android-sdk'
$env:ANDROID_SDK_ROOT = $env:ANDROID_HOME
dotnet publish GuitarFlashcards\GuitarFlashcards.csproj -f net10.0-android -c Release -p:AndroidPackageFormats=apk
```

The Windows setup installer is written to `Setup\MattsGuitarFlashcardsSetup.exe`. It installs the unpackaged app under Program Files, creates a Start Menu shortcut, and offers an optional desktop shortcut.

Core tests cover open/fretted/muted states, common C/G/D/E/Em fingerings, inversions, incomplete/rootless and ambiguous note sets, persistent round-trips and atomic invalid imports, fractional timing, countdown and stop behavior, next-chord previews, manual-advance waiting/cancellation, enabled filtering, and generated open/fretted/polyphonic WAV data. Build success does not constitute physical playback testing: manual Windows speaker and Android device playback still need listening checks. No microphone capture or microphone analysis is included.