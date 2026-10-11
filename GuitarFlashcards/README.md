# Guitar Flashcards

An independent .NET MAUI chord practice app for Windows and Android. The tuner does not reference this app or its core library. `GuitarFlashcards.Core` contains the platform-independent fingering, note derivation, chord identification, configuration, persistence, synthesis, and practice-loop logic.

## Chord identification

The six strings are ordered low E to high E in standard tuning. Each string is open, muted, or fretted; fingering notes are derived from MIDI note numbers before identification. The identifier compares the actual pitch-class set against chord interval definitions, not named guitar shapes.

Supported qualities include major, minor, diminished, augmented, suspended, power, sixth, seventh, ninth, and selected minor/major variants. It examines all twelve roots, accepts common incomplete voicings and supported rootless voicings, and uses the lowest sounding note to name inversions. Candidate ranking is deterministic: complete matches first, then a present root, a bass note matching the candidate root, fewer missing tones, and the quality preference table. Equally ranked interpretations are displayed as ambiguous rather than arbitrarily chosen. Names use sharp pitch-class spelling (`C#`, `D#`, etc.); slash bass notation marks inversions.

The fully open standard guitar yields pitch classes E, G, B, D, and A. The identifier names this as `Em7(add11)` rather than leaving the five-note set unidentified.

## Configuration

Schema version 1 stores stable chord IDs, display names, six complete string states, derived metadata, enabled flags, fractional minimum/maximum durations, and both practice display/audio settings. Missing required fields, unsupported versions, invalid settings/fingerings, duplicate IDs, and chord metadata inconsistent with the fingering are rejected before import can replace local configuration. Writes use a temporary file followed by replacement. Local settings live in the platform app-data directory; import uses MAUI file picking and export uses the native MAUI save UI.

## Audio

`ChordWaveformSynthesizer` independently excites a Karplus–Strong delay line for every sounding string and mixes mono PCM. Low E begins first and high E last across a configurable 25 ms strum. Each string also gets a deterministic, high-passed 2.5 ms noise pick transient. Delay-line averaging naturally damps upper partials rapidly while feedback controls each string's decay, avoiding the fixed, phase-coherent additive chord that sounded organ-like. The tuner reference still uses its original fundamental plus restrained second through fourth harmonics; that balance is intentionally not imposed on the guitar-string sustain. The persisted strum interval is adjustable from 0.25 to 4.0 seconds in quarter-second increments, defaulting to 1.0 second. Each string's decay approaches the retrigger threshold by the configured next-strum interval; a fresh independent low-to-high excitation is then injected. The resulting phrase loops for continuous Play/Pause; stop and cancellation fade the active player. Synthesis runs off the UI thread with bounded delay-line and output buffers. Windows/Android hardware playback still needs listening evaluation.

## Build and tests

```powershell
dotnet test StringTuner.sln
dotnet publish GuitarFlashcards\GuitarFlashcards.csproj -f net10.0-windows10.0.19041.0 -c Release -p:WindowsPackageType=None
$env:ANDROID_HOME = 'C:\Program Files (x86)\Android\android-sdk'
$env:ANDROID_SDK_ROOT = $env:ANDROID_HOME
dotnet publish GuitarFlashcards\GuitarFlashcards.csproj -f net10.0-android -c Release -p:AndroidPackageFormats=apk
```

Core tests cover open/fretted/muted states, common C/G/D/E/Em fingerings, inversions, incomplete/rootless and ambiguous note sets, persistent round-trips and atomic invalid imports, fractional timing, countdown and stop behavior, enabled filtering, and generated open/fretted/polyphonic WAV data. Build success does not constitute physical playback testing: manual Windows speaker and Android device playback still need listening checks. No microphone capture or microphone analysis is included.