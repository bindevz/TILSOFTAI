namespace TILSOFTAI.Domain.ValueObjects;

public sealed record DocumentChunkHit(
    int DocId,
    long ChunkId,
    int ChunkNo,
    string? Title,
    string? Uri,
    string Snippet,
    double Distance
);
