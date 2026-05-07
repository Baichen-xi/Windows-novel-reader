using NovelShelf.Models;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;

namespace NovelShelf.Services;

public sealed class LibraryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public string AppDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NovelShelf");

    public string BooksDirectory => Path.Combine(AppDataDirectory, "books");
    public string LibraryPath => Path.Combine(AppDataDirectory, "library.json");

    public IReadOnlyList<BookInfo> Load()
    {
        Directory.CreateDirectory(BooksDirectory);

        if (!File.Exists(LibraryPath))
        {
            return Array.Empty<BookInfo>();
        }

        var json = File.ReadAllText(LibraryPath);
        return JsonSerializer.Deserialize<List<BookInfo>>(json) ?? new List<BookInfo>();
    }

    public void Save(IEnumerable<BookInfo> books)
    {
        Directory.CreateDirectory(BooksDirectory);
        var json = JsonSerializer.Serialize(books, JsonOptions);
        File.WriteAllText(LibraryPath, json);
    }

    public BookInfo Import(string sourcePath)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("找不到要导入的小说文件。", sourcePath);
        }

        Directory.CreateDirectory(BooksDirectory);

        var id = Guid.NewGuid().ToString("N");
        var extension = Path.GetExtension(sourcePath);
        var storedPath = Path.Combine(BooksDirectory, $"{id}{extension}");
        File.Copy(sourcePath, storedPath, overwrite: false);

        return new BookInfo
        {
            Id = id,
            Title = Path.GetFileNameWithoutExtension(sourcePath),
            OriginalFileName = Path.GetFileName(sourcePath),
            StoredPath = storedPath,
            ImportedAt = DateTimeOffset.Now,
            LastReadAt = DateTimeOffset.Now,
            CharacterOffset = 0
        };
    }

    public void DeleteBookFile(BookInfo book)
    {
        if (File.Exists(book.StoredPath))
        {
            File.Delete(book.StoredPath);
        }
    }
}
