# S0 准备阶段记录

日期：2026-11-11　｜　执行人：开发　｜　工期预算 0.5 天，实际约 0.5 天

## 一、环境事实（先说清，避免后续踩坑）

| 项 | 实际情况 | 处理方式 |
| :-- | :-- | :-- |
| 系统 .NET | `C:\Program Files\dotnet` **只有运行时 10.0.15，没有 SDK**，且 `dotnet` 不在 PATH | 不用系统安装包（需管理员且会改系统目录） |
| 采用方案 | 下载 SDK 10.0.404 zip 解压到仓库外 `.dotnet/` | 无需管理员，不污染系统，随仓库一起换机 |
| 版本对齐 | SDK 10.0.404 自带运行时 10.0.15，与原版依赖的框架大版本一致 | 与原版运行环境同源，行为可比对 |
| 下载通道 | 只有走代理可通（绕代理超时） | `build/setup-sdk.cmd` 走默认代理下载，百度网盘不可用 |

调用方式统一走 `build\dotnet.cmd`，它把 `.dotnet` 临时加入 PATH，不改系统环境变量。

## 二、完成事项

1. **工具链**：SDK 10.0.404 解压至 `.dotnet/`，`dotnet --list-sdks` 与 `--list-runtimes` 均正常（含 WindowsDesktop，WPF 可编译）。
2. **仓库骨架**：`git init -b main`，七个项目按六层建立，15 条单向项目引用。
3. **版本锁定**：启用中央包管理，依赖版本集中在 `Directory.Packages.props`。Playwright 1.41.2 与 MVVM Toolkit 8.4.2 与原版一致。
4. **证据归档**：`docs/evidence/` 下建立索引与提取物，原包与解包目录留工作区不入库。
5. **原包只读化**：`_orig/` 下 317 个文件全部置只读，防止误改污染证据。
6. **敏感项处理**：`data.ini` 含原版真实卡密，**已脱敏为 `data.ini.sample`**，真实文件不入库并加入 `.gitignore`。`settings.json` 全为空字段与默认值，原样保留。
7. **账号清单模板**：`docs/accounts.sample.csv`，凭证列只存引用（`cred://acc01`），不落明文密码。

## 三、验收对照（计划 P4 的通过标准）

| 标准 | 结果 | 证据 |
| :-- | :-- | :-- |
| 空方案能编译 | ✅ 0 警告 0 错误 | `dotnet build FbAuto.slnx` 七个项目全部产出 |
| 仓库无 Cookie / 登录态文件 | ✅ | 首次提交 30 个文件，`grep` 检查无 cookie / log / rar |
| 每条结论可追溯到文件 | ✅ | `docs/evidence/EVIDENCE-INDEX.md` 编号 E1–E12，含 SHA256 与精度分级 |
| 环境清单可复现 | ✅ | `build/setup-sdk.cmd` + README 构建段 |
| 任一 Profile 手动打开仍是登录态 | ⏸ 待账号接入 | 依赖账号清单，S2 前不阻塞 |

## 四、结构决策（与方案的偏差及理由）

1. **`.slnx` 而非 `.sln`**：.NET 10 默认格式，`dotnet build` 正常。README 已同步，若后续 CI 或旧版 VS 不兼容，可用 `dotnet sln migrate` 回退。
2. **增加 `FbAuto.Core`**：方案六层中的"外部 Facebook"不是代码层，跨层模型与接口需要落点，故补一个零依赖的契约层，实际代码层仍是五层。
3. **输出统一到 `build/out`**：避免八个项目各写一份 `bin/`，打包时只需盯一个目录。`obj` 保留项目内默认位置（改动它易触发 restore 异常）。
4. **依赖方向** `Presentation → Application → Automation → Browser → Infrastructure → Core`：Browser 层只暴露 `IBrowserChannel`，Playwright 类型不向上泄漏，为后续换底座留出边界。

## 五、遗留与下一步

- **账号清单待填**：`docs/accounts.sample.csv` 复制为仓库外 `profiles/accounts.csv`，填 3 个账号（通道、Profile 路径、代理）。这是 S4 的唯一硬输入，但**不阻塞 S1**。
- **Profile 路径约定**：一律在仓库外，`config.local.json` 指向；`config.local.json` 已加入 `.gitignore`。
- 下一步进入 **S1 骨架与三栏界面**（3 天）：按还原版 P13 写 `MainWindow` 三栏与 `LoginWindow`，接 MVVM Toolkit，落日志与配置读写。
