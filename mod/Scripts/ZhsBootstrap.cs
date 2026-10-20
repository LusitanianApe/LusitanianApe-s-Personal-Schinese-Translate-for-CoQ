// 引导：把所有一次性初始化与手工补丁集中到这里，由一个**必定在开局执行**的钩子调用。
//
// 为什么需要这个文件（根因）：
//   之前所有初始化都写在 CookbookGeneratePatch 的静态构造函数里。但 Harmony 只是把补丁挂上去，
//   **不会执行静态构造函数** —— .NET 里静态构造函数要等该类型第一次被访问才跑，
//   而我们的 Postfix 只有在食谱被命名时才会被调用。
//   于是：玩家 wish 随机书籍、没生成食谱时，马尔科夫补丁从未挂载 →
//   Player.log 里除了一条编译器警告，我们的日志一行都没有（this was the real bug）。
//
// 现在由 ZhsStartupPatch 钩住游戏开局方法调用 Init()，保证每次启动都执行一次。

using System;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsBootstrap
    {
        private static bool _done;
        private static readonly object Gate = new object();

        /// <summary>
        /// 补丁守卫用：目标类型上是否存在**唯一**的指定名字方法。
        /// 为什么需要：Harmony.PatchAll 顺序执行、遇异常即中断，
        /// 任何一个补丁类抛异常（尤其是重载导致的 AmbiguousMatch）都会让后面的补丁全不生效。
        /// 所以每个补丁类都要用 [HarmonyPrepare] 先问一句；这里统一实现，
        /// 解析失败一律返回 false（宁可不挂，绝不抛）。
        /// </summary>
        public static bool HasMethod(Type t, string name)
        {
            try
            {
                if (t == null || string.IsNullOrEmpty(name)) return false;
                var ms = t.GetMethods(System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Static
                    | System.Reflection.BindingFlags.Instance);
                int n = 0;
                foreach (var m in ms)
                    if (m.Name == name) n++;
                return n == 1;   // 只有唯一匹配才敢挂
            }
            catch { return false; }
        }

        /// <summary>
        /// ★★ 排查用：**跳过某个补丁的挂载**（不改代码、无需重编译）。
        ///
        /// 读法（每次调用读一次文件，代价极小 —— 只在初始化时被调几十次）：
        ///   模组目录下存在空文件 `_off_&lt;补丁名&gt;.txt` ⇒ 返回 true ⇒ 跳过该补丁挂载。
        ///
        /// 【为什么要它】排查"哪个补丁导致崩溃"时，最省事的办法是**只挂一部分**。
        ///   但 `ZhsBootstrap.cs` 硬引用 77 个补丁类 ⇒ **禁文件会编译失败**（CS0103×77）；
        ///   改注释又要反复编译、容易改坏。
        ///   ⇒ 用标记文件：**切换只需建/删一个空文件**，随时可逆、零编译。
        /// </summary>
        public static bool MountOff(string name)
        {
            // ★★★ v3：**无条件留痕**。前两版都栽在"静默失败"上：
            //     v1 根路径算错（用 ModAssemblies 当模组根）、v2 只在命中时写日志
            //     ⇒ "开关没生效" 时毫无线索。这里把**每次调用 + 所有尝试路径**都写下来。
            string tried = "";
            bool hit = false;
            try
            {
                string file = "_off_" + name + ".txt";
                var roots = new System.Collections.Generic.List<string>();

                // ① 模组目录（**首选**：标记文件的常规位置）
                string modRoot = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "AppData", "LocalLow", "Freehold Games", "CavesOfQud",
                    "Mods", "QudZhsCookbook");
                roots.Add(modRoot);

                // ② Assembly 所在目录（本模组应在 ModAssemblies）
                try
                {
                    string loc = System.Reflection.Assembly.GetExecutingAssembly().Location;
                    if (!string.IsNullOrEmpty(loc))
                        roots.Add(System.IO.Path.GetDirectoryName(loc));
                }
                catch { }

                // ③ 从 Location 往上找 QudZhsCookbook
                try
                {
                    string loc2 = System.Reflection.Assembly.GetExecutingAssembly().Location;
                    var di = string.IsNullOrEmpty(loc2) ? null
                             : new System.IO.DirectoryInfo(System.IO.Path.GetDirectoryName(loc2));
                    for (int i = 0; i < 4 && di != null; i++, di = di.Parent)
                        if (string.Equals(di.Name, "QudZhsCookbook",
                                          StringComparison.OrdinalIgnoreCase))
                        { roots.Add(di.FullName); break; }
                }
                catch { }

                foreach (var r in roots)
                {
                    if (string.IsNullOrEmpty(r)) continue;
                    string p = System.IO.Path.Combine(r, file);
                    tried += p + " | ";
                    if (System.IO.File.Exists(p)) { hit = true; break; }
                }

                // ★ 无条件写探针日志（放在模组目录；若模组目录不可写则放弃）
                try
                {
                    System.IO.File.AppendAllText(
                        System.IO.Path.Combine(modRoot, "_off_probe.log"),
                        DateTime.Now.ToString("HH:mm:ss.fff") + "  " + name
                        + "  hit=" + (hit ? "YES" : "no")
                        + "  tried=" + tried + "\n");
                }
                catch { }
            }
            catch { }
            return hit;
        }

        public static void Init(string from)
        {
            lock (Gate)
            {
                if (_done) return;
                _done = true;
            }
            UnityEngine.Debug.Log("[QudZhsCookbook] === 引导开始（触发点：" + from + "）===");

            try { ZhsTitleGenerator.EnsureBuilt(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 书名链构建失败：" + e); }

            try { ZhsBookText.EnsureBuilt(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 书籍正文链构建失败：" + e); }

            if (!MountOff("FallbackPatch")) try { FallbackPatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 兜底补丁失败：" + e); }

            if (!MountOff("RecipeNamePatch")) try { RecipeNamePatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 食谱正文补丁失败：" + e); }

            // ★ 物品改装（Mod*）描述：4 个 mod 的 GetDescription 是**裸字面量**
            //   （Keen / Metallized / Nanochelated / Overbuilt），不经过 _S/_T；
            //   其余 mod 都走串表且已覆盖。出口 = ItemModding.GetModificationDescription。
            if (!MountOff("ZhsModDescPatch")) try { ZhsModDescPatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 改装描述补丁失败：" + e); }

            // ★ 烹饪文本中文语序接管：餐食描述出口（Campfire.DescribeMeal）
            //   与合成假食材名的英语语法（Grammar.MakePossessive / Grammar.Ordinal）。
            //   逆向依据见 ZhsCookingPatch.cs 顶部注释。
            if (!MountOff("ZhsCookingPatch")) try { ZhsCookingPatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 烹饪接管补丁失败：" + e); }

            // ★ 物品拿取 message log 接管：`=subject.Does:take=` 等动词变形出口。
            //   走显式 TryApply()（属性式挂载在 0.40 上无任何日志记录，怀疑未生效）。
            if (!MountOff("ZhsTakePatch")) try { ZhsTakePatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 拿取接管失败：" + e); }

            // ★ 物品显示名接管：adjunct noun / 冠词 / 参数级英文（brackish 等）
            //   实证：`一份水洼 of 藻类的 brackish 水` 与 `该 villagers of Terchemas`
            if (!MountOff("ZhsAdjunctPatch")) try { ZhsAdjunctPatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 物品名接管失败：" + e); }

            // ★★ 冠词杀手（2.15.70）：接管 `Grammar.A` 的 **void / StringBuilder** 重载。
            //   `ZhsArticlePatch` 只枚举**返回 string** 的重载（它内部 `ReturnType != string ⇒ continue`），
            //   而 `PostProcessors.Article` 实际调的是 void 重载 ⇒ 那个重载**从没被接管**。
            //   后果实测：`其形制令人想见a 莹润玛瑙`（裸冠词）与 `=item|article=`（自我循环），
            //   以及 `Grammar.A` 在空串上 `Word[0]` ⇒ **IndexOutOfRange 崩溃**。
            if (!MountOff("ZhsArticleVoidPatch")) try { ZhsArticleVoidPatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 冠词杀手挂载失败：" + e); }

            // ★★ 冠词杀手 v3（2.16.74）：补上**显示名**那条路。
            //   ★ 用户提问「为什么之前杀冠词没覆盖到水池」—— 因为上面两个只挂了
            //     `Grammar.A(...)`（`XRL.Language.Grammar` 门类），
            //     而**池子名**的 `a` 走的是**另一条链**：
            //         WithIndefiniteArticle → GetDisplayName(WithIndefiniteArticle: true)
            //           → Translator.GameObjectDisplayName（门面，只转发）
            //           → provider.GameObjectDisplayName（TranslatorEnglish : TranslatorBase）
            //           → TranslatorBase.AddArticle
            //                Params.SB.Insert(0, "a ");      ← ★ 直接插，**从不经过 Grammar.A**
            //     ⇒ 典型「**补丁挂对了、但挂错了层**」（§46/§50 同族）。
            if (!MountOff("ZhsArticleTransPatch")) try { ZhsArticleTransPatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 冠词杀手v3挂载失败：" + e); }

            // ★★ 数词汉化（2.15.71）：`Translator.Cardinal(long)` 产出**英文数词**
            //   （`one` / `thirty-seven` / `one hundred`），而名词已中文 ⇒ 中英混排。
            //   实证（`Local\VariableReplacers.txt`）：
            //     `=1.cardinal.names#…=`  → "one奇异遗物"      != "one weird artifact"
            //     `=37.a.or.cardinal.names#…=` → "thirty-seven 奇异遗物" != "thirty-seven weird artifacts"
            //   `decomp find Translator.Cardinal` ⇒ NumberReplacers 全族都走它
            //   ⇒ 挂一处即全覆盖。
            if (!MountOff("ZhsNumberZhPatch")) try { ZhsNumberZhPatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 数词汉化挂载失败：" + e); }

            // ★★ 身体部位的**方位前缀**汉化（修截图里的「leftfore脚」）
            //   根因：`Laterality.BuildLateralityAdjective` 里是**硬编码英文**
            //         （inner/outer/upper/lower/right/left/inside/outside/mid-fore/mid-hind/fore/hind），
            //         而 `WithLateralityAdjective` 末尾 `textBuilder.Append(Base.ToLower())`
            //         ⇒ `left`+`fore`+我们译好的「脚」= **「leftfore脚」** ✗
            //   挂点：`WithLateralityAdjective`（4 参重载）—— 它**只被** BodyPart 用它拼名字/描述，
            //         且 `BuildLateralityAdjective` 只被它和 `Strip` 调用 ⇒ 一处覆盖全部 ✓
            //   同挂 `StripLateralityAdjective`：换方位时负责剥旧前缀，不挂会累积 ✗
            if (!MountOff("ZhsLateralityPatch")) try { ZhsLateralityPatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 方位前缀挂载失败：" + e); }

            // ★★ 2026-10-17（测试窗口 #40）：**终端屏内容**（Ereshkigal / GritGate 等 9 类）。
            //   `XRL.UI.TerminalScreen::Update` 统一把 `MainText` + `Options` 拼成 `RenderedText`
            //   ⇒ 一个出口覆盖全部终端屏（见 `ZhsTerminalPatch.cs` 头注）。
            if (!MountOff("ZhsTerminalPatch")) try { ZhsTerminalPatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 终端接管挂载失败：" + e); }

            // ★★ 2026-10-17（测试窗口 #39）：12 个 `GetLevelText` 的硬编码等级文本
            //   （Confusion / 10 个 Breather / FlamingRay / FreezingRay）——
            //   `Mutations.xml` 的 `<leveltext>` 被类方法覆盖，只能挂出口。
            if (!MountOff("ZhsLevelTextPatch")) try { ZhsLevelTextPatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 等级文本挂载失败：" + e); }

            // ★★ 中文语言 Provider（走**官方框架** `Translator.SetProvider`）
            //   官方 `Translator.Provider` 有 31 个可覆写点，`TranslatorEnglish : TranslatorBase { }`
            //   是空壳 ⇒ 只要继承 `TranslatorBase` 覆写想改的即可。
            //   ★ 换这条路的额外好处：`AddArticleParams` 等 **ref struct** 参数
            //     当年让 Harmony 报 `Cannot box IsByRefLike type` ⇒ 补丁等于不存在；
            //     继承 + SetProvider 是**直接调用**，不经装箱 ⇒ 这类方法从此可改。
            //   ⚠ 本版只覆写了 `NextPossibleLineBreakIndex`（中文折行），其余走基类英文实现。
            if (!MountOff("ZhsChineseProvider")) try { ZhsChineseProviderPatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 中文Provider挂载失败：" + e); }

            // ★ 所有格补丁（`Grammar.MakePossessive`）：中文名补「的」、已是 `'s` 的原样放行。
            //   ⚠ 它此前是 `[HarmonyPatch]` 属性式挂载 —— 日志里**没有它的 Prepare 记录**
            //     ⇒ 从未生效 ⇒ 这里改为**显式挂载**（与其它补丁一致，便于用日志验收）。
            if (!MountOff("ZhsPossessivePatch")) try { ZhsPossessivePatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 所有格挂载失败：" + e); }

            // ★★ 牌匾/墓碑排版（2.15.73）：`PostProcessors.VaryPlaque` 用
            //   `ColorUtility.LengthExceptFormatting` 按**字符数**算宽来居中，
            //   而中文一字占 **2 列** ⇒ 填充 NBSP 少一半 ⇒ **右边框右移**。
            //   实测 `Local\test_tombstones.txt`：181 条框行里 **41 条错位**（正确 36 列）。
            //   ⇒ Prefix 整体接管，按**显示宽度**折行与居中。
            if (!MountOff("ZhsPlaqueLayoutPatch")) try { ZhsPlaqueLayoutPatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 牌匾排版挂载失败：" + e); }

            // ★ 字符串表查找**诊断**（2.15.74）：录下 `Strings.TryGetString` 的真实查找过程。
            //   用途：给「有译文却出英文」这类**矛盾现象**定案
            //   （用户截图弹窗 `You receive 奇异饰品!` —— 半英半中）。
            //   输出 `<模组目录>\ZhsStrTableDiag.log`；`_zhs_strdiag_off.txt` 可停用。
            // ↓↓ 发布版：探针 ZhsStrTableDiagPatch 已撤（开发分支保留） ↓↓
#if false
            if (!MountOff("ZhsStrTableDiagPatch")) try { ZhsStrTableDiagPatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 字符串表诊断挂载失败：" + e); }
#endif
            // ↑↑ 发布版：探针 ZhsStrTableDiagPatch 已撤 ↑↑

            // ★★ 飞行消息汉化（2.15.74）：`Flight.StartFlying` 里
            //   `MessageQueue.AddPlayerMessage(Object.Does("begin") + " flying.")`
            //   —— **`" flying."` 是 C# 硬编码字面量** ⇒ 字符串表覆盖不到
            //   ⇒ 出现「蝙蝠 begins flying.」（中文名 + 英文动词短语）。
            //   做法：挂消息入队口 `MessageQueue.AddPlayerMessage`，定点换掉这类尾串。
            if (!MountOff("ZhsFlightPatch")) try { ZhsFlightPatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 飞行消息挂载失败：" + e); }

            // ★★ 效果名采集 + 汉化（2.15.75）：「查看效果」面板里 `swimming` 是英文。
            //   反编译实证：`Effect.DisplayName` 是**构造函数里硬编码**的
            //   （`LiquidCovered::.ctor { DisplayName = "covered in liquid"; }`）
            //   ⇒ **不走字符串表**，任何 XML 都改不到。
            //   做法：挂 `XRL.World.Effect` 构造函数采集全量清单（写 `ZhsEffectNames.log`）
            //   ＋ 查表就地汉化；并扫全程序集挂所有 `GetStateDescription` 重写。
            if (!MountOff("ZhsEffectNameCollectPatch")) try { ZhsEffectNameCollectPatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 效果名挂载失败：" + e); }

            // ★★ 量词汉化（2.15.83）：`=N.things:名词:复数=` 产出**英文名词**
            //   （用户截图「严查」：`Subterranean salt marsh, Innus , legendary 光鱼的巢穴,
            //     1 stratum深层, 甲虫月渐盈`）。
            //   反编译 `NumberReplacers.Things`：`Context.Value.Append(Number).Compound(value, " ")`
            //   ⇒ 凡「数量 == 1」的场合都漏英文单数名词。
            //   官方模板共 **43 条**含 `.things:`，涉及 12 个名词
            //   （unit·dram·turn·square·round·stratum·time·tile·error·point·charge·item）。
            //   做法：挂那两个标量重载，按中文量词表改写（未收录的名词一律交还游戏实现）。
            if (!MountOff("ZhsThingsPatch")) try { ZhsThingsPatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 量词汉化挂载失败：" + e); }

            // ★★ 改名弹窗（2.15.84）：`GameObject.HandleRename` 里 6 条**运行时拼接**骨架
            //   （`"Enter a name for " + text + "."` · `"Rename yourself"` ·
            //     `"Choose a random name from " + … + " culture."` ·
            //     `"You start calling " + text + " by the name '" + name + "'."`）
            //   ⇒ 用户截图里只有 `你自己`/`你的` 是中文（那来自第二人称渲染），骨架是英文。
            if (!MountOff("ZhsRenamePatch")) try { ZhsRenamePatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 改名弹窗挂载失败：" + e); }

            // ★ 弹窗选项**渲染点**诊断（2.15.76）：给「Esc 菜单替换已执行但屏幕仍英文」定案。
            //   日志实证：`系统菜单：首次替换：[设置检查点]…` 说明数组**确实换成了中文**，
            //   但屏幕仍显示英文 ⇒ 必须看**真正要渲染的文本**是什么。
            //   输出 `<模组目录>\ZhsPopupRender.log`。
            // ↓↓ 发布版：探针 ZhsPopupRenderDiagPatch 已撤（开发分支保留） ↓↓
#if false
            if (!MountOff("ZhsPopupRenderDiagPatch")) try { ZhsPopupRenderDiagPatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 弹窗渲染诊断挂载失败：" + e); }
#endif
            // ↑↑ 发布版：探针 ZhsPopupRenderDiagPatch 已撤 ↑↑

            // ★★ 池子名字**组成元素**诊断（接续单「第 0 步」）：
            //   「池子」到底是 `Render.DisplayName`（= `E.Base`）贡献的，
            //   还是 `LiquidVolume` 的 clause 贡献的？「一个」插在哪一层？
            //   ⇒ 挂 `GameObject.GetDisplayName` 的 **Postfix（只读）**，
            //     输出 `<模组目录>\ZhsPoolNameDiag.log`。
            //   ⚠ 反编译已确认顺序写死（ADJ -500 → Base 10 → Clause 600），
            //     所以**必须先看清元素归属**再决定改数据还是改出口。
            // ↓↓ 发布版：探针 ZhsPoolNameDiagPatch 已撤（开发分支保留） ↓↓
#if false
            if (!MountOff("ZhsPoolNameDiagPatch")) try { ZhsPoolNameDiagPatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 池子名诊断挂载失败：" + e); }
#endif
            // ↑↑ 发布版：探针 ZhsPoolNameDiagPatch 已撤 ↑↑

            // ★★ 属性/突变点数弹窗（2.15.79）：`StatusScreen.BuyStat` 里
            //   `Popup.Show(text + "It will cost … to increase " + ChosenStat + " by 1.…")`
            //   —— **全是运行时字符串拼接**，字符串表够不着（用户截图「严查」）。
            //   做法：挂 `Popup.Show` / `ShowYesNo`，对形态明确的骨架定点重写。
            if (!MountOff("ZhsStatPopupPatch")) try { ZhsStatPopupPatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 属性弹窗挂载失败：" + e); }

            // ★★ 动词变形**中文直通闸**（2.15.87）：用户截图 `一个年轻象牙 strikes 此处!`
            //   反编译链：`Impaler` 的 `Message = "…=verb:strike=…"`（**部件数据字段**，可覆盖）
            //     → `StringReplacers.ThirdPerson(Context, Verb)`
            //     → `Grammar.ThirdPerson("strike")` ⇒ **"strikes"**
            //   我方动词表有 `"strike"` 但**无 `"strikes"`** ⇒ **变形之后才查表** ⇒ 漏英文。
            //   （同族 `impale`/`impales` 两条都收了 ⇒ 所以那条恰好没漏）
            //   ⇒ 给 `ThirdPerson` / `PastTenseOf` 加中文直通闸（中文动词无形态变化），
            //     一处修全族，胜过逐个补变形词（打地鼠）。
            if (!MountOff("ZhsVerbFormPatch")) try { ZhsVerbFormPatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 动词变形挂载失败：" + e); }

            // ★★ 技能冷却提示（2.15.88）：用户截图 `冷却：53 rounds` +
            //   `Cooldown increased by 2 due to low 意志.`
            //   反编译 `XRL.Templates.StatCollector::CollectCooldownTurns`：
            //       postfix += $"\nCooldown increased by {num} due to {calculation.Reason}.";
            //       postfix = postfix + "\nCooldown cannot be reduced below " + text + " rounds.";
            //   ⇒ **C# 字符串插值**，字符串表够不着 ⇒ 挂该方法改 `postfix` 字段。
            if (!MountOff("ZhsCooldownPatch")) try { ZhsCooldownPatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 冷却提示挂载失败：" + e); }

            // ★★ 短描述后置串（2.15.82）：生物卡的 `Base demeanor:` / `Engagement style:`
            //   —— 反编译 `Brain.HandleEvent(GetShortDescriptionEvent)` 里的 4 条裸字面量，
            //   **不走 MessageQueue** ⇒ 挂 `TextBuilder.Append(string)` 在入口替换。
            if (!MountOff("ZhsShortDescPatch")) try { ZhsShortDescPatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 短描述挂载失败：" + e); }

            // ★ 任务通知接管：`XRL.World.Quest` 里**硬编码**的英文骨架
            //   （反编译实证：Popup.Show("You have received a new quest, " + DisplayName + "!")）
            //   ⇒ 字符串表与 DLL 字面量都覆盖不到，只能拦 Popup/MessageQueue 入口。
            if (!MountOff("ZhsQuestTextPatch")) try { ZhsQuestTextPatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 任务通知接管失败：" + e); }

            // ★ 对话选项**方括号注解**（#19）：`[Complete Quest Step]` / `[begin water ritual; …]`
            //   反编译实证：`QuestHandler` / `WaterRitualBegin` 的 `GetChoiceTagEvent` 里硬编码，
            //   挂 `Choice.GetDisplayText(bool)` 的 Postfix 定点替换。
            if (!MountOff("ZhsConversationTagPatch")) try { ZhsConversationTagPatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 对话注解挂载失败：" + e); }

            // ★ 任务名 / 物件名的中文显示接管
            //   `Quest.Name` 是主键（被 IfFinishedQuest / QuestStepFinisher 引用），
            //   改它会破坏任务判定 —— 只挂 `Quest.DisplayName` 与
            //   `GameObjectBlueprint.DisplayName()`，Name 一律不动。
            if (!MountOff("ZhsNameDisplayPatch")) try { ZhsNameDisplayPatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 名称显示接管失败：" + e); }

            // ★ 技能栏（底边栏）能力名接管
            //   `Skill.*::AddSkill` 用 `_S("<类名> ActivatedAbility Name", "<英文名>")`
            //   取显示名；我们的 Strings.xml 里 23 条与官方示例一一对应（差集 0），
            //   但 `_S()` 的查找有额外限定导致不生效 —— 直接换 `AddAbility` 的 Name 形参。
            if (!MountOff("ZhsAbilityNamePatch")) try { ZhsAbilityNamePatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 能力名接管失败：" + e); }

            // ★ 角色创建流程诊断（只读探针）→ _zhs_chargen.log
            // ★ 天职守卫：阻止预设设定的天职被 handleModuleDataChange 误清空
            if (!MountOff("ZhsSubtypeGuardPatch")) try { ZhsSubtypeGuardPatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 天职守卫失败：" + e); }

            // ★【已退休 · 2026-10-15 用户指示】天职窗口跳过（ZhsWindowSkipPatch）
            //   它是 callings bug 的兜底，但 `_tools\callings-结案报告.md` 已证明：
            //     ① 它是报告里「走过的弯路」三层失败护栏的同款做法
            //        （"在跟 Unity 的异步时序搏斗"，注定打不赢）；
            //     ② 它还在 `GetNextEnabledWindowDescriptor` 的 Postfix 里**主动调 AdvanceWindow**
            //        ⇒ 在别人方法收尾阶段改 UI 状态，属高危（A0 崩溃嫌疑名单）；
            //     ③ 真根因是我们改过 `MutationEntry.Name`，1.61.0 已改用官方 `DisplayName`
            //        ⇒ 连角色创建的推进链都正常了（ChooseSubtypes → Mutations → PickAttributes → BuildSummary）。
            //   ⇒ 兜底无必要，代码留存：`Scripts\optional\ZhsWindowSkipPatch.cs.disabled`
            //      （要回退：删掉 `.disabled` 后缀，并把本注册块恢复）

            // ★ 英语语法引擎的中文短路：`Grammar.ThirdPerson/Pluralize/PastTenseOf/...`
            //   遇到中文词时直接返回原文，避免产出 `跳s` `拿走了ed` 这类垃圾。
            if (!MountOff("ZhsGrammarShortCircuit")) try { ZhsGrammarShortCircuit.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 语法短路失败：" + e); }

            // ★ 堆叠数量后缀（x2）与「一个」冲突 → 删掉多余量词
            if (!MountOff("ZhsStackCountFix")) try { ZhsStackCountFix.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 数量后缀处理失败：" + e); }

            // ★ 战斗日志硬编码英文的中文接管
            //   `Combat.HandleEvent` / `MeleeAttackWithWeaponInternal` 里的
            //   `AddPlayerMessage("You don't penetrate " + ...)` 等**不走字符串表**，
            //   必须按模式识别改写。
            if (!MountOff("ZhsCombatMsgPatch")) try { ZhsCombatMsgPatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 战斗消息接管失败：" + e); }

            // ★ 许愿菜单显示名（2026-10-17 新）
            //   `wishcommands.xml` 的 `<wish>` **没有 Name 属性** ⇒ 模组整份加进去只会
            //   **追加**（英文一条 + 中文一条重复）⇒ 不在数据侧做，
            //   改在运行时翻 `WishMenu.MenuItems[i].DisplayName`
            //   （`OpenWishMenu` 用**下标**取 `Commands` ⇒ 改显示名安全）。
            //   停用：装机目录放 `_zhs_wishmenu_off.txt`
            if (!MountOff("ZhsWishMenuPatch")) try { ZhsWishMenuPatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 许愿菜单挂载失败：" + e); }

            // ★★ 碎片字典兜底（2026-10-17 新）：游戏**没有**「动词 → 非谓语」的生成器
            //    （`Grammar` 只有 ThirdPerson / PastTenseOf 两处真变位），
            //    `to X` / `X-ing` 全是数据里的固定字面量 ⇒ 句子被拼成
            //    「动态动词槽 + 固定英文碎片」⇒ 逐词接管必然留英文。
            //    本出口在**消息定稿处**对**登记过**的碎片做贪心最长匹配替换。
            //    表：`ZhsFragmentDictTable.cs`（生成器 `_tools\_gen_frag_table.py`）
            //    停用：装机目录放 `_zhs_fragdict_off.txt`
            if (!MountOff("ZhsFragmentDictPatch"))
            {
                try
                {
                    ZhsFragmentDictTable.Load();
                    ZhsFragmentDictPatch.TryApply();
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogError("[QudZhsCookbook] 碎片字典挂载失败：" + e);
                }
            }

            // ★ `Messaging.XDidYToZ` 的动词中文化
            //   动词是 C# 调用方以英文实参传入的（如 Chair.SitDown 传 "sit down on"），
            //   字符串表改不到 —— 只能改写该形参。
            if (!MountOff("ZhsMessagingVerbPatch")) try { ZhsMessagingVerbPatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] XDidY 动词接管失败：" + e); }

            // 2.16.120：DidX 家族**合成后整句**（合成侧修不掉硬编码的 "You "，只能在出口侧）
            if (!MountOff("ZhsDidXZh")) try { ZhsDidXZh.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] DidX 合成消息接管失败：" + e); }

            // ★ 动词片段汉化（`=subject.Does:verb=` 的动词位）。
            //   原先只有属性式 `[HarmonyPatch]`，而本项目实测属性式**从未生效**
            //   → `You wade` 这类英文残留。改为显式挂载。
            if (!MountOff("ZhsVerbPatch")) try { ZhsVerbPatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 动词接管失败：" + e); }

            // ★ 变异窗口数据同步护栏（修游戏自身缺失的 null 检查）
            if (!MountOff("ZhsMutationWindowGuard")) try { ZhsMutationWindowGuard.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 变异窗口护栏失败：" + e); }

            // ★ 射线变体的**部位名**中文化（`Hands`/`Face`/`Feet` ⇒ 手/脸/脚）
            //   挂 `FlamingRay`/`FreezingRay` 的 `GetVariantName`（**必须挂具体重写**：
            //   `SelectVariant` 里是 `callvirt` ⇒ 挂基类收不到）。
            //   依据见 `ZhsRayVariantPatch.cs` 头部（数据侧三条路已实证堵死）。
            if (!MountOff("ZhsRayVariantPatch")) try { ZhsRayVariantPatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 射线变体名失败：" + e); }

            // ★ 墓碑排版（按显示宽度重排；v1 因反向解析输出而吃掉碑文，v2 改为在输入上处理）
            if (!MountOff("ZhsTombstoneLayout")) try { ZhsTombstoneLayout.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 墓碑排版失败：" + e); }

            // ★ Esc 主菜单（标签硬编码在 XRLCore::PlayerTurn 的字面量里）
            try { ZhsKeyMappingTitlePatch.TryApply();
        ZhsHelpIndexPatch.TryApply();
 ZhsSystemMenuPatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 系统菜单失败：" + e); }

            // ★ 现代 UI（UGUI / `Qud.UI`）三处英文 —— 数据表与字符串表**都够不到**
            //   ① 标题 `HELP` / `CONTROL MAPPING`：预制体 `UITextSkin.text` 的序列化值
            //      （`level0` 里就是 `{{W|HELP}}` / `{{W|CONTROL MAPPING}}`；DLL 与字符串表均无）
            //   ② 帮助左侧栏 12 条：`LeftSideCategory::setData` 渲染的是 `HelpDataRow.CategoryId`
            //      （= 官方 `name` 键，不可动）⇒ 改用 `Description`（= 我方 `DisplayName`）
            //   ③ 按键设置那列大写英文分类：`CategoriesInOrder` 里官方英文分类的**空壳残留**
            //      （`HandleCommandNode` 改分类时只增不减）⇒ 打开前剔除空壳
            if (!MountOff("ZhsModernUiPatch")) try { ZhsModernUiPatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 现代界面失败：" + e); }

            // ★ 2.16.58：效果说明（状态面板）
            //   数据侧：本模组提供中文 `EffectsDetails.xml`（覆盖官方 49 个 `Effect.*.Details` 模板）
            //   运行侧：本补丁兜住**绕过模板、直接返回字面量**的效果类（如 `Flying.GetDetails()`）
            if (!MountOff("ZhsEffectDetailsPatch")) try { ZhsEffectDetailsPatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 效果说明失败：" + e); }

            // ★ 2.16.48：**已撤掉**「超长文本护栏」（`ZhsLongTextGuard`，2.16.41 加的）。
            //   用户 2026-10-09 决定撤除。
            //   ⚠️ 后果：`HistoryTestView` 的 `GenerateTombstones` 若导出文本 > 约 3 万字符，
            //      可能重新出现「进程冻结」（CPU 零增量、`File.WriteAllText` 永不执行）。
            //      根因在官方代码本身（`Emit` 先 `OutputWindow.SetText` 再写文件），
            //      详见 `_tools\GenerateTombstones冻结-根因.md` 与 `KNOWN-ISSUES.md`。

            // ★ 筛选栏类目（库存筛选 + 日志页签）
            //   实证：`Qud.UI.FilterBarCategoryButton::.cctor` 里写死；
            //   显示文字兜底 = 英文类目名，`categoryTextMap` 只有 1 条 `*All`。
            //   日志页签（`Village Histories` 等）数据文件里**全无** ⇒ 纯硬编码。
            if (!MountOff("ZhsFilterBarPatch")) try { ZhsFilterBarPatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 筛选栏失败：" + e); }

            // ★ 漏译自动采集探针（借 DFCN「自动采集未翻译片段」的思路）
            //
            //   反编译实证收敛点（`XRL.World.Capabilities.Messaging::HandleMessage`）：
            //     ├─ Popup.Show(…)                     → 弹窗
            //     └─ MessageQueue.AddPlayerMessage(…)  → 消息日志
            //   `MessageQueue` 的公开方法**只有 2 个 AddPlayerMessage 重载**
            //   ⇒ **输入散，输出收敛** ⇒ 挂在出口就能看到「最终显示文本」。
            //
            //   探针把仍含英文的文本**归类**（这是它的核心价值）：
            //     半译（中英混排）   ⇒ 名字映射缺口 / 规则未覆盖
            //     占位符残留         ⇒ XML 字符串表有对应条目但未命中
            //     纯英文             ⇒ C# 硬编码 / 静态字段 / 运行时拼接
            //   ⇒ 漏译不必再靠人肉截图发现；且能**区分**是 XML 漏译还是硬编码。
            //
            //   ★ 必须**最后挂**：Harmony 的 postfix 后注册者后执行，
            //     这样读到的是**其它补丁改完之后**的文本。
            // ↓↓ 发布版：探针 ZhsUntranslatedProbe 已撤（开发分支保留） ↓↓
#if false
            if (!MountOff("ZhsUntranslatedProbe")) try { ZhsUntranslatedProbe.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 漏译探针失败：" + e); }
#endif
            // ↑↑ 发布版：探针 ZhsUntranslatedProbe 已撤 ↑↑

            // ★ 单位探针（**只读**）—— 定位「巨像构筑界面」那批文本走哪条出口
            //   起因（2026-10-16）：巨像界面同一类信息四种形态（名字/骨架 中英混杂），
            //   且这批文本在 Player.log 里**完全不留痕** ⇒ 不经过已有的任何出口 ⇒ 疑第三套。
            //   已确证拼接点：`GameObjectGolemQuestRandomUnit::Apply` 里
            //     `textBuilder?.Compound(item.GetDescription(Inscription: true), '\n')`
            //   本探针记录 `XRL.World.Units.*` 家族全部 `GetDescription` 的
            //   **类型 / 调用方 / 实例字段（Name/Class/Level/Entry.Name/Entry.DisplayName）/ 返回值**。
            //   ★ 只读：不改文本、不改行为。输出 `_zhs_unitprobe.log`。
            // ↓↓ 发布版：探针 ZhsUnitProbe 已撤（开发分支保留） ↓↓
#if false
            if (!MountOff("ZhsUnitProbe")) try
            {
                Harmony hUnit = new Harmony("nattens.qudzhscookbook.unitprobe");
                ZhsUnitProbe.Apply(hUnit);
            }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 单位探针失败：" + e); }
#endif
            // ↑↑ 发布版：探针 ZhsUnitProbe 已撤 ↑↑

            // ★ 单位名接管（**真补丁**）—— 巨像构筑界面「变异名 + at level N」
            //   出口：`GameObjectMutationUnit::GetDescription`（`Name` 被硬编码时
            //         `Entry.GetDisplayName()` 走不到 ⇒ 我方 XML `DisplayName` 够不着）
            //   实证：`Name="Corrosive Gas Generation" Class=(null)` ⇒ 全英文
            //         `Name="常态气体生成" Class="NormalityGasGeneration"` ⇒ 中文
            //   判据与证据链见 `_tools\文本生命周期-全景图.md` §7.9
            if (!MountOff("ZhsUnitNamePatch")) try
            {
                Harmony hUnitName = new Harmony("nattens.qudzhscookbook.unitname");
                ZhsUnitNamePatch.Apply(hUnitName);
            }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 单位名接管失败：" + e); }

            // ★ 变异 `statline` 的**取值**（`射程：sight` ⇒ `射程：视野`）
            //   出口：`StatCollector::Set(string,string)` 的 arg1（源头字符串）
            //   ⇒ 改源头 ⇒ 官方渲染管线原样跑（颜色/单位/复数不动）
            //   ★ 不走 `StatLineNode` 的 Prefix：那条路要 `ToString()` 再 Append，
            //     会丢 `TextBuilder` 的颜色段（`AppendTo` 保留 span，`Append(string)` 不保留）
            //   取证与判据见 `ZhsStatValuePatch.cs` 头注释
            if (!MountOff("ZhsStatValue")) try { ZhsStatValuePatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 取值源出口失败：" + e); }

            // ★ 状态效果显示名（HUD「生效效果：」那一行）
            //   实证：截图 `生效效果： defensive stance`
            //   反编译：`XRL.World.Effect.DisplayName` 是**公开字段**，
            //   各效果类在 `.ctor` 里**直接赋硬编码英文**：
            //     LongbladeStance_Defensive() { DisplayName = "{{G|defensive stance}}"; }
            //     GetStateDescription() => "{{G|in defensive stance}}"
            //   ⇒ 两者都**不走字符串表**，XML 改不到 ⇒ 只能挂补丁。
            if (!MountOff("ZhsEffectNamePatch")) try { ZhsEffectNamePatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 效果名接管失败：" + e); }

            // ★ 传送目标选择弹窗（Select a destination 等**硬编码**在
            //   GameObject::PullDown 与 GetDirectionFromCellXY 里，XML 改不到）
            if (!MountOff("ZhsPullDownPatch")) try { ZhsPullDownPatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 传送选单接管失败：" + e); }

            // ★ 「查看效果」界面（Active Effects / No active effects. 硬编码在
            //   GameObject::ShowActiveEffects 里；页脚翻页走字符串表本来就已中文）
            if (!MountOff("ZhsEffectBookPatch")) try { ZhsEffectBookPatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 效果书接管失败：" + e); }

            // ★ 硬编码弹窗（Popup.ShowYesNoCancel("…") 等字面量，
            //   实证：Would you like to walk to the nearest stairway down?）
            if (!MountOff("ZhsPopupPatch")) try { ZhsPopupPatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 弹窗接管失败：" + e); }

            // ★★ 2.15.98：电力/液压/机械/生物机械**传输**描述
            //   `IPowerTransmission::GetBehaviorDescription()` 的 4 分支拼接
            //   （用户截图：`Contains wiring enabling it to function as part of
            //     a power grid, producing electrical charge.`）
            //   挂该方法 Postfix，按替换表改写（固定片段 + 12 个部件字段值）。
            if (!MountOff("ZhsPowerTransPatch")) try { ZhsPowerTransPatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 传输描述挂载失败：" + e); }


            // ★ 称呼词「字段级」汉化（第 3 版）—— 前两版挂在**属性 getter** 上从未执行；
            //   实证：XRL.World.BasePronounProvider._FormalAddressTerm = "friend" 是
            //   **字段级硬编码默认值**，且随 Save/Load 存进存档 ⇒ 只能挂构造函数 + Load。
            if (!MountOff("ZhsPronounFieldPatch")) try { ZhsPronounFieldPatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 称呼词接管失败：" + e); }

            // ★ XDidY / XDidYToZ 的**补语**（Extra）英文清理
            //   实证消息：阿盖夫 has restocked 他的 inventory!
            //   根因：GenericInventoryRestocker::PerformStock 传
            //         Messaging.XDidY(…, "have", "restocked " + its + " inventory", "!")
            //         ⇒ 动词与补语都是**英文实参**，字符串表改不到
            if (!MountOff("ZhsXDidYPatch")) try { ZhsXDidYPatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] XDidY 补语接管失败：" + e); }

            // ★ 物种名（=player.apparentSpecies= ⇒ human）—— 只挂渲染出口，
            //   不动 Species（它是名字风格的查表键，有机制用途）
            if (!MountOff("ZhsSpeciesPatch")) try { ZhsSpeciesPatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 物种名接管失败：" + e); }

            // ★★ 2026-10-17（用户实机 `croccales帐篷` / `scales皮革`）：
            //   村庄物品的两个槽 —— `=X.species=` 与 `=X.fragment:Skin:skin=`
            //   ① `GetFragment` Postfix（只对 `Key=="Skin"`）
            //   ② `Species` Prefix（复用 `ZhsSpecies.L`，查不到就放行）
            //   ⇒ 产物从「croc + scales」变成「鳄 + 鳞片」
            if (!MountOff("ZhsVillageSkinPatch")) try { ZhsVillageSkinPatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 村庄材质出口失败：" + e); }

            // ★★ T1（2026-10-16）：任务步骤的**中文显示**
            //   背景：`Quests.xml` 的 `<step Name>` **同时是查表键**（`HandleStep` 里
            //         `ID` 缺省即 = `Name`）⇒ 我们曾把它译成中文 ⇒ 任务**永不结算**。
            //         已把 84 个 `Name` 还原成官方英文（键正确），本补丁负责"显示"时换回中文。
            //   出口：`XRL.World.Text.Delegates.QuestReplacers::StepName(ctx, QuestStep)`
            //         （即模板里的 `=step.name=`）—— **只改返回值，不碰 `Name` 字段**
            //         ⇒ 键与搜索（`QuestsLineData.searchText` 直读 `Name`）都不受影响。
            if (!MountOff("ZhsQuestStepPatch")) try { ZhsQuestStepPatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 任务步骤中文显示失败：" + e); }

            // ★★ N6（2026-10-17）：技能界面「前置要求」里的**技能名**
            //   根因（反编译 `XRL.UI.SPNode::ModernUIText`）：同一循环里两类取法不同 ——
            //         Requires  → `argument = Entry.Name;`                 ← 英文 ✗
            //         Exclusion → `argument = Entry2.GetDisplayName();`    ← 中文 ✓（官方自己不一致）
            //   出口：`ModernUIText(GameObject)` 的 Postfix，**只改返回值**；
            //         只替换 `{{G|X}}`/`{{g|X}}`/`{{R|X}}`/`{{r|X}}` 里**与表键全等**的整段
            //         ⇒ 防 `Jump`/`Charge`/`Cleave` 这类常用词被裸替换误伤（§44 同族）
            if (!MountOff("ZhsSkillReqPatch")) try { ZhsSkillReqPatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 技能前置名中文化失败：" + e); }

            // ★ 物品改名弹窗（Rename 你的 woven tunic. 那一屏）—— 英文骨架硬编码
            // （物品改名弹窗的**弹窗文案**重写走 ZhsItemNameRewrite，
            //   `ZhsItemNameDialog` 那个挂载点已废弃 —— `ItemNaming::NameItem`
            //   有默认参数，`Type.GetMethod` 会抛 AmbiguousMatchException，
            //   而它的 Prefix 本来什么都不做。）
            if (!MountOff("ZhsItemNameRewrite")) try { ZhsItemNameRewrite.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 物品改名重写挂载失败：" + e); }

            // ★ 版本播报：往**消息栏**发一条自带版本号的记录。
            //   为什么：多次出现「截图来自旧版本」导致重复排查（2026-10-07 三次）。
            //   ⇒ 让存档的消息栏永久留下版本号，我可离线读取核对。
            try { AnnounceVersion(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 版本播报失败：" + e); }

            // ★ 诊断开关：`_zhs_strdebug_on.txt` ⇒ 打开 `Strings.DebugEnabled`
            //   （查明：**游戏自己没有开关**，唯一写入点是它的静态构造器；详见方法注释）
            try { ApplyStringDebugSwitch(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 字符串调试开关失败：" + e); }

                        // ★【已回滚 1.24.1】墓碑排版修正 —— 会吃掉碑文，停用
            // // ★ 墓碑排版修正：按**显示宽度**（CJK=2）重排
            // //   游戏原实现 `PostProcessors.Tombstone` 按**字符数**算填充，
            // //   中文 1 字占 2 列 ⇒ 框被撑破。
            // try { ZhsTombstoneLayoutPatch.TryApply(); }
            // catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 墓碑排版修正失败：" + e); }

            if (!MountOff("MarkovTextPatch")) try { MarkovTextPatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 书籍正文补丁失败：" + e); }

            if (!MountOff("MarkovTitlePatch")) try { MarkovTitlePatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 随机书籍标题补丁失败：" + e); }

            // ★ 伊什纳家族遗物的坐标线索：**必须**与随机文本改造配套。
            //   那把枪的位置只能靠马尔科夫随机文本得知，我们换掉了生成器
            //   ⇒ 线索在 ZhsIsnerCluePatch 里换回带真实坐标的中文句。
            //   补丁是 Attribute 式（[HarmonyPatch] + [HarmonyTargetMethod]），
            //   程序集加载时自动安装；这行只做一次自检与日志。
            try { ZhsIsnerClueSelfCheck.Run(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 伊什纳线索自检失败：" + e); }

            // 运行期追踪（可选）：模组目录里放了 _zhs_trace_on.txt 才会启动
            // ★ 最先挂角色创建信息过滤：角色创建界面早于 bootGame 出现，
            //   必须尽早挂上，否则玩家第一次打开时英文还没被过滤掉。
            // （角色创建的英文过滤已改为 Attribute 式补丁，在程序集加载时自动安装，

            try
            {
                string modDir = FindModDirectory();
                ZhsTrace.SetModDirectory(modDir);
                ZhsTrace.Install();
                // 玩家行动文本探针（部件接管）：常态化开启，只在发现未汉化文本时写文件
            // ↓↓ 发布版：探针 ZhsJournalProbe 已撤（开发分支保留） ↓↓
#if false
                ZhsJournalProbe.SetModDirectory(modDir);
#endif
            // ↑↑ 发布版：探针 ZhsJournalProbe 已撤 ↑↑
            // ↓↓ 发布版：探针 ZhsJournalProbe 已撤（开发分支保留） ↓↓
#if false
                ZhsJournalProbe.Install();
#endif
            // ↑↑ 发布版：探针 ZhsJournalProbe 已撤 ↑↑

                // ★ 烹饪调用链追踪（按需）：模组目录放 _zhs_cooktrace_on.txt 才启用。
                //   开关注入 ZhsCookTrace.cs，输出 _zhs_cooktrace.log。
            // ↓↓ 发布版：探针 ZhsCookTrace 已撤（开发分支保留） ↓↓
#if false
                ZhsCookTrace.SetModDirectory(modDir);
#endif
            // ↑↑ 发布版：探针 ZhsCookTrace 已撤 ↑↑
            // ↓↓ 发布版：探针 ZhsCookTrace 已撤（开发分支保留） ↓↓
#if false
                ZhsCookTrace.Install();
#endif
            // ↑↑ 发布版：探针 ZhsCookTrace 已撤 ↑↑

                // ★ message log 入口探针（按需）：模组目录放 _zhs_msgtrace_on.txt。
                //   在 XRL.Messages.MessageQueue.AddPlayerMessage 记录文本 + 调用栈。
            // ↓↓ 发布版：探针 ZhsMsgTrace 已撤（开发分支保留） ↓↓
#if false
                ZhsMsgTrace.SetModDirectory(modDir);
#endif
            // ↑↑ 发布版：探针 ZhsMsgTrace 已撤 ↑↑
            // ↓↓ 发布版：探针 ZhsMsgTrace 已撤（开发分支保留） ↓↓
#if false
                ZhsMsgTrace.Install();
#endif
            // ↑↑ 发布版：探针 ZhsMsgTrace 已撤 ↑↑

                // ★ XML 加载顺序探针（诊断，按需）：模组目录放 _zhs_xmlload_on.txt。
                //   查「模组 DisplayName 为何没覆盖基础蓝图」——只剩加载顺序这一个未知量。
            // ↓↓ 发布版：探针 ZhsXmlLoadProbe 已撤（开发分支保留） ↓↓
#if false
                ZhsXmlLoadProbe.SetModDirectory(modDir);
#endif
            // ↑↑ 发布版：探针 ZhsXmlLoadProbe 已撤 ↑↑
            // ↓↓ 发布版：探针 ZhsXmlLoadProbe 已撤（开发分支保留） ↓↓
#if false
                ZhsXmlLoadProbe.Install();
#endif
            // ↑↑ 发布版：探针 ZhsXmlLoadProbe 已撤 ↑↑
                // 苏丹史词池接管（HistoricEvent 系统）
                ZhsHistorySpice.SetModDirectory(modDir);
                // ★ 2.16.22：电力读数块（`Power available:` 等）—— 整块读取/替换/写回
                ZhsPowerReadoutPatch.TryApply();
                ZhsHistorySpicePatch.TryApply();
                // 代词接管（Genders.xml / PronounSets.xml 的 IPronounProvider 路径）
                // ⚠ 0.40 起**停用**：实测这条路只覆盖到 PronounSet/初始化扫表，
                //   对历史文本里的代词无效（代词是世界生成时写进事件属性的）。
                //   保留代码备查，但不再挂载，避免在世界生成路径上引入风险。
                // ZhsPronounPatch.TryApply();
                // ★ 实体属性写入接管：代词在世界生成时被 InitializeSultan 写进
                //   HistoricEvent.entityProperties，这里是唯一有效的落点（保留）
                ZhsEntityPropPatch.ModDirHint = modDir;

                ZhsEntityPropPatch.TryApply();
                // 角色创建信息面板：剔除与中文并存的英文残留（extrainfo 是追加语义，
                // XML 去不掉本体条目，只能在返回时过滤）
                ZhsChargenInfoFilter.SetModDirectory(modDir);   // 只为读取开关文件
                // 变异汉化（Harmony 就地替换名称与说明）——只为读取开关文件
                try
                {
                    if (System.IO.File.Exists(System.IO.Path.Combine(modDir, "_zhs_mutation_off.txt")))
                    {
                        ZhsMutationZh.Disabled = true;
                        UnityEngine.Debug.Log("[QudZhsCookbook] 变异汉化：开关文件存在，已停用");
                    }
                    if (System.IO.File.Exists(System.IO.Path.Combine(modDir, "_zhs_textfilter_off.txt")))
                    {
                        ZhsTextFilter.Disabled = true;
                        UnityEngine.Debug.Log("[QudZhsCookbook] 文本过滤：开关文件存在，已停用");
                    }
                    // 动词片段汉化开关
                    if (System.IO.File.Exists(System.IO.Path.Combine(modDir, "_zhs_verb_off.txt")))
                    {
                        ZhsVerbZh.Disabled = true;
                        UnityEngine.Debug.Log("[QudZhsCookbook] 动词汉化：开关文件存在，已停用");
                    }
                    // 冠词汉化开关
                    if (System.IO.File.Exists(System.IO.Path.Combine(modDir, "_zhs_article_off.txt")))
                    {
                        ZhsArticleFix.Disabled = true;
                        UnityEngine.Debug.Log("[QudZhsCookbook] 冠词汉化：开关文件存在，已停用");
                    }
                    // ★ 2.16.127 单位词替换开关（`rounds` ⇒ 回合）
                    if (System.IO.File.Exists(System.IO.Path.Combine(modDir, "_zhs_units_off.txt")))
                    {
                        ZhsUnits.Disabled = true;
                        UnityEngine.Debug.Log("[QudZhsCookbook] 单位词替换：开关文件存在，已停用");
                    }
                    // 换行吞字修补策略：读 _zhs_nlfix.txt（内容 A / B / C / off）
                    try
                    {
                        string np = System.IO.Path.Combine(modDir, "_zhs_nlfix.txt");
                        if (System.IO.File.Exists(np))
                        {
                            string mode = (System.IO.File.ReadAllText(np) ?? "").Trim().ToUpperInvariant();
                            if (mode == "OFF" || mode == "0")
                            {
                                ZhsNewlineFix.Disabled = true;
                                UnityEngine.Debug.Log("[QudZhsCookbook] 换行修补：已由开关文件停用");
                            }
                            else if (mode == "A" || mode == "B" || mode == "C")
                            {
                                ZhsNewlineFix.Mode = mode;
                                UnityEngine.Debug.Log("[QudZhsCookbook] 换行修补策略 = " + mode);
                            }
                        }
                    }
                    catch { }

            // ★ 词池合并修正：HistorySpice 的模组合并是**追加**语义，
            //   导致运行时词池「基础英文 + 我方中文」各占一半
            //   （实证见 Local\HistorySpice.jsonc：pinchOf 16 条、cookTemplate 8 条）。
            //   这里在 HistoricSpice.Init() 之后裁掉中英混杂数组的英文前缀。
            if (!MountOff("ZhsSpiceMergeFix")) try { ZhsSpiceMergeFix.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 词池合并修正失败：" + e); }

            // ★★ 2026-10-16（F3）：**死亡界面那行 `You were killed by ____.`**
            //   反编译取证：整句在 `XRL.World.Parts.Physics::ProcessTakeDamage` 里
            //   由 **C# `Append` 链拼装**（`"You "`/`"were "`/动词/`" by "`/`an()`/`"."`）
            //   ⇒ 字符串表里**没有完整句可查** ⇒ 只能按句式改写。
            //   ★ `CheckHP` 里那条 `_T("DeathReason Killed By", …)` 是**死代码**
            //     （`LastDeathReason` 早被这里填满 ⇒ 永不执行）⇒ 我方 `Strings.xml:885`
            //     的中文译文从未生效。详见本补丁头部注释。
            if (!MountOff("ZhsDeathScreenPatch")) try { ZhsDeathScreenPatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 死亡界面接管失败：" + e); }

            // ★★ 2026-10-16（F7）：**场景内浮动文字**（`*stuck*` / `*critical hit*` 那种）
            //   反编译取证：`XRL.World.GameObject::ParticleText` 的 4 个重载，
            //   `Effects.Stuck::Apply` / `Parts.Combat::MeleeAttackWithWeaponInternal` 都调它，
            //   **不经过字符串表**（这是它从未被覆盖的机制级原因）。
            //   ★ 与消息栏**不共用出口**（那条走 `MessageQueue` → `EmitMessage`）。
            if (!MountOff("ZhsFloatTextPatch")) try { ZhsFloatTextPatch.TryApply(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 浮动文字接管失败：" + e); }
                }
                catch { }
                // 代词来源追踪器（临时诊断）—— 同样停用，避免多余挂钩
                // ZhsPronounTrace.SetModDirectory(modDir);
                // ZhsPronounTrace.Install();
            }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 追踪安装失败：" + e); }

            // ★ 2026-10-18（F17）：钩子顺序普查（只在 `_zhs_hooktrace_on.txt` 存在时启用）
            //   ① DumpPatchInfo：Harmony 的实际补丁顺序 → `_zhs_patchinfo.log`
            //   ② Init：运行期顺序追踪（触发串 dump）→ `_zhs_hooktrace.log`
            try { ZhsHookTrace.Init(); ZhsHookProbe.DumpPatchInfo(); } catch { }

            UnityEngine.Debug.Log("[QudZhsCookbook] === 引导完成 ===");
        }

        /// <summary>从本程序集位置反推模组目录（…\ModAssemblies\X.dll → …\Mods\QudZhsCookbook）。</summary>
        private static string FindModDirectory()
        {
            try
            {
                string asmPath = typeof(ZhsBootstrap).Assembly.Location;
                if (string.IsNullOrEmpty(asmPath)) return null;
                string gameRoot = System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(asmPath));
                if (gameRoot == null) return null;
                string mods = System.IO.Path.Combine(gameRoot, "Mods");
                if (!System.IO.Directory.Exists(mods)) return null;
                foreach (string d in System.IO.Directory.GetDirectories(mods))
                {
                    if (System.IO.File.Exists(System.IO.Path.Combine(d, "manifest.json"))
                        && System.IO.File.Exists(System.IO.Path.Combine(d, "Scripts", "ZhsTrace.cs")))
                        return d;
                }
            }
            catch { }
            return null;
        }
    /// <summary>
    /// 本模组版本 —— ★ **只用于版本戳与日志播报**，与运行逻辑无关。
    ///
    /// ⚠️ 2026-10-09 踩坑：装机脚本一直只改 `manifest.json`，**没同步这个常量**，
    ///   于是 2.16.42 / 2.16.43 跑起来日志里仍写「2.16.41」
    ///   ⇒ 差点被误判成「游戏跑的是旧版本」。
    ///   ⇒ 装机脚本改版本号时**两处一起改**（manifest.json + 本常量）。
    ///
    /// ★★ 2026-10-12 **正式版**：自 1.0.0.0 起改用**四段式**，位序自左向右递减：
    ///   `A.B.C.D`
    ///     A = 主版本（架构级 / 不兼容变更）
    ///     B = **大修改**        ← 改动大、涉及面广（如整批重译、机制改造）
    ///     C = **换译名且涉及硬编码** ← 改了译名，且要动 C# / 字符串表键等硬编码
    ///     D = **仅 XML 文本改动**    ← 只改 XML 里的译文（换词、换译名、修错字）
    ///   ⇒ 口诀：**越靠右 = 改动越轻**；「动到 C#」就往前挪一位。
    /// ⚠ 两处必须一起改（manifest.json + 本常量），见上。
    /// </summary>
    internal const string ModVersion = "1.0.0.1";

    /// <summary>把版本号写进消息栏（存档可见，便于离线核对）。</summary>
        private const string VersionStampName = "_zhs_last_run.txt";

    /// <summary>
    /// 版本播报。
    ///
    /// ★ 主通道 = **文件** `_zhs_last_run.txt`（模组目录）
    ///   实测「写消息栏」这条路**不可靠**（日志说写了，但存档里一个字都没有 ——
    ///   多半是时机太早、MessageQueue 尚未接上）。文件必定成功且离线可读。
    ///   ⇒ 排查「截图来自哪一版」时**以这个文件为准**。
    /// 次通道 = 消息栏（能显示就显示，不能也不影响判断）。
    /// </summary>
        // ══════════════════════════════════════════════════════════════════════
        //  ★★ 诊断开关：`_zhs_strdebug_on.txt` ⇒ `XRL.Language.Strings.DebugEnabled = true`
        // ══════════════════════════════════════════════════════════════════════
        //  【怎么查清的 · 2026-10-15】用 `_tools\fieldscan`（自写 IL 扫描器）扫
        //  `Assembly-CSharp.dll` 全量方法体，找 `stsfld/ldsfld Strings::DebugEnabled`：
        //      写：**1 处 —— `Strings::.cctor`**（就是 `= false` 那个字段初始化器）
        //      读：8 处（下）
        //  ⇒ ★ **游戏自己从不把它置 true**：不是 `[OptionFlag]`（全 DLL 唯一的
        //    `OptionDebug*` 是 `OptionDebugExampleLanguage`），也没有 wish / 调试菜单写它。
        //  ⇒ 所以「怎么开」的答案只能是：**我们自己设**（本方法）。
        //
        //  【开启后的效果（8 个读点全部核实）】
        //    · `Strings._S(ID)`            ⇒ 返回 `"_S:" + ID`
        //      `Strings._S(Context, ID)`   ⇒ 返回 `"_S:" + Context + ":" + ID`
        //    · `Strings._T(ID)`            ⇒ 返回 `"_T:" + ID`（模板）
        //      ⇒ **绕过译文表、直接显示查表键** ⇒ 界面上一眼分辨三态：
        //         带 `_S:` / `_T:` 前缀 ⇒ 该文本**走字符串表** ⇒ 若仍英文 = 缺条目（没翻译）
        //         不带前缀            ⇒ 硬编码 / XML 直接显示 / 运行时拼接（另一条排查线）
        //    · `MetricsManager.AssertLocalizationMatch` 与 `Messaging::HandleMessage`
        //      ⇒ 只是**静音两处开发期自检**（`!DebugEnabled && ActiveLanguage=="en"`），
        //        不影响任何显示逻辑 ✓
        //
        //  【⚠ 使用纪律】这是**诊断态**：所有走表文本都会变成英文键 ⇒ 不能长期开着。
        //    §57（残留状态）：用完**必须删掉标记文件**；残留体检已把本文件列入
        //    （`_tools\AGENTS-57-残留状态.md`）。
        private static void ApplyStringDebugSwitch()
        {
            string modRoot = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "AppData", "LocalLow", "Freehold Games", "CavesOfQud",
                "Mods", "QudZhsCookbook");
            string p = System.IO.Path.Combine(modRoot, "_zhs_strdebug_on.txt");
            bool on = System.IO.File.Exists(p);

            // ★ 无条件留痕（§57：开关类东西"没生效"时必须有线索）
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(modRoot, "_off_probe.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff")
                    + "  zhs_strdebug  hit=" + (on ? "YES" : "no")
                    + "  tried=" + p + "\n");
            }
            catch { }

            if (!on) return;

            // ★ 唯一写入点就是我们：静态构造器早已跑过（访问该字段必然先触发 .cctor），
            //   所以这里的赋值不会被 `= false` 覆盖。
            XRL.Language.Strings.DebugEnabled = true;
            UnityEngine.Debug.LogError("[QudZhsCookbook] ★★ 字符串调试已开启"
                + "（Strings.DebugEnabled = true）：界面文本将以 `_S:` / `_T:` + 英文查表键显示。"
                + "带前缀 = 走字符串表；不带 = 硬编码/XML/拼接。**用完请删除** " + p);
        }

    private static void AnnounceVersion()
    {
        // ① 版本戳文件（主）
        try
        {
            string dir = FindModDir();
            if (dir != null)
            {
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(dir, VersionStampName),
                    "模组版本 = " + ModVersion + "\r\n"
                    + "启动时间 = " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n",
                    new System.Text.UTF8Encoding(false));
                UnityEngine.Debug.Log("[QudZhsCookbook] 版本戳已写："
                    + System.IO.Path.Combine(dir, VersionStampName));
            }
            else
            {
                UnityEngine.Debug.LogError("[QudZhsCookbook] 版本戳：找不到模组目录");
            }
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError("[QudZhsCookbook] 版本戳写入失败：" + e);
        }

        // ② 消息栏（次）
        try
        {
            XRL.Messages.MessageQueue.AddPlayerMessage(
                "【汉化】模组版本 " + ModVersion + " 已加载", "W", false);
            UnityEngine.Debug.Log("[QudZhsCookbook] 版本播报：模组版本 "
                + ModVersion + " 已写入消息栏");
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError("[QudZhsCookbook] 版本播报异常：" + e);
        }
    }

    /// <summary>找模组目录（判据与 `ZhsTrace` 一致：含 manifest.json）。</summary>
    private static string FindModDir()
    {
        try
        {
            string low = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            // LocalApplicationData = …\AppData\Local ⇒ 上一级 + LocalLow
            string cand = System.IO.Path.GetFullPath(System.IO.Path.Combine(
                low, "..", "LocalLow", "Freehold Games", "CavesOfQud", "Mods",
                "QudZhsCookbook"));
            if (System.IO.File.Exists(System.IO.Path.Combine(cand, "manifest.json")))
                return cand;
        }
        catch { }
        return null;
    }


    }

    /// <summary>
    /// 注意：这里曾经用过 [ModuleInitializer]，但**本模组编译不过**：
    ///   游戏的 Mods 编译环境（Unity/Mono 的 mscorlib）里
    ///   System.Runtime.CompilerServices.ModuleInitializerAttribute 是 internal，
    ///   报 error CS0122: 'ModuleInitializerAttribute' is inaccessible due to its protection level。
    ///   → 改用下面的 bootGame Prefix 作为"最早且可用"的钩子。
    /// </summary>
    /// <summary>
    /// 开局钩子零（**必须最先**）：QudGameBootModule.bootGame 的 **Prefix**。
    ///
    /// 为什么需要：崩溃栈与实测证实，世界/苏丹史生成发生在
    ///     XRLCore.NewGame → EmbarkInfo.bootGame → QudGameBootModule.bootGame
    ///         → EmbarkInfo.fireBootEvent → QudSpecificBootHandlersModule.handleBootEvent
    ///         → QudHistoryFactory.GenerateNewSultanHistory → InitializeSultan.Generate
    /// 而原来的三个钩子（RunGame / _Start / PlayerTurn）**全都在这之后**才跑
    /// （实测 Player.log：引导开始 = XRLCore.PlayerTurn）。
    /// 于是 InitializeSultan 在**补丁还没装**的情况下把英文代词
    /// 写进 HistoricEvent.entityProperties，文本当场渲染定稿并存档 ——
    /// 这就是代词迟迟改不动的根因（写侧 0 命中、读侧 0 命中都源于此）。
    ///
    /// 挂 bootGame 的 Prefix，保证补丁在世界生成之前就位。
    /// </summary>
    [HarmonyPatch(typeof(XRL.CharacterBuilds.Qud.QudGameBootModule))]
    [HarmonyPatch("bootGame")]
    public static class ZhsStartupPatchBootGame
    {
        // 守卫：任何解析失败都返回 false，绝不抛异常。
        // （Harmony.PatchAll 顺序执行、遇异常即中断 —— 一个补丁抛错会让后面的补丁全不生效，
        //   本模组已被 `Faction.GetFormattedName` 的歧义坑过一次。）
        [HarmonyPrepare]
        public static bool Prepare() { return ZhsBootstrap.HasMethod(typeof(XRL.CharacterBuilds.Qud.QudGameBootModule), "bootGame"); }

        [HarmonyPrefix]
        public static void Prefix()
        {
            try { ZhsBootstrap.Init("QudGameBootModule.bootGame(Prefix)"); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 引导异常：" + e); }
        }
    }

    /// <summary>开局钩子一：XRLCore.RunGame（读档/开局后进入游戏循环）。</summary>
    [HarmonyPatch(typeof(XRL.Core.XRLCore))]
    [HarmonyPatch("RunGame")]
    public static class ZhsStartupPatchRunGame
    {
        [HarmonyPrepare]
        public static bool Prepare() { return ZhsBootstrap.HasMethod(typeof(XRL.Core.XRLCore), "RunGame"); }

        [HarmonyPostfix]
        public static void Postfix()
        {
            try { ZhsBootstrap.Init("XRLCore.RunGame"); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 引导异常：" + e); }
        }
    }

    /// <summary>开局钩子二：XRLCore._Start（游戏主线程起点）。两个钩子互为备份。</summary>
    [HarmonyPatch(typeof(XRL.Core.XRLCore))]
    [HarmonyPatch("_Start")]
    public static class ZhsStartupPatchStart
    {
        [HarmonyPrepare]
        public static bool Prepare() { return ZhsBootstrap.HasMethod(typeof(XRL.Core.XRLCore), "_Start"); }

        [HarmonyPostfix]
        public static void Postfix()
        {
            try { ZhsBootstrap.Init("XRLCore._Start"); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 引导异常：" + e); }
        }
    }
    /// <summary>
    /// 开局钩子三（**最可靠**）：XRLCore.PlayerTurn —— 每个回合都会被调用。
    /// 为什么需要它：RunGame / _Start 都在进程早期执行，而模组的补丁是在 build 阶段（约开局后几秒）
    /// 才挂上的 —— 谁先谁后是竞态。实测出现过"上一次赶上了、这一次没赶上"
    /// （Player.log 里有 MarkovBook 的报错却完全没有我们的日志 = 补丁挂上时开局方法早跑完了）。
    /// 挂在每回合调用的方法上，只要玩家一动手就必定引导；Init 内部有幂等锁，重复调用无代价。
    /// </summary>
    [HarmonyPatch(typeof(XRL.Core.XRLCore))]
    [HarmonyPatch("PlayerTurn")]
    public static class ZhsStartupPatchPlayerTurn
    {
        [HarmonyPrepare]
        public static bool Prepare() { return ZhsBootstrap.HasMethod(typeof(XRL.Core.XRLCore), "PlayerTurn"); }

        [HarmonyPostfix]
        public static void Postfix()
        {
            try { ZhsBootstrap.Init("XRLCore.PlayerTurn"); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 引导异常：" + e); }
        }

    }
}
