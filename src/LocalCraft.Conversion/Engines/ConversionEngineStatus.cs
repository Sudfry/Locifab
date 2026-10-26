namespace LocalCraft.Conversion.Engines;

public sealed record ConversionEngineStatus(
    string Id,
    string Name,
    bool Available,
    string Description);
