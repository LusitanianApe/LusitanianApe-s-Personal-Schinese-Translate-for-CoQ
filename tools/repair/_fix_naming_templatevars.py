# -*- coding: utf-8 -*-
r"""
★★ 修 `Naming.xml` 的 `<templatevars>`：补 `Load="Replace"`（与 `<templates>` 同一机制）

★ 取证（2026-10-17 · 用户截图 `Mayor 恩图`）：
   官方 `Base\Naming.xml` L4221-4223：<templatevar Name="mayorSyn">
                                       <value Name="mayor" /><value Name="elder" />
   我方同段（原本）：                    <value Name="村长" /><value Name="长老" />
   官方**示例**给的写法（`ExampleLanguage\Naming.example.xml`）：
       <templatevars Load="Replace">          ← ★★ 关键
         <templatevar Name="mayorSyn">
           <value Name="▶mayor" />
   反编译 `NameStyles::HandleNameStyleTemplateVarsNode`：与 templates 同源逻辑 ——
       `if (LoadMode != "Merge") Clear()`；`Find(Name)` 找**同名** `<value>` 才替换
   我方 value 的 Name 是「村长」/「长老」，官方的 Name 是 "mayor"/"elder"
   ⇒ `Find` 认不出 ⇒ **官方两条从不被移除** ⇒ 随机抽
   ⇒ `mayorSyn` 有 4 个候选（村长/长老/mayor/elder）⇒ 25% 出英文 "Mayor"
   ★ 这就是"同一个坑的第二次露面"：**`<templates>` 修了，`<templatevars>` 忘了**。

用法：
    python _tools\\_fix_naming_templatevars.py            # 干跑
    python _tools\\_fix_naming_templatevars.py --apply    # 真改
"""
import io
import os
import re
import shutil
import sys
import time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
NAMING = os.path.join(ROOT, "mods", "QudZhsCookbook", "Naming.xml")
BACKUP = os.path.join(ROOT, "_backup", "tvar_" + time.strftime("%Y%m%d-%H%M%S"))
TVAR = re.compile(r"<templatevars(\s[^>]*)?>", re.I)


def main():
    apply_ = "--apply" in sys.argv
    t = io.open(NAMING, encoding="utf-8").read()

    all_tv = TVAR.findall(t)
    with_load = [x for x in all_tv if x and "Load=" in x]
    print("`<templatevars>` 段 = %d · 已带 Load= 的 = %d" % (len(all_tv), len(with_load)))

    def fix(m):
        head = m.group(0)
        if "Load=" in head:
            return head
        return "<templatevars Load=\"Replace\">"

    new = TVAR.sub(fix, t)
    after = len(re.findall(r"<templatevars\s+Load=", new))
    print("改后带 Load= 的 = %d / %d" % (after, len(all_tv)))

    # 顺带报告：这些段里的 value 是不是中文（只有中文才该 Replace）
    zh = 0
    for m in re.finditer(r"<templatevar\s+Name=\"([^\"]*)\">(.*?)</templatevar>", t, re.S):
        vals = re.findall(r'<value\s+Name="([^"]*)"', m.group(2))
        has_cn = any(re.search(r"[\u4e00-\u9fff]", v) for v in vals)
        if has_cn:
            zh += 1
    print("含中文 value 的 templatevar = %d（这些必须 Replace，否则与官方英文候选混抽）" % zh)

    if not apply_:
        print("\n（干跑；加 --apply 才写）")
        return 0

    os.makedirs(BACKUP, exist_ok=True)
    shutil.copy2(NAMING, os.path.join(BACKUP, "Naming.xml"))
    io.open(NAMING, "w", encoding="utf-8", newline="").write(new)
    from lxml import etree
    etree.parse(NAMING)
    print("\n已备份 → %s" % BACKUP)
    print("自证 ✅ XML 可解析")
    return 0


if __name__ == "__main__":
    sys.exit(main())
