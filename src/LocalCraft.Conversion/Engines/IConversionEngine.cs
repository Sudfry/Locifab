using LocalCraft.Conversion.Formats;

namespace LocalCraft.Conversion.Engines;

public interface IConversionEngine
{
    string Id { get; }

    string DisplayName { get; }

    bool IsAvailable { get; }

    bool CanConvert(string sourcePath, ConversionTargetFormat target);

    Task ConvertAsync(
        string sourcePath,
        string targetTempPath,
        ConversionTargetFormat target,
        CancellationToken cancellationToken);
}
