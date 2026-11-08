using System.Globalization;

/// <summary>Validated settings for a bounded console recording.</summary>
internal sealed record CaptureOptions(
    TimeSpan Duration,
    string OutputDirectory,
    bool TestTone,
    string? WhisperExecutable,
    string? ModelPath,
    string Language,
    bool CaptureMicrophone)
{
    public static CaptureOptions Parse(string[] args)
    {
        double seconds = 10;
        var directory = Path.GetFullPath(Path.Combine("captures", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")));
        string? whisper = null, model = null;
        string language = "fr";
        bool tone = false;
        bool microphone = false;

        for (var i = 0; i < args.Length; i++)
        {
            string ReadValue() => ++i < args.Length ? args[i] : throw new ArgumentException("Valeur manquante.");

            switch (args[i])
            {
                case "--duration":
                    seconds = double.Parse(ReadValue(), CultureInfo.InvariantCulture);
                    break;

                case "--output":
                    directory = Path.GetFullPath(ReadValue());
                    break;

                case "--test-tone":
                    tone = true;
                    break;

                case "--microphone":
                    microphone = true;
                    break;

                case "--whisper":
                    whisper = ReadValue();
                    break;

                case "--model":
                    model = ReadValue();
                    break;

                case "--language":
                    language = ReadValue();
                    break;

                default:
                    throw new ArgumentException($"Option inconnue : {args[i]}");
            }
        }
        if (!double.IsFinite(seconds) || seconds is < 1 or > 3600)
        {
            throw new ArgumentException("Durée attendue : 1 à 3600 secondes.");
        }

        return new CaptureOptions(TimeSpan.FromSeconds(seconds), directory, tone, whisper, model, language, microphone);
    }
}
