using Audio2Mcp.Capture.Windows;
using Audio2Mcp.Core;
using Audio2Mcp.Transcription.Whisper;

try
{
    if (args.Contains("--help"))
    {
        Console.WriteLine("--duration <1..3600> --output <dossier neuf> --test-tone\n--model <ggml.bin> --language <fr|en|auto>\nAvec --model : Whisper.net en mémoire. --whisper <exe> : moteur externe optionnel.\nSans modèle : capture seule. Ctrl+C : arrêt propre.");
        return 0;
    }

    var options = CaptureOptions.Parse(args);
    if (!OperatingSystem.IsWindows())
    {
        throw new PlatformNotSupportedException("Capture implémentée uniquement sous Windows.");
    }

    if (Directory.Exists(options.OutputDirectory))
    {
        throw new IOException("Choisissez un nouveau dossier de capture.");
    }

    ITranscriber transcriber;
    if (options.WhisperExecutable is not null)
    {
        transcriber = new WhisperCliTranscriber(
            options.WhisperExecutable,
            options.ModelPath ?? throw new ArgumentException("--model requis"),
            options.Language);
    }
    else if (options.ModelPath is not null)
    {
        transcriber = new WhisperNetTranscriber(options.ModelPath, options.Language);
    }
    else
    {
        transcriber = new NoTranscription();
    }

    await using var session = new RecordingSession(
        new WindowsLoopbackCapture(testTone: options.TestTone),
        transcriber);

    session.StatusChanged += Console.WriteLine;
    session.SegmentReady += s => Console.WriteLine($"[{s.Start:mm\\:ss}] {s.Text}");

    using var stop = new CancellationTokenSource();
    session.CaptureFailed += ex =>
    {
        Console.Error.WriteLine(ex.Message);
        stop.Cancel();
    };

    ConsoleCancelEventHandler handler = (_, e) =>
    {
        e.Cancel = true;
        stop.Cancel();
    };
    Console.CancelKeyPress += handler;

    try
    {
        await session.StartAsync(options.OutputDirectory);
        Console.WriteLine($"Capture {options.Duration.TotalSeconds} s dans {options.OutputDirectory}" + (options.TestTone ? " (signal audible 440 Hz)" : ""));

        try
        {
            await Task.Delay(options.Duration, stop.Token);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // Ctrl+C and device failure both finalize the recording below.
        }

        var result = await session.StopAsync();
        Console.WriteLine($"WAV : {result.Path}\nAudio : {result.Bytes} octets, {result.Duration.TotalSeconds:0.000} s, crête {result.Peak:0.000000}");
        Console.WriteLine(result.IsSilent ? "Silence : audio utile NON validé." : "Signal non silencieux validé ; parole non garantie.");

        return result.IsSilent ? 2 : 0;
    }
    finally
    {
        Console.CancelKeyPress -= handler;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

sealed class NoTranscription : ITranscriber
{
    public Task<string> TranscribeAsync(string path, CancellationToken token) => Task.FromResult("");

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
