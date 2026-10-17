# -*- coding: utf-8 -*-
"""
液体 `<part Name="…" Alt="…">` 缺口普查。

★ 起因（用户 2026-10-17 截图）：物品栏显示 `[29 drams of fresh water]` ——
   `fresh water` 来自官方 `Liquids.xml` 的 `<part Name="AltNameWhenPure"><alt>…`，
   而**官方 ExampleLanguage\Liquids.example.xml 明确给了本地化口**：
       <part Name="AltNameWhenPure" Alt="▶{{B|fresh water}}" />
   我方 `Liquids.xml` 只译了 displayName / adjective / smearedName …，**漏了这些 part 的 Alt 属性**。

输出：_tools\\_liquid_parts.md（缺口清单，可直接照着填）
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
from collections import defaultdict

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BASE = _COQ(r"\CoQ_Data\StreamingAssets\Base")
OURS = os.path.join(ROOT, "mods", "QudZhsCookbook", "Liquids.xml")
OUT = os.path.join(ROOT, "_tools", "_liquid_parts.md")

LIQ = re.compile(r"<liquid\s+Name=\"(?P<name>[^\"]*)\">(?P<body>.*?)</liquid>", re.S)
PART_ALT = re.compile(r"<part\s+Name=\"(?P<part>[^\"]*)\"\s+Alt=\"(?P<alt>[^\"]*)\"\s*/>")
PART_BLOCK = re.compile(r"<part\s+Name=\"(?P<part>[^\"]*)\"\s*>(?P<body>.*?)</part>", re.S)
ALT_IN = re.compile(r"<alt>(?P<alt>.*?)</alt>", re.S)


def read(p):
    return io.open(p, encoding="utf-8", errors="replace").read()


def collect(path):
    """{liquid: {part: alt}}"""
    t = read(path)
    out = defaultdict(dict)
    for m in LIQ.finditer(t):
        nm, body = m.group("name"), m.group("body")
        for pm in PART_ALT.finditer(body):
            out[nm][pm.group("part")] = pm.group("alt")
        for pm in PART_BLOCK.finditer(body):
            am = ALT_IN.search(pm.group("body"))
            if am:
                out[nm][pm.group("part")] = am.group("alt").strip()
    return out


def main():
    off = collect(os.path.join(BASE, "ExampleLanguage", "Liquids.example.xml"))
    ours = collect(OURS)
    print("官方模板里带 Alt 的液体 = %d · 条目 %d"
          % (len(off), sum(len(v) for v in off.values())))
    print("我方已写的 = %d · 条目 %d" % (len(ours), sum(len(v) for v in ours.values())))

    lines = ["# 液体 `<part … Alt=…>` 缺口（官方模板给了本地化口）\n",
             "> 生成：`python _tools\\_liquid_parts_probe.py`\n"]
    miss_total = 0
    for nm in sorted(off):
        miss = {p: a for p, a in off[nm].items() if p not in ours.get(nm, {})}
        if not miss:
            continue
        miss_total += len(miss)
        lines.append("\n## `%s`（缺 %d）\n" % (nm, len(miss)))
        lines.append("```xml")
        for p, a in sorted(miss.items()):
            lines.append('    <part Name="%s" Alt="%s" />' % (p, a.replace("▶", "")))
        lines.append("```")
    lines.append("\n**合计缺口 = %d 条**\n" % miss_total)
    io.open(OUT, "w", encoding="utf-8", newline="\n").write("\n".join(lines))

    print("★ 缺口 = %d 条 → %s" % (miss_total, OUT))
    print()
    for nm in sorted(off):
        miss = {p: a for p, a in off[nm].items() if p not in ours.get(nm, {})}
        if miss:
            for p, a in sorted(miss.items()):
                print("   %-18s %-34s %s" % (nm, p, a))
    return 0


if __name__ == "__main__":
    sys.exit(main())
