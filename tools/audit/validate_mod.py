import os as _os
# ── 游戏目录：可用环境变量 COQ_DIR 覆盖（默认指向 Steam 常见安装位置）──
COQ_ROOT = _os.environ.get(
    "COQ_DIR",
    r"C:\\Program Files (x86)\\Steam\\steamapps\\common\\Caves of Qud")
def _COQ(rel):
    return COQ_ROOT + rel

# -*- coding: utf-8 -*-
"""校验模组文件的合法性：
   1. XML 可解析、JSON 可解析
   2. C# 语料常量与语料文件逐句一致
   3. Strings.xml 的 (Context, ID) 是否都能在官方英文模板中命中
      —— 注意要比对「解析后」的 ID：像 &#xA; 这种实体会被 XML 解析器还原成真实换行，
         直接拿原文做子串匹配会产生假阳性。
"""
import json, os, re, sys, io
from lxml import etree

MOD = r"C:\Users\Nattens\Downloads\CoQ-马尔科夫文本调研\mods\QudZhsCookbook"
BASE = _COQ(r"\CoQ_Data\StreamingAssets\Base\ExampleLanguage")
ok = True

for name in ("Languages.xml", "Strings.xml", "Items.xml", "Mutations.xml"):
    if not os.path.exists(os.path.join(MOD, name)):
        ok = False
        print(f"[FAIL] 交付文件缺失: {name}")
        continue
    try:
        etree.parse(os.path.join(MOD, name))
        print(f"[OK]   XML 可解析: {name}")
    except Exception as e:
        ok = False
        print(f"[FAIL] XML: {name} -> {e}")

# ★ 扫描**全部** XML（而非硬编码清单）—— 新增的覆盖文件会被自动校验
_skip = {"Languages.xml", "Strings.xml", "Items.xml", "Mutations.xml"}
_all_xml = sorted(f for f in os.listdir(MOD) if f.endswith(".xml"))
_n = 0
for name in _all_xml:
    if name in _skip:
        continue
    try:
        etree.parse(os.path.join(MOD, name))
        _n += 1
    except Exception as e:
        ok = False
        print(f"[FAIL] XML: {name} -> {e}")
print(f"[OK]   其余 {_n} 个 XML 全部可解析（模组共 {len(_all_xml)} 个 xml）")

try:
    m = json.load(open(os.path.join(MOD, "manifest.json"), encoding="utf-8-sig"))
    print(f"[OK]   JSON 可解析: manifest.json  ID={m.get('ID')}  Version={m.get('Version')}")
except Exception as e:
    ok = False
    print(f"[FAIL] JSON: manifest.json -> {e}")

# 交付文件不允许带 BOM：PowerShell 的 Set-Content -Encoding UTF8 会写 BOM，已经害过 manifest.json 一次
import glob
_bom = []
for _p in glob.glob(os.path.join(MOD, "**", "*.*"), recursive=True):
    try:
        if open(_p, "rb").read(3).startswith(b"\xef\xbb\xbf"):
            _bom.append(os.path.relpath(_p, MOD))
    except OSError:
        pass
if _bom:
    ok = False
    print(f"[FAIL] 以下文件带 BOM，需要去掉: {_bom}")
else:
    print("[OK]   全部交付文件均无 BOM")

# ── 语料一致性 ──
# ★ 1.1.0 起，「书籍正文」的语料方案已换成**句模 + 槽位填充**：
#   旧的字级链语料 `zh_book_corpus.txt` / `ZhsBookCorpus.cs` 已退役删除。
#   书名仍走字级链（`zh_cookbook_corpus.txt` / `ZhsCookbookCorpus.cs`），继续校验。
CORPORA = [
    ("zh_cookbook_corpus.txt", "ZhsCookbookCorpus.cs", "书名用的中文语料"),
]
for corpus_name, cs_name, label in CORPORA:
    corpus = [l.strip() for l in open(
        os.path.join(r"C:\Users\Nattens\Downloads\CoQ-马尔科夫文本调研\_tools", corpus_name),
        encoding="utf-8").read().split("\n") if l.strip()]
    cs = open(os.path.join(MOD, "Scripts", cs_name), encoding="utf-8").read()
    missing = [l for l in corpus if ('"' + l + '",') not in cs]
    if missing:
        ok = False
        print(f"[FAIL] {label}：{cs_name} 缺少 {len(missing)} 句，例如: {missing[:3]}")
    else:
        print(f"[OK]   {label}：{cs_name} 与 {corpus_name} 逐句一致（{len(corpus)} 句）")

# ── 词库一致性（句模+槽位填充方案的 C# 侧）──
# 保证 `_tools\zh_lexicon.json` == `Scripts\ZhsMarkovLexicon.cs`：
# C# 词库是由 `_tools\gen_markov_lexicon_cs.py` 生成的，这里反向校验，
# 防止"改了 json 忘了重跑生成器"导致游戏里用的是旧词库。
_lex_path = os.path.join(r"C:\Users\Nattens\Downloads\CoQ-马尔科夫文本调研\_tools",
                         "zh_lexicon.json")
_lexcs_path = os.path.join(MOD, "Scripts", "ZhsMarkovLexicon.cs")
if os.path.exists(_lex_path) and os.path.exists(_lexcs_path):
    _lex = json.load(open(_lex_path, encoding="utf-8"))
    _cs = open(_lexcs_path, encoding="utf-8").read()
    _miss = []
    for _slot, _g in _lex.items():
        if not isinstance(_g, dict) or "words" not in _g:
            continue
        _ws = _g["words"]
        if not isinstance(_ws, dict):
            continue
        for _w in _ws:
            if ('"' + _w + '"') not in _cs:
                _miss.append(f"{_slot}:{_w}")
    for _w in _lex.get("ADJ", {}).get("words", []):
        if ('"' + _w + '"') not in _cs:
            _miss.append("ADJ:" + _w)
    if _miss:
        ok = False
        print(f"[FAIL] 词库不一致：ZhsMarkovLexicon.cs 缺少 {len(_miss)} 个词，"
              f"例如 {_miss[:5]} —— 请重跑 _tools\\gen_markov_lexicon_cs.py")
    else:
        print("[OK]   词库一致：ZhsMarkovLexicon.cs 与 zh_lexicon.json 完全对齐")

# ── 等级文本 `@N` 完整性（★ 2026-10-04 新增，防回归）──
#
# 两条判据（由 `_tools\check_leveltext_order.py` 提供）：
#   A. **根级占位不能漏**：官方 leveltext 里每个「没有占位元素祖先」的占位
#      （stat / statline / switch / gametext）都必须在我们译文里出现，
#      且**首次出现顺序**要与官方文档顺序一致。
#      被祖先包含的（如 switch 内的 stat）可以不写 —— C# 侧 `EmitSkeleton`
#      是递归渲染的，写祖先把子孙一起带出来。
#   B. **不能是「纯占位」条目**：去掉 `@N` 后必须还有实义字符。
#      否则会顶掉官方英文回退，玩家只看到一串数字、毫无说明文字。
try:
    sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__))))
    import check_leveltext_order as _CLO
    _ours = _CLO.our_leveltexts()
    _miss, _thin, _off_thin = [], [], []
    for _cls in sorted(_ours):
        _places, _roots = _CLO.official_places_tree(_cls)
        _paras = _ours[_cls]
        _seq = []
        for _t in _paras:
            for _x in re.findall(r"@(\d+)", _t):
                _i = int(_x)
                if _i not in _seq:
                    _seq.append(_i)
        if _places:
            _missing = [i for i in _roots if i not in _seq]
            _used = [i for i in _seq if i in range(len(_places))]
            if _missing or _used != sorted(_used):
                _miss.append((_cls, _missing, _used))
        _stripped = re.sub(r"[\s\W_]+", "", re.sub(r"@\d+", "", "".join(_paras)))
        if not _stripped:
            # ★ 区分两种情况：
            #   官方 leveltext **本身**就只有占位元素（如 <p><stat/><stat/></p>）——
            #   那不是我们的问题，官方原文就没文字，面板上本来也只有数字；
            #   官方有文字而我们写成纯占位 —— 那才是顶掉了英文回退，要报错。
            _off = _CLO.official_paragraphs(_cls)
            _off_stripped = re.sub(
                r"[\s\W_]+", "", re.sub(r"@\d+", "", "".join(t for _a, _b, t in _off)))
            if _off_stripped:
                _thin.append(_cls)
            else:
                _off_thin.append(_cls)
    if _miss:
        ok = False
        print(f"[FAIL] 等级文本 @N 有 {len(_miss)} 条漏用根级占位或顺序不对，例如 "
              f"{[(c, m) for c, m, _ in _miss[:4]]} —— 跑 _tools\\check_leveltext_order.py --mismatch")
    else:
        print(f"[OK]   等级文本 @N：{len(_ours)} 条的根级占位与顺序全部正确")
    if _thin:
        ok = False
        print(f"[FAIL] 等级文本有 {len(_thin)} 条是「纯占位」（官方有文字、我们只写 @N）"
              f"：{_thin[:6]} —— 会顶掉官方英文回退，请补文字或删条目")
    else:
        print("[OK]   等级文本无「我们自己造成的纯占位」条目")
    if _off_thin:
        print(f"[i]    另有 {len(_off_thin)} 条「官方原文就只有占位、没有文字」，"
              f"面板上本来也只有数字（非我们的问题）：{_off_thin}")
except Exception as _e:
    ok = False
    print(f"[FAIL] 等级文本检查失败：{type(_e).__name__} {_e}")

# ── C# 集合初始化器的**重复键**（★ 2026-10-04 新增，防回归）──
#
# 为什么必须查：`new Dictionary<K,V> { {k1,v1}, {k2,v2} }` 会编译成 `Add(...)`，
# 而 **`Add` 遇重复键抛 `ArgumentException`**。若该字典是 `static readonly` 字段的
# 初始化器，异常会包成 `TypeInitializationException` 从 `.cctor` 抛出 ⇒
# **依赖该类型的全部逻辑一起失效**。
# 实测代价：`ZhsMutationTemplates` 的静态构造崩了 ⇒ 变异模板一个都没注册
# ⇒ 游戏里变异面板**全变英文**，而日志只留一行 `TypeInitializationException`。
try:
    sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__))))
    import find_cs_dup_dict_keys as _DUP
    _scripts = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..",
                            "mods", "QudZhsCookbook", "Scripts")
    _dup_total = 0
    _dup_detail = []
    for _f in sorted(os.listdir(_scripts)):
        if not _f.endswith(".cs"):
            continue
        for _line, _n, _dups in _DUP.scan_file(os.path.join(_scripts, _f)):
            _dup_total += len(_dups)
            _dup_detail.append("%s:%d %s" % (_f, _line, list(_dups.keys())))
    if _dup_total:
        ok = False
        print(f"[FAIL] C# 字典初始化器有 {_dup_total} 个重复键（会让 .cctor 抛异常、"
              f"连累整个类型）：{_dup_detail[:4]}")
    else:
        print("[OK]   C# 字典初始化器无重复键（.cctor 不会因此抛异常）")
except Exception as _e:
    ok = False
    print(f"[FAIL] 重复键检查失败：{type(_e).__name__} {_e}")

# 官方模板里所有 (Context, ID) 对——解析后比对
pairs = set()
ids_only = set()
for f in ("Strings.example.xml", "Strings.appendix.example.xml",
              "Strings.Conversations.example.xml"):
    t = etree.parse(os.path.join(BASE, f))
    for s in t.getroot().iter("string"):
        pairs.add(((s.get("Context") or ""), (s.get("ID") or "")))
        ids_only.add(s.get("ID") or "")

bad = 0
n = 0
# 模组里所有根元素为 <strings> 的文件都要查（Strings.xml、Sonnet.xml …）
_string_files = []
for _f in sorted(os.listdir(MOD)):
    if not _f.endswith(".xml"):
        continue
    try:
        _r = etree.parse(os.path.join(MOD, _f)).getroot()
    except Exception:
        continue
    if _r.tag == "strings":
        _string_files.append((_f, _r))
print(f"[i] 字符串表文件: {[f for f, _ in _string_files]}")

# 允许的「非官方模板键」白名单：这些必须是**故意**添加的。
# `subtype.<Name>.extrainfo` 的 ID 就是那一段原文（官方 IL：Strings.TryGetString(id, 原文) ），
# 所以中文段落必须补一条「ID = 中文原文 → 值 = 中文原文」的恒等条目，
# 否则日志会刷 miss、面板还会多出一行英文。详见 _tools/fix_subtype_extrainfo.py。
_EXTRAINFO_CTX = re.compile(r"^subtype\..+\.extrainfo$")

# ★★ 2026-10-11（2.16.117）：**DLL 硬编码直查表**（自动举证，不靠手工白名单）
#   问题：DLL 里**硬编码**的字符串（如 `Sirocco` / `Grants you =skillName=.` /
#         `MentalScreen RulesPostfix …`）**官方 ExampleLanguage 里根本没有** ——
#         它们不是从数据 XML 来的，而是 `ldstr` 直接写在 C# 里。
#         上面那条「必须命中官方模板」的检查对这类条目**不适用**（实测 41 条假 FAIL）。
#   判据：**该 ID 是否真的存在于 DLL 的 UserString heap**（= IL 里真有一条 `ldstr`）。
#         `_tools\_run_ldstr_il.py` 产出的 `literals-heap.txt` 就是权威名单，
#         与 `ldstr` 用**同一套单射转义** ⇒ 可无损还原。
#   ⇒ 好处：真·硬编码自动放行；**打错字的 ID 不在 heap 里 ⇒ 仍然会被抓**。
_DLL_LITERALS = set()
try:
    _cen = os.path.join(os.path.dirname(os.path.abspath(__file__)), "_census")
    _heap = None
    for _d in sorted(os.listdir(_cen), reverse=True):
        _p = os.path.join(_cen, _d, "literals-heap.txt")
        if os.path.isfile(_p):
            _heap = _p
            break
    if _heap:
        _esc = re.compile(r"\\(.)")
        _un = {"\\": "\\", "t": "\t", "r": "\r", "n": "\n"}
        for _l in io.open(_heap, encoding="utf-8").read().split("\n"):
            if _l != "":
                _DLL_LITERALS.add(_esc.sub(lambda m: _un.get(m.group(1), m.group(1)), _l))
    print(f"[i] DLL UserString heap 名单: {len(_DLL_LITERALS)} 条"
          f"（来源 {os.path.relpath(_heap, os.path.dirname(os.path.abspath(__file__))) if _heap else '缺'}）")
except Exception as _e:
    print(f"[warn] 读不到 heap 名单（{_e}）⇒ DLL 硬编码条目会被误报")

for _fname, mine in _string_files:
    for s in mine.iter("string"):
        n += 1
        ctx, sid = (s.get("Context") or ""), (s.get("ID") or "")
        if (ctx, sid) in pairs:
            continue
        if _EXTRAINFO_CTX.match(ctx) and (s.text == sid):
            # 恒等条目：值必须与 ID 完全一致，否则就是写错了
            continue
        if sid in ids_only:
            print(f"[warn] {_fname}: Context 不匹配但 ID 存在: {ctx!r} / ID={sid[:60]!r}")
            continue
        if sid in _DLL_LITERALS:
            # ★ 2.16.117：DLL 硬编码串（官方数据里没有，但 IL 里真有这条 ldstr）
            continue
        # ★ 2026-10-08：官方**字符串表里根本没有单位 ID**（
        #   ound/square/degree 实测 0 条），
        #   而游戏用 XRL.Language.Strings._S(string ID)（= TryGetString(null, ID, …)）
        #   查它们，Unit="round" 走 =unit|value.pluralize= 时同样查这个 ID。
        #   ⇒ 我方以 **null Context** 补的这些条目**没有官方先例可对**，属**已知合法例外**：
        #     _tools\_find_official_strings.py 可复现「官方 0 条」这一事实。
        if not ctx and re.match(r"^[a-z][a-z ]{1,20}$", sid):
            print(f"[ok]   {_fname}: 单位/词条 ID 无官方先例（null Context，已知合法）: {sid!r}")
            continue
        bad += 1
        # 注意：这条必须留在循环内（之前缩进错了，导致它用残留变量无条件打印，
        # 既产生假 FAIL，又让 bad 计数失真）
        print(f"[FAIL] {_fname} 模板中找不到该条目: Context={ctx!r}  ID={sid[:80]!r}")

if bad == 0:
    print(f"[OK]   字符串表共 {n} 条（{[f for f, _ in _string_files]}）的 (Context, ID) 全部命中官方英文模板")
else:
    ok = False
    print(f"[FAIL] 字符串表共 {n} 条，其中 {bad} 条未命中")

# 基线守卫：模组里根元素为 <strings> 的 XML，若**因解析失败被跳过**，n 会静默变小而照样"全绿"。
# 这里用一个下限基线把它变成硬失败（0.21.0 实测：未转义的 `<` 让 202 条掉到 107 条仍报通过）。
_STRINGS_BASELINE = 190
if n < _STRINGS_BASELINE:
    ok = False
    print(f"[FAIL] 字符串表条数 {n} 低于基线 {_STRINGS_BASELINE} —— "
          f"极可能有某个 .xml 解析失败被跳过（检查上面是否有解析错误）")
else:
    print(f"[OK]   字符串表条数 {n} ≥ 基线 {_STRINGS_BASELINE}")

# ---------- 新增：Books / Liquids / Creatures 的覆盖校验 ----------

def read(path):
    return open(path, encoding="utf-8", errors="replace").read()

# 1) Books.xml：书 ID 必须存在；且占位符集合与原文完全一致（漏一个 =IS1= 就会静默失效）
base_books = read(os.path.join(_COQ(r"\CoQ_Data\StreamingAssets\Base"), "Books.xml"))
mine_books = read(os.path.join(MOD, "Books.xml"))
base_block = re.search(r'<book ID="Corpus".*?</book>', base_books, re.S)
mine_block = re.search(r'<book ID="Corpus".*?</book>', mine_books, re.S)
if not base_block or not mine_block:
    ok = False
    print("[FAIL] Books.xml：任一侧找不到 <book ID=\"Corpus\"> 块")
else:
    bp = set(re.findall(r"=[A-Za-z0-9]+=", base_block.group(0)))
    mp = set(re.findall(r"=[A-Za-z0-9]+=", mine_block.group(0)))
    if bp == mp:
        print(f"[OK]   Books.xml：Corpus 书占位符与原文完全一致 {sorted(bp)}")
    else:
        ok = False
        print(f"[FAIL] Books.xml：占位符不一致  原文多出={sorted(bp - mp)}  译文多出={sorted(mp - bp)}")
    # 页数与着色器标识符一致性
    bpages = base_block.group(0).count("<page>")
    mpages = mine_block.group(0).count("<page>")
    bsh = sorted(set(re.findall(r"\{\{([A-Za-z0-9#]+)\|", base_block.group(0))))
    msh = sorted(set(re.findall(r"\{\{([A-Za-z0-9#]+)\|", mine_block.group(0))))
    if bpages == mpages:
        print(f"[OK]   Books.xml：页数一致（{mpages} 页）")
    else:
        ok = False
        print(f"[FAIL] Books.xml：页数不一致 原文={bpages} 译文={mpages}")
    if bsh == msh:
        print(f"[OK]   Books.xml：着色器标识符一致 {bsh}")
    else:
        print(f"[warn] Books.xml：着色器标识符差异 原文={bsh} 译文={msh}（若为刻意精简可忽略）")

# 2) Liquids.xml：液体名必须在官方模板里；着色器标识符必须与本体一致
tmpl_liq = read(os.path.join(BASE, "Liquids.example.xml"))
base_liq = read(os.path.join(_COQ(r"\CoQ_Data\StreamingAssets\Base"), "Liquids.xml"))
mine_liq = read(os.path.join(MOD, "Liquids.xml"))
tmpl_liq_names = set(re.findall(r'<liquid Name="([^"]+)"', tmpl_liq))
base_liq_name = {}
for m in re.finditer(r'<liquid Name="([^"]+)"[^>]*>(.*?)(?=<liquid Name=|\Z)', base_liq, re.S):
    base_liq_name[m.group(1)] = m.group(2)
bad_liq = 0
for nm in re.findall(r'<liquid Name="([^"]+)"', mine_liq):
    if nm not in tmpl_liq_names:
        bad_liq += 1
        print(f"[FAIL] Liquids.xml：模板中不存在液体 {nm}")
if bad_liq == 0:
    print(f"[OK]   Liquids.xml：{len(re.findall(chr(60) + 'liquid Name=', mine_liq))} 种液体名都存在于官方模板")

mine_liq_blocks = dict((m.group(1), m.group(2)) for m in re.finditer(
    r'<liquid Name="([^"]+)"[^>]*>(.*?)(?=<liquid Name=|\Z)', mine_liq, re.S))
sh_mismatch = []
for nm, blk in mine_liq_blocks.items():
    b = base_liq_name.get(nm, "")
    # 逐字段比对：只检查"我覆盖了的字段"，且只比着色器标识符（{{id| 的 id）
    for fm in re.finditer(r"<(\w+)>(.*?)</\1>", blk, re.S):
        field, value = fm.group(1), fm.group(2)
        bf = re.search(r"<%s>(.*?)</%s>" % (field, field), b, re.S)
        if not bf:
            continue
        bs = sorted(set(re.findall(r"\{\{([A-Za-z0-9#]+)\|", bf.group(1))))
        ms = sorted(set(re.findall(r"\{\{([A-Za-z0-9#]+)\|", value)))
        if bs != ms:
            sh_mismatch.append((nm, field, bs, ms))
if sh_mismatch:
    ok = False
    for nm, field, bs, ms in sh_mismatch:
        print(f"[FAIL] Liquids.xml：{nm}.{field} 的着色器标识符与本体不一致 原文={bs} 译文={ms}")
else:
    print("[OK]   Liquids.xml：逐字段比对着色器标识符均与本体一致"
          "（未把 cider/putrid/slimy 等命名着色器当文本翻译）")

# 3) Creatures.xml：对象名必须在官方模板里
tmpl_cre = read(os.path.join(BASE, "Creatures.example.xml"))
tmpl_cre_names = set(re.findall(r'<object Name="([^"]+)"', tmpl_cre))
mine_cre_names = re.findall(r'<object Name="([^"]+)"', read(os.path.join(MOD, "Creatures.xml")))
bad_cre = [nm for nm in mine_cre_names if nm not in tmpl_cre_names]
if bad_cre:
    print(f"[warn] Creatures.xml：这些对象名不在官方模板中（可能是本地化无关对象）: {bad_cre}")
else:
    print(f"[OK]   Creatures.xml：{len(mine_cre_names)} 个对象名都存在于官方模板")

# 4) 术语括注规范：登记在 glossary.json 的专有名词，每处（每页）首次出现必须括注英文原文
#    注意：这里**必须遍历全部字符串表文件**，而不是写死文件名清单。
#    写死清单曾导致真实故障：HistoricEvents.xml 里一个未转义的 `<`（ID="<unknown>"）
#    让文件解析失败；该文件是从写死清单里读的 → 直接把异常吞成 "[warn] 跳过"，
#    字符串表条数从 202 静默掉到 107，而结论仍然是"全部通过"。改完必须让
#    「字符串表共 N 条」的 N 不许无故变小。
try:
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import apply_glossary as ag
    _ents = ag.load_glossary()
    _glossary_checked = 0
    for _f in sorted(os.listdir(MOD)):
        if not _f.endswith(".xml"):
            continue
        _p = os.path.join(MOD, _f)
        try:
            if etree.parse(_p).getroot().tag != "strings":
                continue
        except Exception as e:
            # 解析失败 = 交付文件坏了，必须 FAIL（绝不能再降级成 warn）
            ok = False
            print(f"[FAIL] {_f}: XML 解析失败，无法做术语括注检查 -> {e}")
            continue
        _hits = ag.process_file(_p, _ents, dry=True)
        _glossary_checked += 1
        if _hits:
            ok = False
            print(f"[FAIL] {_f}: 专有名词缺英文括注 {len(_hits)} 处 -> {_hits[:6]}")
        else:
            print(f"[OK]   {_f}: 登记的专有名词均已括注英文原文")
    print(f"[i] 术语括注检查覆盖 {_glossary_checked} 个字符串表文件")
except Exception as e:
    ok = False
    print(f"[FAIL] 术语括注检查本身出错: {e}")

# 5) Mutations.xml / Subtypes.xml：官方解析器是「按名字就地更新」，名字对不上
#    就会**追加**成重复条目。1.0.1 之前的两处真实故障都出在这里：
#      * Mutations.xml 的 category 名与官方不同 → 重复变异、Cost 丢成 -999
#      * Subtypes.xml 的 Callings 多套了一层 <category> → 天职被塞进自动建的空
#        category，表现为「选完预设还弹出天职选择」
_BASE_DIR = _COQ(r"\CoQ_Data\StreamingAssets\Base")

try:
    _base_mut = etree.parse(os.path.join(_BASE_DIR, "Mutations.xml"))
    _bcat = [c.get("Name") for c in _base_mut.getroot() if c.tag == "category"]
    _mine_mut = etree.parse(os.path.join(MOD, "Mutations.xml"))
    _mtpl = _mine_mut.getroot()
    _mcat = [c.get("Name") for c in _mtpl if c.tag == "category"]
    _bad_cat = [c for c in _mcat if c not in _bcat]
    if _bad_cat:
        ok = False
        print(f"[FAIL] Mutations.xml：category 名不在官方列表里（会新建类别）: {_bad_cat}")
    if not _mcat:
        ok = False
        print("[FAIL] Mutations.xml：一个 category 都没有")
    if _bad_cat == [] and _mcat:
        print(f"[OK]   Mutations.xml：{len(_mcat)} 个 category 名与官方逐字一致（官方共 {len(_bcat)} 个）")
    # 根上**不应**出现 LoadPriority：Lang 存在时 CacheFile 给的 -1000 正是
    # "语言文件最后解析、覆盖 base" 的设计（`DataFile.CompareTo` 里
    # `Other.Priority.CompareTo(this.Priority)` + 升序 `List.Sort` ⇒ 优先级越小越晚）。
    if _mtpl.get("LoadPriority") is not None:
        ok = False
        print(f"[FAIL] Mutations.xml：不该设 LoadPriority（当前 {_mtpl.get('LoadPriority')!r}）——"
              f"依赖 Lang 带来的默认 -1000 才会排在 base 之后")
    else:
        print("[OK]   Mutations.xml：未设 LoadPriority（靠 Lang 的默认 -1000 排在 base 之后）")
except Exception as e:
    ok = False
    print(f"[FAIL] Mutations.xml 校验本身出错: {e}")

# 5b) Subtypes.xml 的层级必须与官方一致（Castes 有 category 层、Callings 没有）
try:
    _base_sub = etree.parse(os.path.join(_BASE_DIR, "Subtypes.xml")).getroot()
    _mine_sub = etree.parse(os.path.join(MOD, "Subtypes.xml")).getroot()
    _shape = {}
    for _c in _base_sub:
        if _c.tag != "class":
            continue
        _shape[_c.get("ID")] = (
            [x.get("Name") for x in _c if x.tag == "category"],
            len(_c.findall("subtype")),
        )
    _bad_shape = []
    for _c in _mine_sub:
        if _c.tag != "class":
            continue
        _ref = _shape.get(_c.get("ID"))
        if _ref is None:
            _bad_shape.append((_c.get("ID"), "官方无此 class", None))
            continue
        _cats = [x.get("Name") for x in _c if x.tag == "category"]
        _backing = _c.findall("subtype")
        if _cats != _ref[0]:
            _bad_shape.append((_c.get("ID"), f"category 层={_cats}", f"官方={_ref[0]}"))
        if _backing and not _ref[1]:
            _bad_shape.append((_c.get("ID"), f"有 {len(_backing)} 个直属 subtype", "官方无（会落进自动建的空 category）"))
        elif not _backing and _ref[1]:
            _bad_shape.append((_c.get("ID"), "缺直属 subtype", f"官方有 {_ref[1]} 个"))
    if _bad_shape:
        ok = False
        for _b in _bad_shape:
            print(f"[FAIL] Subtypes.xml：{_b[0]} 层级与官方不一致 -> {_b[1]} / {_b[2]}")
    else:
        print(f"[OK]   Subtypes.xml：class/category/subtype 层级与官方一致（{[ (k, v[0], v[1]) for k, v in _shape.items() ]}）")
except Exception as e:
    ok = False
    print(f"[FAIL] Subtypes.xml 校验本身出错: {e}")

# 6) 等级文本表：`@N` 必须落在官方占位元素数量内（越界会在运行时被跳过 → 中文缺数字）
try:
    import re as _re
    _cs = open(os.path.join(MOD, "Scripts", "ZhsMutationZh_LevelText.cs"), encoding="utf-8").read()
    _counts = {}
    for _f in ("Mutations.xml", "HiddenMutations.xml"):
        _p = os.path.join(_BASE_DIR, _f)
        if not os.path.exists(_p):
            continue
        for _cat in etree.parse(_p).getroot():
            if _cat.tag != "category":
                continue
            for _m in _cat:
                if _m.tag != "mutation":
                    continue
                _lt = _m.find("leveltext")
                _n = 0
                if _lt is not None:
                    for _el in _lt.iter():
                        if isinstance(_el.tag, str) and _el.tag in ("stat", "switch", "statline", "gametext"):
                            _n += 1
                _counts[_m.get("Class") or _m.get("Name")] = _n
    _bad = []
    for _m in _re.finditer(r'\{\s*"([^"]+)",\s*new\[\]\s*\{(.*?)\}\s*\}', _cs, _re.S):
        _cls, _body = _m.group(1), _m.group(2)
        _maxn = _counts.get(_cls)
        if _maxn is None:
            _bad.append((_cls, "官方无此 Class"))
            continue
        for _at in _re.finditer(r"@(\d+)", _body):
            if int(_at.group(1)) >= _maxn:
                _bad.append((_cls, f"@{_at.group(1)} 越界（官方仅 {_maxn} 个占位元素）"))
                break
    if _bad:
        ok = False
        print(f"[FAIL] ZhsMutationZh_LevelText.cs：{len(_bad)} 条越界 -> {_bad[:6]}")
    else:
        print(f"[OK]   ZhsMutationZh_LevelText.cs：所有 @N 都在官方占位元素范围内（{len(_counts)} 个 Class 可查）")
except Exception as e:
    ok = False
    print(f"[FAIL] 等级文本表校验本身出错: {e}")

# 7) 技能/能力完整性（Class 命中、无重复、名称与说明已汉化）
#    独立脚本便于单跑：_tools/check_skills.py
try:
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import check_skills as _cs
    if not _cs.run():
        ok = False
except Exception as e:
    ok = False
    print(f"[FAIL] Skills.xml 校验本身出错: {e}")

# 8) XML 结构完整性（按身份键配对，查「官方有而我方缺」的属性）
#    ★ 这条校验器的由来：`Genotypes.xml` 丢 `Subtypes="Callings"` 等属性
#      导致「选完预设仍弹出天职选择」，追了 9 轮才定位。
#      独立脚本便于单跑：_tools/check_xml_attrs.py
try:
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import check_xml_attrs as _xa
    if not _xa.run():
        ok = False
except Exception as e:
    ok = False
    print(f"[FAIL] XML 结构校验本身出错: {e}")

# 9) 中文显示文本（查「本该译却仍是英文」的显示字段）
#    ★ 这条校验器的由来：以官方骨架重建 `EmbarkModules.xml` 时漏译了
#      `<type Title=>`、`<mode description>`、`<location description>` 等
#      散落的显示文本，玩家在「选择角色类型 / 开局地点」页直接看到英文。
#      独立脚本便于单跑：_tools/check_showtext_zh.py
try:
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import check_showtext_zh as _sz
    if not _sz.run():
        ok = False
except Exception as e:
    ok = False
    print(f"[FAIL] 中文显示文本校验本身出错: {e}")

# ══════════════════════════════════════════════════════════════════════════
#  ★★ 2026-10-15：**路由计数基线**（③-C-c，借日语组 `--expect-count` 的做法）
#     我方 1,571 条规则/译名分在 15 张表里，**改一处悄悄弄坏别处**没有任何信号。
#     基线在 `_tools\计数基线.json`；有意改动后跑 `_count_baseline.py --write` 更新。
# ══════════════════════════════════════════════════════════════════════════
try:
    import subprocess as _sp
    _cb = os.path.join(os.path.dirname(os.path.abspath(__file__)), "_count_baseline.py")
    if os.path.isfile(_cb):
        _r = _sp.run([sys.executable, _cb, "--check"], capture_output=True, text=True,
                    encoding="utf-8", errors="replace")
        print()
        print(_r.stdout.strip() or _r.stderr.strip())
        if _r.returncode != 0:
            print("[FAIL] 计数基线与源码不一致（见上）")
            _fail = True
except Exception as _e:
    print("[WARN] 计数基线检查跳过：%s" % _e)


print("\n结论：" + ("全部通过" if ok else "存在问题"))
sys.exit(0 if ok else 1)
