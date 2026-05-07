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
    private BookInfo? _currentBook;
    private ScrollViewer? _readerScrollViewer;
    private bool _isLoadingBook;

    public MainWindow()
    {
        InitializeComponent();
        BooksList.ItemsSource = _books;
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
        if (BooksList.SelectedItem is not BookInfo book)
        {
            return;
        }

        OpenBook(book);
    }

    private void OpenBook(BookInfo book)
    {
        SaveVisiblePosition();
        _currentBook = book;
        _isLoadingBook = true;

        try
        {
            ReaderTextBox.Text = TextFileReader.Read(book.StoredPath);
            TitleText.Text = book.Title;
            MetaText.Text = $"来源：{book.OriginalFileName} · 导入时间：{book.ImportedAt:yyyy-MM-dd HH:mm}";
            StatusText.Text = $"阅读位置：{book.CharacterOffset:N0} / {ReaderTextBox.Text.Length:N0}";

            ReaderTextBox.CaretIndex = Math.Clamp(book.CharacterOffset, 0, ReaderTextBox.Text.Length);
            ReaderTextBox.ScrollToLine(GetLineIndex(ReaderTextBox.CaretIndex));
        }
        catch (Exception ex)
        {
            ReaderTextBox.Text = "";
            MessageBox.Show(this, ex.Message, "打开失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isLoadingBook = false;
        }
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
        ReaderTextBox.Text = "";
        TitleText.Text = "还没有打开小说";
        MetaText.Text = "";
        StatusText.Text = "已从本地书库移除。";
    }

    private void ReaderTextBox_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (!_isLoadingBook)
        {
            SaveVisiblePosition();
        }
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
        StatusText.Text = $"阅读位置：{_currentBook.CharacterOffset:N0} / {ReaderTextBox.Text.Length:N0}";
    }

    private int GetLineIndex(int characterIndex)
    {
        if (ReaderTextBox.Text.Length == 0)
        {
            return 0;
        }

        return Math.Max(0, ReaderTextBox.GetLineIndexFromCharacterIndex(characterIndex));
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
}

