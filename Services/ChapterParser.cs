using NovelShelf.Models;
using System.Text.RegularExpressions;

namespace NovelShelf.Services;

public static partial class ChapterParser
{
    public static IReadOnlyList<ChapterInfo> Extract(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<ChapterInfo>();
        }

        var chapters = new List<ChapterInfo>();
        foreach (Match match in ChapterHeadingRegex().Matches(text))
        {
            var title = match.Groups["title"].Value.Trim();
            if (title.Length == 0)
            {
                continue;
            }

            chapters.Add(new ChapterInfo
            {
                Title = title.Length > 48 ? title[..48] : title,
                CharacterOffset = match.Index
            });
        }

        return chapters
            .GroupBy(chapter => chapter.CharacterOffset)
            .Select(group => group.First())
            .ToList();
    }

    [GeneratedRegex(@"(?m)^\s*(?<title>(第[0-9零〇一二两三四五六七八九十百千万]+[章节卷回部篇][^\r\n]{0,36}|Chapter\s+[0-9]+[^\r\n]{0,36}))\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex ChapterHeadingRegex();
}

