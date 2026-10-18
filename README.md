# CoQ 汉化工具链（Caves of Qud localization toolkit）

> 仓库：<https://github.com/LusitanianApe/LusitanianApe-s-Personal-Schinese-Translate-for-CoQ>
> 作者：<https://github.com/LusitanianApe>
> （`Schinese` 是 Steam 的简体中文语言代码，不是拼写错误）

做《Caves of Qud》这类 **Unity + 自研字符串表 + 大量 C# 拼接消息** 的游戏汉化时，
真正难的不是"翻译"，是 **找到每一句是从哪出来的、以及为什么改了没生效**。

这个仓库是我们在做一个 10k+ 条目的简体中文模组时沉淀下来的**取证与验证工具**。
不含游戏内容，不含反编译产物，只含脚本与判据。

---

## 一、核心模型：文本出口有三个，不是两个

```
① XML 数据侧        Base\*.xml + ExampleLanguage      → 覆盖文件即可
② 字符串表          (Context, ID) 查表                  → <string> 覆盖
③ C# 硬编码/拼接    `"A " + 槽 + " B."`                 → 只能挂钩子
```

第 ③ 类是最容易漏的，因为它**没有"原文"可以查表** —— 你手上只有拼接后的结果。
本仓库一半的工具是为了对付它。

### 定位一句英文的固定顺序

```
① 官方 Base / ExampleLanguage 搜整句     → 有 ⇒ 改 XML
② DLL 搜整句字面量（utf8 + utf16 双搜）  → 有 ⇒ 硬编码
③ DLL 搜**片段**                        → 有 ⇒ 拼接生成，要按句式写规则
④ 反编译定位调用点                       → 看它读哪个字段/标签
⑤ 都没有 ⇒ 运行时拼接 / Unity 资源 / 预制体默认值
```

`src/ilkit/` 就是第 ②③④ 步的工具（一个 CLI：反编译 + `ldstr` 归属 + IL 反汇编 + 元数据探针）。

---

## 二、最有用的一个：离线规则测试台（`src/rulecheck/`）

**不需要启动游戏**，把游戏里那份 IL 直接加载进控制台跑：

```powershell
$env:COQ_MANAGED = "C:\Program Files (x86)\Steam\steamapps\common\Caves of Qud\CoQ_Data\Managed"

dotnet build src\rulecheck -c Release
dotnet run -c Release --project src\rulecheck -- fixtures\_msg_fixtures.tsv
dotnet run -c Release --project src\rulecheck -- fixtures\某一句.txt --trace
```

- 默认模式：每行 `英文<TAB>期望中文`，做**回归基线**
- `--trace`：逐条规则报命中下标 —— "为什么这句没按预期翻"一眼看穿
- `--residue`：只报**译完仍含拉丁字母**的，做漏译审计

> 设计要点：用**反射**加载已编译的规则程序集，而不是把源码编进来 ——
> 游戏程序集里的 `ReadOnlySpan` / `ZString` 是 mscorlib 版，
> net8.0 下直接引源码会 `CS7069`；反射只解析用得到的方法。

---

## 三、工具清单

### `tools/probe/` —— 普查（回答"这东西在哪、占多少"）

| 脚本 | 作用 |
|---|---|
| `_fragments_probe.py` | 把 DLL 里可能显示给玩家的**英文碎片**全抄出来，按 owner 聚类 |
| `_fragment_worklist.py` | 上一步的产物二次提纯：整句 / 短语 / 状态词 / 标识符嫌疑 四档 |
| `_route_surface_status.py` | 我方各处规则计数 vs 外部基准，**报覆盖率必须带口径** |
| `_naming_usage_census.py` | 命名模板用量分布（**模板是加权随机抽的**，见 `docs/METHOD.md`） |
| `_naming_weight_detail.py` | 逐个命名样式打印各条概率 |
| `_abilityname_census.py` | 能力名的**字面量首参**与译表做差集 |
| `_authority_names.py` | 权威译名表反查（保证同一英文只有一种译法） |
| `_liquid_parts_probe.py` | 液体 `<part Alt*>` 缺口普查 |
| `_honorifics_census.py` | `Honorifics` / `Titles` 族普查（**数据侧字面量**，官方标了可译位） |
| `_nl_entity_audit.py` | 检查文件里换行实体的写法是否与官方一致（防键错配） |

### `tools/fragments/` —— 碎片字典全链路

给"动态槽 + 固定英文碎片"这种句子用的：动态名字已汉化，碎片还留着英文。

```
_zh_fragments.py  （源表，人维护）
      ↓ _gen_frag_table.py --write
生成的 C# 表（按长度降序，等价于贪心最长匹配）
      ↓ 挂在「消息定稿处」
运行时把残留英文碎片替换掉
```

| 脚本 | 作用 |
|---|---|
| `_zh_fragments.py` | 源表（分 TABLE / INFINITIVE / STATE / COMBO …） |
| `_gen_frag_table.py` | 生成 C# 表；`--write` 落盘 |
| `_frag_selfcheck.py` | **离线自检**：用同一条正则语义跑样例 + 真实语料，含反例 |
| `_frag_batch_gap.py` | ★ **批量差集**：DLL 候选 vs 现表 → 还缺哪些（分层过滤） |
| `_frag_admit.py` / `_frag_reclass.py` | 三态分流：这些英文到底该进字符串表、还是碎片表、还是写规则 |
| `_build_msg_corpus.py` | 从普查产物生成测试语料（**碎片与整句必须分开**，否则数字没法用） |

### `tools/audit/` —— 收尾检查

| 脚本 | 作用 |
|---|---|
| `validate_mod.py` | XML 可解析 / `(Context, ID)` 是否命中官方英文 / 键重复 |
| `_count_baseline.py` | 各表条目数基线，`--check` 抓"改完没生效" |
| `scan_residue.py` | 示例标记残留（`▶` 之类） |
| `_check_dupkeys.py` | C# 字典重复键（`.cctor` 会**静默失效**） |

### `tools/repair/` —— 可直接复用的修复器

| 脚本 | 作用 |
|---|---|
| `_fix_naming_load.py` | 给命名样式批量补 `Load="Merge"` / `<templates Load="Replace">` |
| `_fix_naming_templatevars.py` | 同上，补 `<templatevars Load="Replace">`（**同一个坑的第二次露面**） |

### `src/` —— C# 工程

| 工程 | 作用 |
|---|---|
| `src/ilkit/` | ★ **一个 CLI 合并四个取证工具**（ICSharpCode.Decompiler + BCL 元数据）：<br>`list` / `type` / `member` / `find` / `literals` / `ldstr`（反编译族）<br>`il` / `il-find` / `field` / `asmref` / `sig`（IL 与元数据探针） |
| `src/rulecheck/` | ★ 离线规则测试台（见上） |
| `src/stablecheck/` | 对指定 `Managed` 目录**真编译**一遍；另有裸读 `TypeAttributes` 的模式（判断某类型在不在，别信反编译器的输出） |

#### `ilkit` 速用

```powershell
ilkit --dll <程序集> list  Ascension           # 有哪些类型
ilkit --dll <程序集> find  "You have finished"  # 这句字面量被谁引用（秒级，不反编译全量）
ilkit --dll <程序集> il    XRL.World.Quest ShowFinishStepPopup   # IL 反汇编（操作数已解析）
ilkit --dll <程序集> field XRL.UI.JournalScreen STR_LOCATIONS     # 静态字段读写点
ilkit --dll <程序集> asmref ReadOnlySpan        # 这个类型到底属于哪个程序集
```

> `find` 的实现是**扫 `ldstr`**（ECMA-335 operand 表 + `#US` heap），不做全量反编译：
> 同一个 20 MB 程序集，旧的反编译法 222.5 s，这条路 2.4 s，而且只收**二进制里真实存在**的串。

---

## 四、几条踩出来的判据（`docs/METHOD.md` 有全文）

```
① 「有译文」≠「生效」：判定"译没译"只看该处是不是中文，不看文件名出现在搜索结果里
② 命名模板是**加权随机抽**的；`<templates>` 与 `<templatevars>` 都必须 Load="Replace"
   否则官方英文条目从不被移除 ⇒ 中英 1/N 轮盘
③ 前置片段（以空格结尾）不能加右词边界 —— 后面紧跟名字是正常的
④ 碎片表若只挂消息栏，**弹窗是盲区**（弹窗不经过 MessageQueue）
⑤ `Show(ID)` 的单 token 是**字符串表的键**，不是碎片；混在一起查会显得"永远补不完"
⑥ 规则顺序：专用规则必须排在通配规则**之前**，否则被"短词抢先"
```

---

## 五、环境

```
Python 3.10+（标准库为主；部分脚本用 lxml）
.NET 8 SDK（C# 工程）
Windows / PowerShell（脚本里有 pwsh 调用；核心逻辑跨平台）
```

`COQ_DIR`：游戏安装目录（默认指向 Steam 常见位置）
`COQ_MANAGED`：`<游戏>\CoQ_Data\Managed`

---

## 六、规则夹具：松捕获 → 严校验 → 兜底不半译（`tools/rules/`）

```powershell
python tools\rules\rule_spec_check.py          # 机器跑一遍 → 生成人工复核表
```

- `_rule_spec.json`：每条规则带 `positives`（必须命中）/ `negatives`（必须不命中）
- `_rule_review_table.md`：机器结果 + 空白的「人工意见」列，给人复核用
- 判据：正例 `命中`=匹配上、`漏`=没匹配；反例 `未命中`=正确、`误伤`=匹配了

它治的是「正则卡太死」和「正则放太松」两种病：
先用**松捕获**认形状，再用**槽校验**收紧，最后给宽规则加**兜底负向断言**
（宁可整句不译，也不半译）。

## 六·补、批量离线查漏（`tools/audit/`）

```powershell
python tools\audit\_audit_popup_forms.py      # Popup / MessageQueue / Messaging 的句子形字面量
python tools\audit\_audit_other_exits.py      # GetDescription / GetDisplayName / GetLevelText 的字面量
python tools\audit\_triage68.py               # 缺口分诊：串表已覆盖 / 调试内部 / 需要规则
```

输出 `*_gaps.txt`（原文 → 译文对照），可直接当补译清单。
实际用法：先跑审计拿缺口 → 分诊 → 写规则/补串表 → 复跑看数字下降。

## 六·补2、发布分支与工作区整理（`tools/release/`、`tools/maintenance/`）

```powershell
python tools\release\release_branch.py --to-release   # 生成「无探针」发布版并装机
python tools\release\release_branch.py --to-dev       # 切回开发版（带探针）
python tools\maintenance\cleanup_workspace.py         # 备份留 5 删 5 留 1 + 清缓存（默认干跑）
```

适用于「Steam 创意工坊只能游戏内上传」的游戏：用**两个目录 + 不同 ID** 共存，
开发版带诊断探针、发布版不带，靠 Release 脚本一键切换。

## 七、版权

**本仓库不含任何游戏反编译产物、游戏资源或游戏 API 文档。**
详见 `NOTICE.md`。

代码按 MIT 发布，见 `LICENSE`。
