# -*- coding: utf-8 -*-
"""
骨架碎片工作单 v2 —— 在 `_fragments.tsv` 基础上**二次提纯**，产出可直接填中文的工作单。

★ 提纯规则（把"句子的可能性"与"差分的名字"分开）：
   · **句子/短语**（含空格 ≥2，且词数 ≥3，或首词是虚词）  ⇒ 直接要译文
   · **标识符嫌疑**（Title Case 多词、不含句末标点，如 `Firefighting On Self`）⇒ 单独一档
     —— 它们多半是被当成字面量传给 `Strings._S/_T` 的**半成品键**，不是玩家看到的文本
   · **单词形态**（`ed` / `ing` 单词，如 `dazed` / `healing`）⇒ 单独一档（状态形容词，可复用）

用法：python _tools\\_fragment_worklist.py
输出：_tools\\_frag_worklist.tsv（fragment / kind / zh / 类别 / 出口 / owners / 备注）
"""
import io
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "_tools", "_fragments.tsv")
OUT = os.path.join(ROOT, "_tools", "_frag_worklist.tsv")

FUNC = set("the a an of to in on at with from into by for and or but is are was were be been "
           "your you his her its their it this that as not no out up down over off than then "
           "when while".split())


def classify(frag, kind):
    w = frag.split()
    if kind in ("inf", "prep"):
        return "非谓语/介词短语"
    if kind in ("ing", "ed") and len(w) == 1:
        return "状态词（单词）"
    # 多词
    if re.search(r"[.!?]$", frag):
        return "整句"
    if len(w) >= 3 and (w[0].lower() in FUNC or frag[0].islower()):
        return "短语"
    # Title Case 多词、无标点 ⇒ 标识符嫌疑
    if all(x[:1].isupper() or x.lower() in FUNC for x in w) and len(w) >= 2:
        return "★标识符嫌疑"
    return "短语"


def main():
    rows = [l.rstrip("\n").split("\t") for l in io.open(SRC, encoding="utf-8")]
    hdr, body = rows[0], rows[1:]
    idx = {k: i for i, k in enumerate(hdr)}

    out = ["fragment\tkind\tzh\t类别\tstrong\toutlets\towners"]
    order = {"整句": 0, "短语": 1, "非谓语/介词短语": 2, "状态词（单词）": 3, "★标识符嫌疑": 9}
    items = []
    for r in body:
        frag = r[idx["fragment"]]
        kind = r[idx["kind"]]
        miss = r[idx["already_in_strings"]] == "no"
        if not miss:
            continue
        cat = classify(frag, kind)
        items.append((order.get(cat, 5), cat, frag, kind, r[idx["strong"]],
                      r[idx["outlets"]], r[idx["owners"]]))
    items.sort(key=lambda x: (x[0], -len(x[2]), x[2]))

    for _o, cat, frag, kind, strong, outs, owners in items:
        out.append("\t".join([frag, kind, "", cat, strong, outs[:60], owners[:70]]))

    io.open(OUT, "w", encoding="utf-8", newline="\n").write("\n".join(out) + "\n")

    from collections import Counter
    c = Counter(x[1] for x in items)
    print("写出 %s" % OUT)
    print("待填中文 = %d 条" % len(items))
    for k, v in c.most_common():
        print("   %-16s %4d" % (k, v))
    return 0


if __name__ == "__main__":
    sys.exit(main())
