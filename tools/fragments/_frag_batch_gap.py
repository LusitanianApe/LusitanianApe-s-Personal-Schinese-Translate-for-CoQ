# -*- coding: utf-8 -*-
r"""
★★ 碎片字典**批量差集**（回答"雷霆正则空了怎么批量解决"）

思路：**不再靠截图撞**。把「DLL 里所有可能出现在消息里的英文碎片」当作**有限全集**，
      与「我方现表」做差集 ⇒ 得到**待译清单**，按可达性分档，批量填。

★ 输入
    _tools\_zh_fragments.py               我方现表（TABLE / INFINITIVE / STATE / COMBO / COMBO_CAP）
    _tools\_frag_worklist.tsv             已提纯的候选（653 条，带 类别 / 出口 / owners）
    _tools\_fragments.tsv                 原始普查（备查）

★ 输出
    _tools\_frag_gap.tsv                  差集工作单：fragment / 类别 / 出口 / owners / 缺口类型
    _tools\_frag_gap.md                   汇总（给人看：几类、各多少、怎么批量填）

★ 缺口类型（决定怎么填）
    `missing`     现表完全没有
    `case`        现表有**另一个大小写**形态（`because…` 有、`Because…` 无）
    `punct`       现表有**去掉句末标点**的形态（或反之）
    `prefix`      现表有**更长的**以它开头（可能是子串，不必单列）
    `covered`     已有

用法：
    python _tools\_frag_batch_gap.py            # 干跑
    python _tools\_frag_batch_gap.py --write    # 写文件
"""
import io
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
T = os.path.join(ROOT, "_tools")
FRAG = os.path.join(T, "_zh_fragments.py")
WORK = os.path.join(T, "_frag_worklist.tsv")
OUT_TSV = os.path.join(T, "_frag_gap.tsv")
OUT_MD = os.path.join(T, "_frag_gap.md")

DICTS = ("TABLE", "INFINITIVE", "STATE", "COMBO", "COMBO_CAP")

# 出口白名单：真正打到玩家眼前的
STRONG_OUTLETS = {"AddPlayerMessage", "Show", "ShowYesNo", "ShowBlock", "DisplayMessage",
                  "SetText", "Popup", "EmitMessage", "AddMessage"}


def load_pairs():
    src = io.open(FRAG, encoding="utf-8").read()
    ns = {}
    for name in DICTS:
        m = re.search(r"^%s\s*=\s*\{" % name, src, re.M)
        if not m:
            continue
        i = src.index("{", m.start())
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
    d = {}
    for n in DICTS:
        d.update(ns.get(n, {}))
    return d


def main():
    write = "--write" in sys.argv
    pairs = load_pairs()
    keys = set(pairs)
    lower = {}
    for k in keys:
        lower.setdefault(k.lower(), []).append(k)

    rows = [l.rstrip("\n").split("\t")
            for l in io.open(WORK, encoding="utf-8")][1:]
    print("现表 = %d 条 · 候选 = %d 条" % (len(keys), len(rows)))

    out = []
    stat = {}
    for r in rows:
        if len(r) < 7:
            continue
        frag, kind, zh, cat, strong, outlets, owners = r[:7]
        f = frag.strip()
        if not f:
            continue

        # ── 判缺口 ──
        gap = None
        if f in keys:
            gap = "covered"
        elif f.lower() in lower:
            gap = "case"
        elif f.rstrip(".!?") in keys or (f + ".") in keys or (f + "!") in keys:
            gap = "punct"
        elif any(k.startswith(f) for k in keys):
            gap = "prefix"
        else:
            gap = "missing"

        stat[gap] = stat.get(gap, 0) + 1

        # ── ★ 分流：单 token（无空格、纯字母/下划线）= `Show(ID)` 的**键**，不是显示文本 ──
        #   实证：`HindrenMysteryCriticalNPCKilled` / `StructureScannerEquipped` /
        #         `CookingAndGathering` 全是事件/成就/选项 ID，走 `_S(key, 英文)` 查表，
        #         不该进碎片字典（进了也只会误伤）。
        route = "frag"
        if re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", f):
            route = "id"
        elif cat == "★标识符嫌疑":
            route = "id?"
        out.append((f, kind, zh, cat, strong, outlets, owners, gap, route))

    # ── 只保留真缺口（且出口是强出口 / 类别不是标识符嫌疑）──
    real = [r for r in out
            if r[7] in ("missing", "case", "punct")
            and r[8] == "frag"]
    real_strong = [r for r in real
                   if any(o.strip() in STRONG_OUTLETS for o in r[5].split(";"))]
    ids = [r for r in out if r[8] in ("id", "id?") and r[7] != "covered"]

    print()
    print("=== 缺口分类 ===")
    for k in ("covered", "case", "punct", "prefix", "missing"):
        print("   %-8s %4d" % (k, stat.get(k, 0)))
    print()
    print("分流：**键/ID**（单 token）待查表 = %d 条 · 真碎片缺口 = %d 条"
          % (len(ids), len(real)))
    print("      其中走**强出口**（消息栏/弹窗/文本）= %d 条" % len(real_strong))

    by_cat = {}
    for r in real_strong:
        by_cat.setdefault(r[3], []).append(r)
    print()
    print("=== 按类别（强出口）===")
    for k, v in sorted(by_cat.items(), key=lambda kv: -len(kv[1])):
        print("   %-18s %4d" % (k, len(v)))

    print()
    print("=== 样板（前 20，强出口 + case 优先）===")
    real_strong.sort(key=lambda r: (r[7] != "case", r[3], -len(r[0])))
    for r in real_strong[:20]:
        print("   [%-6s] %-58r ← %s" % (r[7], r[0][:58], r[5][:26]))

    # ── ★ 第四层过滤：调试/内部消息 ──
    #   实证样板里大量是控制台/wish/调试文案（`trade move to container`、
    #   `EquipmentLoadoutManager Update a loadout`、`RandomAltarBaetyl shouldn't hit this`）
    #   —— 玩家正常游戏**看不到**，填了白填（还可能误伤同形中文）。
    DEBUG_HINT = ("Debug", "Wish", "Test", "Dev", "Console", "TradeUI", "Loadout",
                  "shouldn't", "should not", "picked for you")
    DEBUG_FRAG = ("trade move", "Hostile walk radius", "picked for you",
                  "shouldn't hit this", "Update a loadout", "delete a loadout",
                  "Factionrep set", "Alarms blare across the enclave")

    def is_debug(r):
        blob = r[0] + "|" + r[6]
        if any(h.lower() in blob.lower() for h in DEBUG_HINT):
            return True
        return any(r[0].startswith(p) for p in DEBUG_FRAG)

    player = [r for r in real_strong if not is_debug(r)]
    dbg = [r for r in real_strong if is_debug(r)]
    print()
    print("★ 第四层过滤（调试/内部消息）：剔除 %d 条 ⇒ **玩家可见真缺口 = %d 条**"
          % (len(dbg), len(player)))
    print()
    print("=== 玩家可见清单（全量）===")
    for r in sorted(player, key=lambda x: (x[3], x[0])):
        print("   [%-6s] %-60r ← %s" % (r[7], r[0][:60], r[5][:24]))

    if write:
        with io.open(OUT_TSV, "w", encoding="utf-8", newline="\n") as f:
            f.write("fragment\tkind\tzh\t类别\tstrong\toutlets\towners\tgap\n")
            for r in sorted(real_strong, key=lambda x: (x[3], x[0])):
                f.write("\t".join(r) + "\n")
        md = ["# 碎片字典批量差集\n",
              "现表 %d 条 · 候选 %d 条 · 真缺口 %d · 强出口 %d · **玩家可见 %d**\n"
              % (len(keys), len(rows), len(real), len(real_strong), len(player)),
              "## 按类别（玩家可见）\n", "| 类别 | 条数 |", "|---|---|"]
        for k, v in sorted({c: [x for x in player if x[3] == c]
                            for c in set(x[3] for x in player)}.items(),
                           key=lambda kv: -len(kv[1])):
            md.append("| %s | %d |" % (k, len(v)))
        md.append("\n## 清单\n")
        for r in sorted(player, key=lambda x: (x[3], x[0])):
            md.append("- `%s` ← %s" % (r[0], r[5]))
        io.open(OUT_MD, "w", encoding="utf-8", newline="\n").write("\n".join(md) + "\n")
        print()
        print("已写出 %s / %s" % (OUT_TSV, OUT_MD))
    return 0


if __name__ == "__main__":
    sys.exit(main())
