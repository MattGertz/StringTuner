# StringTuner

A .NET MAUI tuner for Windows and Android. Instrument/tuning data, audio I/O, pitch analysis, and the MAUI presentation are kept in separate layers.

## Design

- `Core/` is a plain `net10.0` library. Instrument templates are loaded from the versioned, strict JSON file `Resources/Raw/instruments.json`; `Resources/Raw/instruments.schema.json` documents and validates its format. Strings store an exact pitch source and order.
- `YinPitchDetector` uses YIN's cumulative-mean normalized difference function rather than the strongest FFT bin. This suits monophonic strings because it can find periodicity when harmonics dominate. It refines lag parabolically and rejects weak, clipped, or low-confidence frames.
- `OverlappingPitchAnalyzer` consumes 44.1 kHz mono PCM in 8,192-sample windows with 4,096-sample hops. The 50% overlap gives a new estimate about every 93 ms after the initial 186 ms window. A log-frequency smoother reduces small display fluctuations and resets on a large note jump.
- `Plugin.Maui.Audio` 4.x is the only audio runtime dependency. It is MIT-licensed, actively maintained, and provides live PCM streaming on both targets; its shared API avoids duplicating Android and Windows device backends. Capture and generated-tone playback remain behind `IAudioCapture` and `IAudioToneGenerator`.
- The reference tone is a low-volume mono PCM sine wave with a near-cycle-aligned loop. Playback volume ramps at start and stop to avoid clicks.
- A4 defaults to 440 Hz and is saved across launches. Microphone access is requested when listening starts; denial and device failures remain visible in the signal status.
- Windows reads `instruments.json` beside the executable and copies that sidecar into build/publish output. Android prefers an override in `FileSystem.AppDataDirectory` and falls back to the JSON packaged in the APK, because an APK has no editable adjacent executable directory. Invalid or missing data is reported in the UI and logged; it never silently substitutes hard-coded tunings.

## Included tunings

- Guitar, Standard: E2 A2 D3 G3 B3 E4
- Ukulele, Standard G-C-E-A
- Mountain dulcimer, DAA, 3 string
- Mountain dulcimer, DAd, 4 string

Each string has exactly one pitch source: `midiNote` (equal temperament follows the configured A4 reference), `frequency` (positive Hz, treated as absolute), or `note`. Notes may be a letter, accidental, and octave, or an enharmonic pair separated by `/`, such as `A♯3/B♭3`; both spellings must resolve to the same pitch. When a single accidental is read, StringTuner fills in its enharmonic spelling. Natural spellings are preferred for redundant accidental forms, so B-sharp 3 canonicalizes to C4 rather than `B♯3/C4`. Supported symbols are U+266F sharp (+100 cents), U+266D flat (-100 cents), U+1D132 quarter-tone sharp (+50 cents), U+1D133 quarter-tone flat (-50 cents), U+1D134 three-quarter-tone sharp (+150 cents), and U+1D135 three-quarter-tone flat (-150 cents). The editor accepts ASCII `#` and `b` as typing aliases and converts them to Unicode on commit; whitespace is stripped. Strict JSON uses Unicode symbols and contains no comments or trailing commas. The audio and presentation code do not depend on the number of strings.

## Build and test

```powershell
dotnet test tests\StringTuner.Core.Tests\StringTuner.Core.Tests.csproj
dotnet build StringTuner.csproj -f net10.0-windows10.0.19041.0 -c Release
dotnet publish StringTuner.csproj -f net10.0-android -c Release -p:AndroidPackageFormats=apk
```

Android publish requires the Android workload and produces an APK in the Release output directory. Production distribution should use the developer's own Android signing key.

## Package checks

```powershell
dotnet list StringTuner.csproj package --outdated
dotnet list StringTuner.csproj package --vulnerable
dotnet list tests\StringTuner.Core.Tests\StringTuner.Core.Tests.csproj package --outdated
dotnet list tests\StringTuner.Core.Tests\StringTuner.Core.Tests.csproj package --vulnerable
```