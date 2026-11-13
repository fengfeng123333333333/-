namespace FbAuto.Tests.Support;

/// <summary>
/// 本地仿真页：用**真实选择器**搭一份仿 Facebook 的 DOM，让四件套能在真 Chrome 里跑完整流程。
/// </summary>
/// <remarks>
/// 为什么值得造这东西：本机到 facebook.com 的 443 不通，真页面验收做不了。但「选择器能不能命中」
/// 「隐藏的 file input 能不能注入」「点击序号会不会点到隐藏元素」这些问题是**跟域名无关**的 ——
/// 只要 DOM 结构与属性跟真页面一致，本地页就能把它们全测出来。
///
/// 结构不是随便写的，每一处都对着原版提取串：
/// <list type="bullet">
/// <item>封面 file input 必须是 `div[role='button']:has-text('封面照片')` 的**兄弟**，
/// 因为选择器是 <c>div:has(> div[role='button']:has-text('封面照片')) &gt; input[type='file']</c>。</item>
/// <item>拒绝提示必须是 <c>div[aria-label='请选择其他封面照片'][aria-modal='true'][role='dialog']</c>，
/// 三个属性少一个都命不中。</item>
/// <item>file input 一律 <c>display:none</c> —— 真页面就是这样，先等可见会等到超时。</item>
/// </list>
///
/// **这不能替代真机验收**。它证明的是「动作与选择器在真浏览器里成立」，证明不了
/// 「Facebook 现在的 DOM 还是这个样子」。后者只能等出口通了再验。
/// </remarks>
public static class FacebookMimicPage
{
    /// <summary>写一个本地页并返回绝对路径。</summary>
    public static string Write(string directory, string fileName, string html)
    {
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, html);
        return path;
    }

    /// <summary>
    /// 封面编辑页：file input 隐藏，选中后按文件名决定「被拒」还是「接受」。
    /// </summary>
    /// <param name="withCloseButton">
    /// 拒绝对话框里是否带「关闭」按钮。传 false 用来验证「找不到关闭按钮就按 Escape」那条路。
    /// </param>
    public static string CoverEditor(bool withCloseButton = true)
    {
        var closeButton = withCloseButton
            ? """<div aria-label="关闭" role="button" id="cover-close">关闭</div>"""
            : string.Empty;

        return $$"""
            <!doctype html>
            <html lang="zh-CN">
            <head><meta charset="utf-8"><title>封面编辑</title></head>
            <body>
              <div class="cover-wrap">
                <div role="button" aria-label="编辑封面照片" id="edit-cover">封面照片</div>
                <input type="file" id="cover-input" accept="image/*" style="display:none">
              </div>

              <div id="cover-dialog" aria-label="请选择其他封面照片" aria-modal="true" role="dialog"
                   style="display:none;position:fixed;top:40px;left:40px;border:1px solid #888;padding:12px">
                <div>请选择其他封面照片</div>
                {{closeButton}}
              </div>

              <script>
                window.__uploads = [];
                window.__accepted = null;
                window.__escaped = false;
                window.__clicks = 0;

                var input = document.getElementById('cover-input');
                var dialog = document.getElementById('cover-dialog');

                input.addEventListener('change', function (e) {
                  var name = e.target.files && e.target.files.length ? e.target.files[0].name : '';
                  window.__uploads.push(name);
                  if (/reject/i.test(name)) {
                    dialog.style.display = 'block';
                  } else {
                    window.__accepted = name;
                  }
                });

                var closeBtn = document.getElementById('cover-close');
                if (closeBtn) {
                  closeBtn.addEventListener('click', function () {
                    window.__clicks = window.__clicks + 1;
                    dialog.style.display = 'none';
                  });
                }

                document.addEventListener('keydown', function (e) {
                  if (e.key === 'Escape') {
                    window.__escaped = true;
                    dialog.style.display = 'none';
                  }
                });
              </script>
            </body>
            </html>
            """;
    }

    /// <summary>
    /// 头像编辑页：结构与封面页相同，只是 file input 挂在 <c>div[aria-label='上传照片']</c> 下面。
    /// </summary>
    /// <remarks>
    /// 单独一个页而不是给封面页加参数：两处的选择器完全不同（
    /// <c>div[aria-label='上传照片']</c> vs <c>div[role='button']:has-text('封面照片')</c>），
    /// 共用一份 HTML 会让「选择器写错」这件事测不出来 —— 反正都能命中。
    /// </remarks>
    public static string AvatarEditor()
    {
        return """
            <!doctype html>
            <html lang="zh-CN">
            <head><meta charset="utf-8"><title>头像编辑</title></head>
            <body>
              <div role="button" aria-label="头像操作" id="avatar-area">头像</div>

              <div class="avatar-wrap">
                <div aria-label="上传照片">上传照片</div>
                <input type="file" id="avatar-input" accept="image/*" style="display:none">
              </div>

              <div id="avatar-dialog" aria-label="请选择其他封面照片" aria-modal="true" role="dialog"
                   style="display:none;position:fixed;top:40px;left:40px;border:1px solid #888;padding:12px">
                <div>请选择其他照片</div>
                <div aria-label="关闭" role="button" id="avatar-close">关闭</div>
              </div>

              <script>
                window.__uploads = [];
                window.__accepted = null;
                window.__escaped = false;

                var input = document.getElementById('avatar-input');
                var dialog = document.getElementById('avatar-dialog');

                input.addEventListener('change', function (e) {
                  var name = e.target.files && e.target.files.length ? e.target.files[0].name : '';
                  window.__uploads.push(name);
                  if (/reject/i.test(name)) {
                    dialog.style.display = 'block';
                  } else {
                    window.__accepted = name;
                  }
                });

                document.getElementById('avatar-close').addEventListener('click', function () {
                  dialog.style.display = 'none';
                });

                document.addEventListener('keydown', function (e) {
                  if (e.key === 'Escape') {
                    window.__escaped = true;
                    dialog.style.display = 'none';
                  }
                });
              </script>
            </body>
            </html>
            """;
    }

    /// <summary>
    /// 通知弹窗页：一个 <c>div[aria-label='关闭']</c>。禁用态用 <c>aria-disabled='true'</c> 表达。
    /// </summary>
    /// <param name="ariaDisabled">是否把关闭按钮标成禁用（点了没反应，只能靠 Escape）。</param>
    public static string NotificationPopup(bool ariaDisabled = false)
    {
        var disabledAttr = ariaDisabled ? """ aria-disabled="true" """ : string.Empty;

        return $$"""
            <!doctype html>
            <html lang="zh-CN">
            <head><meta charset="utf-8"><title>通知弹窗</title></head>
            <body>
              <div id="content">页面正文</div>
              <div id="popup" aria-label="关闭" role="button"{{disabledAttr}}
                   style="position:fixed;top:20px;right:20px;border:1px solid #888;padding:8px">关闭</div>

              <script>
                window.__clicks = 0;
                window.__escaped = false;
                var popup = document.getElementById('popup');

                popup.addEventListener('click', function () {
                  window.__clicks = window.__clicks + 1;
                  if (popup.getAttribute('aria-disabled') === 'true') {
                    return;
                  }
                  popup.style.display = 'none';
                });

                document.addEventListener('keydown', function (e) {
                  if (e.key === 'Escape') {
                    window.__escaped = true;
                    popup.style.display = 'none';
                  }
                });
              </script>
            </body>
            </html>
            """;
    }

    /// <summary>
    /// 没有弹窗、但资料编辑对话框开着。用来验证最危险的一条：**不该按 Escape 时绝不按**。
    /// </summary>
    /// <remarks>
    /// 对话框里的关闭控件刻意**不是** <c>aria-label='关闭'</c>，只是一个 <c>span</c>。
    /// 因为默认规则表里那条 <c>div[aria-label='关闭']</c> 是不分区域的（原版行为），
    /// 只要页面上有这么一个按钮就会被点。这条边界见 S6 文档「已知风险」。
    /// </remarks>
    public static string EditorDialogOpen()
    {
        return """
            <!doctype html>
            <html lang="zh-CN">
            <head><meta charset="utf-8"><title>资料编辑</title></head>
            <body>
              <div id="editor" role="dialog" aria-modal="true"
                   style="position:fixed;top:20px;left:20px;border:1px solid #888;padding:12px">
                <div>编辑资料</div>
                <input id="name" value="已经填好的内容">
                <span id="editor-close" role="button">关闭</span>
              </div>

              <script>
                window.__escaped = false;
                document.addEventListener('keydown', function (e) {
                  if (e.key === 'Escape') {
                    window.__escaped = true;
                    document.getElementById('editor').style.display = 'none';
                  }
                });
              </script>
            </body>
            </html>
            """;
    }

    /// <summary>
    /// 通用按钮页：用来测「可见序号」与「可用性判定」。
    /// </summary>
    /// <remarks>
    /// 顺序刻意是：一个隐藏的 → 三个可见的（第二个 aria-disabled）→ 一个原生 disabled 的按钮。
    /// 「隐藏的排在最前面」是故意的：按全部匹配数取序号会先拿到隐藏元素，表现是点击超时；
    /// 按可见数取序号才对。这条差异必须能被测出来，否则隐藏元素一直在偷偷吃掉序号。
    /// </remarks>
    public static string ButtonGrid()
    {
        return """
            <!doctype html>
            <html lang="zh-CN">
            <head><meta charset="utf-8"><title>按钮</title></head>
            <body>
              <div role="button" class="pick" data-id="hidden" style="display:none">选项0</div>
              <div role="button" class="pick" data-id="a">选项1</div>
              <div role="button" class="pick" data-id="b" aria-disabled="true">选项2</div>
              <div role="button" class="pick" data-id="c">选项3</div>
              <button id="native-disabled" disabled>不可用按钮</button>
              <button id="native-enabled">可用按钮</button>

              <script>
                window.__picked = [];
                window.__escaped = false;

                var picks = document.querySelectorAll('.pick');
                for (var i = 0; i < picks.length; i++) {
                  picks[i].addEventListener('click', function (e) {
                    window.__picked.push(e.currentTarget.getAttribute('data-id'));
                  });
                }

                document.addEventListener('keydown', function (e) {
                  if (e.key === 'Escape') {
                    window.__escaped = true;
                  }
                });
              </script>
            </body>
            </html>
            """;
    }

    /// <summary>
    /// 失败草稿列表页：每条一个「移除」按钮，点了弹二次确认（移除视频 / 继续）。
    /// </summary>
    /// <param name="count">可见条目数。</param>
    /// <param name="disabledPosition">第几个条目的移除按钮置为 aria-disabled（1 起），0 表示没有。</param>
    /// <param name="withHiddenLeadingRow">
    /// 是否在最前面插一条隐藏条目。默认插 —— 真页面关掉的对话框会留下隐藏的同名按钮，
    /// 序号若按全部匹配数取就会点到隐藏的那个。
    /// </param>
    public static string DraftList(int count, int disabledPosition = 0, bool withHiddenLeadingRow = true)
    {
        var rows = new System.Text.StringBuilder();

        if (withHiddenLeadingRow)
        {
            rows.Append(
                """<div class="row" data-id="hidden" style="display:none">"""
                + """<span>隐藏条目.mp4</span><div role="button" class="remove">移除</div></div>""");
        }

        for (var i = 1; i <= count; i++)
        {
            var disabled = i == disabledPosition ? """ aria-disabled="true" """ : string.Empty;
            rows.Append($$"""
                <div class="row" data-id="{{i}}">
                  <span>视频{{i}}.mp4</span>
                  <div role="button" class="remove"{{disabled}}>移除</div>
                </div>
                """);
        }

        return $$"""
            <!doctype html>
            <html lang="zh-CN">
            <head><meta charset="utf-8"><title>失败草稿</title></head>
            <body>
              <div id="list">
            {{rows}}
              </div>

              <div id="confirm" role="dialog" aria-modal="true"
                   style="display:none;position:fixed;top:40px;left:40px;border:1px solid #888;padding:12px">
                <div>移除这个视频？</div>
                <div role="radio"><span>移除视频</span></div>
                <div role="button" id="confirm-continue"><span>继续</span></div>
              </div>

              <script>
                window.__removed = [];
                window.__escaped = false;
                var pending = null;
                var confirmBox = document.getElementById('confirm');

                function bindRemove(row) {
                  var btn = row.querySelector('.remove');
                  btn.addEventListener('click', function () {
                    if (btn.getAttribute('aria-disabled') === 'true') {
                      return;
                    }
                    pending = row;
                    confirmBox.style.display = 'block';
                  });
                }

                var rows = document.querySelectorAll('#list .row');
                for (var i = 0; i < rows.length; i++) {
                  bindRemove(rows[i]);
                }

                document.getElementById('confirm-continue').addEventListener('click', function () {
                  if (pending) {
                    window.__removed.push(pending.getAttribute('data-id'));
                    pending.remove();
                    pending = null;
                  }
                  confirmBox.style.display = 'none';
                });

                document.addEventListener('keydown', function (e) {
                  if (e.key === 'Escape') {
                    window.__escaped = true;
                    pending = null;
                    confirmBox.style.display = 'none';
                  }
                });
              </script>
            </body>
            </html>
            """;
    }

    /// <summary>
    /// 资料设置页：**一份 HTML 按 query 渲染三个视图**（签名 / 类别 / 发帖设置），
    /// 另有一个默认视图当「我的主页」，供 profile id 提取用。
    /// </summary>
    /// <remarks>
    /// 为什么合成一份而不是写三份：流程里三项是**三次真实导航**（签名页 → 类别页 → 发帖设置页）。
    /// 一份页按 query 切视图，这三次导航就真的各执行一遍；写三份则要在一个测试里维护三个文件，
    /// 而且只要有一处的 query 拼错，测出来的就是「导航到了别的页」而不是「导航没发生」。
    ///
    /// 结构每一处对着原版串：
    /// <list type="bullet">
    /// <item>签名入口分两种：有签名是 <c>[aria-label='编辑个性签名']</c>，
    /// 没签名只有一个写着「关于你」的按钮 —— 真页面在没有签名时根本不渲染前者。</item>
    /// <item>签名框是 <c>textarea[dir='ltr'][maxlength='255']</c>，三个属性少一个都命不中。</item>
    /// <item>类别选项是 <c>div[dir='auto']</c>，选择器按文本拼（<c>:has-text</c>）。</item>
    /// <item>发帖设置那行的文本在 <c>span</c> 里，按钮是它的祖先 —— 点 span 不一定有用。</item>
    /// <item>三个保存按钮初始都带 <c>aria-disabled='true'</c>，改完内容才解禁：
    /// 这是真页面的行为，也是「不主动等就会卡一个点击超时」的来源。</item>
    /// </list>
    /// </remarks>
    /// <param name="hasExistingSignature">true 渲染「编辑个性签名」入口，false 只渲染「关于你」。</param>
    /// <param name="withSignatureEntry">
    /// 是否渲染签名入口。false 用来制造「签名一项失败」，从而验证后续项照跑。
    /// </param>
    /// <param name="withSignatureSaveButton">
    /// 签名框旁边是否带保存按钮。false 用来覆盖「按自动保存处理」那条分支。
    /// </param>
    /// <param name="withCategoryEntry">是否渲染「编辑类别」入口。false 用来制造单项失败。</param>
    /// <param name="currentCategory">
    /// 类别页上展示的「当前类别」文本。填成目标类别用来验证「已是则跳过」那条分支。
    /// </param>
    /// <param name="categories">类别搜索的下拉候选。</param>
    /// <param name="saveEnableDelayMilliseconds">
    /// 改完内容后隔多久解禁保存按钮。非零才测得出「等按钮可用」这段实际生效。
    /// </param>
    public static string ProfileSettings(
        bool hasExistingSignature = true,
        bool withSignatureEntry = true,
        bool withSignatureSaveButton = true,
        bool withCategoryEntry = true,
        string currentCategory = "未设置",
        string[]? categories = null,
        int saveEnableDelayMilliseconds = 150)
    {
        categories ??= ["教育", "戏剧"];

        var signatureEntry = !withSignatureEntry
            ? string.Empty
            : hasExistingSignature
                ? """<div class="btn" role="button" aria-label="编辑个性签名" id="sig-edit">编辑个性签名</div>"""
                : """<div class="btn" role="button" id="sig-about">关于你</div>""";

        var signatureSave = withSignatureSaveButton
            ? """<div class="btn" role="button" aria-label="保存" id="sig-save" aria-disabled="true">保存</div>"""
            : string.Empty;

        var categoryEntry = withCategoryEntry
            ? """<div class="btn" role="button" aria-label="编辑类别" id="cat-edit">编辑类别</div>"""
            : string.Empty;

        var catalogJson = System.Text.Json.JsonSerializer.Serialize(categories);

        return $$$"""
            <!DOCTYPE html>
            <html lang="zh-CN">
            <head>
              <meta charset="utf-8">
              <title>资料设置</title>
              <style>
                body { font-family: sans-serif; padding: 20px; }
                .btn { display: inline-block; padding: 6px 14px; border: 1px solid #888;
                       border-radius: 6px; cursor: pointer; margin: 4px 6px 4px 0; }
                #sig-editor, #cat-panel, #post-panel, #signature-view, #category-view,
                #posting-view, #home { display: none; margin-top: 12px; }
                textarea { width: 320px; height: 60px; display: block; margin-bottom: 8px; }
                #cat-options div[role='option'] { padding: 4px 8px; border: 1px solid #ccc;
                                                 margin-top: 4px; cursor: pointer; }
              </style>
            </head>
            <body>
              <div id="home">
                <a href="https://www.facebook.com/profile.php?id=100012345678">我的公共主页</a>
              </div>

              <div id="signature-view">
                {{{signatureEntry}}}
                <div id="sig-editor">
                  <textarea dir="ltr" maxlength="255" id="sig-text"></textarea>
                  {{{signatureSave}}}
                </div>
              </div>

              <div id="category-view">
                <div dir="auto" id="cat-current">{{{currentCategory}}}</div>
                {{{categoryEntry}}}
                <div id="cat-panel">
                  <input role="combobox" aria-label="类别" id="cat-input">
                  <div id="cat-options"></div>
                  <div class="btn" role="button" aria-label="保存" id="cat-save" aria-disabled="true">保存</div>
                </div>
              </div>

              <div id="posting-view">
                <div class="btn" role="button" id="post-row"><span>谁能在你的公共主页发帖？</span></div>
                <div id="post-panel">
                  <div class="btn" role="button" id="post-public">公开</div>
                  <div class="btn" role="button" id="post-self">仅限自己</div>
                  <div class="btn" role="button" aria-label="保存隐私分享对象选择并关闭对话框"
                       id="post-save" aria-disabled="true">保存</div>
                </div>
              </div>

              <script>
                var SAVE_DELAY = {{{saveEnableDelayMilliseconds}}};

                window.__view = '';
                window.__entry = null;
                window.__signature = null;
                window.__signatureSaved = false;
                window.__category = null;
                window.__categorySaved = false;
                window.__posting = null;
                window.__postingSaved = false;

                function show(id) {
                  document.getElementById(id).style.display = 'block';
                }

                function enableLater(id) {
                  setTimeout(function () {
                    var el = document.getElementById(id);
                    if (el) { el.removeAttribute('aria-disabled'); }
                  }, SAVE_DELAY);
                }

                var q = location.search || '';
                if (q.indexOf('directory_intro') >= 0) {
                  window.__view = 'signature';
                  show('signature-view');
                } else if (q.indexOf('directory_category') >= 0) {
                  window.__view = 'category';
                  show('category-view');
                } else if (q.indexOf('profile_and_tagging') >= 0) {
                  window.__view = 'posting';
                  show('posting-view');
                } else {
                  window.__view = 'home';
                  show('home');
                }

                (function bindSignature() {
                  var edit = document.getElementById('sig-edit');
                  var about = document.getElementById('sig-about');
                  var entry = edit || about;
                  if (entry) {
                    entry.addEventListener('click', function () {
                      window.__entry = entry.id;
                      show('sig-editor');
                    });
                  }

                  var text = document.getElementById('sig-text');
                  if (text) {
                    text.addEventListener('input', function () {
                      // 输入即记录：没有保存按钮时（自动保存），这是唯一能观测到的写入结果。
                      window.__signature = text.value;
                      enableLater('sig-save');
                    });
                  }

                  var save = document.getElementById('sig-save');
                  if (save) {
                    save.addEventListener('click', function () {
                      if (save.getAttribute('aria-disabled') === 'true') { return; }
                      window.__signature = text.value;
                      window.__signatureSaved = true;
                    });
                  }
                })();

                (function bindCategory() {
                  var edit = document.getElementById('cat-edit');
                  if (edit) {
                    edit.addEventListener('click', function () { show('cat-panel'); });
                  }

                  // 真页面的类别是搜索引擎：输入关键词 → 下拉里出现候选 → 点候选。
                  // 不是「页面上摆一列现成选项」，所以仿真页也照这个做。
                  var catalog = {{{catalogJson}}};
                  var input = document.getElementById('cat-input');
                  var box = document.getElementById('cat-options');

                  input.addEventListener('input', function () {
                    box.innerHTML = '';
                    var keyword = input.value;
                    if (!keyword) { return; }

                    for (var i = 0; i < catalog.length; i++) {
                      if (catalog[i].indexOf(keyword) < 0) { continue; }

                      var option = document.createElement('div');
                      option.setAttribute('role', 'option');
                      option.textContent = catalog[i];
                      option.addEventListener('click', function (e) {
                        window.__category = e.currentTarget.textContent;
                        enableLater('cat-save');
                      });
                      box.appendChild(option);
                    }
                  });

                  var save = document.getElementById('cat-save');
                  if (save) {
                    save.addEventListener('click', function () {
                      if (save.getAttribute('aria-disabled') === 'true') { return; }
                      window.__categorySaved = true;
                    });
                  }
                })();

                (function bindPosting() {
                  document.getElementById('post-row').addEventListener('click', function () {
                    show('post-panel');
                  });

                  document.getElementById('post-public').addEventListener('click', function () {
                    window.__posting = '公开';
                    enableLater('post-save');
                  });

                  document.getElementById('post-self').addEventListener('click', function () {
                    window.__posting = '仅限自己';
                    enableLater('post-save');
                  });

                  var save = document.getElementById('post-save');
                  save.addEventListener('click', function () {
                    if (save.getAttribute('aria-disabled') === 'true') { return; }
                    window.__postingSaved = true;
                  });
                })();
              </script>
            </body>
            </html>
            """;
    }


    /// <summary>
    /// 主页首页仿真页：头像与背景图都在这一页上改。
    /// </summary>
    /// <remarks>
    /// 结构和原版串逐条对上：
    /// <list type="bullet">
    /// <item>头像入口是 <c>[aria-label='头像操作']</c>；点它才出现
    /// <c>div[role='menuitem']</c> 的「选择头像」；点菜单项才**动态创建**
    /// <c>div[aria-label='上传照片']</c> 与它的 <c>input[type='file']</c>。
    /// 动态创建是刻意的：真页面上这个 input 在点开对话框之前根本不在 DOM 里，
    /// 若一开始就写进 HTML，「必须先点两步」这件事在测试里就永远测不出来。</item>
    /// <item>封面 file input 是 <c>div[role='button']:has-text('封面照片')</c> 的**兄弟**，
    /// 且始终存在于 DOM —— 与头像相反，封面不需要点开任何菜单。</item>
    /// <item>两个保存按钮初始都 <c>aria-disabled='true'</c>，注入图片后隔一小段才解禁，
    /// 用来验证「不主动等就会卡一个点击超时」。</item>
    /// <item>拒绝对话框沿用 <c>div[aria-label='请选择其他封面照片'][aria-modal='true'][role='dialog']</c>；
    /// 文件名含 reject 就弹它。</item>
    /// </list>
    /// </remarks>
    /// <param name="withAvatarEntry">是否渲染「头像操作」。false 用来制造「找不到头像入口」。</param>
    /// <param name="withAvatarMenu">点头像区域后是否弹出「选择头像」菜单。</param>
    /// <param name="withCoverInput">是否渲染封面 file input。</param>
    /// <param name="saveEnableDelayMilliseconds">注入后隔多久解禁保存按钮。</param>
    public static string ProfileHome(
        bool withAvatarEntry = true,
        bool withAvatarMenu = true,
        bool withCoverInput = true,
        int saveEnableDelayMilliseconds = 120)
    {
        var avatarEntry = withAvatarEntry
            ? """<div class="btn" role="button" aria-label="头像操作" id="avatar-area">头像</div>"""
            : string.Empty;

        var cover = withCoverInput
            ? """
              <div class="cover-wrap">
                <div class="btn" role="button" aria-label="编辑封面照片" id="edit-cover">封面照片</div>
                <input type="file" id="cover-input" accept="image/*" style="display:none">
              </div>
              """
            : string.Empty;

        return $$"""
            <!DOCTYPE html>
            <html lang="zh-CN">
            <head>
              <meta charset="utf-8">
              <title>主页</title>
              <style>
                body { font-family: sans-serif; padding: 20px; }
                .btn { display: inline-block; padding: 6px 14px; border: 1px solid #888;
                       border-radius: 6px; cursor: pointer; margin: 4px 6px 4px 0; }
                #avatar-menu, #cover-panel { display: none; margin-top: 8px; }
              </style>
            </head>
            <body>
              {{avatarEntry}}

              <div id="avatar-menu">
                <div role="menuitem" id="avatar-choose">选择头像</div>
              </div>

              <div id="avatar-upload"></div>

              <div id="cover-panel" style="display:none">
                <div class="btn" role="button" aria-label="保存更改" id="cover-save"
                     aria-disabled="true">保存更改</div>
              </div>

              {{cover}}

              <div id="reject-dialog" aria-label="请选择其他封面照片" aria-modal="true" role="dialog"
                   style="display:none;position:fixed;top:40px;left:40px;border:1px solid #888;padding:12px">
                <div>请选择其他封面照片</div>
                <div class="btn" role="button" aria-label="关闭" id="reject-close">关闭</div>
              </div>

              <script>
                var SAVE_DELAY = {{saveEnableDelayMilliseconds}};

                window.__avatarUploads = [];
                window.__avatarSaved = false;
                window.__coverUploads = [];
                window.__coverAccepted = null;
                window.__coverSaved = false;
                window.__rejectClosed = 0;

                var dialog = document.getElementById('reject-dialog');

                function enableLater(id) {
                  setTimeout(function () {
                    var el = document.getElementById(id);
                    if (el) { el.removeAttribute('aria-disabled'); }
                  }, SAVE_DELAY);
                }

                document.getElementById('reject-close').addEventListener('click', function () {
                  window.__rejectClosed = window.__rejectClosed + 1;
                  dialog.style.display = 'none';
                });

                (function bindAvatar() {
                  var area = document.getElementById('avatar-area');
                  if (!area) { return; }

                  area.addEventListener('click', function () {
                    document.getElementById('avatar-menu').style.display = 'block';
                  });

                  var choose = document.getElementById('avatar-choose');
                  if (!choose) { return; }

                  choose.addEventListener('click', function () {
                    var host = document.getElementById('avatar-upload');
                    if (host.getAttribute('data-ready') === '1') { return; }
                    host.setAttribute('data-ready', '1');

                    // 真页面：点「选择头像」之后才渲染「上传照片」对话框与它的 file input。
                    var label = document.createElement('div');
                    label.setAttribute('aria-label', '上传照片');
                    label.textContent = '上传照片';

                    var input = document.createElement('input');
                    input.type = 'file';
                    input.id = 'avatar-input';
                    input.accept = 'image/*';
                    input.style.display = 'none';

                    var panel = document.createElement('div');
                    panel.id = 'avatar-save-panel';
                    panel.style.display = 'none';
                    panel.innerHTML = '<div class="btn" role="button" aria-label="保存" id="avatar-save"'
                      + ' aria-disabled="true">保存</div>';

                    input.addEventListener('change', function (e) {
                      var name = e.target.files && e.target.files.length ? e.target.files[0].name : '';
                      window.__avatarUploads.push(name);
                      panel.style.display = 'block';
                      enableLater('avatar-save');
                    });

                    host.appendChild(label);
                    host.appendChild(input);
                    host.appendChild(panel);

                    panel.querySelector('#avatar-save').addEventListener('click', function (e) {
                      if (e.currentTarget.getAttribute('aria-disabled') === 'true') { return; }
                      window.__avatarSaved = true;
                      // 保存后面板关掉 —— 「保存完成」在页面上的唯一信号。
                      panel.style.display = 'none';
                    });
                  });
                })();

                (function bindCover() {
                  var input = document.getElementById('cover-input');
                  if (!input) { return; }

                  input.addEventListener('change', function (e) {
                    var name = e.target.files && e.target.files.length ? e.target.files[0].name : '';
                    window.__coverUploads.push(name);
                    if (/reject/i.test(name)) {
                      dialog.style.display = 'block';
                      return;
                    }

                    window.__coverAccepted = name;
                    document.getElementById('cover-panel').style.display = 'block';
                    enableLater('cover-save');
                  });

                  var save = document.getElementById('cover-save');
                  save.addEventListener('click', function () {
                    if (save.getAttribute('aria-disabled') === 'true') { return; }
                    window.__coverSaved = true;
                    document.getElementById('cover-panel').style.display = 'none';
                  });
                })();
              </script>
            </body>
            </html>
            """;
    }

    /// <summary>
    /// Messenger 收件箱仿真页：删除走「进会话 → 会话详情 → 删除对话 → 二次确认」四步。
    /// </summary>
    /// <remarks>
    /// **这里的每条选择器都没有原版证据**（原版对「删除消息」一个页面串都没有），
    /// 所以这个仿真页证明的只是「我们写的四步序列在真浏览器里能跑通、退出条件能生效」，
    /// 证明不了「FB 的 Messenger 现在就是这个结构」。真机验收必须重新确认一遍。
    ///
    /// 三个参数对应三条真机上会遇到的岔路：
    /// <list type="bullet">
    /// <item><paramref name="withInfoButton"/> 为 false：详情入口找不到 —— 页面改版的典型表现。</item>
    /// <item><paramref name="confirmRemoves"/> 为 false：确认点了但列表没变 ——
    ///   对方已删了这段对话，或者点了没生效。</item>
    /// <item><paramref name="conversationCount"/> 为 0：空收件箱。</item>
    /// </list>
    /// </remarks>
    public static string MessengerInbox(
        int conversationCount = 3,
        bool withInfoButton = true,
        bool confirmRemoves = true)
    {
        var rows = new System.Text.StringBuilder();
        for (var i = 1; i <= conversationCount; i++)
        {
            var id = 1000 + i;
            rows.AppendLine($"""  <a class="row" href="/messages/t/{id}/" data-id="{id}">会话 {i}</a>""");
        }

        var infoButton = withInfoButton
            ? """<div class="btn" role="button" aria-label="会话详情" id="info">详情</div>"""
            : string.Empty;

        var emptyHint = conversationCount == 0
            ? """<div id="empty">暂无私信</div>"""
            : string.Empty;

        var removesJs = confirmRemoves ? "true" : "false";

        return $$$"""
            <!DOCTYPE html>
            <html lang="zh-CN">
            <head>
              <meta charset="utf-8">
              <title>Messenger</title>
              <style>
                body { font-family: sans-serif; padding: 16px; }
                .row { display: block; padding: 8px 10px; border-bottom: 1px solid #ddd; color: #111; }
                .btn { display: inline-block; padding: 6px 12px; border: 1px solid #888;
                       border-radius: 6px; cursor: pointer; margin: 6px 4px 6px 0; }
                #thread, #panel, #dialog { display: none; margin-top: 12px; }
                #dialog { border: 1px solid #888; padding: 12px; }
                [role='menuitem'] { padding: 8px 10px; border: 1px solid #888;
                                    border-radius: 6px; cursor: pointer; display: inline-block; }
              </style>
            </head>
            <body>
              <div id="list">
            {{{rows}}}
              </div>
              {{{emptyHint}}}

              <div id="thread">
                <div>会话内容</div>
                {{{infoButton}}}
              </div>

              <div id="panel">
                <div role="menuitem" id="del">删除对话</div>
              </div>

              <div id="dialog" role="dialog" aria-modal="true">
                <div>删除这段对话？</div>
                <div class="btn" role="button" id="confirm">删除</div>
                <div class="btn" role="button" id="cancel">取消</div>
              </div>

              <script>
                var REMOVES = {{{removesJs}}};
                var rows = document.querySelectorAll('a.row');
                var thread = document.getElementById('thread');
                var panel = document.getElementById('panel');
                var dialog = document.getElementById('dialog');

                for (var i = 0; i < rows.length; i++) {
                  rows[i].addEventListener('click', function (e) {
                    e.preventDefault();
                    window.__opened = this.getAttribute('data-id');
                    thread.style.display = 'block';
                    panel.style.display = 'none';
                    dialog.style.display = 'none';
                  });
                }

                var info = document.getElementById('info');
                if (info) {
                  info.addEventListener('click', function () {
                    panel.style.display = 'block';
                  });
                }

                var del = document.getElementById('del');
                if (del) {
                  del.addEventListener('click', function () {
                    dialog.style.display = 'block';
                  });
                }

                document.getElementById('cancel').addEventListener('click', function () {
                  dialog.style.display = 'none';
                });

                document.getElementById('confirm').addEventListener('click', function () {
                  window.__confirmed = (window.__confirmed || 0) + 1;
                  dialog.style.display = 'none';
                  if (!REMOVES) { return; }

                  var opened = window.__opened
                    ? document.querySelector('a.row[data-id="' + window.__opened + '"]')
                    : null;
                  if (opened) { opened.remove(); }

                  window.__deleted = (window.__deleted || 0) + 1;
                  panel.style.display = 'none';
                  thread.style.display = 'none';
                  window.__opened = null;
                });
              </script>
            </body>
            </html>
            """;
    }

    /// <summary>
    /// 批量上传页：注入后长出进度条，全部结束才出现「发布」。
    /// </summary>
    /// <remarks>
    /// 结构对着原版串搭：
    /// <list type="bullet">
    /// <item>file input 是 <c>display:none</c> 的 —— 真页面就是这样，判可见永远 false。</item>
    /// <item>进度条必须带齐 <c>role=progressbar</c> + <c>aria-valuemax=100</c> + <c>aria-valuemin=0</c>，
    /// 原版选择器三个属性都要。</item>
    /// <item>「发布」按钮的文本必须**只有「发布」两个字**：产品代码用的是
    /// <c>:text-is('发布')</c>，多一个字就命不中 —— 这正是要防「点到立即发布」的那条改动。</item>
    /// </list>
    /// </remarks>
    /// <param name="failCount">失败的条目数（直接出现「移除」按钮，不走进度条）。</param>
    /// <param name="gateByAddVideo">
    /// true 表示初始没有 file input，必须先点「添加视频」才出现 —— 用来覆盖那条分支。
    /// </param>
    /// <param name="copyrightReview">进度到 100 后转成「版权审核」文案而不是消失。</param>
    /// <param name="tickMilliseconds">进度条每次递增的间隔。</param>
    /// <param name="failRenderDelayMilliseconds">
    /// 失败条目延迟多久才画出来。用来复现真机上的时序：进度条先消失，
    /// 失败条目晚几帧才出现 —— 这时候立刻去数失败数会数到 0。
    /// </param>
    public static string UploadComposer(
        int failCount = 0,
        bool gateByAddVideo = false,
        bool copyrightReview = false,
        int tickMilliseconds = 60,
        int failRenderDelayMilliseconds = 0)
    {
        return $$$"""
            <!DOCTYPE html>
            <html lang="zh-CN">
            <head>
              <meta charset="utf-8">
              <title>批量上传</title>
              <style>
                body { font-family: sans-serif; padding: 24px; }
                #addVideo, #publish, #done, #immediate, #timed {
                  display: inline-block; padding: 8px 16px; border: 1px solid #888;
                  border-radius: 6px; cursor: pointer; margin: 6px 6px 6px 0;
                }
                #publish, #done { display: none; }
                #desc { min-height: 48px; border: 1px solid #ccc; padding: 8px; margin: 12px 0; }
                #panel { display: none; margin: 12px 0; }
                #bars > div { height: 18px; background: #dbeafe; margin: 6px 0; }
                .failed-item { border: 1px solid #f3b; padding: 6px; margin: 6px 0; }
                .failed-item > div[role='button'] { display: inline-block; padding: 4px 10px; border: 1px solid #888; }
              </style>
            </head>
            <body>
              <div id="app">
                <div role="button" id="addVideo">添加视频</div>
                <div id="stage"></div>
                <div id="desc" contenteditable="true" aria-label="在对话框中输入内容"></div>
                <div>
                  <div role="button" id="immediate">立即发布</div>
                  <div role="button" id="timed">定时</div>
                </div>
                <div id="panel">
                  <input id="date" placeholder="年-月-日">
                  <input id="hour" aria-label="小时">
                </div>
                <div id="bars"></div>
                <div id="failed"></div>
                <div role="button" id="publish">发布</div>
                <div role="button" id="done">完成</div>
              </div>
              <script>
                var FAIL_COUNT = {{{failCount}}};
                var FAIL_DELAY = {{{failRenderDelayMilliseconds}}};
                var GATE = {{{(gateByAddVideo ? "true" : "false")}}};
                var COPYRIGHT = {{{(copyrightReview ? "true" : "false")}}};
                var TICK_MS = {{{tickMilliseconds}}};

                var stage = document.getElementById('stage');
                var bars = document.getElementById('bars');
                var failed = document.getElementById('failed');
                var publish = document.getElementById('publish');
                var done = document.getElementById('done');
                var panel = document.getElementById('panel');

                window.__injected = 0;
                window.__published = false;
                window.__done = false;
                window.__removed = 0;
                window.__uploadFinished = false;
                window.__description = '';

                var remaining = 0;

                function makeInput() {
                  var el = document.createElement('input');
                  el.type = 'file';
                  el.multiple = true;
                  el.style.display = 'none';
                  el.addEventListener('change', onFiles);
                  return el;
                }

                function onFiles() {
                  var n = this.files ? this.files.length : 0;
                  window.__injected = n;
                  if (n === 0) { return; }

                  var failN = Math.min(FAIL_COUNT, n);
                  for (var i = 0; i < failN; i++) {
                    if (FAIL_DELAY > 0) {
                      (function () { setTimeout(addFailedItem, FAIL_DELAY); })();
                    } else {
                      addFailedItem();
                    }
                  }

                  remaining = n - failN;
                  if (remaining === 0) { finish(); return; }

                  for (var j = 0; j < remaining; j++) {
                    var bar = document.createElement('div');
                    bar.setAttribute('role', 'progressbar');
                    bar.setAttribute('aria-valuemin', '0');
                    bar.setAttribute('aria-valuemax', '100');
                    bar.setAttribute('aria-valuenow', '0');
                    bar.textContent = '0%';
                    bars.appendChild(bar);
                    runBar(bar);
                  }
                }

                function runBar(bar) {
                  var timer = setInterval(function () {
                    var current = Number(bar.getAttribute('aria-valuenow'));
                    var next = Math.min(100, current + 25);
                    bar.setAttribute('aria-valuenow', String(next));
                    bar.textContent = next + '%';

                    if (next < 100) { return; }

                    clearInterval(timer);

                    if (COPYRIGHT) {
                      bar.textContent = '版权审核';
                      bar.removeAttribute('aria-valuenow');
                      remaining--;
                      if (remaining === 0) { finish(); }
                      return;
                    }

                    bar.remove();
                    remaining--;
                    if (remaining === 0) { finish(); }
                  }, TICK_MS);
                }

                function addFailedItem() {
                  var row = document.createElement('div');
                  row.className = 'failed-item';
                  var btn = document.createElement('div');
                  btn.setAttribute('role', 'button');
                  btn.textContent = '移除';
                  row.appendChild(btn);
                  failed.appendChild(row);
                }

                function finish() {
                  publish.style.display = 'inline-block';
                  window.__uploadFinished = true;
                }

                failed.addEventListener('click', function (e) {
                  var btn = e.target.closest("div[role='button']");
                  if (!btn) { return; }
                  var row = btn.closest('.failed-item');
                  if (!row) { return; }
                  row.remove();
                  window.__removed++;
                });

                publish.addEventListener('click', function () {
                  window.__published = true;
                  publish.style.display = 'none';
                  done.style.display = 'inline-block';
                });

                done.addEventListener('click', function () {
                  window.__done = true;
                  done.style.display = 'none';
                });

                document.getElementById('immediate').addEventListener('click', function () {
                  window.__immediate = true;
                });

                document.getElementById('timed').addEventListener('click', function () {
                  window.__timed = true;
                  panel.style.display = 'block';
                });

                document.getElementById('date').addEventListener('input', function () {
                  window.__date = this.value;
                });

                document.getElementById('hour').addEventListener('input', function () {
                  window.__hour = this.value;
                });

                document.getElementById('desc').addEventListener('input', function () {
                  window.__description = this.innerText;
                });

                if (!GATE) {
                  stage.appendChild(makeInput());
                } else {
                  document.getElementById('addVideo').addEventListener('click', function () {
                    stage.appendChild(makeInput());
                    window.__addVideoClicked = true;
                  });
                }
              </script>
            </body>
            </html>
            """;
    }
}
