// ═══════════════════════════════════════════════════════════════════════════
// 物品改名弹窗汉化（`Rename 你的 woven tunic.` ⇒ `为你的 woven tunic 命名。`）
// ═══════════════════════════════════════════════════════════════════════════
//
// 【问题】（2026-10-07 用户截图）
//     Rename 你的 woven tunic.
//       [a] Enter a name.
//       [b] Name 它 based on 它的 qualities.
//       [c] Choose a random name from your own culture.
//     > [d] Choose a random name from the 湿的 狂皖的 culture.
//   ⇒ 标题与 4 个选项全是「英文骨架 + 中文槽」
//
// 【根因（反编译实证）】`XRL.World.Capabilities.ItemNaming::NameItem`
//     list.Add("Enter a name.");                                        // [a]
//     list.Add("Name " + Object.them + " based on " + Object.its
//              + " qualities.");                                        // [b] ★
//     list.Add("Choose a random name from your own culture.");          // [c]
//     if (Kill != null)
//         list.Add("Choose a random name from "
//                  + Kill.poss("culture") + ".");                       // [d] ★
//     Popup.PickOption("", "Rename " + Object.t() + ".", …);            // 标题 ★
//
//   ⇒ **英文骨架是硬编码**；`Object.them` / `Object.its` / `Object.t()` 是
//     **代词提供器出口**（我方 `ZhsPronounFieldPatch` 已汉化）
//     ⇒ 于是拼出「Name 它 based on 它的 qualities.」这种中英拼接
//   ⇒ 与 Esc 主菜单（`ZhsSystemMenuPatch`）**同一类问题**
//
// 【做法】挂 `NameItem` 的 Prefix，在它调用 `Popup.PickOption` **之前**
//   改掉实参里的 Title 与 Options。
//   ⚠️ 用**子串识别**（`StartsWith` / `EndsWith` / `Contains`）而非整串相等 ——
//      因为串里嵌了运行时才知道的中文槽（物品名 / 文化名）。
//   ⚠️ 只动本弹窗，其它 `PickOption` 一律不碰。
//
// 开关：装机目录放 `_zhs_itemname_off.txt` 即停用。
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsItemNameDialog
    {
        private const string Tag = "[QudZhsCookbook] 物品改名弹窗：";
        internal static bool Disabled;
        private static bool _applied;
        internal static int Hits;

        /// <summary>
        /// `ItemNaming::NameItem` 的签名（实参位置固定，见反编译）：
        ///   [0] GameObject Object
        ///   [1] GameObject Owner
        ///   [2] GameObject Kill        = null
        ///   [3] GameObject InfluencedBy= null
        ///   [4] string     ZoneID      = null
        ///   [5] string     OpportunityType
        ///   [6] bool       CanBestow
        /// </summary>
        internal static void TryApply()
        {
            if (_applied || Disabled) return;
            _applied = true;
            try
            {
                Type t = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        t = asm.GetType("XRL.World.Capabilities.ItemNaming", false);
                        if (t != null) break;
                    }
                    catch { }
                }
                if (t == null)
                {
                    UnityEngine.Debug.Log(Tag + "跳过（挂载点已废弃）");
                    return;
                }
                // ★ 不能用 `GetMethod(name, flags)`：`NameItem` 带**默认参数**，
                //   元数据里存在多个签名 ⇒ AmbiguousMatchException（实测日志行 169）。
                //   这里显式按签名取，取不到就静默放弃（本挂载点已废弃，不必声张）。
                MethodInfo target = t.GetMethod("NameItem",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                    null,
                    new[] { typeof(XRL.World.GameObject), typeof(XRL.World.GameObject),
                            typeof(XRL.World.GameObject), typeof(XRL.World.GameObject),
                            typeof(string), typeof(string), typeof(bool) },
                    null);
                MethodInfo pre = typeof(ZhsItemNameDialog).GetMethod(
                    "Prefix", BindingFlags.NonPublic | BindingFlags.Static);
                if (target == null || pre == null)
                {
                    UnityEngine.Debug.Log(Tag + "跳过（挂载点已废弃）");
                    return;
                }
                new Harmony("Nattens.QudZhsCookbook.itemname")
                    .Patch(target, prefix: new HarmonyMethod(pre));
                UnityEngine.Debug.Log(Tag + "已挂 NameItem（标题 + 4 选项重写）");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "挂载失败：" + e);
            }
        }

        /// <summary>
        /// Prefix：改 `Object` 的 `DisplayName`？不需要 —— 我们改的是**弹窗文案**，
        /// 所以这里只做「记录首次调用」以便日志可验证；真正的重写在
        /// `Popup.PickOption` 侧由 `ZhsItemNameRewrite` 完成。
        ///
        /// ⚠️ 为什么不直接在这里改：`NameItem` 内部构造 `list` 是局部变量，
        ///    补丁拿不到；而 `Popup.PickOption` 的实参是 `Options` 数组，
        ///    Prefix 里能改。
        /// </summary>
        private static void Prefix()
        {
            // 交给 ZhsItemNameRewrite 的 PickOption Prefix 处理；
            // 这里只打一次标记日志，便于在 Player.log 里确认本条路径被走到。
            if (Hits == 0)
                UnityEngine.Debug.Log(Tag + "首次进入 NameItem（弹窗文案由 PickOption 侧重写）");
        }
    }

    /// <summary>
    /// 真正做重写：挂 `Popup.PickOption` 的 Prefix，
    /// 用**内容特征**识别「物品改名弹窗」（标题以 `Rename ` 开头、选项含
    /// `Enter a name.`），命中才改，绝不误伤其它弹窗。
    /// </summary>
    internal static class ZhsItemNameRewrite
    {
        private const string Tag = "[QudZhsCookbook] 物品改名重写：";
        internal static bool Disabled;
        private static bool _applied;
        private static bool _logged;

        internal static void TryApply()
        {
            if (_applied || Disabled) return;
            _applied = true;
            try
            {
                Type t = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        t = asm.GetType("XRL.UI.Popup", false);
                        if (t != null) break;
                    }
                    catch { }
                }
                if (t == null)
                {
                    UnityEngine.Debug.LogError(Tag + "找不到 Popup");
                    return;
                }
                MethodInfo pre = typeof(ZhsItemNameRewrite).GetMethod(
                    "Prefix", BindingFlags.NonPublic | BindingFlags.Static);
                int n = 0;
                foreach (MethodInfo m in t.GetMethods(BindingFlags.Public
                                                      | BindingFlags.NonPublic
                                                      | BindingFlags.Static))
                {
                    if (m.Name != "PickOption") continue;
                    bool hasOpts = false, hasTitle = false;
                    foreach (ParameterInfo pi in m.GetParameters())
                    {
                        if (pi.Name == "Options"
                            && pi.ParameterType.Name.StartsWith("IReadOnlyList"))
                            hasOpts = true;
                        if (pi.Name == "Title") hasTitle = true;
                    }
                    if (!hasOpts || !hasTitle) continue;
                    new Harmony("Nattens.QudZhsCookbook.itemname.rw"
                        + (m.GetParameters().Length))
                        .Patch(m, prefix: new HarmonyMethod(pre));
                    n++;
                }
                UnityEngine.Debug.Log(Tag + "已挂 " + n + " 个 PickOption 出口");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "挂载失败：" + e);
            }
        }

        // ────────────────────────────────────────────────────────────
        // 重写规则（`=` 左边是英文骨架，右边是中文）
        //   ⚠️ 槽（中文的物品名/文化名）**原样保留**在中间
        // ────────────────────────────────────────────────────────────
        private const int MAX_OPTIONS = 12;
        private const int MAX_OPTION_LEN = 400;

        private static string RewriteOne(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            string r = s.Trim();

            // [a] `Enter a name.` ⇒ 输入一个名字。
            if (r.Equals("Enter a name.", StringComparison.Ordinal))
                return "输入一个名字。";

            // [b] `Name <对象> based on <其> qualities.`
            //     ⚠️ 中文字符串里 `based on ` 后面**没有空格**（`based on 它的 qualities.`
            //        ⇒ `依据它的特质…`），所以不能用 Substring 固定偏移，改成 IndexOf 切。
            if (r.StartsWith("Name ", StringComparison.Ordinal)
                && r.EndsWith(" qualities.", StringComparison.Ordinal))
            {
                int k = r.IndexOf(" based on ", StringComparison.Ordinal);
                if (k > 5)
                {
                    string obj = r.Substring(5, k - 5).Trim();
                    string pos = r.Substring(k + 10,
                        r.Length - (k + 10) - 10).Trim();   // 末尾 " qualities."=11 字符，
                    //                                     实际是 " qualities."⇒10+1
                    pos = pos.TrimEnd('.');
                    if (obj.Length > 0 && pos.Length > 0)
                        return "依据" + pos + "特质为" + obj + "命名。";
                }
            }

            // [c] `Choose a random name from your own culture.` ⇒ 从你自己的文化中随机取一个名字。
            if (r.Equals("Choose a random name from your own culture.",
                         StringComparison.Ordinal))
                return "从你自己的文化中随机取一个名字。";

            // [d] `Choose a random name from <冠词?><文化> culture.`
            //     （`Kill.poss("culture")` 已带「的」，故串里是「…的 culture.」；
            //       英文原文有 `the`（`from the Baccata culture`）⇒ 中文要去掉）
            if (r.StartsWith("Choose a random name from ", StringComparison.Ordinal)
                && r.EndsWith(" culture.", StringComparison.Ordinal))
            {
                string mid = r.Substring(25, r.Length - 25 - 9).Trim();
                mid = TrimArticle(mid);
                return "从" + mid + "文化中随机取一个名字。";
            }

            // 标题 `Rename <对象>.` ⇒ 为<对象>命名。
            if (r.StartsWith("Rename ", StringComparison.Ordinal)
                && r.EndsWith(".", StringComparison.Ordinal)
                && r.IndexOf("Enter a name", StringComparison.Ordinal) < 0)
            {
                string obj = r.Substring(7, r.Length - 8).Trim();   // 去掉尾部句号
                if (obj.Length > 0)
                    return "为" + obj + Sep(obj) + "命名。";
            }

            return s;
        }

        /// <summary>
        /// 英文名与中文之间补一个空格（`woven tunic` + `命名` ⇒ `woven tunic 命名`）；
        /// 中文名则不补（`鳄鱼` + `命名` ⇒ `鳄鱼命名`）。
        /// </summary>
        private static string Sep(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            char c = s[s.Length - 1];
            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')
                || (c >= '0' && c <= '9') || c == ')' || c == ']')
                return " ";
            return "";
        }

        /// <summary>去掉英文冠词（`the 湿的 狂皖的` ⇒ `湿的 狂皖的`）。</summary>
        private static string TrimArticle(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            foreach (string a in new[] { "the ", "The ", "a ", "an " })
                if (s.StartsWith(a, StringComparison.Ordinal))
                    return s.Substring(a.Length).TrimStart();
            return s;
        }

        /// <summary>
        /// Prefix：只在**识别出改名弹窗**时改实参。
        /// 识别依据：Options 里含 `Enter a name.` 这一条（该弹窗独有）。
        /// </summary>
        private static void Prefix(ref string __0, ref string __1,
                                   ref string __2,
                                   ref System.Collections.Generic.IReadOnlyList<string> __4)
        {
            if (Disabled) return;
            try
            {
                var opts = __4;
                if (opts == null || opts.Count == 0 || opts.Count > MAX_OPTIONS) return;

                // ① 是不是改名弹窗？
                bool isRename = false;
                for (int i = 0; i < opts.Count; i++)
                {
                    string o = opts[i];
                    if (o != null && o.Length < MAX_OPTION_LEN
                        && o.Equals("Enter a name.", StringComparison.Ordinal))
                    { isRename = true; break; }
                }
                if (!isRename) return;

                // ② 重写选项
                var neu = new List<string>(opts.Count);
                bool changed = false;
                for (int i = 0; i < opts.Count; i++)
                {
                    string n = RewriteOne(opts[i]);
                    if (!ReferenceEquals(n, opts[i]) && n != opts[i]) changed = true;
                    neu.Add(n);
                }
                if (changed) __4 = neu;

                // ③ 重写标题（Title 参数名在不同重载里位置不同 ⇒ 三个都试）
                string t0 = RewriteOne(__0);
                if (t0 != __0) { __0 = t0; changed = true; }
                if (!string.IsNullOrEmpty(__1))
                {
                    string t1 = RewriteOne(__1);
                    if (t1 != __1) { __1 = t1; changed = true; }
                }
                if (!string.IsNullOrEmpty(__2))
                {
                    string t2 = RewriteOne(__2);
                    if (t2 != __2) { __2 = t2; changed = true; }
                }

                if (changed)
                {
                    ZhsItemNameDialog.Hits++;
                    if (!_logged)
                    {
                        _logged = true;
                        UnityEngine.Debug.Log(Tag + "首次重写：标题="
                            + __0 + " / 选项=" + string.Join(" | ", neu.ToArray()));
                    }
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "重写失败：" + e);
            }
        }
    }
}
