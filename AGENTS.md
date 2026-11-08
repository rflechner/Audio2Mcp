# Coding guidelines

These guidelines apply to first-party code written or modified in Audio2Mcp. Do not reformat or refactor vendored code in `external/whisper.cpp`.

## Use English

- Write code in English, including names of files, classes, methods, functions, variables, and constants.
- Write code comments, API documentation, user guides, and all first-party Markdown files in English. Keep the existing French user interface and CLI messages in French unless a task explicitly requests their translation.
- Choose descriptive names that make intent clear; avoid obscure abbreviations.

## Document the code

- Document public APIs, important contracts, and non-obvious behavior using the language's documentation conventions.
- Explain the intent, constraints, and reasons behind complex decisions rather than restating what the code does.
- Keep documentation and comments up to date when changing the code.

## Prioritize readability

- Favor clear, straightforward implementations over clever or overly compact code.
- Follow the SOLID principles: single responsibility, open/closed, Liskov substitution, interface segregation, and dependency inversion.
- Apply these principles pragmatically; introduce abstractions when they clarify responsibilities or dependencies, and avoid unnecessary complexity.
- Keep classes and modules focused on a cohesive responsibility.

## Express domain concepts explicitly

- Favor a pragmatic Domain-Driven Design (DDD) approach: use the domain's vocabulary consistently in types, methods, and properties.
- Represent meaningful concepts with small, focused objects rather than passing raw collections, tuples, or primitives whose meaning callers must reconstruct.
- Keep invariants, state interpretation, and related behavior inside the object that owns the concept.
- Expose expressive properties and methods so calling code communicates intent. For example, use `chunk.IsSilent` and `result.IsSilent` instead of repeating peak comparisons.
- Make names reflect what is actually known: a non-silent audio signal does not prove that speech was captured.
- Prefer immutable state and read-only collections where practical, especially for value objects and operation results.
- Introduce domain objects when they clarify the code; keep them focused and avoid unnecessary architectural layers or abstractions.

## Keep methods short and focused

- Give each method or function one clear purpose.
- Split long methods into small, well-named helpers at meaningful responsibility boundaries.
- Avoid deeply nested control flow; use guard clauses and early returns where they improve clarity.
- Prefer readable parameter lists and expressions; break up complex expressions when needed.

## Give the code breathing room

- Separate logical steps with blank lines and keep distinct responsibilities visually separated.
- Use consistent indentation and follow the repository's existing formatting conventions.
- Avoid dense blocks, multiple statements on one line, and unnecessary blank lines.
- Optimize for a tired human reader: put a blank line between validation, setup, execution, result handling, and cleanup when these steps are adjacent.
- Separate methods, properties, events, and logical groups of fields with blank lines. Keep closely related assignments together; do not insert a blank line after every statement.
- Always use braces and separate lines for conditional and loop bodies, including short guards.
- Expand long object initializers and argument lists across lines. Prefer named intermediate values over dense nested expressions.

## Respect the audio architecture

- Keep `Audio2Mcp.Core` independent of NAudio, Avalonia, Windows APIs, and Whisper process management. Depend on `IAudioCapture` and `ITranscriber` at adapter boundaries.
- Keep window layout, session lifecycle, audio conversion, and process execution in focused methods or types.
- Preserve native-format full recordings and PCM16 mono 16 kHz transcription chunks.
- Keep the transcription queue bounded and report overflow without blocking capture or deleting saved chunks.
- Preserve graceful stop (final chunk and queue drain) and disposal (cancel transcription and release resources).
- Document event threading, ownership, cancellation, and single-session lifecycle contracts with XML documentation.
- Keep audio data local; do not introduce network transcription or commit captures, models, or generated binaries.

## Verify changes

- Build with `dotnet build Audio2Mcp.slnx -c Release` and run `dotnet test tests/Audio2Mcp.Tests -c Release`.
- Use deterministic fake adapters to test session behavior and domain boundaries. Do not equate these tests with hardware validation.
- For changes to capture or conversion, separately verify the Windows loopback path and WAV formats when an audio device is available. Report any checks that were not performed.

## Disclose AI assistance in commits

- Every commit created or materially assisted by an AI agent must identify that agent in the commit message for transparency. This applies to ChatGPT Codex, Mistral, Claude, and other AI assistants.
- Preserve the human author's configured Git identity. Add attribution trailers after a blank line at the end of the commit message; do not impersonate the human or replace their Git configuration.
- For ChatGPT Codex, use `Co-authored-by: ChatGPT Codex <noreply@openai.com>`.
- For Mistral, Claude, or another assistant, use a `Co-authored-by: Name <email>` trailer when an attribution identity has been supplied or configured. Otherwise, use an explicit `AI-Assisted-By: Mistral` or `AI-Assisted-By: Claude` trailer instead of inventing an email address.
- Credit only assistants that actually contributed to the committed changes. Preserve existing attribution when amending a commit and include all known contributors when combining commits.
- Before committing, verify that the message contains the appropriate attribution. Do not claim AI-generated changes were written entirely by a human.
