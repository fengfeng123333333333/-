"""S3 离线授权真机验收：启动程序 → 激活窗输入授权码 → 主界面出现。

必须在同一条命令里跑完：本工具会话结束时会把拉起的 GUI 子进程一起回收。
用法：python uia_activate_check.py "<授权码>"
"""

import os
import subprocess
import sys
import time
from pathlib import Path

import win32clipboard
from pywinauto import Application
from pywinauto.keyboard import send_keys

# 路径按脚本位置推算（本文件在 FbAuto/tools/uia/ 下），换机器换盘符都不用改。
REPO = Path(__file__).resolve().parents[2]
BASE = str(REPO / "build" / "out" / "FbAuto.Presentation" / "Debug" / "net10.0-windows")
EXE = os.path.join(BASE, "FbAuto.Presentation.exe")

MAIN_SIZE = (1176, 752)
LOGIN_SIZE = (540, 580)


def describe(window):
    rect = window.rectangle()
    return f"{window.window_text()!r} {rect.width()}x{rect.height()}"


def set_clipboard(text):
    win32clipboard.OpenClipboard()
    try:
        win32clipboard.EmptyClipboard()
        win32clipboard.SetClipboardText(text, win32clipboard.CF_UNICODETEXT)
    finally:
        win32clipboard.CloseClipboard()


def get_clipboard():
    win32clipboard.OpenClipboard()
    try:
        return win32clipboard.GetClipboardData(win32clipboard.CF_UNICODETEXT)
    except Exception:  # noqa: BLE001
        return ""
    finally:
        win32clipboard.CloseClipboard()


def main():
    code = sys.argv[1] if len(sys.argv) > 1 else ""
    if not code:
        print("[错误] 需要传入授权码")
        return 1

    proc = subprocess.Popen([EXE], cwd=BASE)
    time.sleep(8)

    try:
        app = Application(backend="uia").connect(path=EXE, timeout=20)
        window = app.top_window()
        print("启动后窗口:", describe(window))

        edits = window.descendants(control_type="Edit")
        print(f"输入框数量: {len(edits)}")
        for index, edit in enumerate(edits):
            try:
                value = edit.get_value()
            except Exception as exc:  # noqa: BLE001
                value = f"<读取失败 {exc}>"
            print(f"  [{index}] 值={value!r}")

        if len(edits) < 2:
            print("[失败] 找不到授权码输入框")
            return 2

        print("写入授权码（走剪贴板粘贴，和真人操作一致）…")
        set_clipboard(code)
        print("剪贴板长度:", len(get_clipboard()))

        # 窗口必须先到前台，否则 send_keys 会发给别的窗口
        window.set_focus()
        time.sleep(0.6)
        edits[1].click_input()
        time.sleep(0.4)
        send_keys("^a")
        time.sleep(0.2)
        send_keys("^v")
        time.sleep(1.0)

        actual = edits[1].get_value()
        if actual != code:
            print(f"[失败] 粘贴后值与预期不符：长度 {len(actual)}，前 60 字符 {actual[:60]!r}")
            return 4
        print("粘贴校验通过，长度", len(actual))

        button = window.child_window(title="登录激活", control_type="Button")
        print("点击登录激活")
        button.click_input()
        time.sleep(5)

        # 重新连接，看激活窗是否消失、主窗口是否出现
        app2 = Application(backend="uia").connect(path=EXE, timeout=15)
        windows = app2.windows()
        print(f"激活后可见窗口 {len(windows)} 个:")
        for w in windows:
            print("  -", describe(w))

        sizes = [(w.rectangle().width(), w.rectangle().height()) for w in windows]
        if MAIN_SIZE in sizes:
            print("[通过] 主界面已出现，激活流程走通")
            return 0

        print("[失败] 没有出现主界面窗口")
        return 3
    finally:
        try:
            proc.terminate()
            proc.wait(timeout=10)
        except Exception:  # noqa: BLE001
            pass
        subprocess.run(["taskkill", "/F", "/IM", "FbAuto.Presentation.exe"],
                       capture_output=True, check=False)


if __name__ == "__main__":
    sys.exit(main())
