You are an expert .NET/C# developer working in VS Code. In the current workspace folder, build a production-quality cross-platform stringed-instrument tuner using .NET MAUI, targeting Windows and Android initially. The application will be sideloaded onto an Android phone and run as a Windows desktop application.

I am an experienced programmer, so do not explain basic C#, .NET, Git, MVVM, or software-engineering concepts to me. Prefer clean, conventional, maintainable code over elaborate abstractions. Do not ask me to manually implement code that you can implement yourself.

Primary goal

Build a tuner that listens to a musical instrument through the device microphone, determines the actual fundamental pitch being played, compares it with the target pitch for the selected string, and provides a continuous visual indication of the measured pitch relative to the target.

The UI must NOT merely say "sharp" or "flat."

For example, when tuning an A2 string (110.000 Hz), the application should be able to display something conceptually like:

A
109.72 Hz
-4.4 cents

with a large, highly visible tuning indicator showing the measured pitch relative to the target:

FLAT                    IN TUNE                    SHARP
|-------------------------|--------------------------|
                     ▲
                   -4.4¢

The visual display should make it immediately obvious whether the string is flat or sharp and by how much.

The displayed frequency and cents offset should update continuously while the instrument is sounding.

Important architectural requirement

Do NOT hard-code the application around six-string guitar.

The application must have a data-driven concept of an instrument tuning template.

An instrument template should define at least:

Instrument name
Tuning name
Number of strings
String name/label
Target MIDI note number
Target frequency
Octave
String ordering
Optional display name
Optional metadata suitable for future extensions

Examples to support initially:

Standard 6-string guitar
E2
A2
D3
G3
B3
E4
Ukulele

Provide at least one common standard tuning as an example, but design the system so alternate tunings can easily be added.

Mountain dulcimer

Support both 3-string and 4-string configurations through templates. Do not assume that every dulcimer uses the same tuning.

The data model should make adding an instrument/tuning require data/configuration rather than changes to the pitch-detection engine or UI code.

Keep the concept of:

Instrument
    → Tuning
        → Strings

distinct from:

Audio capture
    → Pitch detection
        → Musical interpretation
            → UI
Pitch detection

Implement a real pitch detector suitable for monophonic string instruments.

Do NOT simply select the largest FFT bin. Guitar and other stringed instruments produce strong harmonics, and the fundamental may not be the strongest spectral component.

Start with a robust time-domain algorithm such as YIN, autocorrelation, or an appropriately improved variant. Choose the algorithm based on suitability for the application and document the reasoning briefly in the code/design documentation.

Requirements:

Accurate enough for musical tuning
Low latency
Stable output
Good behavior during string attack and decay
Able to reject obviously unreliable/noisy measurements
Return a confidence/quality metric in addition to frequency
Avoid reporting wildly incorrect frequencies when there is insufficient signal

The detector should operate on a continuous stream of audio samples rather than waiting for long recordings.

Use overlapping analysis windows where appropriate.

The application should feel responsive enough that a musician can pluck a string and immediately watch the pitch converge.

Audio capture

Create an abstraction such as:

IAudioCapture

The rest of the application must not depend directly on Android or Windows audio APIs.

Provide platform-specific implementations as necessary:

Android microphone capture
Windows microphone capture

Use an appropriate existing .NET/MAUI/NuGet audio library if one is mature and suitable. Do not reinvent platform audio APIs unnecessarily.

Before selecting a third-party package:

Inspect the current project/package ecosystem.
Prefer actively maintained packages with appropriate licensing.
Prefer a package that provides low-latency PCM microphone access.
Keep the platform-specific dependency isolated behind the audio abstraction.

The DSP/pitch-detection code must remain platform independent.

Request and correctly handle microphone permissions.

The application must fail gracefully if microphone permission is denied or the microphone cannot be opened.

Audio format

Use a sensible PCM format, likely mono floating-point or 16-bit PCM at 44.1 kHz or 48 kHz.

Do not unnecessarily resample audio.

Choose buffer sizes and analysis windows appropriate for detecting guitar-range pitches.

The detector must comfortably handle at least the range from approximately:

E2 = 82.41 Hz
through
E4 = 329.63 Hz

but design it so other instruments can extend the supported range without architectural changes.

Musical pitch representation

Represent target notes using MIDI note numbers or another unambiguous pitch representation rather than storing arbitrary frequency constants throughout the code.

Calculate target frequency from the pitch representation using equal temperament:

f = 440 * 2^((midiNote - 69) / 12)

Use A4 = 440 Hz as the initial reference pitch.

However, make the reference pitch configurable so that later the application can support A4 = 442 Hz, 432 Hz, etc.

The pitch comparison should be expressed in cents:

cents = 1200 * log2(measuredFrequency / targetFrequency)

The UI should display both:

measured frequency
cents deviation

where useful.

Tuning reference

Provide a global configurable tuning reference:

A4 = 440 Hz

with an appropriate UI for changing it later.

Do not bake 440 Hz into the pitch-detection algorithm.

String selection

The user should be able to select the string being tuned.

The UI should make the available strings obvious.

For guitar, for example:

E2   A2   D3   G3   B3   E4

When a string is selected, that string's target pitch becomes the tuning target.

Eventually I may want an "automatic string detection" mode, where the application determines which string is being played based on detected pitch. Do NOT implement that initially unless it falls out naturally from the architecture, but do not design the system in a way that makes it difficult to add later.

Tuning visualization

Create a large, usable tuner display intended to be viewed while holding a phone several feet away.

The primary indicator should show the continuous pitch error in cents.

For example:

-50¢       -25¢        0¢        +25¢       +50¢
  |----------|----------|----------|----------|
                         ▲

The center position represents the exact target frequency.

Use a nonlinear or suitably scaled visual range if that produces a more useful display, but make the relationship between position and cents explicit and consistent.

Provide clear visual indications for:

substantially flat
slightly flat
in tune
slightly sharp
substantially sharp

Do not make color the only means of communicating this.

Display at least:

Selected string
Target note
Target frequency
Measured frequency
Cents deviation
Confidence/signal state where useful

When no useful signal is detected, show an appropriate idle/no-signal state rather than stale pitch data.

Tone generation

The application must also be able to generate a reference tone for the selected string.

Provide:

Play/stop control
Generated frequency equal to the selected string's target frequency
Configurable duration or continuous playback
Reasonable volume control
Clean sine wave initially

Architect the tone generator behind an abstraction such as:

IAudioToneGenerator

Do not couple tone generation to the microphone capture implementation.

The tone generator should be capable of being extended later to other waveforms if desired.

Avoid generating audible clicks when starting/stopping the tone. Use appropriate short fade-in/fade-out envelopes.

UI architecture

Use standard MAUI architecture, preferably MVVM.

Keep the following concerns separate:

Views
ViewModels
Domain/model classes
Audio capture
Audio generation
DSP
Pitch detection
Instrument/tuning definitions

Do not put DSP or audio processing logic in code-behind.

Use dependency injection where it genuinely simplifies the architecture, but don't create gratuitous abstractions.

The core pitch-detection and musical-logic code should be testable without a MAUI UI.

Instrument templates

Create an extensible template system.

Initially provide templates for:

6-string standard guitar
Standard ukulele tuning
3-string mountain dulcimer
4-string mountain dulcimer

Make the templates easy to add.

For example, adding another tuning should ideally look conceptually like defining data such as:

{
    name: "Drop D",
    strings:
    [
        { label: "6", midiNote: ... },
        ...
    ]
}

Do not require modifications to pitch detection, audio capture, or the tuner UI to add a tuning.

If JSON or another external representation makes sense, consider it, but don't unnecessarily externalize data that is better represented as strongly typed C# objects. Explain the tradeoff briefly and choose the simpler maintainable solution.

Settings

Provide a settings mechanism for at least:

A4 reference frequency
Selected instrument
Selected tuning
Selected string
Tone volume

Persist appropriate settings between application launches.

Do not over-engineer settings in the first implementation.

Performance

The tuner must remain responsive on an ordinary Android phone and Windows PC.

Avoid:

allocating large amounts of garbage on every audio callback
unnecessary LINQ in the real-time audio path
blocking the UI thread
blocking the audio callback
performing expensive UI work for every audio sample/buffer

The audio callback should do the minimum necessary work.

DSP processing should run appropriately off the UI thread where necessary.

UI updates should be throttled to a sensible rate rather than occurring for every audio buffer.

Testing

Create unit tests for the platform-independent parts.

At minimum test:

MIDI note → frequency conversion
frequency → cents conversion
cents conversion in both directions where applicable
standard guitar tuning definitions
instrument/template loading
pitch detector using synthetic sine waves
pitch detector using harmonically rich synthetic signals
pitch detector behavior with noise
rejection/low confidence for silence
reference pitch other than A4=440
known frequencies such as E2, A2, D3, G3, B3, E4

If practical, create test WAV/sample data or synthetic sample generators rather than relying exclusively on live microphone tests.

Project structure

Use a sensible solution/project structure.

Prefer something along the lines of:

Tuner/
    Tuner.sln
    Tuner/
        Audio/
        DSP/
        Instruments/
        Models/
        Services/
        ViewModels/
        Views/
        Resources/
    Tuner.Tests/

Adjust this if a better MAUI convention exists.

Development approach

Do NOT try to build every feature simultaneously.

Work incrementally in this order:

Phase 1 — project skeleton

Create the MAUI application targeting Windows and Android.

Establish the core models, dependency injection, MVVM structure, and instrument/tuning template system.

Get the application compiling for both platforms.

Phase 2 — offline pitch detector

Implement and unit-test the pitch detector independently of MAUI.

Use generated signals to establish that:

known frequencies are detected accurately
harmonics don't cause octave errors
noise/silence is rejected appropriately
the detector has acceptable latency

Do not proceed until this part is reasonably solid.

Phase 3 — live microphone input

Implement the platform-specific microphone capture.

Feed live samples into the existing pitch detector.

Do not mix platform audio code into the DSP implementation.

Phase 4 — tuner UI

Implement the visual tuner display and connect it to live pitch detection.

Make this visually useful on both a desktop window and a phone screen.

Phase 5 — tone generation

Add reference-tone generation and UI controls.

Phase 6 — persistence and polish

Add settings persistence, permission handling, error handling, and UI polish.

Code quality requirements

Use modern C# and current supported .NET/MAUI APIs.

Enable nullable reference types.

Use async/await appropriately.

Avoid unnecessary third-party dependencies.

Do not write "demo code" that will need to be thrown away later just to get something on screen.

Do not silently substitute a crude FFT-bin detector for the requested pitch detector.

Do not hard-code guitar-specific assumptions into generic classes.

Do not make the UI responsible for musical calculations.

Do not make the DSP layer dependent on MAUI.

Do not claim that something works on Android or Windows without actually building/testing the relevant project.

When you encounter an API/package/version uncertainty, inspect the installed/current project dependencies and official documentation available to you rather than guessing.

Important interaction rule

I am using GHCP from VS Code and can inspect/run the resulting code myself.

When you reach an architectural decision that has meaningful alternatives, briefly explain the tradeoff and choose a sensible default rather than stopping for approval.

If something genuinely requires a decision that cannot reasonably be inferred, ask me.

Otherwise, implement it.

Start by inspecting the current workspace and determining whether a MAUI project already exists. If one exists, work with it rather than creating a redundant project.

Then implement Phase 1 and proceed through the phases incrementally, keeping the project buildable at each stage.