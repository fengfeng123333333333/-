# 证据索引

原版程序分析证据的登记表。**每条结论都必须能回溯到本表某一行**，否则视为推测。

> 性质：原版为第三方商业授权软件，此处仅作对标参考。
> 使用边界：只读、只用于内部对照，不随产品发布，不复制其代码或素材。

## 一、原始样本

| # | 文件 | 大小 | SHA256 | 位置 |
| :-- | :-- | --: | :-- | :-- |
| E1 | `FaceBook自动化V1.1.1_1(1).rar` | 24.5 MB | `9aa8d026062cc81b3f2631fba6bb47630c902869fcdbc75ce7516603e7e26cc8` | 工作区根目录（不入库） |
| E2 | `FaceBook自动化.exe` | — | `4f4cd80ed14ae50808c27f18dcfbe1ebdb11105bbd9caf186e3adaa69ebbae5a` | `_orig/FaceBook自动化V1.1.1_1/` |
| E3 | `FaceBook自动化.dll`（业务逻辑） | 524 KB | `bb87da0bc210bc0abb1549ea2b6f082868b618c44cd28386d7bc342a146136ac` | 同上 |

解包后整目录 88 MB，体积大头是内置的 Playwright 驱动与 node.exe，业务 DLL 仅 524 KB。

## 二、配置文件证据（已归档至 `extracted/`）

| # | 文件 | 证明什么 | 精度 |
| :-- | :-- | :-- | :-- |
| E4 | `data.ini` | 授权结构：Base64 载荷 `{Product, LicenseFormat:FBA1, CardId, Plan:L2099, MachineCode, ExpiresAt, CreatedAt}` + `\|\|` + 16 位十六进制签名。**对称签名，密钥内置于客户端**；机器码为完整 5 段 GUID | 一级（字段直出） |
| E5 | `settings.json` | 配置字段全集：视频路径、描述、保存目录、`ConcurrentTaskCount:1`、`BrowserOpenInterval:3`、`ScheduledDate`、`ScheduledStartHour`、浏览器类型、公共主页发帖设置（页名/密码/头像目录/评论屏蔽词）。**无队列与任务持久化字段** → 原版任务随进程消失 | 一级（字段直出） |
| E6 | `FaceBook自动化.runtimeconfig.json` | 声明 frameworks 依赖、目录内无 hostfxr/coreclr → **依赖框架发布，非自包含**，目标机须装 .NET 10 Desktop Runtime | 一级（实测启动验证） |

## 三、行为证据

| # | 来源 | 结论 | 精度 |
| :-- | :-- | :-- | :-- |
| E7 | `Logs/*.log`（20260728–0729） | 启动即写一行「应用程序启动」；浏览器列表惰性加载；比特浏览器初始化失败会阻断流程；任务粒度日志含账号、结果 | 一级（原文） |
| E8 | 隔离运行实验（`_runtest/`，2026-11-11） | 缺 `DOTNET_ROOT` 时**静默退出、零日志、无弹窗**；设置后正常启动，窗口标题为 **「自动化工具_Facebook」**；运行期间**零对外网络连接**，`settings.json` 不改写 | 一级（实测） |
| E9 | UI Automation 采集（`_uia_tree.txt`） | 激活窗 540×580；机器码行 + 复制按钮；**多行**卡密输入框（带滚动条）；全宽「登录激活」按钮；错误态「× 设备不匹配 · 卡密与当前设备不一致」；底部「已自动读取本地卡密」→ **启动即自动尝试激活，失败才停在此窗** | 一级（实测控件树） |
| E10 | `deps.json` | 依赖版本：Microsoft.Playwright 1.41.2、CommunityToolkit.Mvvm 8.4.2、ClosedXML、XLParser、Irony | 一级（清单直出） |

## 四、字符串还原（`extracted/*.txt`）

| # | 文件 | 内容 | 用途 |
| :-- | :-- | :-- | :-- |
| E11 | `clean_cn.txt` | 清洗后的 472 条中文界面串 | 界面文案 1:1 对齐的唯一依据 |
| E12 | `*.cn.txt` / `*.ascii.txt` | 原始提取串（含类型名、XAML 命名空间、方法名） | 反查类结构与方法语义 |

从 E12 得到的方法级证据（支撑架构判断）：
- `WaitIfPausedAsync` → 存在 Paused 状态，任务可暂停
- `WaitForElementWithRetryAsync` → **原版有元素级重试**（不可与任务级重试混为一谈）
- `CheckCoverSizeErrorAsync` → 封面尺寸预校验
- `CloseNotificationPopupAsync` → 通知弹窗处理
- `DeleteFailedVideosAsync` → 失败视频草稿清理
- `GetRandomImageFile` → 头像/背景图随机取图
- `GooglePage`/`BitPage`、`GoogleEditPage`/`BitEditPage` 成对出现 → **按通道各写一套 PageObject**
- 全包无业务 URL → 授权完全离线，程序不联网

## 五、精度分级说明

| 级别 | 含义 | 可信度 |
| :-- | :-- | :-- |
| 一级（直出/实测） | 字段、字符串、控件树、运行日志直接读取 | 可直接作为实现依据 |
| 二级（结构推断） | 依据类名成对关系、方法名分布与 WPF 惯例推断布局 | 需真机截图或卡密后校准 |
| 未获取 | 主窗口像素级布局、真实页面选择器 | **1:1 复刻前必须补**（需合法卡密或作者截图） |

## 六、已知边界

1. **未反编译业务逻辑**，只读字符串与元数据，未触碰授权校验。
2. **未绕过授权**：本机机器码与原包卡密不匹配，主窗口在激活成功前不创建，故主窗口布局为二级推断。
3. GUI 截图受安全策略限制不可用，界面证据依赖 UIA 控件树 + 字符串（E9/E11）。
