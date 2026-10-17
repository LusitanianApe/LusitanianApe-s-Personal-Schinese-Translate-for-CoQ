# -*- coding: utf-8 -*-
r"""★ ③-C-c：**路由计数基线**（日语组 `validate_pattern_routes.py --expect-count` 的同款做法）

为什么需要：我方有 **123 条弹窗精确表 + 70 条 REASM 规则 + 35 条整行表 + 数列单位规则 +
90 余张译名表**，全是「一改就可能悄悄弄坏别处」的结构 —— 现在**没有任何计数基线**，
规则被误删/重复插入时**没有任何信号**（§45 门禁纪律的缺口）。

做法：
  ① 从源码里按**表名**提取条目数（正则，逐表一个提取器）
  ② 基线存 `_tools\\计数基线.json`（**人工确认后才更新**）
  ③ `--check` 比对：任何一张表数目变了就**报红并说明差多少**，提示「要么回退、要么更新基线」

用法：
  python _tools\_count_baseline.py            # 打印当前计数；基线不存在则创建
  python _tools\_count_baseline.py --check    # 与基线比对（收尾四步用）
  python _tools\_count_baseline.py --write    # 明确更新基线（改了规则表之后）
"""
import io
import json
import os
import re
import sys

sys.stdout.reconfigure(encoding="utf-8")
WS = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MOD = os.path.join(WS, "mods", "QudZhsCookbook")
SCR = os.path.join(MOD, "Scripts")
BASE = os.path.join(WS, "_tools", "计数基线.json")


def read(name):
    p = os.path.join(SCR, name)
    return io.open(p, encoding="utf-8", errors="replace").read() if os.path.isfile(p) else ""


def seg(t, start, end="};"):
    i = t.find(start)
    if i < 0:
        return ""
    j = t.find(end, i)
    return t[i:j if j > 0 else len(t)]


def count_pairs(s):
    """数 `{ "…", "…" },` 形态的条目"""
    return len(re.findall(r'\{\s*"(?:[^"\\]|\\.)*"\s*,', s))


def collect():
    P = read("ZhsPopupPatch.cs")
    U = read("ZhsUnits.cs")
    M = read("ZhsMutationZh.cs")
    LT = read("ZhsMutationZh_LevelText.cs")
    HM = read("ZhsHardcodedMsg.cs")
    VB = read("ZhsVerbPatch.cs")
    VF = read("ZhsVerbFormPatch.cs")
    CM = read("ZhsCombatMsgPatch.cs")
    BS = read("ZhsBootstrap.cs")
    return {
        "ZhsPopupPatch.cs": {
            "L（弹窗精确表）": count_pairs(seg(P, "Dictionary<string, string> L")),
            "Line（整行精确表）": count_pairs(seg(P, "Dictionary<string, string> Line")),
            "REASM（正则模板表）": len(re.findall(r"new System\.Text\.RegularExpressions\.Regex\(",
                                                 seg(P, "REASM ="))),
            "REASON（固定原因短语）": count_pairs(seg(P, "Dictionary<string, string> REASON")),
        },
        "ZhsUnits.cs": {
            "Values（取值词表）": len(re.findall(r"new\[\]\s*\{", seg(U, "string[][] Values"))),
            "RuleUnits（rules 后单位词）": count_pairs(seg(U, "Dictionary<string, string> RuleUnits")),
            "单位正则": len(re.findall(r"new Regex\(", U)),
        },
        "ZhsMutationZh.cs": {
            "NameByOrig": count_pairs(seg(M, "Dictionary<string, string> NameByOrig")),
            "NameByClass": count_pairs(seg(M, "Dictionary<string, string> NameByClass")),
            "Bearer": count_pairs(seg(M, "Dictionary<string, string> Bearer")),
            "Desc（说明段落）": count_pairs(seg(M, "Dictionary<string, string[]> Desc")),
        },
        "ZhsMutationZh_LevelText.cs": {
            "ByClass（等级文本）": count_pairs(seg(LT, "Dictionary<string, string[]>")),
        },
        "ZhsHardcodedMsg.cs": {"规则/条目": len(re.findall(r'^\s*\{\s*"', HM, re.M))},
        # ★ 2026-10-16：动词表原来**没被跟踪**（改它没有信号 = 监控盲区）⇒ 补上
        "ZhsVerbPatch.cs": {
            "V（=verb: 动词表）": len(re.findall(r'\{\s*"', seg(VB, "Dictionary<string, string> V"))),
        },
        "ZhsVerbFormPatch.cs": {
            # ★ 2026-10-16 补：子代理新增的系词/助动词**排除表**（不回查 V 表）
            # ★ 2026-10-16：改成**声明锚定**（原 seg() 抓到注释里的那次出现 ⇒ 读数 0）
            "AuxSkip（排除的系词/助动词）": len(re.findall(
                r'"[a-z]{2,}"',
                (re.search(r"(?m)^(?!\s*//).*?\bAuxSkip\b.*$", VF) or [None]) and
                VF[(re.search(r"(?m)^(?!\s*//).*?\bAuxSkip\b.*$", VF).start()):
                   VF.find("};", re.search(r"(?m)^(?!\s*//).*?\bAuxSkip\b.*$", VF).end())]
                if re.search(r"(?m)^(?!\s*//).*?\bAuxSkip\b.*$", VF) else "")),
            "EnVerbs（变形直通闸已知动词）": len(re.findall(r'"[a-z]{2,}"',
                                                          seg(VF, "EnVerbs"))),
        },
        "ZhsCombatMsgPatch.cs": {"规则/条目": len(re.findall(r'^\s*R\(', CM, re.M))},
        "ZhsBootstrap.cs": {
            "MountOff 挂载点": len(re.findall(r"MountOff\(", BS)),
        },
    }


def main():
    cur = collect()
    mode = sys.argv[1] if len(sys.argv) > 1 else ""
    total = sum(sum(v.values()) for v in cur.values())

    if mode == "--check":
        if not os.path.isfile(BASE):
            print("★ 计数基线不存在 ⇒ 请先跑一次 `python _tools\\_count_baseline.py --write`")
            return 1
        base = json.load(io.open(BASE, encoding="utf-8"))
        bad = []
        for f, d in base.items():
            for k, v in d.items():
                now = cur.get(f, {}).get(k)
                if now != v:
                    bad.append((f, k, v, now))
        if bad:
            print("★ 计数基线**不一致**（%d 处）—— 规则表被改动过：" % len(bad))
            for f, k, old, now in bad:
                print("   %-28s %-24s 基线 %s ⇒ 现在 %s" % (f, k, old, now))
            print("   处理：① 若是误删/误增 ⇒ 回退；② 若是有意改动 ⇒ 跑 `--write` 更新基线并写进 patch notes")
            return 1
        print("★ 计数基线一致（%d 张表 · 共 %d 条）✓" % (sum(len(v) for v in cur.values()), total))
        return 0

    for f, d in cur.items():
        print("%-30s %s" % (f, " · ".join("%s=%d" % (k, v) for k, v in d.items())))
    print("合计 %d 条" % total)
    if mode == "--write" or not os.path.isfile(BASE):
        io.open(BASE, "w", encoding="utf-8", newline="\n").write(
            json.dumps(cur, ensure_ascii=False, indent=2, sort_keys=True) + "\n")
        print("★ 已写入基线 %s" % BASE)
    return 0


if __name__ == "__main__":
    sys.exit(main())
