using Microsoft.Win32;
using NovelShelf.Models;
using NovelShelf.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace NovelShelf;

public partial class MainWindow : Window
{
    private readonly LibraryStore _store = new();
    private readonly ObservableCollection<BookInfo> _books = new();
    private readonly ObservableCollection<ChapterInfo> _chapters = new();
    private readonly ReaderSettings _settings;
    private BookInfo? _currentBook;
    private string _bookText = "";
    private int _currentChapterIndex = -1;
    private int _currentChapterStartOffset;
    private int _currentChapterEndOffset;
    private ScrollViewer? _readerScrollViewer;
    private readonly DispatcherTimer _clockTimer = new();
    private readonly DispatcherTimer _positionUpdateTimer = new();
    private bool _isLoadingBook;
    private bool _isNavigatingChapter;
    private bool _isLibraryVisible;
    private bool _isCatalogVisible;
    private bool _isOptionsVisible;
    private bool _isUpdatingProgress;
    private int _lastSelectedChapterIndex = -1;
    private int _lastSearchIndex = -1;

    public MainWindow()
    {
        _settings = _store.LoadSettings();
        InitializeComponent();
        BooksList.ItemsSource = _books;
        HomeGridBooksList.ItemsSource = _books;
        HomeTableBooksList.ItemsSource = _books;
        ChaptersList.ItemsSource = _chapters;
        ApplySettingsToControls();
        ConfigureClock();
        ConfigurePositionUpdateTimer();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        LoadLibrary();
        _readerScrollViewer = FindVisualChild<ScrollViewer>(ReaderTextBox);
        if (_readerScrollViewer is not null)
        {
            _readerScrollViewer.ScrollChanged += (_, _) => ScheduleVisiblePositionSave();
        }
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        SaveVisiblePosition();
        _store.Save(_books);
        _store.SaveSettings(_settings);
        _clockTimer.Stop();
        _positionUpdateTimer.Stop();
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

        UpdateLibraryCount();
        ShowHome();
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
            ShowHome();
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

    private void HomeBook_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox listBox || listBox.SelectedItem is not BookInfo book)
        {
            return;
        }

        HomeGridBooksList.SelectedItem = null;
        HomeTableBooksList.SelectedItem = null;
        OpenBook(book);
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
        ShowReader();
        _currentBook = book;
        _isLoadingBook = true;
        _lastSelectedChapterIndex = -1;
        _lastSearchIndex = -1;

        try
        {
            _bookText = TextFileReader.Read(book.StoredPath);
            TitleText.Text = book.Title;
            MetaText.Text = $"来源：{book.OriginalFileName} · 导入时间：{book.ImportedAt:yyyy-MM-dd HH:mm}";
            RefreshChapters(_bookText);
            if (_chapters.Count > 0)
            {
                SetCatalogVisible(true);
            }
            NavigateToOffset(book.CharacterOffset, saveToBook: false);
            UpdateStatus();
        }
        catch (Exception ex)
        {
            _bookText = "";
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
        _lastSelectedChapterIndex = -1;
        foreach (var chapter in ChapterParser.Extract(text))
        {
            _chapters.Add(chapter);
        }

        if (_chapters.Count > 0 && _chapters[0].CharacterOffset > 0)
        {
            _chapters.Insert(0, new ChapterInfo
            {
                Title = "开篇",
                CharacterOffset = 0
            });
        }

        if (_chapters.Count == 0 && text.Length > 0)
        {
            const int chunkSize = 16000;
            for (var offset = 0; offset < text.Length; offset += chunkSize)
            {
                _chapters.Add(new ChapterInfo
                {
                    Title = $"片段 {(offset / chunkSize) + 1}",
                    CharacterOffset = offset
                });
            }
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
        _bookText = "";
        _currentChapterIndex = -1;
        _currentChapterStartOffset = 0;
        _currentChapterEndOffset = 0;
        _chapters.Clear();
        UpdateLibraryCount();
        UpdateCatalogCount();
        ReaderTextBox.Text = "";
        TitleText.Text = "还没有打开小说";
        CurrentChapterText.Text = "阅读区";
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

    private void ToggleLibrary_Click(object sender, RoutedEventArgs e)
    {
        SetLibraryVisible(!_isLibraryVisible);
    }

    private void ToggleCatalog_Click(object sender, RoutedEventArgs e)
    {
        SetCatalogVisible(!_isCatalogVisible);
    }

    private void ToggleOptions_Click(object sender, RoutedEventArgs e)
    {
        SetOptionsVisible(!_isOptionsVisible);
    }

    private void ShowHome_Click(object sender, RoutedEventArgs e)
    {
        SaveVisiblePosition();
        ShowHome();
    }

    private void ShowGridShelf_Click(object sender, RoutedEventArgs e)
    {
        HomeGridBooksList.Visibility = Visibility.Visible;
        HomeTableBooksList.Visibility = Visibility.Collapsed;
    }

    private void ShowListShelf_Click(object sender, RoutedEventArgs e)
    {
        HomeGridBooksList.Visibility = Visibility.Collapsed;
        HomeTableBooksList.Visibility = Visibility.Visible;
    }

    private void ReaderSurface_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && IsInteractiveElement(source))
        {
            return;
        }

        ControlLayer.Visibility = ControlLayer.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (ControlLayer.Visibility == Visibility.Visible)
        {
            UpdateStatus();
        }
    }

    private void Bookmark_Click(object sender, RoutedEventArgs e)
    {
        SaveVisiblePosition();
        _store.Save(_books);
        StatusText.Text = "已记录当前位置。";
    }

    private void ReadAloud_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "朗读功能还未接入，入口已预留。";
    }

    private void CycleTheme_Click(object sender, RoutedEventArgs e)
    {
        var themes = new[] { "Ink", "Dark", "Green", "Vintage" };
        var index = Array.IndexOf(themes, _settings.Theme);
        _settings.Theme = themes[(index + 1 + themes.Length) % themes.Length];
        SelectThemeComboItem(_settings.Theme);
        ApplyTheme(_settings.Theme);
        _store.SaveSettings(_settings);
    }

    private void ChapterProgressSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingProgress || _bookText.Length == 0)
        {
            return;
        }

        var offset = (int)Math.Round(_bookText.Length * e.NewValue / 100);
        NavigateToOffset(offset, saveToBook: true);
    }

    private void FontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ReaderTextBox is null)
        {
            return;
        }

        _settings.FontSize = e.NewValue;
        ApplyTypography();
        _store.SaveSettings(_settings);
    }

    private void FontFamilyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FontFamilyComboBox?.SelectedItem is not ComboBoxItem item || item.Tag is not string fontFamily)
        {
            return;
        }

        _settings.FontFamily = fontFamily;
        ApplyTypography();
        _store.SaveSettings(_settings);
    }

    private void LineSpacingSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _settings.LineSpacing = e.NewValue;
        ApplyTypography();
        _store.SaveSettings(_settings);
    }

    private void ParagraphSpacingSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _settings.ParagraphSpacing = e.NewValue;
        ApplyTypography();
        _store.SaveSettings(_settings);
    }

    private void PageWidthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _settings.PageWidth = e.NewValue;
        ApplyTypography();
        _store.SaveSettings(_settings);
    }

    private void PagePaddingSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _settings.PagePadding = e.NewValue;
        ApplyTypography();
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
            ScheduleVisiblePositionSave();
        }
    }

    private void FindText(bool forward)
    {
        var query = SearchBox.Text.Trim();
        var text = _bookText;
        if (query.Length == 0 || text.Length == 0)
        {
            StatusText.Text = "请输入要查找的关键词。";
            return;
        }

        var comparison = StringComparison.CurrentCultureIgnoreCase;
        var start = _currentBook?.CharacterOffset ?? 0;
        int index;

        if (forward)
        {
            start = _lastSearchIndex >= 0 ? _lastSearchIndex + query.Length : start;
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
        NavigateToOffset(index, saveToBook: true);
        var selectionStart = Math.Clamp(index - _currentChapterStartOffset, 0, ReaderTextBox.Text.Length);
        var selectionLength = Math.Min(query.Length, Math.Max(0, ReaderTextBox.Text.Length - selectionStart));
        ReaderTextBox.Select(selectionStart, selectionLength);
        StatusText.Text = $"已找到：{query}";
    }

    private void NavigateToOffset(int characterOffset, bool saveToBook)
    {
        if (_bookText.Length == 0)
        {
            return;
        }

        var offset = Math.Clamp(characterOffset, 0, _bookText.Length);
        var chapterIndex = FindChapterIndex(offset);
        LoadChapter(chapterIndex);

        var localOffset = Math.Clamp(offset - _currentChapterStartOffset, 0, ReaderTextBox.Text.Length);
        ReaderTextBox.CaretIndex = localOffset;
        ReaderTextBox.ScrollToLine(GetLineIndex(localOffset));

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
        if (_currentBook is null || _isLoadingBook || ReaderTextBox.Text.Length == 0 || _bookText.Length == 0)
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

        var globalOffset = _currentChapterStartOffset + offset;
        _currentBook.CharacterOffset = Math.Clamp(globalOffset, 0, _bookText.Length);
        _currentBook.LastReadAt = DateTimeOffset.Now;
        UpdateSelectedChapter(_currentBook.CharacterOffset);
        UpdateStatus();
    }

    private void ScheduleVisiblePositionSave()
    {
        if (_currentBook is null || _isLoadingBook || ReaderTextBox.Text.Length == 0 || _bookText.Length == 0)
        {
            return;
        }

        _positionUpdateTimer.Stop();
        _positionUpdateTimer.Start();
    }

    private void UpdateSelectedChapter(int offset)
    {
        if (_chapters.Count == 0)
        {
            return;
        }

        var selectedIndex = FindChapterIndex(offset);

        if (selectedIndex == _lastSelectedChapterIndex)
        {
            return;
        }

        var selected = _chapters[selectedIndex];
        _lastSelectedChapterIndex = selectedIndex;
        _isNavigatingChapter = true;
        ChaptersList.SelectedItem = selected;
        if (_isCatalogVisible)
        {
            ChaptersList.ScrollIntoView(selected);
        }
        _isNavigatingChapter = false;
    }

    private int FindChapterIndex(int offset)
    {
        if (_chapters.Count == 0)
        {
            return -1;
        }

        var low = 0;
        var high = _chapters.Count - 1;
        var selectedIndex = 0;

        while (low <= high)
        {
            var mid = low + ((high - low) / 2);
            if (_chapters[mid].CharacterOffset <= offset)
            {
                selectedIndex = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return selectedIndex;
    }

    private void LoadChapter(int chapterIndex)
    {
        if (chapterIndex < 0 || chapterIndex >= _chapters.Count || _bookText.Length == 0)
        {
            return;
        }

        if (chapterIndex == _currentChapterIndex && ReaderTextBox.Text.Length > 0)
        {
            return;
        }

        _currentChapterIndex = chapterIndex;
        _currentChapterStartOffset = _chapters[chapterIndex].CharacterOffset;
        _currentChapterEndOffset = chapterIndex + 1 < _chapters.Count
            ? _chapters[chapterIndex + 1].CharacterOffset
            : _bookText.Length;

        var length = Math.Max(0, _currentChapterEndOffset - _currentChapterStartOffset);
        ReaderTextBox.Text = _bookText.Substring(_currentChapterStartOffset, length);
        ApplyTypography();
    }

    private void UpdateStatus()
    {
        if (_currentBook is null || _bookText.Length == 0)
        {
            return;
        }

        var chapterText = ChaptersList.SelectedItem is ChapterInfo chapter
            ? $" · {chapter.Title}"
            : "";
        CurrentChapterText.Text = ChaptersList.SelectedItem is ChapterInfo currentChapter
            ? currentChapter.Title
            : "阅读区";
        var progress = _bookText.Length == 0
            ? 0
            : (double)_currentBook.CharacterOffset / _bookText.Length;
        var percentage = Math.Clamp(progress, 0, 1);

        BottomProgressText.Text = $"{percentage:P0}";
        StatusText.Text = $"阅读位置：{_currentBook.CharacterOffset:N0} / {_bookText.Length:N0}{chapterText}";

        if (ControlLayer.Visibility == Visibility.Visible)
        {
            _isUpdatingProgress = true;
            ChapterProgressSlider.Value = percentage * 100;
            _isUpdatingProgress = false;
        }
    }

    private void SetLibraryVisible(bool visible)
    {
        _isLibraryVisible = visible;
        LibraryColumn.Width = visible ? new GridLength(280) : new GridLength(0);
    }

    private void SetCatalogVisible(bool visible)
    {
        _isCatalogVisible = visible;
        CatalogColumn.Width = visible ? new GridLength(300) : new GridLength(0);
        if (visible && ChaptersList.SelectedItem is ChapterInfo chapter)
        {
            ChaptersList.ScrollIntoView(chapter);
        }
    }

    private void SetOptionsVisible(bool visible)
    {
        _isOptionsVisible = visible;
        OptionsColumn.Width = visible ? new GridLength(260) : new GridLength(0);
    }

    private void ShowHome()
    {
        ControlLayer.Visibility = Visibility.Collapsed;
        HomeSurfaceGrid.Visibility = Visibility.Visible;
        ReaderSurfaceGrid.Visibility = Visibility.Collapsed;
        SetLibraryVisible(false);
        SetCatalogVisible(false);
        SetOptionsVisible(false);
    }

    private void ShowReader()
    {
        HomeSurfaceGrid.Visibility = Visibility.Collapsed;
        ReaderSurfaceGrid.Visibility = Visibility.Visible;
    }

    private void UpdateLibraryCount()
    {
        LibraryCountText.Text = _books.Count == 0
            ? "0 本本地小说"
            : $"{_books.Count} 本本地小说";
        HomeLibraryCountText.Text = LibraryCountText.Text;
        EmptyShelfText.Visibility = _books.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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
        LineSpacingSlider.Value = _settings.LineSpacing;
        ParagraphSpacingSlider.Value = _settings.ParagraphSpacing;
        PageWidthSlider.Value = _settings.PageWidth;
        PagePaddingSlider.Value = _settings.PagePadding;
        SelectFontFamilyComboItem(_settings.FontFamily);
        ApplyTypography();
        _settings.Theme = NormalizeTheme(_settings.Theme);
        SelectThemeComboItem(_settings.Theme);

        ApplyTheme(_settings.Theme);
    }

    private void ApplyTypography()
    {
        if (ReaderTextBox is null || ReaderBorder is null)
        {
            return;
        }

        ReaderTextBox.FontSize = _settings.FontSize;
        ReaderTextBox.FontFamily = new FontFamily(_settings.FontFamily);
        ReaderBorder.MaxWidth = _settings.PageWidth;
        ReaderBorder.Padding = new Thickness(
            _settings.PagePadding,
            Math.Max(28, _settings.PagePadding * 0.64),
            _settings.PagePadding,
            Math.Max(28, _settings.PagePadding * 0.64));

        var lineHeight = _settings.FontSize * _settings.LineSpacing;
        ReaderTextBox.SetValue(TextBlock.LineHeightProperty, lineHeight);
        ReaderTextBox.SetValue(TextBlock.LineStackingStrategyProperty, LineStackingStrategy.BlockLineHeight);
        ReaderTextBox.Padding = new Thickness(0, 0, 0, Math.Max(0, (_settings.ParagraphSpacing - 1) * _settings.FontSize));
    }

    private void ApplyTheme(string theme)
    {
        var palette = theme switch
        {
            "Dark" => new ThemePalette("#1A1C1E", "#222528", "#1A1C1E", "#A0A0A0", "#7D8288", "#2D3237", "#DD222528"),
            "Green" => new ThemePalette("#CCE8CF", "#D9EEDB", "#CCE8CF", "#3E4E42", "#667765", "#AECFB2", "#DDD9EEDB"),
            "Vintage" => new ThemePalette("#EAD8B1", "#F0DFC0", "#EAD8B1", "#5D4037", "#7B6159", "#D3B783", "#DDF0DFC0"),
            _ => new ThemePalette("#F4F1EA", "#F8F5EE", "#F4F1EA", "#2C2C2C", "#77736B", "#D6CDBF", "#DDF8F5EE")
        };

        Background = BrushFrom(palette.AppBackground);
        RootGrid.Background = BrushFrom(palette.AppBackground);
        LibraryBorder.Background = BrushFrom(palette.PanelBackground);
        LibraryBorder.BorderBrush = BrushFrom(palette.Border);
        HomeSurfaceGrid.Background = BrushFrom(palette.AppBackground);
        HomeNavBorder.Background = BrushFrom(palette.OverlayBackground);
        HomeNavBorder.BorderBrush = BrushFrom(palette.Border);
        HomeBooksBorder.Background = BrushFrom(palette.OverlayBackground);
        HomeBooksBorder.BorderBrush = BrushFrom(palette.Border);
        CatalogBorder.Background = BrushFrom(palette.PanelBackground);
        CatalogBorder.BorderBrush = BrushFrom(palette.Border);
        OptionsBorder.Background = BrushFrom(palette.PanelBackground);
        OptionsBorder.BorderBrush = BrushFrom(palette.Border);
        ReaderSurfaceGrid.Background = BrushFrom(palette.AppBackground);
        CatalogHintBorder.Background = BrushFrom(palette.ReaderBackground);
        CatalogHintBorder.BorderBrush = BrushFrom(palette.Border);
        ReaderBorder.Background = BrushFrom(palette.ReaderBackground);
        ReaderBorder.BorderBrush = BrushFrom(palette.ReaderBackground);
        ReaderTextBox.Background = BrushFrom(palette.ReaderBackground);
        ReaderTextBox.Foreground = BrushFrom(palette.Text);
        ControlPanelBorder.Background = BrushFrom(palette.OverlayBackground);
        ControlPanelBorder.BorderBrush = BrushFrom(palette.Border);
        SetTextForeground(RootGrid, BrushFrom(palette.Text));
        MetaText.Foreground = BrushFrom(palette.MutedText);
        StatusText.Foreground = BrushFrom(palette.MutedText);
        LibraryCountText.Foreground = BrushFrom(palette.MutedText);
        HomeLibraryCountText.Foreground = BrushFrom(palette.MutedText);
        EmptyShelfText.Foreground = BrushFrom(palette.MutedText);
        CatalogCountText.Foreground = BrushFrom(palette.MutedText);
        CurrentChapterText.Foreground = BrushFrom(palette.MutedText);
        BottomProgressText.Foreground = BrushFrom(palette.MutedText);
        ClockText.Foreground = BrushFrom(palette.MutedText);
        BooksList.Background = BrushFrom(palette.PanelBackground);
        BooksList.Foreground = BrushFrom(palette.Text);
        HomeGridBooksList.Foreground = BrushFrom(palette.Text);
        HomeTableBooksList.Foreground = BrushFrom(palette.Text);
        ChaptersList.Background = BrushFrom(palette.PanelBackground);
        ChaptersList.Foreground = BrushFrom(palette.Text);
    }

    private void ConfigureClock()
    {
        _clockTimer.Interval = TimeSpan.FromSeconds(10);
        _clockTimer.Tick += (_, _) => UpdateClock();
        UpdateClock();
        _clockTimer.Start();
    }

    private void ConfigurePositionUpdateTimer()
    {
        _positionUpdateTimer.Interval = TimeSpan.FromMilliseconds(250);
        _positionUpdateTimer.Tick += (_, _) =>
        {
            _positionUpdateTimer.Stop();
            SaveVisiblePosition();
        };
    }

    private void UpdateClock()
    {
        ClockText.Text = DateTime.Now.ToString("HH:mm");
    }

    private void SelectThemeComboItem(string theme)
    {
        foreach (var item in ThemeComboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag as string, theme, StringComparison.OrdinalIgnoreCase))
            {
                ThemeComboBox.SelectedItem = item;
                return;
            }
        }

        ThemeComboBox.SelectedIndex = 0;
    }

    private void SelectFontFamilyComboItem(string fontFamily)
    {
        foreach (var item in FontFamilyComboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag as string, fontFamily, StringComparison.OrdinalIgnoreCase))
            {
                FontFamilyComboBox.SelectedItem = item;
                return;
            }
        }

        FontFamilyComboBox.SelectedIndex = 0;
    }

    private static string NormalizeTheme(string theme) => theme switch
    {
        "Paper" => "Ink",
        "浅墨" => "Ink",
        "深夜" => "Dark",
        "护眼" => "Green",
        "复古" => "Vintage",
        _ => theme
    };

    private int GetLineIndex(int characterIndex)
    {
        if (ReaderTextBox.Text.Length == 0)
        {
            return 0;
        }

        return Math.Max(0, ReaderTextBox.GetLineIndexFromCharacterIndex(characterIndex));
    }

    private static Brush BrushFrom(string color) => (Brush)new BrushConverter().ConvertFromString(color)!;

    private static bool IsInteractiveElement(DependencyObject source)
    {
        while (source is not null)
        {
            if (source is FrameworkElement { Name: "ReaderTextBox" })
            {
                return false;
            }

            if (source is FrameworkElement { Name: "ControlPanelBorder" })
            {
                return true;
            }

            if (source is ButtonBase or TextBox or Slider or ComboBox or ListBox)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

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
        string Border,
        string OverlayBackground);
}
