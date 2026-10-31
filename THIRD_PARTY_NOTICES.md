# THIRD_PARTY_NOTICES — 第三方依赖与许可声明

本项目（Project Focus Manager）在开发过程中使用了以下第三方组件。各组件的版权归其各自所有者，本项目按其许可条款使用。

## 运行时依赖（随程序分发）

| 组件 | 版本 | 许可 | 用途 | 来源 |
|---|---|---|---|---|
| Microsoft.Data.Sqlite | 10.0.14 | MIT | SQLite 数据库访问 | nuget.org |
| Microsoft.Web.WebView2 | 1.0.4314.35 | MIT | 内置 ChatGPT 页面的浏览器控件 | nuget.org |
| H.NotifyIcon.Wpf | 2.4.1 | MIT | 系统托盘图标 | nuget.org |
| Velopack | 1.2.161 | MIT | 安装包与自动更新基础设施 | nuget.org |

补充说明：

- **SQLite 引擎**本身属公有领域（Public Domain），SQLite 见 https://www.sqlite.org/copyright.html
- **WebView2 浏览器内核**（Evergreen Runtime）由微软随系统分发，不在本安装包内。
- 本项目界面样式、图标、业务代码均为手写，**未引入任何 UI 框架或图标库**。

## 开发/测试依赖（不随程序分发）

| 组件 | 版本 | 许可 |
|---|---|---|
| xunit / xunit.runner.visualstudio | 2.9.3 / 3.1.4 | Apache-2.0 |
| Microsoft.NET.Test.Sdk | 17.14.1 | MIT |
| coverlet.collector | 6.0.4 | MIT |

## 运行时基础

- 程序基于 **.NET 10**（WPF），自包含发布时随安装包携带微软官方运行时，适用 [.NET 许可](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT)。

## 调研阶段参考过的开源项目

项目初期（阶段 0）为做技术选型，阅读过 4 个 GitHub 上开源专注/时间管理类项目的源码，**仅学习其思路与踩坑经验，未复制任何代码**。

## 许可证全文

各组件的 MIT / Apache-2.0 许可证全文可在其官方仓库查阅：

- MIT：https://opensource.org/licenses/MIT
- Apache-2.0：https://www.apache.org/licenses/LICENSE-2.0
