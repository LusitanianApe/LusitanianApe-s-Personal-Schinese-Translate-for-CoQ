# -*- coding: utf-8 -*-
"""
★★ 「能力名字面量」缺口普查 + 批量补齐候选

> 起因（用户 2026-10-17 截图）：`你获得了主动能力 Tongue。`
>   出处实证：`StickyTongue::Mutate` 调
>       AddMyActivatedAbility("Tongue", "CommandStickyTongue", …)   ← **字面量**
>   `ActivatedAbilities::AddAbility` 里 `activatedAbilityEntry.DisplayName = Name;`
>   ⇒ 只有 `ZhsAbilityNamePatch.Names` 这张表能兜。
>   ⇒ 该表一直是"**逐个发现**"累积的 ⇒ 本脚本一次把所有缺的找出来。

输出：
  ① 我方 Names 表已有多少键
  ② `AddMyActivatedAbility` 的**字面量首参**全集，与 Names 表做差集
  ③ 对每个缺口给出**权威译名候选**（从 Skills.xml 的 `<power Name=… DisplayName=…>`、
     Mutations.xml 的 `DisplayName=` 里反查 —— 不另起译名，§10 原则①）
"""
import glob
import io
import os
import re
from collections import defaultdict

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CENSUS = glob.glob(os.path.join(ROOT, "_tools", "_census", "asm-*"))[0]
MOD = os.path.join(ROOT, "mods", "QudZhsCookbook")
PATCH = os.path.join(MOD, "Scripts", "ZhsAbilityNamePatch.cs")


def names_keys():
    t = io.open(PATCH, encoding="utf-8", errors="replace").read()
    i = t.find("Dictionary<string, string>")
    j = t.find("};", i)
    body = t[i:j]
    return set(re.findall(r'\{\s*"((?:[^"\\]|\\.)*)"\s*,', body))


def xml_display_map(path, tag, name_attr):
    """{'<power Name="Lunge"...DisplayName="突刺"/>'} → {Lunge: 突刺}"""
    if not os.path.isfile(path):
        return {}
    t = io.open(path, encoding="utf-8", errors="replace").read()
    out = {}
    for m in re.finditer(r"<%s\b([^>]*)>" % tag, t):
        a = m.group(1)
        nm = re.search(r'%s="([^"]*)"' % name_attr, a)
        dn = re.search(r'DisplayName="([^"]*)"', a)
        if nm and dn:
            out[nm.group(1)] = dn.group(1)
    return out


def main():
    have = names_keys()
    print("① 我方 ZhsAbilityNamePatch.Names 键数 = %d" % len(have))

    rows = [l.rstrip("\n").split("\t")
            for l in io.open(os.path.join(CENSUS, "callsites.tsv"), encoding="utf-8")][1:]
    calls = defaultdict(dict)
    for r in rows:
        if "AddMyActivatedAbility" not in r[3] and "AddAbility" not in r[3]:
            continue
        try:
            calls[(r[0], r[1], r[2])][int(r[4])] = r[5]
        except ValueError:
            pass

    lits = {}
    for (typ, mem, _off), args in calls.items():
        a0 = args.get(0, "")
        if a0 and not a0.startswith("=") and len(a0) < 40:
            lits.setdefault(a0, set()).add("%s::%s" % (typ.split(".")[-1], mem))

    print("② Add*ActivatedAbility 的**字面量首参** = %d 个" % len(lits))
    miss = {k: v for k, v in lits.items() if k not in have}
    print("   其中我方 Names 表**没有**的 = %d 个" % len(miss))

    skills = xml_display_map(os.path.join(MOD, "Skills.xml"), "power", "Name")
    muts = {}
    for fn in ("Mutations.xml", "HiddenMutations.xml"):
        muts.update(xml_display_map(os.path.join(MOD, fn), "mutation", "Name"))
    print("   权威译名池：Skills.xml <power> %d · Mutations %d" % (len(skills), len(muts)))

    print()
    print("=" * 78)
    print("★ 缺口清单（带权威译名候选；空 = 需要新译）")
    can, need = [], []
    for k in sorted(miss):
        zh = skills.get(k) or muts.get(k)
        owner = " · ".join(sorted(miss[k])[:2])
        if zh:
            can.append((k, zh, owner))
        else:
            need.append((k, owner))
    for k, zh, owner in can:
        print("   ✅ %-26r → %-14s ← %s" % (k, zh, owner))
    print()
    for k, owner in need:
        print("   ❓ %-26r ← %s" % (k, owner))
    print()
    print("⇒ 有权威译名可直接补 %d 条 · 需人工定译 %d 条" % (len(can), len(need)))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
