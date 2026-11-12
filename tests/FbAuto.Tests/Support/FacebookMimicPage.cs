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
