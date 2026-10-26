namespace LocalCraft.Conversion.Engines;

public sealed record ConversionEngineMatch(
    bool IsSupported,
    IConversionEngine? Engine,
    string Note);
