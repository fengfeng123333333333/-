namespace FbAuto.Tests.Support;

/// <summary>
/// 粉丝公开内容仿真页：一个「输入即打标签」的框 + 一个保存按钮。
/// </summary>
internal static class FollowersMimicPage
{
    /// <summary>
    /// 生成页面。
    /// </summary>
    /// <param name="withInput">是否渲染关键词输入框（false 用来验证「跳过关键词设置」）。</param>
    /// <param name="withSaveButton">是否渲染保存按钮（false 用来验证走 Escape 兜底）。</param>
    public static string Build(bool withInput = true, bool withSaveButton = true)
    {
        var input = withInput
            ? """
              <label for="kw">屏蔽关键词</label>
              <textarea id="CometUSFTagInputFreeformDialogTagEditor" placeholder="每行限 1000 字"></textarea>
              """
            : "<div>本页没有关键词输入框</div>";

        var save = withSaveButton
            ? """<div class="btn" role="button" aria-label="保存" id="kw-save">保存</div>"""
            : string.Empty;

        return $$"""
            <!DOCTYPE html>
            <html lang="zh-CN">
            <head>
              <meta charset="utf-8">
              <title>粉丝公开内容</title>
              <style>
                body { font-family: sans-serif; padding: 20px; }
                .btn { display: inline-block; padding: 6px 14px; border: 1px solid #888;
                       border-radius: 6px; cursor: pointer; margin-top: 8px; }
                textarea { width: 420px; height: 40px; }
                #tags { margin-top: 10px; }
                .tag { display: inline-block; background: #eef; border: 1px solid #99c;
                       border-radius: 10px; padding: 2px 8px; margin: 2px 4px 2px 0; }
              </style>
            </head>
            <body>
              {{input}}
              {{save}}
              <div id="tags"></div>

              <script>
                var input = document.getElementById('CometUSFTagInputFreeformDialogTagEditor');
                var tags = document.getElementById('tags');

                // 页面上的真实计数，测试用它判断「词真的变成标签了」而不是只填进框里。
                window.__tags = [];
                window.__saved = false;

                function addTag(text) {
                  var t = text.trim();
                  if (!t) { return; }
                  window.__tags.push(t);
                  var el = document.createElement('span');
                  el.className = 'tag';
                  el.textContent = t;
                  tags.appendChild(el);
                }

                if (input) {
                  // 只有按回车才变成标签 —— 与真页面一致。只填不回车，保存后词就丢了。
                  input.addEventListener('keydown', function (e) {
                    if (e.key === 'Enter') {
                      e.preventDefault();
                      addTag(input.value);
                      input.value = '';
                    }
                  });
                }

                var save = document.getElementById('kw-save');
                if (save) {
                  save.addEventListener('click', function () {
                    window.__saved = true;
                    // 真页面点保存会关掉面板；这里只留个可观测标记。
                    save.setAttribute('aria-disabled', 'true');
                  });
                }
              </script>
            </body>
            </html>
            """;
    }
}
