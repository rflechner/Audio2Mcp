using System.Diagnostics;
using Audio2Mcp.Transcription.Whisper;

// Explicitly invoked integration check: never downloads models or captures live audio.
if (args.Length is < 2 or > 3)
{
    Console.Error.WriteLine("Usage: <model.bin> <mono-16kHz.wav> [language]");
    return 1;
}

try
{
    await using var transcriber = new WhisperNetTranscriber(args[0], args.Length == 3 ? args[2] : "en");
    for (var index = 0; index < 2; index++)
    {
        var timer = Stopwatch.StartNew();
        var text = await transcriber.TranscribeAsync(args[1], CancellationToken.None);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("The speech fixture produced no text.");
        }

        Console.WriteLine($"Pass {index + 1}: {timer.Elapsed.TotalSeconds:F2} s: {text}");
    }

    using (var cancellation = new CancellationTokenSource())
    {
        var inference = transcriber.TranscribeAsync(args[1], cancellation.Token);
        await Task.Delay(50);
        cancellation.Cancel();
        await ExpectCancellationAsync(inference);
    }

    var recovered = await transcriber.TranscribeAsync(args[1], CancellationToken.None);
    if (string.IsNullOrWhiteSpace(recovered))
    {
        throw new InvalidOperationException("Transcription failed after cancellation.");
    }

    Console.WriteLine($"Recovered after cancellation: {recovered}");

    var pending = transcriber.TranscribeAsync(args[1], CancellationToken.None);
    await Task.Delay(50);
    await transcriber.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30));
    await ExpectCancellationAsync(pending);
    await transcriber.DisposeAsync();

    try
    {
        await transcriber.TranscribeAsync(args[1], CancellationToken.None);
        throw new InvalidOperationException("A disposed transcriber accepted more work.");
    }
    catch (ObjectDisposedException)
    {
        Console.WriteLine("Disposal during inference and rejection of further calls: passed.");
    }

    foreach (ProcessModule module in Process.GetCurrentProcess().Modules)
    {
        if (module.ModuleName.Contains("whisper", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"Loaded in process: {module.FileName}");
        }
    }

    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    return 1;
}

static async Task ExpectCancellationAsync(Task<string> inference)
{
    try
    {
        await inference.WaitAsync(TimeSpan.FromSeconds(30));
        throw new InvalidOperationException("Inference finished before cancellation; use a longer speech fixture.");
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine("Native inference cancellation: passed.");
    }
}
