# -*- coding: utf-8 -*-
r"""批量离线查漏：**弹窗 / 消息出口**的硬编码字面量覆盖审计。

数据源：`_tools\_census\asm-*\callsites.tsv`
    type / member / il_offset / callee / arg_index / literal
筛选：
    callee ∈ `XRL.UI.Popup::*` · `XRL.Messages.MessageQueue::*` · `XRL.World.Capabilities.Messaging::*`
    且 literal 像**句子**（含空格、长度 ≥ 12、以 `.`/`!`/`?` 结尾，或含 ` you `/` your `）
输出：
    `_tools\_audit_popup_corpus.txt`  待测语料
    `_tools\_audit_popup_gaps.txt`   译后仍是英文/未变化的行（查漏结果）
用法：
    python _tools\_audit_popup_forms.py
"""
import io
import os
import re
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
T = os.path.join(ROOT, "_tools")
RC = os.path.join(T, "rulecheck", "bin", "Release", "net8.0", "rulecheck.exe")
OUT_CORPUS = os.path.join(T, "_audit_popup_corpus.txt")
OUT_GAPS = os.path.join(T, "_audit_popup_gaps.txt")

CALL = re.compile(r"XRL\.UI\.Popup::|XRL\.Messages\.MessageQueue::|XRL\.World\.Capabilities\.Messaging::")


def sentence_like(s):
    if len(s) < 12 or " " not in s:
        return False
    if s.startswith("//"):
        return False
    if s.endswith((".", "!", "?", "。", "！", "？")):
        return True
    low = s.lower()
    return " you " in low or " your " in low or low.startswith("you ")


def main():
    got = {}
    for dp, dn, fn in os.walk(os.path.join(T, "_census")):
        for f in fn:
            if f != "callsites.tsv":
                continue
            p = os.path.join(dp, f)
            for line in io.open(p, encoding="utf-8", errors="ignore"):
                parts = line.rstrip("\n").split("\t")
                if len(parts) < 6:
                    continue
                callee, lit = parts[3], parts[5]
                if not CALL.search(callee):
                    continue
                if sentence_like(lit):
                    got[lit] = callee
    print("命中文面量:", len(got))
    io.open(OUT_CORPUS, "w", encoding="utf-8", newline="\n").write(
        "\n".join(got.keys()) + "\n")

    r = subprocess.run([RC, OUT_CORPUS, "--call",
                        "Nattens.QudZhsCookbook.ZhsCombatMsgPatch.TranslateText"],
                       capture_output=True, text=True, encoding="utf-8", errors="replace")
    out = r.stdout or ""
    m = re.search(r"有变化 (\d+)", out)
    print("有变化:", m.group(1) if m else "?", "（总", len(got), "）")

    # 逐行比对：语料 → 输出
    lines = io.open(OUT_CORPUS, encoding="utf-8").read().split("\n")
    out_lines = out.split("\n")
    pairs = []
    for i, l in enumerate(out_lines):
        m2 = re.match(r"^\s*→\s+(.*)$", l)
        if m2 and i > 0:
            en = out_lines[i - 1].strip()
            zh = m2.group(1).strip()
            pairs.append((en, zh))
    gaps = [(en, zh) for en, zh in pairs if en == zh]
    io.open(OUT_GAPS, "w", encoding="utf-8", newline="\n").write(
        "\n".join("%s\t%s" % (en, zh) for en, zh in gaps) + "\n")
    print("未变化(候选缺口):", len(gaps))
    for en, zh in gaps[:20]:
        print("  ", en[:100])


if __name__ == "__main__":
    sys.exit(main())
