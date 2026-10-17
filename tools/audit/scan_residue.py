# -*- coding: utf-8 -*-
r"""★ 残留扫描器（每次迭代收尾跑一遍）

检出：
  ① 编译器的 CS0169 / CS0414 警告：private 字段声明了但从未被赋值/使用
  ② 自己声明的 `private static readonly Regex X` 但正文里除声明外无引用
  ③ 自定义的 `private static ... M(...)` 方法从未被调用
  ④ 说明「已移除/已废弃/不再使用」但实际还有引用的注释
  ⑤ C# 源里的 `▸`(U+25B8) / `▶`(U+25B6) 等 example 标记残留
"""
import io
import os
import re
import sys

sys.stdout.reconfigure(encoding="utf-8")

DEST = os.path.join(os.environ["USERPROFILE"], "AppData", "LocalLow",
                    "Freehold Games", "CavesOfQud", "Mods", "QudZhsCookbook")
LOW = os.path.join(os.environ["USERPROFILE"], "AppData", "LocalLow",
                   "Freehold Games", "CavesOfQud")
SCR = os.path.join(DEST, "Scripts")

files = [f for f in sorted(os.listdir(SCR)) if f.endswith(".cs")]
print("扫描 %d 个 .cs" % len(files))
print()

# ── ① 未使用的 private 字段 ──
print("=" * 100)
print("① 声明但从未使用的 private 字段（编译器会报 CS0169/CS0414）")
print("=" * 100)
n1 = 0
for f in files:
    s = io.open(os.path.join(SCR, f), encoding="utf-8", errors="ignore").read()
    for m in re.finditer(
            r"private\s+(?:static\s+)?(?:readonly\s+)?[\w<>\[\],\.\?]+\s+(\w+)\s*(?:=|;)", s):
        name = m.group(1)
        if name in ("Disabled",):          # 由外部开关文件控制，正常
            continue
        uses = len(re.findall(r"\b" + re.escape(name) + r"\b", s))
        if uses <= 1:
            n1 += 1
            print("   [%s] %s" % (f, name))
print("   ⇒ %d 个" % n1)

# ── ② 声明了但没用的 Regex 常量 ──
print()
print("=" * 100)
print("② 声明但正文里没引用的 Regex 常量")
print("=" * 100)
n2 = 0
for f in files:
    s = io.open(os.path.join(SCR, f), encoding="utf-8", errors="ignore").read()
    for m in re.finditer(r"Regex\s+(\w+)\s*=", s):
        name = m.group(1)
        uses = len(re.findall(r"\b" + re.escape(name) + r"\b", s))
        if uses <= 1:
            n2 += 1
            print("   [%s] %s" % (f, name))
print("   ⇒ %d 个" % n2)

# ── ③ 声明了但没被调用的自定义静态方法 ──
print()
print("=" * 100)
print("③ 声明但从未被调用的 private 方法")
print("=" * 100)
n3 = 0
for f in files:
    s = io.open(os.path.join(SCR, f), encoding="utf-8", errors="ignore").read()
    for m in re.finditer(r"private\s+static\s+[\w<>\[\],\.\?]+\s+(\w+)\s*\(", s):
        name = m.group(1)
        # ★ 白名单：Harmony 通过反射/特性调用，源码里看不到引用
        if name in ("Prepare", "Postfix", "Prefix", "TargetMethod",
                    "TargetMethods", "GetTargetMethod", "Target",
                    "Cleanup", "Finalizer", "Transpiler"):
            continue
        # ★ 黑名单：已确认的死代码（迭代时清理，避免反复刷屏）
        KNOWN_DEAD = {
            ("ZhsMarkovBookPatch.cs", "PatchByKeyword"),
            ("ZhsMarkovBookPatch.cs", "PatchGroup"),
            ("ZhsMutationPatch.cs", "FindOfficialDescription"),
            ("ZhsVerbPatch.cs", "ThirdOf"),
            ("ZhsXmlLoadProbe.cs", "DumpMember"),
        }
        if (f, name) in KNOWN_DEAD:
            continue
        uses = len(re.findall(r"\b" + re.escape(name) + r"\b", s))
        if uses <= 1:
            n3 += 1
            print("   [%s] %s" % (f, name))
print("   ⇒ %d 个" % n3)

# ── ④ example 标记残留 ──
print()
print("=" * 100)
print("④ example 标记残留（▶ / ▸ 等）")
print("=" * 100)
n4 = 0
for dp, dn, fn in os.walk(DEST):
    for f in fn:
        if not f.endswith((".cs", ".xml", ".json", ".jsonc", ".txt")):
            continue
        p = os.path.join(dp, f)
        t = io.open(p, encoding="utf-8", errors="ignore").read()
        if f.endswith(".cs"):
            # ★ .cs 里排除注释行（我自己注释里的 └─► 箭头不是 example 标记）
            t = chr(10).join(l for l in t.split(chr(10))
                             if not l.lstrip().startswith("//"))
        for ch, nm in (("\u25b6", "▶"), ("\u25b8", "▸"), ("\u25ba", "►")):
            c = t.count(ch)
            if c:
                n4 += c
                print("   [%s] %s ×%d" % (os.path.relpath(p, DEST), nm, c))
print("   ⇒ %d 个" % n4)

# ── ⑤ 编译警告（从 build_log 抓） ──
print()
print("=" * 100)
print("⑤ build_log 里的编译器警告（实时）")
print("=" * 100)
p = os.path.join(LOW, "build_log.txt")
if os.path.isfile(p):
    s = io.open(p, encoding="utf-8", errors="ignore").read()
    for m in re.findall(r"warning CS\d+[^\n]{0,160}", s):
        print("   %s" % m[:180])
else:
    print("   （无 build_log）")
