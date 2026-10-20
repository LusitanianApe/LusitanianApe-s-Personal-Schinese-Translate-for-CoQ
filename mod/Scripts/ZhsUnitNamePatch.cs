// ═══════════════════════════════════════════════════════════════════════════
//  ZhsUnitNamePatch —— 巨像构筑界面「变异名 + at level N」的出口补丁
//
//  【为什么需要它】（2026-10-16 夜 · 探针实证，见 `_tools\文本生命周期-全景图.md` §7.9）
//
//    出口：`XRL.World.Units.GameObjectMutationUnit::GetDescription`
//              if (Name == null) Name = Entry?.GetDisplayName();   // ★ 只有 null 才本地化
//              if (!ShouldShowLevel) return Name;
//              return Name + " at level " + Level;                 // 纯 Concat
//
//    产地：`XRL.World.Quests.GolemQuest.GolemHamsaSelection` 里
//              yield return new GameObjectMutationUnit { Name = "Electrical Generation", Level = 10 };
//          （同族 **25 处**硬编码英文名，**只有 1 处**给 `Class`）
//
//    ★★★ 判据：`Name` 被硬编码 ⇒ `Entry.GetDisplayName()` **永远走不到**
//        ⇒ 我方 `Mutations.xml` 的 `DisplayName` **结构上够不着**这个出口
//        · 同时给了 `Class` ⇒ `TryGetMutationEntry(Name, Class)` 还能解析 ⇒ 中文
//        · `Class` 为 null ⇒ **无解** ⇒ 只能显示硬编码英文
//
//    探针实证（`_zhs_unitprobe.log`）：
//        Name="双翼"                     Class="Wings"                  → 中文 ✅
//        Name="常态气体生成"              Class="NormalityGasGeneration" → 中文 ✅
//        Name="Corrosive Gas Generation" Class=(null)                   → 全英文 ❌
//
//  【为什么不能靠现有出口】`ZhsPopupPatch` 里**早就**有 `^(.+?) at level (\d+)$` 的处理
//    （L1537「★★★ 必须在早退之前处理」），但那是 **`Popup` 通道**；
//    巨像构筑界面**不走 `Popup`**（直接 `Compound(GetDescription(true), '\n')`）
//    ⇒ 规则再多也够不着 ⇒ 必须在 `GetDescription` **本身上**挂。
//
//  【做法】Postfix：`Name` 英文 → 查 `ZhsMutationZh` 名表；`Name` 中文 → **原样**（幂等）；
//    骨架 `" at level N"` → 「N 级」。★ 只改 `__result`，不碰原方法与实例字段。
//
//  ⚠ 已知边界：`GameObjectMutationUnit` **随存档序列化**（`Name` 是 public 字段）
//    ⇒ 旧存档里已冻结的英文 Unit，本补丁**能救**（它读的是当时的 `Name` 值再翻）；
//      但若 `Name` 是**中文**则原样放过 —— 两种情况都幂等。
//
//  开关：装机目录放 `_zhs_unitname_off.txt` 即停用（默认开启）
// ═══════════════════════════════════════════════════════════════════════════
using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    /// <summary>巨像界面 `GameObjectMutationUnit.GetDescription` 的汉化出口。</summary>
    internal static class ZhsUnitName
    {
        internal static bool Disabled;
        internal static int Hits;

        /// <summary>`X at level N` —— 与 `ZhsPopupText.AT_LEVEL` 同形（那份是 private，这里自备）。</summary>
        private static readonly Regex AT_LEVEL =
            new Regex(@"^(.+?) at level (\d+)$", RegexOptions.Compiled);

        // ── 巨像 Unit 的三种硬编码模板（2026-10-18 第二批 · 反编译实证） ──
        //   `GameObjectSecretUnit::GetDescription`  → $"Reveals {Amount} secrets on creation"
        //   `GameObjectAttributeUnit::GetDescription`→ `+2` + Compound("to all stats")
        //   `GameObjectGolemQuestRandomUnit::GetDescription`
        //        → string.Format("{0} random effect{1}", Amount, Amount == 1 ? "" : "s")
        //   ★ 它们的出口是 `GetDescription` 本身（巨像界面不走 Popup）⇒ 必须挂在这里。
        private static readonly Regex REVEALS =
            new Regex(@"^Reveals (\d+) secrets? on creation$", RegexOptions.Compiled);
        private static readonly Regex TO_ALL_STATS =
            new Regex(@"^(?:(.+?)\s+)?to all stats$", RegexOptions.Compiled);
        private static readonly Regex RANDOM_EFFECTS =
            new Regex(@"^(\d+) random effects?$", RegexOptions.Compiled);

        /// <summary>把 `Name + " at level " + Level` 转成中文。命中返回中文，未命中返回 null。</summary>
        internal static string Fix(string s)
        {
            if (Disabled || string.IsNullOrEmpty(s)) return null;

            // ⓪ 巨像 Unit 的三种整句/片段（先做，且**不依赖**变异名表）
            string t = s.Trim();
            Match u = REVEALS.Match(t);
            if (u.Success) return "创造时揭示 " + u.Groups[1].Value + " 个秘密";
            u = RANDOM_EFFECTS.Match(t);
            if (u.Success) return u.Groups[1].Value + " 个随机效果";
            u = TO_ALL_STATS.Match(t);
            if (u.Success)
            {
                string amount = u.Groups[1].Value;
                return (amount.Length == 0) ? "全部属性" : ("全部属性 " + amount);
            }

            // ① 先剥 ` at level N` 骨架
            string body = s;
            string lvl = null;
            Match m = AT_LEVEL.Match(s.Trim());
            if (m.Success)
            {
                body = m.Groups[1].Value.Trim();
                lvl = m.Groups[2].Value;
            }

            // ② 名字：英文查名表；已是中文则原样（★ 幂等，且防"双翼→双翼"这类二次处理）
            //    ★ `MutName` / `HasCjk` 住在 `ZhsPopupPatch`（**不是** `ZhsPopupText`）
            string zh = ZhsPopupPatch.MutName(body);
            if (zh == null && ZhsPopupPatch.HasCjk(body)) zh = body;
            if (zh == null) return null;          // 名字既不认识也不是中文 ⇒ 整串放过

            // ③ 重新拼
            string r = (lvl == null) ? zh : (zh + " " + lvl + " 级");
            if (r == s) return null;              // 没变化 ⇒ 不发日志、不改
            return r;
        }
    }

    /// <summary>把 <see cref="ZhsUnitName"/> 挂到 `GetDescription` 上。</summary>
    internal static class ZhsUnitNamePatch
    {
        private const string Tag = "[QudZhsCookbook] 单位名接管：";
        private static bool _applied;

        private static void Postfix(ref string __result)
        {
            if (ZhsUnitName.Disabled) return;
            try
            {
                string zh = ZhsUnitName.Fix(__result);
                if (zh == null) return;
                if (ZhsUnitName.Hits < 24)
                    UnityEngine.Debug.Log(Tag + __result + "  =>  " + zh);
                ZhsUnitName.Hits++;
                __result = zh;
            }
            catch { }
        }

        internal static void Apply(Harmony h)
        {
            if (_applied) return;
            _applied = true;
            try
            {
                // 开关文件
                try
                {
                    string off = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        "AppData", "LocalLow", "Freehold Games", "CavesOfQud",
                        "Mods", "QudZhsCookbook", "_zhs_unitname_off.txt");
                    if (File.Exists(off)) ZhsUnitName.Disabled = true;
                }
                catch { }

                if (ZhsUnitName.Disabled)
                {
                    UnityEngine.Debug.Log(Tag + "开关文件存在，已停用");
                    return;
                }

                MethodInfo post = typeof(ZhsUnitNamePatch).GetMethod(
                    nameof(Postfix), BindingFlags.NonPublic | BindingFlags.Static);
                var hm = new HarmonyMethod(post);

                int n = 0;
                var detail = new System.Text.StringBuilder();

                // ★ 只挂 `GameObjectMutationUnit` 一族（其它 Unit 类型没有 `at level` 骨架）
                //   为什么用反射扫而不是写死类型名：`Class` 为 null 的实例也是这个类型，
                //   且显式实现/重载可能有多个
                // ★ 2026-10-18 第二批：巨像界面还有三种硬编码描述（Reveals N secrets /
                //   +N to all stats / N random effect(s)），出口同样是各自的 `GetDescription`
                //   ⇒ 一并挂上（规则在 `ZhsUnitName.Fix` 的 ⓪ 段）。
                string[] typeNames =
                {
                    "XRL.World.Units.GameObjectMutationUnit",
                    "XRL.World.Units.GameObjectSecretUnit",
                    "XRL.World.Units.GameObjectAttributeUnit",
                    "XRL.World.Units.GameObjectGolemQuestRandomUnit",
                };
                Assembly asm = typeof(XRL.World.GameObject).Assembly;
                int missing = 0;
                foreach (string typeName in typeNames)
                {
                    Type t = asm.GetType(typeName);
                    if (t == null)
                    {
                        missing++;
                        detail.Append(" ✗缺").Append(typeName);
                        continue;
                    }
                    foreach (MethodInfo mi in t.GetMethods(BindingFlags.Public
                        | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    {
                        if (mi.Name != "GetDescription" || mi.IsAbstract) continue;
                        try
                        {
                            h.Patch(mi, postfix: hm);
                            n++;
                            detail.Append(" ").Append(t.Name).Append('.').Append(mi.Name);
                        }
                        catch (Exception ex)
                        {
                            detail.Append(" ✗").Append(ex.GetType().Name);
                        }
                    }
                }

                UnityEngine.Debug.Log(Tag + "已挂 " + n + " 个 GetDescription 出口"
                    + "（巨像界面「变异名 + at level N」+ 三种硬编码描述；缺 " + missing + " 类）；明细" + detail);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError(Tag + "挂载失败：" + ex);
            }
        }
    }
}
