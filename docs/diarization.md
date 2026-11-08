# Speaker diarization and separate microphone capture

## Two supported cases

- **Several voices in the system audio:** record normally, Stop, then choose **Identify speakers**. Sherpa-ONNX estimates turns and clusters the voices across the whole source recording. Whisper transcribes each estimated turn.
- **Your microphone and people in a call:** enable **Include microphone (separate source)** before starting. Live results distinguish the capture sources. After Stop, the same analysis can distinguish multiple voices within either source.

A source is not a person: multiple people may speak near a microphone or through one call output. `PC / Speaker 1` and `Microphone / Speaker 1` are independent labels. They are not real-world identities and are not matched across sources, recordings, or sessions. Known names require a separate user-provided mapping, which this prototype does not implement.

## Local dependencies and setup

The project pins `org.k2fsa.sherpa.onnx` to **2.0.1**. Its NuGet runtime supplies native CPU inference. Two additional local models are required:

| Purpose | Model | Upstream distribution |
|---|---|---|
| Speech segmentation and speaker turns | pyannote segmentation 3.0, ONNX conversion | [Sherpa segmentation models](https://github.com/k2-fsa/sherpa-onnx/releases/tag/speaker-segmentation-models) |
| Voice comparison | NeMo TitaNet Small | [Sherpa speaker embedding models](https://github.com/k2-fsa/sherpa-onnx/releases/tag/speaker-recongition-models) |

```powershell
./scripts/setup-diarization.ps1
```

The script downloads roughly 47 MB into `models/diarization/`, verifies pinned SHA256 hashes, and preserves the segmentation archive's license and README. It does not overwrite unexpected existing model files. Models, runtime binaries, recordings, and generated results remain excluded from Git. Audio is never uploaded. Model paths can be changed in the advanced speaker settings.

See the [official model documentation](https://k2-fsa.github.io/sherpa/onnx/speaker-diarization/models.html) and [C# examples](https://k2-fsa.github.io/sherpa/onnx/speaker-diarization/csharp.html).

## Capture and output contracts

`WindowsAudioCapture` captures either an output endpoint or a microphone endpoint in its native format. `DualSourceCapture` owns both adapters, labels chunks, and requests both devices to stop before awaiting either one. Both sources feed the existing bounded transcription queue. Failed microphone startup releases both adapters instead of leaving output capture running.

System audio remains in `capture.wav` and `segment-*.wav`. Optional microphone files are under `microphone/`. `sources.json` records source paths, durations, and approximate startup offsets. Live transcript lines contain their source. The primary `CaptureResult` properties still describe the system WAV; its `Recordings` property carries both source descriptors.

After Stop, `ConversationAnalysis` reads each full native WAV, converts it to mono 16 kHz, and runs speaker clustering over the entire source. This avoids renumbering speakers independently for each live chunk. Speaker numbers follow first appearance. Automatic clustering uses a 0.5 threshold. The optional known speaker count applies only to the PC source; the microphone is always analyzed automatically.

Whisper receives each estimated turn with up to 150 ms of surrounding context, clipped to neighboring detected turns. Overlapping turns receive no extra context and are marked explicitly. Turns longer than 20 seconds are split for transcription. A successful pass creates a fresh `diarization-<id>/` directory containing `transcript-speakers.txt`, `speakers.json`, and the extracted turn WAVs. The live transcript is never replaced on disk. A cancelled or failed pass may leave partial analysis files for inspection; only a successful result replaces the UI transcript.

## Limitations

- This is offline analysis after Stop, not live speaker recognition. It is limited to 30 minutes per source to bound the managed audio buffer.
- Automatic speaker counts and boundaries are estimates. Similar voices, short replies, distant speech, noise, and music can cause merges, extra speakers, or wrong labels. The PC speaker count is shown directly in the main panel. Set it to `2` only when two people are known to be present there; forcing it is not evidence that two people were detected, and individual turn assignments can still be wrong.
- Advanced settings expose the automatic clustering threshold (default 0.5). Increasing it tends to produce fewer groups but can merge different people. It applies to automatic clustering, including the microphone, and has no effect on a source whose count is fixed. On the reported interview recording, thresholds 0.5, 0.65, 0.8, and 0.9 produced 8, 4, 3, and 3 groups respectively. A known count of 2 produced two groups; this count check does not validate each speaker assignment.
- Overlapping voices are not separated. Text for a flagged turn may contain both voices. Whisper transcription can change when audio is recut, and a forced 20-second split can still cut a word.
- No echo cancellation is applied. Speaker playback picked up by the microphone can appear twice with different source labels. Headphones are recommended.
- Source timing uses host startup offsets, not sample-accurate synchronization. No clock drift correction is implemented; ordering across sources is approximate.
- Sherpa's native diarization call does not support interruption through its progress callback. Cancel and window close wait for that call to return, discard its result, and prevent subsequent transcription. Native resources are not freed during inference. Whisper's subsequent transcription remains cooperatively cancellable.
- Tests with synthetic adapters do not validate microphone hardware or recognition quality. Initial native tests cover a public English two-speaker fixture and one French single-speaker recording; realistic French conversations and simultaneous speech still require evaluation.

## Reproduce the native check

The explicit smoke test uses already-local models and a mono or native WAV. Its optional expected count checks **automatic** detection; it does not force clustering to that number.

```powershell
New-Item -ItemType Directory -Force captures/diarization-fixtures
Invoke-WebRequest https://github.com/k2-fsa/sherpa-onnx/releases/download/speaker-segmentation-models/1-two-speakers-en.wav -OutFile captures/diarization-fixtures/two-speakers.wav
dotnet run --project tests/Audio2Mcp.DiarizationSmokeTest -c Release -- captures/diarization-fixtures/two-speakers.wav 2
```

For a short hardware check that records both sources and plays a tone:

```powershell
dotnet run --project src/Audio2Mcp.Cli -c Release -- --duration 3 --test-tone --microphone --output captures/dual-source-check
```

The output directory must be new. This verifies routing and WAV formats, not the accuracy of speaker labels.
