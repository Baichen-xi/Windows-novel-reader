namespace NovelShelf.Models;

public sealed class ReaderSettings
{
    public double FontSize { get; set; } = 20;
    public string FontFamily { get; set; } = "SimSun";
    public double LineSpacing { get; set; } = 1.8;
    public double ParagraphSpacing { get; set; } = 1.2;
    public double PageWidth { get; set; } = 880;
    public double PagePadding { get; set; } = 72;
    public string Theme { get; set; } = "Ink";
}
