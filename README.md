# NovelShelf

NovelShelf 是一个个人使用的 Windows WPF 本地小说阅读器。

## 当前功能

- 导入本地 `.txt` 小说
- 将小说副本持久化保存到 `%LOCALAPPDATA%\NovelShelf\books`
- 使用 `%LOCALAPPDATA%\NovelShelf\library.json` 保存书库和阅读进度
- 支持 UTF-8、UTF-16、GB18030/GBK 常见中文文本编码
- 自动保存当前可见阅读位置
- 从书库移除时只删除应用数据目录里的副本，不删除原始文件

## 隐私和 GitHub

小说正文不会保存到仓库目录。应用导入文件后，会复制到 Windows 用户本机目录：

```text
%LOCALAPPDATA%\NovelShelf\books
```

仓库的 `.gitignore` 也额外排除了 `books/`、`novels/`、`local-books/`、`*.txt`、`*.epub` 等常见本地书籍路径和文件类型，避免误提交小说文件。

## 运行要求

- Windows
- .NET 8 SDK

安装 SDK 后在仓库目录执行：

```powershell
dotnet run
```

