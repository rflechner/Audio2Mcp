# Audio2Mcp

A .NET 10 / Avalonia proof of concept that captures **Windows system audio** through WASAPI loopback, saves a full WAV recording, and transcribes it progressively with local whisper.cpp inference. No LLM, MCP integration, or speaker diarization is implemented yet.

## Getting started on Windows

Requirements: .NET SDK **10.0.303 or a compatible later 10.0 version**, Git, CMake, and Visual Studio 2022 with the C++ workload and a Windows SDK. An active audio output device is required.

From the repository root:

```powershell
git submodule update --init --recursive
./scripts/setup-whisper.ps1
dotnet build Audio2Mcp.slnx -c Release
dotnet run --project src/Audio2Mcp.Desktop -c Release --no-build
```

The setup script builds whisper.cpp for CPU inference and downloads the multilingual `base` model (~148 MB). It does not install anything globally. Tools and models are excluded from Git. If you already have the engine installed, enter its path and the model path in the window, or use the `WHISPER_CLI` and `WHISPER_MODEL` environment variables.

In Rider, select the `Audio2Mcp.Desktop` launch profile from `src/Audio2Mcp.Desktop/Properties/launchSettings.json`. This profile sets the working directory to the repository root (`../..` from the project directory), so the relative paths `tools/whisper/whisper-cli.exe`, `models/ggml-base.bin`, and `captures/` resolve from that root. `dotnet run --project src/Audio2Mcp.Desktop` also uses this profile by default. If an older Rider run configuration is still selected, switch to the one imported from `launchSettings.json`.

Play a video, join a call, or start another audio source, then click **Start**. A chunk is sent for transcription after roughly 800 ms of quiet audio, or after 20 seconds without a sufficient pause. Text appears once inference finishes. **Stop** ends capture, transcribes the final chunk, and waits for the remaining queue to drain. Closing the window during capture cancels ongoing transcription and finalizes the audio recording.

Each session writes to `captures/<timestamp>/`:

- `capture.wav`: the complete recording in the output device's native format (usually stereo float).
- `segment-00000.wav`, etc.: 16-bit PCM, mono, 16 kHz chunks for whisper.cpp.
- `transcript.txt`: recognized text with each chunk's start time; absent if no text is recognized.

## Time-limited test without a model

```powershell
dotnet run --project src/Audio2Mcp.Cli -c Release -- --duration 6 --test-tone
```

This command plays an audible 440 Hz tone and captures it through the system output. Without `--test-tone`, play an audio source yourself. A silent playback stream preserves silent intervals in the WAV. Exit codes: `0` = non-silent signal, `2` = silence or near-silence, `1` = error. A nonzero peak does not establish that speech was captured.

```powershell
dotnet run --project src/Audio2Mcp.Cli -c Release -- --duration 15 --whisper tools/whisper/whisper-cli.exe --model models/ggml-base.bin --language fr
dotnet test tests/Audio2Mcp.Tests -c Release
```

The example uses `fr` for French audio; use `en` for English or `auto` for automatic language detection. `--output` specifies a **new session directory**. Ctrl+C finalizes the WAV. Captures remain local and are ignored by Git.

## Scope and limitations

Loopback captures the application mix sent to the default multimedia output selected at startup. It does not automatically capture the microphone: during a call, the remote voice will be included, but the local voice usually will not. The selected output stays fixed throughout a session; switching headsets requires restarting capture. Protected content and some exclusive audio paths may not be capturable.

Chunking detects pauses using the RMS level over 20 ms windows (threshold 0.003, approximately -50 dBFS). It preserves the audio before speech and the entire trailing pause; periods without an active signal are saved in 5-second chunks. This heuristic does not distinguish speech from music or noise: continuous background audio can delay transcription until the 20-second limit, and very quiet speech may be split incorrectly. A forced boundary can still split a word; there is no overlap, dedicated voice activity detection (VAD), or speaker identification. Whisper can hallucinate on music or noise. The near-silence filter used before transcription is separate from the chunking threshold. The queue is bounded to 8 chunks: if the CPU cannot keep up, new chunks remain on disk but are reported as not transcribed.

The core and UI target `net10.0`, with no NAudio types in the core. Only the Windows capture adapter is implemented; Start is disabled on Linux and macOS. Portable capture remains future work.

See [architecture and data flow](docs/architecture.md), [whisper.cpp integration](docs/whisper.md), and [validation](docs/validation.md).
