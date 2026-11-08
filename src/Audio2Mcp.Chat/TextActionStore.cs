using System.Text.Json;

namespace Audio2Mcp.Chat;

/// <summary>Stores action definitions locally. Failed saves leave the previous file intact.</summary>
public sealed class TextActionStore(string path)
{
    /// <summary>Loads and validates definitions; a missing file represents an empty catalog.</summary>
    public IReadOnlyList<TextAction> Load()
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var actions = JsonSerializer.Deserialize<TextAction[]>(File.ReadAllText(path))
            ?? throw new InvalidOperationException("Le fichier d’actions est vide.");
        Validate(actions);
        return Array.AsReadOnly(actions);
    }

    /// <summary>Validates the entire catalog before atomically replacing its file.</summary>
    public void Save(IReadOnlyList<TextAction> actions)
    {
        Validate(actions);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(actions, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static void Validate(IReadOnlyList<TextAction> actions)
    {
        foreach (var action in actions)
        {
            if (action is null)
            {
                throw new InvalidOperationException("Définition d’action invalide.");
            }

            action.Validate();
        }

        if (actions.Select(action => action.Name).Distinct(StringComparer.Ordinal).Count() != actions.Count)
        {
            throw new InvalidOperationException("Chaque action doit avoir un nom unique.");
        }
    }
}
