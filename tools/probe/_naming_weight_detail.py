import os as _os
# ── 游戏目录：可用环境变量 COQ_DIR 覆盖（默认指向 Steam 常见安装位置）──
COQ_ROOT = _os.environ.get(
    "COQ_DIR",
    r"C:\\Program Files (x86)\\Steam\\steamapps\\common\\Caves of Qud")
def _COQ(rel):
    return COQ_ROOT + rel

# -*- coding: utf-8 -*-
"""看几个"多模板段"的**完整内容与权重**，判断抽取意图（变体轮换 / 稀有度）。"""
import io
import re
import sys

BASE = _COQ(r"\CoQ_Data\StreamingAssets\Base\Naming.xml")
WANT = ["Merchant Title", "Tinker Title", "Apothecary Title",
        "Goatfolk Site", "Qlippoth Site", "Godhed Hero Title",
        "Newly Sentient Being Hero Title"]


def main():
    t = io.open(BASE, encoding="utf-8", errors="replace").read()
    for w in WANT:
        m = re.search(r'<namestyle\s+Name="%s"[^>]*>(.*?)</namestyle>' % re.escape(w), t, re.S)
        if not m:
            print("（找不到 %s）" % w)
            continue
        body = m.group(1)
        print("=" * 78)
        print("### %s" % w)
        sm = re.search(r"<scopes>(.*?)</scopes>", body, re.S)
        if sm:
            print("  scopes:")
            for s in re.findall(r"<scope[^>]*/>", sm.group(1)):
                print("     " + s.strip()[:150])
        for tm in re.finditer(r"<templates[^>]*>(.*?)</templates>", body, re.S):
            items = re.findall(r'<template\s+Name="([^"]*)"([^>]*)/?>', tm.group(1))
            tot = 0
            rows = []
            for name, attrs in items:
                wm = re.search(r'Weight="(\d+)"', attrs)
                wt = int(wm.group(1)) if wm else 1
                tot += wt
                rows.append((name, wt))
            print("  templates（共 %d 条 · 权重合计 %d）:" % (len(rows), tot))
            for name, wt in rows:
                print("     %5.1f%%  Weight=%-5d %s" % (100.0 * wt / tot, wt, name[:88]))
    return 0


if __name__ == "__main__":
    sys.exit(main())
