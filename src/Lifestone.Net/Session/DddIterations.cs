namespace Lifestone.Net.Session;

/// <summary>
/// The dat iterations the client reports in F7E6 DDD_InterrogationResponse (docs/protocol.md §4.2 and §5.2). They must
/// equal the server's exactly, or ACE boots the session (patching is off). The host fills this from
/// <c>DatSet.Iterations</c> (<c>DatIterations</c>; its Local is the language dat); never hard-coded (m2-plan §1.6 item
/// 24). End of retail: portal 2072, cell 982, language 994, high-res 497.
/// </summary>
/// <param name="Portal">client_portal.dat's iteration (greater than 0).</param>
/// <param name="Cell">client_cell_1.dat's iteration (0 is tolerated by ACE).</param>
/// <param name="Language">client_local_English.dat's iteration (greater than 0; required).</param>
/// <param name="HighRes">client_highres.dat's iteration, reported only when the server allows it (F7E5 product bit
/// 0x4); null when the client has none.</param>
public readonly record struct DddIterations(int Portal, int Cell, int Language, int? HighRes);
