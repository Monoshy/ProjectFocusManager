# 界面设计规范与改造前后对照

这份文档记录 Project Focus Manager 界面现代化改造后的设计规范，以及改造前后的截图对照。
改界面之前先读这里，避免随手加一套新的颜色或字号。

- 改造前基线：Git 标签 `ui-before-modernization`（提交 399a80e）
- 改造后：提交 211e16b 起
- 想整体回退界面：`git reset --hard ui-before-modernization`

---

## 一、颜色

所有颜色都定义在两个主题文件里，**界面代码只能引用资源名，不许写死颜色值**：

- `src/FocusManager.App/Themes/Light.xaml` —— 浅色主题
- `src/FocusManager.App/Themes/Dark.xaml` —— 深色主题

两个文件必须拥有完全相同的资源名，运行时由 `ThemeManager.Apply()` 整本替换。

| 资源名 | 浅色 | 深色 | 用途 |
|---|---|---|---|
| `WindowBackgroundBrush` | `#F6F7F9` | `#16171A` | 内容区页面底色（最底层） |
| `SurfaceBrush` | `#FFFFFF` | `#1E1F23` | 卡片、对话框底色（比页面亮） |
| `SurfaceAltBrush` | `#EFF1F4` | `#26272C` | 次级块：心得块、统计块、列表悬停 |
| `SurfaceSunkenBrush` | `#EAECF0` | `#121316` | 凹陷区域（备用） |
| `NavRailBrush` | `#FFFFFF` | `#1A1B1F` | 左侧导航栏底色 |
| `DividerBrush` / `LineBrush` | `#E5E7EB` | `#2C2E34` | 1px 分隔线 |
| `ControlBorderBrush` | `#DFE2E7` | `#33353C` | 输入框、下拉框边框 |
| `TextPrimaryBrush` | `#1B1D21` | `#ECEDEF` | 正文、标题 |
| `TextSecondaryBrush` | `#5A6069` | `#A8ADB6` | 说明文字 |
| `TextMutedBrush` | `#8A9099` | `#7C828C` | 弱化文字、占位提示 |
| `AccentBrush` | `#4C63D2` | `#8B9DF5` | 主强调：主按钮、选中态、累计时长数字 |
| `AccentHoverBrush` | `#3F55BD` | `#A0B0FF` | 主按钮悬停 |
| `AccentPressedBrush` | `#364AA8` | `#7A8CE8` | 主按钮按下 |
| `AccentSoftBrush` | `#EEF0FC` | `#232842` | 选中项底色、单选项选中底色 |
| `AccentTextBrush` | `#FFFFFF` | `#14161B` | 强调色底上的文字（深色主题要用深字） |
| `FocusBrush` / `SuccessBrush` | `#2E9E6B` | `#5FD3A0` | 专注中状态、保存成功 |
| `FocusSoftBrush` | `#E8F6EF` | `#16302A` | 专注状态胶囊底色 |
| `PauseBrush` | `#C77B2B` | `#E8A657` | 暂停状态、警告 |
| `PauseSoftBrush` | `#FBF1E4` | `#33270F` | 暂停状态胶囊底色 |
| `DangerBrush` | `#D14343` | `#F08A8A` | 结束专注、错误 |
| `DangerSoftBrush` | `#FBEAEA` | `#3A1F1F` | 错误底色（备用） |
| `HoverOverlayBrush` | `#08000000` | `#14FFFFFF` | 按钮悬停遮罩（半透明） |
| `PressedOverlayBrush` | `#14000000` | `#24FFFFFF` | 按钮按下遮罩 |

配色原则：

1. 卡片永远比页面底色**亮**（深色主题也一样），靠亮度差分层，不靠粗边框。
2. 强调色只用一种（低饱和靛蓝），绿色/橙色/红色只表示状态，不做装饰。
3. 深色主题不是把浅色反相，而是重新定的一套层级：页面更深、卡片稍亮、文字降亮度。

---

## 二、字体

| 资源名 | 值 | 用途 |
|---|---|---|
| `UiFontFamily` | `Microsoft YaHei UI, Segoe UI` | 全部界面文字 |
| `TimerFontFamily` | `Consolas, Microsoft YaHei UI` | 计时数字、统计数字（等宽，数字跳动不抖） |

字号令牌在 `Themes/Tokens.xaml`：

| 资源名 | 值 | 用途 |
|---|---|---|
| `FontSizePageTitle` | 26 | 页面标题 |
| `FontSizeSection` | 15 | 卡片标题、分组标题 |
| `FontSizeBody` | 14 | 正文、按钮文字 |
| `FontSizeCaption` | 13 | 辅助说明 |
| `FontSizeMuted` | 12 | 最弱化的一层说明 |
| `FontSizeTimer` | 72 | 专注计时数字（唯一视觉中心） |
| `FontSizeBigNumber` | 24 | 累计时长等大数字 |

层级原则：一屏之内只允许**一个** 72px 的数字；26px 只用于页面标题；正文一律 14px。

---

## 三、间距与圆角

| 资源名 | 值 | 用途 |
|---|---|---|
| `PagePadding` | 32,28,32,32 | 页面内容外边距 |
| `CardPadding` | 20 | 卡片内边距 |
| `DialogPadding` | 24 | 对话框内边距 |
| `RadiusControl` | 8 | 按钮、输入框、下拉框、列表项 |
| `RadiusCard` | 12 | 卡片、对话框 |
| `RadiusLarge` | 16 | 大容器（备用） |
| `RadiusPill` | 999 | 状态胶囊 |
| `ControlHeight` | 36 | 普通按钮 / 输入框 / 下拉框高度 |
| `BigButtonHeight` | 44 | 「开始专注」这类主操作 |
| `NavRailWidth` | 200 | 左侧导航栏宽度 |
| `ContentMaxWidth` | 760 | 内容区最大宽度（超宽窗口时居中，不拉伸） |

间距刻度只用 **8 / 12 / 16 / 24 / 32**。不要出现 13、17、21 这种随手写的值。

---

## 四、图标

`src/FocusManager.App/Themes/Icons.xaml` 里全是 `PathGeometry`（24×24 画布），
用 `<Path Data="{StaticResource IconXxx}" Stroke="..." Width="18" Height="18" Stretch="Uniform"/>` 使用。

为什么不用图标字体：`Segoe Fluent Icons` 只有 Windows 11 自带，Windows 10 会显示成方块。
为什么不用图标包：为了十来个图标引入一个依赖不值得。

现有图标：`IconFocus` `IconProjects` `IconSettings` `IconIdea` `IconChat` `IconPlus`
`IconCheck` `IconChevronDown` `IconPause` `IconStop` `IconPlay` `IconFolder` `IconBackup`
`IconSun` `IconMoon`。

---

## 五、交互与性能约定

1. **不加动画**。悬停/按下反馈用 `HoverOverlayBrush` / `PressedOverlayBrush` 半透明遮罩切换。
2. **不用阴影和模糊**（`DropShadowEffect` / `BlurEffect` / Acrylic），避免渲染开销。
3. **不用透明窗口**。
4. 页面切换是"三个 View 常驻，只换 `ContentControl.Content`"，不重新创建页面、不重新初始化服务。
5. 主窗口隐藏（最小化）时计时刷新定时器会停掉，计时本身不受影响。
6. 新增控件样式一律加在 `Themes/Controls.xaml`，不要在页面里写内联 `Style`。

---

---

## 六、界面截图

> 应所有者要求，界面截图已于 2026-11-01 从仓库移除（其中包含真实学习项目名，属于个人数据）。
> 本文件保留完整的设计规范；截图证据保留在本机 `.tools\` 目录下，不随仓库分发。
