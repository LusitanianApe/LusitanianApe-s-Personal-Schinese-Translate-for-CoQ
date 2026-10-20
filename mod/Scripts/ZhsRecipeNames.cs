// 食谱正文（菜名）的中文生成。
//
// 结论依据：
//   * 食谱书页的正文主体是「菜谱条目」，其名字来自 CookingRecipe.GenerateRecipeName()，
//     而它的模板在字符串表里是：
//         Context="CookingRecipe Name Generator"
//         ID="=spice:cooking.recipeNames.$ingredientCount.!random|title="
//     即从 HistorySpice.jsonc 的 cooking.recipeNames.* 里随机抽模板（foods / foodAdj /
//     categorizedFoods / ingredient1NounPhrase / preposition / suffix …），拼出英文菜名。
//   * 显示时的框架是 Context="CookingRecipe GetDisplayName" → "=chefName|'s= =displayName="，
//     所以只要把菜名换成中文，厨师前缀那层交给 Strings.xml 的框架翻译即可。
//
// 生成策略：中文菜名短且高度结构化，用「做法 + 主料 + 菜品」词池组合比字级马尔科夫更稳。
// 关键性质——**确定性**：以英文原名做稳定哈希来播种，同一个食谱无论被调用多少次，
// 都会得到同一个中文名（避免名字每次刷新都变）。注意不能用 string.GetHashCode()，
// .NET Core 下它每进程随机化。

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsRecipeNames
    {
        private static readonly string[] Methods =
        {
            "香烤", "盐渍", "烟熏", "慢炖", "油炸", "蜜炙", "醋腌", "风干",
            "火焙", "冷拌", "生切", "爆炒", "炭烧", "闷煮", "霜冻", "日晒"
        };

        private static readonly string[] Mains =
        {
            "仙人掌", "浆果", "孢子", "蜥肉", "甲虫", "盐根", "蘑菇", "麦芽",
            "鱼干", "蜜", "铬屑", "骨粉", "苔藓", "藤茎", "块根", "蟹肉",
            "鸟卵", "蛇段", "沼虾", "锈菌", "沙葱", "苦叶"
        };

        private static readonly string[] Dishes =
        {
            "汤", "羹", "饼", "串", "糊", "饭", "面", "粥", "脯", "酱", "卷", "糕"
        };

        /// <summary>把英文菜名换成中文；已含中文（说明已被本模组处理过）则原样返回，保证幂等。</summary>
        public static string Translate(string english)
        {
            if (string.IsNullOrEmpty(english)) return english;
            if (Markup.HasCjk(english)) return english;

            List<string> wrappers = new List<string>();
            string inner = Markup.Unwrap(english, wrappers);

            // 若处于 GetDisplayName 阶段，文本形如 "Chef Banks's fried cactus"：保留厨师前缀
            string chef = null;
            Match cm = Regex.Match(inner, @"^Chef\s+(.+?)[’']s\s+(.+)$");
            if (cm.Success)
            {
                chef = cm.Groups[1].Value.Trim();
                inner = cm.Groups[2].Value;
            }

            string name = Compose(StableHash(inner));
            string body = string.IsNullOrEmpty(chef) ? name : chef + "师傅的" + name;
            return Markup.Rewrap(body, wrappers);
        }

        private static string Compose(int seed)
        {
            Random rnd = new Random(seed);
            switch (rnd.Next(3))
            {
                case 0:
                    return Methods[rnd.Next(Methods.Length)] + Mains[rnd.Next(Mains.Length)];
                case 1:
                    return Mains[rnd.Next(Mains.Length)] + Dishes[rnd.Next(Dishes.Length)];
                default:
                    return Methods[rnd.Next(Methods.Length)]
                         + Mains[rnd.Next(Mains.Length)]
                         + Dishes[rnd.Next(Dishes.Length)];
            }
        }

        /// <summary>FNV-1a，跨进程稳定（不要用 string.GetHashCode）。</summary>
        private static int StableHash(string s)
        {
            unchecked
            {
                int h = (int)2166136261;
                for (int i = 0; i < s.Length; i++)
                {
                    h = (h ^ s[i]) * 16777619;
                }
                return h & 0x7fffffff;
            }
        }

        // ────────────────────────────────────────────────────────────────
        //  ★ 2026-10-18：**固定菜谱**（`XRL.World.Skills.Cooking.<Dish>`）
        //
        //  反编译实证：这 10 个类**各自 override** `GetDisplayName()/GetDescription()`，
        //  返回硬编码 `{{W|Apple Matz}}` / `"+10-15 Heat Resist\nReflect 3-4%…"`。
        //  基类的 Postfix 拦不到虚方法重写（虚拟分派直接进重写体）⇒ 必须逐个类挂。
        //
        //  ★ 为什么**不能**走 `Translate()`：那是给生成菜名用的「随机组合」，
        //    拿它翻固定菜名会把 "Apple Matz" 变成随机中文字。⇒ 这里只做**精确表**，
        //    查不到就原样放过（不半译）。
        // ────────────────────────────────────────────────────────────────

        /// <summary>固定菜名（键含 `{{W|…}}` 标记，与原字面量逐字一致）。</summary>
        public static readonly Dictionary<string, string> FixedNames =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "{{W|Apple Matz}}",           "{{W|苹果玛兹}}" },
            { "{{W|Bone Babka}}",           "{{W|骨巴布卡}}" },
            { "{{W|Cloaca Surprise}}",      "{{W|泄殖腔惊喜}}" },
            { "{{W|Crystal Delight}}",      "{{W|晶之悦}}" },
            { "{{W|Goat in Sweet Leaf}}",   "{{W|甜叶山羊肉}}" },
            { "{{W|Hot and Spiny}}",        "{{W|香辣多刺}}" },
            { "{{W|Mah Lah Soup}}",         "{{W|玛拉赫汤}}" },
            { "{{W|Mulled Mushroom Cider}}","{{W|热香菌苹果酒}}" },
            { "{{W|The Porridge}}",         "{{W|那碗粥}}" },
            { "{{W|Tongue and Cheek}}",     "{{W|舌颊双拼}}" },
        };

        /// <summary>固定菜谱的**效果说明**（整串精确匹配；换行是字面 `\n`）。</summary>
        public static readonly Dictionary<string, string> FixedDescs =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            {
                "+10-15 Heat Resist\nReflect 3-4% damage back at your attackers, rounded up.",
                "热抗性 +10-15\n将 3-4% 的伤害反弹给攻击者，向上取整。"
            },
            {
                "+3 to saves vs. disease\nWhenever you eat an unfermented yuckwheat stem, you release an electrical discharge per Electrical Generation at rank 5-6.",
                "对抗疾病的豁免 +3\n每当你食用未发酵的辟瘟麦茎时，你会释放一次电击，效果等同 5-6 级的「电能生成」。"
            },
            {
                "+4-5 Quickness\n+8-12 to saves vs. bleeding\n75% chance that itchy skin doesn't develop into a fungal infection.",
                "迅捷 +4-5\n对抗流血的豁免 +8-12\n75% 几率使发痒的皮肤不发展为真菌感染。"
            },
            {
                "Can use Burrowing Claws at rank 5-6. If you already have Burrowing Claws, it's enhanced by 3-4 ranks.\nWhenever you identify an artifact, you gain +6 AV for 50 turns.",
                "可使用 5-6 级的「钻地爪」。若你已拥有钻地爪，其等级提高 3-4 级。\n每当你鉴定一件器物时，获得 +6 护甲值，持续 50 回合。"
            },
            {
                "Can use Psychometry at level 4-5.\nWhenever you take damage, there's a 12-15% chance you teleport all creatures surrounding you.",
                "可使用 4-5 级的「心灵测定」。\n每当你受到伤害时，有 12-15% 概率把周围的所有生物传送走。"
            },
            {
                "Can use Sticky Tongue at rank 4-5.\nWhenever you jump, you beguile a creature as per Beguiling at rank 7-8 for the duration of this effect.",
                "可使用 4-5 级的「黏舌」。\n每当你跳跃时，你按 7-8 级「魅惑」的效果蛊惑一个生物，持续至本效果结束。"
            },
        };

        /// <summary>固定菜名/说明：命中返回中文，未命中返回 null（**绝不**随机生成）。</summary>
        public static string Fixed(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            string zh;
            if (FixedNames.TryGetValue(s, out zh)) return zh;
            if (FixedDescs.TryGetValue(s, out zh)) return zh;
            return null;
        }
    }
}
