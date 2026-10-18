#!/usr/bin/env python3
# -*- coding: utf-8 -*-
r"""
开发版 / 发布版**双目录共存**（Steam 创意工坊只能游戏内上传）

用户 2026-10-18 定：
    「直接在游戏 Mods 文件夹里建个新文件夹放无探针的发布版；
      只要在模组名上做差分，我就能想起来哪个是开发版、哪个是发布版
      —— 发布版比开发版名字少一个『什么』。」

命名（模组名 = manifest.json 的 Title）：
    开发版  `L什么什么什么的个人汉化`   （3 个「什么」）
    发布版  `L什么什么的个人汉化`       （2 个「什么」）

目录与 ID（★ 两个文件夹必须用**不同 ID**，否则加载器会撞）：
    Mods\\QudZhsCookbook         开发版  ID=LusitanianApe_QudZhsCookbookDev
    Mods\\LApePersonalTranslation  发布版  ID=LusitanianApe_QudZhsCookbook   ← 与原工坊一致

为什么发布版保留原 ID：玩家存档/创意工坊订阅都按这个 ID 认模组，换 ID 等于换了一个模组。
为什么 `ZhsTrace` 不算探针：多个功能补丁共用它（`_zhs_trace_on.txt` 才生效），删了编译不过。

用法：
    python _tools\release_branch.py --to-release     # 建/刷新 发布版 + 开发版两个目录
    python _tools\release_branch.py --to-dev         # 只刷新开发版目录
    python _tools\release_branch.py --restore-original  # 测试用：删发布版、按工作区原样装开发版
    python _tools\release_branch.py --status
"""
import io
import json
import os
import re
import shutil
import subprocess
import sys
import time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEV = os.path.join(ROOT, "mods", "QudZhsCookbook")
REL_ROOT = os.path.join(ROOT, "_release")
REL = os.path.join(REL_ROOT, "QudZhsCookbook")
BACKUP = os.path.join(REL_ROOT, "backup")

MODS = os.path.join(os.environ.get("USERPROFILE", ""), "AppData", "LocalLow",
                    "Freehold Games", "CavesOfQud", "Mods")
DEV_INSTALL = os.path.join(MODS, "QudZhsCookbook")
# ★ 2026-10-18（用户要求）：发布版**文件夹名**改 `LApePersonalTranslation`。
#   文件夹名是纯本地标识 —— 游戏/工坊/存档认的是 `manifest.json` 的 **ID**，
#   所以这里爱叫什么都行；只要 REL_ID 保持 `LusitanianApe_QudZhsCookbook` 不变。
REL_INSTALL = os.path.join(MODS, "LApePersonalTranslation")

DEV_ID = "LusitanianApe_QudZhsCookbookDev"
REL_ID = "LusitanianApe_QudZhsCookbook"
# ★ 用户 2026-10-18 定：**发布版 3 个「什么」、开发版 2 个**。
#   理由：工坊一直是 3 个「什么」—— 群友更新时模组名不能变，否则会弹 "mods differ"。
DEV_TITLE = "L什么什么的个人汉化"
REL_TITLE = "L什么什么什么的个人汉化"
PROBES = os.path.join(ROOT, "_tools", "release_probes.txt")
EXCLUDE_DIRS = {"bin", "obj", ".git"}


def fail(msg):
    print("[FAIL] " + msg)
    sys.exit(1)


def game_running():
    try:
        r = subprocess.run(["tasklist", "/FI", "IMAGENAME eq CoQ.exe"],
                           capture_output=True, text=True, encoding="utf-8", errors="replace")
        return "CoQ.exe" in (r.stdout or "")
    except Exception:
        return False


def read_probes():
    names = []
    with io.open(PROBES, encoding="utf-8") as f:
        for line in f:
            s = line.strip()
            if not s or s.startswith("#"):
                continue
            names.append(s)
    return names


def copytree(src, dst, skip_files=()):
    os.makedirs(dst, exist_ok=True)
    for name in os.listdir(src):
        sp = os.path.join(src, name)
        if os.path.isdir(sp):
            if name in EXCLUDE_DIRS:
                continue
            copytree(sp, os.path.join(dst, name), skip_files)
        else:
            if name in skip_files:
                continue
            shutil.copy2(sp, os.path.join(dst, name))


def wrap_probe_mounts(text, classes):
    """把引用探针的语句包进 `#if false` … `#endif`（三种书写形态都吃）。"""
    lines = text.split("\n")
    out = []
    i = 0
    wrapped = 0
    while i < len(lines):
        line = lines[i]
        stripped = line.strip()
        hit = None
        if not stripped.startswith("//"):
            for c in classes:
                if re.search(r'\b' + re.escape(c) + r'\b', line):
                    hit = c
                    break
        if hit is None:
            out.append(line)
            i += 1
            continue
        out.append("            // ↓↓ 发布版：探针 %s 已撤（开发分支保留） ↓↓" % hit)
        out.append("#if false")
        if ("MountOff" in line or stripped.endswith("try")
                or ("try" in line and "{" in line)):
            depth = 0
            seen_catch = False
            j = i
            while j < len(lines):
                out.append(lines[j])
                depth += lines[j].count("{") - lines[j].count("}")
                if "catch" in lines[j]:
                    seen_catch = True
                if seen_catch and depth <= 0:
                    break
                j += 1
            i = j + 1
        else:
            out.append(line)
            i += 1
        out.append("#endif")
        out.append("            // ↑↑ 发布版：探针 %s 已撤 ↑↑" % hit)
        wrapped += 1
    return "\n".join(out), wrapped


def read_manifest(path):
    with io.open(path, encoding="utf-8-sig") as f:
        return json.load(f)


def write_manifest(path, data):
    with io.open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(data, f, ensure_ascii=False, indent=4)
        f.write("\n")


def release_title(dev_title):
    """旧接口保留；**方向已反转**：发布版是 3 个「什么」，开发版 2 个。"""
    return REL_TITLE


def patch_manifest(mod_dir, mid, title=None):
    p = os.path.join(mod_dir, "manifest.json")
    d = read_manifest(p)
    d["ID"] = mid
    if title:
        d["Title"] = title
    write_manifest(p, d)


def build_release():
    probes = read_probes()
    skip = {p + ".cs" for p in probes}
    if os.path.isdir(REL):
        shutil.rmtree(REL)
    copytree(DEV, REL, skip_files=skip)

    boot = os.path.join(REL, "Scripts", "ZhsBootstrap.cs")
    t = io.open(boot, encoding="utf-8").read()
    t2, n = wrap_probe_mounts(t, set(probes))
    io.open(boot, "w", encoding="utf-8", newline="\n").write(t2)

    # 发布版的 manifest：ID 用原 ID（工坊/存档一致），Title = **3 个「什么」**
    patch_manifest(REL, REL_ID, REL_TITLE)

    depth, bad = 0, []
    for ln in t2.split("\n"):
        s = ln.strip()
        if s == "#if false":
            depth += 1
        elif s == "#endif" and depth > 0:
            depth -= 1
        if depth > 0 or s.startswith("//"):
            continue
        for c in probes:
            if re.search(r'\b' + re.escape(c) + r'\b', ln):
                bad.append((c, ln.strip()[:80]))
                break
    if bad:
        for c, ln in bad[:6]:
            print("   [未包住] %s : %s" % (c, ln))
        fail("发布版里仍有 %d 处裸引用探针" % len(bad))

    cc_src = os.path.join(ROOT, "mods", "_compilecheck", "CompileCheck.csproj")
    cc_dir = os.path.join(REL_ROOT, "_compilecheck")
    os.makedirs(cc_dir, exist_ok=True)
    shutil.copy2(cc_src, os.path.join(cc_dir, "CompileCheck.csproj"))
    print("[..] 编译校验发布版 …")
    r = subprocess.run(["dotnet", "build", cc_dir, "-c", "Release", "-v", "q"],
                       capture_output=True, text=True, encoding="utf-8", errors="replace")
    ok = r.returncode == 0
    errs = [l for l in (r.stdout or "").split("\n") if "error" in l.lower()][:8]
    print("[OK] 发布版编译通过" if ok else "[FAIL] 发布版编译失败")
    for e in errs:
        print("   " + e.strip())
    if not ok:
        fail("发布版编译失败，未装机")
    return len(probes), n


def backup_dev():
    os.makedirs(BACKUP, exist_ok=True)
    dst = os.path.join(BACKUP, "dev_" + time.strftime("%Y%m%d-%H%M%S"))
    copytree(DEV, dst)
    print("[OK] 开发版已备份 → " + dst)
    return dst


def install(src, dst, label, mid, title=None):
    if game_running():
        fail("游戏正在运行 —— 按 AGENTS §2 不装机")
    if os.path.isdir(dst):
        shutil.rmtree(dst)
    shutil.copytree(src, dst)
    patch_manifest(dst, mid, title)
    print("[OK] 已装机（%s）→ %s（ID=%s）" % (label, dst, mid))


def status():
    for label, p in (("开发版", DEV), ("发布版(staging)", REL),
                     ("开发版装机", DEV_INSTALL), ("发布版装机", REL_INSTALL)):
        if not os.path.isdir(p):
            print("  %-14s 不存在" % label)
            continue
        n = sum(len(f) for _, _, f in os.walk(p))
        mf = os.path.join(p, "manifest.json")
        title = mid = "?"
        if os.path.isfile(mf):
            d = read_manifest(mf)
            title, mid = d.get("Title", "?"), d.get("ID", "?")
        print("  %-14s %4d 文件  ID=%s  Title=%s" % (label, n, mid, title))


def main():
    if "--to-release" in sys.argv:
        if game_running():
            fail("游戏正在运行 —— 先退出再切发布版")
        dev_title = DEV_TITLE
        backup_dev()
        n, wrapped = build_release()
        install(REL, REL_INSTALL, "发布版·无探针", REL_ID, REL_TITLE)
        install(DEV, DEV_INSTALL, "开发版·带探针", DEV_ID, DEV_TITLE)
        print("[OK] 已撤掉 %d 个探针（%d 处挂载已 #if false）" % (n, wrapped))
        print("[!] 游戏里**同时只启用一个**：开发版 ID 以 Dev 结尾，发布版是原 ID")
    elif "--to-dev" in sys.argv:
        if game_running():
            fail("游戏正在运行 —— 先退出")
        install(DEV, DEV_INSTALL, "开发版·带探针", DEV_ID, DEV_TITLE)
    elif "--restore-original" in sys.argv:
        # 测试/回退用：删掉发布版目录，把工作区**原样**装回（manifest 不动）
        if game_running():
            fail("游戏正在运行 —— 先退出")
        if os.path.isdir(REL_INSTALL):
            shutil.rmtree(REL_INSTALL)
            print("[OK] 已删除发布版装机目录")
        install(DEV, DEV_INSTALL, "开发版·原样", read_manifest(
            os.path.join(DEV, "manifest.json"))["ID"])
    elif "--status" in sys.argv:
        status()
    else:
        print(__doc__)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
