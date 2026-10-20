// 变异汉化 —— 在 MutationFactory.Init **完成后**统一改名与替换说明。
//
// ★ 为什么不挂 HandleMutationNode / HandleDescriptionNode（实测两轮都失败）：
//   1. `HandleMutationNode` 的 Postfix 里 `currentMutation` 仍是 **null**
//      —— 原方法把赋值放在了更靠后的位置（同方法内还调用了别的流程），
//      Postfix 拿不到刚解析的条目。
//   2. `HandleDescriptionNode` 的 Prefix 用 `ref XmlNode node` 会让 Harmony
//      在 `EmitCallParameter` 阶段**直接抛异常**（Harmony 不支持按引用改原方法参数），
//      日志表现为 MODERROR "Exception applying harmony patches"，
//      而且这个异常会连累同一批 PatchAll 的其它补丁。
//
// ★ 现方案：Postfix `MutationFactory.Init`。
//   Init 结束时 `_MutationsByClass` 已装好全部条目、`Templates.TemplateByID`
//   也已注册好说明模板 —— 此时统一处理，时机最稳、不依赖任何中间状态。
//
// 变异名称：改 `IPartEntry.Name`（`get_DisplayName` 直接返回它），
//           `Class` 保持不变（代码用它判定变异类型，绝不能动）。
// 变异说明：模板 ID 是 "Mutation." + 原英文名 + ".Description"，
//           用官方节点结构 + 我们的中文重建模板节点。
//
// 安全设计：全部 try/catch；开关文件 _zhs_mutation_off.txt 可停用。

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Xml;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsMutationUtil
    {
        public static readonly string[] PlaceTags = { "stat", "switch", "statline", "gametext" };

        /// <summary>
        /// 读对象上的字段（**会遍历基类链**）。
        ///
        /// ★★ 1.1.10 修掉的关键 bug ★★
        ///   原实现是 `AccessTools.Field(o.GetType(), name)` —— 而 Harmony 的
        ///   `AccessTools.Field` **只在指定类型自己声明的字段里找，不遍历基类链**。
        ///   变异条目的 `Name` 声明在基类 `XRL.IPartEntry` 上
        ///   （实测：`XRL.IPartEntry` 不是接口而是**抽象基类**，`Name` 是它的 public 字段；
        ///     `typeof(XRL.MutationEntry).GetField("Name")` → `XRL.IPartEntry.Name`），
        ///   所以这一个 bug 让**所有** `FieldStr(entry, "Name")` 都返回 null：
        ///     · `ZhsMutationTemplates` 拿不到中文名 ⇒ 用英文键注册模板
        ///       ⇒ 读侧 `GetMutationEntry().Name`（中文）查不到 ⇒ 面板显示英文；
        ///     · 别名键（`nmAlt`）也为 null ⇒ 连英文别名都没注册。
        ///   实测日志证据：`注册名样本 [Quills] → FieldStr(Name)=(null)`。
        /// </summary>
        public static string FieldStr(object o, string name)
        {
            if (o == null) return null;
            try
            {
                for (Type t = o.GetType(); t != null; t = t.BaseType)
                {
                    var f = AccessTools.Field(t, name);
                    if (f != null) return f.GetValue(o) as string;
                }
            }
            catch { }
            return null;
        }

        public static void StripText(XmlNode n)
        {
            foreach (XmlNode c in new List<XmlNode>(ToList(n.ChildNodes)))
                StripText(c);
            if (n.NodeType == XmlNodeType.Text || n.NodeType == XmlNodeType.Whitespace
                || n.NodeType == XmlNodeType.SignificantWhitespace)
                n.ParentNode?.RemoveChild(n);
        }

        private static IEnumerable<XmlNode> ToList(XmlNodeList l)
        {
            foreach (XmlNode n in l) yield return n;
        }

        /// <summary>用「段落数组 + «N» 记号」构造 &lt;description&gt; 节点。</summary>
        public static XmlElement Build(XmlDocument doc, string[] paras, List<XmlNode> places)
        {
            XmlElement repl = doc.CreateElement("description");
            foreach (string text in paras)
            {
                // 以 \u0001 开头的段落：含 =xxx= 运行时模板，必须套 <gametext> 原样保留
                if (text.Length > 0 && text[0] == '\u0001')
                {
                    XmlElement pg = doc.CreateElement("p");
                    XmlElement gt = doc.CreateElement("gametext");
                    gt.InnerText = text.Substring(1);
                    pg.AppendChild(gt);
                    repl.AppendChild(pg);
                    continue;
                }

                XmlElement p = doc.CreateElement("p");
                XmlNode cur = null;
                int pos = 0;
                while (true)
                {
                    int lt = text.IndexOf('\u00ab', pos);
                    if (lt < 0) break;
                    int gt2 = text.IndexOf('\u00bb', lt + 1);
                    if (gt2 < 0) break;

                    Append(doc, p, ref cur, text.Substring(pos, lt - pos));

                    int idx;
                    if (int.TryParse(text.Substring(lt + 1, gt2 - lt - 1), out idx) && idx < places.Count)
                    {
                        XmlNode imp = doc.ImportNode(places[idx], true);
                        StripText(imp);
                        p.AppendChild(imp);
                        cur = imp;
                    }
                    else
                    {
                        XmlNode txt = doc.CreateTextNode(text.Substring(lt, gt2 - lt + 1));
                        p.AppendChild(txt);
                        cur = txt;
                    }
                    pos = gt2 + 1;
                }
                Append(doc, p, ref cur, text.Substring(pos));
                if (p.ChildNodes.Count == 0) p.AppendChild(doc.CreateTextNode(""));
                repl.AppendChild(p);
            }
            return repl;
        }

        private static void Append(XmlDocument doc, XmlElement p, ref XmlNode cur, string seg)
        {
            if (string.IsNullOrEmpty(seg)) return;
            XmlNode t = doc.CreateTextNode(seg);
            if (cur == null)
            {
                if (p.ChildNodes.Count == 0) p.AppendChild(t);
                else p.LastChild.AppendChild(t);
            }
            else p.AppendChild(t);
        }
    }

    /// <summary>
    /// 变异说明的落点：官方刚用 `LoadTemplateFromExternal("Mutation.&lt;Class&gt;.Description", node)`
    /// 注册完英文说明后，我们**立刻覆盖注册**中文版。
    ///
    /// 为什么挂这里（而不是 MutationFactory.Init）：
    ///   实测 `Init` 执行完毕时 `_MutationsByName` / `_MutationsByClass` **还是空的**
    ///   （变异是惰性加载的，Init 只装解析器），所以那条路取到 0 个条目。
    ///   而本方法在**每个变异解析到说明时**都会被调用，时机精确、且
    ///   `currentMutation` 在此刻必定是当前变异。
    /// </summary>
    [HarmonyPatch]
    internal static class ZhsMutationDescHook
    {
        private static int _hits;
        private static int _miss;

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(XRL.Templates), "LoadTemplateFromExternal");
        }

        [HarmonyPrepare]
        private static bool Prepare()
        {
            try { return TargetMethod() != null; } catch { return false; }
        }

        /// <summary>
        /// 用 `string id` 定位变量（**绝不能用 ref XmlNode**）。
        ///
        /// ★★ 第三次踩同一个坑，必须记牢 ★★
        ///   Harmony 的 Prefix **不能按引用改原方法参数** ——
        ///   写 `ref XmlNode xml` 会让 MethodCreator.EmitCallParameter 在
        ///   **挂载阶段**抛 HarmonyException，而 PatchAll 遇异常即中断，
        ///   导致本补丁类之后的补丁全部失效（"变异没汉化"就是这么来的）。
        ///   正确做法：只用值参数（或用 `__0`/`__1` 等按序号取值），
        ///   通过 **就地修改对象内容** 达到效果 —— 命令式地改 childNodes 即可，
        ///   原方法随后读到的就是新内容。
        /// </summary>
        [HarmonyPrefix]
        private static void Prefix(string __0, object __1)
        {
            try
            {
                string id = __0;
                XmlNode xml = __1 as XmlNode;
                // ★ 重要：`Templates.LoadTemplateFromExternal` 的第二个参数是
                //   `XmlDataHelper`（`XmlTextReader` 的子类），**不是** `XmlNode`，
                //   所以这里的 `as XmlNode` 恒为 null、本 Prefix 恒不生效 ——
                //   保留它只是为了给未来版本留个观测点，别指望它做替换。
                if (xml == null) return;
                if (ZhsMutationZh.Disabled || string.IsNullOrEmpty(id)) return;
                const string P = "Mutation.";
                const string S = ".Description";
                if (!id.StartsWith(P, StringComparison.Ordinal) || !id.EndsWith(S, StringComparison.Ordinal))
                    return;
                string cls = id.Substring(P.Length, id.Length - P.Length - S.Length);

                string[] paras;
                if (!ZhsMutationZh.Desc.TryGetValue(cls, out paras) || paras == null || paras.Length == 0)
                {
                    _miss++;
                    return;
                }

                List<XmlNode> places = new List<XmlNode>();
                foreach (XmlNode n in xml.ChildNodes)
                    if (n.NodeType == XmlNodeType.Element
                        && Array.IndexOf(ZhsMutationUtil.PlaceTags, n.Name) >= 0)
                        places.Add(n);

                XmlDocument doc = xml.OwnerDocument ?? new XmlDocument();
                XmlElement repl = ZhsMutationUtil.Build(doc, paras, places);

                // ★ 只能这样"就地替换内容"：Harmony 不支持按引用改原方法参数
                //   （用 ref XmlNode 会让 EmitCallParameter 抛异常、中断整个 PatchAll）。
                //   把原描述节点的子节点全部换成中文的即可，方法体随后读到的是新内容。
                xml.RemoveAll();
                foreach (XmlNode c in new List<XmlNode>(Children(repl)))
                    xml.AppendChild(xml.OwnerDocument != null ? xml.OwnerDocument.ImportNode(c, true) : c);

                _hits++;
                if (_hits <= 5)
                    UnityEngine.Debug.Log("[QudZhsCookbook] 变异说明已替换：" + cls
                        + "（" + paras.Length + " 段、占位元素 " + places.Count + " 个）");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[QudZhsCookbook] 变异说明替换失败：" + e.Message);
            }
        }

        private static IEnumerable<XmlNode> Children(XmlNode n)
        {
            foreach (XmlNode c in n.ChildNodes) yield return c;
        }
    }

    [HarmonyPatch(typeof(XRL.MutationFactory))]
    [HarmonyPatch("Init")]
    internal static class ZhsMutationInitPatch
    {
        private static bool _done;
        private static Dictionary<string, XmlElement> _officialDesc;

        /// <summary>
        /// 守卫：若 Init 有重载或找不到，宁可不挂也**绝不抛异常**。
        /// （Harmony.PatchAll 顺序执行、遇异常即中断，一个补丁抛错会连累后面全部补丁 ——
        ///   `Faction.GetFormattedName` 的歧义就是这么把变异汉化一起弄没的。）
        /// </summary>
        [HarmonyPrepare]
        private static bool Prepare()
        {
            try { return AccessTools.Method(typeof(XRL.MutationFactory), "Init", Type.EmptyTypes, null) != null; }
            catch { return false; }
        }

        /// <summary>
        /// 从**官方** Mutations.xml 读出每个变异的 &lt;description&gt; 节点。
        /// 为什么需要：Init 结束后官方的解析节点已经用完，而我们重建中文说明时
        /// 必须把官方的占位元素（stat/switch/gametext）**原样搬过来**，
        /// 否则运行时数值不会显示。
        /// 文件路径用 UnityEngine.Application.streamingAssetsPath 拼，随安装位置自适应。
        /// </summary>
        private static XmlElement FindOfficialDescription(object entry, string englishName)
        {
            try
            {
                if (_officialDesc == null)
                {
                    _officialDesc = new Dictionary<string, XmlElement>(StringComparer.Ordinal);
                    string baseDir = System.IO.Path.Combine(
                        UnityEngine.Application.streamingAssetsPath, "Base");
                    string path = System.IO.Path.Combine(baseDir, "Mutations.xml");
                    if (!System.IO.File.Exists(path))
                    {
                        UnityEngine.Debug.LogError("[QudZhsCookbook] 变异说明：找不到官方 Mutations.xml：" + path);
                    }
                    else
                    {
                        XmlDocument doc = new XmlDocument();
                        doc.Load(path);
                        foreach (XmlNode n in doc.DocumentElement.ChildNodes)
                        {
                            if (n.NodeType != XmlNodeType.Element || n.Name != "category") continue;
                            foreach (XmlNode m in n.ChildNodes)
                            {
                                if (m.NodeType != XmlNodeType.Element || m.Name != "mutation") continue;
                                string cls = null, nm = null;
                                if (m.Attributes != null)
                                {
                                    cls = m.Attributes["Class"]?.Value;
                                    nm = m.Attributes["Name"]?.Value;
                                }
                                XmlNode d = null;
                                foreach (XmlNode c in m.ChildNodes)
                                    if (c.NodeType == XmlNodeType.Element && c.Name == "description") { d = c; break; }
                                if (d == null) continue;
                                foreach (string key in new[] { cls, nm })
                                    if (!string.IsNullOrEmpty(key) && !_officialDesc.ContainsKey(key))
                                        _officialDesc[key] = (XmlElement)d;
                            }
                        }
                        UnityEngine.Debug.Log("[QudZhsCookbook] 变异说明：官方描述已载入 "
                            + _officialDesc.Count + " 条");
                    }
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[QudZhsCookbook] 读取官方 Mutations.xml 失败：" + e.Message);
            }

            if (_officialDesc == null) return null;
            XmlElement r;
            if (_officialDesc.TryGetValue(englishName, out r)) return r;
            string cls2 = ZhsMutationUtil.FieldStr(entry, "Class");
            if (!string.IsNullOrEmpty(cls2) && _officialDesc.TryGetValue(cls2, out r)) return r;
            return null;
        }

        [HarmonyPostfix]
        private static void Postfix()
        {
            try
            {
                if (ZhsMutationZh.Disabled || _done) return;
                _done = true;

                // ★★ 关键：先触发惰性加载 ★★
                // 实测 `Init` 执行完时 `_MutationsByName` / `_MutationsByClass`
                // **还是空的** —— Init 只装解析器，真正的解析发生在首次 CheckInit。
                // 不调这一步就会得到"取得条目 0 个"（改名 0 条）。
                try
                {
                    MethodInfo ci = AccessTools.Method(typeof(XRL.MutationFactory), "CheckInit");
                    if (ci != null) ci.Invoke(null, null);
                }
                catch (Exception cex)
                {
                    UnityEngine.Debug.LogError("[QudZhsCookbook] CheckInit 失败："
                        + (cex.InnerException?.Message ?? cex.Message));
                }

                int renamed = 0, desc = 0;

                // ---- 0) 取全部变异条目 ----
                // 用官方访问器，避免自己拼 _MutationsByClass 的结构（实测那样会得到
                // 132 个条目 —— 里面混了变体/隐藏条目，导致改名只命中一部分）。
                var list = new List<object>();
                try
                {
                    // 把候选重载都列出来（诊断用）
                    var cms = new List<string>();
                    foreach (MethodInfo mi in typeof(XRL.MutationFactory).GetMethods(
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
                        if (mi.Name == "GetMutationEntries")
                            cms.Add((mi.IsStatic ? "static " : "") + mi.ReturnType.Name + "("
                                + string.Join(",", Array.ConvertAll(mi.GetParameters(), p => p.ParameterType.Name)) + ")");
                    UnityEngine.Debug.Log("[QudZhsCookbook] GetMutationEntries 重载: "
                        + (cms.Count == 0 ? "(无)" : string.Join(" | ", cms.ToArray())));

                    foreach (MethodInfo gm in typeof(XRL.MutationFactory).GetMethods(
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                    {
                        if (gm.Name != "GetMutationEntries" || gm.GetParameters().Length != 0) continue;
                        object res;
                        try { res = gm.Invoke(null, null); }
                        catch (Exception ex) { UnityEngine.Debug.LogError("[QudZhsCookbook] 调用失败：" + ex.InnerException?.Message ?? ex.Message); continue; }
                        var en = res as IEnumerable;
                        if (en == null) { UnityEngine.Debug.Log("[QudZhsCookbook] 返回不是 IEnumerable：" + (res?.GetType().FullName ?? "null")); continue; }
                        foreach (object o in en) if (o != null) list.Add(o);
                        UnityEngine.Debug.Log("[QudZhsCookbook] " + gm.ReturnType.Name + " 取得 " + list.Count + " 个");
                        if (list.Count > 0) break;
                    }

                    // 兜底：直接读 _MutationsByName（Name -> 条目）
                    if (list.Count == 0)
                    {
                        var byName = AccessTools.Field(typeof(XRL.MutationFactory), "_MutationsByName")
                            ?.GetValue(null) as IDictionary;
                        if (byName != null)
                        {
                            foreach (DictionaryEntry e in byName)
                                if (e.Value != null) list.Add(e.Value);
                            UnityEngine.Debug.Log("[QudZhsCookbook] 兜底 _MutationsByName 取得 " + list.Count + " 个");
                        }
                    }
                    // 再兜底：_MutationsByClass（Class -> List<entry>）
                    if (list.Count == 0)
                    {
                        var byClass = AccessTools.Field(typeof(XRL.MutationFactory), "_MutationsByClass")
                            ?.GetValue(null) as IDictionary;
                        if (byClass != null)
                        {
                            foreach (DictionaryEntry e in byClass)
                                foreach (object o in (e.Value as IEnumerable) ?? new object[] { e.Value })
                                    if (o != null) list.Add(o);
                            UnityEngine.Debug.Log("[QudZhsCookbook] 兜底 _MutationsByClass 取得 " + list.Count + " 个");
                        }
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError("[QudZhsCookbook] 取变异条目失败：" + ex);
                }
                UnityEngine.Debug.Log("[QudZhsCookbook] 变异汉化：取得条目 " + list.Count + " 个");

                // 先快照「条目 -> 原名」，因为改名后 Name 就查不到字典了
                var snapshot = new List<KeyValuePair<object, string>>();
                foreach (object entry in list)
                {
                    string nm = ZhsMutationUtil.FieldStr(entry, "Name");
                    if (!string.IsNullOrEmpty(nm)) snapshot.Add(new KeyValuePair<object, string>(entry, nm));
                }

                // ---- 1) 改名 ----
                // ★ 用**条目当前的 Name**（即官方 XML 的 Name）查表。
                //   不能用 Class 查 —— 官方 94 处 Name != Class
                //   （如 <mutation Name="Night Vision" Class="DarkVision">），
                //   第一版就是这么漏掉绝大多数变异的。
                foreach (var kv in snapshot)
                {
                    string zh;
                    if (!ZhsMutationZh.NameByOrig.TryGetValue(kv.Value, out zh))
                    {
                        string cls0 = ZhsMutationUtil.FieldStr(kv.Key, "Class");
                        if (string.IsNullOrEmpty(cls0)
                            || !ZhsMutationZh.NameByClass.TryGetValue(cls0, out zh))
                            continue;
                    }
                    if (string.IsNullOrEmpty(zh)) continue;
                    // ★★ 2026-10-05 定案：**不再修改 `entry.Name`**。
                    //   中文名改由 XML 的 `DisplayName` 属性提供
                    //   （官方 `MutationEntry.HandleXMLNode` →
                    //     `BaseDisplayName = ParseAttribute("DisplayName", …)`，
                    //     而 `GetDisplayName()` = `BaseDisplayName ?? Name`）。
                    //
                    //   改 `Name` 曾引发三类连锁故障：
                    //     ① `BaseMutation.GetDescription()` 查
                    //        `Mutation.<Name>.Description` ⇒ 中文名对不上 XML 的英文键；
                    //     ② `QudMutationModuleDataRow.Entry => getMutationEntryByName(...)`
                    //        按名查表落空 ⇒ `UpdateNodesFromData` 的 Find 返回 null
                    //        ⇒ NullReferenceException（callings 现场那个崩溃）；
                    //     ③ `_MutationsByName` 被 `XRL.GameText::TryParseArgument` 使用，
                    //        改名使模板引用变异名失配。
                    //
                    //   保留计数用于日志（便于确认 XML 侧已覆盖多少条）。
                    renamed++;
                }

                // ---- 2) 说明模板 ----
                // ★★ 结论（0.99 → 1.0.1 的关键发现，务必照抄）★★
                //   1. 官方 **没有** 读取 <description> 的代码路径：
                //      `MutationFactory.HandleDescriptionNode` 从未被调用 —— 它虽然登记在
                //      `MutationFactory.MutationSubnodes`（键 "description"）里，但
                //      `IPartEntry.HandleXMLNode` 根本不调用 `MutationSubnodes`，
                //      `HandleCategoryNode` 也只传 `{ "mutation" }`。全程序集里
                //      "HandleDescriptionNode" 只出现 1 次（就是它自己的定义）。
                //      所以"解析完再覆盖模板"这条路**根本不存在**。
                //   2. `Templates.LoadTemplateFromExternal(String, XmlDataHelper)` 的第二个参数
                //      是 `XmlDataHelper`（`XmlTextReader` 的子类），**不是** XmlElement ——
                //      1.0.0 里 `loadExt.Invoke(null, new object[]{ id, repl })` 全部抛
                //      ArgumentException（Player.log 里 80 行"注册说明模板失败"就是这么来的）。
                //   3. 我们自己的补丁挂载时机**晚于** `MutationFactory.Init`
                //      （实证：改名日志出现在 "Loading Mutations.xml" 之后），
                //      所以也不可能在解析期抢先注册。
                //
                //   ⇒ 正确做法：**由我们在 Init 之后直接往 Templates.TemplateByID 写模板**
                //     （见 ZhsMutationTemplates.cs —— 那里有完整的 IL 证据链）。
                //     数据文件 Mutations.xml 仍然保留：它负责把官方的
                //     description/leveltext 解析进 MutationEntry，并顺势让
                //     category DisplayName 汉化。
                if (!ZhsBootstrap.MountOff("ZhsMutationTemplates")) ZhsMutationTemplates.Install();

                int tplTotal = 0, tplZh = 0, dup = 0;
                var dupNames = new List<string>();
                try
                {
                    var tpl = AccessTools.Field(typeof(XRL.Templates), "TemplateByID")?.GetValue(null) as IDictionary;
                    if (tpl != null)
                    {
                        tplTotal = tpl.Count;
                        foreach (DictionaryEntry e in tpl)
                        {
                            string id = e.Key as string;
                            if (id == null || !id.StartsWith("Mutation.", StringComparison.Ordinal)
                                || !id.EndsWith(".Description", StringComparison.Ordinal)) continue;
                            tplZh++;
                        }
                    }
                    // 重复条目检测：这里只能看到「官方可见」的条目。
                    // ★ 真正的重灾区是 `_MutationsByClass`：官方的 `Init` **不会**按名去重，
                    //   若我们的 Mutations.xml 排在 base 之前（LoadPriority 没设或 <= 0），
                    //   base 会把同名变异追加进同一个 Entries 列表 ——
                    //   `_MutationsByName` 看不到重复，`_MutationsByClass` 才看得到。
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var kv in snapshot)
                        if (!seen.Add(kv.Value + "|" + (ZhsMutationUtil.FieldStr(kv.Key, "Class") ?? ""))) dup++;

                    var byClass = AccessTools.Field(typeof(XRL.MutationFactory), "_MutationsByClass")?.GetValue(null) as IDictionary;
                    if (byClass != null)
                    {
                        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
                        foreach (DictionaryEntry e in byClass)
                        {
                            var lst = e.Value as IEnumerable;
                            if (lst == null) continue;
                            foreach (object o in lst)
                            {
                                string nm = ZhsMutationUtil.FieldStr(o, "Name");
                                if (nm == null) continue;
                                int c;
                                counts.TryGetValue(nm, out c);
                                counts[nm] = c + 1;
                            }
                        }
                        foreach (var kv in counts)
                            if (kv.Value > 1)
                            {
                                dup++;
                                if (dupNames.Count < 8)
                                    dupNames.Add(kv.Key + "×" + kv.Value + "[" + kv.Key + "]");
                            }
                    }
                }
                catch { }

                UnityEngine.Debug.Log("[QudZhsCookbook] 变异汉化完成：改名 " + renamed
                    + " 条；说明模板 Mutation.*.Description " + tplZh + " 个 / 模板总数 " + tplTotal
                    + "；重复条目 " + dup + (dup > 0 ? "（按 Class 计数，变体共用 Class 属正常）-> " + string.Join(",", dupNames.ToArray()) : "")
                    + "；已载入说明段 " + desc + " 组");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[QudZhsCookbook] 变异汉化失败：" + e);
            }
        }

        private static IEnumerable<XmlNode> Children(XmlNode n)
        {
            foreach (XmlNode c in n.ChildNodes) yield return c;
        }
    }
}
