# -*- coding: utf-8 -*-
"""
碎片字典**离线自检** —— 用同一套正则语义（Python 复刻）在真实语料上跑一遍。

★ 为什么要它（本项目 §"自证 ≠ 验证"）：
   改完 C# 不能只靠"编译通过"。这里用 Python 按**同一条判据**
   （左不接字母与点、右不接字母）复刻匹配，并在：
     ① 我写的中英混排样例（含**反例**）
     ② 上一局真实消息栏（`_tools\\_消息栏-真消息.txt`）
   上跑一遍，报告：命中条数 + 命中样本 + **反例是否被误伤**。

用法：python _tools\\_frag_selfcheck.py
"""
import io
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
GEN = os.path.join(ROOT, "_tools", "_zh_fragments.py")
MSG = os.path.join(ROOT, "_tools", "_消息栏-真消息.txt")

A_CLASS = {"...", "DynamicQuest you recive an item", "You cannot do that here."}

CASES = [
    ("湿的巨蜻蜓俯冲 down to attack.", ["down to attack"]),
    # ★ 2026-10-17：`Because they X` 大写变体（实际串首字母大写）
    ("Because they despise Elder 伊鲁达德", ["Because they despise "]),
    ("because they despise X", ["because they despise "]),
    ("Because they admire Y", ["Because they admire "]),
    ("You share your 水 with 恩图 and begin the water ritual.",
     [" and begin the water ritual.", "You share your "]),
    ("may be ineffective unless stocked.", ["may be ineffective unless stocked."]),
    ("into pull", []),                                  # ★ 反例：不该被 `to pull` 命中
    ("The 湿的 光鱼 down to attack.", ["down to attack"]),
    ("你已经在地上 on the ground", ["on the ground"]),
    ("你涉过池子的咸的 水。", []),                        # ★ 反例：无碎片
    ("a stream of corrosive acid pours out all over you!",
     ["a stream of corrosive acid", "pours out all over you!"]),
]


def load_pairs():
    src = io.open(GEN, encoding="utf-8").read()
    ns = {}
    for name in ("TABLE", "INFINITIVE", "STATE", "COMBO", "COMBO_CAP"):
        m = re.search(name + r"\s*=\s*\{", src)
        if not m:
            continue
        i = m.end() - 1
        depth, j = 0, i
        while j < len(src):
            if src[j] == "{":
                depth += 1
            elif src[j] == "}":
                depth -= 1
                if depth == 0:
                    break
            j += 1
        ns[name] = eval(src[i:j + 1])
    pairs = {}
    for n in ("TABLE", "INFINITIVE", "STATE", "COMBO", "COMBO_CAP"):
        pairs.update(ns.get(n, {}))
    return {k: v for k, v in pairs.items() if k not in A_CLASS}


def main():
    pairs = load_pairs()
    print("碎片表 = %d 条" % len(pairs))
    items = sorted(pairs.items(), key=lambda kv: (-len(kv[0]), kv[0]))
    def right_for(frag):
        """★ 与 C# `Compile()` 同口径：只有以字母结尾才要求右侧不是字母。"""
        if not frag:
            return ""
        # ★ 只看字面最后一个字符（不 rstrip）：尾空格 = 前缀拼接件 ⇒ 不加右边界
        return r"(?![A-Za-z])" if frag[-1].isascii() and frag[-1].isalpha() else ""

    rules = [(re.compile(r"(?<![A-Za-z.])" + re.escape(k) + right_for(k)), v, k)
             for k, v in items]

    def apply(t):
        hit = []
        neo = t
        for rx, zh, k in rules:
            if rx.search(neo):
                hit.append(k)
                neo = rx.sub(zh, neo)
        return neo, hit

    print()
    print("=" * 78)
    print("① 设计样例（含反例）")
    print("=" * 78)
    bad = 0
    for src, expect in CASES:
        neo, hit = apply(src)
        ok = sorted(hit) == sorted(expect)
        if not ok:
            bad += 1
        print("  [%s] %s" % ("OK " if ok else "FAIL", src))
        print("        命中 %s（期望 %s）" % (hit, expect))
        if neo != src:
            print("        ⇒ %s" % neo)
    print()
    if bad:
        print("!! %d 个样例不符预期" % bad)
    else:
        print("样例全部符合预期 ✅")

    if not os.path.isfile(MSG):
        print("\n（找不到 %s，跳过真实语料）" % MSG)
        return 1 if bad else 0

    print()
    print("=" * 78)
    print("② 真实消息栏语料（%s）" % os.path.basename(MSG))
    print("=" * 78)
    lines = io.open(MSG, encoding="utf-8", errors="replace").read().split("\n")
    hits = []
    for ln in lines:
        if not ln.strip():
            continue
        neo, hit = apply(ln)
        if hit:
            hits.append((ln.strip(), neo.strip(), hit))
    print("  语料行数 = %d · **会被改动**的行 = %d" % (len(lines), len(hits)))
    from collections import Counter
    c = Counter(k for _s, _n, hs in hits for k in hs)
    print("  命中碎片 TOP：")
    for k, n in c.most_common(20):
        print("     %-46s ×%d" % (k[:46], n))
    print()
    print("  样例（前 12 行改动）：")
    for s, n, hs in hits[:12]:
        print("     原: %s" % s[:110])
        print("     新: %s" % n[:110])
        print("     ← %s" % hs)
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
