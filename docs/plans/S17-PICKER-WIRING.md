# S17 · 界面补齐（15 个「选择...」按钮真正接上对话框）

2026-11-16 · 提交 `619beb7` · 不依赖 Chrome 的 **647 条全绿**（Chrome 集测 63 条按指示暂缓）

---

## 1. 这一步做什么

起因是一句结论，反复出现在 S15 的「未完成项」里：

> 界面「选择...」按钮全项目无命令绑定（路径要手填）。

也就是说：所有要填目录、填文件的输入框，使用者都得自己把路径一个字一个字敲进去。
这是交付前最卡的一处 —— 功能都在，但要人手工拼 `D:\...\账号名\视频`。

本步做三件事：

1. 把界面上**已有的 13 个**「选择...」按钮接上系统对话框；
2. 补上 `ChromePath` / `AccountsFile` 这两个**连输入框都没有**的路径入口（合计 15 个）；
3. 加一条**启动自检**：程序一启动就数一遍按钮有没有真的拿到命令，结果写进日志。

第 3 条不是锦上添花，见第 4 节第 1 点：WPF 的绑定失败是完全静默的。

---

## 2. 证据

### 2.1 按钮确实一个都没接

```
$ grep -n 'Content="选择\.\.\."' Views/MainWindow.xaml
201, 213, 230, 242, 254, 368, 390, 410, 421, 453, 467, 481, 492   ← 13 处
$ grep -c 'Command="{Binding PickPathCommand}"' Views/MainWindow.xaml
0
```

13 个按钮全都只有 `Content` / `Style` / `Width` / `Height` / `Margin`，没有一个有 `Command`。
在 WPF 里这就是 13 个纯装饰：能点、有按下动画、什么都不发生，也不报错。

### 2.2 两个路径连输入框都没有

S15 的审计已经记下：`ChromePath` / `AccountsFile` 在 `AppConfig` 里有键、`MainViewModel` 里没有属性、
`MainWindow.xaml` 里没有绑定 —— 只能手改 `config.json`。

两者都有合理默认（自动查找 Chrome / 程序目录 `accounts.csv`），所以不算硬缺口；
但换机器、Chrome 是免安装版、账号清单放在共享盘时就没有办法了。
本步顺手补上，因为「选择...」这一套机制做出来之后，多两个入口几乎零成本。

### 2.3 为什么不做「删除消息」那个 cookie 目录

`DeleteMessageCookieRootPath` 也是个目录，但 S9 已经查清：**这条任务的登录态只从浏览器已登录配置来，
这个配置项只读出来写日志、不注入**（cookie 注入是另一条登录路线）。

给它加个「选择...」按钮，会让人以为配了就生效 —— 那比没有按钮更糟。
所以故意不加，继续留在未完成项里。

---

## 3. 做了什么

### 3.1 抽象放 Application 层，对话框留界面层

```
src/FbAuto.Application/Dialogs/
  IPathPicker.cs          ← PathPickerKind / PathPickerRequest / IPathPicker
  PathPickerCatalog.cs    ← 15 个 key → 选目录还是选文件、什么筛选器
  PathStartFolder.cs      ← 「上次选到哪」的推导
src/FbAuto.Presentation/Services/
  WpfPathPicker.cs        ← 唯一碰 Microsoft.Win32 的地方
```

为什么这么分：**测试项目刻意不引用 WPF 层**（`tests/FbAuto.Tests.csproj` 里没有 Presentation），
否则整组用例要拖上 STA 线程与桌面依赖。所以「哪个按钮选目录、哪个选文件、什么扩展名」
必须是数据、放在能被单测的地方，不能只写在 XAML 里。

这类错误特别隐蔽：把描述文件选成了目录，界面照样显示一串路径，直到真跑任务才报「读不到描述文件」。

### 3.2 十五个按钮共用一个命令

```xml
<Button Content="选择..." Command="{Binding PickPathCommand}" CommandParameter="VideoDirectory" />
```

```csharp
[RelayCommand]
private void PickPath(string? key)
{
    var request = PathPickerCatalog.TryResolve(key);
    if (request is null || string.IsNullOrWhiteSpace(key)) { /* 记警告 */ return; }

    var picked = _pathPicker.Pick(request, CurrentPathOf(key));
    if (string.IsNullOrWhiteSpace(picked)) return;   // 取消：保留原值

    ApplyPath(key, picked.Trim());
}
```

十五份一模一样的 `[RelayCommand] private void PickXxx()` 也能写，但改一个筛选器要改十五处，
而且漏改不会有任何报错。做成 key → 请求的对照表后，界面上只剩「这个按钮选哪一项」。

`CurrentPathOf` / `ApplyPath` 是两个 `switch`，都以 `nameof(属性名)` 作分支 ——
属性改名时 `nameof` 跟着变，对照表没跟着改就会在运行期落到 `default` 并记警告。

### 3.3 取消 = 不算数，不是清空

对话框取消时返回 `null`，ViewModel 直接返回、不动属性。

这一条写进了 `IPathPicker` 的注释和用例：**取消必须返回 null，不能返回空串**。
两者混同的话，使用者手滑点一次取消，已经填好的目录就没了，而且没有任何提示。

### 3.4 启动自检

`MainWindow` 的 `Loaded` 里遍历可视化树，数出 `Content == "选择..."` 的按钮总数和其中
`Command != null` 的个数，交给 `MainViewModel.VerifyPickerWiring` 记日志：

```
[INF] 界面自检通过：15 个「选择...」按钮都接上了路径选择命令
[WRN] 界面自检失败：「选择...」按钮应有 15 个且全部接命令，实际 15 个、其中 14 个接了命令（这些按钮点下去不会有反应，属于程序缺陷）
```

第二行是真的跑出来的（见第 5 节 N8），不是设计文档里的想象。

### 3.5 两个新入口

「视频上传」页底部新增「程序设置」区：Chrome 路径、账号清单，各带一个「选择...」。
账号清单那行下面写明了「改路径后需重启程序才生效」—— 因为清单在 `App.xaml.cs` 启动时读入，
运行中换文件会让队列里的账号名对不上。

---

## 4. 四个容易错的点

1. **WPF 的绑定失败是完全静默的。**
   `Command="{Binding PickPathCommand}"` 里属性名写错、命令没生成，不抛异常、不报编译错误，
   按钮照样显示、有按下动画、点下去什么都不发生。
   文本守卫只能证明「XAML 里写了这几个字」，所以补了运行时自检 —— 那是唯一能回答
   「真的连上了吗」的证据。

2. **逻辑树里混着非 Visual 的元素，不能直接喂给 `VisualTreeHelper`。**
   第一版遍历写成「逻辑树 + 可视化树都走」，结果在 `RowDefinition` 上直接抛
   `InvalidOperationException: 「System.Windows.Controls.RowDefinition」不是 Visual 或 Visual3D`，
   界面线程未处理异常弹窗（日志 15:11:36 有记录）。
   修法：`if (node is not Visual and not Visual3D) return;` 再走可视化树。
   另外两棵树大量重叠，必须记访问过的节点，否则同一棵子树会被反复遍历。

3. **`ProfileEditBindingTests` 会把 `Command="{Binding XxxCommand}"` 当成数据绑定。**
   接上按钮之后那条守卫立刻红，报「PickPathCommand 没有写回 config.json」。
   命令是行为不是配置值，正则加了 `(?<!Command=")` 前视排除。
   这条改动也让守卫更准确：原来那一页一个 `Command` 都没有，所以这个洞一直没暴露。

4. **「选择...」按钮的 `CommandParameter` 必须等于旁边输入框的绑定名。**
   十三个按钮长得一模一样，复制粘贴时最容易接到上一行去。
   接到了别处不会报错，只会「选了视频目录、填进了保存目录那一格」。
   用例 `按钮参数与旁边输入框绑定同名` 专治这个：从按钮位置往前找最近的
   `Text="{Binding X,`，断言两者相等。

---

## 5. 反向注入（证伪）

8 项，每项都是「改坏 → 重新 build 整个 slnx → 跑对应用例（或启动程序）→ 确认变红 → 还原」。
`build` 整个 slnx 是必须的：只 build 测试项目会跑旧 DLL。

| 注入 | 改坏什么 | 结果 |
| --- | --- | --- |
| N1 | 摘掉 `VideoDirectory` 按钮的 `Command` | 红 3 条。`每个选择按钮都接了命令`：`Expected: 15, Actual: 14` |
| N2 | `CommandParameter="VideoDirectory"` 改成 `"VideoDir"` | 红 3 条。`按钮参数与旁边输入框绑定同名`：`Expected: "VideoDirectory" Actual: "VideoDir"` |
| N3 | 对照表把它描述文件写成 `Folder` | 红 2 条。`每个入口的对话框种类正确`：`Expected: File Actual: Folder`；`文件入口锁定了扩展名` 找不到 `*.txt` |
| N4 | **注释掉** `case nameof(SaveDirectory): SaveDirectory = value;` | 红 1 条：`这些 key 能弹对话框，但 ApplyPath 里没有对应分支，选完不写回：SaveDirectory` |
| N5 | **注释掉** `config.ChromePath = ...` | 红 1 条：`这些路径界面上能选，但 PersistConfig 没有写回，重启就丢：ChromePath` |
| N6 | `SaveDirectory` 按钮的参数改成 `VideoDirectory`（模拟接错行） | 红 2 条：`Expected: "SaveDirectory" Actual: "VideoDirectory"` |
| N7 | `PathStartFolder` 不再识别文件路径（返回 null） | 红 1 条：`现有的文件取它所在的目录`，`Expected: "C:\...\FbAutoTests\..." Actual: null` |
| N8 | 摘掉 `ChromePath` 按钮的命令后**启动程序** | 日志出现 `[WRN] 界面自检失败：…实际 15 个、其中 14 个接了命令` |

N4 / N5 用注释注入是有意的：这两条同时验证「守卫先剥注释再匹配」这个机制真的生效
（S15 的 J5 假绿就是栽在这里）。注释掉之后仍然打红，说明 `CsSource.MethodBody` 的剥注释是对的。

N8 是这次唯一一条**运行期**注入：它证明「界面自检」不是一句永远绿的日志。
还原后重跑，日志回到 `[INF] 界面自检通过：15 个…`。

全部还原后 `grep -rn MUTATION src/ tests/` 无输出。

---

## 6. 未完成项（如实记录）

1. **对话框本身没有被自动化验证过。** 能证明的是：15 个按钮在运行时都拿到了命令（启动自检），
   对照表的种类与筛选器正确（单测），取消保留原值（代码路径）。
   证明不了的是「点下去真的弹出了系统对话框、选完真的填进输入框」——
   这需要真人点一次，或者装 UIA 库（本机 `pywinauto` / `comtypes` / `uiautomation` 都没有，
   `Add-Type` 也被安全策略禁了，截屏这条路也走不通）。
2. **`WpfPathPicker` 里的两行对话框调用没有单测**（`OpenFolderDialog` / `OpenFileDialog`）。
   起始目录的推导已经挪到 Application 层并由 `PathStartFolderTests` 覆盖，
   剩下的就是「调 API、取结果」，只能在真机上验。
3. **`DeleteMessageCookieRootPath` 故意不加按钮**（见 2.3）：加了会让人以为配了就生效。
   要加得先做 cookie 注入那条登录路线。
4. **真机验收仍然没做**：`facebook.com:443` 不通。本步只动界面，不涉及页面选择器，
   但「没在真 FB 上跑过」这个前提没变。
5. **Chrome 集成测试的偶发卡死未定位**（S14 起）。按指示暂缓，恢复前必须先查。
6. **任务清单仍然不持久化**（S15 起记录），等需求方定。

---

## 7. 验收对照

| 要求 | 结果 |
| --- | --- |
| 界面上每个「选择...」按钮都能选东西 | ✅ 15 个全部接上；启动自检日志证明运行时命令都在 |
| 选目录的弹目录框、选文件的弹文件框 | ✅ 对照表区分 Kind，15 条 Theory 逐个钉住 |
| 文件选择锁定扩展名（txt / exe / csv） | ✅ 8 条 Theory 断言筛选器含对应通配符 |
| 取消后不清空已填路径 | ✅ `IPathPicker` 契约写死 null，ViewModel 判空即返回 |
| 选完能存进 config.json | ✅ `PersistConfig` 写回 15 项，守卫逐项核对 |
| 再点一次停在上次的位置 | ✅ `PathStartFolder` 处理目录 / 文件 / 失效路径三种情况，5 条用例 |
| 按钮接错行能被发现 | ✅ 文本守卫 + 注入 N6 验证 |
| 每步有测试且能证伪 | ✅ 新增 49 条，注入 8 项全部命中后还原 |

---

## 8. 测试

不依赖 Chrome 的全量：**647 条全绿（耗时 5 / 6 秒两轮）**。
S16 是 598 条，本步净增 49 条：

| 用例类 | 条数 | 覆盖什么 |
| --- | --- | --- |
| `PathPickerCatalogTests`（新） | 32 | 15 个 key 一个不多一个不少、种类正确、种类与筛选器自洽、标题非空且说明用途、8 个文件入口的扩展名、6 种无效 key 返回空 |
| `PathPickerWiringTests`（新） | 8 | 每个按钮都接命令、参数能在表里查到、表里每项都有按钮、参数与旁边输入框同名、命令真的生成、每个 key 都能写回 / 取当前值 / 被保存写回 |
| `PathStartFolderTests`（新） | 9 | 目录直接用、文件取父目录、两种失效路径、四种空值、首尾空格 |
| `ProfileEditBindingTests` | 0（修） | 正则加 `(?<!Command=")` 排除命令绑定（见第 4 节第 3 点） |

需真 Chrome 的 63 条按指示暂缓，日常 `build\test-nochrome.cmd`，全量 `build\test.cmd`。

额外做了两次**运行期**验证（不在 xunit 里，日志在
`build/out/FbAuto.Presentation/Debug/net10.0-windows/Logs/App_20261116.log`）：

- 正常启动：主窗口标题 `自动化工具_Facebook`，`[INF] 界面自检通过：15 个…`
- 注入 N8 后启动：`[WRN] 界面自检失败：…实际 15 个、其中 14 个接了命令`

---

## 9. 文件清单

**产品**

- `src/FbAuto.Application/Dialogs/IPathPicker.cs`（新：`PathPickerKind` / `PathPickerRequest` / `IPathPicker`）
- `src/FbAuto.Application/Dialogs/PathPickerCatalog.cs`（新：15 个 key 的对照表）
- `src/FbAuto.Application/Dialogs/PathStartFolder.cs`（新：起始目录推导）
- `src/FbAuto.Presentation/Services/WpfPathPicker.cs`（新：唯一碰 `Microsoft.Win32` 的地方）
- `src/FbAuto.Presentation/ViewModels/MainViewModel.cs`
  （注入 `IPathPicker`、`PickPathCommand`、`CurrentPathOf` / `ApplyPath`、两个新属性、
  `PersistConfig` 写回两项、`VerifyPickerWiring`）
- `src/FbAuto.Presentation/Views/MainWindow.xaml`（15 个按钮接命令 + 「程序设置」区两行）
- `src/FbAuto.Presentation/Views/MainWindow.xaml.cs`（`Loaded` 里数按钮）
- `src/FbAuto.Presentation/App.xaml.cs`（装配 `WpfPathPicker`）

**测试**

- `tests/FbAuto.Tests/PathPickerCatalogTests.cs`（新，32 条）
- `tests/FbAuto.Tests/PathPickerWiringTests.cs`（新，8 条）
- `tests/FbAuto.Tests/PathStartFolderTests.cs`（新，9 条）
- `tests/FbAuto.Tests/Support/RepoFiles.cs`（新：按仓库根找文件，两类守卫共用）
- `tests/FbAuto.Tests/Support/CsSource.cs`（新：切方法体 + 剥注释，两类守卫共用）
- `tests/FbAuto.Tests/ProfileEditBindingTests.cs`（改用公共工具 + 排除命令绑定）

**文档**

- `docs/plans/S17-PICKER-WIRING.md`（本文）
- `docs/plans/artifacts/S17-tests.txt`
- `docs/PITFALLS.md`（补「绑定静默失效要运行期自检」「逻辑树里有非 Visual 元素」）
