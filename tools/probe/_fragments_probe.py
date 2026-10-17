# -*- coding: utf-8 -*-
"""
碎片普查 —— 把"玩家可见的英文碎片"抄出来，按**骨架（owner 方法）**聚类。

> 起因（用户 2026-10-17）：
>   消息栏里成片「湿的巨蜻蜓俯冲 down to attack.」这类**半中半英**。
>   机制已定案：句子 = **动态名字槽 + 固定动词碎片**（C# 里 `Does("swoop") + " down to attack."`），
>   我们的补丁是**逐词**接管 ⇒ 名字变中文、碎片留英文。
>   ⇒ 要"暴力覆盖"，就得先把**碎片全集**摸清（碎片有限，词与词的组合不是）。

★ 输入（都是既有普查产物，不重新反编译）：
    _tools\\_census\\asm-*\\literals-heap.txt    全部 #US 字面量（上界 34,800）
    _tools\\_census\\asm-*\\callsites.tsv        owner / il_offset / callee / 参数序号 / 字面量
    _tools\\_census\\asm-*\\literal-owners.tsv    字面量 → (类型, 方法)

★ 输出：
    _tools\\_fragments.md        统计 + 代表性样例（给人看）
    _tools\\_fragments.tsv       工作单：fragment / kind / 出现次数 / 出口 / owners / 我方是否已有
"""
import io
import os
import re
import sys
from collections import Counter, defaultdict

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CENSUS = os.path.join(ROOT, "_tools", "_census")
OUT_MD = os.path.join(ROOT, "_tools", "_fragments.md")
OUT_TSV = os.path.join(ROOT, "_tools", "_fragments.tsv")

# 玩家可见出口（出现这些 callee 的方法，其字面量就算"会被玩家看到"）
OUTLETS = (
    "AddPlayerMessage", "EmitMessage", "HandleMessage", "Popup::", "ShowBlock",
    "NewPopupMessageAsync", "ShowPopup", "XDidY", "DidX", "WDidX", "Does",
    "GetDisplayName", "DisplayName", "AddAccomplishment", "AddJournal",
)

# 碎片形态判据（**保守**：只收"一眼是非谓语/介词短语"的，宁可漏不可错）
KINDS = [
    ("inf", re.compile(r"^(?:down |up |back |over |out |in )?to [a-z]+(?: [a-z]+)?$")),
    ("ing", re.compile(r"^[a-z]{3,}ing$")),
    ("ed", re.compile(r"^[a-z]{3,}ed$")),
    ("prep", re.compile(r"^(?:with|into|onto|from|at|in|on|of|by) (?:his|her|its|their|your|the|a|an) [a-z]+$")),
    # ★ 多词碎片：必须**含空格**，且**含至少一个英语虚词**
    #   —— 虚词表是"这是句子碎片、不是标识符"的最强判据
    #      （方法名/配置键如 `GenerateRecipeName` 没有虚词 ⇒ 被排掉）
    ("phrase", re.compile(
        r"^(?=.*\b(?:the|a|an|of|to|in|on|at|with|from|into|by|for|and|or|but|"
        r"is|are|was|were|be|been|your|you|his|her|its|their|it|this|that|"
        r"with|as|not|no|out|up|down|over|off|than|then|when|while)\b)"
        r"[a-z][a-z'\-]*(?: [a-z][a-z'\-]*){1,4}[.!?]?$")),
]


def census_dir():
    for d in sorted(os.listdir(CENSUS)):
        p = os.path.join(CENSUS, d)
        if os.path.isdir(p) and os.path.isfile(os.path.join(p, "callsites.tsv")):
            return p
    return None


def main():
    d = census_dir()
    if not d:
        print("!! 找不到 _census\\asm-*\\callsites.tsv")
        return 2
    print("census =", os.path.basename(d))

    # ── 读 callsites ──
    rows = []
    with io.open(os.path.join(d, "callsites.tsv"), encoding="utf-8") as f:
        next(f, None)
        for line in f:
            p = line.rstrip("\n").split("\t")
            if len(p) >= 6:
                rows.append(p)
    print("callsites 行数 =", len(rows))

    # ── 读 literal-owners（拿"字面量住在哪个方法"）──
    own = defaultdict(set)
    with io.open(os.path.join(d, "literal-owners.tsv"), encoding="utf-8") as f:
        next(f, None)
        for line in f:
            p = line.rstrip("\n").split("\t")
            if len(p) >= 3:
                own[p[2]].add((p[0], p[1]))

    # ── 我方 Strings.xml 已有的 ID 集合（判断"有没有译"）──
    have = set()
    sp = os.path.join(ROOT, "mods", "QudZhsCookbook", "Strings.xml")
    if os.path.isfile(sp):
        t = io.open(sp, encoding="utf-8", errors="replace").read()
        for m in re.finditer(r'ID="([^"]*)"', t):
            have.add(m.group(1))
    print("我方 Strings.xml ID 数 =", len(have))

    # ── 出口侧：哪些方法里有"玩家可见"的调用 ──
    outlet_owners = defaultdict(set)   # (type, member) -> {callee}
    # ★ 强判据：字面量**本身就是**某个出口调用的实参（callsites 行直接可见）
    lit_as_arg = {}                    # literal -> callee（第一个命中）
    for r in rows:
        typ, mem, _off, callee, _arg, lit = r[0], r[1], r[2], r[3], r[4], r[5]
        if any(k in callee for k in OUTLETS):
            outlet_owners[(typ, mem)].add(callee.split("::")[-1][:28])
            if lit and lit not in lit_as_arg:
                lit_as_arg[lit] = callee.split("::")[-1][:28]

    # ── 出现频次（同一 owner 里出现的字面量条数，用来排序"骨架有多富"）──
    per_owner = Counter()
    for (typ, mem) in outlet_owners:
        per_owner[(typ, mem)] = 0
    for lit, owners in own.items():
        for o in owners:
            if o in outlet_owners:
                per_owner[o] += 1

    # ── 逐字面量判形态 ──
    stat = Counter()
    hits = []          # (fragment, kind, owners, outlets, callcount, inStrings)
    for lit, owners in own.items():
        s = lit.strip()
        if not s or len(s) > 60 or len(s.split()) > 5:
            continue
        if not re.fullmatch(r"[A-Za-z][A-Za-z' \-.,!?]*", s):
            continue
        kind = None
        for k, rx in KINDS:
            if rx.fullmatch(s.lower()):
                kind = k
                break
        if kind is None:
            continue
        outs = set()
        cnt = 0
        for (typ, mem) in owners:
            o = outlet_owners.get((typ, mem))
            if o:
                outs |= o
                cnt += 1
        stat[kind] += 1
        if outs:
            strong = s in lit_as_arg
            hits.append((s, kind, sorted(owners)[:3], sorted(outs)[:3], cnt, s in have, strong))

    # ── 按形态聚类，按"是否已在串表"排序 ──
    lines = []
    add = lines.append
    add("# 玩家可见英文碎片普查（含非谓语）\n")
    add("> 生成：`python _tools\\_fragments_probe.py` · 输入 = `_tools\\_census\\%s`\n"
        % os.path.basename(d))
    add("## 一、形态分布（在**玩家可见出口**的方法里出现的）\n")
    add("```")
    add("  形态          条目数")
    for k, _ in KINDS:
        add("  %-12s %5d" % (k, stat[k]))
    add("  %-12s %5d" % ("合计（过筛）", sum(stat.values())))
    add("```\n")

    add("## 二、缺译碎片（我方 Strings.xml 里**没有**这条 ID）\n")
    miss = [h for h in hits if not h[5]]
    strong = [h for h in miss if h[6]]
    add("```")
    add("  缺译 %d 条 / 命中 %d 条" % (len(miss), len(hits) - len(miss)))
    add("  ★ 其中**强判据**（字面量本身就是出口实参）= %d 条  ← 优先做这批" % len(strong))
    add("```\n")
    add("### ★ 强判据缺译（%d 条，字面量直接喂给 `AddPlayerMessage`/`EmitMessage`/`Popup` 等）\n"
        % len(strong))
    add("```")
    for s, k, owners, outs, cnt, _h, _s in sorted(strong, key=lambda x: (x[1], x[0])):
        add("  %-34s [%-5s] %s" % (s, k, " · ".join(outs)))
    add("```\n")
    by_kind = defaultdict(list)
    for h in miss:
        by_kind[h[1]].append(h)
    for k, _ in KINDS:
        g = by_kind.get(k) or []
        if not g:
            continue
        add("### 弱判据 · `%s`（%d 条）\n" % (k, len(g)))
        add("```")
        for s, _k, owners, outs, cnt, _h, _s in sorted(g, key=lambda x: (-x[4], x[0]))[:40]:
            add("  %-30s ← %s" % (s, " · ".join("%s.%s" % o for o in owners)))
            add("  %-30s   出口: %s" % ("", " · ".join(outs)))
        add("```\n")

    add("## 三、已在串表里的（说明另有出口没走到）\n")
    add("```")
    for s, k, owners, outs, cnt, _h, _s in sorted([h for h in hits if h[5]], key=lambda x: x[0])[:40]:
        add("  %-30s [%s] 出口: %s" % (s, k, " · ".join(outs)))
    add("```")

    # ── 骨架聚类：按 owner 方法看"这个方法一共吐多少碎片" ──
    add("\n## 四、★ 骨架聚类 —— 按 owner 方法看碎片全集（这就是要整表做的地方）\n")
    per = defaultdict(list)
    for s, k, owners, outs, cnt, inhave, _st in hits:
        first = owners[0]
        per[first].append((s, k, inhave))
    ranked = sorted(per.items(), key=lambda kv: -len(kv[1]))
    add("```")
    add("  含可见碎片的 owner 方法数 = %d" % len(ranked))
    add("")
    add("  碎片最多的前 25 个方法：")
    for (typ, mem), items in ranked[:25]:
        add("    %-52s %3d 条碎片" % ("%s::%s" % (typ.split(".")[-1], mem), len(items)))
    add("```\n")
    add("### 前 6 个方法的**碎片全集**（可直接照这个写中文）\n")
    for (typ, mem), items in ranked[:6]:
        add("#### `%s::%s`（%d 条）\n" % (typ, mem, len(items)))
        add("```")
        for s, k, inhave in sorted(items):
            add("  %-34s [%-5s] %s" % (s, k, "已有" if inhave else "缺译"))
        add("```\n")

    io.open(OUT_MD, "w", encoding="utf-8", newline="\n").write("\n".join(lines))

    # ── TSV 工作单 ──
    tsv = ["fragment\tkind\tstrong\towners\toutlets\talready_in_strings"]
    for s, k, owners, outs, cnt, inhave, strong in sorted(hits, key=lambda x: (x[1], -x[4], x[0])):
        tsv.append("%s\t%s\t%s\t%s\t%s\t%s" % (
            s, k, "yes" if strong else "no",
            ";".join("%s.%s" % o for o in owners),
            ";".join(outs),
            "yes" if inhave else "no"))
    io.open(OUT_TSV, "w", encoding="utf-8", newline="\n").write("\n".join(tsv) + "\n")

    print("写出 %s（%d 行）" % (OUT_MD, len(lines)))
    print("写出 %s（%d 条）" % (OUT_TSV, len(tsv) - 1))
    print()
    print("形态分布:", dict(stat))
    print("缺译:", len(miss), " / 命中:", len(hits) - len(miss))
    return 0


if __name__ == "__main__":
    sys.exit(main())
