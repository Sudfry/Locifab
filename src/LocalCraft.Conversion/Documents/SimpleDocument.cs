namespace LocalCraft.Conversion.Documents;

internal enum DocumentBlockKind
{
    Heading,
    Paragraph,
    Bullet
}

internal sealed record DocumentBlock(DocumentBlockKind Kind, string Text, int Level = 1);

internal sealed class SimpleDocument
{
    public List<DocumentBlock> Blocks { get; } = new();

    public void AddHeading(int level, string text)
    {
        text = text.Trim();
        if (text.Length == 0) return;
        Blocks.Add(new DocumentBlock(DocumentBlockKind.Heading, text, Math.Clamp(level, 1, 6)));
    }

    public void AddParagraph(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return;
        Blocks.Add(new DocumentBlock(DocumentBlockKind.Paragraph, text));
    }

    public void AddBullet(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return;
        Blocks.Add(new DocumentBlock(DocumentBlockKind.Bullet, text));
    }
}
