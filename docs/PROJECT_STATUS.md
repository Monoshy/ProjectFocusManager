# Project Focus Manager — 项目状态与交接文档

> 这份文档的唯一目的：**换一个新对话、换一台机器、或者过三个月再回来看，都能立刻知道这个项目做到哪了、为什么这么做、还剩什么。**
>
> 最后更新：2026-11-01（收尾 + 打包完成，v1.0.0 交付）

---

## 1. 一句话目标与当前状态

**目标**：一个完全本地、低内存、不打扰学习的 Windows 桌面专注管理工具。以「项目」为中心正向计时，记录学习进度和随手想法，切到不该用的软件时只提醒、不强制。

**当前状态**：六个阶段全部完成并交付。181 项测试全过，收尾验证（备份恢复、迁移演练、内存、高 DPI）做完，Velopack 用户级安装包与自包含发布产物已产出。

| 项目 | 现状 |
|---|---|
| 最新提交 | `git log -1` 查询 |
| 提交总数 | `git rev-list --count HEAD` |
| 自动化测试 | **181 项，全部通过**（约 1 秒） |
| 代码规模 | Core 2,987 行 + App 7,558 行 + 测试约 2,100 行 ≈ 12,700 行 |
| 发布产物 | `dist/Installer/ProjectFocusManager-win-Setup.exe`（用户级安装包）、`dist/Installer/ProjectFocusManager-win-Portable.zip`（免安装）、`dist/win-x64/`（自包含目录，不进仓库） |
| 数据位置 | 正式版：`%LOCALAPPDATA%\ProjectFocusManager\data\focus.db`；三级回退见 `docs/数据库说明.md` |

**最短上手路径**（Windows，已有 .NET SDK 的情况）：

```bash
cd /d/focustool
source build/env.sh          # 必须！它把 SDK、TEMP、NuGet 缓存都指到项目内
dotnet build FocusManager.slnx -c Debug
dotnet test  FocusManager.slnx -c Debug
```

---

## 2. 架构总览

三层 + 一个系统层，**依赖方向单向**：界面 → 服务 → 数据 → Windows API。Core 完全不认识 WPF，所以能脱离界面做单元测试。

```
界面层  FocusManager.App (WPF)
          Views / ViewModels / Themes
              ↓ 只调用服务
业务层  FocusManager.Core (纯 C#，无界面依赖)
          Services / Models / Time
              ↓ 只通过 DatabaseService
数据层  Microsoft.Data.Sqlite + Migrations
              ↓
系统层  Native/ (P/Invoke: 前台窗口、全屏、托盘、DWM)
```

```
D:\focustool\
├─ AGENTS.md                 协作规则（只有 2 条：每次改动建 commit、每次改动写测试）
├─ Directory.Build.props     统一属性（net10 语言版本、可空引用、版本号、zh-CN）
├─ FocusManager.slnx         解决方案（.NET 10 新格式，**不是 .sln**）
├─ build\
│   ├─ env.sh                环境变量（SDK 路径 / TEMP / NuGet 缓存），每次命令前 source
│   ├─ install-dotnet-sdk.ps1 包装微软官方 dotnet-install.ps1
│   └─ dotnet-install.ps1    官方脚本（不要手改）
├─ docs\
│   ├─ PROJECT_STATUS.md     本文件
│   ├─ 使用说明.md            面向使用者
│   └─ ui\                   界面设计规范 + 各阶段截图
├─ src\
│   ├─ FocusManager.Core\
│   │   ├─ Models\           9 个数据模型
│   │   ├─ Data\             DatabaseService（连接/迁移/备份）、Migrations、SqliteValue、扩展方法
│   │   ├─ Services\         10 个服务（见下表）
│   │   └─ Time\             ITimeSource / SystemTimeSource / TimeFormatter
│   └─ FocusManager.App\
│       ├─ App.xaml(.cs)     启动流程、单实例、未结束会话恢复、全局异常日志
│       ├─ MainWindow       左侧导航壳 + 托盘接线 + 关闭行为
│       ├─ Views\           4 个页面 + 10 个对话框
│       ├─ ViewModels\      FocusViewModel / ProjectsViewModel / ApplicationsViewModel
│       ├─ Services\        7 个 App 层服务（见下表）
│       ├─ Native\          WindowsApi（P/Invoke）、FullscreenDetector、ForegroundHelper、DwmHelper
│       └─ Themes\          Tokens / Light / Dark / Icons / Controls
└─ tests\FocusManager.Tests\ 12 个测试文件 + FakeTimeSource / TempDatabase
```

### Core 服务（业务逻辑都在这里）

| 服务 | 职责 |
|---|---|
| `FocusTimerService` | 计时核心。Stopwatch 单调时钟，分段记录，冻结/解冻，心跳落库。**不依赖数据库和界面** |
| `SessionService` | 会话生命周期：开始、暂停、恢复、结束、异常恢复、心跳检查点 |
| `ProjectService` | 项目增删改查、累计时间重算、删除影响评估与级联删除 |
| `StatisticsService` | 按项目统计、按天统计、会话列表 |
| `ApplicationService` | 学习应用登记（预置三条、从文件添加、从运行窗口添加、停用、删除） |
| `SettingsService` | AppSettings 键值对读写 |
| `FocusAllowList` | 判断某个进程名在本轮是否被允许（纯字符串逻辑） |
| `FocusGuardPolicy` | 提醒状态机（Normal / UnauthorizedDetected / ReminderShown / UserConfirmed） |
| `PauseReminderRule` | 暂停提醒阈值解析（5/10/15 分钟 / 不提醒） |
| `CloseBehaviorRule` | 关闭窗口行为解析（每次询问 / 最小化到托盘 / 退出） |

### App 层服务（依赖 Windows 或界面）

| 服务 | 职责 |
|---|---|
| `ActiveWindowMonitor` | `SetWinEventHook` 事件驱动监听前台窗口 + 5 秒低成本兜底 |
| `FocusGuardService` | 把窗口事件接到 `FocusGuardPolicy`，发出「离开了 / 回来了」事件 |
| `PauseReminderService` | 暂停满阈值时触发一次提醒 |
| `TrayService` | 系统托盘图标（三态）、悬停文字、右键菜单 |
| `SingleInstance` | Named Mutex + 命名事件实现单实例与窗口唤醒 |
| `ApplicationActivator` | **只激活已经在运行的学习软件，绝不启动** |
| `RunningWindowList` | 枚举当前有可见窗口的进程（供「从运行中的程序添加」用） |

---

## 3. 已完成功能对照（按原始需求的六阶段）

判断标准：**「有代码」不等于「已验证」**。下表「已验证」= 有可复现的单元测试或实测记录。

| 阶段 | 内容 | 状态 | 依据 |
|---|---|---|---|
| 0 | 环境审查、开源调研、架构设计 | 已实现且已验证 | 阶段 0 报告已交付；4 个 GitHub 项目逐个读过源码 |
| 1 | 骨架、数据库、项目管理、正向计时、暂停恢复、统计、单元测试 | 已实现且已验证 | 178 项测试；含「关机 2 小时不算专注时间」「85+110+65 分钟 = 4 小时 20 分钟」等关键用例 |
| 2 | 应用管理、前台窗口识别、白名单、未授权提醒、确认切换后暂停/结束 | 已实现，实机验证；App 层无自动化测试 | 只测了两个纯逻辑类（`FocusAllowList` 10 项、`FocusGuardPolicy` 15 项），其余靠脚本实测 |
| 3 | 随手记、专注总结、项目累计、历史记录 | 已实现且已验证 | 20 项 `IdeaNoteService` 测试；实机验证记录/标记完成/删除/重启后仍在/专注中记下自动关联会话/总结窗口显示真实想法数（`0 次 / 1 条`） |
| 4 | 托盘、主窗口隐藏、暂停原因、5 分钟提醒、全屏检测 | 已实现，主体已验证 | 5 分钟提醒实测 251 秒弹出；全屏延后有日志证据；**托盘图标外观与右键菜单无法自动验证** |
| 5 | 内置 ChatGPT（WebView2） | 已实现且已验证 | 启动时 WebView2 进程数 = 0；点开后真实加载 chatgpt.com；关掉/离开页面后进程归零（见 `docs/内存测试结果.md`） |
| 6 | 全量测试、内存实测、Release 打包、中文文档 | **已完成** | 181 项测试全通过；Release 五态内存实测、高 DPI 核验、备份恢复实测、v1→v2 迁移演练（`docs/测试报告.md`、`docs/内存测试结果.md`）；Velopack 用户级安装包 + Portable + 自包含发布；`THIRD_PARTY_NOTICES.md`、架构说明、数据库说明、测试报告、学习报告全部交付 |

### 具体已实现的功能清单

**项目管理**
- 新建项目（名称 ≤200 字、可填说明）、重命名、归档
- 项目列表（按创建时间倒序）、累计专注时间（由 `FocusIntervals` 求和得出）
- 删除项目：显示影响（N 轮记录、累计时长、N 条想法）、二次确认、`running/paused` 会话时拒绝删除、显式按顺序级联清理 6 张表

**专注计时**
- 正向计时，不限时长，`Stopwatch` 单调时钟（改系统时间不影响）
- 开始 / 暂停（必填原因）/ 恢复 / 结束
- 有效时间 = 各段正常工作之和；暂停时间单独统计，不计入有效时间
- 15 秒心跳检查点（`SessionCheckpoints`），崩溃最多丢 15 秒
- 结束总结：必填「本轮完成了什么」、选填心得，显示有效时间 / 暂停时间 / 暂停次数 / 想法数
- 中途结束也保留已记录时间

**随手记**
- 独立页面：选项目、写一句、按 Ctrl+Enter 或点「记下来」保存；时间自动记录
- 专注页底部有「记一笔」入口，不用离开当前页面
- 专注中记下的想法自动关联当前会话（`SessionId`），平时记的不关联
- 可标记完成 / 标回待处理，可删除（二次确认）；按「全部 / 待处理 / 已完成」筛选
- 结束总结里的「想法」数量取本轮真实条数（`CountBySession`）

**应用管理与提醒**
- 预置 Obsidian / 百度网盘 / CCtalk（可按进程名识别，不要求路径）
- 从 `.exe` 文件添加、从正在运行的程序添加、修改、停用/启用、删除
- 路径失效检测（`HasUsablePath`）
- 本轮白名单 = 选定的主应用 + Obsidian + 内置 ChatGPT + 自身 + 必要系统窗口
- **登记过 ≠ 自动获权**
- 切到未授权应用：强制置顶、抢焦点提醒一次；「返回项目」继续计时；「确认切换」后再选暂停（填原因）或结束
- 同一段未授权只提醒一次；回到允许环境才重置
- 真全屏时提醒延后，退出全屏后补上（区分全屏与最大化）

**内置 ChatGPT**
- 左侧导航「内置 ChatGPT」是程序里的一个页面，里面就是网页版 chatgpt.com，可以直接登录使用
- 程序启动时**不**加载网页内核（实测启动态 WebView2 进程数为 0）
- 只在用户点「打开 ChatGPT」时初始化；整个程序只有一份浏览器环境
- 用户数据目录独立（`数据目录\WebView2`），登录状态保留；程序不读取、不保存对话内容
- 点「关闭网页」或离开这一页，网页被释放（实测浏览器进程归零）；释放控件不等于一定立刻回收，真实数字见 `docs/内存测试结果.md`

**暂停与托盘**
- 暂停满 5/10/15 分钟（可关）弹一次「该回来了」，非模态、不抢焦点、不自动恢复
- 「回到项目」会先激活已在运行的学习软件；**应用没开就提示用户手动打开，并保持暂停**
- 系统托盘三态图标（空闲灰 / 专注绿 / 暂停橙）、悬停显示项目名与实时时长、右键菜单（显示主界面 / 最小化到托盘 / 暂停或继续 / 结束 / 退出）
- 关闭按钮：可配置「每次询问 / 最小化到托盘 / 直接退出」，可记住选择
- 单实例：第二次启动会把已有窗口叫到前面并提示，然后自己退出

**其他**
- 浅色 / 深色主题（自绘控件样式，无第三方 UI 框架），主题偏好持久化，深色时连标题栏一起变深
- 启动时发现上次未正常结束的会话 → 恢复窗口（继续 / 按已保存内容结束），**不会把关机时间算进专注**
- 数据库版本迁移（`PRAGMA user_version`）+ 升级前自动备份 + 旧程序拒绝打开新库
- 手动备份（`VACUUM INTO`）
- 项目记录页的「进度」「心得」是只读文本框，可选中复制

---

## 4. 关键技术决策及理由（只写「为什么」）

| 决策 | 理由 |
|---|---|
| 计时用 `Stopwatch` 单调时钟，界面每 200ms 只负责显示 | 用「每秒减一」累计会漂移，且改系统时间、休眠都会算错。单调时钟只用来算时长，**绝不入库** |
| `FocusIntervals` 是唯一真相，`FocusSessions.EffectiveSeconds` 只是缓存 | 暂停、崩溃、恢复都只影响「段」；随时可以从段求和重算，不怕缓存被写坏 |
| 前台检测用 `SetWinEventHook` 事件 + 5 秒兜底 | 需求明确禁止高频轮询。事件负责实时，兜底只做极低成本的「句柄或全屏状态是否变化」比较，顺带解决「退出全屏没有前台切换事件」这个盲区 |
| 全屏判定要三个条件（铺满 `rcMonitor` 而非 `rcWork` + 无标题栏边框 + 未被 cloak） | 只看「是否最大化」会把普通最大化误判成全屏；只看 `SHQueryUserNotificationState` 会漏掉无边框播放器 |
| 提醒用状态机（4 个状态）而不是加延时 | 原来「已提醒」标记会被自己的提醒窗口抢前台触发重置，导致每 5 秒循环弹窗。状态机让「本程序自己」成为中性事件，从根上断开循环 |
| 自己进程的窗口在兜底检查里直接跳过 | `WINEVENT_SKIPOWNPROCESS` 只作用于事件回调，管不住兜底定时器读 `GetForegroundWindow()` |
| 激活学习软件绝不 `Process.Start` | 需求要求「只激活已经运行的」，自动启动会偷偷打开应用、可能动到 Obsidian 的 Vault。代码里写死了这条铁律 |
| 单实例用 Named Mutex + 命名事件（不是命名管道） | 只需要「叫醒已有实例」一个动作，事件足够；管道要处理连接、超时、协议，属过度设计。用 `Local\` 前缀限制在当前登录会话，避免多用户互相干扰 |
| 托盘用 `H.NotifyIcon.Wpf` | WPF 没有原生托盘控件；这个包只做托盘一件事，2.4.1 是当前稳定版。**必须先设 `Icon` 再 `ForceCreate()`**，否则 `TryCreate failed` |
| 数据目录三级回退（`%LOCALAPPDATA%` → `%APPDATA%` → exe 同目录） | 开发机上前两级被 ACL 拒绝，需要第三条兜底才能跑起来。**正式版以第一级为准**，打包时要确认它可用 |
| 界面全手写 Style + `ControlTemplate`，不引入 UI 框架 | 内存与启动时间是硬指标；一个 UI 框架的启动开销就超过整个程序的目标值 |
| 图标用 WPF `Path` + `PathGeometry` 自绘 | `Segoe Fluent Icons` 只在 Win11 有，Win10 会显示方块；引图标包又违背「零额外依赖」 |

---

## 5. 数据库

- 位置：`{数据目录}\focus.db`，备份在同目录 `backups\`
- 引擎：SQLite，`Microsoft.Data.Sqlite 10.0.14`，`journal_mode=WAL`、`synchronous=NORMAL`、`busy_timeout=5000`、`foreign_keys=ON`
- 版本：`PRAGMA user_version = 1`（`DatabaseService.TargetSchemaVersion`）
- **时间一律存 UTC**，格式 `yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'`；显示时才转本地时间

| 表 | 说明 |
|---|---|
| `Projects` | 项目；`TotalEffectiveSeconds` 是缓存，真实值由 `FocusIntervals` 求和 |
| `Applications` | 登记的学习应用；`ProcessName` 用于窗口识别，`ExecutablePath` 可为空（预置条目靠进程名） |
| `FocusSessions` | 一次专注；`Status` ∈ running / paused / completed / aborted / recovered |
| `FocusIntervals` | 每段有效专注（**唯一真相**） |
| `PauseRecords` | 每次暂停（原因必填，`EndedAtUtc` 为空表示仍在暂停） |
| `IdeaNotes` | 随手记想法（`Status` ∈ pending / done，`SessionId` 可空） |
| `AppSettings` | 键值对：`Theme`、`PauseReminderMinutes`、`CloseAction` |
| `SessionCheckpoints` | 15 秒心跳快照，用于崩溃后估算已专注时间（只保留最近 50 条） |

**迁移规则**：新增表结构时，`TargetSchemaVersion` 加 1、写一个 `ApplyV2`，`Initialize()` 会自动先备份再升级；数据库版本高于程序时**拒绝打开**并提示升级程序。

**备份**：`VACUUM INTO` 生成一致性快照，文件名带时间戳与标签，不覆盖同名文件。

---

## 6. 测试

```bash
source build/env.sh
dotnet test FocusManager.slnx -c Debug            # 全量，178 项
dotnet test FocusManager.slnx -c Debug --filter FocusTimerServiceTests   # 单类
```

| 测试文件 | 数量 | 覆盖内容 |
|---|---|---|
| `FocusTimerServiceTests` | 23 | 计时准确性、暂停不计时、多段、冻结、心跳、整秒四舍五入、系统时间跳变 |
| `SessionServiceTests` | 17 | 会话生命周期、异常恢复不算停机时间、累计统计 |
| `FocusGuardPolicyTests` | 15 | 提醒状态机（含自己窗口不重置、多未授权程序算同一段、确认后不再提醒） |
| `IdeaNoteServiceTests` | 20 | 随手记：内容校验、项目/会话归属校验、筛选、标记完成与撤回、删除、随项目级联删除、会话结束后仍保留 |
| `ApplicationServiceTests` | 19 | 预置应用、添加/修改/停用/删除、进程名规范化、路径失效 |
| `ProjectServiceTests` | 11 | 项目增改查、搜索通配符转义 |
| `FocusAllowListTests` | 16 | 白名单语义（登记≠获权、系统窗口、大小写与后缀） |
| `TimeFormatterTests` | 16 | 时长中文表述、时钟格式 |
| `DatabaseServiceTests` | 9 | 迁移、版本拒绝、备份不覆盖、UTC 往返、备份恢复实测、v1→v2 迁移演练 |
| `CloseBehaviorRuleTests` | 14 | 关闭行为解析 |
| `PauseReminderRuleTests` | 7 | 提醒阈值解析 |
| `ProjectDeletionTests` | 7 | 删除影响、级联清理、运行中拒绝删除 |
| `SettingsServiceTests` | 6 | 键值读写 |
| `ForegroundHelperTests` | 1 | 真实窗口最小化后 ForceActivate 回到前台（winver.exe 实测） |

合计 181 项（14 个测试类；Theory 的每个数据行单独算一项，用 `dotnet test --list-tests` 可核对）。

**测试没覆盖的（重要）**：整个 App 层（窗口、托盘、前台监听、激活应用、单实例）基本无 xUnit 用例（仅 ForegroundHelper 一项引入了 App 工程）。这些行为靠 `.tools\screens\` 下的 PowerShell + UIAutomation 脚本实测，**脚本不进仓库**（被 gitignore），换机器就没了。

---

## 7. 开发环境必读（踩过的坑）

1. **SDK 不在系统里**：`.NET SDK 10.0.403` 装在 `D:\focustool\.tools\dotnet`（被 gitignore）。所以每条命令前必须 `source build/env.sh`，否则 `dotnet` 找不到。
2. **`%TEMP%` 不可写**：`C:\Users\<用户>\AppData\Local\Temp` 被 ACL 拒绝。`env.sh` 把 `TEMP/TMP` 指到 `.tools\temp`。PowerShell 的 `Add-Type` 也依赖它，脚本里要显式设。
3. **`%LOCALAPPDATA%`、`%APPDATA%`、`D:\` 根目录都不能建目录**：所以数据落在 `bin\...\data\`，`AppPaths` 需要三级回退。
4. **解决方案文件是 `FocusManager.slnx`**，不是 `.sln`。
5. **PowerShell 5.1 读无 BOM 的 UTF-8 会把中文读坏**：所有 `.ps1` 必须存成 UTF-8 **with BOM**（`encoding='utf-8-sig'`）。
6. **bash 单条命令约 8KB 上限**：用 `cat > file <<'EOF'` 写大文件会被截断，必须用写文件工具。
7. **改界面后先停旧进程**，否则 `dotnet build` 报 `MSB3027/MSB3021`（exe 被占用）。
8. **截图不要用 `SetForegroundWindow`**：会抢用户焦点。用 `PrintWindow(hwnd, hdc, 2)`。窗口最小化时 `GetWindowRect` 会返回 160×32，要先 `ShowWindow(hwnd, 9)`。
9. **UIA 看不到本程序的模态窗口**（`ShowInTaskbar=False` 的 owned window）：要用 Win32 `EnumWindows` 按标题找 hwnd。
10. **控制台显示中文是乱码**（GBK 终端），判断中文内容要看截图或用 python 读数据库。
11. **禁 `rm` / `del`**：删文件一律 `mv` 到 `.tools\trash\`。

---

## 8. 已知问题与技术债

**已解决（历史记录）**

1. ~~连续双击 exe 叠出多个「已经开着了」提示框~~ → 已修（`e7d55fa`，提示框去重）
2. ~~暂停 5 分钟提醒「回到项目」可能被 Windows 前台限制卡死~~ → 已修（`8f12a35`：激活后轮询确认 + 最小化还原/SwitchToThisWindow 回退 + 「直接继续计时」逃生出口）
3. ~~数据目录与安装器目录冲突~~ → 已修（`1afcd9b`：数据定稿 `%LOCALAPPDATA%\ProjectFocusManager\data`，安装器占用不带 `data` 的上层目录）

**仍存在的技术债**

4. **App 层基本零自动化测试**，验证脚本不进仓库。
5. **内存基线高于原始目标**：Release 五态实测 145–170 MB（开 ChatGPT 时 WebView2 另计），放弃「启动 < 60 MB」目标，结论见 `docs/内存测试结果.md`。
6. **死代码**：`Mvvm\RelayCommand.cs` 全仓库无人使用。
7. **未验证项**：多显示器与非 100% 缩放的高 DPI、安装包在真实用户机的完整安装/升级流程（本机 `%LOCALAPPDATA%` 被 ACL 拒绝，只验证到 bundle 完整性）、百度网盘/CCtalk 的真实激活、托盘图标外观与右键菜单的人眼复核。

---

## 9. 剩余任务与推荐顺序

用户已确认的顺序：**随手记（已完成）→ ChatGPT（已完成）→ 收尾（已完成）→ 打包（已完成）**。

全部交付。若后续继续迭代，优先级建议：

| 顺序 | 任务 | 说明 |
|---|---|---|
| 1 | 真机安装验证 | 在一台 `%LOCALAPPDATA%` 正常的机器上跑 Setup.exe，验证安装/首次启动/升级流程 |
| 2 | App 层 UI 自动化 | 引入 FlaUI 之类框架，把托盘、提醒链路补进测试 |
| 3 | 更新通道 | 用 `vpk` 的 url/渠道参数配置自动更新源 |

---

## 10. 开工前需要确认的事

1. ~~数据目录~~：已确认并落地 `%LOCALAPPDATA%\ProjectFocusManager\data`（`1afcd9b`）。
2. ~~是否允许为 ChatGPT 引入 WebView2 依赖~~：已按需求第五节加上 `Microsoft.Web.WebView2`（微软官方包，浏览器内核由系统运行时提供）。
3. **App 层要不要补自动化测试**：补的话成本不小（需要 UI 测试框架），不补则界面行为长期靠手工脚本验证。

---

## 附：第三方依赖与许可

| 依赖 | 版本 | 许可 | 用途 |
|---|---|---|---|
| `Microsoft.Data.Sqlite` | 10.0.14 | MIT | SQLite 访问 |
| `Microsoft.Web.WebView2` | 1.0.4314.35 | MIT | 内置 ChatGPT 页面（浏览器内核由系统运行时提供） |
| `H.NotifyIcon.Wpf` | 2.4.1 | MIT | 系统托盘 |
| `Velopack` | 1.2.161 | MIT | 安装包与自动更新 |
| `xunit` / `xunit.runner.visualstudio` | 2.9.3 / 3.1.4 | Apache-2.0 | 单元测试 |
| `Microsoft.NET.Test.Sdk` | 17.14.1 | MIT | 测试宿主 |
| `coverlet.collector` | 6.0.4 | MIT | 覆盖率（暂未启用） |

> 完整声明见根目录 `THIRD_PARTY_NOTICES.md`。
