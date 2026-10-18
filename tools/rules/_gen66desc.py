# -*- coding: utf-8 -*-
"""#68 第二弹：`GetDescription` / `GetLevelText` 系列里的**整句默认描述**（变异/义体）。

输入：`_tools/_audit_other_corpus.txt`（严格过滤后的整句候选 66 条）
输出：插入 `ZhsCombatMsgPatch.Rules`（在 #68 块之后、A 类之前）
"""
import io
import re

P = "mods/QudZhsCookbook/Scripts/ZhsCombatMsgPatch.cs"
CORPUS = "_tools/_audit_other_corpus.txt"

T = {
    "Whenever you drink freshwater": "每当你饮用淡水，有 25% 几率在 6 小时内免疫恐惧。",
    "Defib:": "除颤：通电时，若穿戴者心脏骤停，此护甲会每回合向穿戴者释放电击，直到心脏恢复跳动。",
    "Flare-compensating:": "防眩补偿：通电并启动时，该物品提供对视觉闪光效果的保护。",
    "HUD:": "平视显示：通电并启动时，该物品可与智能枪配合以启用其加成。",
    "Heartstopper:": "停搏：通电时，此武器有几率使对手心脏骤停。",
    "Massively overloaded:": "严重超载：该物品性能大幅提升，但会消耗大量额外电量、使用时产生可观热量，并会因耗电量增加而有几率损坏。",
    "Microserrated:": "微锯齿：此武器有几率肢解对手。",
    "Mighty:": "强力：此武器没有力量加成穿透上限。",
    "You startle easily": "你容易受惊，并会立刻启动防御机制。",
    "You beguile a nearby creature": "你魅惑附近的生物，使其忠诚地为你效劳。",
    "You garrote an adjacent creature's mind": "你绞紧邻近生物的心智，在自身躯体休眠时操控其行动。",
    "You may fire missile weapons through the forcefield": "你可以穿过力场发射远程武器。",
    "You possess a towering vision of self": "你怀有宏大的自我意象，并将其投射到附近生物的心智中。",
    "You possess extraordinary analytical prowess": "你拥有非凡的分析能力，却难以与他人相处。",
    "You are possessed of an indefatigable focus": "你拥有不知疲倦的专注力，这种专注有时会表现为固执。",
    "You bond with a nearby organic creature": "你与附近的有机生物建立连结，并汲取其生命力。",
    "You tap into the aggregate mind": "你接入聚合心智，从其他灵能者身上窃取力量。",
    "You reflect mental attacks back": "你将精神攻击反射回攻击者。",
    "You peer into your near future.": "你窥视自己的近期未来。",
    "You may open security doors": "你可以通过触摸打开安全门并使用某些安全设备。",
    "You sunder spacetime, sending things nearby": "你撕裂时空，使附近之物翻滚着穿过宇宙织物的裂口。",
    "You invoke a concussive force": "你在附近区域引发震荡之力，将敌人击退并使其昏迷。",
    "You sunder the mind of an enemy": "你撕裂敌人的心智，使其在痛苦中踉跄。",
    "You can move things with your mind.": "你可以用意念移动物体。",
    "You spit a puddle of corrosive acid.": "你吐出一滩腐蚀性酸液。",
    "You live in an alternate plane of reality.": "你生活在现实的另一个位面。",
    "You breathe confusion gas.": "你呼出混乱气体。",
    "You breathe corrosive gas.": "你呼出腐蚀气体。",
    "You breathe normality gas.": "你呼出常态气体。",
    "You can travel underground by burrowing.": "你可以掘地在地下穿行。",
    "You provoke waking dreams with your gaze.": "你的凝视会引发清醒梦。",
    "You are a crystalline being.": "你是一个晶体生命。",
    "Effects that make non-biological clones": "制造你非生物克隆体的效果产量翻倍。",
    "You extract carbon from living material.": "你从活体中提取碳。",
    "In stressful situations, you scintillate.": "在紧张情境下，你会闪耀。",
    "You scare creatures around you.": "你惊吓周围的生物。",
    "Additionally, you sometimes scare creatures passively.": "此外，你有时会被动地惊吓生物。",
    "You teleport and bring creatures along with you.": "你传送时会把生物一并带走。",
    "You molt powerful beams": "你蜕射出横跨光与物质光谱的强大光束。",
    "You emit powerful magnetic pulses.": "你发出强大的磁脉冲。",
    "Several horns jut out of your head.": "你的头上突出数只角。",
    "You bear a sphincter-choked bilge hose": "你长着一根带括约肌的舱底软管，用来吸取附近液体并向敌人喷吐，偶尔将其击倒。",
    "You capture prey with your sticky tongue.": "你用黏舌捕捉猎物。",
    "You turn things to stone with your gaze.": "你的凝视能将物体化为石头。",
    "You can move across walls": "你只能沿墙壁移动。",
    "You dash along a waveform.": "你沿着波形疾驰。",
    "You regulate your body's release of adrenaline.": "你能调节身体的肾上腺素分泌。",
    "You regenerate by absorbing cold.": "你通过吸收寒冷再生。",
    "Whenever you would have taken cold damage": "每当你将受到寒冷伤害时，你改为治疗 0 点。",
    "You accrue electrical charge": "你积累电荷，可将其释放以造成伤害。",
    "You emit jets of frost from your mouth.": "你从口中喷出寒霜。",
    "You regenerate by absorbing heat.": "你通过吸收热量再生。",
    "Your joints stretch much further": "你的关节比常人伸展得远得多。",
    "You are possessed of exceptionally acute smell.": "你拥有异常敏锐的嗅觉。",
    "You are possessed of hulking strength.": "你拥有庞大的力量。",
    "Horns jut out of your head.": "你的头上突出角。",
    "You assume the form of any creature you touch.": "你能变成任何触碰到的生物。",
    "You have an extra set of arms.": "你多出一双手臂。",
    "You have an extra set of legs.": "你多出一双腿。",
    "You may phase through solid objects": "你可以短暂穿过固体。",
    "Your wounds heal very quickly.": "你的伤口愈合得很快。",
    "You can walk over slime without slipping.": "你能在黏液上行走而不滑倒。",
    "You can spin sticky silk webs.": "你能纺出黏性的丝网。",
    "While spinning, you leave webs in your wake": "纺丝时，你移动会留下一路蛛网。",
    "You are immune to getting stuck.": "你不会被黏住。",
    "Creatures are pushed away from center of blast.": "生物会被冲击波从中心推开。",
}

rows = [l for l in io.open(CORPUS, encoding="utf-8").read().split("\n") if l.strip()]
sel = [r for r in rows if r[0].isupper() and r.rstrip().endswith((".", "!", "?"))
       and not r.startswith(" ") and len(r) >= 25 and "\\n" not in r]
print("整句候选:", len(sel))

pairs = []
for s in sel:
    hits = [k for k in T if s.startswith(k)]
    if len(hits) != 1:
        print("!! 未匹配/多匹配:", s[:80], hits)
        continue
    pairs.append((s, T[hits[0]]))
print("成对:", len(pairs))


def esc_regex(s):
    out = []
    for ch in s:
        if ch in r"\.^$+?()[]{}|":
            out.append("\\" + ch)
        else:
            out.append(ch)
    return "".join(out)


def esc_cs(s):
    return s.replace('"', '""')


lines = ['            R(@"^%s$",' % esc_cs(esc_regex(en)) + '\n              "%s"),' % esc_cs(zh)
         for en, zh in pairs]
block = (
    "\n            // ═══════════════════════════════════════════════════════════════\n"
    "            // ★★ 2026-10-18（#68 第二弹）：`GetDescription` / `GetLevelText` 的**整句默认描述**\n"
    "            //   （变异/义体/缺陷的默认文案，硬编码在 C# 里；%d 条）\n"
    "            //   来源 `_tools\\_audit_other_exits.py` → 66 条整句候选。\n"
    "            // ═══════════════════════════════════════════════════════════════\n" % len(pairs)
) + "\n".join(lines) + "\n"

t = io.open(P, encoding="utf-8-sig").read()
marker = "            // ── A 类：整句（原文就是一整条 Show(...)）──"
i = t.find(marker)
assert i > 0
t = t[:i] + block + "\n" + t[i:]
io.open(P, "w", encoding="utf-8", newline="\n").write(t)
print("inserted", len(pairs))
