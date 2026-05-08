# NovelShelf

NovelShelf 是一个个人使用的 Windows WPF 本地小说阅读器。

## 当前功能

- 导入本地 `.txt` 小说
- 将小说副本持久化保存到 `%LOCALAPPDATA%\NovelShelf\books`
- 使用 `%LOCALAPPDATA%\NovelShelf\library.json` 保存书库和阅读进度
- 使用 `%LOCALAPPDATA%\NovelShelf\settings.json` 保存字号和主题
- 支持 UTF-8、UTF-16、GB18030/GBK 常见中文文本编码
- 自动识别常见章节标题，并提供章节列表、上一章、下一章
- 支持书内关键词搜索、向前/向后查找
- 支持浅墨、深夜、护眼、复古四种低饱和阅读模式
- 支持字号调节
- 阅读界面采用居中纸页式布局，书架、目录、阅读设置以侧边抽屉和独立面板呈现
- 主界面提供“我的书架”，支持书封网格和列表两种本地书籍展示方式
- 默认阅读态只保留顶部章节、底部进度和时间；点击正文可唤起半透明控制层
- 自动保存当前可见阅读位置
- 从书库移除时只删除应用数据目录里的副本，不删除原始文件

## 隐私和 GitHub

小说正文不会保存到仓库目录。应用导入文件后，会复制到 Windows 用户本机目录：

```text
%LOCALAPPDATA%\NovelShelf\books
```

仓库的 `.gitignore` 额外排除了 `books/`、`novels/`、`local-books/`、`*.txt`、`*.epub` 等常见本地书籍路径和文件类型，避免误提交小说文件。

## 运行要求

- Windows
- .NET 8 SDK

在仓库目录执行：

```powershell
dotnet run
```

## 后续计划

- EPUB 导入
- 书签和批注
- 章节索引缓存
- 更细的阅读排版设置，例如行距、段距、页边距
