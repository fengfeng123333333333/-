# S18 · 交付准备：发布包、部署步骤，以及一个发布前挖出来的授权漏洞

2026-11-16 · 代码提交 `c9d203b`（S17 `619beb7` 之后）· 不依赖 Chrome 的 **654 条全绿**（Chrome 集测 63 条仍按指示暂缓）

---

## 1. 起因

需求方定了验收方案：**不在本机解决网络，改在境外云主机上跑**（A 方案）。
本步要做两件事：

1. 产出可以直接拷到另一台机器的**发布包**（`build\publish.cmd`）；
2. 写清「买到机器之后怎么从零跑到出结果」（`docs/DEPLOY-REMOTE.md`）。

做第一件的时候撞出第二件事：**发布包默认跳过激活**。

---

## 2. 证据

### 2.1 发布包里带着「跳过激活」

第一次打出来的包，启动日志原文：

```
[INF] 授权状态：Active，签发 2026-11-12，首次激活 2026-11-12 09:51，到期 2026-12-12，剩余 26 天
[WRN] 开发模式：跳过激活直接进入主界面（SkipActivationInDevelopment=true，发布前必须置 false）
```

源码里两处：

```
src/FbAuto.Core/Configuration/AppConfig.cs:285:  public bool SkipActivationInDevelopment { get; set; } = true;
src/FbAuto.Presentation/App.xaml.cs:57:          if (configService.Current.SkipActivationInDevelopment)
```

注释写着「S3 完成后默认改 false，发布版必须为 false」—— **一直没改**，而且这个判断读的是
`config.json`，而 `config.json` 是使用者能手改的明文文件。也就是说：

- 包里默认写着 `true` → 拿到包的人不用改任何东西就能用；
- 就算改了默认值，使用者把这一行改回 `true` 照样绕过。

签名、密钥、机器绑码、AES-GCM 凭证，全部作废。

### 2.2 发布产物本身是干的

`build\dist\FbAuto`：269 个文件 / 220MB，含 `coreclr.dll`（自包含，目标机不用装 .NET）、
`.playwright\node\`（Playwright 驱动）、`accounts.sample.csv`、`accounts-HOWTO.md`。
不含 `license.dat`（机器绑码，拷过去解不开）和 `config.json`（首次运行生成）。

---

## 3. 改动

| 文件 | 改动 |
| --- | --- |
| `src/FbAuto.Core/Licensing/ActivationGate.cs` | **新增**。`BuildAllowsSkip` 用 `#if DEBUG` 在编译期定死：Debug 允许跳过，Release 恒不允许 |
| `src/FbAuto.Presentation/App.xaml.cs` | 跳过判定改为 `ActivationGate.ShouldSkip(...)`；配置里写了但发布版不许时，单独打一行警告（便于支持人员认出现场） |
| `src/FbAuto.Core/Configuration/AppConfig.cs` | 去掉 `= true` 初值器，注释改成「只在 Debug 构建里有效」 |
| `build/publish.cmd` | **新增**。自包含 win-x64 发布到 `build\dist\FbAuto`，顺带拷账号样例与说明；注释写清 Chrome 与 `license.dat` 为什么不随包 |
| `.gitignore` | 忽略 `build/dist/` |
| `docs/DEPLOY-REMOTE.md` | **新增**。13 节：买机器 → 连上去 → 装依赖 → 拷程序 → 激活 → 人工登录 → 备数据 → 跑验收 → 时区 → 风控 → 费用 → 记录表 → 已知未验项 |
| `tests/FbAuto.Tests/ActivationGateTests.cs` | **新增** 7 条 |

设计上把判定拆成两个重载：`ShouldSkip(bool)` 走当前构建，`ShouldSkip(bool, bool)` 纯函数。
后者让「Release 的行为」在 Debug 测试里也能断言，不必只能靠「打个包看看」。

---

## 4. 踩到的坑：守卫是假绿的

`配置项的默认值不是跳过` 第一版这么写：从属性名往后找第一个 `;` 截断，断言里面没有 `=`。
注入 G3（把 `= true` 加回去）之后**用例没红**。

原因：`public bool SkipActivationInDevelopment { get; set; } = true;`
第一个分号在 `{ get;` 里，截出来的是 `SkipActivationInDevelopment { get`，`= true` 根本没进比对。

修法：先把 `{ get; set; }` 从行里剥掉，再断言不含 `=`。改完重跑 G3 立刻变红。

> 这已经是本项目第三次「守卫自己写错导致假绿」（S15 的 J5 是没剥注释，S17 的绑定守卫把命令当配置）。
> 共同点：**守卫的定位逻辑（截取范围）出错时，用例是绿的**，而且看不出来。
> 唯一可靠的发现方式是注入 —— 不注入就永远不知道这条守卫是否在管。

---

## 5. 反向注入（证伪）

| 注入 | 改坏什么 | 结果 | 命中的用例 / 现象 |
| --- | --- | --- | --- |
| G1 | `ShouldSkip(配置, 构建)` 直接返回配置值，不看构建 | 红 | `发布构建即使配置写了跳过也不跳`：`Assert.False() Failure — Expected: False, Actual: True` |
| G2 | `App.xaml.cs` 改回 `if (wantsSkip)` 直接读配置 | 红 | `启动流程必须经由闸门判定而不是直接读配置`：`Assert.Contains() Failure — Sub-string not found` |
| G3 | `AppConfig` 加回 `= true` 初值器 | **先绿后红** | 第一版守卫漏判（见第 4 节），修守卫后：`Assert.DoesNotContain() Failure — Sub-string found` |
| G4 | `BuildAllowsSkip` 去掉 `#if`，恒返回 `true` | 红（运行期） | 打发布包 → 无 `license.dat`、`config.json` 写 `true` → **进了主界面**，日志出现「账号清单为空」与「界面自检通过」 |

G4 是这一步最值钱的一条：它把「编译期闸门」这条设计从「读代码觉得对」变成「实测过」。
还原后同样的场景停在激活窗（25 秒内没有任何主界面日志）。

全部还原，`grep -rn MUTATION src/ tests/` 无输出。

---

## 6. 未完成项（如实记录）

1. **真机验收仍未做**。`facebook.com:443` 本机不通，方案已改为境外云主机，步骤已写进
   `docs/DEPLOY-REMOTE.md`，但**还没实际跑过一轮**。
2. **激活窗口本身没有自动化测试**。本步只验证了「什么情况下会走到它」，没验证
   「输入码之后能激活成功」—— 这部分由 `OfflineLicenseService` 的单测覆盖，UI 交互没人点过。
   云主机上首次激活就是第一次真人点它，如果卡住要现场看日志。
3. **离线授权的固有限制没变**：码不含机器绑定，同一码可在多台机器激活（各自从首次激活
   起算 30 天）。这是「无服务器」换来的代价，已记录，不在本步范围。
4. **发布包没有版本号 / 校验和**。拷过去之后分不清是哪次构建的。建议交付时把
   `git rev-parse HEAD` 写进一个 `VERSION.txt`，本次没做（需求方没提，先不擅自加文件）。
5. **`build\dist\` 不进仓库**（体积 220MB），换机器要重新 `build\publish.cmd`。
6. Chrome 集测偶发卡死、任务不持久化、比特通道未真机验 —— 与前几步一致，仍未解决。

---

## 7. 怎么用

```
build\publish.cmd                 # 产出 build\dist\FbAuto（代码改了要重打）
```

然后把 `build\dist\FbAuto` 整个目录拷到目标机器，按 `docs/DEPLOY-REMOTE.md` 往下走。

自查是否打对了包（三样必须同时在）：

- `FbAuto.Presentation.exe`
- `.playwright\node\`（缺了页面操作全废，报错却是「浏览器起不来」）
- 启动日志里有 `界面自检通过：15 个「选择...」按钮都接上了路径选择命令`
