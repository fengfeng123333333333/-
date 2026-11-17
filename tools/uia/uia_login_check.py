"""读取 S1 激活窗的真实控件树，并验证空卡密时的错误提示。

只读式采集 + 一次按钮点击，不改程序、不碰任何外部系统。
"""

from pathlib import Path

from pywinauto import Application

# 路径按脚本位置推算（本文件在 FbAuto/tools/uia/ 下），换机器换盘符都不用改。
REPO = Path(__file__).resolve().parents[2]
EXE = str(REPO / "build" / "out" / "FbAuto.Presentation" / "Debug" / "net10.0-windows" / "FbAuto.Presentation.exe")
TITLE = "自动化工具_Facebook"

app = Application(backend="uia").connect(path=EXE, timeout=20)
win = app.window(title=TITLE)
win.wait("visible", timeout=20)

rect = win.rectangle()
print(f"窗口标题: {win.window_text()}")
print(f"窗口尺寸: {rect.width()} x {rect.height()}")

print("\n=== 控件树（前 40 个） ===")
count = 0
for c in win.descendants():
    try:
        name = (c.window_text() or "").strip()
        ctype = c.element_info.control_type
    except Exception:
        continue
    if not name:
        continue
    print(f"[{ctype}] {name}")
    count += 1
    if count >= 40:
        break

print("\n=== 点击「登录激活」（卡密为空） ===")
try:
    win.child_window(title="登录激活", control_type="Button").click_input()
    win.wait("exists", timeout=2)
except Exception as ex:
    print("按钮点击异常:", ex)

import time
time.sleep(1.5)

print("\n=== 点击后的可见文本 ===")
for c in win.descendants():
    try:
        name = (c.window_text() or "").strip()
    except Exception:
        continue
    if name and ("卡密" in name or "激活" in name or "错误" in name):
        print("·", name)
