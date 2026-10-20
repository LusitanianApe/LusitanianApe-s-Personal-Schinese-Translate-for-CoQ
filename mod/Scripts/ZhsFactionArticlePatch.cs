// 阵营名前的英文冠词 "the" 清除（兜底）。
//
// 主要手段在数据层：Factions.xml 里把 `FormatWithArticle` 设为 false，
// 让 `Faction.GetFormattedName` 用的模板
//     "=withArticle.if:the ==displayName="
// 不再输出 "the"（见 Factions.xml 生成脚本 gen_factions.py）。
//
// ★★ 重大教训（这次踩的坑）★★
//   `Faction.GetFormattedName` 有**两个重载**：
//       string GetFormattedName()           ← 实例、无参
//       static string GetFormattedName(string)
//   用 `[HarmonyPatch(typeof(Faction))] + [HarmonyPatch("GetFormattedName")]`
//   会让 Harmony 的 AccessTools.DeclaredMethod 抛
//       AmbiguousMatchException: Ambiguous match found
//   而 **Harmony.PatchAll 是顺序执行、遇异常即中断** ——
//   这一个异常导致排在后面的所有补丁类（包括变异汉化）全都没挂上。
//   ⇒ 所以：凡是可能重载的方法，一律用 <see cref="TargetMethod"/> 显式选签名，
//     并且用 Prepare() 做守卫，宁可不挂也不能抛。
//
// 中文没有冠词，所以清掉开头的 "the " 总是安全的。

using System;
using System.Reflection;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    [HarmonyPatch]
    internal static class ZhsFactionNamePatch
    {
        private static int _hits;

        /// <summary>显式选"实例、无参"的重载，避免 AmbiguousMatch。</summary>
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(XRL.World.Faction), "GetFormattedName", Type.EmptyTypes, null);
        }

        [HarmonyPrepare]
        private static bool Prepare()
        {
            try { return TargetMethod() != null; }
            catch { return false; }   // 宁可不挂，也绝不抛异常中断 PatchAll
        }

        [HarmonyPostfix]
        private static void Postfix(ref string __result)
        {
            try
            {
                if (string.IsNullOrEmpty(__result)) return;
                if (__result.Length > 4 && __result.StartsWith("the ", StringComparison.OrdinalIgnoreCase))
                {
                    string old = __result;
                    __result = __result.Substring(4);
                    if (_hits < 5)
                        UnityEngine.Debug.Log("[QudZhsCookbook] 去除阵营冠词：'" + old + "' -> '" + __result + "'");
                    _hits++;
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[QudZhsCookbook] 阵营冠词清除失败：" + e.Message);
            }
        }
    }

    /// <summary>
    /// 注意：**不要**给 FactionReplacers::FormattedNameColor 打补丁。
    ///
    /// 它的签名是 `static System.Void FormattedNameColor(...)`（返回 void，
    /// 结果写进参数里的 StringBuilder），而我们写成 `ref string __result` 时，
    /// Harmony 在 `MethodCreator.AddPostfixes -> EmitCallParameter` 阶段会抛
    ///
    ///     HarmonyException: Patching exception in method static System.Void
    ///         XRL.World.Text.Delegates.FactionReplacers::FormattedNameColor
    ///
    /// 而 **PatchAll 顺序执行、遇异常即中断** —— 这次又把这个文件之后的补丁
    /// （包括变异汉化）全部连累掉了。
    ///
    /// 况且数据层（Factions.xml 的 FormatWithArticle=false）已经彻底解决了 "the"
    /// （用户已验证生效），这个补丁本来就没有必要。
    /// </summary>

}
