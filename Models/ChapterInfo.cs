namespace NovelShelf.Models;

public sealed class ChapterInfo
{
    public string Title { get; set; } = "";
    public int CharacterOffset { get; set; }

    public override string ToString() => Title;
}

