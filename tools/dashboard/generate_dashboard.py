#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
制作人工作台数据生成器（ONIBLADE 鬼刃）

用途：把散在 docs/TASKS.md、git 提交历史、HANDOFF 交接报告、docs/14 排期里的
      "哪个 agent 做了什么 / 准备干什么" 汇总成一个离线 HTML 看板，
      让制作人一眼看清当前状态，不用翻 5 份文档。

用法（在项目根目录跑）：
    python tools/dashboard/generate_dashboard.py
    或双击 tools/dashboard/refresh.ps1

输出：tools/dashboard/dashboard.html （用浏览器直接打开，无需服务器）
数据源：
    1. docs/TASKS.md         任务卡状态总览表（看板列）
    2. git log               最近提交（agent 活动时间线）
    3. Txx-HANDOFF.md        交接报告清单
    4. docs/14-下一阶段排期.md  阻塞与梯队（"准备干什么"）
    5. tools/dashboard/check_state.json  最近一次 check.ps1 实测结果（可选，缺省留白）
"""

import datetime
import glob
import json
import os
import re
import shutil
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DASH_DIR = os.path.join(ROOT, "tools", "dashboard")
TEMPLATE = os.path.join(DASH_DIR, "dashboard.template.html")
OUT = os.path.join(DASH_DIR, "dashboard.html")
CHECK_STATE = os.path.join(DASH_DIR, "check_state.json")


# ---------------------------------------------------------------- 小工具
def clean_md(text: str) -> str:
    """去掉 **加粗** 与 ~~删除线~~，留纯文本。"""
    text = re.sub(r"\*\*", "", text)
    text = re.sub(r"~~", "", text)
    return text.strip()


def git(*args: str) -> str:
    """调 git，优先系统 PATH，兜底本机常见安装路径。"""
    exe = shutil.which("git")
    if not exe:
        for cand in (r"C:\Program Files\Git\cmd\git.exe", r"C:\Program Files (x86)\Git\cmd\git.exe"):
            if os.path.exists(cand):
                exe = cand
                break
    if not exe:
        return ""
    try:
        return subprocess.run([exe, *args], cwd=ROOT, capture_output=True,
                              text=True, encoding="utf-8", errors="replace").stdout
    except Exception:
        return ""


# ---------------------------------------------------------------- 1. 任务卡
def parse_tasks() -> list:
    """解析 docs/TASKS.md 的状态总览表。

    表格行形如：| **T1** | 标题 | 负责 | 状态（含证据） | 依赖 |
    个别行把"阻塞标记"写在负责列（如 T38 的 🔴 试玩阻塞 → 🟢 已解除），
    所以状态归类看整行，而不是只看某一列。
    """
    path = os.path.join(ROOT, "docs", "TASKS.md")
    if not os.path.exists(path):
        return []
    with open(path, encoding="utf-8") as f:
        text = f.read()

    cards = []
    for ln in text.splitlines():
        m = re.match(r"^\|\s*\*{0,2}T(\d+)\*{0,2}\s*\|(.*)$", ln)
        if not m:
            continue
        tid = int(m.group(1))
        cells = [c.strip() for c in m.group(2).split("|")]
        cells = [c for c in cells if c]  # 去掉空单元格
        if not cells:
            continue
        row = m.group(2)  # 整行剩余部分，用于状态归类
        title = clean_md(cells[0]) if len(cells) > 0 else ""
        owner_raw = cells[1] if len(cells) > 1 else ""
        status_raw = cells[2] if len(cells) > 2 else ""
        dep_raw = cells[3] if len(cells) > 3 else ""

        # 负责列常见写法：~~执行 agent~~ → **制作人** / 执行 agent（唯一成功的）/ 👤 由项目主人执行
        owner = "—"
        if "→" in owner_raw:
            owner_raw = owner_raw.split("→")[-1]
        if "执行 agent" in owner_raw:
            owner = "执行 agent"
        elif "项目主人" in owner_raw:
            owner = "项目主人"
        elif "制作人" in owner_raw:
            owner = "制作人"

        cards.append({
            "id": f"T{tid}",
            "num": tid,
            "title": title,
            "owner": owner,
            "owner_raw": clean_md(owner_raw),
            "status": classify_status(row, status_raw),
            "note": clean_md(status_raw) or clean_md(owner_raw),
            "flag": clean_md(owner_raw) if ("⚠" in owner_raw or "无卡片" in owner_raw) else "",
            "dep": clean_md(dep_raw),
        })
    cards.sort(key=lambda c: c["num"])
    return cards


REVIEW_KEYWORDS = ("待验收", "已产出", "前置已完成", "实现完成", "灰盒完成",
                   "代码层已完成", "部分", "在制品", "程序化通道已通")


def classify_status(row: str, status_raw: str) -> str:
    """把一行卡归到一个状态列。优先级：完成 > 制作人 > 阻塞 > 在制品 > 可领取 > 未开始。

    只认状态列（status_raw）里的标记，不看负责列/依赖列——
    依赖列会写 "T15 ✅" 这种引用，扫整行会把 ⚪ 未开始的卡误判成已完成。

    返回：done / producer / blocked / review / ready / todo
    """
    if "✅" in status_raw:
        return "done"
    if "👤" in row or "项目主人" in row or "制作人定" in row or "制作人裁定" in row:
        return "producer"
    # 🔴 只在状态列才算阻塞；负责列里的 🔴 多是"解锁型/已解除"说明
    if "🔴" in status_raw and not any(k in status_raw for k in ("解除", "解锁型")):
        return "blocked"
    if "🟡" in status_raw:
        return "review"
    if "🟢" in status_raw:
        if any(k in status_raw for k in REVIEW_KEYWORDS):
            return "review"
        if "完成" in status_raw:  # 表里 🟢 完成（如 T35）= 已完成，只是没走 ✅ 记号
            return "done"
        return "ready"
    if "⚪" in status_raw:
        if "可领取" in row:  # 表里负责列写 "🟢 可领取" 的卡 = 现在就能派发
            return "ready"
        return "todo"
    return "todo"


# ---------------------------------------------------------------- 2. git 历史
def parse_git(n: int = 150) -> tuple:
    out = git("log", "--pretty=format:%H%x1f%ad%x1f%s",
              "--date=format:%Y-%m-%d %H:%M", "-n", str(n))
    entries = []
    for ln in out.splitlines():
        parts = ln.split("\x1f")
        if len(parts) == 3:
            entries.append({
                "hash": parts[0][:8],
                "date": parts[1],
                "subject": clean_md(parts[2]),
            })
    uncommitted = len([x for x in git("status", "--porcelain").splitlines() if x.strip()])
    return entries, uncommitted


# ---------------------------------------------------------------- 3. 交接报告
def parse_handoffs() -> list:
    """找仓库根目录的 Txx-HANDOFF.md 与 docs/HANDOFF-*.md，取标题与写作日期。"""
    found = []
    for p in glob.glob(os.path.join(ROOT, "T*-HANDOFF.md")) + \
             glob.glob(os.path.join(ROOT, "docs", "HANDOFF-*.md")):
        title, date = "", ""
        with open(p, encoding="utf-8", errors="replace") as f:
            for i, ln in enumerate(f):
                if i == 0 and ln.startswith("#"):
                    title = ln.lstrip("# ").strip()
                elif "写于" in ln and not date:
                    m = re.search(r"(\d{4}-\d{2}-\d{2})", ln)
                    if m:
                        date = m.group(1)
                if i > 8:
                    break
        rel = os.path.relpath(p, ROOT).replace("\\", "/")
        found.append({"title": title or os.path.basename(p), "date": date, "path": rel})
    found.sort(key=lambda h: h["date"], reverse=True)
    return found


# ---------------------------------------------------------------- 4. docs/14 排期
def parse_schedule() -> dict:
    path = os.path.join(ROOT, "docs", "14-下一阶段排期.md")
    out = {"tiers": [], "blockers": [], "summary": ""}
    if not os.path.exists(path):
        return out
    with open(path, encoding="utf-8") as f:
        lines = f.read().splitlines()

    in_summary = False
    for ln in lines:
        m = re.match(r"^\|\s*\*{2}([①-④])\s*([^*]+?)\*{2}\s*\|(.*?)\|\s*(.*?)\s*\|$", ln)
        if m:
            out["tiers"].append({
                "tier": m.group(1),
                "cards": clean_md(m.group(2)),
                "why": clean_md(m.group(3)),
            })
            continue
        m2 = re.match(r"^### 阻塞 ([ABC])：(.+)$", ln)
        if m2:
            out["blockers"].append({"key": m2.group(1), "title": m2.group(2).strip()})
            continue
        if ln.startswith("## 3."):
            in_summary = True
            continue
        if in_summary and ln.startswith("## "):
            in_summary = False
        if in_summary and ln.strip() and not ln.startswith(">"):
            out["summary"] += ln.strip()
    return out


# ---------------------------------------------------------------- 5. check 状态
def parse_check_state() -> dict:
    if os.path.exists(CHECK_STATE):
        try:
            with open(CHECK_STATE, encoding="utf-8") as f:
                return json.load(f)
        except Exception:
            pass
    return {"green": None, "steps": [], "note": "最近一次 check.ps1 结果尚未记录"}


# ---------------------------------------------------------------- 组装
def build_data() -> dict:
    tasks = parse_tasks()
    by_id = {c["id"]: c for c in tasks}
    commits, uncommitted = parse_git()
    handoffs = parse_handoffs()
    sched = parse_schedule()
    check = parse_check_state()

    # 时间线里给每条提交挂上任务标签与"谁干的"（继承该卡负责列）
    timeline = []
    for c in commits:
        tags = sorted({f"T{m.group(1)}" for m in re.finditer(r"T(\d+)", c["subject"])},
                      key=lambda t: int(t[1:]))
        owners = {by_id[t]["owner"] for t in tags if t in by_id and by_id[t]["owner"] != "—"}
        c["tags"] = tags
        c["owner"] = " / ".join(sorted(owners)) or "—"
        timeline.append(c)

    # 按日期分组
    by_day = {}
    for c in timeline:
        day = c["date"][:10]
        by_day.setdefault(day, []).append(c)

    return {
        "generated_at": datetime.datetime.now().strftime("%Y-%m-%d %H:%M"),
        "project": "ONIBLADE 鬼刃",
        "tasks": tasks,
        "timeline_by_day": [{"day": d, "items": by_day[d]} for d in sorted(by_day, reverse=True)],
        "commit_count": len(commits),
        "uncommitted": uncommitted,
        "last_commit": timeline[0]["date"] if timeline else "—",
        "handoffs": handoffs,
        "schedule": sched,
        "check": check,
        # M1 唯一验收标准（08 文档红线，做不做正式验收是制作人自己的事）
        "m1_criterion": "弹开成功时会想再试一次。",
    }


def main() -> None:
    data = build_data()
    with open(TEMPLATE, encoding="utf-8") as f:
        html = f.read()
    payload = json.dumps(data, ensure_ascii=False, indent=1)
    payload = payload.replace("</", "<\\/")  # 防止 JSON 里的 </script> 截断页面
    html = html.replace("__DATA_JSON__", payload)
    with open(OUT, "w", encoding="utf-8") as f:
        f.write(html)
    print(f"OK 看板已生成 → {os.path.relpath(OUT, ROOT)}")
    print(f"   卡片 {len(data['tasks'])} 张 · 提交 {data['commit_count']} 条"
          f" · 交接报告 {len(data['handoffs'])} 份 · 未提交变更 {data['uncommitted']}")


if __name__ == "__main__":
    main()
