# -*- coding: utf-8 -*-
"""
碎片字典**准入验证** —— 每一条全局兜底条目，必须在官方英文模板里**找到出处**。

★ 为什么需要（2026-10-17 踩到）：
   第一版凭"我从反编译碎片里挑的"就往 `Strings.xml` 写了 233 条**全局 ID**，
   `validate_mod.py` 报 **78 条未命中官方模板**。
   ⇒ 我自己定的规则是「只收有出处的」，那就必须**逐条证明出处**，否则
     `ID` 一旦错一个字符，这条就是**死条目**（永远不命中，纯噪声）。
   ⇒ 本脚本按**三种取证**判"可不可以收"：
        ① `(Context, ID)` 精确命中官方 `ExampleLanguage\*.xml`
        ② **裸文本出现**（官方模板里任意位置含这个串；因为同一句可能带别的 Context）
        ③ `_S/_T` 调用点：该串是某个查表调用的 **fallback（arg1）**
     三种全不中 ⇒ **剔除**（记入 `_frag_rejected.tsv`，附原因）

用法：
    python _tools\\_frag_admit.py            # 只报告：哪些可收 / 哪些要剔
    python _tools\\_frag_admit.py --prune    # 直接从 `_tools\\_zh_fragments.py` 的 TABLE 里删掉不合格项
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
GEN = os.path.join(ROOT, "_tools", "_zh_fragments.py")
CENSUS = os.path.join(ROOT, "_tools", "_census")
OFFDIR = os.path.join(_COQ(r"\CoQ_Data"),
                      "StreamingAssets", "Base", "ExampleLanguage")
REJ = os.path.join(ROOT, "_tools", "_frag_rejected.tsv")


def load_official():
    """返回 (全部原文拼接, {(ctx,id)}, {id})"""
    raw = []
    ctx_id = set()
    ids = set()
    for fn in sorted(os.listdir(OFFDIR)):
        if not fn.endswith(".xml"):
            continue
        p = os.path.join(OFFDIR, fn)
        t = io.open(p, encoding="utf-8", errors="replace").read()
        raw.append(t)
        for m in re.finditer(r"<string(?P<a>[^>]*?)/?>", t):
            a = m.group("a")
            idm = re.search(r'ID="([^"]*)"', a)
            if not idm:
                continue
            # 反转义（模板里 &amp; &lt; &#xA; 等）
            v = idm.group(1)
            ids.add(v)
            cm = re.search(r'Context="([^"]*)"', a)
            if cm:
                ctx_id.add((cm.group(1), v))
    return "\n".join(raw), ctx_id, ids


def load_fallbacks():
    """callsites 里 `_S/_T` 的**实参**（arg0=键名 · arg1=fallback 英文）集合。

    ★ 两种来源要分开（2026-10-17 踩到）：
      · 出现在 `_S/_T` 的 **arg0**（无第二参数时）或 **arg1** ⇒ **走字符串表**的句子
        ⇒ 写进 `Strings.xml` 能生效 ✅
      · 只在其它 callee 的参数里（`Messaging.DidX` 之类）⇒ **C# 里拼出来的**
        ⇒ 写 `Strings.xml` **无效**（永远不查表）⇒ 必须走补丁规则 ❌
    """
    in_table = set()     # 走字符串表的
    composed = set()     # 只在拼接路径上的
    for d in sorted(os.listdir(CENSUS)):
        p = os.path.join(CENSUS, d, "callsites.tsv")
        if not os.path.isfile(p):
            continue
        calls = defaultdict(dict)
        with io.open(p, encoding="utf-8") as f:
            next(f, None)
            for line in f:
                q = line.rstrip("\n").split("\t")
                if len(q) < 6:
                    continue
                try:
                    arg = int(q[4])
                except ValueError:
                    continue
                calls[(q[0], q[1], q[2], q[3])][arg] = q[5]
        for (typ, mem, off, callee), args in calls.items():
            is_lookup = "Strings::_" in callee
            for arg, lit in args.items():
                if not lit:
                    continue
                if is_lookup:
                    in_table.add(lit)
                else:
                    composed.add(lit)
    composed -= in_table
    return in_table, composed


def unescape(s):
    return (s.replace("&amp;", "&").replace("&lt;", "<").replace("&gt;", ">")
             .replace("&#xA;", "\n").replace("&#10;", "\n").replace("&quot;", '"'))


def main():
    prune = "--prune" in sys.argv

    # 从生成器里取 TABLE（用 exec 取，避免 import 副作用）
    src = io.open(GEN, encoding="utf-8").read()
    ns = {}
    for name in ("TABLE", "INFINITIVE", "STATE", "COMBO", "COMBO_CAP"):
        m = re.search(name + r"\s*=\s*\{", src)
        if not m:
            continue
        # 取到配对的 }（表里没有嵌套大括号）
        i = m.end() - 1
        depth = 0
        for j in range(i, len(src)):
            if src[j] == "{":
                depth += 1
            elif src[j] == "}":
                depth -= 1
                if depth == 0:
                    break
        ns[name] = eval(src[m.end() - 1:j + 1])
    pairs = {}
    for name in ("TABLE", "INFINITIVE", "STATE", "COMBO", "COMBO_CAP"):
        pairs.update(ns.get(name, {}))
    print("待验证条目 = %d" % len(pairs))

    raw, ctx_id, ids = load_official()
    print("官方模板：%d 文件 · (Context,ID) %d · 唯一 ID %d"
          % (len(os.listdir(OFFDIR)), len(ctx_id), len(ids)))
    in_table, composed = load_fallbacks()
    print("callsites：走字符串表的字面量 %d · 只在拼接路径上的 %d" % (len(in_table), len(composed)))

    # ★★ 三分类（这才是能落地的判据）：
    #   A = 走字符串表 **且** 官方模板有出处   ⇒ 写 Strings.xml，**必定生效**
    #   B = 走字符串表 **但** 官方模板没有      ⇒ 官方自己的缺口，写了也生效（回退串就是键）
    #   C = 只在拼接路径上                     ⇒ 写 Strings.xml **无效** ⇒ 交补丁规则
    A, B, C, X = {}, {}, {}, []
    for k, v in sorted(pairs.items()):
        kk = unescape(k)
        official = ((None, kk) in ctx_id or any(i == kk for _c, i in ctx_id)
                    or kk in ids or kk in raw or kk.replace("&", "&amp;") in raw)
        if kk in in_table:
            if official:
                A[k] = v
            else:
                B[k] = v
        elif kk in composed:
            C[k] = v
        else:
            X.append((k, v))

    print()
    print("★ A 类（走字符串表 + 官方有出处）= %d   ⇒ 直接写 Strings.xml" % len(A))
    print("★ B 类（走字符串表 + 官方无出处）= %d   ⇒ 也写 Shadows…不，也写 Strings.xml（回退串即键）"
          % len(B))
    print("★ C 类（只在拼接路径上）= %d          ⇒ **写 Strings.xml 无效**，交补丁规则"
          % len(C))
    print("★ X 类（两种路径都没找到）= %d          ⇒ 剔除（可能是我抄错，或纯内部串）" % len(X))
    print()
    print("── C 类（补丁规则清单，前 40）──")
    for k in list(C)[:40]:
        print("   ✎ %-56s → %s" % (k[:56], C[k][:26]))
    print()
    print("── X 类（剔除，前 20）──")
    for k, v in X[:20]:
        print("   ✗ %-56s → %s" % (k[:56], v[:26]))

    ok, bad = {}, X
    io.open(REJ, "w", encoding="utf-8", newline="\n").write(
        "fragment\tzh\t类别\n" +
        "\n".join("%s\t%s\tX(两条路径都没有)" % (k, v) for k, v in X) + "\n")
    print("\n已写出 %s" % REJ)

    if prune and bad:
        lines = src.split("\n")
        drop = {k for k, _v in bad}
        out, removed = [], 0
        for ln in lines:
            m = re.match(r'\s*"((?:[^"\\]|\\.)*)"\s*:', ln)
            if m:
                key = m.group(1)
                try:
                    key = key.encode().decode("unicode_escape")
                except Exception:
                    pass
                if key in drop:
                    removed += 1
                    continue
            out.append(ln)
        io.open(GEN, "w", encoding="utf-8", newline="\n").write("\n".join(out))
        print("已从 %s 删除 %d 行（%d 条）" % (os.path.basename(GEN), removed, len(drop)))
        print("★ 下一步：先 --fresh 重来，或直接重跑 --apply（幂等会整段替换）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
