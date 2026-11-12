"""S3 重启验收：已有有效凭证时，启动应直接进主界面，不再弹激活窗。

用法：python uia_restart_check.py
返回 0 表示直接进了主界面（凭证持久化生效）。
"""

import os
import subprocess
import sys
import time

from pywinauto import Application

BASE = r"D:\AI\workbuddy\短视频工具\FbAuto\build\out\FbAuto.Presentation\Debug\net10.0-windows"
EXE = os.path.join(BASE, "FbAuto.Presentation.exe")
MAIN_SIZE = (1176, 752)


def main():
    proc = subprocess.Popen([EXE], cwd=BASE)
    time.sleep(8)

    try:
        app = Application(backend="uia").connect(path=EXE, timeout=20)
        windows = app.windows()
        print(f"启动后窗口 {len(windows)} 个:")
        for w in windows:
            rect = w.rectangle()
            print(f"  - {w.window_text()!r} {rect.width()}x{rect.height()}")

        sizes = [(w.rectangle().width(), w.rectangle().height()) for w in windows]
        if MAIN_SIZE in sizes:
            print("[通过] 直接进入主界面，无需再次激活")
            return 0

        print("[失败] 没有直接进主界面")
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
