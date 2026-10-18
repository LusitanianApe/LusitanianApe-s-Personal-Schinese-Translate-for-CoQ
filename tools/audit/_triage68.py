# -*- coding: utf-8 -*-
"""#68 缺口分诊：串表已覆盖 / 调试内部 / 需要规则。"""
import io
import re

ROOT = None
gaps = [l.split("\t")[0] for l in io.open("_tools/_audit_popup_gaps.txt", encoding="utf-8").read().split("\n") if l.strip()]
strings = io.open("mods/QudZhsCookbook/Strings.xml", encoding="utf-8-sig").read()
ids = set(re.findall(r'<string\b[^>]*\bID="([^"]*)"', strings))

DEBUG_KW = ("DEBUG", "Debug", "Clearing player", "duplication glitch", "No faction found",
            "No skill by that name", "Factionrep", "zone didn't build", "Angor taking a turn")

tbl, dbg, need = [], [], []
for g in gaps:
    if g in ids or g.replace("\\n", "\n") in ids:
        tbl.append(g)
    elif any(k in g for k in DEBUG_KW):
        dbg.append(g)
    else:
        need.append(g)

print("串表已覆盖:", len(tbl))
for g in tbl:
    print("  T", g[:80])
print("调试/内部:", len(dbg))
for g in dbg:
    print("  D", g[:80])
print("需要规则:", len(need))
io.open("_tools/_audit_popup_need.txt", "w", encoding="utf-8", newline="\n").write("\n".join(need) + "\n")
