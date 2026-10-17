# -*- coding: utf-8 -*-
"""
★★ 碎片字典（全局兜底条目）—— 第一批

> 机制（已实证，见 `_tools\\_verify_textlookup.py`）：
>   `XRL.Language.Strings::TryGetString(Context, ID)` 的三段顺序是
>       ① `ContextStrings(Context)[ID]`
>       ② **`ContextStrings(null)[ID]`**     ← 全局表（就是「不带 Context」的条目）
>       ③ `Result = ID`（回退英文）
>   ⇒ 写 `<string ID="英文原句">中文</string>`（**不带 Context**）能兜住
>     **所有**上下文里的同一句英文 —— 官方自己也这么干（ExampleLanguage 里 54 条
>     "像句子"的全局 ID）
>   ⇒ 这就是 DFCN 的"片段级 / 模板层"翻译表在 CoQ 这边的等价物。

用法：
    python _tools\\_zh_fragments.py            # 干跑：只报告将新增/已存在
    python _tools\\_zh_fragments.py --apply    # 真写（先备份 Strings.xml 到 _backup\\）
    python _tools\\_zh_fragments.py --apply --fresh   # 从备份重来（丢弃上次写入的这批）
"""
import io
import os
import re
import shutil
import sys
import time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
STRINGS = os.path.join(ROOT, "mods", "QudZhsCookbook", "Strings.xml")
BACKUP = os.path.join(ROOT, "_backup", "frag_" + time.strftime("%Y%m%d-%H%M%S"))
MARK = "<!-- ★ 碎片字典（全局兜底）· 2026-10-17 · 生成器 _tools\\_zh_fragments.py -->"

# ★★ 已证明**走字符串表**的条目（`_S/_T` 的实参；分类工具 `_tools\\_frag_reclass.py`）
#    只有这几条写 `Strings.xml` 会生效；其余必须走补丁侧的碎片字典。
SAFE_KEYS = {
    "...",
    "DynamicQuest you recive an item",
    "You cannot do that here.",
}

# ═══════════════════════════════════════════════════════════════════════════
#  译文表：英文原句（= ID） → 中文
#  ★ 规则：`=槽=` / `{{…}}` / `&#xA;` 一律**原样保留**，只动英文词
#  ★ 判据：这句英文**离开上下文也能翻译** ⇒ 才放全局表；
#          同一英文在不同语境要不同中文 ⇒ 必须带 Context（本表不收）
# ═══════════════════════════════════════════════════════════════════════════
TABLE = {
    # ── A. 官方模板确认存在的（游戏主动 `_S` 查表，填了立刻生效）──
    "Attempting to generate reserved zone.": "正在生成预留区域。",
    "Attempting to thaw reserved zone.": "正在解冻预留区域。",
    "...": "……",

    # ── B. 战斗/状态消息（`DidX` / `XDidY` 家族，玩家高频可见）──
    "You are paralyzed!": "你瘫痪了！",
    "You are startled!": "你被吓了一跳！",
    "You feel drowsy.": "你觉得昏昏欲睡。",
    "You feel uneasy.": "你感到不安。",
    "You feel feverish.": "你在发热。",
    "You feel less feverish.": "你的发热减轻了。",
    "You no longer feel ill.": "你不再感到不适。",
    "You feel increasingly unstable.": "你感到越来越不稳定。",
    "You regain your bearings.": "你重新辨明了方向。",
    "You return to the ground.": "你回到了地面。",
    "You do not budge.": "你纹丝不动。",
    "You are lost!": "你迷路了！",
    "You are already burrowed.": "你已经钻入地下了。",
    "You are not enclosed.": "你没有被围住。",
    "You can't be mutated.": "你无法被变异。",
    "You can't imprint yet.": "你还不能铭印。",
    "You can't recoil yet.": "你还不能回溯。",
    "You can't deploy there!": "你不能在那里展开！",
    "You cannot do that here.": "你不能在这里那么做。",
    "You don't have a tongue!": "你没有舌头！",
    "You have no inventory!": "你的物品栏是空的！",
    "You don't have any": "你没有",
    "Your wish is my command!": "遵命！",
    "Your bilge sphincter is missing.": "你的舱底括约肌缺失。",
    "Your healing is interrupted!": "你的治疗被打断了！",
    "Your heart rate slows again.": "你的心率重新慢了下来。",
    "Your body flushes with adrenaline!": "肾上腺素涌遍你全身！",
    "Your cloning capacity is refreshed.": "你的克隆容量已恢复。",
    "Your psyche is too exhausted.": "你的精神力已经枯竭。",
    "Your lunge is interrupted.": "你的扑击被打断了。",
    "The limb rejects the infection!": "那条肢体排斥了感染！",
    "The soothing tingle fades.": "那阵抚慰的酥麻消退了。",
    "The experience is fleeting.": "这种感受转瞬即逝。",
    "The gates swing wide.": "大门敞开了。",
    "The sarcophagus is inert.": "石棺毫无反应。",
    "The sprayer head won't move.": "喷头纹丝不动。",
    "The svardym egg hatches.": "斯瓦德姆的卵孵化了。",
    "The svardym eggs hatch.": "斯瓦德姆的卵孵化了。",
    "There are hostiles nearby!": "附近有敌人！",
    "There's nothing there to hobble.": "那里没有可以致跛的目标。",
    "There's nothing there to shank.": "那里没有可以捅刺的目标。",
    "There's no magnetic containment inside": "里面没有磁约束",
    "Something prevents you from dominating": "有什么在阻止你支配",
    "No problems found.": "未发现问题。",
    "Not found.": "未找到。",
    "Zone has no parts.": "区域没有任何部件。",
    "It is already daytime.": "现在已经是白天了。",
    "It's miniature you.": "那是迷你版的你。",
    "It's shadow you.": "那是你的影子。",
    "You ended the game.": "你结束了这局游戏。",
    "You start to prowl.": "你开始潜行。",
    "You stop meditating.": "你停止了冥想。",
    "You climb into the sarcophagus.": "你爬进了石棺。",
    "You crossed into Brightsheol.": "你跨入了光灵界（Brightsheol）。",
    "You journeyed to Bethesda Susa.": "你跋涉到了贝塞斯达·苏萨。",
    "You journeyed to Golgotha.": "你跋涉到了各各他。",
    "You journeyed to Grit Gate.": "你跋涉到了砂砾门。",
    "You journeyed to Kyakukya.": "你跋涉到了基亚库基亚。",
    "You journeyed to Omonporch.": "你跋涉到了奥蒙波奇。",
    "You journeyed to Red Rock.": "你跋涉到了红岩。",
    "You feel shaken and infirm.": "你感到身心俱损。",
    "You have completed the quest": "你完成了任务",
    "You have failed the quest": "你任务失败了",
    "You have rapidly advanced": "你迅速进阶了",
    "You are promoted to the": "你被提升为",
    "You have increased your": "你提升了自己的",
    "You are able to collect": "你能够采集",
    "You have no supplies that": "你没有可用于",
    "You muse over the secret": "你思索着那个秘密",
    "You abide the memory of": "你承载着这段记忆：",
    "You don't have any": "你没有",
    "You fail to extricate": "你没能挣脱",
    "You are puzzled by": "你被……弄糊涂了：",
    "You think you broke": "你觉得自己弄坏了",
    "You can't aggressively lunge through": "你不能强行扑击穿过",
    "Your lunge passes through": "你的扑击穿过了",
    "Your mind was stranded inside": "你的意识被困在了",

    # ── C. 答对/答错的弹窗与选项 ──
    "Choose a reward": "选择奖励",
    "Pick a filter to change": "选择要更改的过滤器",
    "Select your maker's mark.": "选择你的匠人印记。",
    "Select a destination": "选择目的地",
    "Attack in what direction?": "朝哪个方向攻击？",
    "Move in what direction?": "朝哪个方向移动？",
    "Launch spaceship and end game?": "发射飞船并结束游戏？",
    "That was awful!": "太糟糕了！",
    "No valid targets for": "没有合法目标：",
    "No valid targets in that square.": "那一格没有合法目标。",
    "no usable base genders": "没有可用的基础性别",
    "Unable to": "无法",

    # ── D. 液体 / 环境 / 物件 ──
    "pours out all over you!": "全部倾倒在你身上！",
    "a pool of stinking liquid": "一滩发臭的液体",
    "a stream of corrosive acid": "一股腐蚀性酸液",
    "a cloud of spores": "一团孢子云",
    "a swatch of frost webs": "一片霜网",
    "a berserk fury": "一阵狂怒",
    "a weakness in your defenses.": "你防御中的一处弱点。",
    "into a block of shale": "变成一块页岩",
    "into cardiac arrest": "陷入心脏骤停",
    "into another cardiac arrest": "再次陷入心脏骤停",
    "into a blood frenzy": "陷入嗜血狂暴",
    "into their fold and taught": "纳入门下并传授了",
    "to fungi across Qud": "传给卡德各处的真菌",
    "to rapidly advance!": "迅速进阶！",
    "to rapidly advance.": "迅速进阶。",
    "and gain some insight.": "并获得一些洞见。",
    "and cannot be poured from.": "且无法从中倒出。",
    "and cannot be poured into.": "且无法倒入其中。",
    "and neither BaseObject nor ExcludeFromDynamicEncounters": "BaseObject 与 ExcludeFromDynamicEncounters 都没有",
    "and holds you in place": "并把你定在原地",
    "with life and arranging": "赋予生命，并安排",
    "with understanding of": "带着对……的理解",
    "with a flash": "闪光一闪",
    "with life.": "带着生命。",
    "with life": "带着生命",
    "under the pressure of normality": "在常态的压力下",
    "until your guard is down.": "直到你放松警惕。",
    "that you are enclosed by.": "你所处的围合结构。",
    "no longer your follower.": "不再是你的追随者。",
    "no longer lost.": "不再迷路。",
    "no longer poisoned": "不再中毒",
    "no duplicate recipes found": "未发现重复的配方",
    "not dismemberable.": "无法被肢解。",
    "not budge.": "纹丝不动。",
    "not seem operational.": "似乎无法运转。",
    "now your follower.": "现在是你的追随者。",
    "loosens and breaks away.": "松脱并脱落了。",
    "mandated association with trolls in": "与巨魔的强制关联",
    "method of construction.": "的建造方式。",
    "may be ineffective unless stocked.": "若不装填可能无效。",
    "seem to understand you.": "似乎能听懂你的话。",
    "plans to return to it!": "打算回到那里！",
    "who can see this item.": "能看到这件物品的人。",
    "you can store?": "你能存放吗？",
    "your shield slam.": "你的盾击。",
    "your life drain!": "你的生命汲取！",
    "your wound is an illusion": "你的伤口是幻觉",
    "their secrets.": "它们的秘密。",
    "of fresh water.": "淡水。",
    "of electrical charge": "电荷",
    "units of electrical charge": "单位电荷",
    "of anyone who reads": "任何读到",
    "to anyone who reads": "给任何读到",
    "of your cells to supply": "你的细胞来供给",
    "brain begins to hemorrhage.": "大脑开始出血。",
    "core begins to leak.": "核心开始泄漏。",
    "nose begins to bleed.": "鼻子开始流血。",
    "blade in the air.": "空中的刀刃。",
    "from player body.": "从玩家身上。",
    "from the ground": "从地面",
    "on the ground": "在地上",
    "to the ground.": "倒地。",
    "to the ground": "倒地",
    "to confirm.": "确认。",
    "to confirm": "确认",
    "is in disarray.": "陷入混乱。",
    "shell glimmers with unseen light": "外壳闪着不可见的光",
    "overwhelmed with terror": "被恐惧压倒",
    "a sip of fresh water": "一口淡水",
    "a definitely unexpected three": "确实是意外的三",
    "a vast freezing ray from": "一道巨大的冰冻射线自",
    "an electromagnetic pulse": "一道电磁脉冲",
    "spacetime in the local region": "局部区域的时空",
    "units of electrical charge": "单位电荷",
    "blown forcefully back by": "被强力吹回，来自",
    "blown into the air by": "被吹到空中，来自",
    "being blown back by": "被吹回，来自",
    "from colliding with": "因与……碰撞",
    "forced to the surface": "被迫浮出表面",
    "away from the danger": "远离危险",
    "away from": "远离",
    "before your eyes": "在你眼前",
    "back into hibernation": "重新进入休眠",
    "Deep in the wilds of": "在……的荒野深处",
    "While traveling around": "在……一带旅行时",
    "While wandering around": "在……一带游荡时",
    "While traveling in": "在……旅行时",
    "change their opinion of": "改变他们对……的看法",
    "the secret to becoming": "成为……的秘诀",
    "opponent's reaction to": "对手对……的反应",
    "found with no context": "找到时缺少上下文",
    "sleep mode by emergency interrupt": "因紧急中断而进入休眠模式",
    "DynamicQuest you recive an item": "动态任务：你收到了一件物品",
    "You'll have to pony up": "你得掏钱",
    "will have to pony up": "得掏钱",
    "to take advantage of": "以利用",
    "anything useful from": "从……得到任何有用的东西",
    "of anyone who reads": "任何读到的人",
    "with understanding of": "带着对……的理解",
    "The polygel morphs into another": "聚合胶变成了另一种",
    "A sewage eel wraps itself": "一条污水鳗缠住了自己",
    "There's nothing there to shank.": "那里没有可以捅刺的目标。",
    "Do you want to empty": "你想倒空",
    "Error when disarming": "卸下武器时出错",
    "Pour on self message": "倒在自己身上的消息",
    "SunderMind no target": "裂脑无目标",
    "Failed to rebuild body as": "重建躯体失败，因为",
    "have a chat with": "聊一聊",
    "no object in ObjectLeavingCellEvent": "ObjectLeavingCellEvent 里没有对象",
    "there isn't a body in": "里面没有尸体",
    "befriened fungi and contracted": "与真菌为友并缔结",
    "and dissolved the Klanq on": "并溶解了上面的克兰克",
    "condition this does little": "这种情况下这没什么用",
    "is solid and has RenderLayer": "是实心的且有 RenderLayer",
    "no thermal insulation": "没有隔热层",
}

# ★ 2026-10-17 组合句组件（水之仪式家族）——取证见 _tools\_fill_combo_1017.py 头注
# ★ 2026-10-17：`Because they X` 的**大写**变体（实际串首字母大写）
COMBO_CAP = {
    "Because they admire ": "因为他们钦佩",
    "Because they despise ": "因为他们鄙视",
    "Because they dislike ": "因为他们不喜欢",
    "Because they love ": "因为他们喜爱",
    "Because they regard ": "因为他们看待",
}

# 2026-10-17：日志地点里的商队名（此前一直没落盘）
COMBO0 = {
    "a dromad caravan": "一支驼人商队",
    "dromad caravan": "驼人商队",
}

COMBO = {
    " and begin the water ritual.": "，并开始了水之仪式。",
    "You share your ": "你分享了自己的",
    "and begin the water ritual.": "并开始了水之仪式。",
    "because they admire ": "因为他们钦佩",
    "because they despise ": "因为他们鄙视",
    "because they dislike ": "因为他们不喜欢",
    "because they love ": "因为他们喜爱",
    "because they regard ": "因为他们看待",
}

# 非谓语（不定式）单独一小节：这些是"动词碎片"，出现在 `to X` 位置上
INFINITIVE = {
    # ★ 用户截图那条（`ZhsCombatMsgPatch` 已有整句正则，这里是**兜底**）
    "down to attack": "俯冲攻击",
    "to dust": "扬尘",
    "to hobble": "致跛",
    "to shield slam": "盾击",
    "to stand": "站起",
    "to warm": "取暖",
    "to pull": "拉拽",
    "to touch": "触碰",
    "to extricate": "挣脱",
    "to protect": "保护",
    "to avenge": "复仇",
    "to clean": "清洁",
    "to help": "帮助",
    "to be": "成为",
    "to distort around": "扭曲环绕",
}

# 状态词（过去分词/现在分词，单独出现时作形容词/状态用）
STATE = {
    "dazed": "眩晕",
    "hobbled": "跛行",
    "paralyzed": "瘫痪",
    "stunned": "昏迷",
    "disarmed": "被卸械",
    "revealed": "被揭示",
    "suppressed": "被压制",
    "destroyed": "被摧毁",
    "grafted": "被嫁接",
    "chromed": "镀铬",
    "cyberized": "义体化",
    "polarized": "被极化",
    "conjoined": "被接合",
    "healing": "治疗中",
    "crackling": "噼啪作响",
    "scintillating": "闪烁",
    "dazzling": "耀眼",
}


def all_pairs():
    d = {}
    d.update(TABLE)
    d.update(INFINITIVE)
    d.update(STATE)
    d.update(COMBO0)
    d.update(COMBO)
    d.update(COMBO_CAP)
    return d


def main():
    apply_ = "--apply" in sys.argv
    fresh = "--fresh" in sys.argv
    # ★★ `--safe-only`：只写**已证明可达**的那几条（A 类，走 `Strings._S/_T` 查表）。
    #    其余（C/X 类 = 在 C# 里拼出来的）写进 Strings.xml **不会生效**（永不查表），
    #    它们必须走补丁侧的碎片字典（见 `Scripts\\ZhsFragmentDictPatch.cs`）。
    safe_only = "--safe-only" in sys.argv
    pairs = all_pairs()
    if safe_only:
        pairs = {k: v for k, v in pairs.items() if k in SAFE_KEYS}
    print("碎片字典条目 = %d（整句 %d · 非谓语 %d · 状态词 %d）%s"
          % (len(pairs), len(TABLE), len(INFINITIVE), len(STATE),
             "（--safe-only）" if safe_only else ""))

    text = io.open(STRINGS, encoding="utf-8").read()

    # ★ 先把**上一次写入的同一段落**摘掉（幂等的关键），再算"已有"
    #   —— 否则上一次写的条目会被当成"已有"⇒ 换小表时删不掉旧条目（本次踩到）
    had_block = MARK in text
    text = re.sub(r"\n?" + re.escape(MARK) + r".*?<!-- /碎片字典 -->\n?", "\n",
                  text, flags=re.S)

    # 已存在（带 Context 的也算"已有"：全局表只需补缺）
    have_global = set()
    for m in re.finditer(r'<string(?P<a>[^>]*)>', text):
        a = m.group("a")
        if 'Context=' in a:
            continue
        idm = re.search(r'ID="([^"]*)"', a)
        if idm:
            have_global.add(idm.group(1))

    new = [(k, v) for k, v in sorted(pairs.items()) if k not in have_global]
    dup = [k for k in pairs if k in have_global]
    print("上次写入的段落: %s" % ("有（已摘除，将重写）" if had_block else "无"))
    print("本次表内 %d 条 · 需写入 %d 条 · 已在别处存在（跳过）%d 条"
          % (len(pairs), len(new), len(dup)))
    if dup:
        print("  已在别处:", dup[:8])
    if not new and not had_block:
        print("（无新增，结束）")
        return 0
    for k, v in new[:10]:
        print("   + %-52s → %s" % (k[:52], v[:36]))
    if len(new) > 10:
        print("   …… 共 %d 条" % len(new))

    if not apply_:
        print("\n（干跑；加 --apply 才写）")
        return 0

    # 备份
    os.makedirs(os.path.dirname(BACKUP), exist_ok=True)
    if not fresh:
        os.makedirs(BACKUP, exist_ok=True)
        shutil.copy2(STRINGS, os.path.join(BACKUP, "Strings.xml"))
        print("\n已备份 → %s" % BACKUP)

    # 幂等：上面已把旧段落摘掉，这里直接拼新段落
    block = [MARK]
    for k, v in sorted(pairs.items()):
        e = (k.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;"))
        block.append('  <string ID="%s">%s</string>' % (e, v))
    block.append("<!-- /碎片字典 -->")
    ins = "\n".join(block) + "\n"

    # 插在 </strings> 之前（保持根元素）
    m = re.search(r"\n</strings>\s*$", text)
    if not m:
        print("!! 找不到 </strings> 结尾，未写入")
        return 1
    out = text[:m.start()] + "\n" + ins + text[m.start():]
    io.open(STRINGS, "w", encoding="utf-8", newline="").write(out)

    # 自证：重新解析 + 计数
    t2 = io.open(STRINGS, encoding="utf-8").read()
    n2 = len(re.findall(r'<string ID="[^"]*">', t2))
    print("已写入 %d 条；Strings.xml 现有 <string> 总数 = %d"
          % (len(pairs), len(re.findall(r"<string", t2))))
    assert MARK in t2 and "<!-- /碎片字典 -->" in t2
    # 幂等自证：再跑一次移除逻辑应与写入后一致
    t3 = re.sub(r"\n?" + re.escape(MARK) + r".*?<!-- /碎片字典 -->\n?", "\n", t2, flags=re.S)
    if MARK in t3:
        print("!! 幂等自证失败：MARK 未被完整移除")
        return 1
    print("幂等自证 ✅（重跑时能整段替换）")
    print("★ 下一步：python _tools\\validate_mod.py（必须用捆绑 python）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
