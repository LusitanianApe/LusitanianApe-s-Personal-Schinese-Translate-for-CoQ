# -*- coding: utf-8 -*-
"""
修 `Naming.xml`：给所有 `<namestyle>` 加 `Load="Merge"`、所有 `<templates>` 加 `Load="Replace"`。

★★ 为什么必须这么做（反编译实证，2026-10-17）：
   `XRL.Names.NameStyles::HandleNameStyleTemplatesNode`：
       LoadMode = Reader.ParseAttribute("Load", LoadMode);
       if (LoadMode != "Merge") currentLoadingStyle.Templates.Clear();   ← ★ 只有 Replace 才清空
   `XRL.Names.NameStyles::HandleNameStyleTemplateNode`：
       NameTemplate t = currentLoadingStyle.Templates.Find(Name);        ← ★ 按 **Name** 找
       if (t == null) { …新增… }                                         官方模板名是 "village apothecary"，
                                                                         我方译名是「村中药师」⇒ **Find 找不到**
                                                                         ⇒ 官方那条**从不被移除**，被追加在后面
   ⇒ 结果：Templates 表里**中英并存**，游戏随机/轮选 ⇒ 有时出英文
      （用户截图：`Mnun, dromad merchant has 补满了 她的 的货物！` 就是抽到了官方那条
        `Naming.xml` → `<template Name="dromad merchant" />`；我方写的是「驼人商人」）

★ 官方示例的正确形态（`Base\\ExampleLanguage\\Naming.example.xml`）：
      <namestyle Name="Dromad Merchant Title" Load="Merge">
        <templates Load="Replace">
          <template Name="▶dromad merchant" />
        </templates>
      </namestyle>
   ⇒ `Load="Merge"` 保住官方 scopes/prefixes，`Load="Replace"` 把模板整批换掉。

用法：
    python _tools\\_fix_naming_load.py            # 干跑
    python _tools\\_fix_naming_load.py --apply    # 真改（先备份到 _backup\\）
"""
import io
import os
import re
import shutil
import sys
import time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
NAMING = os.path.join(ROOT, "mods", "QudZhsCookbook", "Naming.xml")
BACKUP = os.path.join(ROOT, "_backup", "naming_" + time.strftime("%Y%m%d-%H%M%S"))


def main():
    apply_ = "--apply" in sys.argv
    t = io.open(NAMING, encoding="utf-8").read()

    n_style = len(re.findall(r"<namestyle\s", t))
    n_style_ok = len(re.findall(r"<namestyle\s[^>]*Load=", t))
    n_tpl = len(re.findall(r"<templates(\s|>)", t))
    n_tpl_ok = len(re.findall(r"<templates\s[^>]*Load=", t))
    print("namestyle  %d 个（已带 Load 的 %d）" % (n_style, n_style_ok))
    print("templates  %d 段（已带 Load 的 %d）" % (n_tpl, n_tpl_ok))

    # ① <namestyle Name="X" ...> → 追加 Load="Merge"（已有 Load= 的跳过）
    def fix_style(m):
        head = m.group(0)
        if "Load=" in head:
            return head
        return head[:-1].rstrip() + ' Load="Merge">'

    new = re.sub(r"<namestyle\s[^>]*>", fix_style, t)

    # ② <templates> → <templates Load="Replace">（已有 Load= 的跳过）
    def fix_tpl(m):
        head = m.group(0)
        if "Load=" in head:
            return head
        if head.endswith("/>"):
            return '<templates Load="Replace" />'
        return '<templates Load="Replace">'

    new = re.sub(r"<templates(\s[^>]*)?>", fix_tpl, new)

    a = len(re.findall(r"<namestyle\s[^>]*Load=", new))
    b = len(re.findall(r"<templates\s[^>]*Load=", new))
    print("改后：namestyle 带 Load 的 %d/%d · templates 带 Load 的 %d/%d" % (a, n_style, b, n_tpl))

    if not apply_:
        print("\n（干跑；加 --apply 才写）")
        # 打印几处样例
        for m in list(re.finditer(r"<namestyle[^>]*>", new))[:3]:
            print("   样例:", m.group(0))
        for m in list(re.finditer(r"<templates[^>]*>", new))[:3]:
            print("   样例:", m.group(0))
        return 0

    os.makedirs(BACKUP, exist_ok=True)
    shutil.copy2(NAMING, os.path.join(BACKUP, "Naming.xml"))
    io.open(NAMING, "w", encoding="utf-8", newline="").write(new)
    print("\n已备份 → %s" % BACKUP)
    print("已写出 %s" % os.path.relpath(NAMING, ROOT))

    # 自证：重新解析 XML
    try:
        from lxml import etree
        etree.parse(NAMING)
        print("自证 ✅ XML 可解析")
    except Exception as e:
        print("!! 自证失败：%s" % e)
        print("   回滚：copy %s\\Naming.xml 回来" % BACKUP)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
