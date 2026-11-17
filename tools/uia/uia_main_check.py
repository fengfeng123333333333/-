"""读取主界面控件树，核对三栏结构、列头文案与空态提示。

只读采集，不做任何点击（避免误触发任务）。
"""

import sys
import io
from pathlib import Path

from pywinauto import Application

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

# 路径全部按脚本位置推算（本文件在 FbAuto/tools/uia/ 下），换机器换盘符都不用改。
REPO = Path(__file__).resolve().parents[2]
EXE = str(REPO / "build" / "out" / "FbAuto.Presentation" / "Debug" / "net10.0-windows" / "FbAuto.Presentation.exe")
OUT = REPO / "build" / "uia_main.txt"
TITLE = "自动化工具_Facebook"

app = Application(backend="uia").connect(path=EXE, timeout=20)
win = app.window(title=TITLE)
win.wait("visible", timeout=20)

rect = win.rectangle()
print(f"窗口标题: {win.window_text()}")
print(f"窗口尺寸: {rect.width()} x {rect.height()}")

print("\n=== 全部文本与按钮 ===")
buttons, texts, tabs = [], [], []
for c in win.descendants():
    try:
        name = (c.window_text() or "").strip()
        ctype = c.element_info.control_type
        r = c.rectangle()
    except Exception:
        continue
    if not name:
        continue
    item = (r.left, r.top, r.width(), r.height(), name)
    if ctype == "Button":
        buttons.append(item)
    elif ctype == "Text":
        texts.append(item)
    elif ctype in ("TabItem", "Tab"):
        tabs.append((ctype, name))

print("--- 按钮（按横向位置排序）---")
for left, top, w, h, name in sorted(set(buttons)):
    print(f"  x={left:<5} y={top:<5} {w}x{h:<4} {name}")

print("\n--- 页签 ---")
for ctype, name in tabs:
    print(f"  [{ctype}] {name}")

print("\n--- 文本（去重，按位置）---")
out = open(OUT, "w", encoding="utf-8")
out.write(f"窗口: {win.window_text()}  {rect.width()}x{rect.height()}\n")
seen = set()
for left, top, w, h, name in sorted(set(texts), key=lambda t: (t[1], t[0])):
    if name in seen:
        continue
    seen.add(name)
    line = f"  x={left:<5} y={top:<5} {name}"
    print(line)
    out.write(line + "\n")
out.write("\n=== 空态检查 ===\n")
out.write("含'暂无任务': " + str(any("暂无任务" in n for n in seen)) + "\n")
out.close()
