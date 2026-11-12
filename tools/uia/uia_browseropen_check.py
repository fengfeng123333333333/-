"""验证「打开浏览器」与关窗口收尾。

链路：点「打开浏览器」-> 出现带指定 profile 的 Chrome 进程 -> 关闭主窗口 -> 该进程消失。

这条必须真机验：单元测试碰不到「窗口关闭时统一关闭浏览器」这段，
而它一旦失效，用户每次退出都会留下孤儿 Chrome 进程，慢慢吃掉内存和 profile 锁。
"""

import io
import subprocess
import sys
import time

from pywinauto import Application

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

EXE = r"D:\AI\workbuddy\短视频工具\FbAuto\build\out\FbAuto.Presentation\Debug\net10.0-windows\FbAuto.Presentation.exe"
TITLE = "自动化工具_Facebook"
PROFILE_MARK = "FbProfiles\\acc01"


def chrome_pids():
    """找命令行里带目标 profile 的 chrome 进程（只认自己开的，不碰用户日常浏览器）。"""
    script = (
        "Get-CimInstance Win32_Process -Filter \"Name='chrome.exe'\" | "
        f"Where-Object {{ $_.CommandLine -like '*{PROFILE_MARK}*' }} | "
        "Select-Object -ExpandProperty ProcessId"
    )
    try:
        out = subprocess.run(
            ["powershell", "-NoProfile", "-Command", script],
            capture_output=True, text=True, timeout=30,
        )
        return [int(x) for x in out.stdout.split() if x.strip().isdigit()]
    except Exception as ex:  # noqa: BLE001
        print(f"  查询进程失败: {ex}")
        return []


def main():
    app = Application(backend="uia").connect(path=EXE, timeout=20)
    win = app.window(title=TITLE)
    win.wait("visible", timeout=20)
    win.set_focus()

    failures = []

    before = chrome_pids()
    print(f"[0] 打开前，带 {PROFILE_MARK} 的 Chrome 进程: {before or '无'}")

    btn = win.child_window(title="打开浏览器", control_type="Button")
    btn.wait("enabled", timeout=10)
    print("[1] 点击「打开浏览器」")
    try:
        btn.invoke()
    except Exception:
        btn.click_input()

    # Chrome 冷启动通常 3-8 秒
    appeared = []
    for _ in range(20):
        time.sleep(1)
        now = chrome_pids()
        new = [p for p in now if p not in before]
        if new:
            appeared = new
            break

    if appeared:
        print(f"[2] 已启动 Chrome: {appeared}")
    else:
        failures.append("点「打开浏览器」后 20 秒内没有出现目标 Chrome 进程")

    print("[3] 关闭主窗口")
    try:
        win.close()
    except Exception as ex:  # noqa: BLE001
        print(f"  关闭窗口异常（可忽略）：{ex}")

    time.sleep(4)
    left = [p for p in chrome_pids() if p in (appeared or [])]
    if left:
        failures.append(f"程序退出后仍有 Chrome 残留: {left}")
        print(f"[4] 残留: {left}")
    else:
        print("[4] 没有残留进程")

    print("\n=== 结论 ===")
    if failures:
        for f in failures:
            print("[失败]", f)
        return 1

    print("[通过] 打开浏览器可用，退出时浏览器被收干净")
    return 0


if __name__ == "__main__":
    sys.exit(main())
