"""S4 真机验收：添加任务 -> 开始执行 -> 读状态与结果 -> 停止。

只做「用户会做的动作」，不碰内部实现：
  1. 点左栏第一个任务类型（[视频上传] - 谷歌版）
  2. 读队列行，确认任务已加入且状态为待执行
  3. 点「开始任务」
  4. 轮询列表，直到状态落到终态（成功/失败/中断）或超时
  5. 检查执行结束后「停止任务」是否已禁用

队列用 x:Name="TaskList" 定位。不要用全窗口的 DataItem 搜——
左栏任务类型列表也是 DataItem，会把 5 个任务类型当成队列行（第一版就踩了这个）。

退出码：0 全通过，非 0 表示有断言没过。
"""

import io
import sys
import time

from pywinauto import Application

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

EXE = r"D:\AI\workbuddy\短视频工具\FbAuto\build\out\FbAuto.Presentation\Debug\net10.0-windows\FbAuto.Presentation.exe"
TITLE = "自动化工具_Facebook"
TASK_TYPE = "[视频上传] - 谷歌版"

TERMINAL = ("成功", "失败", "中断")


def dump_rows(win):
    """读队列 ListBox 里每一行的文本，去重保序。"""
    lst = win.child_window(auto_id="TaskList")
    rows = []
    for item in lst.children():
        texts = []
        for child in item.descendants():
            name = (child.window_text() or "").strip()
            if name and name not in texts:
                texts.append(name)
        if texts:
            rows.append(texts)
    return rows


def press(win, title):
    """按 UIA 的 InvokePattern 触发按钮。

    不用 click_input：它依赖鼠标坐标和窗口焦点，窗口刚起来或位置变化时会点空
    （实测有一次点「添加任务」完全没有反应，日志里一行都没有）。
    Invoke 直接调控件的 action，不经过鼠标。
    """
    btn = win.child_window(title=title, control_type="Button")
    btn.wait("enabled", timeout=10)
    try:
        btn.invoke()
    except Exception:
        btn.click_input()
    time.sleep(0.8)


def main():
    app = Application(backend="uia").connect(path=EXE, timeout=20)
    win = app.window(title=TITLE)
    win.wait("visible", timeout=20)
    win.set_focus()

    rect = win.rectangle()
    print(f"窗口: {win.window_text()}  {rect.width()}x{rect.height()}")

    failures = []

    # ---- 1. 添加任务 ----
    press(win, TASK_TYPE)

    rows = dump_rows(win)
    print(f"\n[1] 添加任务后队列行数: {len(rows)}")
    for row in rows:
        print("    ", " | ".join(row))

    if len(rows) != 1:
        failures.append(f"添加一条任务后队列应有 1 行，实际 {len(rows)} 行")
    joined = " ".join(" ".join(r) for r in rows)
    if "待执行" not in joined:
        failures.append("新加的任务状态不是「待执行」")
    if "Chrome-acc01" not in joined:
        failures.append("浏览器列没有显示分配到的账号（应显示 Chrome-acc01）")

    # ---- 2. 开始任务 ----
    start = win.child_window(title="开始任务", control_type="Button")
    if not start.is_enabled():
        failures.append("有任务时「开始任务」按钮不可用")
    else:
        start.invoke()
    print("\n[2] 已点击「开始任务」，开始轮询状态")

    # ---- 3. 轮询到终态 ----
    seen_running = False
    final_rows = []
    deadline = time.time() + 60

    while time.time() < deadline:
        time.sleep(1.2)
        rows = dump_rows(win)
        joined = " ".join(" ".join(r) for r in rows)
        if "执行中" in joined:
            seen_running = True
        print(f"    {time.strftime('%H:%M:%S')} → {joined[:170]}")

        if rows and any(any(t in " ".join(r) for t in TERMINAL) for r in rows):
            final_rows = rows
            break

    print("\n[3] 终态")
    for row in final_rows or dump_rows(win):
        print("    ", " | ".join(row))

    if not final_rows:
        failures.append("60 秒内没有落到终态（卡在执行中）")
    else:
        joined = " ".join(" ".join(r) for r in final_rows)
        if "失败" not in joined and "成功" not in joined:
            failures.append("终态既不是成功也不是失败")
        if "尚未接入" not in joined:
            # S4 阶段五类任务都还没有页面动作，正确结果是「失败 + 说明未接入」
            failures.append("失败原因里没有「尚未接入」，说明执行器没有按预期报错")

    # ---- 4. 空闲时停止按钮应禁用 ----
    time.sleep(0.5)
    stop = win.child_window(title="停止任务", control_type="Button")
    if stop.is_enabled():
        failures.append("执行结束后「停止任务」仍可用（应禁用）")

    print("\n=== 结论 ===")
    print(f"过程中{'捕获到' if seen_running else '未捕获到'}「执行中」中间态（任务很快时可能一闪而过）")
    if failures:
        for f in failures:
            print("[失败]", f)
        return 1

    print("[通过] 队列调度、状态回写、结果文案、按钮可用性均符合预期")
    return 0


if __name__ == "__main__":
    sys.exit(main())
