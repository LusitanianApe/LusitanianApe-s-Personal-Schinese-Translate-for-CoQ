# -*- coding: utf-8 -*-
"""
★★ 全局普查：**`&#10;` vs `&#xA;` 的键不一致**（我方写的实体与官方不同 ⇒ 查表必失败）

机制（实证 2026-10-17 · 退出确认框）：
  · 游戏在 DLL 里内联的英文回退串用**真实换行**：`"…PROGRESS?\n\nType '=abandonConfirm=' to confirm."`
  · 官方导出模板把它写成 `&#xA;`；**我方手写成了 `&#10;`**
  · XML 解析后两者都是 U+000A ⇒ *看起来*等价，但查表键是**字符串比较**
    ⇒ 一旦有一侧没走 XML 解析（DLL 内联字面量那条路），就不相等
  · 实证代价：`Quit Without Saving No/With Checkpoint` 两条**双双回退英文**，
    连带那个 `=abandonConfirm=` 槽也没被替换 ⇒ 文案里硬写了「ABANDON」，
    而 Roleplay 模式下游戏期望输入 **QUIT** ⇒ 玩家**无法确认退出**（功能级缺陷）

本脚本：把我方 `Strings.xml` 里所有「含 `&#10;` 的 ID」与官方模板比对，
        报告**官方用 `&#xA;` 而我们用 `&#10;`** 的条目（= 高风险键错配）。

用法：python _tools\\_nl_entity_audit.py [--apply]
      --apply 时：把我方这些 ID 里的 `&#10;` 统一改成 `&#xA;`（值不动）
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
import shutil
import sys
import time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OURS = os.path.join(ROOT, "mods", "QudZhsCookbook", "Strings.xml")
EXDIR = os.path.join(_COQ(r"\CoQ_Data"),
                     "StreamingAssets", "Base", "ExampleLanguage")
BACKUP = os.path.join(ROOT, "_backup", "nl_" + time.strftime("%Y%m%d-%H%M%S"))

STR = re.compile(r'<string([^>]*?)>(.*?)</string>', re.S)


def parse(path):
    t = io.open(path, encoding="utf-8", errors="replace").read()
    out = []
    for m in STR.finditer(t):
        a = m.group(1)
        idm = re.search(r'ID="([^"]*)"', a)
        if not idm:
            continue
        cm = re.search(r'Context="([^"]*)"', a)
        out.append(((cm.group(1) if cm else None), idm.group(1), m.group(0)))
    return out


def norm(s):
    """把三种换行写法归一，用于"解析后是否等价"的比较。"""
    return s.replace("&#xA;", "\n").replace("&#10;", "\n").replace("&#xD;", "\r")


def main():
    apply_ = "--apply" in sys.argv
    ours = parse(OURS)

    off = set()
    for fn in sorted(os.listdir(EXDIR)):
        if fn.endswith(".xml"):
            for _c, i, _r in parse(os.path.join(EXDIR, fn)):
                off.add(i)

    # 我方含 &#10; 的 ID
    cand = [(c, i, raw) for (c, i, raw) in ours if "&#10;" in i]
    print("我方含 `&#10;` 的 ID = %d 条" % len(cand))

    risky = []
    for c, i, raw in cand:
        normed = norm(i)
        if normed in off:
            # 归一后官方有 ⇒ 官方写的是 &#xA; 形态（若官方也是 &#10; 就无害）
            official_uses_xa = any(norm(o) == normed and "&#xA;" in o for o in off)
            if official_uses_xa:
                risky.append((c, i))
    print("★ 其中「官方用 &#xA;、我方用 &#10;」= %d 条  ← 高风险键错配" % len(risky))
    print()
    for c, i in risky[:40]:
        print("   [%s]" % c)
        print("      %s" % i.replace("\n", "\\n")[:150])

    if not risky:
        print("（无风险项）")
        return 0

    if not apply_:
        print()
        print("（干跑；加 --apply 才改：只把 **ID 内**的 &#10; 换成 &#xA;，值不动）")
        return 0

    t = io.open(OURS, encoding="utf-8").read()

    def fix(m):
        a, body = m.group(1), m.group(2)
        if "&#10;" not in a:
            return m.group(0)
        a2 = a.replace("&#10;", "&#xA;")
        return "<string%s>%s</string>" % (a2, body)

    new = STR.sub(fix, t)
    if new == t:
        print("!! 没有可替换处（正则没吃上？）")
        return 1

    os.makedirs(BACKUP, exist_ok=True)
    shutil.copy2(OURS, os.path.join(BACKUP, "Strings.xml"))
    io.open(OURS, "w", encoding="utf-8", newline="").write(new)
    print("已备份 → %s" % BACKUP)

    # 自证：重新解析 + 计数 + 归一后仍与官方命中
    after = parse(OURS)
    still = [i for _c, i, _r in after if "&#10;" in i]
    hit = sum(1 for _c, i, _r in after if norm(i) in off)
    print("自证：改后仍含 &#10; 的 ID = %d（应为 0 或仅官方也用 &#10; 的）" % len(still))
    print("自证：归一后命中官方模板的条目 = %d" % hit)
    from lxml import etree
    etree.parse(OURS)
    print("自证 ✅ XML 可解析")
    return 0


if __name__ == "__main__":
    sys.exit(main())
