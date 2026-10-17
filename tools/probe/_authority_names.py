# -*- coding: utf-8 -*-
"""取权威中文名：我方 Mutations / HiddenMutations / Skills 里对应英文的 DisplayName。"""
import io
import os
import re

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MOD = os.path.join(ROOT, "mods", "QudZhsCookbook")

WANT = ["Burrowing", "Sticky Tongue", "Slog Glands", "Acid Slime Glands",
        "Liquid Spitter", "Frost Webs", "Spider Webs", "Ley Shifting", "Serenity",
        "Waveform Worm", "MultiHorns", "Decarbonizer", "Defensive Chromatophores",
        "Will Force", "Fear Aura", "Irisdual Beam", "Freeze Breath", "Engulfing",
        "Run Over", "Cloneling", "Burrowing Claws", "Tongue",
        # 技能侧
        "Death From Above", "Lay Mine", "Set Bomb", "Deploy Turret"]


def dump(path, tag, name_attr):
    if not os.path.isfile(path):
        return
    t = io.open(path, encoding="utf-8", errors="replace").read()
    for m in re.finditer(r"<%s\b([^>]*)>" % tag, t):
        a = m.group(1)
        nm = re.search(r'%s="([^"]*)"' % name_attr, a)
        dn = re.search(r'DisplayName="([^"]*)"', a)
        if nm and dn and nm.group(1) in WANT:
            print("   %-24s %-28s → %s" % (os.path.basename(path), nm.group(1), dn.group(1)))


def main():
    print("=== 我方变异表 ===")
    for fn in ("Mutations.xml", "HiddenMutations.xml"):
        dump(os.path.join(MOD, fn), "mutation", "Name")
    print("=== 我方技能表（<power>）===")
    dump(os.path.join(MOD, "Skills.xml"), "power", "Name")
    print("=== 我方技能表（<skill>）===")
    dump(os.path.join(MOD, "Skills.xml"), "skill", "Name")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
