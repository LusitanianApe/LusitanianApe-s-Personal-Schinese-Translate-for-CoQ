# -*- coding: utf-8 -*-
r"""
把语料拆成两份（测试台的口径必须分开，否则数字没法用）：

  `_msg_corpus.txt`            全部含空格的候选（**碎片+整句混装**）
  `_msg_corpus_sentences.txt`  只留**完整句子** —— 只有这份适合跑 `--residue`

★ 为什么必须分：
   `and cannot be poured from.` 这类是**句尾碎片**，真实消息是
   `X cannot be poured from.`（X = 动态槽）⇒ 单独喂进去永远"译不出"，
   那是**测试口径错**，不是漏译。
   ⇒ 碎片覆盖率由 `_frag_batch_gap.py` 回答（查表里有没有这条碎片）；
     整句覆盖率才由 `rulecheck --residue` 回答。
"""
import io
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "_tools", "_frag_worklist.tsv")
ALL = os.path.join(ROOT, "_tools", "_msg_corpus.txt")
SENT = os.path.join(ROOT, "_tools", "_msg_corpus_sentences.txt")

# 整句判据：首字母大写 + 以 . ! ? 结尾 + 至少 3 个词
SENTENCE = re.compile(r"^[A-Z\"'(].*[.!?]$")


def main():
    rows = [l.rstrip("\n").split("\t") for l in io.open(SRC, encoding="utf-8")][1:]
    allf, sents = [], []
    seen = set()
    for r in rows:
        if not r or not r[0].strip():
            continue
        f = r[0].strip()
        if " " not in f or f in seen:
            continue
        seen.add(f)
        allf.append(f)
        if SENTENCE.match(f) and len(f.split()) >= 3:
            sents.append(f)

    for path, data in ((ALL, allf), (SENT, sents)):
        with io.open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write("\n".join(data) + "\n")

    print("含空格候选 %d 条 → %s" % (len(allf), os.path.basename(ALL)))
    print("★ 完整句子 %d 条 → %s  ← 只有这份适合 --residue"
          % (len(sents), os.path.basename(SENT)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
