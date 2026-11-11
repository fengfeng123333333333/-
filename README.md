# FbAuto

Facebook 多账号自动化桌面客户端的重写实现。功能与交互对齐原版工具，代码全部重写。

> 依据文档：《Facebook自动化工具仿制技术方案》(v0.4) 与《Facebook自动化_仿制实施计划》(15 页)。
> 本仓库只做功能等效重写，**不含**原版任何代码、资源素材或授权逻辑。

## 分层

| 层 | 项目 | 职责 |
| :-- | :-- | :-- |
| 界面层 Presentation | `FbAuto.Presentation` | WPF 三栏主窗 + 激活窗，ViewModel 命令 |
| 编排层 Application | `FbAuto.Application` | 任务队列、并发控制、暂停恢复、定时启动、结果回写 |
| 自动化层 Automation | `FbAuto.Automation` | Playwright 封装：定位等待、文件上传、导航容错、PageObject 基类 |
| 适配层 Browser Adapter | `FbAuto.Browser` | `IBrowserChannel` 六方法；Chrome 调试端口 / 比特浏览器本地 HTTP |
| 基础设施 Infrastructure | `FbAuto.Infrastructure` | 配置读写、日志、授权校验、SQLite 任务表、Excel 导入导出 |
| 契约 Core | `FbAuto.Core` | 跨层模型、接口、枚举，无外部依赖 |

依赖方向单向向下：Presentation → Application → Automation → Browser → Infrastructure → Core。
`Browser` 只暴露接口，不向上泄漏 Playwright 类型。

## 固定依赖（版本在 Directory.Packages.props 统一管理）

| 包 | 版本 | 用途 |
| :-- | :-- | :-- |
| Microsoft.Playwright | 1.41.2 | 浏览器控制（与原版同版本，便于行为对照） |
| CommunityToolkit.Mvvm | 8.4.2 | MVVM（原版同款） |
| praeclarum/sqlite-net | 最新稳定 | 任务表持久化 |
| ClosedXML | 最新稳定 | Excel 任务表读写 |
| xunit | 最新稳定 | 单元测试 |

反检测备选 `patchright-dotnet` **不入依赖**，仅在出现可复现的自动化特征检测时再评估（见 P14 标注）。

## 构建

本项目不污染系统 .NET 安装，SDK 解压在仓库外的 `.dotnet/` 目录。

```bat
build\dotnet.cmd --version          :: 确认 10.0.404
build\dotnet.cmd build FbAuto.slnx
build\run.cmd                       :: 启动调试版（已内置 DOTNET_ROOT）
```

`build\dotnet.cmd` 会把本地 SDK 临时加入 PATH 后调用 dotnet，等价于：

```bat
set PATH=D:\AI\workbuddy\短视频工具\.dotnet;%PATH%
set DOTNET_ROOT=D:\AI\workbuddy\短视频工具\.dotnet
```

## 当前进度

| 步骤 | 状态 | 说明 |
| :-- | :-- | :-- |
| S0 准备 | 完成 | 六层骨架、证据归档、SDK 本地化 |
| S1 骨架与三栏 | 完成 | 三栏 240/400/520、激活窗 540×580、日志与配置落盘，四项验收已实测 |
| S2 双通道适配器 | 未开始 | 依赖外网出口决策（见 docs/plans/S1-SKELETON-LOG.md） |
| S3–S10 | 未开始 | 见实施计划 |

> 已知前置条件：Facebook 在境内网络不可达（DNS 正常、TCP 443 超时）。S4 起的页面验证需先确定执行节点位置（境外云主机或跨境专线）。

## 不可违反的约定

1. **仓库内不得出现任何 Cookie、登录态、账号密码文件**。Profile 一律放在仓库外，由 `config.local.json` 指向。
2. **不按进程名杀 chrome.exe**。关闭浏览器只走 `Browser.Close()`，避免误伤用户自己开的窗口。
3. **任务级失败不自动重跑，元素级等待保留轮询重试**。两者不能混为一谈。
4. **所有失败必须留证据**：截图 + 当前 URL + 日志，三者缺一不可。
5. 界面文案、字段名、默认值与还原版一致（并发默认 1、启动间隔 3 秒）；图标自绘，不搬原版素材。

## 目录

```
FbAuto/
├─ src/            六个分层项目
├─ tests/          单元测试
├─ build/          构建脚本
└─ docs/
   ├─ plans/       实施计划与验收记录
   └─ evidence/    原版分析证据（只读，仅供内部对照）
```
