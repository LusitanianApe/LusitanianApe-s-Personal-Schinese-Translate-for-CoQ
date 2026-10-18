# -*- coding: utf-8 -*-
"""Unit* 模板键覆盖审计：4 个官方 Context，补齐缺失的 Unit Gain Level。"""
import io
import re

P = "mods/QudZhsCookbook/Strings.xml"
t = io.open(P, encoding="utf-8-sig").read()

# 官方 4 个 Unit* 键（ExampleLanguage/Strings.example.xml）
OFFICIAL = [
    ("Unit Add Body Part", "Extra =part.name= slot"),
    ("Unit Cybernetic Install", "Cybernetic implant installed"),
    ("Unit Gain Level", "=amount.signed= =amount.pluralize:level="),
    ("Unit Gain Experience", "=amount.signed= experience"),
]

have = set()
for m in re.finditer(r'<string\b([^>]*)>', t):
    a = m.group(1)
    c = re.search(r'Context="([^"]*)"', a)
    i = re.search(r'ID="([^"]*)"', a)
    if c and i:
        have.add((c.group(1), i.group(1)))

missing = [x for x in OFFICIAL if x not in have]
print("官方 Unit* 键:", len(OFFICIAL), "· 已覆盖:", len(OFFICIAL) - len(missing), "· 缺:", len(missing))
for c, i in missing:
    print("  缺:", c, "|", i)

if missing:
    NEW = ('  <string Context="Unit Gain Level" '
           'ID="=amount.signed= =amount.pluralize:level=">=amount.signed= 级</string>\n')
    anchor = '  <string Context="Unit Gain Experience" ID="=amount.signed= experience">=amount.signed= 经验值</string>\n'
    assert anchor in t
    t = t.replace(anchor, anchor + NEW, 1)
    io.open(P, "w", encoding="utf-8", newline="\n").write(t)
    print("已补 Unit Gain Level")

report = [
    "# Unit* 模板键覆盖审计（2026-10-18）",
    "",
    "官方来源：`Base\\ExampleLanguage\\Strings.example.xml`；DLL #US 里 `Unit ` 开头的字面量也是这 4 个。",
    "",
    "| Context | ID | 状态 |",
    "|---|---|---|",
]
for c, i in OFFICIAL:
    ok = (c, i) in have or c == "Unit Gain Level"
    report.append("| %s | `%s` | %s |" % (c, i, "✅" if ok else "❌ 缺"))
io.open("_tools/_audit_unit_keys.md", "w", encoding="utf-8", newline="\n").write("\n".join(report) + "\n")
print("report → _tools/_audit_unit_keys.md")
