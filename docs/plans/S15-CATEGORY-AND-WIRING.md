# S15 · 配置界面补齐（类别值与比特屏蔽词）· 以及一个「原版其实没有调度」的结论

2026-11-16 · 提交见文末 · 不依赖 Chrome 的 **589 条全绿**（Chrome 集测 63 条按指示暂缓）

---

## 1. 这一步做什么

原计划把这一步叫「调度与持久化」。动手前先回头查证据，结论跟计划不一样，所以先把结论写在这里：

**原版没有「到点自动开始」这种调度。** 证据是这几条：

- `ScheduledDate` / `ScheduledStartHour` 在 `settings.json` 里有（原版实例：`2026-07-29T00:00:00+08:00` / `0`），
  但界面上这两个控件的标签是「**定时发布**：」（见本仓库 `MainWindow.xaml` 对应位置），
  语义是**视频在 Facebook 上的定时发布时间**，不是程序的启动时间。全项目搜「定时 / 调度 / 每天 / 每周」
  在 DLL 字符串里**一条都没有**。
- 原版是手动点「开始执行」（`开始执行` / `开始执行任务: [` / `开始执行选中任务: [` 三句）。
- `settings.json` 的 22 个键里**没有任务集合**。任务只活在 `TaskCollection` 这个运行时集合里，
  关掉程序就没了 —— 原版没有任务持久化。

所以这一步改成**审计并补齐持久化链路上的真缺口**。审计方法：把 `AppConfig` 的 46 个属性与
`MainViewModel` 的读取 / 写回 / XAML 绑定逐个对一遍。

审计结果分两类：

| 类别 | 键 | 判断 |
| --- | --- | --- |
| 数据不会丢 | `ProfileCategory` `BitProfileCategory` `BitCommentsBlockWords` `ChromePath` `AccountsFile` `BitBrowserApiBaseUrl` `LicenseServerBaseUrl` `SkipActivationInDevelopment` `LogRetentionDays` | `PersistConfig` 改的是 `_configService.Current` **同一个实例**，没碰的键保持原值，不会写空 |
| **功能缺口** | `ProfileCategory` / `BitProfileCategory` / `BitCommentsBlockWords` | 配置键存在，**界面没有任何入口** —— 填不了，等于永远用默认值 |

其中 `ProfileCategory` 是最严重的一处：S14 刚加的「修改类别」勾选框，勾上之后走到
`CategoryEditor` 的「类别为空，跳过该项」—— 开关是开的，日志也正常，**那一项从来没生效过**。

这一步就把这三个入口补上，并且把「留空到底算跳过还是算按原版改」这个判断钉死。

---

## 2. 证据

### 2.1 原版的类别是硬编码

`_analysis/u16_all.txt` 第 3307–3309 行，只有三处出现「戏剧」，全是写死的：

```
div[role='option']:has-text('戏剧'), li[role='option']:has-text('戏剧')
: 未找到【戏剧】选项
: 已选择【戏剧】
```

再核对配置：原版 `settings.json` 的 22 个键里**没有类别**（既没有 `GoogleCategory` 也没有 `BitCategory`），
原版 ViewModel 的属性块（`u16_all.txt` 2528–2615 行）里也只有 `GoogleEditCategory` / `BitEditCategory`
两个**开关**，没有类别值字段。

结论：原版的语义是「**永远按戏剧改**」。所以本实现留空时必须回落到「戏剧」，
不能理解成「不改」—— 后者会让「修改类别」这一项永远空转。

### 2.2 原版的比特屏蔽词是独立键

`GoogleCommentsBlockText` 与 `BitCommentsBlockText` 在 `settings.json` 里是**两个键**
（两份实例里都填着 `.com,https,pn,dn,inbox`）。界面上原先只有谷歌版一个输入框，
比特版永远用默认值 —— 而两个通道的粉丝页语言可能不同，词表不该共用。

---

## 3. 做了什么

### 3.1 界面补三个入口

`MainWindow.xaml` 资料修改页，签名文件之后补两行类别，屏蔽词之后补比特版屏蔽词：

- 「类别：」→ `{Binding ProfileCategory}`
- 「比特类别：」→ `{Binding BitProfileCategory}`
- 「屏蔽关键词（比特版，逗号分隔）：」→ `{Binding BitCommentsBlockWords}`

三行说明文字都写清了「留空会发生什么」，避免使用者以为留空是「不改」。

### 3.2 ViewModel 三个属性 + 读写

`MainViewModel`：`_profileCategory` / `_bitProfileCategory` / `_bitCommentsBlockWords`，
构造器读取、`PersistConfig` 写回；屏蔽词与谷歌版一样按 `,`/`，` 拆分后存成数组。

### 3.3 类别留空回落「戏剧」

`CategoryEditor` 新增：

```csharp
public const string DefaultCategory = "戏剧";   // 原版写死的词

public static string ResolveTarget(string? configured) =>
    string.IsNullOrWhiteSpace(configured) ? DefaultCategory : configured.Trim();
```

`ProfileEditExecutor` 用 `ResolveTarget` 算出真正要填的值，并在留空时打一行
「没有配置类别，按原版默认值「戏剧」修改」，让日志能解释为什么改的是戏剧。

**刻意没动 `CategoryEditor.RunAsync` 的空值守卫**：它收到空仍然跳过。
两层语义分开 —— 执行器保证不传空，编辑器自身保持防御。混成一处的话，
将来有人直接调 `RunAsync` 就会静默改成戏剧。

---

## 4. 三个容易错的点

1. **「留空即跳过」看着更安全，实际是让开关空转。**
   勾了「修改类别」却不改，界面上完全看不出异常。规则是：勾选代表意图，
   有意图就必须有事发生，缺料时按原版默认值补上并打日志，而不是静默跳过。

2. **Trim 不能省。** 从别处复制类别名常带首尾空格，直接填进 FB 搜索框会「搜不到选项」，
   日志会打出 `未找到【戏剧 】选项` 这种看着一模一样却匹配不上的怪现象。
   但不能顺手把所有空白都吃掉：FB 上确实有多词类别（如「电视 节目」），内部空格要保留。

3. **注释掉的写回会骗过文本级的守卫。** 见第 5 节注入 J5：
   `// config.BitProfileCategory = ...;` 这行注释里仍然含有 `config.BitProfileCategory =`，
   正则照样匹配，用例全绿。守卫必须先剥掉注释再匹配，否则它恰好在最该管用的那次不管用。

---

## 5. 反向注入（证伪）

每项都是「改坏产品代码 → 重编译 → 跑对应用例 → 确认变红 → 还原」。
**注意：每项注入后都必须重新 `build FbAuto.slnx`**，否则 `--no-build` 跑的是旧 DLL ——
J1 第一次就是这样假绿的（见下方记录）。

| 注入 | 改坏什么 | 命中 | 命中的用例 |
| --- | --- | --- | --- |
| J1 | `ResolveTarget` 留空返回空串（等价「留空即跳过」） | 4 | `留空按原版默认类别处理而不是跳过`（null / "" / "   " / "\t\n" 四个变体） |
| J2 | `ResolveTarget` 去掉 `.Trim()` | 1 | `首尾空白被去掉` |
| J3 | `DefaultCategory` 改成「教育」 | 5 | 上面四条 + `原版默认类别是戏剧` |
| J4 | XAML 绑定名改成 `ProfileCategoryX` | 2 | `绑定都对应ViewModel属性` + `每个绑定都被保存写回`，报错直接点出 `ProfileCategoryX` |
| J5 | 注释掉 `config.BitProfileCategory = ...` 写回 | 1 | `每个绑定都被保存写回`，报错点出 `BitProfileCategory` |
| J6 | `ForChannel` 比特侧传 `ProfileCategory`（回落） | 2 | `比特通道不会读到谷歌版的路径` + `两套参数各自独立赋值` |
| J7 | `BitCommentsBlockWords` 的 getter 改成返回 `CommentsBlockWords`、setter 置空 | 2 | `两套参数各自独立赋值` + `谷歌版与比特版配置互不覆盖` |

**J1 第一次没打红**，原因不是用例有问题，是我编译命令的 SDK 路径写错了
（`./.dotnet/dotnet.exe` 少了一层目录），`grep error CS` 又刚好没输出，于是跑的还是旧 DLL。
改正后一次命中 4 条。这条记进 `docs/PITFALLS.md`：注入后没变红，先怀疑「代码真的编译进去了没有」。

**J5 第一次也没打红，这次是用例的漏洞** —— 注释掉的代码仍被正则匹配。已按第 4 节第 3 点修掉，
并在 `PersistConfigBody` 的注释里写明了这条是被注入实验逼出来的。

全部还原后 `grep -rn MUTATION src/ tests/` 无输出。

---

## 6. 未完成项（如实记录）

1. **真机验收没做。** `facebook.com:443` 在这台机器上不通，Chrome 集成测试也按需求方指示暂缓。
   类别选择器（`has-text('戏剧')`）来自原版串，但「FB 现在还是这个 DOM」没有证据。
2. **类别输入框是原版没有的东西。** 原版硬编码「戏剧」，本实现做成可配置。
   留空时回落原版值，所以不填就等于原版行为；但「填了别的词能不能匹配上」需要真机验。
3. **任务清单仍然不持久化。** 审计确认原版也没有，所以本步没动。
   代价是：关掉程序后队列清空，重启要重新添加任务。是否要补，等需求方定。
4. **`ChromePath` / `AccountsFile` / `BitBrowserApiBaseUrl` 界面上没有入口。**
   三者都有合理默认（自动查找 Chrome / 程序目录 `accounts.csv` / 本地 54345），不算硬缺口；
   要改只能手改 `config.json`。
5. **界面「选择...」按钮全项目仍然没有命令绑定**（不是本步引入的）。新增的三个入口都是纯文本框，够用。
6. **`ProfileCategory` 的默认值仍是空串，回落发生在执行器里。** 所以直接看 `config.json`
   会看到类别是空的，容易被误读成「没配置」。选择放在执行器而不是默认值上，
   是因为改成 `= "戏剧"` 会污染「用户从没设过」这个信息，将来想区分「没配」与「配了戏剧」就做不到了。
7. **Chrome 集成测试的偶发卡死未定位**（S14 已记录）。恢复前必须先查。

---

## 7. 验收对照

| 要求 | 结果 |
| --- | --- |
| 界面上能填类别 | ✅ 谷歌版 / 比特版各一个输入框 |
| 类别留空时「修改类别」仍然生效 | ✅ 回落到原版「戏剧」，日志说明「没有配置类别，按原版默认值「戏剧」修改」 |
| 比特版屏蔽词可独立配置 | ✅ 独立输入框，独立配置键，用例钉住不回落 |
| 改动的配置能存进 `config.json` 并读回来 | ✅ `JsonConfigServiceTests.谷歌版与比特版配置互不覆盖` 断言四键往返 |
| 界面绑定不会静默失效 | ✅ 两条守卫：绑定名有对应属性、每个绑定都被 `PersistConfig` 写回 |
| 每步有测试且能证伪 | ✅ 新增 10 条，注入 7 项全部命中后还原 |

---

## 8. 测试

不依赖 Chrome 的全量：**589 条全绿，两轮一致（8 秒 / 5 秒）**。
S14 是 579 条，本步净增 10 条：

| 用例类 | 条数 | 覆盖什么 |
| --- | --- | --- |
| `CategoryEditorTests`（新） | 9 | 默认值是戏剧、四种留空形态都回落、填了用填的、首尾空白去掉、内部空白保留、直接调编辑器空仍跳过 |
| `ProfileEditBindingTests` | +1 | 新增 `每个绑定都被保存写回`；`绑定都对应属性` 扩容到涵盖三个新绑定 |
| `ChannelProfileTests` | 0（扩容） | `两套参数各自独立赋值` 加类别两侧不同值的断言 |
| `JsonConfigServiceTests` | 0（扩容） | 往返用例加类别与两个屏蔽词数组 |

需真 Chrome 的 63 条按指示暂缓，日常用 `build\test-nochrome.cmd`，全量 `build\test.cmd`。

---

## 9. 文件清单

**产品**

- `src/FbAuto.Automation/Profiles/CategoryEditor.cs`（`DefaultCategory` / `ResolveTarget`）
- `src/FbAuto.Application/Execution/ProfileEditExecutor.cs`（用 `ResolveTarget`，留空打日志）
- `src/FbAuto.Presentation/ViewModels/MainViewModel.cs`（三个属性 + 读写）
- `src/FbAuto.Presentation/Views/MainWindow.xaml`（三个输入框 + 三行说明）
- `src/FbAuto.Core/Configuration/AppConfig.cs`（类别注释改准：留空按原版戏剧）
- `src/FbAuto.Core/Configuration/ChannelProfile.cs`（同上）

**测试**

- `tests/FbAuto.Tests/CategoryEditorTests.cs`（新）
- `tests/FbAuto.Tests/ProfileEditBindingTests.cs`（加写回守卫 + 剥注释）
- `tests/FbAuto.Tests/ChannelProfileTests.cs`、`tests/FbAuto.Tests/JsonConfigServiceTests.cs`（扩容）

**文档**

- `docs/plans/S15-CATEGORY-AND-WIRING.md`（本文）
- `docs/plans/artifacts/S15-tests.txt`
- `docs/PITFALLS.md`（补「注入后先确认编译进去了」）
