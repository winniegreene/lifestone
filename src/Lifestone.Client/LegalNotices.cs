namespace Lifestone.Client;

/// <summary>A third-party work Lifestone builds on, with its licence and what it is used for.</summary>
/// <param name="Name">The work.</param>
/// <param name="Licence">Its licence (SPDX-style short name).</param>
/// <param name="Use">What Lifestone takes from it.</param>
public sealed record Attribution(string Name, string Licence, string Use);

/// <summary>
/// Lifestone's "Appropriate Legal Notices" (GNU AGPL-3.0 §0 and §5(d)): the copyright notice, that there is no
/// warranty, that the program may be conveyed under the licence and how to see the licence and the source, plus the
/// attributions and the note on Asheron's Call's own data. The single source of this text: the About panel (F1) shows
/// it and a test keeps the README's licence section in step with it, so the UI and the docs cannot drift apart.
/// </summary>
public static class LegalNotices
{
    public const string ProductName = "Lifestone";

    /// <summary>The licence, as LICENSE holds it (the GNU AGPL, version 3).</summary>
    public const string Licence = "GNU Affero General Public License, version 3 (AGPL-3.0)";

    /// <summary>
    /// The copyright line. TODO(lead): the owner has not named the copyright holder or chosen AGPL-3.0-only versus
    /// -or-later (docs audit agpl-version-and-legal-notices); until then the notice names the contributors and the version
    /// LICENSE carries.
    /// </summary>
    public const string Copyright = "Copyright (C) the Lifestone contributors.";

    /// <summary>What the program is.</summary>
    public const string Summary =
        "Lifestone is a faithful, single-player rebuild of Asheron's Call: a Godot client that renders your own retail " +
        "client files, with a bundled local ACEmulator server.";

    /// <summary>The licence grant (the FSF's recommended notice, worded for the version LICENSE carries).</summary>
    public const string LicenceNotice =
        "Lifestone is free software: you can redistribute it and/or modify it under the terms of the " + Licence +
        " as published by the Free Software Foundation.";

    /// <summary>No warranty (AGPL-3.0 §15, in the FSF's recommended words).</summary>
    public const string WarrantyNotice =
        "Lifestone is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied " +
        "warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU Affero General Public License for more details.";

    /// <summary>Where the licence and the corresponding source are.</summary>
    public const string SourceNotice =
        "The full licence text is in the LICENSE file that comes with Lifestone (also at https://www.gnu.org/licenses/agpl-3.0.html). " +
        "The complete corresponding source code is available: see LICENSE and the project repository.";

    /// <summary>Asheron's Call's own data.</summary>
    public const string GameDataNotice =
        "Asheron's Call and its data files are the property of their respective owners. Lifestone contains no retail game " +
        "data: you must supply your own client files.";

    /// <summary>The works Lifestone incorporates, ports from or was checked against (README "License" lists the same).</summary>
    public static IReadOnlyList<Attribution> Attributions { get; } =
    [
        new("ACEmulator (ACE)", "AGPL-3.0", "the bundled server; game rules and algorithms ported from its source"),
        new("ACE-World-16PY-Patches", "AGPL-3.0", "the world database: places, portals and spell visuals"),
        new("OpenAC", "MIT", "terrain blending code (TexMerge)"),
        new("Chorizite DatReaderWriter", "MIT", "reading the dat files"),
        new("WorldBuilder", "MIT", "landscape and terrain blending reference"),
        new("ACViewer", "GPL-3.0", "terrain blending, named locations and test oracles"),
        new("Godot Engine", "MIT", "the game engine"),
    ];

    /// <summary>Every notice as plain text, paragraphs separated by blank lines (the About panel's body).</summary>
    public static string FullText()
    {
        var lines = new List<string>
        {
            Summary,
            string.Empty,
            Copyright,
            LicenceNotice,
            string.Empty,
            WarrantyNotice,
            string.Empty,
            SourceNotice,
            string.Empty,
            "Built on:",
        };
        lines.AddRange(Attributions.Select(a => $"  - {a.Name} ({a.Licence}): {a.Use}"));
        lines.Add(string.Empty);
        lines.Add(GameDataNotice);
        return string.Join('\n', lines);
    }
}
