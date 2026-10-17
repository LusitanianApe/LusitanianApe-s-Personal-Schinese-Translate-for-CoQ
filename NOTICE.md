# 版权与边界

## 本仓库**不含**任何游戏反编译产物

- 不含 `Assembly-CSharp.dll` 及其反编译源码
- 不含游戏字符串堆/调用点普查数据（本项目内部的 `_census/` 约 65 MB，未收录）
- 不含游戏 API 文档导出（`Assembly-CSharp.xml`）
- 不含游戏资源（`.assets` / 字体 / 图集）

`src/decomp/` 是**自己写的 ILSpy 封装**（NuGet 依赖 `ICSharpCode.Decompiler`），
它只在**使用者本机**对**使用者自己合法持有**的游戏程序集运行。

## 译文数据

`tools/fragments/_zh_fragments.py` 含英文短语 → 中文的对照表，
是本项目为《Caves of Qud》制作的简体中文模组的一部分。
英文短语的著作权归 Freehold Games 所有；本仓库只包含**短句级别的对照**，
不含任何连续的、可替代原作的文本。

## 本仓库代码

自称原创的部分（脚本、C# 工程、文档）按 MIT 发布，见 `LICENSE`。

## 测试样本

`fixtures/` 里是少量**单句**英文测试输入（约 80 条），仅用于验证正则规则的行为，
不构成对游戏的替代或可游玩内容。若权利人认为不妥，删掉 `fixtures/` 不影响工具本身。
