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

## 六、补测：3 组冒烟测试（2026-11-11 16:50）

S1 初验全靠人工 UIA 取证，测试项目里 **0 个用例**，是当时明确记下的缺口。本轮补齐，共 **18 个用例，176 ms 全绿**。

| 组 | 文件 | 用例 | 盯的是什么 |
| :-- | :-- | :-- | :-- |
| 配置 | `tests/.../JsonConfigServiceTests.cs` | 8 | 文件不存在→生成默认值；4 种损坏 JSON（半截/空串/null/类型错）→回退默认值且不抛；部分字段→其余保持默认；保存后中文不被转义；允许注释与尾逗号 |
| 日志 | `tests/.../FileAppLoggerTests.cs` | 6 | 文件名 `App_yyyyMMdd.log`；同一天追加不覆盖；毫秒级时间戳格式；异常日志带类型与堆栈；60 线程并发不残行；**日志目录不可用时构造与写入都不抛异常** |
| 机器码 | `tests/.../MachineCodeProviderTests.cs` | 4 | 8-4-4-4-12 大写 GUID；同实例恒定；同机器跨实例一致；可被 `Guid.TryParse` 解析回原值 |

**测试有效性用反向注入证明过，不是只看绿灯**：

1. 把 `FileAppLogger` 构造里的 try/catch 拿掉 → `日志目录建不出来时构造与写入都不抛异常` 单独变红（6 个用例中失败 1）。
2. 把 `AppConfig.ConcurrentCount` 默认值 1 改成 9 → 配置组 5 条断言默认值的用例全红（8 个中失败 5）。
   两次注入均已还原，`grep MUTATION` 无残留。

### 补测过程中查实的两个缺陷（已修）

1. **`FileAppLogger` 构造函数会抛**。`Directory.CreateDirectory` 未包 try，目录只读或路径被文件占用时直接崩在写第一行日志之前——正是原版「静默退出、无日志可查」的成因。已改为失败静默，首启可写性校验留给 S9。
2. **`build/run.cmd` 的 `DOTNET_ROOT` 路径错**。`dotnet.cmd` 用 `..\..\.dotnet`（仓库外一层），`run.cmd` 却写成 `..\.dotnet`（仓库内，不存在）。此前能起来是因为本机装了系统运行时兜底；换台只带仓库内 SDK 的机器就会闪退。已拆成 `REPO` 与 `SDK_ROOT` 两个根，并在 SDK 缺失时打印警告而非静默。

### 复检证据

改完日志构造函数后重新启动实测（该改动在启动路径上，只跑单测不够）：

- 窗口 `1176 × 752`，进程存活，`Logs/App_20261111.log` 记 `配置已加载` 与 `机器码：6AD79298-...`（与补测前一致，说明机器码没被动到），无异常行。
- 空态检查 `含'暂无任务': True` → `docs/plans/artifacts/S1-ui-recheck-20261111.txt`
- 构建 `0 警告 0 错误`。

新增 `build\test.cmd` 一键跑测试（复用 `dotnet.cmd` 的本地 SDK）。
