# Audio2Mcp

A .NET 10 / Avalonia proof of concept that captures **Windows system audio** through WASAPI loopback, optionally records the microphone separately, and transcribes progressively with local whisper.cpp inference. Experimental offline speaker diarization labels anonymous voices after recording. A separate chat tab supports streaming conversations and images with a local OpenAI-compatible LLM. MCP integration is not implemented yet.

## Getting started on Windows

Requirements: .NET SDK **10.0.303 or a compatible later 10.0 version**, Windows 11 or Windows Server 2022 (or newer), and the Visual C++ 2022 Redistributable. The bundled CPU runtime requires AVX, AVX2, FMA, and F16C on x86/x64. An active audio output device is required. See the [Whisper.net runtime requirements](https://github.com/sandrohanea/whisper.net#pre-requisites).

From the repository root:

```powershell
./scripts/setup-whisper.ps1 -ModelOnly
dotnet build Audio2Mcp.slnx -c Release
dotnet run --project src/Audio2Mcp.Desktop -c Release --no-build
```

The default engine uses **Whisper.net 1.9.1** and its matching CPU runtime to call whisper.cpp in process. NuGet supplies the native libraries; no executable path or C++ compilation is needed. The model loads on the first non-silent chunk and stays in memory until the session is disposed. Audio and inference remain local.

The setup command downloads the multilingual `base` model (~148 MB), or keeps an existing model. It does not install anything globally. Tools and models are excluded from Git. Set the model path in the window or through `WHISPER_MODEL`.

The engine selector also offers the original external CLI for comparison. To build it, run `./scripts/setup-whisper.ps1` without `-ModelOnly`; this additionally requires Git, CMake, and Visual Studio 2022 with the C++ workload and a Windows SDK. The executable path field and `WHISPER_CLI` apply only to that external engine.

In Rider, select the `Audio2Mcp.Desktop` launch profile from `src/Audio2Mcp.Desktop/Properties/launchSettings.json`. This profile sets the working directory to the repository root (`../..` from the project directory), so the relative paths `tools/whisper/whisper-cli.exe`, `models/ggml-base.bin`, and `captures/` resolve from that root. `dotnet run --project src/Audio2Mcp.Desktop` also uses this profile by default. If an older Rider run configuration is still selected, switch to the one imported from `launchSettings.json`.

Play a video, join a call, or start another audio source, then click **Start**. A chunk is sent for transcription after roughly 800 ms of quiet audio, or after 20 seconds without a sufficient pause. Text appears once inference finishes. **Stop** ends capture, transcribes the final chunk, and waits for the remaining queue to drain. Closing the window during capture cancels ongoing transcription and finalizes the audio recording.

Each session writes to `captures/<timestamp>/`:

- `capture.wav`: the complete recording in the output device's native format (usually stereo float).
- `segment-00000.wav`, etc.: 16-bit PCM, mono, 16 kHz chunks for whisper.cpp.
- `transcript.txt`: recognized text with each chunk's start time; absent if no text is recognized.

## Local LLM chat

Open the **Chat** tab to send text and attach PNG, JPEG, or WebP files. Replies appear incrementally using the official `OpenAI` .NET client (Chat Completions streaming). **Interrompre** cancels generation; **Nouvelle discussion** clears the conversation and reloads configuration on the next send. Enter inserts a newline; use **Envoyer** to submit.

Edit `src/Audio2Mcp.Desktop/appsettings.json` before building, or the copied `appsettings.json` beside the deployed executable:

```json
{
  "Llm": {
    "Endpoint": "http://127.0.0.1:8888/v1",
    "Model": "Ministral-3-3B-Instruct-2512-GGUF:Q4_K_M",
    "ApiKeyEnvironmentVariable": "LLM_API_KEY"
  }
}
```

The API key itself must be available in the application's process environment; never put it in JSON or source control. Restart the application and its launching IDE/terminal after changing environment variables. Use the exact model ID reported by your server. Image understanding requires a loaded vision-capable model and server support; the UI accepts up to four images per message, each at most 10 MiB.

History and attachments remain in memory until a new discussion or application exit. Each request sends prior completed turns and their images to the configured endpoint. Interrupted or failed turns stay visible but are excluded from subsequent model context, and their draft is retained for retry. Audio is never sent to the LLM; transcripts are sent automatically only when the optional PC client mode below is enabled. The default endpoint is loopback; changing it changes where chat text and images are sent. There is no automatic context trimming, persistent chat storage, or Markdown rendering.

### PC audio as client requests

In **Chat**, enable **Envoyer les demandes entendues sur le PC au LLM**, then start recording in **Transcription**. Each nonempty live system-output transcript becomes a chat message prefixed with “Le client a demandé ceci”, with automatic-transcription and unverified-speaker context. Microphone segments are always excluded, even during dual-source recording. Messages appear as **Client · audio PC**, use the current conversation and enabled tools, and do not consume or erase manual drafts or pending images. Previous conversation images remain part of the normal chat history.

To try Google search, save and enable the Google example in **Actions**, enable the PC client mode, and play a spoken search request through the PC output. Watch **Chat** for the recognized request, tool arguments, execution result, and model reply. This uses the existing pause-based transcription pipeline, not a new voice detector: delivery waits for transcription after a pause or the chunk duration limit. All system output can be recognized, including media and browser audio; it does not identify a particular client. Recognition errors or incomplete chunks can affect tool selection.

The LLM processes requests sequentially, with at most eight pending PC messages. Overflow is reported in the chat and the transcript stays saved on disk. Stopping recording still delivers the final transcribed chunks and lets queued chat requests finish. Disabling the mode clears pending messages and cancels its current automatic response; **Interrompre** also disables the mode. An automatic request error disables the mode without retrying. **Nouvelle discussion** resets it, and closing cancels pending work. Already executed actions cannot be undone. No capture or audio conversion formats are changed by this mode.

## Text actions in chat

The **Actions** tab creates, edits, enables, and deletes tools described in natural language. Each definition includes a unique tool name, a description explaining when the model should use it, an execution type (**browser** or **HTTP GET**), and an HTTP(S) URL template containing `{query}` in its path or query string. **Exemple Google** fills a search example; click **Enregistrer** to make it available. The description guides tool selection; the URL template defines the actual effect. This prototype does not generate executable code from descriptions.

Definitions are stored in `%LOCALAPPDATA%/Audio2Mcp/actions.json`. Saved changes apply to the next chat message, including in an existing conversation. Enabled actions execute automatically when requested by the model. The model supplies a concise `query`, which is URL-encoded before substitution. For example, save the Google example and ask in **Chat**: “Recherche des tutoriels Avalonia pour débutants.” A tool-capable model can then open the search in the default browser. Browser results only report that the URL opened; they do not fetch search results.

The chat displays tool arguments and execution results, and returns results to the model for its final response. HTTP GET results contain the status and up to 4,000 response characters; requests time out after 20 seconds and do not follow redirects. The configured site receives the query, and HTTP response text is sent to the configured LLM. Do not put credentials in URL templates. Tool support is required in both the model and its server. Each message allows at most four tool rounds with eight calls per round. Cancellation stops pending work but cannot undo an opened browser or an HTTP request already received by a server. Retrying a failed turn can repeat its actions. This is chat function calling, not an MCP server.

## Microphone and speaker labels

For calls, enable **Include microphone (separate source)** before Start. The microphone and system output are recorded separately and live text is labeled `Micro` or `PC`. A source is not necessarily a single person. Use headphones to reduce playback leaking into the microphone; this prototype does not cancel acoustic echo.

To distinguish voices already mixed in the system audio, install the local analysis models once:

```powershell
./scripts/setup-diarization.ps1
```

After **Stop**, click **Identify speakers**. The application analyzes each full source recording, then retranscribes its speaker turns. Labels are scoped to each source, such as `PC · Speaker 1` and `Microphone · Speaker 1`; they do not identify people by name or match voices across sources. The original live transcript is preserved. Results are saved in a new `diarization-<id>/` directory with text, JSON, and turn WAV files.

The main panel shows the PC speaker count: leave it at `0` for an automatic estimate, or set `2` when you know there are two speakers in that source. Automatic estimates can split one voice into several groups. Advanced **Speaker settings** expose a clustering threshold: increasing it produces fewer groups but can merge different voices. Microphone voices are counted automatically. Speaker labels are assigned after recording, not live. See [diarization details and limitations](docs/diarization.md).

## Time-limited test without a model

```powershell
dotnet run --project src/Audio2Mcp.Cli -c Release -- --duration 6 --test-tone
```

This command plays an audible 440 Hz tone and captures it through the system output. Without `--test-tone`, play an audio source yourself. A silent playback stream preserves silent intervals in the WAV. Exit codes: `0` = non-silent signal, `2` = silence or near-silence, `1` = error. A nonzero peak does not establish that speech was captured.

```powershell
dotnet run --project src/Audio2Mcp.Cli -c Release -- --duration 15 --model models/ggml-base.bin --language fr
dotnet test tests/Audio2Mcp.Tests -c Release
```

The example uses Whisper.net and `fr` for French audio; use `en` for English or `auto` for automatic language detection. Add `--microphone` to capture a separate microphone track, or `--whisper tools/whisper/whisper-cli.exe` to compare with the external engine. Without `--model` or `--whisper`, the console only captures audio. `--output` specifies a **new session directory**. Ctrl+C finalizes the WAV. Captures remain local and are ignored by Git.

## Scope and limitations

Loopback captures the application mix sent to the default multimedia output selected at startup. The optional microphone track uses the default communications input and stays separate from the output mix. Selected devices stay fixed throughout a session; switching headsets requires restarting capture. Protected content and some exclusive audio paths may not be capturable. The two device timelines use approximate host startup offsets and have no hardware clock synchronization or drift correction.

Live chunking detects pauses using the RMS level over 20 ms windows (threshold 0.003, approximately -50 dBFS). It preserves the audio before speech and the entire trailing pause; periods without an active signal are saved in 5-second chunks. This heuristic does not distinguish speech from music or noise: continuous background audio can delay transcription until the 20-second limit, and very quiet speech may be split incorrectly. A forced boundary can still split a word; live capture has no overlap or dedicated VAD. Whisper can hallucinate on music or noise. The near-silence filter used before transcription is separate from the chunking threshold. Both sources share a queue bounded to 8 chunks: if the CPU cannot keep up, new chunks remain on disk but are reported as not transcribed. Offline diarization is a separate, experimental pass and does not separate overlapping voices into clean audio.

The core and UI target `net10.0`, with no NAudio types in the core. Only the Windows capture adapter is implemented; Start is disabled on Linux and macOS. Portable capture remains future work.

See [architecture and data flow](docs/architecture.md), [whisper.cpp integration](docs/whisper.md), and [validation](docs/validation.md).
