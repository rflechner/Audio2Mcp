# whisper.cpp integration

Official submodule: `external/whisper.cpp`, version **v1.9.5**, commit `d1be6fde11ac6e0407606b4e42fe72d34add8037`. No upstream code changes are required.

```powershell
git submodule update --init --recursive
./scripts/setup-whisper.ps1
```

The script configures CMake with CUDA disabled and static libraries enabled, builds `whisper-cli` in Release mode, and copies the executable to `tools/whisper/`. The binary is optimized for the build machine; rebuild it for another machine if CPU instruction support differs. The `ggml-base.bin` model is multilingual; do not use `base.en` for French audio.

The model comes from the model repository referenced by upstream. Before installation, the download is verified against SHA256 `60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe`, recorded for the validated version. If the remote file changes, the script refuses to promote the `.partial` file. An existing model is kept and its hash is displayed. Models and compiled tools are local dependencies and are not versioned. Another compatible GGML model can be selected in the window.

The adapter starts one process per chunk:

```text
whisper-cli -m <model> -f <segment.wav> -l fr -otxt -of <unique-prefix> -nt -np -t 4
```

Arguments are passed through `ProcessStartInfo.ArgumentList`, without a shell. Standard output and standard error are read concurrently; the exit code and output text file are checked. Text is read as UTF-8, and the temporary file is deleted. A two-minute timeout prevents inference from hanging indefinitely. The model is reloaded for every chunk: simple and replaceable, but less efficient than a persistent engine.

Transcription is progressive by chunk, rather than token-by-token streaming. Chunking waits for 800 ms of quiet audio and limits each chunk to 20 seconds. A forced boundary can still split a word. Energy-based pause detection does not replace a VAD capable of distinguishing speech from noise. Future improvements could include a persistent engine, dedicated VAD, overlapping chunks, and deduplication.

Sources: [whisper.cpp](https://github.com/ggml-org/whisper.cpp/tree/v1.9.5), [NAudio WasapiRecorder](https://naudio.github.io/NAudio/docs/WasapiRecorder.html), [Avalonia](https://docs.avaloniaui.net/docs/get-started/install-avalonia).
