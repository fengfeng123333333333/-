# 踩坑清单（FbAuto）

这份文件是本项目所有「已经付过代价」的坑，按领域分组，末尾标注发现的阶段。
**新增一条坑的门槛**：能说出「做了什么 → 现象是什么 → 为什么不是别的原因」。
只写结论不写现象的记录，下次遇到时认不出来。

从 `S13` 起，本文档是唯一权威版本；`.workbuddy/memory/MEMORY.md` 只留索引。

---

## 一、Playwright / 页面

- **动作超时抛的是 `System.TimeoutException`，不是 `PlaywrightException`**（S6）。
  只 catch 后者会把英文 `Timeout 30000ms exceeded` 原样写进任务日志。统一走 `IsPageFailure`（两者都认，取消异常除外）。
- **`aria-disabled='true'` 会让 click 一直等到超时**（S6 实测 1.41，日志 `element is not enabled - waiting...`）。
  所以自写的「可用性判定」必须与它同口径，否则决策器会挑一个点不动的按钮，每条任务白卡一个超时。
  反向看：**Playwright 的 click 自己会等解禁**，所以「不等解禁直接点」在真浏览器里照样成功——拿它做注入不会变红。
  想压住「有没有等」这个行为，注入要写成「只探一次就放弃」。
- **真页面的 file input 一律 `display:none`**（S7）。先等可见必然超时（日志会打
  `locator resolved to hidden <input type="file">`）。`SetInputFilesAsync` 不需要可见，别加等待。
  **批量必须一次传数组**：循环调单文件只会留下最后一个（每次 change 都重置待传卡片）。
- **`InnerTextAsync` / `GetAttributeAsync` 默认会等元素出现（30 秒）**（S7）。
  轮询「正在消失」的元素（进度条到 100 就没了）必然踩。读操作必须传 `Timeout = 0`，
  并把 `TimeoutException` 当「刚好消失」跳过——否则一轮采样卡 30 秒，任务判超时。
- **`:has-text()` 是子串匹配且祖先也命中**（S8）。`div[role='button']:has-text('移除')` 会连带命中「移除视频」。
  按序号点击前必须确认页面上只有一种同名按钮。
- **`IsVisibleAsync` 不能只看第一个匹配**（S8 实测）。真页面上「关掉了但没销毁」的节点会留着，
  一组同名元素里排最前的可能是隐藏的。用 `.First` 判定会得到「不可见」而后面明明有可见的
  （三个视图各一个 `[aria-label='保存']`，报「未找到保存按钮」）。已改成「任一匹配可见即可见」，
  守卫用例 `隐藏元素排在前面也判为可见`。
- **`--remote-debugging-port=0` 只能靠 `DevToolsActivePort` 文件拿端口**（S2），启动前必须删残留文件。
  **绝不按进程名杀 `chrome.exe`**：只按自己启动的 PID（含子进程）关。
- **frame 内操作必须按「URL 含片段」定位，且密码/确认按钮要回主 frame**（S13）。
  主页名字的表单在含 `cquick` 的 iframe 里，而 `#save_password` 与 `layerConfirm` 在主 frame。
  两套原语串用的表现是「未找到密码输入框」，流程随即以为没配密码直接点确认，日志看着一切正常。
  已加 `IPageInterop` 的 frame 四件套（GetFrames / FrameExists / FrameFill / FrameClick）。
  **等 frame 要连元素一起等**：FB 设置页先出壳再挂 iframe，只等 frame 会在「frame 在、元素还没到」白等一个超时。
  定位口径不取序号（随加载顺序变）也不取 name（FB 上基本为空）。
- **遍历 `_page.Frames` 读 URL 会抛**（S13，导航中 frame 被移除）：逐个 try，跳过读不到的，
  不让整份 frame 清单打印失败。

## 二、流程设计

- **循环类流程必须有与业务判断无关的轮次硬上限**（S9）。
  删除消息的出口是「达上限 / 列表空 / 连续失败 / 轮次上限 / 无进展」。
  去掉任意一个再配上「列表数量不降」就是死循环——注入实验里它让测试进程**挂死**（零输出，不是失败）。
  任何「点一下 → 等一下 → 再点」的循环，都要有一条与业务判断无关的绝对刹车。
- **虚拟滚动列表不能用「数量下降」判进展**（S9）。Messenger 删一条会补一条，渲染行数本来就不变；
  按数量判停会变成「删两条就停」。该判断默认关闭，只在真机确认列表不是虚拟滚动后再打开。
- **`UploadProgressMonitor` 的「无进度条」分支必须自带超时**（S7 埋、S10 挖出）。
  原实现该分支直接 `return Waiting`，而总超时检查写在它**后面**，永远执行不到 → 监控可以无限等，
  真机卡十几分钟无日志，测试里是进程挂死、零输出。已加 `NoBarTimeout`（默认 90 秒）。
  **修的时候别把总超时提到所有分支之前**——会把「进度条消失=完成」压成失败，
  用例 `进度条消失优先于总超时_传完了就不该判失败` 会红。正确顺序：完成判定优先，超时只管「还在等」的分支。
- **「输入即打标签」的框必须按回车**（S12）。只 `FillAsync` 不 `PressKeyAsync("Enter")`，
  词留在框里保存后一起丢，而日志照样打「已添加屏蔽词: x」。
  用 `Locator.PressAsync`（自带聚焦），不能用页面级 `Keyboard.PressAsync`——那打在没有焦点的页面上。
  测试要守「先填后按」的**先后**：`FakePageInterop.Timeline` 记 `fill:x` / `press:Enter` 交错，
  光数两个表的长度看不出顺序反了。
- **假页面实现（FakePageInterop）必须检查取消令牌**（S9）。假实现忽略 token 会造出
  「取消之后照样做完」的假绿，取消语义的用例永远红不了。所有取 token 的方法都要 `ThrowIfCancellationRequested()`。
- **守卫用例必须配非空反例**（S11 记过一次、S13 又犯）。`ChannelProfileTests` 里「比特版不读谷歌版」
  的图片目录断言，两边都是空串时注入「回落」打不红。写完先想「怎么才能让它红」，
  做法改成「写完守卫用例先做一次注入，再声称写完」。

## 三、证据与命名

- **`docs/evidence/extracted/*.cn.txt` 是脏文件**（S0 起）。里面的中文与选择器是 UTF-16LE 字节
  被按单字节解出来的（显示成 `慦嬀栀爀攀昀⨀...`），在里面 grep 关键词几乎什么都找不到。
  要找原版串必须回 `_orig/` 的原始 DLL 按 UTF-16LE 重扫，结果见 `_analysis/u16_all.txt`。
- **方法名归属按类名前缀看，别按名字猜**（S7）。`CheckCoverSizeErrorAsync` 名字像视频上传，
  实际在 `ProfileEditService`（资料修改的封面/背景图）。
- **键名叫 Comments 不代表它是「评论」功能的**（S11 错一次、S12 又错一次）。
  `GoogleCommentsBlockText` / `BitCommentsBlockText` 是**资料修改 → 粉丝公开内容的关键词**。
  第一次当成「删除消息的屏蔽词」，第二次当成「评论屏蔽词」放进视频上传页。
  证据是同段串里「跳转到粉丝公开内容设置页...」+ `textarea[id='CometUSFTagInputFreeformDialogTagEditor']`
  + 默认值 `.com https pn dn inbox`（与配置默认值一字不差）。
  **必须回串看它出现在哪一段，不能照字段名想当然。**
- **定「执行顺序」不能拿字符串堆里两段串比先后**（S11 埋、S12 挖出）：堆的顺序是方法体书写顺序。
  要找「每项一条」的分组串（原版资料修改是那串「已跳过【修改X】」），那个才是分发顺序。
  正确顺序是 **背景图→头像→发帖→粉丝公开内容→签名→类别→主页名字**。
- **原版配置是「按通道成对」的**（S10）：`GoogleVideoPath`/`BitVideoPath`、`GoogleSignatureFilePath`/`BitSignatureFilePath`……
  共 8 对，S13 加名称文件与密码成 10 对；共享的只有 `CookieRootPath`、`DeleteMsgCookieRootPath`、
  `BrowserOpenInterval`、`ConcurrentTaskCount`。**页面流程没有比特版差异**（搜「比特」只命中服务层日志）。
  取配置统一走 `AppConfig.ForChannel`，别在执行器里直接读字段。
- **`[删除消息]` 跟随界面「浏览器类型」下拉，不是固定谷歌版**（S10）：串里有
  `正在获取比特浏览器列表（删除消息）...`。写死谷歌版的后果是比特版账号永远删不了消息，
  界面提示「没有可用账号」，看着像账号清单错了。已加 `TaskTypeDescriptor.FollowsBrowserType` + `TaskTypeCatalog.ChannelFor`。
- **「删除消息」原版没有任何页面串**（S9）：只有任务名 + 三句生命周期日志 + 一个 `DeleteMsgCookieRootPath`。
  无法仿制，只能自建；四条选择器（会话行 / 会话详情 / 删除对话 / 二次确认）全按 FB 现行 DOM 惯例写，
  **真机必须逐条重验**。

## 四、工程 / 配置 / 测试

- **`.gitignore` 的 `profiles/` 在 Windows 上把源码目录一起吃掉了**（S8 埋、S11 收尾才发现，本项目最严重的一次）。
  `core.ignorecase=true`，所以 `profiles/` 不分大小写命中了 `src/**/Profiles/`。
  后果：**S8 的资料修改 9 个源码文件从来没进过仓库**，`git ls-tree HEAD` 里该目录 0 个文件，
  而 `ProfileEditExecutor.cs` 第 2 行就 `using FbAuto.Automation.Profiles;`——**HEAD 编译不过**，
  且 `git status` 上一切干净，看不出异常。已改 `/profiles/`（锚定根目录），
  并把 `**/Profile*/` 换成 `**/Profile [0-9]*/`（Chrome 的 Profile 1 **中间有空格**）。
  **教训：`git status` 干净不等于该提交的都提交了**，收尾要用 `git ls-files` 与磁盘对账：
  ```bash
  find src tests -name '*.cs' -not -path '*/obj/*' | while read f; do git check-ignore -q "$f" && echo "$f"; done
  ```
- **逐任务取 `AccountImagePlan` 时下标恒为 0，所有账号会拿到同一张图**（S11）。
  它的设计前提是「一次拿到全部账号」，按名字在列表里的下标取图；执行器逐任务跑，列表只有一个元素。
  已改成「洗牌种子 = 别名的稳定哈希（FNV-1a，取低 31 位）」，S13 抽成共用 `Support/StableHash.cs`
  （头像与名字都按别名分配，两处各写一份迟早算出不同值，表现为「同一账号的头像与名字对不上人」）。
  **不要用 `string.GetHashCode()`**（每进程都不同，断点续跑会换图）。
  这是「大概率不同」不是「保证不同」，严格不重复要走批量入口。
- **名字文件去 BOM 用 `Replace` 而非 `TrimStart`**（S13）：BOM 可能写在空白之后（`\uFEFF  名字`），
  `TrimStart` 只吃紧贴行首的那个，残留空格会被写进页面上的名字。
- **头像的 file input 只在「上传照片」对话框里**（S11）。被拒一次对话框就关了，
  **重试必须重开菜单**（点 `[aria-label='头像操作']` → 点「选择头像」），否则注第二张必然「找不到元素」超时。
  已加 `CoverAttemptFlow.RunForAvatarAsync(prepareAsync)`，每轮注入前调用；
  背景图的 input 直接挂主页上，**不要给它传这个钩子**（会多点两次菜单）。
- **xunit 里启动浏览器的测试类必须同属 `[Collection("chrome")]`**（S7）。漏一个就会出现
  「单独跑全绿、全量跑零输出挂死」，因为两个类并行各起一个 Chrome（每个十几进程）互相拖死。
  集合定义见 `tests/.../Support/ChromeCollection.cs`。
- **排查挂起不要直接杀 testhost**（S13）：会留下 `--type=renderer` 孤儿进程，下一轮 `LaunchAsync` 被拖住，
  表现成「又挂了一次」，实际是自己上一轮弄脏的。要么让 blame 跑完（它会自己清理），
  要么先记下 Chrome PID 事后按 PID 清。**先清残留 → 再重跑**，不要边跑边杀。
  （另一条同类经验：单进程跑 611 条 2m58s 全绿，「要拆两段跑」的说法是错的，那是残留造成的假象。）
- **xunit 的 `Assert.DoesNotContain(string, string)` 走文化比较，会「忽略」U+FEFF**（S13）。
  `Assert.DoesNotContain("\uFEFF", 干净字符串)` 报「在位置 0 找到」——U+FEFF 是零权重字符，
  文化比较下对任何串都成立。断言「不含零宽字符」一律用 `Assert.False(s.Contains('\uFEFF'))`（char 重载 = 序数比较）。
- **WPF 激活成功后程序直接消失**（S1 埋、S3 暴露）：`ShutdownMode` 默认 `OnLastWindowClose`，
  必须先 `Show()` 主窗口再 `Close()` 旧窗口，顺序反了进程就退出。
- **新增 `FbAuto.Application` 命名空间后 `App.xaml.cs` 里裸写 `Application` 报 CS0118**，基类必须写
  `System.Windows.Application`；XAML 注释不能出现连续 `--`（MC3000）。
- **原始字符串插值：JSON 结尾 `}}` 会被 `$$"""` 当成插值定界符**（CS9007），用 `$$$"""` + `{{{ }}}`。
- **`build/*.cmd` 必须纯 ASCII，不能有中文**（S5）。cmd.exe 按系统 ANSI 码页（936）解析 `.cmd`，
  UTF-8 中文会被解成乱码并切断行，报 `'笉渚濊禆绯荤粺' 不是内部或外部命令`。
  5 个脚本已全部改英文注释，新增脚本照此办理；`new-profile.cmd` 生成的启动器名也用 `start-<别名>.cmd`，
  中文文件名同样会被弄乱。
- **pywinauto 对 WPF 输入框赋值不可靠**（S3）：`set_text` / `iface_value.SetValue` 在中文输入法下
  会多出 `zhen`、`2` 之类前缀。验收脚本改用剪贴板 + `send_keys("^v")`，且先 `set_focus()`。
  **UIA 点按钮用 `invoke()` 不要用 `click_input()`**：坐标点击依赖窗口焦点，窗口刚起来时会点空
  （表现是「点了没反应、日志一行没有」）。**定位列表必须按 `x:Name`**：全窗口搜 `DataItem`
  会把左栏 `ItemsControl` 的任务类型当成队列行。
- **改完代码只 build 测试项目不算**：Presentation 输出目录还是旧 DLL，真机验收会跑带 bug 的版本。
  真机验收前必须 `build FbAuto.slnx`。
- **原版只暴露 `WriteAllText`，不代表它的行为就是「整体覆盖」**（S14）。字符串证据里只有
  `WriteAllText`、没有 `AppendAllText`，据此写成覆盖会有一个具体后果：同一天跑两轮，
  第一轮的失败记录被抹掉。并发任务逐个「读-改-写」还会互相覆盖。
  最终做法是**一轮攒够、轮末一次写、写前把当天已有内容接在前面**，并在文档「未完成项」里
  写明「与原版可能不同，我们只会多留不会丢」。
- **「原版有键名」不等于「原版有值」**（S14）。`AccountErrors_`、`[账号失败] 浏览器: | assetId: | 原因:`
  都有字面量，但**扩展名 `.txt` 没有独立字面量**（只有文件对话框的 `*.txt` 过滤器）。
  这种情况下要么找到独立证据，要么按最合理推断实现并**逐条写进「未完成项」**，不能当已确认写。
- **一轮执行可以混跑谷歌版和比特版，导出目录必须按通道查表**（S14）。配置层是成对的
  （`Google/BitProfileEditResultExportDir`），`TaskRunOptions` 只给单一目录的话，
  两种通道的失败会写进同一个文件，事后按通道对账就做不到了。
- **连着 6 个 bool 的位置传参错位时，任何单测都发现不了**（S14）。`ProfileEditRequest` 有 14 个参数，
  改成**具名实参**后错位在结构上不可能发生 —— 比多写几条断言更省事、更可靠。
- **注入还原之后必须重新 build，"`--no-build` 跑全量等于在查上一个注入"**（S14）。
  注入 I7 还原源码后直接 `--no-build` 跑，`没配目录时记了也不落盘` 报 Expected 0 / Actual 1，
  差点被当成产品缺陷去改。判据很简单：**报错位置正好是刚才注入的那一行**，先怀疑 DLL 过期。
- **注入后没变红，先怀疑「代码真的编译进去了没有」，再怀疑用例**（S15）。J1 注入后 9 条全绿，
  原因是编译命令写成了 `./.dotnet/dotnet.exe`（少一层目录，`build/*.cmd` 的 SDK 根是 `..\..\.dotnet`），
  编译压根没跑；而 `grep "error CS"` 又没有输出，看起来「编译成功」。判据：**注入后编译必须有产物时间变化**，
  最稳的是统一用 `build\dotnet.cmd build FbAuto.slnx`，别在 bash 里手拼 SDK 路径。
- **文本级守卫分不清「写了」和「写着但注释了」**（S15）。`ProfileEditBindingTests` 检查
  `PersistConfig` 是否写回每个绑定，把 `config.BitProfileCategory = ...;` 注释掉之后用例照样绿 ——
  注释行里仍含 `config.Xxx =`，正则照样匹配。**守卫要先剥注释再匹配**，否则它恰好在最该管用的
  那一次不管用。这条是被注入 J5 逼出来的：写完守卫就该立刻注入一次，别等「以后」。
- **配置键存在 ≠ 界面能填**（S15）。`ProfileCategory` / `BitProfileCategory` / `BitCommentsBlockWords`
  三个键一直在 `AppConfig` 里，但 `MainWindow.xaml` 从来没有它们的绑定，
  于是「修改类别」勾上之后永远走到「类别为空，跳过该项」—— 开关是开的、日志正常、那一项从没生效。
  审计办法：把 `AppConfig` 的属性名与 XAML 的 `{Binding ...}` 名对一遍，
  再对一遍 `PersistConfig` 里的 `config.Xxx =`。三者缺一都是静默失效。
- **「原版没有的东西」不要想当然按「不改」处理**（S15）。原版把类别硬编码成「戏剧」
  （`has-text('戏剧')`、`未找到【戏剧】选项`、`已选择【戏剧】`，且 settings.json 22 个键里没有类别）。
  做成可配置之后，**留空必须回落那个词，不能理解成「跳过」** —— 跳过等于让开关空转。
- **信号量「拿到了没有」不能用引用非空判断**（S16）。`SemaphoreSlim.WaitAsync(0, token)`
  在令牌已取消时会直接抛 `OperationCanceledException`，此时闸门对象早已被 `TryGetValue` 赋了值
  却没拿到锁；`finally` 里只看非空就 `Release()` 会抛 `SemaphoreFullException`
  （`Adding the specified count to the semaphore would cause it to exceed its maximum count`），
  而且这条异常从 `finally` 冒出去会把整轮 `Task.WhenAll` 打挂。
  **必须单独记一个 `heldXxx` 布尔**。这个 bug 是 S16 第一版代码里真实存在的，被新用例抓住后
  又用注入 M5 复现了一次。
- **同账号并发会互相打死浏览器**（S16）。两条同账号任务各启一个 Chrome 指同一个 `user-data-dir`：
  Chrome 把第二个实例的启动请求转发给第一个之后自己退出，而启动前那句「清残留
  `DevToolsActivePort`」删的是**第一个实例正在用的**文件 —— 先开的读不到端口、后开的报
  「启动后立即退出」，两条一起废。调度层必须按账号串行，且**账号锁要放在并发槽之前**，
  否则会占着并发槽干等，并发形同虚设。
- **静默吞异常的地方必须落盘断言**（S16）。失败截图是故意吞异常的（截图失败不该让任务崩溃变程序崩溃），
  代价是它坏掉时只有一句 `Warn`。所以它的用例不能只断言「返回路径不为 null」，
  要真写文件、数目录里的文件个数，否则「两条任务覆盖成一张图」这种情况永远测不出来。
- **WPF 的绑定失败是静默的，界面自检是唯一的运行期证据**（S17）。`Command="{Binding Xxx}"` 里
  属性名写错、命令没生成，不抛异常、不报编译错误 —— 按钮照样显示、有按下动画、点下去什么都不发生。
  文本级守卫只能证明「XAML 里写了这几个字」。所以 `MainWindow.Loaded` 里遍历可视化树数一遍
  「选择...」按钮有没有 `Command`，把结果写进日志。S17 的注入 N8 证明了这条自检不是永远绿：
  摘掉一个按钮的命令后日志立刻变成「应有 15 个…实际 15 个、其中 14 个接了命令」。
- **逻辑树里混着非 Visual 的元素**（S17）。`LogicalTreeHelper.GetChildren` 会返回
  `RowDefinition` / `ColumnDefinition`，把它们直接递给 `VisualTreeHelper.GetChildrenCount`
  会抛 `InvalidOperationException: 「…RowDefinition」不是 Visual 或 Visual3D`，
  而且是界面线程未处理异常。遍历前先判 `node is Visual or Visual3D`。
  另外逻辑树与可视化树大量重叠，必须记访问过的节点，否则同一棵子树被反复遍历。
- **同一页的「数据绑定」守卫会把命令绑定也算进去**（S17）。`ProfileEditBindingTests` 用
  `{Binding (\w+)}` 收集绑定名，接上「选择...」按钮之后它开始要求「PickPathCommand 要写回 config.json」。
  命令是行为、不是配置值，正则加 `(?<!Command=")` 排除。
- **Chrome 集成测试在「全量跑」时会偶发卡死，单跑该类却基本正常**（S14，尚未定位根因）。
  症状：`--blame --blame-hang-timeout 90s` 会点名 `VideoUploadFlowIntegrationTests` 里的某一条，
  但不固定是哪一条（停过的页面有 `upload.html` / `upload-nodesc.html` / `upload-schedule.html`），
  已排除残留 Chrome、残留临时目录、残留 testhost。单跑该类 5 次里 4 次全绿、1 次以
  `Assert.True(result.Published)` 失败 —— 抖动而非确定性缺陷。
  需求方 2026-11-16 指示「需要调 Chrome 的测试暂时先不用」，日常改用 `build\test-nochrome.cmd`
  （排除 7 个类共 63 条，654 条 5~6 秒全绿）。**恢复前必须先定位它**，否则全量结论不可信。
  排查入口：`tests/FbAuto.Tests/TestResults/<guid>/Sequence_*.xml` 里找唯一
  `Completed` 不为 `True` 的那条。
- **「开发期跳过激活」这类开关不能放在可编辑的配置文件里**（S18，交付前发现）。
  `SkipActivationInDevelopment` 原本默认 `true` 且从 `config.json` 读，打成发布包之后
  里面就写着 `true` —— 拿到包的人改都不用改，不输授权码就能用，签名/密钥/机器绑码全部作废。
  修法是把闸门放到**编译期**（`ActivationGate.BuildAllowsSkip` 在 Release 下恒 `false`），
  配置里那个键只对 Debug 构建生效。教训：**凡是「为方便开发而留的后门」，判定必须依赖
  构建类型，不能依赖运行时能改的文件**；而且「发布前记得改成 false」这种口头约定一定会忘。
- **自包含发布包里 `license.dat` 不能跟着走**（S18）。凭证密钥由机器码派生，换机器解不开，
  带了也没用；而 `.playwright\` 目录必须跟走，缺了页面操作全废（但报错是「浏览器起不来」，
  不会直说驱动缺失）。`build\publish.cmd` 已经把这两条写进注释。

## 五、环境（本机特有）

- 本机无系统 .NET SDK，SDK 在仓库外一层 `D:\AI\workbuddy\短视频工具\.dotnet`，
  三个脚本的 SDK 根都指 `..\..\.dotnet`；Bash 里调用需自设 `DOTNET_ROOT`，
  且**不要**用 bash 跑 `.cmd`（cmd 脚本会被逐行当命令执行）。
- `facebook.com:443` 超时（DNS 正常），需境外出口才能做真机页面验收——这也是全项目页面验收
  都靠本地仿真页的原因。
- 比特浏览器未安装，`127.0.0.1:54345` 无服务；比特通道目前靠裸 `TcpListener` 桩验证。
- 系统注入 `HTTP(S)_PROXY=127.0.0.1:54802`：**所有本机探针（Chrome `/json/version`、比特接口）
  必须 `UseProxy=false`**，否则连 127.0.0.1 都会走代理。
- 非管理员下 `HttpListener` 对 `127.0.0.1` 前缀会拒绝监听，测试桩一律用 `TcpListener`。
- 工具会话拉起的 GUI 程序在命令结束时被回收：启动与 UIA 采集必须放同一条命令内。
