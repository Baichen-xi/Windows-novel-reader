using Microsoft.Win32;
using NovelShelf.Models;
using NovelShelf.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NovelShelf;

public partial class MainWindow : Window
{
    private readonly LibraryStore _store = new();
    private readonly ObservableCollection<BookInfo> _books = new();
    private readonly ObservableCollection<ChapterInfo> _chapters = new();
    private readonly ReaderSettings _settings;
    private BookInfo? _currentBook;
    private ScrollViewer? _readerScrollViewer;
    private bool _isLoadingBook;
    private bool _isNavigatingChapter;
    private int _lastSearchIndex = -1;

    public MainWindow()
    {
        InitializeComponent();
        _settings = _store.LoadSettings();
        BooksList.ItemsSource = _books;
        ChaptersList.ItemsSource = _chapters;
        ApplySettingsToControls();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        LoadLibrary();
        _readerScrollViewer = FindVisualChild<ScrollViewer>(ReaderTextBox);
        if (_readerScrollViewer is not null)
        {
            _readerScrollViewer.ScrollChanged += (_, _) => SaveVisiblePosition();
        }
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        SaveVisiblePosition();
        _store.Save(_books);
        _store.SaveSettings(_settings);
    }

    private void LoadLibrary()
    {
        _books.Clear();
        foreach (var book in _store.Load())
        {
            if (File.Exists(book.StoredPath))
            {
                _books.Add(book);
            }
        }

        if (_books.Count > 0)
        {
            BooksList.SelectedIndex = 0;
        }

        UpdateLibraryCount();
    }

    private void ImportBook_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 TXT 小说",
            Filter = "TXT 小说 (*.txt)|*.txt|所有文件 (*.*)|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var imported = _store.Import(dialog.FileName);
            _books.Add(imported);
            _store.Save(_books);
            UpdateLibraryCount();
            BooksList.SelectedItem = imported;
            StatusText.Text = $"已导入：{imported.Title}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "导入失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BooksList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BooksList.SelectedItem is BookInfo book)
        {
            OpenBook(book);
        }
    }

    private void ChaptersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isNavigatingChapter || ChaptersList.SelectedItem is not ChapterInfo chapter)
        {
            return;
        }

        NavigateToOffset(chapter.CharacterOffset, saveToBook: true);
    }

    private void OpenBook(BookInfo book)
    {
        SaveVisiblePosition();
        _currentBook = book;
        _isLoadingBook = true;
        _lastSearchIndex = -1;

        try
        {
            ReaderTextBox.Text = TextFileReader.Read(book.StoredPath);
            TitleText.Text = book.Title;
            MetaText.Text = $"来源：{book.OriginalFileName} · 导入时间：{book.ImportedAt:yyyy-MM-dd HH:mm}";
            RefreshChapters(ReaderTextBox.Text);
            NavigateToOffset(book.CharacterOffset, saveToBook: false);
            UpdateStatus();
        }
        catch (Exception ex)
        {
            ReaderTextBox.Text = "";
            _chapters.Clear();
            MessageBox.Show(this, ex.Message, "打开失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isLoadingBook = false;
        }
    }

    private void RefreshChapters(string text)
    {
        _chapters.Clear();
        foreach (var chapter in ChapterParser.Extract(text))
        {
            _chapters.Add(chapter);
        }

        UpdateCatalogCount();

        if (_chapters.Count == 0)
        {
            StatusText.Text = "未识别到章节，可以继续作为整本书阅读。";
        }
    }

    private void PreviousChapter_Click(object sender, RoutedEventArgs e)
    {
        if (_chapters.Count == 0)
        {
            return;
        }

        var index = ChaptersList.SelectedIndex <= 0 ? 0 : ChaptersList.SelectedIndex - 1;
        ChaptersList.SelectedIndex = index;
        NavigateToOffset(_chapters[index].CharacterOffset, saveToBook: true);
    }

    private void NextChapter_Click(object sender, RoutedEventArgs e)
    {
        if (_chapters.Count == 0)
        {
            return;
        }

        var index = ChaptersList.SelectedIndex < 0 ? 0 : Math.Min(_chapters.Count - 1, ChaptersList.SelectedIndex + 1);
        ChaptersList.SelectedIndex = index;
        NavigateToOffset(_chapters[index].CharacterOffset, saveToBook: true);
    }

    private void SavePosition_Click(object sender, RoutedEventArgs e)
    {
        SaveVisiblePosition();
        _store.Save(_books);
        StatusText.Text = "阅读位置已保存。";
    }

    private void RemoveBook_Click(object sender, RoutedEventArgs e)
    {
        if (_currentBook is null)
        {
            return;
        }

        var result = MessageBox.Show(
            this,
            "会从本机应用数据目录删除这本小说的副本，原始文件不会被删除。",
            "确认移除",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.OK)
        {
            return;
        }

        var book = _currentBook;
        _books.Remove(book);
        _store.DeleteBookFile(book);
        _store.Save(_books);
        _currentBook = null;
        _chapters.Clear();
        UpdateLibraryCount();
        UpdateCatalogCount();
        ReaderTextBox.Text = "";
        TitleText.Text = "还没有打开小说";
        MetaText.Text = "";
        StatusText.Text = "已从本地书库移除。";
    }

    private void FindNext_Click(object sender, RoutedEventArgs e)
    {
        FindText(forward: true);
    }

    private void FindPrevious_Click(object sender, RoutedEventArgs e)
    {
        FindText(forward: false);
    }

    private void FontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded && ReaderTextBox is null)
        {
            return;
        }

        _settings.FontSize = e.NewValue;
        ReaderTextBox.FontSize = e.NewValue;
        _store.SaveSettings(_settings);
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeComboBox.SelectedItem is not ComboBoxItem item || item.Tag is not string theme)
        {
            return;
        }

        _settings.Theme = theme;
        ApplyTheme(theme);
        _store.SaveSettings(_settings);
    }

    private void ReaderTextBox_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (!_isLoadingBook)
        {
            SaveVisiblePosition();
        }
    }

    private void FindText(bool forward)
    {
        var query = SearchBox.Text.Trim();
        var text = ReaderTextBox.Text;
        if (query.Length == 0 || text.Length == 0)
        {
            StatusText.Text = "请输入要查找的关键词。";
            return;
        }

        var comparison = StringComparison.CurrentCultureIgnoreCase;
        var start = ReaderTextBox.SelectionStart;
        int index;

        if (forward)
        {
            start = _lastSearchIndex >= 0 ? _lastSearchIndex + query.Length : start + ReaderTextBox.SelectionLength;
            if (start >= text.Length)
            {
                start = 0;
            }

            index = text.IndexOf(query, start, comparison);
            if (index < 0 && start > 0)
            {
                index = text.IndexOf(query, 0, comparison);
            }
        }
        else
        {
            start = _lastSearchIndex > 0 ? _lastSearchIndex - 1 : Math.Max(0, start - 1);
            index = text.LastIndexOf(query, start, comparison);
            if (index < 0 && start < text.Length - 1)
            {
                index = text.LastIndexOf(query, text.Length - 1, comparison);
            }
        }

        if (index < 0)
        {
            StatusText.Text = $"没有找到：{query}";
            return;
        }

        _lastSearchIndex = index;
        ReaderTextBox.Focus();
        ReaderTextBox.Select(index, query.Length);
        ReaderTextBox.ScrollToLine(GetLineIndex(index));
        NavigateToOffset(index, saveToBook: true);
        StatusText.Text = $"已找到：{query}";
    }

    private void NavigateToOffset(int characterOffset, bool saveToBook)
    {
        if (ReaderTextBox.Text.Length == 0)
        {
            return;
        }

        var offset = Math.Clamp(characterOffset, 0, ReaderTextBox.Text.Length);
        ReaderTextBox.CaretIndex = offset;
        ReaderTextBox.ScrollToLine(GetLineIndex(offset));

        if (saveToBook && _currentBook is not null)
        {
            _currentBook.CharacterOffset = offset;
            _currentBook.LastReadAt = DateTimeOffset.Now;
            _store.Save(_books);
        }

        UpdateSelectedChapter(offset);
        UpdateStatus();
    }

    private void SaveVisiblePosition()
    {
        if (_currentBook is null || _isLoadingBook || ReaderTextBox.Text.Length == 0)
        {
            return;
        }

        var firstVisibleLine = _readerScrollViewer is null
            ? GetLineIndex(ReaderTextBox.CaretIndex)
            : Math.Max(0, ReaderTextBox.GetFirstVisibleLineIndex());

        var offset = ReaderTextBox.GetCharacterIndexFromLineIndex(firstVisibleLine);
        if (offset < 0)
        {
            offset = ReaderTextBox.CaretIndex;
        }

        _currentBook.CharacterOffset = Math.Clamp(offset, 0, ReaderTextBox.Text.Length);
        _currentBook.LastReadAt = DateTimeOffset.Now;
        UpdateSelectedChapter(_currentBook.CharacterOffset);
        UpdateStatus();
    }

    private void UpdateSelectedChapter(int offset)
    {
        if (_chapters.Count == 0)
        {
            return;
        }

        var selected = _chapters[0];
        foreach (var chapter in _chapters)
        {
            if (chapter.CharacterOffset > offset)
            {
                break;
            }

            selected = chapter;
        }

        _isNavigatingChapter = true;
        ChaptersList.SelectedItem = selected;
        ChaptersList.ScrollIntoView(selected);
        _isNavigatingChapter = false;
    }

    private void UpdateStatus()
    {
        if (_currentBook is null || ReaderTextBox.Text.Length == 0)
        {
            return;
        }

        var chapterText = ChaptersList.SelectedItem is ChapterInfo chapter
            ? $" · {chapter.Title}"
            : "";
        StatusText.Text = $"阅读位置：{_currentBook.CharacterOffset:N0} / {ReaderTextBox.Text.Length:N0}{chapterText}";
    }

    private void UpdateLibraryCount()
    {
        LibraryCountText.Text = _books.Count == 0
            ? "0 本本地小说"
            : $"{_books.Count} 本本地小说";
    }

    private void UpdateCatalogCount()
    {
        CatalogCountText.Text = _chapters.Count == 0
            ? "尚未载入章节"
            : $"{_chapters.Count} 个章节";
    }

    private void ApplySettingsToControls()
    {
        FontSizeSlider.Value = _settings.FontSize;
        ReaderTextBox.FontSize = _settings.FontSize;

        foreach (var item in ThemeComboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag as string, _settings.Theme, StringComparison.OrdinalIgnoreCase))
            {
                ThemeComboBox.SelectedItem = item;
                break;
            }
        }

        if (ThemeComboBox.SelectedItem is null)
        {
            ThemeComboBox.SelectedIndex = 0;
        }

        ApplyTheme(_settings.Theme);
    }

    private void ApplyTheme(string theme)
    {
        var palette = theme switch
        {
            "Dark" => new ThemePalette("#171717", "#222222", "#2A2A2A", "#E8E3D8", "#B8AEA1", "#3A564A"),
            "Green" => new ThemePalette("#E7F0E7", "#F4FAF1", "#F4FAF1", "#263326", "#65755F", "#4F765C"),
            _ => new ThemePalette("#F5F1E8", "#FFFCF5", "#FFFCF5", "#2A2118", "#7A7066", "#426B5A")
        };

        Background = BrushFrom(palette.AppBackground);
        RootGrid.Background = BrushFrom(palette.AppBackground);
        AppHeaderBorder.Background = BrushFrom(palette.PanelBackground);
        AppHeaderBorder.BorderBrush = BrushFrom(palette.Border);
        LibraryBorder.Background = BrushFrom(palette.PanelBackground);
        LibraryBorder.BorderBrush = BrushFrom(palette.Border);
        CatalogBorder.Background = BrushFrom(palette.PanelBackground);
        CatalogBorder.BorderBrush = BrushFrom(palette.Border);
        CatalogHintBorder.Background = BrushFrom(palette.ReaderBackground);
        CatalogHintBorder.BorderBrush = BrushFrom(palette.Border);
        ReaderToolbarBorder.Background = BrushFrom(palette.PanelBackground);
        ReaderToolbarBorder.BorderBrush = BrushFrom(palette.Border);
        ReaderBorder.Background = BrushFrom(palette.ReaderBackground);
        ReaderBorder.BorderBrush = BrushFrom(palette.Border);
        ReaderTextBox.Background = BrushFrom(palette.ReaderBackground);
        ReaderTextBox.Foreground = BrushFrom(palette.Text);
        SetTextForeground(RootGrid, BrushFrom(palette.Text));
        MetaText.Foreground = BrushFrom(palette.MutedText);
        StatusText.Foreground = BrushFrom(palette.MutedText);
        LibraryCountText.Foreground = BrushFrom(palette.MutedText);
        CatalogCountText.Foreground = BrushFrom(palette.MutedText);
        BooksList.Background = BrushFrom(palette.PanelBackground);
        BooksList.Foreground = BrushFrom(palette.Text);
        ChaptersList.Background = BrushFrom(palette.PanelBackground);
        ChaptersList.Foreground = BrushFrom(palette.Text);
    }

    private int GetLineIndex(int characterIndex)
    {
        if (ReaderTextBox.Text.Length == 0)
        {
            return 0;
        }

        return Math.Max(0, ReaderTextBox.GetLineIndexFromCharacterIndex(characterIndex));
    }

    private static Brush BrushFrom(string color) => (Brush)new BrushConverter().ConvertFromString(color)!;

    private static void SetTextForeground(DependencyObject parent, Brush brush)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is TextBlock textBlock)
            {
                textBlock.Foreground = brush;
            }

            SetTextForeground(child, brush);
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
            {
                return typedChild;
            }

            var nested = FindVisualChild<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private sealed record ThemePalette(
        string AppBackground,
        string PanelBackground,
        string ReaderBackground,
        string Text,
        string MutedText,
        string Border);
}
