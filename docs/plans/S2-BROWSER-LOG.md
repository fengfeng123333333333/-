# S2 浏览器双通道适配器 · 执行记录

日期：2026-11-11 17:30
状态：代码与单测完成；**原验收动作「打开 facebook.com 读已登录主页名」未完成**（见第五节）
对应计划：《Facebook自动化_仿制实施计划》S2（P06）

## 一、输入

- S1 产出：六层骨架、日志、配置、组合根。
- 本机环境：Chrome 156.0.8078.81（`C:\Program Files\Google\Chrome\Application\chrome.exe`）、.NET 10、Microsoft.Playwright 1.41.2（与原版同版本）。
- 缺什么：比特浏览器未安装；本机到 facebook.com 的 TCP 443 不通。

## 二、产出物

| 层 | 文件 | 作用 |
| :-- | :-- | :-- |
| Browser · 契约 | `Abstractions/IBrowserChannel.cs` | 六方法：Launch / GetWsEndpoint / Connect / NewPage / IsAlive / Close |
| Browser · 契约 | `Abstractions/BrowserSession.cs` | 会话句柄：PID、调试端口、ws、http 端点、profile 目录、比特窗口 ID |
| Browser · 契约 | `Abstractions/BrowserLaunchRequest.cs` | 启动参数：profile 目录、比特窗口 ID、代理、可执行文件、无头、追加参数、超时 |
| Browser · 契约 | `Abstractions/BrowserChannelException.cs` | 10 种失败原因 + 中文可执行提示（说原因、给下一步） |
| Browser · 谷歌 | `Chrome/ChromeChannel.cs` | 独立 profile 启动、读端口、ConnectOverCDP、双条件探活、按 PID 关闭 |
| Browser · 谷歌 | `Chrome/ChromeOptionsBuilder.cs` | 参数拼装（纯函数，单测友好） |
| Browser · 谷歌 | `Chrome/ChromeLocator.cs` | 找 chrome.exe：显式配置 → 常见路径 → 注册表 App Paths |
| Browser · 谷歌 | `Chrome/DevToolsActivePortFile.cs` | 解析 `--remote-debugging-port=0` 写出的端口与 ws 路径 |
| Browser · 比特 | `Bit/BitBrowserApiClient.cs` | `POST /browser/open`、`POST /browser/close`，关闭代理、连不上报中文 |
| Browser · 比特 | `Bit/BitApiParser.cs` | 兼容 `code`/`success`、`msg`/`message`、pid 字符串等版本差异 |
| Browser · 比特 | `Bit/BitBrowserChannel.cs` | 按 profileId 开窗、复用首个 context、接口关闭 |
| Browser · 公共 | `Internal/CdpConnector.cs` | 两通道共用的 ConnectOverCDP + 开页，杜绝「一边 Launch 一边 Connect」 |
| Browser · 公共 | `Internal/LocalHttpProbe.cs` | 本机探针，**强制 UseProxy=false** |
| Browser · 页面 | `Pages/PageObjectBase.cs` | 导航 / 等元素 / 截图，另加读文本（验收要读主页名） |
| Application | `Browser/BrowserChannelFactory.cs` | 按通道类型创建实现，配置里的 ChromePath、比特接口地址真正传下去 |
| Core | `Configuration/AppConfig.cs` | 新增 `ChromePath`、`BitBrowserApiBaseUrl` |

## 三、验收动作与实测结果

| 计划验收项 | 结果 | 证据 |
| :-- | :-- | :-- |
| ① 两通道各打开 facebook.com 并读到已登录主页名 | **未完成** | 本机 FB 443 不通、比特未安装。替代验证见第四节，缺口记在第五节 |
| ② 比特未启动时错误提示中文且指明原因 | 通过 | `客户端没开时报中文错误并说明是本地接口连不上`；消息含「比特浏览器未启动」「http://127.0.0.1:PORT」「请先打开比特浏览器客户端并登录」 |
| ③ 每完成一步必须有测试用例 | 通过 | 87 个用例全绿，27 秒；明细 `artifacts/S2-tests-20261111.txt` |
| ④ 构建零警告零错误 | 通过 | `0 个警告 0 个错误`（7 个程序集） |

## 四、替代验证（在缺口补上之前，这就是最强的证据）

**Chrome 通道：真机全链路**

`--headless=new` + 独立临时 profile，实测走完：

启动进程 → 读 `DevToolsActivePort` → 拼 ws → `ConnectOverCDP` → 开页 → 导航本地 HTML → 读 `#title` 文本「本地验收页」→ 读多元素文本 ["A","B"] → 截图（>1KB）→ 探活 true → 按 PID 关闭 → PID 消失、探活 false。

覆盖的异常路径：等元素超时（中文，带选择器）、导航打不开的地址（中文，带 URL）、可执行文件不存在（中文，带路径）、进程在但调试端口不通（判不活）、上次残留端口文件被清理。

**比特通道：协议层 + 错误路径，用裸 TcpListener 桩服务**

未装比特客户端，因此用桩服务模拟 `127.0.0.1:54345`：

- `/browser/open` 收到 `{"id":"profile-001"}`，返回 ws/http/pid → 会话字段逐个核对。
- `/browser/close` 收到窗口 ID → 「关闭窗口调的是接口而不是杀进程」。
- 端口无人监听 → 中文「比特浏览器未启动」，带 baseUrl。
- `code:10001` → 中文「接口拒绝打开窗口：code=10001，浏览器实例不存在」。

> 桩服务用 `TcpListener` 而不是 `HttpListener`：后者在非管理员下对 `127.0.0.1` 前缀会拒绝监听，测试会因权限而红，和环境无关。

## 五、未完成项与前置条件（不许含糊）

| 缺口 | 卡在哪 | 补上的条件 |
| :-- | :-- | :-- |
| 两通道打开 facebook.com 读主页名 | 本机 `facebook.com:443` 超时（DNS 正常，TCP 不通） | 需要境外出口：境外云主机或跨境专线；账号需先在那条出口上登录过 |
| 比特通道真机联调 | 比特浏览器客户端未安装，`127.0.0.1:54345` 无服务 | 装比特客户端 + 建 3 个窗口，拿到窗口 ID 填进账号清单 |
| 有头模式视觉确认 | 集成测试用无头，界面渲染没验 | 出口通了之后再看，无头更容易被识别，正式跑必须有头 |

计划 P06 的退回条件写的是「某通道三天内调不通，就先只交付 Chrome 通道」。当前**两个通道的代码都已交付**，卡的是外部条件而不是技术路线，所以不走退回。

## 六、本轮踩到的坑

1. **`Application` 命名空间与 `System.Windows.Application` 冲突**。新增 `FbAuto.Application` 后，`App.xaml.cs` 里裸写 `Application` 报 CS0118。基类改成 `System.Windows.Application` 全名。
2. **原始字符串插值 `$$"""` 与 JSON 的双大括号冲突**（CS9007）。JSON 结尾的 `}}` 被当成插值定界符，改用 `$$$"""` + `{{{ }}}`。
3. **本机 HTTP 探针必须 `UseProxy=false`**。系统注入了 `HTTP(S)_PROXY=127.0.0.1:54802`，不关代理时连 `127.0.0.1` 也会送去代理，探活会得出与事实相反的结论。
4. **Chrome 的 `--remote-debugging-port=0` 只能靠 `DevToolsActivePort` 文件拿端口**，扫端口会撞上别的实例；重启前必须删掉上次的残留文件，否则可能读到旧端口连到已关闭的浏览器。

## 七、测试有效性（反向注入，按 17:20 的新规矩）

绿灯不算证据，逐项注入缺陷验证测试真的会红：

| 注入的缺陷 | 变红的用例 | 结论 |
| :-- | :-- | :-- |
| `ChromeChannel.IsAliveAsync` 只看进程、不问端口 | `ChromeChannelIntegrationTests.进程在但调试端口不通时判为不活` | **先发现该分支原本无用例覆盖**，补上用例后才拦得住 |
| `BitApiParser` 忽略失败码（`success = true`） | 解析器 2 条 + 客户端 1 条，共 3 条 | 失败码这条链有人守 |
| 去掉残留端口文件清理 | 保留观察（用例断言 DebugPort≠旧值） | 见下条说明 |

第三项说明清楚：Chrome 启动时会自己重写该文件，所以「不清理」在多数情况下仍能连上，用例的拦截力弱于前两项。保留它是因为真机上存在「读到旧文件 → 连已关闭实例」的竞态，属于低概率但排障极难的一类。

## 八、S2 明确没做的事

- 没把通道接进界面按钮（S4）：点「打开浏览器」仍然没有行为。
- 没做账号级 profile 目录分配（S4）：目录由调用方传，尚未和账号清单绑定。
- 没做反检测（不属于 S2，且计划里明确「反检测库不如节奏拟人有效」）。
- 没做任务级重试：元素级等待超时只报错，不重跑整个任务（V1 口径）。
