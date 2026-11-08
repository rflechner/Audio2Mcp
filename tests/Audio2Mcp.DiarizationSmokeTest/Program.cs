using Audio2Mcp.Core;
using Audio2Mcp.Diarization;
using Audio2Mcp.Transcription.Whisper;

// Explicit offline integration check. All models and the speech WAV must already be local.
if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine("Usage: <speech.wav> [expected-speakers]");
    return 1;
}

try
{
    var diarizer = new SherpaSpeakerDiarizer(
        "models/diarization/sherpa-onnx-pyannote-segmentation-3-0/model.onnx",
        "models/diarization/nemo_en_titanet_small.onnx");
    var samples = RecordingAudio.Read(args[0], CancellationToken.None);
    var turns = diarizer.Analyze(samples, 0, CancellationToken.None);
    var speakers = turns.Select(turn => turn.Speaker).Distinct().Count();
    Console.WriteLine($"Automatic detection: {speakers} speakers, {turns.Count} turns.");
    foreach (var turn in turns)
    {
        Console.WriteLine($"{turn.Start.TotalSeconds:F2}-{turn.End.TotalSeconds:F2}: speaker {turn.Speaker}");
    }

    if (args.Length == 2 && speakers != int.Parse(args[1]))
    {
        throw new InvalidOperationException("Unexpected number of speaker clusters.");
    }

    var output = Path.Combine("captures", $"diarization-smoke-{DateTime.Now:yyyyMMdd-HHmmss-fff}");
    await using var transcriber = new WhisperNetTranscriber("models/ggml-base.bin", "auto");
    var recordings = new[]
    {
        new SourceRecording(AudioSource.SystemOutput, args[0], TimeSpan.Zero,
            TimeSpan.FromSeconds((double)samples.Length / RecordingAudio.SampleRate), 1)
    };
    var result = await new ConversationAnalysis(diarizer).AnalyzeAsync(
        recordings, output, 0, transcriber, null, CancellationToken.None);
    Console.WriteLine(await File.ReadAllTextAsync(result.TranscriptPath));
    Console.WriteLine($"Saved: {result.TranscriptPath}");
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    return 1;
}
