# Project Focus Manager

这是使用AI进行项目编程的试手作品，本项目全部使用AI完成，项目的目标是提升专注力。

一个完全本地运行的 Windows 专注管理工具。以「项目」为中心正向计时，记录学习进度和随手想法；
切到不该用的软件时只提醒，不强制、不锁屏、不打断。

- 不是番茄钟：没有 25 分钟倒计时，没有强制休息，计时一直往上走。
- 不用账号、不联网（除了你自己打开的内置 ChatGPT 网页）、不上传任何数据。
- C# + WPF + .NET 10 + SQLite，原生桌面程序，不用 Electron。

> 面向使用者的说明在 [docs/使用说明.md](docs/使用说明.md)。
> 想知道「做到哪一步了、还差什么、有什么坑」看 [docs/PROJECT_STATUS.md](docs/PROJECT_STATUS.md)。

---

## 现在能做什么

| 功能           | 说明                                                                                            |
| -------------- | ----------------------------------------------------------------------------------------------- |
| 项目管理       | 新建、重命名、归档、删除（删除前告知会连带删掉多少条记录，二次确认）                            |
| 正向计时       | `Stopwatch` 单调时钟，改系统时间也不影响；有效时间 = 各正常工作段之和                           |
| 暂停           | 必须先填原因（喝水 / 临时有事 / 查找资料 / 其他）；暂停时间单独统计，不计入专注                 |
| 暂停提醒       | 暂停满 5/10/15 分钟（可关）弹一次「该回来了」，不自动继续、不自动结束                           |
| 未授权应用提醒 | 切到本轮不允许的软件时提醒一次并置顶；「返回项目」继续计时，「确认切换」后再选暂停或结束        |
| 全屏延后       | 真全屏（不是最大化）播视频时不弹提醒，退出全屏后补上                                            |
| 应用管理       | 预置 Obsidian / 百度网盘 / CCtalk；可从 `.exe` 或正在运行的程序添加，可停用、删除、检测路径失效 |
| 随手记         | 专注页可直接记一句，自动关联当前会话；独立页面可筛选、标记完成、删除                            |
| 结束总结       | 必填「本轮完成了什么」，显示有效时间 / 暂停时间 / 暂停次数 / 想法数                             |
| 项目记录       | 累计时长、按天统计、每一轮的进度与心得（文字可选中复制）                                        |
| 系统托盘       | 图标区分空闲 / 专注 / 暂停，悬停看项目和本轮时间，菜单可暂停、结束、退出                        |
| 单实例         | 全机器只跑一个；重复启动会把已有窗口叫到前面并提示                                              |
| 关闭行为       | 关闭时弹自定义窗口选「最小化到托盘」或「退出」，可记住选择，也能在设置里改                      |
| 浅色 / 深色    | 两套完整配色，选择会记住；深色连标题栏一起变深                                                  |
| 内置 ChatGPT   | 网页版 chatgpt.com 内嵌，默认不加载，点开才启动内核，离开页面就释放                             |

界面设计规范见 [docs/ui/README.md](docs/ui/README.md)。

---

## 技术栈

| 层          | 用了什么                                                         |
| ----------- | ---------------------------------------------------------------- |
| 语言 / 框架 | C#（`LangVersion=latest`）、.NET 10（`net10.0-windows`）、WPF    |
| 数据        | SQLite（`Microsoft.Data.Sqlite`），单文件 `focus.db`，WAL 模式   |
| 系统交互    | Win32 P/Invoke：前台窗口事件钩子、全屏判定、窗口激活、深色标题栏 |
| 托盘        | `H.NotifyIcon.Wpf`                                               |
| 内置网页    | `Microsoft.Web.WebView2`（内核由系统的 WebView2 运行时提供）     |
| 测试        | xUnit（178 项，覆盖 Core 全部业务逻辑）                          |

---

## 目录结构

```
FocusManager.slnx              解决方案（注意是 .slnx，不是 .sln）
Directory.Build.props          全局编译选项（LangVersion / Nullable / 版本号）
build/
  env.sh                       开发环境变量（SDK 路径、NuGet 缓存、TEMP）
  install-dotnet-sdk.ps1       把 .NET SDK 装到 .tools/dotnet（用户级，不改 PATH）
src/
  FocusManager.Core/           纯业务逻辑，不依赖 WPF
    Models/  Data/  Services/  Time/
  FocusManager.App/            界面
    Views/  ViewModels/  Services/  Native/  Themes/  Resources/
tests/
  FocusManager.Tests/          xUnit 测试
docs/
  PROJECT_STATUS.md            交接文档：做到哪、怎么做的、还差什么
  使用说明.md                  给使用者的操作说明
  内存测试结果.md              五态实测数字与测量方法
  ui/                          界面规范 + 各阶段截图
```

`FocusManager.Core` 里没有任何 WPF 引用，界面层通过服务调用它 —— 所以计时、数据库、统计这些逻辑可以脱离界面单独测试。

---

## 编译与运行

前置：Windows 10/11 x64、.NET SDK 10、WebView2 运行时（Windows 11 一般自带）。

```bash
# 1) 准备环境变量（SDK 在项目内 .tools/dotnet 时用这个脚本）
cd /d/focustool && source build/env.sh

# 2) 编译
dotnet build FocusManager.slnx -c Debug

# 3) 跑测试
dotnet test FocusManager.slnx -c Debug

# 4) 启动（改界面之前先关掉旧实例，否则会报文件被占用）
powershell -NoProfile -Command "Get-Process -Name ProjectFocusManager -ErrorAction SilentlyContinue | Stop-Process -Force"
cmd //c start "" "src\FocusManager.App\bin\Debug\net10.0-windows\ProjectFocusManager.exe"
```

几个环境上的坑（换机器时容易踩到，详见 [PROJECT_STATUS.md](docs/PROJECT_STATUS.md) 第 7 节）：

- 解决方案文件名是 **`FocusManager.slnx`**，`dotnet build FocusManager.sln` 会找不到。
- 若 `%TEMP%` 或用户目录不可写，必须用 `build/env.sh` 把 `TEMP` / `NUGET_PACKAGES` / `DOTNET_CLI_HOME` 指到项目内。
- 用 PowerShell 5.1 写中文脚本要存成 **UTF-8 with BOM**，否则中文会变乱码。
- 在 bash 里启动 GUI 程序要用 `cmd //c start ""`，直接 `Start-Process` 会让命令挂住不返回。

---

## 数据保存在哪里

一个 SQLite 文件 `focus.db` 加上一个 `backups` 子目录，程序按顺序尝试：

1. `%LOCALAPPDATA%\ProjectFocusManager` ← **正式版目标位置**
2. `%APPDATA%\ProjectFocusManager`
3. 程序所在目录下的 `data\`（开发时因为本机 ACL 限制会落在这里）

设置页会显示**当前真实路径**，并提供「打开这个文件夹」和「立即备份一份」两个按钮。

数据库共 8 张表：`Projects`、`Applications`、`FocusSessions`、`PauseRecords`、`FocusIntervals`、
`IdeaNotes`、`AppSettings`、`SessionCheckpoints`；结构版本记录在 `PRAGMA user_version`，
升级前会自动备份到 `backups\`。时间统一以 UTC 存储，界面显示时转本地时间。

### 备份与恢复

- 备份：设置页点「立即备份一份」，或直接把整个数据目录复制走（**先退出程序**，否则 WAL 文件可能没落盘）。
- 恢复：退出程序 → 用备份的 `focus.db` 覆盖数据目录里的同名文件 → 重新打开。
- 程序每 15 秒写一次检查点；意外断电/强杀后再次启动会弹「上次的专注没有正常结束」，让你选继续或结束，
  关机期间的时间**不会**被算进专注时间。

---

## 测试

```bash
dotnet test FocusManager.slnx -c Debug     # 220 项，约 1 秒
```

覆盖的是 Core 里的业务逻辑：计时（含改系统时间、冻结、心跳分段）、会话（暂停/恢复/异常退出恢复）、
项目（含删除级联与守卫）、统计、应用管理、随手记、提醒状态机、暂停提醒规则、数据库迁移与备份、时间格式化。
界面行为目前靠脚本实测（`.tools/screens/*.ps1`，不进仓库），没有 UI 自动化测试。

---

## 已知限制

1. **托盘图标在 Windows 11 上会被收进「隐藏的图标」溢出区**，程序本身注册是成功的（启动日志有「系统托盘已就绪」），
   但无法用自动化点击验证右键菜单，需要人工点开 `^` 确认。
2. **Debug 构建主进程占用 147–162 MB**，高于最初的设计设想；Release 基线还没测（见 [内存测试结果.md](docs/内存测试结果.md)）。
3. **安装包只有 x64**：`dist\Installer-traditional\ProjectFocusManager-1.1.0-Setup.exe`（NSIS 向导式，可自选安装路径）与 `dist\win-x64\`（自包含目录）。没有 ARM64 包，也没有自动更新。
4. 高 DPI、多显示器尚未实测。
5. 界面层没有自动化测试：窗口、托盘、前台监听这些行为靠脚本实测，仓库里没有 UI 自动化用例。

---

## 第三方组件

| 组件                                                                                    | 版本                            | 用途               | 许可                   |
| --------------------------------------------------------------------------------------- | ------------------------------- | ------------------ | ---------------------- |
| `Microsoft.Data.Sqlite`                                                                 | 10.0.14                         | SQLite 访问        | MIT                    |
| `H.NotifyIcon.Wpf`                                                                      | 2.4.1                           | 系统托盘图标与菜单 | MIT                    |
| `Microsoft.Web.WebView2`                                                                | 1.0.4314.35                     | 内置 ChatGPT 页面  | 微软官方包（可再分发） |
| `xunit` / `xunit.runner.visualstudio` / `Microsoft.NET.Test.Sdk` / `coverlet.collector` | 2.9.3 / 3.1.4 / 17.14.1 / 6.0.4 | 测试               | Apache-2.0 / MIT       |

界面图标全部是 `Themes/Icons.xaml` 里手写的 `PathGeometry`，没有引入图标字体或图标包。
`THIRD_PARTY_NOTICES.md` 会在打包阶段补全（含参考过的开源项目版权说明）。

---

## 隐私

- 所有项目、专注记录、想法都只存在本机的 SQLite 文件里，程序没有任何上传逻辑、没有账号系统、不调用 AI API。
- 不读取键盘输入、不截屏、不抓取网页内容。
- 唯一的联网行为是：你自己点开「内置 ChatGPT」时加载 chatgpt.com，以及打开该网页后它自己的正常请求。
  程序不读取、不保存你的对话内容；登录状态由 WebView2 保存在本机数据目录里。
