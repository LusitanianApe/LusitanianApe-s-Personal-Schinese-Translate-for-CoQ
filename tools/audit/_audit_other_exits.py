# -*- coding: utf-8 -*-
r"""批量离线查漏 · 第二弹：**描述/名称/等级文本**的硬编码字面量。

数据源：`_tools\_census\asm-*\literal-owners.tsv`（type/member/literal）
筛选：member ∈ GetDescription / GetDisplayName / GetLevelText / GetShortDescription
判据：含空格 · 长度 ≥ 8（覆盖**拼接碎片**，不只整句）
输出：`_tools\_audit_other_corpus.txt` / `_tools\_audit_other_gaps.txt`
"""
import io
import os
import re
import subprocess

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
T = os.path.join(ROOT, "_tools")
RC = os.path.join(T, "rulecheck", "bin", "Release", "net8.0", "rulecheck.exe")
OUT_CORPUS = os.path.join(T, "_audit_other_corpus.txt")
OUT_GAPS = os.path.join(T, "_audit_other_gaps.txt")

MEMBERS = {"GetDescription", "GetDisplayName", "GetLevelText", "GetShortDescription"}


def main():
    got = {}
    for dp, dn, fn in os.walk(os.path.join(T, "_census")):
        for f in fn:
            if f != "literal-owners.tsv":
                continue
            for line in io.open(os.path.join(dp, f), encoding="utf-8", errors="ignore"):
                p = line.rstrip("\n").split("\t")
                if len(p) < 3:
                    continue
                if p[1] not in MEMBERS:
                    continue
                lit = p[2]
                if len(lit) >= 8 and " " in lit:
                    got[lit] = p[0] + "::" + p[1]
    print("命中文面量:", len(got))
    io.open(OUT_CORPUS, "w", encoding="utf-8", newline="\n").write(
        "\n".join(got.keys()) + "\n")

    r = subprocess.run([RC, OUT_CORPUS, "--call",
                        "Nattens.QudZhsCookbook.ZhsCombatMsgPatch.TranslateText"],
                       capture_output=True, text=True, encoding="utf-8", errors="replace")
    out = r.stdout or ""
    m = re.search(r"有变化 (\d+)", out)
    print("有变化:", m.group(1) if m else "?", "（总", len(got), "）")
    pairs = []
    lines = out.split("\n")
    for i, l in enumerate(lines):
        mm = re.match(r"^\s*→\s+(.*)$", l)
        if mm and i > 0:
            pairs.append((lines[i - 1].strip(), mm.group(1).strip()))
    gaps = [(a, b) for a, b in pairs if a == b]
    io.open(OUT_GAPS, "w", encoding="utf-8", newline="\n").write(
        "\n".join("%s\t%s" % (a, b) for a, b in gaps) + "\n")
    print("未变化(候选缺口):", len(gaps))
    for a, b in gaps[:20]:
        print("  ", a[:110])


if __name__ == "__main__":
    main()
