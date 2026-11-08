# Validation on November 8, 2026

Environment: Windows, .NET SDK 10.0.303, NAudio.Wasapi 3.0.0, Avalonia 12.1.4, whisper.cpp v1.9.5 built with MSVC 2022 for CPU inference, multilingual `ggml-base.bin` model.

## Observed results

| Check | Result |
|---|---|
| Release build of the solution | Passed, 0 warnings, 0 errors |
| Core tests | 3 tests: final chunk and silence, recovery after an error, queue overflow and cancellation on disposal |
| Test tone through real loopback capture | 6.010 s, 2,120,328 audio bytes, peak 0.049988 |
| No playback | 2.010 s, peak 0.000019, correctly reported as near-silent |
| whisper-cli on `samples/jfk.wav` | Complete sentence recognized in English |
| System playback + console + Whisper | Voice captured and text recognized; playback started late, so only the end of the recording contains speech |
| Avalonia UI | Window visually inspected; Start, progressive display of 3 chunks, Stop, and re-enabled fields verified |
| Avalonia demo recording | 17.2 seconds; WAV and transcript finalized |
| Independent inspection of WAV headers | Correct RIFF headers and sizes; PCM16 mono 16 kHz chunks confirmed |

The output device was the **JBL Go 3 speaker**, with a native format of 32-bit float, 44.1 kHz, and 2 channels. Chunks sent to Whisper are converted to PCM16 mono 16 kHz.

The Avalonia demo used the upstream sample `external/whisper.cpp/samples/jfk.wav`, played through the Windows audio player during capture. Text appeared **before Stop**:

```text
[00:00] And so my fellow Americans ask not
[00:05] What your country can do for you ask what you can do
[00:10] for your country.
```

Local results are in `captures/validation-tone/`, `captures/validation-silence/`, `captures/validation-speech/`, and `captures/20261108-110335-394/` (GUI demo). They are intentionally excluded from Git. `captures/whisper-reference.txt` contains the direct engine check.

## Limitations of this validation

- The 440 Hz tone validates the output-to-capture path, but not speech transcription; the separate speech test covers that.
- French recognition quality, real two-person calls, device switching, long sessions, and Linux/macOS were not validated in the initial pass.
- Peak measurement detects a signal, not its nature; the near-silence filter does not replace VAD.
- Unit tests use fake adapters and do not establish that hardware works. The hardware checks above are separate.
- The .NET build and test runner required execution outside the agent sandbox (Windows registry / IPC); the application itself does not require administrator privileges.

## Reproducing the checks

### Verification after applying coding conventions

- Release build: 0 warnings, 0 errors.
- Core tests: 7 passing cases, including 4 values around the silence threshold.
- One-second loopback test: 0.920 s recorded, 324,576 audio bytes, peak 0.049988.
- RIFF headers and sizes checked independently: native stereo 44.1 kHz / 32-bit recording; PCM16 mono 16 kHz chunk.
- Local results: `captures/guidelines-tone-check/`, ignored by Git.
- GUI and full speech transcription were not retested in this pass.

### Verification of pause-based chunking

- Release build: 0 warnings, 0 errors. All 15 tests passed, covering lifecycle behavior, phrases longer than 5 seconds, short pauses, state reset, silence and continuous-signal limits, conversion, and the final partial chunk.
- Conversion tests use stereo float at 44.1/48 kHz and mono PCM16 at 16 kHz, with deliberately fragmented writes. The resulting WAV files are PCM16 mono 16 kHz with consistent durations.
- Local replay of the existing French recording through the new writer: 110.67 seconds of input and the same total duration across output chunks. A sentence previously split before the retirement age now stays in a single 7.12-second chunk and is recognized in full with the same `base` model. Files remain in `captures/pause-replay/`, ignored by Git.
- Separate hardware test with a 440 Hz tone through Windows loopback: 21.99 seconds recorded, split into 20 seconds followed by 1.99 seconds at Stop. RIFF headers and sizes checked independently: complete native stereo 44.1 kHz / 32-bit extensible WAV; PCM16 mono 16 kHz chunks. Results are in `captures/pause-loopback-check/`.
- Limitations: the speech test uses a local recording rather than newly captured live speech. The GUI was not retested in this pass. The RMS threshold remains a heuristic: low-level noise in a subsequent chunk still produced a music annotation. Neither dedicated VAD nor overlap at forced boundaries is implemented.

### Whisper.net integration verification

- Release solution build: 0 warnings and 0 errors. All 17 deterministic tests passed, including transcriber ownership, disposal after cancellation, repeated disposal, and cleanup when the capture adapter fails to dispose.
- `Whisper.net` and `Whisper.net.Runtime` are pinned to 1.9.1. The smoke test confirmed that `whisper.dll` and the matching GGML libraries were loaded inside the .NET process.
- Native inference on the existing 7.12-second French chunk recognized the complete retirement-age sentence on two successive calls using the same adapter. Observed times were 1.03 s and 0.83 s, including lazy model loading on the first call. These are individual observations, not a controlled performance comparison with the CLI.
- The public JFK sample was transcribed correctly with automatic language detection. Observed times were 1.66 s and 1.44 s.
- Both native runs verified cancellation after inference began, successful transcription afterward, disposal during another inference, repeated disposal, and rejection of calls after disposal. Native cancellation is cooperative; these checks do not establish a hard shutdown deadline for every model or machine.
- `setup-whisper.ps1 -ModelOnly` was checked with the existing model and preserved its expected SHA256. Fresh model downloading was not repeated.
- This pass tested native inference from saved WAV files. Live loopback capture, the desktop UI, GPU runtimes, and published deployment on a clean machine were not retested. Capture and conversion code were unchanged.
- Reproduce with the explicit [native smoke test](whisper.md#repeatable-native-smoke-test). No models or recordings are committed.

### Separate microphone capture and offline diarization

- Build: Release compilation passed without warnings. The deterministic suite includes 27 cases covering the existing session behavior, separate source tagging, parallel stop requests, rollback on microphone startup failure, disposal failures, overlapping turn semantics, and offline WAV conversion.
- Native Sherpa-ONNX 2.0.1 test with the public `1-two-speakers-en.wav` fixture: automatic clustering found 2 speakers and 4 turns. The complete analysis retranscribed the turns with Whisper.net and saved source-scoped labels.
- Native single-speaker French check: automatic clustering found 1 speaker, and the retirement-age sentence was recognized in full after adding up to 150 ms of context around non-overlapping turns. A separate silent WAV produced 0 speakers and 0 turns.
- Hardware check with simultaneous PC output and microphone: 2.850 s of system output in native stereo 44.1 kHz / 32-bit extensible format, and 2.960 s from the microphone in native stereo 48 kHz / 32-bit extensible format. Separate transcription WAVs were PCM16 mono 16 kHz. RIFF sizes and formats were inspected independently. Startup offset metadata was recorded; this is not sample-accurate synchronization.
- The desktop's microphone option, speaker settings, and disabled-before-Stop analysis controls were visually inspected with the computer-use skill. The directly launched executable needed an absolute model path because it was not started through the repository launch profile. A complete GUI recording-to-analysis interaction was not exercised.
- The model setup script verified the downloaded archive, extracted segmentation model, and embedding model hashes. Local artifacts are under `models/diarization/`, `captures/diarization-fixtures/`, `captures/diarization-smoke-*/`, and `captures/dual-source-hardware-check/`; none are versioned.
- Limitations: no two-person French live call, sustained dual-device recording, overlapping-speech accuracy evaluation, or clean-machine deployment test was performed. Native diarization cancellation waits for the current native call to finish. The labels are estimates, not personal identities.

### Launch commands

```powershell
dotnet build Audio2Mcp.slnx -c Release
dotnet test tests/Audio2Mcp.Tests -c Release
dotnet run --project src/Audio2Mcp.Cli -c Release -- --duration 6 --test-tone
dotnet run --project src/Audio2Mcp.Desktop -c Release
```

For the speech demo, select `en`, click Start, play `external/whisper.cpp/samples/jfk.wav` on the default multimedia output, observe the updates, then click Stop. Switch back to `fr` for French audio.
