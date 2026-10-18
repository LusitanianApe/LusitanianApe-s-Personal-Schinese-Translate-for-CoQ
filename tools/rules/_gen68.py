# -*- coding: utf-8 -*-
"""#68：把弹窗缺口里的**整句**批量写成 C# 规则，插入 ZhsCombatMsgPatch.Rules 开头。

跳过：串表已覆盖（7 条）、调试/内部（6 条）、census 里被截断的（2 条）。
每个条目：英文原句 → 中文；英文会做**正则转义**（只保留 `{}` 颜色标记的字面匹配）。
"""
import io
import re

P = "mods/QudZhsCookbook/Scripts/ZhsCombatMsgPatch.cs"

# 英文 → 中文（整句；含 \n 的用真实换行）
MAP = [
    ("You've been recalled to a resting place.", "你已被召回至一处休憩之地。"),
    ("You were not recalled as you're already in a resting place.", "你已在休憩之地，无需召回。"),
    ("You don't have a missile weapon equipped that uses that ammunition.", "你没有装备使用这种弹药的远程武器。"),
    ("Choose an item to slot a cell into.", "选择要装入能量电池的物品。"),
    ("You have no hands to beat at the flames with!", "你没有手可以拍打火焰！"),
    ("What would you like to name?", "你想取什么名字？"),
    ("Barathrum is not in the golem. Ascend anyway?", "巴拉瑟鲁姆不在魔像里。仍要升空吗？"),
    ("Do you want to wait until the ascent is done?", "要等到升空结束吗？"),
    ("No mound of scrap and clay was found to complete.", "未找到可完成任务的废料与黏土堆。"),
    ("Choose a color for your maker's mark.", "为你的制造者印记选择一种颜色。"),
    ("You have the feeling of waking from a dream.", "你有种从梦中醒来的感觉。"),
    ("The cool swelling deflates as your organs dim.", "随着器官黯淡，那团清凉的肿胀消退了。"),
    ("Your muscles deflate to their usual size.", "你的肌肉回缩到平常大小。"),
    ("You recognize the area and stop being lost!", "你认出了这片区域，不再迷路！"),
    ("Your heart swells with a burning sensation.", "你的心脏膨胀起来，泛起灼烧感。"),
    ("Your skin flattens out and stretches tautly around your body once again.", "你的皮肤重新平复，紧紧绷回身体表面。"),
    ("You feel a soothing tingle in your chest as your wounds start to close.", "你感到胸口一阵舒缓的酥麻，伤口开始愈合。"),
    ("Your hearts begin to beat faster and your pupils dilate.", "你的心脏开始加速跳动，瞳孔放大。"),
    ("Your heart begins to beat faster and your pupils dilate.", "你的心脏开始加速跳动，瞳孔放大。"),
    ("Your heart rate returns to normal and your pupils shrink.", "你的心率恢复正常，瞳孔收缩。"),
    ("The clouds part in your mind and a ray of clarity strikes through.", "你脑中的迷雾散开，一道清明之光穿透而来。"),
    ("Your mind clouds over once again.", "你的心神再次被阴云笼罩。"),
    ("You cannot do that while submerged.", "在水下无法这么做。"),
    ("A torrent of life rushes over you.", "一股生命之流涌过你全身。"),
    ("The torrent of life sweeps away.", "生命之流退去了。"),
    ("You are nut currently in a landmark location.", "你当前不在地标位置。"),
    ("A loud buzz is emitted. The unauthorized glyph flashes on the side of the applicator.", "响起一声刺耳的蜂鸣。未经授权的符文在施用器侧面闪烁。"),
    ("You find a passageway back to your home dimension.", "你找到了一条回到你原生维度的通道。"),
    ("Use which recoiler?", "使用哪个召回器？"),
    ("Choose a model faction for your facial reconstruction.", "为你的面部重建选择一个样板派系。"),
    ("Choose a model faction for your holographic glamour.", "为你的全息幻象选择一个样板派系。"),
    ("The gates are sealed for eternity.", "大门将永远封闭。"),
    ("You mind swerves from afar, and a force of repulsion from inside the sarcophagus prevents your entry.", "你的心神从远处偏转，石棺内一股排斥力阻止你进入。"),
    ("A force of repulsion from inside the sarcophagus prevents your entry.\n\nYou do not bear the Mark of Death.", "石棺内一股排斥力阻止你进入。\n\n你未带有死亡印记。"),
    ("You are leaving the ambient broadcast power grid and transitioning to backup power. Are you sure?", "你将离开环境广播电网并切换到备用电源。确定吗？"),
    ("The First Council of Omonporch has begun. Choose how to appease Asphodel.", "奥蒙波奇第一次议会开始了。选择如何安抚阿斯弗德尔。"),
    ("The pact is struck. The Barathrumites may lease control of the Spindle, and all the attending factions owe a debt to Asphodel.", "契约已缔结。巴拉斯鲁米特可租借纺锤的控制权，所有与会派系都欠阿斯弗德尔一份人情。"),
    ("The pact is struck. The Barathrumites may lease control of the Spindle, and the chosen factions owe a debt to Asphodel.", "契约已缔结。巴拉斯鲁米特可租借纺锤的控制权，被选中的派系欠阿斯弗德尔一份人情。"),
    ("The pact is struck. The Barathrumites may lease control the Spindle.", "契约已缔结。巴拉斯鲁米特可租借纺锤的控制权。"),
    ("You must have a long blade equipped to switch stances.", "你必须装备长刃才能切换架势。"),
    ("There's nothing there to lunge at.", "那里没有可突刺的目标。"),
    ("There's nothing there you can lunge at.", "那里没有你能突刺的目标。"),
    ("You lose your way beneath a dense canopy of spores.", "你在浓密的孢子穹顶下迷了路。"),
    ("You cannot cross into Brightsheol from this place.", "你无法从此处进入光灵界。"),
    ("The colossal lid slams shut. Darkness engulfs you.", "巨大的盖子轰然合上。黑暗吞没了你。"),
    ("You died.\n\nEntombed in the burial chamber of Resheph, the Last Sultan.", "你死了。\n\n你被埋葬在末代苏丹雷谢夫的墓室中。"),
    ("The moors rattle and a marble brick crumbles as a starship takes off.", "荒原震颤，一艘星舰升空时一块大理石块崩落。"),
    ("You must have an axe equipped in your primary hand to go berserk.", "你必须在主手装备斧才能进入狂暴。"),
    ("You can't go berserk until Dismember is off cooldown.", "肢解冷却结束前无法进入狂暴。"),
    ("You must have a cudgel equipped in your primary hand to demolish things.", "你必须在主手装备棍棒才能拆毁东西。"),
    ("You can't Demolish until Slam is off cooldown.", "盾击冷却结束前无法拆毁。"),
    ("There's nothing there you can shank.", "那里没有你能捅刺的目标。"),
    ("You must have a short blade equipped to shank.", "你必须装备短刃才能捅刺。"),
    ("You have no explosives to deploy!", "你没有可部署的炸药！"),
    ("That wouldn't leave you with NEARLY enough junk! You can't drop that!", "那样你的废料就远远不够了！你不能丢下它！"),
    ("{{r|Unwelcome images of {{G|budding plants}} fill your mind.}}", "{{r|{{G|萌芽植株}}的不祥影像充斥你的脑海。}}"),
    ("&CA flash of insight overcomes you!", "&C一道灵光闪过你的脑海！"),
    ("You can't scintillate again so soon.", "你不能这么快再次闪耀。"),
    ("You're not under enough stress to scintillate.", "你的压力不足以引发闪耀。"),
    ("There is no liquid here for you to spew.", "这里没有可供你喷吐的液体。"),
    ("There are no creatures in range.", "范围内没有生物。"),
    ("You don't have anything to use in that slot.", "那个槽位里没有可用的东西。"),
    ("Are you sure you want to delete this entry?", "确定要删除这条记录吗？"),
    ("You can't delete automatically recorded chronology entries.", "你不能删除自动记录的年表条目。"),
    ("That name is already in use.", "这个名字已被使用。"),
]

# 药剂不良反应族：前缀两段 + 后缀多种
PREFIX = [
    ("Your mutant physiology reacts adversely to the tonic.", "你的变异生理对药剂产生了不良反应。"),
    ("The tonics you ingested react adversely to each other.", "你摄入的药剂彼此产生了不良反应。"),
]
SUFFIX = [
    ("You feel awfully frigid.", "你感到彻骨寒冷。"),
    ("Aaaaaaaaargh!", "啊啊啊啊啊啊！"),
    ("Your skin starts to knot and misshape.", "你的皮肤开始扭结变形。"),
    ("You flicker through spacetime uncontrollably.", "你在时空中不受控制地闪烁。"),
    ("Your field of vision erupts into a plane of blinding, white light.", "你的视野炸成一片刺目的白光。"),
    ("The torrent of life sweeps away.", "生命之流退去了。"),
]

# 多段句（前缀 + 后缀）组合成整句
for pe, pz in PREFIX:
    for se, sz in SUFFIX:
        MAP.append((pe + " " + se, pz + sz))


def esc_regex(s):
    out = []
    for ch in s:
        if ch in r"\.^$+?()[]{}|":
            out.append("\\" + ch)
        else:
            out.append(ch)
    return "".join(out).replace("\n", "\\n")


def esc_cs(s):
    return s.replace('"', '""').replace("\n", "\\n")


lines = []
for en, zh in MAP:
    pat = "^" + esc_regex(en) + "$"
    lines.append('            R(@"%s",\n              "%s"),' % (esc_cs(pat), esc_cs(zh)))

block = (
    "\n            // ═══════════════════════════════════════════════════════════════\n"
    "            // ★★ 2026-10-18（#68）：`Popup`/`MessageQueue` 出口的**整句字面量**批量收口。\n"
    "            //   来源：`_tools\\_audit_popup_forms.py` 的 94 条缺口，剔除串表已覆盖（7）\n"
    "            //   与调试/内部（6）后，把**无变量整句**一次写清（%d 条）。\n"
    "            //   ⚠ 全部 `^…$` 锚定 + 正则转义 ⇒ 只认整句，不碰任何其它文本。\n"
    "            // ═══════════════════════════════════════════════════════════════\n"
    % len(MAP)
) + "\n".join(lines) + "\n"

t = io.open(P, encoding="utf-8-sig").read()
marker = "            // ── A 类：整句（原文就是一整条 Show(...)）──"
i = t.find(marker)
assert i > 0, "找不到插入点"
t = t[:i] + block + "\n" + t[i:]
io.open(P, "w", encoding="utf-8", newline="\n").write(t)
print("inserted", len(MAP), "rules")
