# -*- coding: utf-8 -*-
r"""
`Honorifics` / `Titles` 族普查：**哪个蓝图**用了**哪个值**（用于生成覆盖文件）。

官方 `ObjectBlueprints\Creatures.xml` 里：
    <part Name="Honorifics" Primary="Elder" />
    <part Name="Titles"     Ordinary="dromad merchant" />
官方 `ExampleLanguage\Creatures.example.xml` 给同一批打了 ▶ ⇒ **可译位**（数据侧字面量）。
⇒ 修法：在我方 `Creatures.xml` 里按蓝图 `Load="Merge"` 覆盖这两个 part 的属性。

输出：_tools/_honorifics_census.tsv（blueprint / part / attr / en）
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

BASE = _COQ(r"\CoQ_Data\StreamingAssets\Base")
SRC = os.path.join(BASE, "ObjectBlueprints", "Creatures.xml")
OUT = r"C:\Users\Nattens\Downloads\CoQ-马尔科夫文本调研\_tools\_honorifics_census.tsv"

OBJ = re.compile(r'<object\s+Name="(?P<n>[^"]*)"(?P<a>[^>]*)>', re.I)
PART = re.compile(r'<part\s+Name="(?P<p>Honorifics|Titles)"(?P<attrs>[^>]*?)/?>', re.I)
ATTR = re.compile(r'(Primary|Ordinary|Plural|Singular)="([^"]*)"')


def main():
    t = io.open(SRC, encoding="utf-8", errors="replace").read()
    lines = t.split("\n")

    cur = None
    rows = []
    for i, ln in enumerate(lines, 1):
        m = OBJ.search(ln)
        if m:
            cur = m.group("n")
        pm = PART.search(ln)
        if pm and cur:
            for am in ATTR.finditer(pm.group("attrs")):
                rows.append((cur, pm.group("p"), am.group(1), am.group(2), i))

    print("命中 %d 条（蓝图, part, 属性, 英文值）" % len(rows))
    print()
    by_val = {}
    for bp, part, attr, val, ln in rows:
        by_val.setdefault((part, attr, val), []).append(bp)

    print("=== 去重后的**英文取值** ===")
    for (part, attr, val), bps in sorted(by_val.items()):
        print("  %-12s %-9s %-46s ← %s" % (part, attr, val, " · ".join(sorted(set(bps)))))

    with io.open(OUT, "w", encoding="utf-8", newline="\n") as f:
        f.write("blueprint\tpart\tattr\ten\tline\n")
        for bp, part, attr, val, ln in rows:
            f.write("%s\t%s\t%s\t%s\t%d\n" % (bp, part, attr, val, ln))
    print()
    print("已写出 %s（%d 行）" % (OUT, len(rows)))
    print("可用取值 %d 个（去重）" % len(by_val))
    return 0


if __name__ == "__main__":
    sys.exit(main())
