# S1 骨架与三栏界面 · 执行记录

日期：2026-11-11
状态：已完成
对应计划：《Facebook自动化_仿制实施计划》S1（P05）

## 一、交付物

| 层 | 文件 | 作用 |
| :-- | :-- | :-- |
| Core | `Models/Enums.cs` | 任务类型 / 通道 / 状态（含中文显示名） |
| Core | `Models/TaskTypeCatalog.cs` | 左栏五个任务类型的固定清单，文案与原版一致 |
| Core | `Models/AutomatedTask.cs` | 任务模型 |
| Core | `Configuration/AppConfig.cs` | 配置模型，默认值取原版实测（并发 1、间隔 3 秒） |
| Core | `Abstractions/IAppLogger.cs` `IConfigService.cs` `IMachineCodeProvider.cs` | 契约 |
| Core | `Licensing/LicenseContracts.cs` | 授权状态与结果类型 |
| Infrastructure | `Logging/FileAppLogger.cs` | 按天落盘，进程内加锁 |
| Infrastructure | `Configuration/JsonConfigService.cs` | config.json 读写，损坏回退默认值 |
| Infrastructure | `System/MachineCodeProvider.cs` | MachineGuid → GUID 形式机器码 |
| Infrastructure | `Licensing/PendingLicenseService.cs` | 占位实现，S3 替换 |
| Presentation | `Styles/Theme.xaml` | 配色与控件模板，无第三方主题库 |
| Presentation | `Views/MainWindow.xaml` | 三栏 240 / 400 / 520 |
| Presentation | `Views/LoginWindow.xaml` | 540 × 580 激活窗 |
| Presentation | `ViewModels/MainViewModel.cs` `LoginViewModel.cs` `TaskItemViewModel.cs` | MVVM Toolkit |
| Presentation | `App.xaml.cs` | 组合根：日志 → 配置 → 授权判定 → 开窗 |
| tools | `tools/uia/*.py` | UIA 控件树采集，用于验收取证 |

## 二、验收结果（逐条实测，不是推断）

| 计划验收项 | 结果 | 证据 |
| :-- | :-- | :-- |
| ① 启动出窗口，三栏比例一致，空态显示指定文案 | 通过 | 窗口 1176×752；列头 状态/任务/浏览器/结果；左栏五个任务类型按钮；空态文本「暂无任务，请从左侧添加」可见（`docs/plans/artifacts/S1-uia-main-20261111.txt`） |
| ② 日志出现「应用程序启动」 | 通过 | `Logs/App_20261111.log` 首行 `[INF] 应用程序启动：v0.1.0.0，机器 DESKTOP-K69Q6J5` |
| ③ 改 config.json 并发数重启后生效 | 通过 | 手工改 3 后重启，日志 `配置已加载：并发数=3 启动间隔=3 秒` |
| ④ 拿不到租约时窗口不崩、提示可读 | 通过 | 关掉跳过激活开关后停在 540×580 激活窗；空卡密点「登录激活」显示「请输入卡密」，进程存活 |

## 三、与界面还原版（方案 P13）的三处差异

1. **配色改为中性浅色**。P13 是深色背景的幻灯片，深色是版面观感，不是实测；原版主窗口的真实配色没有证据。因此配色集中在 `Theme.xaml` 一处，拿到截图后只改调色板即可全量生效。
2. **空态提示放在中栏队列区**。P13 把它画在左栏底部，但文案是「请从左侧添加」，语义上必然出现在队列区，按语义实现。
3. **状态枚举命名为 `TaskRunStatus`**。原名 `TaskStatus` 与 .NET 自带的 `System.Threading.Tasks.TaskStatus` 冲突，编译报 CS0104。改名后语义不变。

## 四、过程中踩到的坑（写进经验，避免重复）

- XAML 注释里不能出现连续 `--`（`<!-- ---- 视频上传 ---- -->` 会让 MSBuild 报 MC3000），装饰线一律用 `=`。
- WPF 项目的隐式 using 不含 `System.IO`，`Path` 需显式 `using System.IO;`。
- `DataTrigger` 比枚举值用 `{x:Static}` 更可靠，别依赖字符串自动转换。
- 界面程序由工具会话拉起时，命令结束会被回收；验收脚本与启动必须放在同一条命令里跑。
- 本机只有 .NET 运行时，`apphost` 找不到运行时会**静默退出**，必须设 `DOTNET_ROOT`（`build/run.cmd` 已内置）。

## 五、S1 明确没做的事

- 没有接浏览器（S2）：顶栏浏览器下拉固定显示「正在加载列表...」，按钮点了没有行为，不假装已就绪。
- 没有接执行链路（S4/S5/S7）：开始/停止任务只写日志。
- 没有接真实授权（S3）：`SkipActivationInDevelopment=true` 期间直接进主界面，发布前必须置 false。
