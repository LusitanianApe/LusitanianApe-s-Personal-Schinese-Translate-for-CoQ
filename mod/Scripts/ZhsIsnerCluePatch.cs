// 伊什纳家族遗物坐标线索 · Harmony 接管
//
// 接管 `XRL.LoreGenerator.RuinOfHouseIsnerLore(Int32 X, Int32 Y)`：
// 原方法把「列1 + 列2 + 列3」拼成**英文**线索句，再交给
// `MarkovBook.PostprocessLoadedCorpus` 循环 46 次注入马尔科夫语料。
//
// 我们换掉随机文本生成器 ⇒ 那 46 条线索会变成中文风味文本，
// **坐标信息就丢了，玩家永远找不到这把枪** ⇒ 必须在这里换回带坐标的中文线索。
//
// 为什么用 Postfix + 幂等判断（而不是 Prefix 直接返回）：
//   * Postfix 能拿到原返回值，可用来判断"这条是不是 Isner 线索"，避免误伤别的调用；
//   * 保持原方法的副作用（`Stat.ReseedFrom("HouseIsnerLore")` 的确定性）不变；
//   * 幂等：若返回值已含中文，说明我们已换过，直接放行。
//
// 确定性：用 (X, Y) 派生种子 ⇒ 同一局里同一坐标永远得到同一条线索，
//         与官方 `Stat.ReseedFrom("HouseIsnerLore")` 的意图一致。

using System;
using System.Reflection;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    [HarmonyPatch]
    internal static class ZhsIsnerCluePatch
    {
        private static bool _logged;
        private static int _count;

        [HarmonyPrepare]
        public static bool Prepare()
        {
            return ZhsBootstrap.HasMethod(
                AccessTools.TypeByName("XRL.LoreGenerator"), "RuinOfHouseIsnerLore");
        }

        [HarmonyTargetMethod]
        public static MethodBase TargetMethod()
        {
            Type t = AccessTools.TypeByName("XRL.LoreGenerator");
            if (t == null) return null;
            return AccessTools.Method(t, "RuinOfHouseIsnerLore",
                new[] { typeof(int), typeof(int) });
        }

        [HarmonyPostfix]
        public static void Postfix(int X, int Y, ref string __result)
        {
            try
            {
                // 已含中文 ⇒ 我们已经换过（或官方本地化过），幂等放行
                if (!string.IsNullOrEmpty(__result) && Markup.HasCjk(__result)) return;

                if (!_logged)
                {
                    _logged = true;
                    UnityEngine.Debug.Log("[QudZhsCookbook] 伊什纳线索接管已生效；地标："
                        + ZhsIsnerClue.DescribeLandmarks());
                }

                // 确定性：同一坐标的 46 次注入由**同一个流式 RNG** 产出不同线索
                //（对应官方在循环前 `Stat.ReseedFrom("HouseIsnerLore")` 的做法）；
                // 传 null 让它用内部流 —— 每次 new Random(固定种子) 会让 46 条线索全一样，
                // 而一条线索只给一个轴 ⇒ 玩家拼不出完整坐标。
                string zh = ZhsIsnerClue.Make(X, Y, null);
                if (string.IsNullOrEmpty(zh)) return;

                __result = Markup.Rewrap(zh, Unwrap(__result));
                _count++;
                if (_count <= 3)
                {
                    UnityEngine.Debug.Log("[QudZhsCookbook] 伊什纳线索示例（目标 "
                        + X + "," + Y + "）：" + zh);
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[QudZhsCookbook] 伊什纳线索生成异常（保持原文）：" + e);
            }
        }

        /// <summary>取原返回值的颜色/着色器包裹（复用 Markup 的剥离逻辑）。</summary>
        private static System.Collections.Generic.List<string> Unwrap(string original)
        {
            System.Collections.Generic.List<string> wrappers =
                new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(original)) Markup.Unwrap(original, wrappers);
            return wrappers;
        }
    }
}
