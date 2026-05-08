namespace NovelShelf.Models;

public sealed class BookInfo
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public string OriginalFileName { get; set; } = "";
    public string StoredPath { get; set; } = "";
    public DateTimeOffset ImportedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset LastReadAt { get; set; } = DateTimeOffset.Now;
    public int CharacterOffset { get; set; }
    public int ChapterCacheVersion { get; set; }
    public long StoredFileSize { get; set; }
    public DateTimeOffset StoredFileLastWriteTime { get; set; }
    public List<ChapterInfo> Chapters { get; set; } = new();
}

