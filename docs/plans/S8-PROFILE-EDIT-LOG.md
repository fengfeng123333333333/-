# S8 · 资料修改（谷歌版）· 执行记录

2026-11-12 · 提交 `efa3e20` · 497 用例全绿

## 一、这一步补的是什么

把手写的「改资料」接到真流程：**个性签名、类别、发帖设置**三项。
接完之后的边界：`VideoUploadGoogle`、`ProfileEditGoogle` 两个是真的，另外三类（比特版两种、
删除消息）仍是「未接入」占位。

`ExecutorCatalogTests` 把这条边界钉住了 —— 少注册一个执行器的表现是「任务正常跑、正常失败、
日志说功能没做」，最容易被当成环境问题查半天。

**这一步不含**主页名称与头像/背景图（理由见第七节）。

## 二、证据：原版到底怎么改

全部从 `_analysis/utf16_strings.txt` 还原（原始 DLL 按 UTF-16LE 重扫），按出现顺序：

| 环节 | 证据串 |
|---|---|
| 签名页 | `&sk=directory_intro`、`: 跳转签名页面:` |
| 签名入口（已有） | `[aria-label='编辑个性签名']`、`: 检测到已有签名,点击` |
| 签名入口（没有） | `span:has-text('关于你')`、`: 未检测到已有签名,点击` |
| 签名入口都没有 | `: 未找到任何签名入口,跳过` |
| 签名框 | `textarea[dir='ltr'][maxlength='255']`、`: 未找到签名输入框` |
| 签名保存 | `: 签名已输入:`、`: 点击保存按钮失败(可能会自动保存)`、`: 签名修改完成` |
| 类别页 | `&sk=directory_category`、`: 跳转类别设置页面:` |
| **当前类别判定** | `div[dir='auto']:has-text('戏剧')`、`: 当前类别已是`、`: 当前类别不是` |
| 类别入口 | `[aria-label='编辑类别']`、`: 已点击编辑类别` |
| **类别搜索** | `input[role='combobox'][aria-label='类别']`、`: 在输入框中输入`、`Control+a`、`Backspace`、`: 已输入` |
| **类别下拉项** | `div[role='option']:has-text('戏剧'), li[role='option']:has-text('戏剧')`、`: 从下拉列表中选择`、`: 已选择` |
| 类别保存 | `div[role='button'][aria-label='保存']`、`: 类别修改完成` |
| 发帖设置页 | `https://www.facebook.com/settings/?tab=profile_and_tagging` |
| 设置行 | `span:has-text('谁能在你的公共主页发帖？')`、往上爬 `role='button'` 祖先的 JS、`: 已点击发帖设置行` |
| 仅限自己 | `has-text('仅限自己')`、`el => el.closest('label')`、`: 已选择` |
| 设置保存 | `div[aria-label='保存分享对象选择并关闭对话框']`（原串：`保存隐私分享对象选择并关闭对话框`） |
| 失败语义 | `: 修改签名失败(继续下一步)`、`: 修改发帖设置失败(继续下一步)` |
| 主页名称（未做） | `https://www.facebook.com/settings?tab=profile&section=name&view`、`form[action='/settings/name_change_preview/']`、`#save_password`、`button[value='1'].layerConfirm` |

## 三、修掉的一个错实现（本步最重要的发现）

第一版把类别的改法写成了「点 `div[dir='auto']:has-text('戏剧')` 这个选项」。
写完回头核对证据串的顺序才发现搞错了：

> `div[dir='auto']:has-text('戏剧')` 出现在**跳转之后、点编辑之前**，
> 紧跟着的两句日志是「当前类别已是 / 当前类别不是」。它是**判断「当前类别是否已是目标」**用的，
> 点它等于点页面上展示当前类别的那段只读文本。
> 真正的改法是：点「编辑类别」→ 在 `input[role='combobox'][aria-label='类别']` 里输入关键词
> （原版先 `Control+a` 再 `Backspace` 清空）→ 从 `[role='option']` 下拉里选。

现在两个选择器分开：
`CategoryEditor.CurrentCategorySelector()`（判「已是」）与 `CategoryEditor.OptionSelector()`（选下拉项）。
反向注入第 6 条证明这两者**不能互换**：把判定换回下拉项选择器，6 条用例同时变红。

顺带补上「当前类别已是就跳过」这条分支 —— 它省的不是时间，是一次点击与一次保存：
每一步都在 FB 的视野里。

## 四、另外三处有意偏离原版

1. **`fill` 代替 `Control+a` + `Backspace` + 逐字输入**。`fill` 的语义就是「清空并填值」，
   一步等价；逐字敲在中文输入法下会多出拼音前缀（S1 踩过）。
2. **类别页三个地址做成参数**（`IntroUrlTemplate` / `CategoryUrlTemplate` / `PostingSettingsUrl`）。
   理由与 S7 的 `ComposerUrlTemplate` 相同：本机 443 不通，地址写死则「导航」这段在测试里只能被跳过，
   而「导航之后页面能不能操作」正是最容易错的一段。默认值仍是官方地址，不是另一套逻辑。
3. **超长签名在导航前拦掉**。原版没这道校验，靠 `maxlength` —— 而 `maxlength` 是**静默截断**，
   不报错。程序会以为写进去了，结果页面上只有半句。所以 `SignatureText` 先判长度（255，与页面一致），
   超了直接判失败且**一次页面动作都不做**。

## 五、修掉的一个页面层缺陷（S6 遗留）

仿真页一跑就暴露：`IsVisibleAsync` 用的是 `.First`，而资料页里三个视图各有一个
`[aria-label='保存']`，排在前面的是隐藏的那个 → 判定「不可见」→ 类别改到一半报「未找到保存按钮」。

这不是仿真页造出来的假问题：真页面上「关掉了但没销毁」的弹窗会把同名元素留在 DOM 里，
于是**排在最前的匹配是隐藏的、可见的排在后面**。改成「任一匹配可见即可见」，
并补了守卫用例 `隐藏元素排在前面也判为可见`（反向注入第 4 条：改回 `.First` 立刻变红）。

## 六、反向注入（6 次，全部命中）

| # | 改坏什么 | 命中 | 表现 |
|---|---|---|---|
| 1 | 一项失败就整体中止 | 1 | `Expected: 2, Actual: 0`（`签名失败不中断后面两项`） |
| 2 | 等保存按钮时只探一次就放弃 | 9 | 集成测试大面积红（保存没点下去） |
| 3 | 类别值不做转义 | 1 | `类别名带双引号也能命中` 红 |
| 4 | `IsVisibleAsync` 改回只看 `.First` | 1 | `隐藏元素排在前面也判为可见` 红 |
| 5 | profile id 先宽松后完整 | 1 | `完整链接优先于先出现的相对链接` 红（取到了别人的 id） |
| 6 | 「当前类别」判定改用下拉项选择器 | 6 | 跳过分支失效，6 条同时红 |

第 2 次值得单说：第一次注入写的是「不等解禁，直接点」，结果**全绿** ——
因为 Playwright 自己的 click 会等按钮变成可操作。也就是说那条用例防的不是「直接点」，
而是**「根本没等」**。改成「只探一次就放弃」才把这个行为压住。
这件事本身有价值：它说明「Playwright 会不会替我们等」不能靠猜，得测。

## 七、未完成项（不许写成已完成）

1. **真实 Facebook 页面一条都没验。** 全部结论来自本地仿真页 + 单元测试。
   仿真页证明「选择器与动作在当前 Chrome 里成立」，证明不了「FB 现在还是这个 DOM」。
2. **主页名称没做。** 它要进 iframe 找 `form[action='/settings/name_change_preview/']`，
   提交后还要主账号密码（`#save_password` + `button[value='1'].layerConfirm`）二次确认。
   硬塞进来会让这一步的验收标准变成「六项都过」，而这一项的失败原因（权限/审核/密码）与前三项完全不同。
   地址常量已留在 `FacebookUrls.NameSettings`。
3. **头像/背景图没接。** 复用 S5/S6 的换图流程（`CoverAttemptPlanner` + 拒绝弹窗处理），
   尺寸预校验也在那边。同样单独做。
4. **「仅限自己」的定位可能与真页面不一致。** 原版是 `has-text('仅限自己')` 找到元素后
   `closest('label')` 再点；这里用 `div[role='button']:has-text('仅限自己')`。
   若真页面是 `label` 包 radio，可能命中不到 —— 真机第一件事就是确认这条。
5. **`div[dir='auto']:has-text(...)` 判「当前类别已是」有误判风险。** `:has-text` 是子串匹配且祖先命中，
   页面上任何含该文本的 `div[dir='auto']` 都算命中。原版就是这么用的，风险一致；
   真机上若出现「类别名是别的类别的子串」（如「教育」与「教育机构」），这里会误判成「已是」而跳过修改。
6. **profile id 的来源没定论。** 证据里既有「账号ID为空,跳过类别修改」（说明 id 来自账号表），
   也有 `https://www.facebook.com/` + `a[href*='profile.php?id=']`（说明会去主页取）。
   现在的实现是「已知 id 优先，未知则打开主页解析」，两条路都留着。真机要确认账号表里的
   「账号ID」到底是不是业务主页 id。

## 八、验收对照

| 判据 | 结果 |
|---|---|
| 三项都被执行 | 集成断言三项 `Changed`，日志含「签名已输入」「已选择类别」 |
| 已有签名走「编辑个性签名」入口 | 断言页面 `__entry == 'sig-edit'` |
| 没有签名走「关于你」入口 | 断言 `__entry == 'sig-about'` |
| 保存按钮延迟解禁仍能保存 | 延迟 500ms，断言 `__signatureSaved == true` |
| 没有保存按钮按自动保存 | `Changed` 且日志含「自动保存」 |
| 超长签名一次页面动作都不做 | 断言 `NavigatedUrls` 为空 |
| 类别走搜索 + 下拉项 | 断言搜索框被填过、`__category == 教育` 且已保存 |
| 类别名带双引号能命中 | `说"唱"` 选中成功（转义生效） |
| 当前类别已是则跳过 | `Skipped`，断言 `__categorySaved == false` |
| 发帖设置改「仅限自己」并保存 | 断言 `__posting == 仅限自己` 且 `__postingSaved` |
| 一项失败不中断后续 | 签名找不到入口 → 类别/发帖仍 `Changed`，整体 `Success == false` |
| profile id 从主页解析 | 日志含「已获取主页 profile id: 100012345678」，三项照改 |
| 主页无 profile 链接 | 整条失败，且 URL 没离开登录页 |
| 执行器不再落 NotWired | `ExecutorCatalogTests` 五条 |

## 九、文件清单

新增（产品）：

- `src/FbAuto.Automation/Selectors/FacebookUrls.cs` — 页面地址目录（去哪儿）
- `src/FbAuto.Automation/Profiles/ProfileIdExtractor.cs` — 两级正则取 profile id
- `src/FbAuto.Automation/Profiles/ProfileIdResolver.cs` — 导航主页 + 读整页
- `src/FbAuto.Automation/Profiles/SignatureText.cs` — 长度/空值校验
- `src/FbAuto.Automation/Profiles/SignatureSource.cs` — 读签名 txt
- `src/FbAuto.Automation/Profiles/ProfileFieldResult.cs` — 单项结论 + 调参（含等按钮解禁）
- `src/FbAuto.Automation/Profiles/SignatureEditor.cs` — 两个入口分支
- `src/FbAuto.Automation/Profiles/CategoryEditor.cs` — 已是判定 + 搜索 + 下拉项
- `src/FbAuto.Automation/Profiles/PostingSettingsEditor.cs` — 行按钮 → 仅限自己 → 保存
- `src/FbAuto.Automation/Profiles/ProfileEditFlow.cs` — 编排（失败不中断，汇总仍判失败）
- `src/FbAuto.Application/Execution/ProfileEditExecutor.cs` — 执行器

改动：

- `FacebookSelectors` 补 9 条资料修改选择器（含 `CategorySearchInput`）
- `ProfileEditOptions` 加三个地址模板（默认仍指官方地址）
- `PlaywrightPageInterop.IsVisibleAsync` 改为「任一匹配可见即可见」（第五节）
- `ExecutorCatalog` 注册 `ProfileEditGoogle`
- `AppConfig` 加 `SignatureFile` / `ProfileCategory` / `UpdatePostingSettings`
- `FakePageInterop` 所有方法检查取消令牌（假实现忽略 token 会造出「取消后照样做完」的假绿）

新增（测试）：

- `ProfileEditTests`（53）：profile id、签名校验、签名文件、两个选择器、三个编辑器、编排
- `ProfileEditFlowIntegrationTests`（12）：真 Chrome + 本地仿真页
- `FacebookMimicPage.ProfileSettings`：一份 HTML 按 query 渲染三个视图（三次导航都真的执行）

测试规模：431 → **497**（+66），全绿约 1 分 50 秒。
