# -*- coding: utf-8 -*-
r"""规则夹具校验器：**机器先跑一遍**，再生成一张人工复核表。

用法：
    python _tools\rule_spec_check.py            # 跑全部规则 + 写复核表
    python _tools\rule_spec_check.py --only journal-mapnote-66

输出：
    控制台  每条规则的 正例命中 / 反例误伤
    `_tools\_rule_review_table.md`  人工复核表（每行一条样本，末列留空给用户填）

判据：
    · positive 必须 **整条匹配**（`re.fullmatch` 语义，正则自带 `^…$`）
    · negative 必须 **不匹配**（匹配 = 误伤）
    · `$1..$9` 换成 Python 的 `\1..\9`
"""
import io
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SPEC = os.path.join(ROOT, "_tools", "_rule_spec.json")
TABLE = os.path.join(ROOT, "_tools", "_rule_review_table.md")


def to_py(rep):
    return re.sub(r"\$(\d)", r"\\\1", rep)


def main():
    only = None
    if "--only" in sys.argv:
        i = sys.argv.index("--only")
        only = sys.argv[i + 1] if i + 1 < len(sys.argv) else None

    spec = json.load(io.open(SPEC, encoding="utf-8-sig"))
    rows = []
    total_pos = total_neg = 0
    hit_pos = bad_neg = 0

    print("== 规则夹具校验 ==")
    for r in spec["rules"]:
        if only and r["id"] != only:
            continue
        rx = re.compile(r["regex"])
        pos_ok = neg_ok = 0
        for s in r.get("positives", []):
            total_pos += 1
            m = rx.search(s)
            ok = bool(m)
            if ok:
                hit_pos += 1
                pos_ok += 1
                out = rx.sub(to_py(r["replacement"]), s, count=1)
            else:
                out = ""
            rows.append((r["id"], "正例", s, "命中" if ok else "**漏**", out,
                         r.get("memo", "")))
        for s in r.get("negatives", []):
            total_neg += 1
            m = rx.search(s)
            ok = not m
            if not ok:
                bad_neg += 1
            else:
                neg_ok += 1
            rows.append((r["id"], "反例", s, "未命中" if ok else "**误伤**", "",
                         r.get("memo", "")))
        print("  %-22s 正例 %d/%d · 反例 %d/%d" % (
            r["id"], pos_ok, len(r.get("positives", [])),
            neg_ok, len(r.get("negatives", []))))

    print("\n合计：正例 %d/%d · 反例误伤 %d/%d" % (hit_pos, total_pos, bad_neg, total_neg))

    lines = [
        "# 规则夹具 · 人工复核表",
        "",
        "> 机器已按 `_tools\\_rule_spec.json` 跑过一遍。请重点看「机器」列标了 **漏 / 误伤** 的行。",
        ">",
        "> **判据**：",
        "> · 正例 → `命中` = 正则匹配上了；`漏` = 没匹配上（规则够不到这个形态）",
        "> · 反例 → `未命中` = 正则没匹配（正确）；`误伤` = 正则匹配了（会错误改写）",
        "> · `命中 ≠ 译文好`：命中只说明规则会开火，译文质量看「机器译文」列",
        "",
        "| 规则 | 类型 | 原文 | 机器 | 机器译文 | 人工意见 |",
        "|---|---|---|---|---|---|",
    ]
    for rid, kind, src, verdict, out, memo in rows:
        src = src.replace("|", "\\|")
        out = out.replace("|", "\\|")
        lines.append("| %s | %s | `%s` | %s | %s | %s |" % (rid, kind, src, verdict, out, memo))
    io.open(TABLE, "w", encoding="utf-8", newline="\n").write("\n".join(lines) + "\n")
    print("\n复核表 → " + TABLE)


if __name__ == "__main__":
    sys.exit(main())
