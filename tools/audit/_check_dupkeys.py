# -*- coding: utf-8 -*-
"""检查 C# 字典初始化器的重复键（本项目踩过：重复键 ⇒ .cctor 抛异常 ⇒ 整个补丁静默失效）。"""
import collections
import io
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

FILES = [
    r"mods\QudZhsCookbook\Scripts\ZhsEffectNameCollectPatch.cs",
    r"mods\QudZhsCookbook\Scripts\ZhsAbilityNamePatch.cs",
    r"mods\QudZhsCookbook\Scripts\ZhsFragmentDictPatch.cs",
]

KEY = re.compile(r'\{\s*"((?:[^"\\]|\\.)*)"\s*,')


def main():
    bad = 0
    for rel in FILES:
        p = os.path.join(ROOT, rel)
        if not os.path.isfile(p):
            print("  （缺）%s" % rel)
            continue
        t = io.open(p, encoding="utf-8", errors="replace").read()
        total, dups = 0, {}
        for m in re.finditer(r"Dictionary<string,\s*string>", t):
            i = m.end()
            j = t.find("};", i)
            if j < 0:
                continue
            keys = KEY.findall(t[i:j])
            total += len(keys)
            for k, n in collections.Counter(keys).items():
                if n > 1:
                    dups[k] = n
        flag = "✓" if not dups else "✗"
        print("  %s %-46s 键 %4d  重复 %s" % (flag, os.path.basename(rel), total,
                                              dups if dups else "无"))
        if dups:
            bad += 1
    print()
    print("⇒ %s" % ("全部无重复键 ✅" if not bad else "有 %d 个文件存在重复键 ⛔" % bad))
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
