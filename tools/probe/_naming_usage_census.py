# -*- coding: utf-8 -*-
"""
命名模板的**实际用法普查**：游戏为什么要"抽模板"？

统计官方 `Base\\Naming.xml` 里：
  ① `<templates>` 段里模板条数的分布（几条？）
  ② **权重 ≠ 1** 的模板有多少（权重就是"出镜率"旋钮）
  ③ 挑几个多模板段看**内容差异**（是同义变体，还是按性别/文化/状态分支）
"""
import io
import os

import os as _os
# ── 游戏目录：可用环境变量 COQ_DIR 覆盖（默认指向 Steam 常见安装位置）──
COQ_ROOT = _os.environ.get(
    "COQ_DIR",
    r"C:\\Program Files (x86)\\Steam\\steamapps\\common\\Caves of Qud")
def _COQ(rel):
    return COQ_ROOT + rel

import re
import sys
from collections import Counter

BASE = _COQ(r"\CoQ_Data\StreamingAssets\Base\Naming.xml")


def main():
    t = io.open(BASE, encoding="utf-8", errors="replace").read()

    # 按 namestyle 切块
    blocks = []
    for m in re.finditer(r'<namestyle\s+Name="([^"]*)"[^>]*>(.*?)</namestyle>', t, re.S):
        blocks.append((m.group(1), m.group(2)))
    print("namestyle 总数 = %d" % len(blocks))

    dist = Counter()
    weighted = []
    examples = []
    for nm, body in blocks:
        # 只看 <templates> 段（可能多个）
        for tm in re.finditer(r"<templates[^>]*>(.*?)</templates>", body, re.S):
            seg = tm.group(1)
            items = re.findall(r'<template\s+Name="([^"]*)"([^>]*)/?>', seg)
            dist[len(items)] += 1
            for name, attrs in items:
                wm = re.search(r'Weight="(\d+)"', attrs)
                if wm and int(wm.group(1)) != 1:
                    weighted.append((nm, name, int(wm.group(1))))
            if len(items) >= 4:
                examples.append((nm, [x[0] for x in items]))

    print()
    print("① 每个 <templates> 段的模板条数分布")
    for k in sorted(dist):
        print("     %2d 条 : %3d 段" % (k, dist[k]))

    print()
    print("② 权重 ≠ 1 的模板 = %d 个（权重 = 出镜率旋钮）" % len(weighted))
    for nm, name, w in sorted(weighted, key=lambda x: -x[2])[:20]:
        print("     %-38s %-34s Weight=%d" % (nm[:38], name[:34], w))

    print()
    print("③ 多模板段的内容差异（看是「同义变体」还是「按条件分支」）")
    for nm, names in examples[:10]:
        print("     [%s] %d 条" % (nm, len(names)))
        for n in names[:6]:
            print("         · %s" % n[:88])
    return 0


if __name__ == "__main__":
    sys.exit(main())
