namespace Lifestone.Net.Messages;

/// <summary>
/// F7E7 DDD_BeginDDD: the server found missing iterations and would start a patch download. Lifestone never patches, so
/// the session fails fast; only the two leading words are decoded.
/// </summary>
/// <param name="TotalFileSize">The total size of the patches.</param>
/// <param name="MissingIterations">The number of missing iterations.</param>
public readonly record struct DddBeginMsg(uint TotalFileSize, uint MissingIterations);
