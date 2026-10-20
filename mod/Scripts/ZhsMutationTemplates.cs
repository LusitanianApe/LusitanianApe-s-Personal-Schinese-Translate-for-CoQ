// 变异「说明 / 等级文本」模板注册（1.0.2 新增，替代之前失败的纯数据文件方案）。
//
// 【为什么必须由我们来注册】
//   官方的读取侧是 `XRL.World.Parts.Mutation.BaseMutation.GetDescription()` /
//   `GetLevelText(int)`，IL 明确：
//
//       Templates.TemplateByID.TryGetValue("Mutation." + entry.Name + ".Description", out t)
//         || TryGetValue("Mutation." + GetMutationClass() + ".Description", out t)
//         ? t.Build(CollectStats, "mutation description").TrimEnd('\r','\n')
//         : "<description>"                       // ← 找不到就返回这个字面量
//   LevelText 同理，兜底字面量是 "<Does level N stuff>"。
//
//   而**注册侧**只有 `MutationFactory.HandleDescriptionNode` / `HandleLevelTextNode`
//   两个方法，全程序集里它们**零调用点**（方法体 IL 全量扫描：这两个名字只在自身定义处出现）。
//   实测 Player.log：1.0.1 装机后 `Templates.TemplateByID` 里
//   `Mutation.*.Description` 恰有 95 条（HiddenMutations 那批），但**普通变异的
//   Description / LevelText 一条都没有** —— 于是角色创建右侧面板原样显示
//   `<description>` 和 `<Does level 1 stuff>`。
//
// 【做法】
//   在 `MutationFactory.Init` 的 Postfix 里（此时官方解析早已结束）直接往
//   `Templates.TemplateByID` 写模板。两种写法：
//     * 纯文本（说明） → `Template { ID = ..., Nodes = { new TextNode(中文) } }`
//       `TextNode(string)` 是 public 构造、`Template` 有默认构造。
//     * 带占位元素（等级文本，含 <stat>/<statline>/<switch>）→ **必须交回官方解析器**：
//       `new XmlDataHelper(Stream, null)` + `Templates.ReadTemplateNode(xml)`。
//       ⚠️ 1.0.2 第一版就是漏了这一步：等级文本被当成字符串塞进 TextNode，
//          结果面板把整段 `<template …><p><switch …></template>` XML **原样显示**出来。
//
// 【安全性】
//   * 只写 TemplateByID 里 `Mutation.` 前缀的键，绝不碰别的模板；全程 try/catch。
//   * `_zhs_mutation_off.txt` 可整体停用。

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsMutationTemplates
    {
        private static bool _done;
        private static int _desc, _level, _fallback, _putFail, _xmlFail, _empty, _emptyNoNode;

        private static IDictionary _byId;
        private static MethodInfo _readTemplateNode;
        private static Type _xmlHelperType;

        /// <summary>
        /// 注册空模板：`TextNode("")`。
        /// 空字符串不能走 `PutText` 的 `IsNullOrEmpty` 早退，所以单独一个方法。
        /// </summary>
        private static bool PutEmpty(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            try
            {
                var byId = ById;
                if (byId == null) return false;
                Type tplType = AccessTools.TypeByName("XRL.Templates+Template");
                Type txtType = AccessTools.TypeByName("XRL.Templates+TextNode");
                if (tplType == null || txtType == null) { _putFail++; return false; }
                object tpl = Activator.CreateInstance(tplType);
                AccessTools.Field(tplType, "ID")?.SetValue(tpl, id);
                var nodes = AccessTools.Field(tplType, "Nodes")?.GetValue(tpl) as IList;
                if (nodes == null) { _putFail++; return false; }
                nodes.Clear();
                nodes.Add(Activator.CreateInstance(txtType, new object[] { "" }));
                byId[id] = tpl;
                return true;
            }
            catch { _putFail++; return false; }
        }

        private static IDictionary ById
        {
            get
            {
                if (_byId == null)
                    _byId = AccessTools.Field(typeof(XRL.Templates), "TemplateByID")?.GetValue(null) as IDictionary;
                return _byId;
            }
        }

        /// <summary>把一条说明（纯文本）塞进 TemplateByID。</summary>
        private static bool PutText(string id, string text)
        {
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(text)) return false;
            try
            {
                var byId = ById;
                if (byId == null) return false;
                Type tplType = AccessTools.TypeByName("XRL.Templates+Template");
                Type txtType = AccessTools.TypeByName("XRL.Templates+TextNode");
                if (tplType == null || txtType == null) { _putFail++; return false; }

                // 说明是「段落」语义：每个 \n 一段，交给模板里的换行节点渲染
                object tpl = Activator.CreateInstance(tplType);
                AccessTools.Field(tplType, "ID")?.SetValue(tpl, id);
                var nodes = AccessTools.Field(tplType, "Nodes")?.GetValue(tpl) as IList;
                if (nodes == null) { _putFail++; return false; }
                nodes.Clear();
                object newline = AccessTools.Field(typeof(XRL.Templates), "NEWLINE")?.GetValue(null);
                string[] paras = text.Split('\n');
                for (int i = 0; i < paras.Length; i++)
                {
                    if (i > 0 && newline != null) nodes.Add(newline);
                    nodes.Add(Activator.CreateInstance(txtType, new object[] { paras[i] }));
                }
                byId[id] = tpl;
                return true;
            }
            catch (Exception e)
            {
                if (_putFail == 0)
                    UnityEngine.Debug.LogError("[QudZhsCookbook] 变异模板写入失败 " + id + "：" + e.Message);
                _putFail++;
                return false;
            }
        }

        /// <summary>
        /// 把一段模板 XML 交给**官方解析器**注册（等级文本必须走这条 —— 里面有
        /// `<stat>` / `<statline>` / `<switch>`，塞进 TextNode 会被当成字面量显示）。
        ///
        /// 三种喂法依次降级：
        ///   ① `new XmlDataHelper(MemoryStream)`             —— 无文件、最快
        ///   ② 写临时文件 + `new XmlDataHelper(Stream)`      —— 让 reader 有真实来源
        ///   ③ 写临时文件 + `new XmlDataHelper(路径字符串)`  —— 最贴近官方 `DataManager` 的用法
        ///
        /// ★ 1.1.3 更正：三种喂法**本身都是好的**。1.0.3–1.1.2 期间 130 条全部
        ///   `ArgumentNullException Value cannot be null (Parameter name: key)`
        ///   的真实原因是**读取器没定位到根元素**（见 `PositionReader` 的注释），
        ///   不是喂法有问题 —— 当时把锅甩给了喂法，因此才有了这套没必要的三级降级。
        ///   现在①就够了；②③保留作为兜底。
        /// </summary>
        private static bool PutXml(string id, string xml)
        {
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(xml)) return false;
            try
            {
                if (_readTemplateNode == null)
                {
                    _readTemplateNode = AccessTools.Method(typeof(XRL.Templates), "ReadTemplateNode");
                    _xmlHelperType = AccessTools.TypeByName("XRL.XmlDataHelper");
                }
                if (_readTemplateNode == null || _xmlHelperType == null || ById == null) { _xmlFail++; return false; }

                // ★★ 2.16.38：**幂等化**。调用方可能已经自带 XML 声明
                //   （2.16.34 给 `BuildLevelText` 加过一条 ⇒ 这里再拼一次 = **两个 XML 声明**）
                //   ⇒ `XmlReader.MoveToContent()` 跳过第一个声明后 `Read()` 撞上第二个，
                //     抛 `Unexpected XML declaration`（位置 `line 1 char 40` = 第二个声明的 `?`）
                //   ⇒ **128 条等级文本模板全部注册失败**（日志：`等级文本 XML 解析失败 128`）
                string full = xml.StartsWith("<?xml", StringComparison.Ordinal)
                    ? xml
                    : "<?xml version=\"1.0\" encoding=\"utf-8\"?>" + xml;
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(full);

                Exception last = null;
                string stage = "";
                // ① 内存流
                if (!_streamBroken)
                {
                    try { stage = "内存流构造 XmlDataHelper"; Feed(bytes, null, null); last = null; }
                    catch (Exception e) { last = e; _streamBroken = true; }
                }
                if (last != null || _streamBroken)
                {
                    // ②/③ 临时文件（路径尽量放在模组自己的目录下，避免 temporaryCachePath 取不到）
                    string path = WriteTemp(full);
                    if (path == null)
                    {
                        stage = "临时文件写入失败（temporaryCachePath/modDir 都不可用）";
                    }
                    else
                    {
                        try { stage = "临时文件 + XmlDataHelper(Stream)"; Feed(null, path, null); last = null; }
                        catch (Exception e2)
                        {
                            last = e2;
                            try { stage = "临时文件 + XmlDataHelper(路径)"; Feed(null, null, path); last = null; }
                            catch (Exception e3) { last = e3 ?? e2; }
                        }
                        try { System.IO.File.Delete(path); } catch { }
                    }
                }

                if (last != null)
                {
                    if (_xmlFail == 0)
                    {
                        Exception root = last;
                        while (root.InnerException != null) root = root.InnerException;
                        UnityEngine.Debug.LogError("[QudZhsCookbook] 等级文本模板注册失败 " + id
                            + "（阶段：" + stage + "）"
                            + "：" + root.GetType().Name + " " + root.Message
                            + "\n—— 模板 XML ——\n" + xml
                            + "\n—— 栈 ——\n" + last.StackTrace);
                    }
                    _xmlFail++;
                    return false;
                }
                return _CountAndReturn(id);
            }
            catch (Exception e)
            {
                if (_xmlFail == 0)
                    UnityEngine.Debug.LogError("[QudZhsCookbook] 等级文本模板注册异常 " + id + "：" + e);
                _xmlFail++;
                return false;
            }
        }

        /// <summary>
        /// 走完 `PutXml` 主流程后的收尾：计数 + 返回「键是否真的在字典里」。
        ///
        /// ★ 为什么要单独计数：1.1.8 的日志「等级文本模板 64 条 / 解析失败 0」全绿，
        ///   但 `GetLevelText` 返回的仍是英文原文（含未展开的 `@N`），
        ///   且「含未展开占位符」的条数（33）= 模板 64 − 官方空 31 ——
        ///   强烈提示**我们那份中文 XML 根本没进字典**，
        ///   而原代码 `return ById.Contains(id)` 的返回值**从未被检查**，失败被完全吞掉。
        /// </summary>
        private static bool _CountAndReturn(string id)
        {
            bool present = false;
            try { present = ById != null && ById.Contains(id); } catch { }
            if (present) _xmlOk++; else _xmlContainFalse++;
            return present;
        }

        // ★ 1.1.9：`PutXml` 的**成败计数**。
        //   为什么需要：1.1.8 的日志显示「等级文本模板 64 条 / XML 解析失败 0」全绿，
        //   但 `GetLevelText` 返回的仍是**英文原文**（含未展开的 `@N`）。
        //   而「含未展开占位符」的条数（33）恰好等于「模板 64 − 官方空 31」——
        //   说明**我们那份中文 XML 根本没进字典**，`PutXml` 静默失败了：
        //   原来的代码只用 `ById.Contains(id)` 当返回值，且**从不检查它**，
        //   于是失败被完全吞掉。这两个计数就是为了不再被骗。
        private static int _xmlOk, _xmlContainFalse;

        /// <summary>「字典键 → 实际注册名」的前几条样本（诊断用）。</summary>
        private static int _aliasTraced;
        private static readonly List<string> _aliasLog = new List<string>();

        public static string XmlStats()
        {
            return "PutXml 成功 " + _xmlOk + " 次；返回后键不在字典 " + _xmlContainFalse + " 次";
        }

        private static bool _streamBroken;

        /// <summary>
        /// 是否在 `Invoke` 之前把读取器定位到根元素。
        ///
        /// ★ 这是 1.1.3 修掉 `等级文本 XML 解析失败 130` 的关键。
        ///
        /// 根因（IL 实证 + 日志实证）：
        ///   `XRL.Templates.ReadTemplateNode` → `XRL.Templates+Template::HandleTemplateNode`
        ///   第一件事就是 `id = xml.ParseAttribute("ID", null, /*required*/ true)`，
        ///   然后 `TemplateByID[t.ID] = t`。也就是说**读取器必须已经停在
        ///   `<template ID="…">` 元素上**。
        ///   而 `XmlDataHelper` 初始位置是 `XmlDeclaration` 节点
        ///   （日志实证 `Read()=True NodeType=XmlDeclaration`），
        ///   `ParseAttribute` 取不到 ID ⇒ `Template.ID = null`
        ///   ⇒ `TemplateByID[null]` 抛
        ///   `ArgumentNullException: Value cannot be null. Parameter name: key`。
        ///
        /// 可用开关文件 `_zhs_tpl_readfirst.txt` 关掉（写 `off`），
        /// 以便不改代码就能对比两种行为 —— 因为这条结论必须在游戏里复验。
        /// </summary>
        private static bool _positionBeforeInvoke = true;
        private static bool _posRead;

        private static bool ReadToggle()
        {
            bool on = true;
            try
            {
                string modDir = ZhsTrace.ModDirectory;
                if (!string.IsNullOrEmpty(modDir))
                {
                    string p = System.IO.Path.Combine(modDir, "_zhs_tpl_readfirst.txt");
                    if (System.IO.File.Exists(p))
                    {
                        string txt = System.IO.File.ReadAllText(p).Trim().ToLowerInvariant();
                        on = !(txt == "off" || txt == "0" || txt == "false" || txt == "no");
                    }
                }
            }
            catch { }
            return on;
        }

        /// <summary>
        /// 把读取器从 `XmlDeclaration`（或其它非元素状态）推进到**根元素**。
        /// 只推进一次；已就位则不动（避免第二次 `Read()` 越过 `<template>`）。
        /// </summary>
        private static void PositionReader(object helper)
        {
            if (!_positionBeforeInvoke) return;
            var rd = helper as System.Xml.XmlReader;
            if (rd == null) return;
            try
            {
                if (rd.ReadState == System.Xml.ReadState.Initial) rd.Read();   // 跳过 xml 声明
                if (rd.NodeType != System.Xml.XmlNodeType.Element) rd.MoveToContent();
                if (!_posRead)
                {
                    _posRead = true;
                    UnityEngine.Debug.Log("[QudZhsCookbook] 模板通道：读取器已定位到根元素 "
                        + rd.NodeType + " <" + rd.Name + ">");
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[QudZhsCookbook] 模板通道：定位读取器失败：" + e);
            }
        }

        private static bool Feed(byte[] bytes, string streamPath, string uriPath)
        {
            if (!_toggleRead)
            {
                _toggleRead = true;
                _positionBeforeInvoke = ReadToggle();
                UnityEngine.Debug.Log("[QudZhsCookbook] 模板通道：Invoke 前定位读取器 = "
                    + (_positionBeforeInvoke ? "开（默认）" : "关（开关文件要求）"));
            }

            object helper;
            if (streamPath != null)
            {
                using (var fs = new System.IO.FileStream(streamPath, System.IO.FileMode.Open,
                                                         System.IO.FileAccess.Read, System.IO.FileShare.Read))
                    helper = Activator.CreateInstance(_xmlHelperType, new object[] { fs, null });
            }
            else if (uriPath != null)
            {
                helper = Activator.CreateInstance(_xmlHelperType, new object[] { uriPath, null });
            }
            else
            {
                using (var ms = new System.IO.MemoryStream(bytes))
                    helper = Activator.CreateInstance(_xmlHelperType, new object[] { ms, null });
            }
            if (helper == null) throw new InvalidOperationException("XmlDataHelper 构造返回 null");

            // ★ 必须在 Invoke **之前**定位；且诊断不能再移动读取器
            //   （旧代码在诊断里 `rd.Read()`，且只在头两次执行 ——
            //    等于「前两次被诊断救了一把、其余 128 次没救」，是个自伤 bug）。
            PositionReader(helper);

            if (_feedTraced < 3)
            {
                _feedTraced++;
                var rd0 = helper as System.Xml.XmlReader;
                UnityEngine.Debug.Log("[QudZhsCookbook] Feed 诊断 #" + _feedTraced
                    + "：helper=" + helper.GetType().FullName
                    + "；定位后 NodeType=" + (rd0 == null ? "(非 XmlReader)" : rd0.NodeType.ToString())
                    + " Name=" + (rd0 == null ? "-" : rd0.Name)
                    + "；stream=" + (streamPath ?? "(内存)")
                    + "；uri=" + (uriPath ?? "(无)"));
            }

            _readTemplateNode.Invoke(null, new object[] { helper });
            return true;
        }

        private static bool _toggleRead;
        private static int _feedTraced;

        private static string WriteTemp(string content)
        {
            try
            {
                string dir = System.IO.Path.Combine(UnityEngine.Application.temporaryCachePath, "zhs_tpl");
                System.IO.Directory.CreateDirectory(dir);
                string path = System.IO.Path.Combine(dir, "t" + DateTime.Now.Ticks + ".xml");
                System.IO.File.WriteAllText(path, content, new System.Text.UTF8Encoding(false));
                return path;
            }
            catch { return null; }
        }

        /// <summary>
        /// 官方原始说明 / 等级文本（英文兜底），键 = Name 或 Class。
        /// 直接读 streamingAssets 的 Mutations.xml + HiddenMutations.xml。
        /// </summary>
        private static readonly Dictionary<string, string> Official = new Dictionary<string, string>(StringComparer.Ordinal);
        private static bool _officialLoaded;

        private static void LoadOfficial()
        {
            if (_officialLoaded) return;
            _officialLoaded = true;
            try
            {
                var doc = OfficialDoc();
                if (doc == null) return;
                foreach (System.Xml.XmlNode cat in doc.DocumentElement.ChildNodes)
                {
                    if (cat.NodeType != System.Xml.XmlNodeType.Element || cat.Name != "category") continue;
                    foreach (System.Xml.XmlNode mut in cat.ChildNodes)
                    {
                        if (mut.NodeType != System.Xml.XmlNodeType.Element || mut.Name != "mutation") continue;
                        string nm = mut.Attributes?["Name"]?.Value;
                        string cl = mut.Attributes?["Class"]?.Value;
                        string desc = NodeText(mut, "description");
                        string lvl = NodeText(mut, "leveltext");
                        foreach (string key in new[] { nm, cl })
                        {
                            if (string.IsNullOrEmpty(key)) continue;
                            if (!string.IsNullOrEmpty(desc) && !Official.ContainsKey(key + "\u0001D")) Official[key + "\u0001D"] = desc;
                            if (!string.IsNullOrEmpty(lvl) && !Official.ContainsKey(key + "\u0001L")) Official[key + "\u0001L"] = lvl;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[QudZhsCookbook] 变异模板：解析官方 XML 失败 " + e.Message);
            }
        }

        private static string NodeText(System.Xml.XmlNode mut, string tag)
        {
            foreach (System.Xml.XmlNode c in mut.ChildNodes)
                if (c.NodeType == System.Xml.XmlNodeType.Element && c.Name == tag)
                    return Collapse(c.InnerText);
            return null;
        }

        /// <summary>把 XML 缩进空白压成单空格，并去掉行首尾。</summary>
        private static string Collapse(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var sb = new System.Text.StringBuilder(s.Length);
            bool sp = false;
            foreach (char ch in s)
            {
                if (ch == '\r') continue;
                if (ch == '\n' || ch == '\t' || ch == ' ')
                {
                    if (!sp && sb.Length > 0) { sb.Append(' '); sp = true; }
                    continue;
                }
                sb.Append(ch); sp = false;
            }
            string r = sb.ToString().Trim();
            return r.Length == 0 ? null : r;
        }

        /// <summary>在 MutationFactory.Init 之后调用一次。</summary>
        public static void Install()
        {
            if (_done || ZhsMutationZh.Disabled) return;
            _done = true;
            try
            {
                if (ById == null)
                {
                    UnityEngine.Debug.LogError("[QudZhsCookbook] 变异模板：取不到 Templates.TemplateByID");
                    return;
                }

                var official = Official;   // 仅用于日志里的条目数
                LoadOfficial();
                ProbeReadTemplateNode();   // 自检：官方解析通道本身通不通
                var names = new List<string>();   // 注册用的「条目名」
                var classes = new List<string>();
                try
                {
                    var byName = AccessTools.Field(typeof(XRL.MutationFactory), "_MutationsByName")?.GetValue(null) as IDictionary;
                    if (byName != null)
                        foreach (DictionaryEntry e in byName)
                        {
                            string dictKey = e.Key as string;
                            if (string.IsNullOrEmpty(dictKey)) continue;
                            // ★★ 1.1.8 修掉的关键 bug ★★
                            //   `_MutationsByName` 的**键**是 `MutationFactory.Init` 当初写进去的
                            //   **英文名**；可 `ZhsMutationPatch` 随后把 `entry.Name` 改成了中文。
                            //   而读侧 `BaseMutation.GetLevelText()` 用的是
                            //     `GetMutationEntry().Name`  ← 已改名 ⇒ **中文**
                            //   于是「注册用英文键、查询用中文键」⇒ 必然落空，静默退回英文模板。
                            //   （1.1.7 插桩实证：EntryName=羽刺 / 注册键=Quills / Name键=False）
                            //
                            //   ⇒ 注册必须用**条目当前的 `Name`**（= 读侧会用的那个键）。
                            //     字典键只作为额外变体一起注册，两条路都命中。
                            string entryName = ZhsMutationUtil.FieldStr(e.Value, "Name");
                            names.Add(!string.IsNullOrEmpty(entryName) ? entryName : dictKey);
                            string cl = ZhsMutationUtil.FieldStr(e.Value, "Class");
                            classes.Add(cl);
                            if (_aliasTraced < 8)
                            {
                                _aliasTraced++;
                                _aliasLog.Add("  [" + dictKey + "] → 注册名=" + names[names.Count - 1]
                                    + "（FieldStr(Name)=" + (entryName ?? "(null)") + "）Class=" + (cl ?? "(null)")
                                    + " 对象类型=" + (e.Value == null ? "(null)" : e.Value.GetType().Name));
                            }
                            if (!string.IsNullOrEmpty(entryName)
                                && !string.Equals(entryName, dictKey, StringComparison.Ordinal))
                                _extraKeyAliases.Add(new KeyValuePair<string, string>(entryName, dictKey));
                        }
                }
                catch { }

                for (int i = 0; i < names.Count; i++)
                {
                    string nm = names[i];
                    string cl = i < classes.Count ? classes[i] : null;
                    string clsKey = !string.IsNullOrEmpty(cl) ? cl : nm;
                    string nmAlt = AliasFor(nm);   // 字典键（英文）—— 一起注册，避免漏

                    // 1) 说明
                    string[] paras;
                    ZhsMutationZh.DescByClass.TryGetValue(clsKey, out paras);
                    string text = paras != null ? string.Join("\n", paras) : null;
                    if (string.IsNullOrEmpty(text))
                        text = PickOfficial(official, nm, nmAlt, cl, "\u0001D");   // 官方原文兜底
                    else
                        _desc++;

                    if (!string.IsNullOrEmpty(text))
                    {
                        PutText("Mutation." + nm + ".Description", text);
                        if (!string.IsNullOrEmpty(nmAlt)) PutText("Mutation." + nmAlt + ".Description", text);
                        if (!string.IsNullOrEmpty(cl)) PutText("Mutation." + cl + ".Description", text);
                    }

                    // 2) 等级文本（中文优先，否则官方原文 —— 保证数字占位能渲染）
                    // ★ 模板 ID 必须**逐个条目不同** —— `ReadTemplateNode` 用模板自己的
                    //   `ID` 属性当字典键，用常量 ID 会让所有模板互相覆盖。
                    bool zhLvl = ZhsMutationLevelText.ByClass.ContainsKey(clsKey);
                    string lvlId = "Mutation." + nm + ".LevelText";
                    string lvl = BuildLevelText(clsKey, nm, lvlId);
                    bool isXml = !string.IsNullOrEmpty(lvl);
                    if (!isXml)
                    {
                        LoadOfficial();
                        lvl = PickOfficial(official, nm, nmAlt, cl, "\u0001L");   // 纯文本兜底
                        if (!string.IsNullOrEmpty(lvl)) _fallback++;
                    }
                    else if (zhLvl) _level++;
                    else _fallback++;

                    if (isXml)
                    {
                        // ★ 等级文本必须走官方解析器（里面有 stat/statline/switch），
                        //   塞进 TextNode 会被当成字面量原样显示（1.0.2 第一版的翻车点）。
                        PutXml(lvlId, lvl);
                        if (!string.IsNullOrEmpty(nmAlt))
                            PutXml("Mutation." + nmAlt + ".LevelText",
                                   BuildLevelText(clsKey, nm, "Mutation." + nmAlt + ".LevelText") ?? lvl);
                        if (!string.IsNullOrEmpty(cl))
                            PutXml("Mutation." + cl + ".LevelText",
                                   BuildLevelText(clsKey, nm, "Mutation." + cl + ".LevelText") ?? lvl);
                    }
                    else if (!string.IsNullOrEmpty(lvl))
                    {
                        PutText("Mutation." + nm + ".LevelText", lvl);
                        if (!string.IsNullOrEmpty(nmAlt)) PutText("Mutation." + nmAlt + ".LevelText", lvl);
                        if (!string.IsNullOrEmpty(cl)) PutText("Mutation." + cl + ".LevelText", lvl);
                    }
                    else
                    {
                        // 没有任何可用内容 —— 必须注册**空模板**，
                        // 否则 `GetLevelText` 会返回兜底字面量 `<Does level N stuff>` 露在面板上。
                        //
                        // ★ 1.2.3 修正判据：原来只在 `HasOfficialLevelText`（= 官方**有**
                        //   `<leveltext>` 节点）时才注册空模板。但实测发现有些变异
                        //   （`Gills` / `Interdiction`：官方 `Mutations.xml` 里**连
                        //   `<leveltext>` 节点都没有**）⇒ 那条判据不成立 ⇒ 不注册
                        //   ⇒ 面板显示 `<Does level 1 stuff>`。
                        //   现在改为「只要没内容就注册空模板」，并把两种情况分开计数。
                        PutEmpty("Mutation." + nm + ".LevelText");
                        if (!string.IsNullOrEmpty(nmAlt)) PutEmpty("Mutation." + nmAlt + ".LevelText");
                        if (!string.IsNullOrEmpty(cl)) PutEmpty("Mutation." + cl + ".LevelText");
                        if (HasOfficialLevelText(clsKey, nm) || HasOfficialLevelText(clsKey, nmAlt))
                            _empty++;          // 官方有节点但内容为空（31 个）
                        else
                            _emptyNoNode++;    // 官方连节点都没有（Gills / Interdiction）
                    }
                }

                UnityEngine.Debug.Log("[QudZhsCookbook] 变异模板注册：条目 " + names.Count
                    + "，说明用中文 " + _desc + " 条，等级文本模板 " + _level
                    + " 条（官方空 leveltext " + _empty + "，官方无 leveltext 节点 " + _emptyNoNode + "）"
                    + "，说明纯文本写入失败 " + _putFail
                    + "；等级文本 XML 解析失败 " + _xmlFail
                    + "；" + XmlStats());

                UnityEngine.Debug.Log("[QudZhsCookbook] 注册名样本（字典键 → 实际注册名）：");
                foreach (string s in _aliasLog) UnityEngine.Debug.Log("[QudZhsCookbook]" + s);

                // ★ 1.1.4 新增：**端到端**自检 —— 别只看"注册了多少条"，
                //   要真去问游戏的读侧「你查得到吗、返回什么」。
                //   实测教训：注册数 64/0 全绿，但面板仍无中文 ——
                //   说明问题在**键不匹配**（`GetLevelText` 用的键 ≠ 注册用的键）。
                // ★★ 2026-10-15（待办 C1）：**往返探针改成开关控制，默认关**。
            //   为什么：探针在 `MutationFactory.Init` 期间反射调 `GetLevelText(1)`，
            //   而此刻 `Quills` / `SleepGas` 等**装备蓝图尚未注册** ⇒ 游戏自己刷
            //     `Unknown blueprint (tell support@…)` +
            //     `Attempting to add argument 'null' with alias 'blueprint'`
            //   ⇒ 纯噪音（业务无影响），但污染日志、干扰排查。
            //   要开：模组目录建 `_zhs_ltprobe_on.txt`（与其它开关同风格）。
            if (System.IO.File.Exists(System.IO.Path.Combine(
                    ZhsTrace.ModDirectory ?? "", "_zhs_ltprobe_on.txt")))
                ProbeLevelTextRoundTrip(names, classes);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[QudZhsCookbook] 变异模板注册失败：" + e);
            }
        }

        /// <summary>
        /// 端到端自检：走**游戏自己的读侧**（`BaseMutation.GetLevelText` / `.Description`）
        /// 取几条，并把「注册时用的键」与「条目当前的 Name」一起打出来。
        ///
        /// 这条探针是为了抓「键不匹配」—— 注册全成功、查不到，就是这么来的。
        /// </summary>
        private static void ProbeLevelTextRoundTrip(List<string> names, List<string> classes)
        {
            try
            {
                var byName = AccessTools.Field(typeof(XRL.MutationFactory), "_MutationsByName")?.GetValue(null) as IDictionary;
                if (byName == null) { UnityEngine.Debug.Log("[QudZhsCookbook] 端到端自检：取不到 _MutationsByName"); return; }

                var bmType = AccessTools.TypeByName("XRL.World.Parts.Mutation.BaseMutation");
                var getLvl = AccessTools.Method(bmType, "GetLevelText");
                var getDesc = AccessTools.Method(bmType, "GetDescription");
                if (getLvl == null) { UnityEngine.Debug.Log("[QudZhsCookbook] 端到端自检：找不到 GetLevelText"); return; }

                // ★★ 关键教训（1.1.6 实测）：`_MutationsByName` 里装的是
                //   `XRL.MutationEntry`，**不是** `BaseMutation`！
                //   拿它去调 `BaseMutation.GetLevelText` 会抛
                //   `TargetException: Object does not match target type.`
                //   —— 于是整个探针的结论（"注册键 False/False"）都是假的。
                //   正确做法：走 `MutationEntry.Mutation`（BaseMutation 字段）
                //   或 `CreateInstance()` 拿实例，再调方法。
                var entryType = AccessTools.TypeByName("XRL.MutationEntry");
                var fMut = entryType == null ? null : AccessTools.Field(entryType, "Mutation");
                var mCreate = entryType == null ? null : AccessTools.Method(entryType, "CreateInstance");
                UnityEngine.Debug.Log("[QudZhsCookbook] 端到端自检：MutationEntry.Mutation 字段="
                    + (fMut != null) + " CreateInstance=" + (mCreate != null));

                int n = 0, keyHitLvl = 0, keyHitDesc = 0, probed = 0, noInst = 0;
                int zhLvlOk = 0, placeholders = 0, enFallback = 0;
                var samples = new List<string>();
                var problems = new List<string>();
                foreach (DictionaryEntry e in byName)
                {
                    string dictKey = e.Key as string;
                    object entry = e.Value;                 // MutationEntry
                    if (entry == null) continue;
                    probed++;

                    // 取 BaseMutation 实例
                    // ★ 优先 `CreateInstance()` —— 它返回**新建的干净实例**，
                    //   不共享 `_Entry` 缓存，最接近面板里用的那个对象。
                    //   （1.1.6 直接用 `Mutation` 字段的实例，可能带着
                    //    `_Entry=null` 等历史状态，结论会失真。）
                    object inst = null;
                    try { if (mCreate != null) inst = mCreate.Invoke(entry, null); } catch { }
                    if (inst == null)
                        try { if (fMut != null) inst = fMut.GetValue(entry); } catch { }
                    if (inst == null) { noInst++; continue; }

                    string entryName = null;
                    try { entryName = ZhsMutationUtil.FieldStr(entry, "Name"); } catch { }
                    string mutClass = null;
                    try
                    {
                        var gmc = AccessTools.Method(bmType, "GetMutationClass");
                        if (gmc != null) mutClass = gmc.Invoke(inst, null) as string;
                    }
                    catch { }
                    if (string.IsNullOrEmpty(mutClass)) mutClass = ZhsMutationUtil.FieldStr(entry, "Class");

                    bool haveByName = !string.IsNullOrEmpty(entryName) && ById.Contains("Mutation." + entryName + ".LevelText");
                    bool haveByCls = !string.IsNullOrEmpty(mutClass) && ById.Contains("Mutation." + mutClass + ".LevelText");
                    bool haveReg = haveByName || haveByCls;
                    if (haveReg) keyHitLvl++;
                    if ((!string.IsNullOrEmpty(entryName) && ById.Contains("Mutation." + entryName + ".Description"))
                        || (!string.IsNullOrEmpty(mutClass) && ById.Contains("Mutation." + mutClass + ".Description")))
                        keyHitDesc++;

                    string lvl = null, desc = null, err = "";
                    try { lvl = getLvl.Invoke(inst, new object[] { 1 }) as string; }
                    catch (Exception ex) { err = " LvText抛:" + Root(ex).GetType().Name + " " + Root(ex).Message; }
                    try { desc = getDesc == null ? null : getDesc.Invoke(inst, null) as string; } catch { }
                    bool lvlZh = !string.IsNullOrEmpty(lvl) && Markup.HasCjk(lvl);
                    if (lvlZh) zhLvlOk++;
                    bool lvlBad = !string.IsNullOrEmpty(lvl)
                        && (lvl.Contains("<Does level") || lvl.Contains("<stat")
                            || lvl.Contains("<switch") || lvl.Contains("<statline"));
                    if (lvlBad) placeholders++;
                    // ★ 1.2.3：区分「官方有文本、我们没译」和「官方本来就是空的」。
                    //   后者在游戏里本来也不显示任何东西，不是我们的缺口。
                    bool officialHasText = OfficialPlaces(mutClass, entryName).Count > 0;
                    if (!lvlZh && !lvlBad && officialHasText) enFallback++;

                    // 判据 = 「**产出**是否中文」而不是「键是否存在」。
                    //   1.1.7 的教训：键检查全 True，可返回值仍是英文原文，
                    //   因为 `GetLevelText` 命中回退键、拿到了 `_fallback` 注册的英文模板。
                    //   `<Does level N stuff>` 才是真 bug（占位符没展开）。
                    if (lvlBad)
                        problems.Add("  ★ " + dictKey + "｜EntryName=" + entryName + " MutClass=" + mutClass
                            + "（官方有文本=" + officialHasText + "）｜LvText(1)=" + Short(lvl, 60) + err);

                    if (n < 8)
                    {
                        n++;
                        samples.Add("   [" + dictKey + "] EntryName=" + (entryName ?? "(null)")
                            + " MutClass=" + (mutClass ?? "(null)")
                            + "｜注册键(名/类)=" + haveByName + "/" + haveByCls
                            + "｜LvText(1)=" + Short(lvl, 60) + err
                            + "｜Desc=" + Short(desc, 34));
                    }
                }

                UnityEngine.Debug.Log("[QudZhsCookbook] 端到端自检：条目 " + probed
                    + "（取不到实例 " + noInst + "），注册键命中 名/类=" + keyHitLvl + "/" + keyHitDesc
                    + "；★ GetLevelText(1) 返回**中文**的 " + zhLvlOk + " 条"
                    + "，官方有文本但我们仍回退英文的 " + enFallback + " 条"
                    + "，含未展开占位符（真 bug）的 " + placeholders + " 条");
                foreach (string s in samples) UnityEngine.Debug.Log("[QudZhsCookbook]" + s);
                for (int i = 0; i < problems.Count && i < 20; i++)
                    UnityEngine.Debug.Log("[QudZhsCookbook]" + problems[i]);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[QudZhsCookbook] 端到端自检异常：" + e);
            }
        }

        private static string Short(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return "(空)";
            s = s.Replace("\n", "\\n");
            return s.Length <= n ? s : s.Substring(0, n) + "…";
        }

        /// <summary>剥掉反射包装异常，取最内层（否则只看到 TargetInvocationException）。</summary>
        private static Exception Root(Exception e)
        {
            while (e != null && e.InnerException != null) e = e.InnerException;
            return e;
        }

        /// <summary>
        /// 自检：先把一条**极简模板**交给官方解析通道，确认这条通道本身可用。
        /// 如果连它都失败，说明问题在 `XmlDataHelper`/`ReadTemplateNode` 的调用方式，
        /// 而不是我们生成的那一大段 XML。
        /// </summary>
        private static void ProbeReadTemplateNode()
        {
            const string id = "__zhs_probe__";
            bool a = PutXml(id,
                "<template ID=\"" + id + "\" Encoding=\"utf-8\"><p>探针</p></template>");
            UnityEngine.Debug.Log("[QudZhsCookbook] 模板通道自检：极简模板 "
                + (a ? "成功" : "失败"));
            if (a) { try { ById.Remove(id); } catch { } }

            // 再把「带 stat 占位」的最小组合试一次
            const string id2 = "__zhs_probe2__";
            bool b = PutXml(id2,
                "<template ID=\"" + id2 + "\" Encoding=\"utf-8\"><p>x<stat Name=\"AV\" />y</p>"
                + "<p><statline Name=\"Cooldown\" /></p>"
                + "<p><switch Name=\"AddsRep\"><case Value=\"\"><p>a</p></case>"
                + "<default><p>b</p></default></switch></p></template>");
            UnityEngine.Debug.Log("[QudZhsCookbook] 模板通道自检：带 stat/switch 的模板 "
                + (b ? "成功" : "失败"));
            if (b) { try { ById.Remove(id2); } catch { } }
        }

        private static string Pick(Dictionary<string, string> map, string nm, string cl, string suffix)
        {
            string v;
            if (!string.IsNullOrEmpty(nm) && map.TryGetValue(nm + suffix, out v)) return v;
            if (!string.IsNullOrEmpty(cl) && map.TryGetValue(cl + suffix, out v)) return v;
            return null;
        }

        /// <summary>
        /// 官方原文表的查找：按 `条目名(中文) → 字典键(英文) → 类名` 依次尝试。
        ///
        /// ★ 为什么需要：1.1.8 起注册用的 `Name` 是**条目当前的 Name**（已被汉化，如 `羽刺`），
        ///   而官方原文表 `Official` 是以**英文** `Quills` 为键建的
        ///   ⇒ 只按中文名查会全部落空、退回空串，面板就只剩数字。
        /// </summary>
        private static string PickOfficial(Dictionary<string, string> map,
                                           string nm, string nmAlt, string cl, string suffix)
        {
            return Pick(map, nm, cl, suffix)
                ?? Pick(map, nmAlt, cl, suffix);
        }

        /// <summary>「条目名(中文) → 字典键(英文)」映射，用于同时注册英文别名键。</summary>
        private static readonly List<KeyValuePair<string, string>> _extraKeyAliases =
            new List<KeyValuePair<string, string>>();

        private static string AliasFor(string entryName)
        {
            for (int i = 0; i < _extraKeyAliases.Count; i++)
                if (string.Equals(_extraKeyAliases[i].Key, entryName, StringComparison.Ordinal))
                    return _extraKeyAliases[i].Value;
            return null;
        }

        // ------------------------------------------------------------------
        // 等级文本：把生成器给出的「段落 + @N 占位」拼成 XML 片段，
        // 官方占位元素（<stat>/<statline>/<switch>）直接以原 XML 文本插入。
        // ------------------------------------------------------------------
        private static int _tplLogged;

        private static System.Xml.XmlNode FindMutation(string cls, string nm)
        {
            var doc = OfficialDoc();
            if (doc == null) return null;
            foreach (System.Xml.XmlNode cat in doc.DocumentElement.ChildNodes)
            {
                if (cat.NodeType != System.Xml.XmlNodeType.Element || cat.Name != "category") continue;
                foreach (System.Xml.XmlNode mut in cat.ChildNodes)
                {
                    if (mut.NodeType != System.Xml.XmlNodeType.Element || mut.Name != "mutation") continue;
                    string c = mut.Attributes?["Class"]?.Value;
                    string n = mut.Attributes?["Name"]?.Value;
                    if (string.Equals(c, cls, StringComparison.Ordinal)
                        || string.Equals(n, nm, StringComparison.Ordinal)) return mut;
                }
            }
            return null;
        }

        /// <summary>官方是否有 `&lt;leveltext&gt;` 节点（即使是空的）。</summary>
        private static bool HasOfficialLevelText(string cls, string nm)
        {
            try
            {
                var mut = FindMutation(cls, nm);
                if (mut == null) return false;
                foreach (System.Xml.XmlNode c in mut.ChildNodes)
                    if (c.NodeType == System.Xml.XmlNodeType.Element && c.Name == "leveltext") return true;
            }
            catch { }
            return false;
        }

        private static List<System.Xml.XmlNode> OfficialPlaces(string cls, string nm)
        {
            var list = new List<System.Xml.XmlNode>();
            try
            {
                var mut = FindMutation(cls, nm);
                if (mut == null) return list;
                foreach (System.Xml.XmlNode child in mut.ChildNodes)
                {
                    if (child.NodeType != System.Xml.XmlNodeType.Element || child.Name != "leveltext") continue;
                    // ★ 必须**递归**收集：官方 leveltext 里 `<stat>` 是嵌在 `<p>` 里的
                    //   （例：Adrenal Control 的 leveltext 直接子节点只有 1 个 statline，
                    //    另外两个 stat 在 <p> 内部）。1.0.3 第一版只看直接子节点，
                    //    于是日志刷 `等级文本 X：有 N 个 @N 找不到官方占位元素`。
                    foreach (System.Xml.XmlNode el in child.SelectNodes(".//*"))
                        if (Array.IndexOf(PlaceTags, el.Name) >= 0)
                            list.Add(el);
                }
            }
            catch { }
            return list;
        }

        private static readonly string[] PlaceTags = { "stat", "switch", "statline", "gametext" };

        private static System.Xml.XmlDocument _officialDoc;
        private static bool _officialTried;

        private static System.Xml.XmlDocument OfficialDoc()
        {
            if (_officialTried) return _officialDoc;
            _officialTried = true;
            try
            {
                string baseDir = System.IO.Path.Combine(UnityEngine.Application.streamingAssetsPath, "Base");
                var merged = new System.Xml.XmlDocument();
                merged.AppendChild(merged.CreateElement("mutations"));
                int files = 0, cats = 0;
                foreach (string file in new[] { "Mutations.xml", "HiddenMutations.xml" })
                {
                    string path = System.IO.Path.Combine(baseDir, file);
                    if (!System.IO.File.Exists(path)) continue;
                    var d = new System.Xml.XmlDocument();
                    d.Load(path);
                    files++;
                    foreach (System.Xml.XmlNode n in d.DocumentElement.ChildNodes)
                    {
                        if (n.NodeType != System.Xml.XmlNodeType.Element || n.Name != "category") continue;
                        merged.DocumentElement.AppendChild(merged.ImportNode(n, true));
                        cats++;
                    }
                }
                _officialDoc = merged;
                UnityEngine.Debug.Log("[QudZhsCookbook] 变异模板：官方 XML 合并完成（"
                    + files + " 个文件 → " + cats + " 个 category，根节点 " + merged.DocumentElement.Name + "）");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[QudZhsCookbook] 变异模板：读官方 XML 失败 " + e.Message);
            }
            return _officialDoc;
        }

        private static string XmlEscape(string s)
        {
            return (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        /// <summary>序列化属性（转义值）。</summary>
        private static void Attrs(System.Text.StringBuilder sb, System.Xml.XmlNode n)
        {
            if (n.Attributes == null) return;

            // ★ 只给 **statline** 注入中文标签。
            //   反射实证（`CtorInfo` / `TypeInfo2`）：
            //     * `XRL.Templates+StatLineNode` **有** `String DisplayName` 字段
            //       （官方自己就用了 20+ 处，如 `Damage` → `DisplayName="Damage increment"`）
            //     * `XRL.Templates+StatNode`      **没有** `DisplayName` 字段
            //       （只有 `Name` / `Filters` / `Unit` / `DefaultUnits`）
            //   ⇒ 往 `stat` 上写 `DisplayName` 会被**静默忽略**，写 `statline` 才生效。
            //
            // ★ 与 1.2.2 的关键区别：**不碰游戏全局对象**（那次改的是
            //   `XRL.Blueprints.StatisticBlueprint` 的公开字段，影响面失控 +
            //   字典重复键让整个 `.cctor` 抛异常）。这里只改我们自己生成的模板 XML，
            //   出问题最多是那几行仍显示英文，不会连累其它界面。
            string statName = null;
            if (n.Name == "statline") statName = n.Attributes["Name"]?.Value;
            string zhLabel = StatlineDisplayZh(statName);

            bool wrote = false;
            foreach (System.Xml.XmlAttribute a in n.Attributes)
            {
                if (string.Equals(a.Name, "DisplayName", StringComparison.Ordinal))
                {
                    sb.Append(" DisplayName=\"").Append(XmlEscape(zhLabel ?? a.Value)).Append('"');
                    wrote = true;
                    continue;
                }
                sb.Append(' ').Append(a.Name).Append("=\"").Append(XmlEscape(a.Value)).Append('"');
            }
            if (!wrote && zhLabel != null)
                sb.Append(" DisplayName=\"").Append(XmlEscape(zhLabel)).Append('"');
        }

        /// <summary>
        /// `statline` 的中文标签。没有登记的返回 null（保持官方渲染）。
        ///
        /// ★ 只对 `statline` 生效 —— 见 `Attrs()` 里的注释。
        /// </summary>
        private static string StatlineDisplayZh(string statName)
        {
            if (string.IsNullOrEmpty(statName)) return null;
            string v;
            return _statlineDisplayZh.TryGetValue(statName, out v) ? v : null;
        }

        private static readonly Dictionary<string, string> _statlineDisplayZh =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "Cooldown", "冷却" },
            { "CooldownNoWillpower", "冷却" },
            { "Duration", "持续" },
            { "BonusDuration", "额外持续" },
            { "Range", "射程" },
            { "Area", "范围" },
            { "Damage", "伤害" },
            { "DamageIncrement", "伤害增量" },
            { "LaserDamageInc", "激光伤害增量" },
            { "LaserPen", "激光穿透" },
            { "ToHitBonus", "命中加成" },
            { "AV", "护甲" },
            { "DV", "闪避" },
            { "SpinDuration", "吐丝持续" },
            { "VisionRadius", "视野半径" },
            { "VisionDuration", "视野持续" },
            { "UncertaintyRadius", "不确定性半径" },
            { "Copies", "分身数" },
            { "Quills", "羽刺" },
            { "QuillPen", "尾刺穿透" },
            { "QuillDamage", "尾刺伤害" },
            { "BonusHP", "生命上限" },
            { "HP", "生命" },
            { "RegenRate", "再生速率" },
            { "LevelBelow5", "5 级以下" },
            { "StingerPen", "尾刺穿透" },
            { "DamageNonStructural", "对非结构物的伤害" },
            { "DamageStructural", "对结构物的伤害" },
            { "DamageRound1", "第 1 回合伤害" },
            { "DamageRound2", "第 2 回合伤害" },
            { "DamageRound3", "第 3 回合伤害" },
        };

                /// <summary>
        /// 把官方占位元素渲染成「只含元素骨架」的 XML 文本，**丢掉全部文本**。
        ///
        /// ★ 为什么必须丢：官方 leveltext 里 `<stat/>` 后面紧跟的就是英文说明，
        ///   而官方 `ParseP` 会把这段 tail 吃进节点 —— 也就是说 `<stat Name="Duration"/>`
        ///   这个节点身上挂着 `". While it's flowing, you gain +"` 这样的尾巴。
        ///   直接 `OuterXml` 会把它粘进中文里，变成
        ///   `持续时间 &lt;stat Name="Duration"/&gt;. While it's flowing...。`
        ///   （1.0.2 第一版实测就是这样）。
        ///   中文语境下我们只需要**数字占位**本身，所以自底向上重建、只留标签与属性。
        /// </summary>
        private static void EmitSkeleton(System.Text.StringBuilder sb, System.Xml.XmlNode n)
        {
            EmitSkeleton(sb, n, null, null);
        }

        /// <summary>
        /// ★★ 2.16.30：`<switch>` 的**分支文字**要补回来。
        ///
        /// 原来只保留元素骨架、**丢掉全部文本** ⇒ 中文模板里写成 `"@N"`（N 指向
        /// 一个 `<switch>`）的段落，分支文字整段消失 ⇒ 面板上该行**空白或只剩裸数字**。
        /// 现改为：`switch` 走 <see cref="EmitSwitch"/> 查 `ZhsMutationLevelText.SwitchByClass`；
        /// **查不到就保持原行为**（丢文本）⇒ 零回归。
        /// </summary>
        private static void EmitSkeleton(System.Text.StringBuilder sb, System.Xml.XmlNode n,
                                         string cls, string nm)
        {
            if (n.NodeType != System.Xml.XmlNodeType.Element) return;
            if (n.Name == "switch" && !string.IsNullOrEmpty(cls) && !string.IsNullOrEmpty(nm))
            {
                EmitSwitch(sb, n, cls, nm);
                return;
            }
            sb.Append('<').Append(n.Name);
            Attrs(sb, n);
            var kids = new List<System.Xml.XmlNode>();
            foreach (System.Xml.XmlNode c in n.ChildNodes)
                if (c.NodeType == System.Xml.XmlNodeType.Element) kids.Add(c);
            if (kids.Count == 0) { sb.Append(" />"); return; }
            sb.Append('>');
            foreach (System.Xml.XmlNode c in kids) EmitSkeleton(sb, c, cls, nm);
            sb.Append("</").Append(n.Name).Append('>');
        }

        /// <summary>
        /// `switch` 的中文占位输出。**方案 A**：把**所有分支**的文字按原顺序都输出
        /// （官方 `switch` 是「按条件选一个分支」，我方无从得知运行时选哪个
        ///  ⇒ 宁可多给信息，也不要在面板上留空白行）。
        ///
        /// 键 = `Name\u0001Class\u0001SwitchName\u0001分支`，分支 = `case:<Value>` / `default`。
        /// 值里的 `@K` = 该分支内第 K 个占位元素（同名分支内计数）。
        /// </summary>
        private static void EmitSwitch(System.Text.StringBuilder sb, System.Xml.XmlNode sw,
                                       string cls, string nm)
        {
            string swn = Attr(sw, "Name") ?? "";
            var branches = new List<System.Xml.XmlNode>();
            foreach (System.Xml.XmlNode c in sw.ChildNodes)
                if (c.NodeType == System.Xml.XmlNodeType.Element
                    && (c.Name == "case" || c.Name == "default")) branches.Add(c);
            if (branches.Count == 0) { EmitSkeleton(sb, sw, null, null); return; }

            bool any = false;
            int emitted = 0;
            foreach (System.Xml.XmlNode br in branches)
            {
                string key = br.Name == "case"
                    ? "case:" + (Attr(br, "Value") ?? "")
                    : "default";
                string zh;
                string full = nm + "\u0001" + cls + "\u0001" + swn + "\u0001" + key;
                if (!ZhsMutationLevelText.SwitchByClass.TryGetValue(full, out zh))
                    continue;                       // ★ 未命中 ⇒ 跳过该分支（零回归）
                int before = sb.Length;
                if (!string.IsNullOrEmpty(zh))
                    sb.Append(EmitBranchText(zh, br, cls, nm));
                else
                {
                    // ★ 中文为空（官方该分支文字由子元素构成，如 `<stat Name="Quills"/>`）
                    //   ⇒ 输出**子元素骨架**，否则这一支什么都不显示。
                    foreach (System.Xml.XmlNode k in br.ChildNodes)
                        if (k.NodeType == System.Xml.XmlNodeType.Element)
                            EmitSkeleton(sb, k, cls, nm);
                }
                if (sb.Length == before) continue;   // ★ 分支确实无内容 ⇒ 不占位
                if (emitted > 0) sb.Append(' ');
                emitted++;
                any = true;
            }
            if (!any) EmitSkeleton(sb, sw, null, null);  // ★ 全未命中 ⇒ 保持原行为
        }

        /// <summary>把分支中文里的 `@K` 换成该分支内第 K 个占位元素（骨架/递归 switch）。</summary>
        private static string EmitBranchText(string zh, System.Xml.XmlNode br, string cls, string nm)
        {
            var places = new List<System.Xml.XmlNode>();
            foreach (System.Xml.XmlNode el in br.SelectNodes(".//*"))
                if (Array.IndexOf(PlaceTags, el.Name) >= 0) places.Add(el);

            var sb = new System.Text.StringBuilder(zh.Length + 32);
            int i = 0;
            while (i < zh.Length)
            {
                int at = zh.IndexOf('@', i);
                if (at < 0) { sb.Append(zh, i, zh.Length - i); break; }
                if (at > i) sb.Append(zh, i, at - i);
                int d = at + 1, num = 0, digits = 0;
                while (d < zh.Length && char.IsDigit(zh[d]))
                { num = num * 10 + (zh[d] - '0'); d++; digits++; }
                if (digits == 0) { sb.Append('@'); i = at + 1; continue; }
                if (num < places.Count) EmitSkeleton(sb, places[num], cls, nm);
                i = d;
            }
            return sb.ToString();
        }

        /// <summary>读元素属性（元素才有属性；非元素返回 null）。</summary>
        private static string Attr(System.Xml.XmlNode n, string name)
        {
            var el = n as System.Xml.XmlElement;
            if (el == null) return null;
            var a = el.GetAttributeNode(name);
            return a == null ? null : a.Value;
        }
        /// <summary>中文等级文本（查 ZhsMutationLevelText）；查不到返回 null。</summary>
        private static string BuildLevelText(string cls, string nm)
        {
            return BuildLevelText(cls, nm, null);
        }

        /// <summary>
        /// 生成等级文本模板 XML。
        ///
        /// ★★ 1.2.1 修掉的关键 bug ★★
        ///   原来模板 ID 是**写死的常量** `"Mutation.LevelText.Preview"`。
        ///   而 `XRL.Templates.ReadTemplateNode` 是用**模板自己的 `ID` 属性**当字典键
        ///   （IL 实证：`TemplateByID[t.ID] = t`），**不是** `PutXml(id, …)` 的第一个参数。
        ///   ⇒ 全部 182 次注册都塞进同一个键、互相覆盖；
        ///     `GetLevelText` 去查 `Mutation.<Name>.LevelText` 永远查不到，只能退回英文模板。
        ///   实测日志证据：`PutXml 成功 86 次；返回后键不在字典 96 次`。
        ///   ⇒ 必须把**真实 ID** 写进 `<template ID="…">`。
        /// </summary>
        private static string BuildLevelText(string cls, string nm, string tplId)
        {
            string[] paras;
            if (string.IsNullOrEmpty(cls)
                || !ZhsMutationLevelText.ByClass.TryGetValue(cls, out paras)
                || paras == null || paras.Length == 0) return null;

            var places = OfficialPlaces(cls, nm);
            var sb = new System.Text.StringBuilder();
            string useId = string.IsNullOrEmpty(tplId) ? "Mutation.LevelText.Preview" : tplId;
            // ★★ 2.16.38：**去掉这里的 XML 声明** —— 声明由 `PutXml` 统一负责
            //   （2.16.34 在这里加过一条，与 `PutXml` 的拼成了**两个声明** ⇒ 解析失败，见 `PutXml` 注释）
            // ★★ 2.16.38：**`Encoding="utf-8"` 必须写在 `<template>` 根元素上** ——
            //   `XRL.BaseXmlReader::Read()` 的编码判定是：
            //       第一个 Element 上 `TryGetAttribute("Encoding")` 成功 ⇒ 用它
            //       失败 ⇒ `StringEncoding = "cp437"` + 刷一条 MODERROR 警告
            //   ⇒ **与 XML 声明毫无关系**（2.16.34 的判断「拼模板没写声明」是错的）
            sb.Append("<template ID=\"").Append(XmlEscape(useId)).Append("\" Encoding=\"utf-8\">");
            int used = 0, skipped = 0;

            foreach (string raw in paras)
            {
                string seg = raw ?? "";
                if (seg.Length > 0 && seg[0] == '\u0001')
                {
                    // 含运行时 =...= 替换：原样交给 gametext
                    sb.Append("<p><gametext>").Append(XmlEscape(seg.Substring(1))).Append("</gametext></p>");
                    continue;
                }

                sb.Append("<p>");
                int bodyStart = sb.Length;
                int pos = 0;
                while (pos < seg.Length)
                {
                    int at = seg.IndexOf('@', pos);
                    if (at < 0)
                    {
                        if (pos < seg.Length) sb.Append(XmlEscape(seg.Substring(pos)));
                        break;
                    }
                    if (at > pos) sb.Append(XmlEscape(seg.Substring(pos, at - pos)));
                    int d = at + 1, num = 0, digits = 0;
                    while (d < seg.Length && char.IsDigit(seg[d])) { num = num * 10 + (seg[d] - '0'); d++; digits++; }
                    if (digits == 0)
                    {
                        sb.Append("&#64;"); pos = at + 1; continue;   // 单独的 @
                    }
                    if (num < places.Count)
                    {
                        sb.Append(OuterXml(places[num], cls, nm));
                        used++;
                    }
                    else
                    {
                        skipped++;
                    }
                    pos = d;
                }
                // 整段空（官方有占位元素但中文没写文字）→ 用一个不换行空格占位，
                // 否则 ParseP 会产出一个空节点导致面板上多一行空白
                if (sb.Length == bodyStart) sb.Append("&#160;");
                sb.Append("</p>");
            }
            sb.Append("</template>");

            if (skipped > 0 && _tplLogged < 5)
            {
                _tplLogged++;
                UnityEngine.Debug.Log("[QudZhsCookbook] 等级文本 " + cls
                    + "：有 " + skipped + " 个 @N 找不到官方占位元素（已跳过）");
            }
            return sb.ToString();
        }

        private static string OuterXml(System.Xml.XmlNode n)
        {
            return OuterXml(n, null, null);
        }

        /// <summary>★ 2.16.30：带 `cls`/`nm` 上下文，供 `switch` 查分支中文表。</summary>
        private static string OuterXml(System.Xml.XmlNode n, string cls, string nm)
        {
            try
            {
                var sb = new System.Text.StringBuilder(64);
                EmitSkeleton(sb, n, cls, nm);
                return sb.ToString();
            }
            catch { return ""; }
        }
    }
}
