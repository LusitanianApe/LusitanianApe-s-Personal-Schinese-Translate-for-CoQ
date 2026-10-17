# -*- coding: utf-8 -*-
"""
从 `_zh_fragments.py` 的译文表里，按**可达性**重分类，并给出每条的 owner 出处。

分类见 `_tools\\_frag_admit.py` 头注：
    A = 走字符串表（`_S/_T` 的实参）        ⇒ 写 Strings.xml ✅
    C = 只在 C# 拼接路径上                  ⇒ 写 Strings.xml ❌，必须走补丁
    X = callsites 里两者都没找到            ⇒ 去 `literal-owners.tsv` 里再查一次，
                                              仍然没有 ⇒ 抄错/内部串，剔除
用法：python _tools\\_frag_reclass.py [--drop-x]
"""
import io
import os
import re
import sys
from collections import defaultdict

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
GEN = os.path.join(ROOT, "_tools", "_zh_fragments.py")
CENSUS = [os.path.join(ROOT, "_tools", "_census", d)
          for d in sorted(os.listdir(os.path.join(ROOT, "_tools", "_census")))
          if os.path.isdir(os.path.join(ROOT, "_tools", "_census", d))][0]


def load_generator():
    src = io.open(GEN, encoding="utf-8").read()
    ns = {}
    for name in ("TABLE", "INFINITIVE", "STATE", "COMBO", "COMBO_CAP"):
        m = re.search(name + r"\s*=\s*\{", src)
        if not m:
            continue
        i = m.end() - 1
        depth = 0
        j = i
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
    for name in ("TABLE", "INFINITIVE", "STATE", "COMBO", "COMBO_CAP"):
        pairs.update(ns.get(name, {}))
    return src, pairs


def main():
    src, pairs = load_generator()
    print("译文表条目 = %d" % len(pairs))

    in_table = set()
    composed = defaultdict(set)
    with io.open(os.path.join(CENSUS, "callsites.tsv"), encoding="utf-8") as f:
        next(f, None)
        for line in f:
            q = line.rstrip("\n").split("\t")
            if len(q) < 6 or not q[5]:
                continue
            if "Strings::_" in q[3]:
                in_table.add(q[5])
            else:
                composed[q[5]].add(q[3].split("::")[-1])

    owners = defaultdict(set)
    with io.open(os.path.join(CENSUS, "literal-owners.tsv"), encoding="utf-8") as f:
        next(f, None)
        for line in f:
            q = line.rstrip("\n").split("\t")
            if len(q) >= 3:
                owners[q[2]].add("%s::%s" % (q[0].split(".")[-1], q[1]))

    A, C, X = [], [], []
    for k in sorted(pairs):
        if k in in_table:
            A.append(k)
        elif k in composed:
            C.append(k)
        elif k in owners:
            X.append(k)          # 有 owner ⇒ 是真实字面量，只是不在我抽的路径上
        else:
            X.append(k)
    print("A（字符串表）= %d   C（拼接路径）= %d   X（未确认）= %d" % (len(A), len(C), len(X)))
    print()
    print("=== A 类（可直接写 Strings.xml）===")
    for k in A:
        print("   ✓ %s" % k)
    print()
    print("=== X 类里**有 owner** 的（= 真实字面量，只是走拼接 ⇒ 与 C 同类）===")
    real = [k for k in X if k in owners]
    for k in real[:40]:
        print("   · %-52s ← %s" % (k[:52], " · ".join(sorted(owners[k])[:2])))
    print("   共 %d 条" % len(real))
    print()
    ghost = [k for k in X if k not in owners]
    print("=== X 类里**完全查不到 owner** 的（%d 条，应剔除）===" % len(ghost))
    for k in ghost[:30]:
        print("   ✗ %s" % k)

    if "--drop-x" in sys.argv:
        drop = set(ghost)
        lines = src.split("\n")
        out, n = [], 0
        for ln in lines:
            m = re.match(r'\s*"((?:[^"\\]|\\.)*)"\s*:', ln)
            if m and m.group(1) in drop:
                n += 1
                continue
            out.append(ln)
        io.open(GEN, "w", encoding="utf-8", newline="\n").write("\n".join(out))
        print("\n已删除 %d 行" % n)
    return 0


if __name__ == "__main__":
    sys.exit(main())
