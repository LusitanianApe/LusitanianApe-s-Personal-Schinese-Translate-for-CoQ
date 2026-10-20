// ═══════════════════════════════════════════════════════════════════════════
// 所有格（possessive）中文接管 —— 「的」丢失的**根因**修复
// ═══════════════════════════════════════════════════════════════════════════
//
// 【问题】
//   C# 里大量硬编码拼接靠 `Grammar.MakePossessive(X)` 造英语所有格（X + 's）：
//
//     // XRL.World.GameObject::poss / Poss
//     return Grammar.MakePossessive(GetDisplayName(…)) + " " + text;
//     // XRL.World.Parts.Combat::MeleeAttackWithWeaponInternal
//     AddPlayerMessage("You don't penetrate " + Defender.poss("armor")
//                      + " with " + Attacker.its_(Weapon) + ". […]", 'r');
//
//   ⇒ 一旦 X 被译成中文，`MakePossessive("水蛭")` **加不上 's**，只能原样返回
//     ⇒ 拼出「水蛭 life drain」「她的 咬」这类半中半英、且**「的」丢失**的结果。
//
// 【取证（decomp 全量扫描 444 个成员）】
//     Grammar.MakePossessive(…)   153 处调用   ← ★ 根因，一处覆盖全部
//     X.Poss("…")                  38 处
//     X.poss("…")                  29 处
//     X.its_(Y)                    12 处
//   ⇒ 挂 `MakePossessive` **一处**即覆盖上述 232 处的所有格部分。
//
// 【做法】
//   Postfix 改 `ref __0`（静态方法第 1 个参数）。
//   **不判断是不是玩家** —— 按用户判断：游戏不会给玩家名做变格，
//   玩家一律用 you/your 指代，所以走不到这里；直接对所有中文输入生效。
//
//   规则（保守）：
//     · 输入不含汉字            ⇒ 原样放行（英文名继续走 's）
//     · 输入已以「的」结尾      ⇒ 原样放行（幂等，避免「的的」）
//     · 其余含汉字              ⇒ 末尾补「的」
//
//   开关文件 `_zhs_possessive_off.txt` 可停用。
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Text.RegularExpressions;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsPossessive
    {
        public static bool Disabled;
        private static int _calls, _fixed;

        private static readonly Regex Han =
            new Regex(@"[\u3400-\u4dbf\u4e00-\u9fff\uf900-\ufaff]", RegexOptions.Compiled);

        /// <summary>中文所有格：末尾补「的」。非中文原样返回。</summary>
        public static string Apply(string s)
        {
            if (Disabled || string.IsNullOrEmpty(s)) return s;
            _calls++;
            try
            {
                // ★★ 规则 0（2.6.3 新增，**根因级**）：
                //   输入**已经是英语所有格**（以 `'s` 结尾）⇒ 原样返回，**绝不能再补「的」**。
                //
                //   实证（Player.log，`所有格调用 #N` 诊断）：
                //     [QudZhsCookbook] 所有格调用 #1：'s
                //     [QudZhsCookbook] 所有格调用 #4：Ixerpater's
                //   ⇒ 游戏会把**裸 `'s`** 或**已带 `'s` 的名字**传进来
                //     （调用点自己先拼了 `'s`，或者上游已处理过），
                //   而旧规则「非中文 ⇒ 原样」只挡住了**纯英文名**，
                //   挡不住 `'s` —— 它不含汉字但也**不需要**中文所有格标记。
                //   旧行为 ⇒ `MakePossessive("'s")` 返回 **`'s的`** ⇒
                //     存档里 43 处 `X's的`（如 `Ixerpater's的以离开…`）。
                if (s.EndsWith("'s", StringComparison.Ordinal)
                    || s.EndsWith("’s", StringComparison.Ordinal)) return s;

                if (!Han.IsMatch(s)) return s;          // 纯英文 ⇒ 交给游戏的 's
                if (s.EndsWith("的", StringComparison.Ordinal)) return s;  // 幂等

                // ★★ 规则 2（2.6.3 新增）：**颜色标签要在外面**
                //   实证：`{{M|Asathus Boatis ，legendary 发光鱼}} => …}}的`
                //   ⇒ 尾「的」被塞进了 `{{…}}` **里面**。
                //   正确形态：`{{M|…}}的`（「的」在标签外）。
                //   不过 `{{M|…}}` 里**已经含汉字**，所以按下面分支处理：
                //   若结尾是 `}}`，把「的」加在 `}}` **之后**（当前就是这个位置），
                //   真正的问题是标签**内部**的内容 —— 由其它补丁负责；
                //   这里只保证「的」不出现在 `}}` 之前。
                //
                //   ⇒ 实现上无需特殊处理：`s + "的"` 天然加在最外层
                //     （因为 `s` 以 `}}` 结尾）。此处仅加注释说明，避免后人误改。
                _fixed++;
                return s + "的";
            }
            catch { return s; }
        }

        public static string Stats()
        {
            return "所有格：" + _calls + " 次调用 / " + _fixed + " 次补「的」";
        }
    }

    /// <summary>
    /// 挂 `XRL.Language.Grammar.MakePossessive(string)` ——
    /// 英语所有格的唯一核心，153 处调用点由此一处覆盖。
    /// </summary>
    [HarmonyPatch]
    internal static class ZhsPossessivePatch
    {
        private static bool _applied;

        /// <summary>
        /// ★ 2.15.70：改为**显式挂载**。
        ///
        /// 【为什么要改】本类原本靠 `[HarmonyPatch]` 属性自动挂载，
        ///   但 `Player.log` 里**从来没有**它的 `Prepare：MakePossessive=…` 记录
        ///   ⇒ 它从未被 `PatchAll` 过 ⇒ **从未生效**。
        ///   与本项目其它补丁统一改走显式 `TryApply()`，这样「挂上了没有」有日志可查。
        /// </summary>
        internal static void TryApply()
        {
            if (_applied) return;
            _applied = true;
            try
            {
                var m = TargetMethod();
                if (m == null)
                {
                    UnityEngine.Debug.LogError("[QudZhsCookbook] 所有格：找不到 Grammar.MakePossessive(string)");
                    return;
                }
                var post = AccessTools.Method(typeof(ZhsPossessivePatch), "Postfix");
                if (post == null) return;
                new Harmony("Nattens.QudZhsCookbook.possessive")
                    .Patch(m, postfix: new HarmonyMethod(post));
                UnityEngine.Debug.Log("[QudZhsCookbook] 所有格：已挂 "
                    + m.DeclaringType.FullName + "::" + m.Name
                    + "（中文补「的」· 英文名保留 ='s=）");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[QudZhsCookbook] 所有格挂载失败：" + e);
            }
        }

        private static System.Reflection.MethodBase TargetMethod()
        {
            var m = AccessTools.Method(typeof(XRL.Language.Grammar),
                                       "MakePossessive", new[] { typeof(string) });
            return m;
        }

        [HarmonyPrepare]
        private static bool Prepare()
        {
            try
            {
                var m = TargetMethod();
                UnityEngine.Debug.Log("[QudZhsCookbook] 所有格补丁 Prepare：MakePossessive="
                    + (m == null ? "❌ null"
                                 : "✅ " + m.DeclaringType.FullName + "::" + m.Name
                                   + " 参数 " + m.GetParameters().Length
                                   + " 静态 " + (m.IsStatic ? "是" : "否")));
                return m != null;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.Log("[QudZhsCookbook] 所有格补丁 Prepare 异常：" + e);
                return false;
            }
        }

        [HarmonyPostfix]
        private static void Postfix(ref string __result)
        {
            // ★ 诊断（前 25 次调用无条件记录）—— 用来判断补丁**到底有没有被调到**。
            //   实证需求：某次会话里「所有格补『的』」0 条，但屏幕上明明出现
            //   「他的 钢制长剑」⇒ 无法区分「没被调用」与「调用了但都已是中文」。
            try
            {
                int c = System.Threading.Interlocked.Increment(ref _seen);
                if (c <= 25)
                {
                    UnityEngine.Debug.Log("[QudZhsCookbook] 所有格调用 #" + c
                        + "：" + (__result ?? "(null)"));
                }
            }
            catch { }

            string before = __result;
            __result = ZhsPossessive.Apply(__result);
            if (!ReferenceEquals(before, __result) && before != __result)
            {
                int n = System.Threading.Interlocked.Increment(ref _logged);
                if (n <= 8)
                {
                    UnityEngine.Debug.Log("[QudZhsCookbook] 所有格补「的」：" + before
                        + " => " + __result);
                }
            }
        }

        private static int _logged;
        private static int _seen;
    }
}
