# Guitar Flashcards

An independent .NET MAUI chord practice app for Windows and Android. The tuner does not reference this app or its core library. `GuitarFlashcards.Core` contains the platform-independent fingering, note derivation, chord identification, configuration, persistence, synthesis, and practice-loop logic.

## Chord identification

The six strings are ordered low E to high E in standard tuning. Each string is open, muted, or fretted; fingering notes are derived from MIDI note numbers before identification. The identifier compares the actual pitch-class set against chord interval definitions, not named guitar shapes.

Supported qualities include major, minor, diminished, augmented, suspended, power, sixth, seventh, ninth, and selected minor/major variants. It examines all twelve roots, accepts common incomplete voicings and supported rootless voicings, and uses the lowest sounding note to name inversions. Candidate ranking is deterministic: complete matches first, then a present root, a bass note matching the candidate root, fewer missing tones, and the quality preference table. Equally ranked interpretations are displayed as ambiguous rather than arbitrarily chosen. Names use sharp pitch-class spelling (`C#`, `D#`, etc.); slash bass notation marks inversions.

## Configuration

Schema version 1 stores stable chord IDs, display names, six complete string states, derived metadata, enabled flags, fractional minimum/maximum durations, and both practice display/audio settings. Missing required fields, unsupported versions, invalid settings/fingerings, duplicate IDs, and chord metadata inconsistent with the fingering are rejected before import can replace local configuration. Writes use a temporary file followed by replacement. Local settings live in the platform app-data directory; import uses MAUI file picking and export uses the native MAUI save UI.

## Audio

Chord playback keeps the tuner's established timbre: each note contains its fundamental plus second, third, and fourth harmonics at relative levels `1.0`, `0.28`, `0.12`, and `0.05`. Notes are mixed as mono 44.1 kHz PCM and normalized for the number of simultaneous strings. There is no per-loop fade, avoiding a repeating volume dip; player start/stop volume ramps reduce clicks when playback is explicitly started or stopped. Synthesis is platform-independent; `Plugin.Maui.Audio` supplies playback on the MAUI targets.

## Build and tests

```powershell
dotnet test StringTuner.sln
dotnet publish GuitarFlashcards\GuitarFlashcards.csproj -f net10.0-windows10.0.19041.0 -c Release -p:WindowsPackageType=None
$env:ANDROID_HOME = 'C:\Program Files (x86)\Android\android-sdk'
$env:ANDROID_SDK_ROOT = $env:ANDROID_HOME
dotnet publish GuitarFlashcards\GuitarFlashcards.csproj -f net10.0-android -c Release -p:AndroidPackageFormats=apk
```

Core tests cover open/fretted/muted states, common C/G/D/E/Em fingerings, inversions, incomplete/rootless and ambiguous note sets, persistent round-trips and atomic invalid imports, fractional timing, countdown and stop behavior, enabled filtering, and generated open/fretted/polyphonic WAV data. Build success does not constitute physical playback testing: manual Windows speaker and Android device playback still need listening checks. No microphone capture or microphone analysis is included.