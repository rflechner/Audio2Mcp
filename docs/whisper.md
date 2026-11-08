# whisper.cpp integration

## Default in-process engine

`WhisperNetTranscriber` uses the pinned `Whisper.net` and `Whisper.net.Runtime` **1.9.1** packages. The matching native CPU libraries are copied to the application output by NuGet. They are independent of the repository's whisper.cpp submodule: do not replace them with DLLs built from that submodule, since the native API and managed bindings must match.

Run `./scripts/setup-whisper.ps1 -ModelOnly` to obtain the existing multilingual model without building an executable. The desktop selects Whisper.net by default. The console selects it when `--model` is supplied without `--whisper`.

The adapter loads a `WhisperFactory` lazily on a worker thread and keeps it for the session. Each chunk gets an independent processor with four inference threads, the requested language, and no previous-text context. The processor reads the saved mono 16 kHz WAV and returns text directly; there is no child process or temporary transcription file. Processor disposal waits for native inference to finish before the factory can be freed.

`RecordingSession` owns both adapters. Stop drains the queue; the host then disposes the session, releasing the model. Closing during capture cancels inference and waits for native cleanup. Calls and disposal are serialized inside the native adapter; repeated disposal is safe, and calls after disposal fail. Cancellation and the two-minute timeout are cooperative, not forced process termination. Synchronous model loading cannot be interrupted, and a native crash can terminate the host application.

The CPU runtime's Windows and instruction-set requirements are documented in the [Whisper.net README](https://github.com/sandrohanea/whisper.net#pre-requisites). Only CPU inference is bundled here. No model or audio is downloaded or uploaded during transcription.

### Repeatable native smoke test

This explicit integration check requires a local model and a speech WAV. It does not run as part of the deterministic unit tests and does not capture live audio.

```powershell
git submodule update --init --recursive
dotnet run --project tests/Audio2Mcp.WhisperSmokeTest -c Release -- models/ggml-base.bin external/whisper.cpp/samples/jfk.wav en
```

It transcribes twice using the same adapter, cancels active inference, verifies successful reuse, disposes during another inference, and verifies that further calls are rejected. Use a speech recording long enough for inference to remain active after 50 ms; the test fails explicitly if it finishes sooner.

## Optional external CLI engine

Official submodule: `external/whisper.cpp`, version **v1.9.5**, commit `d1be6fde11ac6e0407606b4e42fe72d34add8037`. No upstream code changes are required.

```powershell
git submodule update --init --recursive
./scripts/setup-whisper.ps1
```

The script configures CMake with CUDA disabled and static libraries enabled, builds `whisper-cli` in Release mode, and copies the executable to `tools/whisper/`. The binary is optimized for the build machine; rebuild it for another machine if CPU instruction support differs. The `ggml-base.bin` model is multilingual; do not use `base.en` for French audio.

The model comes from the model repository referenced by upstream. Before installation, the download is verified against SHA256 `60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe`, recorded for the validated version. If the remote file changes, the script refuses to promote the `.partial` file. An existing model is kept and its hash is displayed. Models and compiled tools are local dependencies and are not versioned. Another compatible GGML model can be selected in the window.

`WhisperCliTranscriber`, selected explicitly in the desktop or with the console's `--whisper` argument, starts one process per chunk:

```text
whisper-cli -m <model> -f <segment.wav> -l fr -otxt -of <unique-prefix> -nt -np -t 4
```

Arguments are passed through `ProcessStartInfo.ArgumentList`, without a shell. Standard output and standard error are read concurrently; the exit code and output text file are checked. Text is read as UTF-8, and the temporary file is deleted. A two-minute timeout prevents inference from hanging indefinitely. The model is reloaded for every chunk: simple and replaceable, but less efficient than a persistent engine.

Both engines transcribe progressively by chunk, rather than token-by-token streaming. Chunking waits for 800 ms of quiet audio and limits each chunk to 20 seconds. A forced boundary can still split a word. Energy-based pause detection does not replace a VAD capable of distinguishing speech from noise. Future improvements could include dedicated VAD, overlapping chunks, and deduplication.

Sources: [whisper.cpp](https://github.com/ggml-org/whisper.cpp/tree/v1.9.5), [NAudio WasapiRecorder](https://naudio.github.io/NAudio/docs/WasapiRecorder.html), [Avalonia](https://docs.avaloniaui.net/docs/get-started/install-avalonia).
