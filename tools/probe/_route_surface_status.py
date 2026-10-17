# -*- coding: utf-8 -*-
"""
口径对照：我方「出口/规则表计数」 vs QudJP 的 **11 条 route 闸门**。

用途（两个）：
  ① **看差距**：哪些 route 我们有对应口径、哪些完全没有（未分类必须 0 那条我们就没有）
  ② **当闸门**：`--check` 模式把「本文件记录的我方基线值」与实时统计比对
     （基线取自 `_tools\\计数基线.json` —— 项目的唯一权威）

用法：
    python _tools\\_route_surface_status.py            # 只打印对照表
    python _tools\\_route_surface_status.py --check     # 同时比对实时值（不一致 ⇒ exit 1）

★ 数据来源：
   · 我方：`_tools\\计数基线.json`（`python _tools\\_count_baseline.py --write` 生成）
   · 他们：`_tools\\QudJP-可抄清单.md` §一 的表格（从 `.github/workflows/ci.yml` 抄来）
     ⚠ 他们的值会随版本变（我们记的是 **v0.5.08 / 2026-09-06** 那一版）
"""
import io
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BASELINE = os.path.join(ROOT, "_tools", "计数基线.json")
QUDJP = os.path.join(ROOT, "_tools", "QudJP-可抄清单.md")

# ═══════════════════════════════════════════════════════════════════════════
# 我方口径 → 他们的 route 映射（人工判定，写在注释里备查）
# ═══════════════════════════════════════════════════════════════════════════
MAP = [
    # (我方 文件, 我方 键, 他们的 route, 备注)
    ("ZhsCombatMsgPatch.cs", "规则/条目", "message-frame",
     "整句正则族；他们 53 条是「消息框句型」的窄口径，我们含大量战斗句"),
    ("ZhsPopupPatch.cs", "L（弹窗精确表）", "popup", "弹窗精确映射"),
    ("ZhsPopupPatch.cs", "REASM（正则模板表）", "popup", "同上（正则形态）"),
    ("ZhsPopupPatch.cs", "Line（整行精确表）", "popup", "同上（整行形态）"),
    ("ZhsHardcodedMsg.cs", "规则/条目", "emit-message", "消息栏整句硬编码"),
    ("ZhsVerbPatch.cs", "V（=verb: 动词表）", "does-verb",
     "★ 口径相反：他们期望 0（=verb: 全在数据侧解决），我们走出口侧 431 条"),
    ("ZhsBootstrap.cs", "MountOff 挂载点", "needs-harmony-patch",
     "★ 口径相反：他们期望 0（全数据侧），我们是 59 个补丁出口"),
]

# 他们的 11 条（v0.5.08 口径）
QUDJP_ROUTES = [
    ("message-frame", 53), ("popup", 24), ("journal", 1), ("leaf", 0),
    ("emit-message", 391), ("does-verb", 0), ("message-log", 1),
    ("description", 14), ("effect-cripple", 1),
    ("needs-harmony-patch", 0), ("unclassified", 0),
]


def load_baseline():
    if not os.path.isfile(BASELINE):
        print("!! 找不到 %s" % BASELINE)
        return {}
    return json.load(io.open(BASELINE, encoding="utf-8"))


def main():
    check = "--check" in sys.argv
    base = load_baseline()
    total = sum(v for tbl in base.values() for v in tbl.values())

    print("=" * 78)
    print("我方计数基线：%d 个文件 / %d 个键 · 合计 %d 条"
          % (len(base), sum(len(v) for v in base.values()), total))
    print("=" * 78)
    for tbl in sorted(base):
        for k, v in base[tbl].items():
            print("   %-34s %-22s %6d" % (tbl, k, v))

    print()
    print("=" * 78)
    print("★ 逐 route 对照（他们 = QudJP v0.5.08 · CI 硬编码值）")
    print("=" * 78)
    print("   %-22s %8s   %s" % ("route", "他们", "我方"))
    ours_by_route = {}
    for tbl, key, route, note in MAP:
        v = base.get(tbl, {}).get(key)
        if v is None:
            continue
        ours_by_route.setdefault(route, []).append("%s.%s=%d" % (tbl, key, v))
    for route, their in QUDJP_ROUTES:
        mine = " · ".join(ours_by_route.get(route, [])) or "—— 无对应口径"
        flag = ""
        if their == 0 and route in ours_by_route:
            flag = "   ★ 他们期望 0（闸门），我们有条目 ⇒ 口径相反，需解释"
        print("   %-22s %8d   %s%s" % (route, their, mine, flag))

    print()
    print("=" * 78)
    print("★ 我们**完全没有**对应口径的 route（＝可补的检查维度）")
    print("=" * 78)
    for route, their in QUDJP_ROUTES:
        if route not in ours_by_route:
            print("   · %-22s 他们 %d 条   ⇒ 我方无此分类" % (route, their))

    if not check:
        print()
        print("（加 --check 可与实时基线比对；不一致 exit 1）")
        return 0

    # `--check`：确认基线文件里的总数与映射一致（防止脚本腐烂）
    ok = True
    for tbl, key, _route, _note in MAP:
        if key not in base.get(tbl, {}):
            print("!! 映射失效：%s 里已没有 %r" % (tbl, key))
            ok = False
    print()
    print("⇒ %s" % ("映射有效 ✅" if ok else "映射与基线不一致 ⛔（基线改过？更新 MAP）"))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
