namespace Lifestone.Client.RetailUi.Radar;

/// <summary>
/// The fields of an object's weenie description (<c>PublicWeenieDesc</c>) the radar's rules read: the object description bits, the ItemType, the server's blip colour byte
/// (<c>_blipColor</c>) and radar behaviour (<c>_radar_enum</c>). The retail UI may not name the net layer's types, so the session's <c>WorldObject</c> offers these as plain values.
/// </summary>
/// <param name="Flags">The object description bits (<c>_bitfield</c>).</param>
/// <param name="ItemType">The ItemType.</param>
/// <param name="BlipColor">The server's blip colour (1 to 10), or null when the description sent none.</param>
/// <param name="Behavior">The radar behaviour (2 show on movement, 3 show when attacking, 4 show always), or null when the description sent none.</param>
public readonly record struct RadarObjectDesc(uint Flags, uint ItemType, byte? BlipColor, byte? Behavior);
