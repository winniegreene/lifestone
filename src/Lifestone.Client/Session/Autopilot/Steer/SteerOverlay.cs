using System.Globalization;
using System.Text.Json;

namespace Lifestone.Client.Session.Autopilot.Steer;

/// <summary>
/// The standing rules (the steer contract, section 9): one immutable snapshot, tied to a character's guid, whose version rises on every change. An unreadable file, a newer schema or another character's file is set aside and the rules start empty, as the
/// checkpoint does. This slice stores and restores it; the orders that change it come with their own slices.
/// </summary>
/// <param name="Version">Rises on every change; 0 is the empty overlay.</param>
/// <param name="CharacterGuid">The character the rules belong to.</param>
/// <param name="Rules">Every rule field of the file as read, by name, so a build that does not act on a rule yet still keeps it.</param>
public sealed record SteerOverlay(int Version, uint CharacterGuid, IReadOnlyDictionary<string, string> Rules)
{
    /// <summary>The one schema this build reads.</summary>
    public const int Schema = 1;

    /// <summary>The overlay's file.</summary>
    public const string FileName = "overlay.json";

    /// <summary>The empty overlay of a character.</summary>
    /// <param name="guid">The character's guid.</param>
    public static SteerOverlay Empty(uint guid) => new(0, guid, new Dictionary<string, string>(StringComparer.Ordinal));

    /// <summary>Reads a file's text for <paramref name="guid"/>.</summary>
    /// <param name="text">The file's text.</param>
    /// <param name="guid">The character in the world.</param>
    /// <param name="problem">Why it cannot be used; null when it can.</param>
    /// <returns>The overlay, or null when it must be set aside.</returns>
    public static SteerOverlay? Parse(string text, uint guid, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(text);
        problem = null;
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("v", out var v) || !v.TryGetInt32(out var schema) || schema != Schema)
            {
                problem = "not this build's schema";
                return null;
            }

            if (!root.TryGetProperty("version", out var version) || !version.TryGetInt32(out var number) || number < 0)
            {
                problem = "no version";
                return null;
            }

            if (!root.TryGetProperty("character_guid", out var owner) || owner.GetString() != SteerJson.Guid(guid))
            {
                problem = "another character's rules";
                return null;
            }

            var rules = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
            {
                if (property.Name is not ("v" or "version" or "character_guid"))
                {
                    rules[property.Name] = property.Value.GetRawText();
                }
            }

            return new SteerOverlay(number, guid, rules);
        }
        catch (Exception e) when (e is JsonException or FormatException or InvalidOperationException)
        {
            problem = "not readable: " + e.GetType().Name;
            return null;
        }
    }

    /// <summary>The overlay as its file's text.</summary>
    public string ToJson() => SteerJson.Text(w =>
    {
        w.WriteStartObject();
        w.WriteNumber("v", Schema);
        w.WriteNumber("version", Version);
        w.WriteString("character_guid", SteerJson.Guid(CharacterGuid));
        foreach (var (name, json) in Rules.OrderBy(r => r.Key, StringComparer.Ordinal))
        {
            w.WritePropertyName(name);
            w.WriteRawValue(json);
        }

        w.WriteEndObject();
    });

    /// <inheritdoc/>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"overlay version={Version} rules={Rules.Count}");
}
