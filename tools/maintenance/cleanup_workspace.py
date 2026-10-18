# -*- coding: utf-8 -*-
r"""工作区整理：mod 备份「留最新 5，其余删5留1」+ 清缓存。

规则（用户 2026-10-18）：
    「除了最后五个版本的 mod 备份，其余删5留1」

分类：
  A. mod 备份 = `_tools\_instalbak_*` · `_tools\_snapshot_*` · `_backup\<版本>` · `_release\backup\dev_*`
     ⇒ 按时间排序，**留最新 5 个**；更老的按 6 个一组，**删5留1**（留每组里最新的那个）
  B. 其它备份目录（`_bak*` / `_a1_bak*` / `_a9bak*` / `_cleanup_*` / `_rollback_*` …）
     ⇒ 直接 6 个一组，删5留1
  C. 缓存：`__pycache__`、工具工程的 `bin/obj` ⇒ 全删（可重建）
  D. `_bak_trash`（上一轮清理的垃圾桶）⇒ 整目录删

用法：
    python _tools\cleanup_workspace.py            # 干跑，只打印计划
    python _tools\cleanup_workspace.py --apply    # 真删
"""
import os
import re
import shutil
import sys
import time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TOOLS = os.path.join(ROOT, "_tools")
APPLY = "--apply" in sys.argv


def dir_size(p):
    s = 0
    for dp, dn, fn in os.walk(p):
        for f in fn:
            try:
                s += os.path.getsize(os.path.join(dp, f))
            except OSError:
                pass
    return s


def ts_from_name(name):
    m = re.search(r'(20\d{6})[-_](\d{4,6})', name)
    if not m:
        return None
    d, t = m.group(1), m.group(2)
    return time.mktime(time.strptime(d + t[:4], "%Y%m%d%H%M"))


def ts_of(p):
    t = ts_from_name(os.path.basename(p))
    if t:
        return t
    try:
        return os.path.getmtime(p)
    except OSError:
        return 0


def list_dirs(parent, pred):
    out = []
    if not os.path.isdir(parent):
        return out
    for n in os.listdir(parent):
        p = os.path.join(parent, n)
        if os.path.isdir(p) and pred(n):
            out.append(p)
    return out


def plan_keep_newest(items, newest=5, ratio=6):
    """留最新 N；更老的按 ratio 个一组留 1（留每组最新的）。返回 (保留, 删除)。"""
    items = sorted(items, key=ts_of, reverse=True)   # 新→旧
    keep = list(items[:newest])
    rest = list(reversed(items[newest:]))            # 旧→新
    for i, p in enumerate(rest):
        if i % ratio == ratio - 1:
            keep.append(p)
    return keep, [p for p in items if p not in keep]


def plan_ratio(items, ratio=6):
    items = sorted(items, key=ts_of)                 # 旧→新
    keep = [p for i, p in enumerate(items) if i % ratio == ratio - 1]
    return keep, [p for p in items if p not in keep]


def human(n):
    for u in ("B", "KB", "MB", "GB"):
        if n < 1024 or u == "GB":
            return "%.1f %s" % (n, u)
        n /= 1024.0


def report(label, keep, drop):
    size = sum(dir_size(p) for p in drop)
    print("\n── %s ──" % label)
    print("   保留 %d 个：%s" % (len(keep), ", ".join(os.path.basename(p) for p in keep[:8])))
    print("   删除 %d 个（%.1f MB）" % (len(drop), size / 1048576.0))
    for p in drop[:6]:
        print("      - " + os.path.relpath(p, ROOT))
    if len(drop) > 6:
        print("      … 其余 %d 个" % (len(drop) - 6))
    return size, len(drop)


def do_delete(paths):
    if not APPLY:
        return
    for p in paths:
        try:
            shutil.rmtree(p, ignore_errors=True)
        except Exception as e:
            print("   [删失败] %s: %s" % (p, e))


def main():
    total = 0
    count = 0

    # ── A. mod 备份 ──
    a = (list_dirs(TOOLS, lambda n: n.startswith("_instalbak_"))
         + list_dirs(TOOLS, lambda n: n.startswith("_snapshot_"))
         + list_dirs(os.path.join(ROOT, "_backup"), lambda n: n.isdigit())
         + list_dirs(os.path.join(ROOT, "_release", "backup"), lambda n: n.startswith("dev_")))
    keep, drop = plan_keep_newest(a)
    s, c = report("A. mod 备份（留最新 5，其余删5留1）", keep, drop)
    total += s
    count += c
    do_delete(drop)

    # ── B. 其它备份目录 ──
    other = list_dirs(TOOLS, lambda n: bool(re.match(
        r'_(a1_bak|a9bak|abandon_bak|bak|bareid_bak|confirmtok_bak|t2_bak|cleanup|rollback)', n)))
    keep, drop = plan_ratio(other)
    s, c = report("B. 其它备份（删5留1）", keep, drop)
    total += s
    count += c
    do_delete(drop)

    # ── C. 缓存 ──
    caches = []
    for dp, dn, fn in os.walk(ROOT):
        if "\\_tools\\cooking" in dp or "\\_research" in dp:
            continue
        if os.path.basename(dp) == "__pycache__":
            caches.append(dp)
    for base in (TOOLS, os.path.join(ROOT, "mods")):
        if not os.path.isdir(base):
            continue
        for n in os.listdir(base):
            p = os.path.join(base, n)
            if not os.path.isdir(p):
                continue
            for sub in ("bin", "obj", "out"):
                q = os.path.join(p, sub)
                if os.path.isdir(q):
                    caches.append(q)
    s, c = report("C. 缓存（全删，可重建）", [], caches)
    total += s
    count += c
    do_delete(caches)

    # ── D. 垃圾桶 ──
    trash = os.path.join(ROOT, "_bak_trash")
    if os.path.isdir(trash):
        s = dir_size(trash)
        report("D. _bak_trash（上一轮垃圾桶）", [], [trash])
        total += s
        count += 1
        do_delete([trash])

    print("\n%s：%d 项 · 释放 %.1f MB" % ("已删除" if APPLY else "预计删除（干跑）",
                                          count, total / 1048576.0))
    if not APPLY:
        print("确认无误后加 --apply 真删。")


if __name__ == "__main__":
    sys.exit(main())
