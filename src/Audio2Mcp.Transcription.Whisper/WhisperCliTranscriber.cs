using System.Diagnostics;
using Audio2Mcp.Core;

namespace Audio2Mcp.Transcription.Whisper;

/// <summary>Runs a local whisper-cli process for each chunk and returns its text output.</summary>
public sealed class WhisperCliTranscriber : ITranscriber
{
    private static readonly TimeSpan InferenceTimeout = TimeSpan.FromMinutes(2);

    private readonly string executable;
    private readonly string model;
    private readonly string language;

    /// <summary>Resolves and validates the executable and GGML model paths.</summary>
    public WhisperCliTranscriber(string executable, string model, string language = "fr")
    {
        this.executable = Path.GetFullPath(executable);
        this.model = Path.GetFullPath(model);
        this.language = language;

        if (!File.Exists(this.executable))
        {
            throw new FileNotFoundException("whisper-cli introuvable.", this.executable);
        }

        if (!File.Exists(this.model))
        {
            throw new FileNotFoundException("Modèle GGML introuvable.", this.model);
        }

    }

    /// <summary>Transcribes one chunk, killing the process tree on cancellation or timeout.</summary>
    public async Task<string> TranscribeAsync(string wavPath, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(InferenceTimeout);

        var output = Path.GetFullPath(wavPath) + "." + Guid.NewGuid().ToString("N");
        using var process = new Process { StartInfo = CreateStartInfo(wavPath, output) };

        try
        {
            process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();

            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                await process.WaitForExitAsync(CancellationToken.None);
                await Task.WhenAll(stdout, stderr);
                if (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException("whisper-cli dépasse 2 minutes par segment.");
                }

                throw;
            }

            await stdout;
            var error = await stderr;

            if (process.ExitCode != 0)
            {
                throw new IOException($"whisper-cli ({process.ExitCode}) : {error}");
            }
            if (!File.Exists(output + ".txt"))
            {
                throw new IOException("whisper-cli n'a pas créé le texte attendu.");
            }

            return await File.ReadAllTextAsync(output + ".txt", cancellationToken);
        }
        finally
        {
            if (File.Exists(output + ".txt"))
            {
                File.Delete(output + ".txt");
            }

        }
    }

    private ProcessStartInfo CreateStartInfo(string wavPath, string output)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        // ArgumentList keeps file paths intact without shell quoting or interpolation.
        string[] arguments =
        [
            "-m", model,
            "-f", Path.GetFullPath(wavPath),
            "-l", language,
            "-otxt", "-of", output,
            "-nt", "-np", "-t", "4"
        ];

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        return info;
    }

    /// <summary>No persistent resources are retained between process-based transcriptions.</summary>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
