# 规则夹具 · 人工复核表

> 机器已按 `_tools\_rule_spec.json` 跑过一遍。请重点看「机器」列标了 **漏 / 误伤** 的行。
>
> **判据**：
> · 正例 → `命中` = 正则匹配上了；`漏` = 没匹配上（规则够不到这个形态）
> · 反例 → `未命中` = 正则没匹配（正确）；`误伤` = 正则匹配了（会错误改写）
> · `命中 ≠ 译文好`：命中只说明规则会开火，译文质量看「机器译文」列

| 规则 | 类型 | 原文 | 机器 | 机器译文 | 人工意见 |
|---|---|---|---|---|---|
| journal-mapnote-66 | 正例 | `You note the location of 一支驼人商队 in the {{W\|位置 › 商人}} section of your journal.` | 命中 | 你在日志的位置 › 商人栏记下了一支驼人商队的位置。 |  |
| journal-mapnote-66 | 正例 | `You note the location of Sithis，传奇蝎蚂的巢穴 in {{W\|地点 › 巢穴}} section of your journal.` | 命中 | 你在日志的地点 › 巢穴栏记下了Sithis，传奇蝎蚂的巢穴的位置。 |  |
| journal-mapnote-66 | 正例 | `You note the location of Alailitara in 地点 › 聚落 section of 你的 journal.` | 命中 | 你在日志的地点 › 聚落栏记下了Alailitara的位置。 |  |
| journal-mapnote-66 | 反例 | `You note the location of the rusted gate.` | 未命中 |  |  |
| journal-mapnote-66 | 反例 | `You note this piece of information in the {{W\|村落史 › Sharshiwar}} section of your journal.` | 未命中 |  |  |
| journal-mapnote-66 | 反例 | `You note the location of X in the 地点 › 巢穴 section of her journal.` | 未命中 |  |  |
| rep-decrease-67 | 正例 | `因为他们鄙视看守 Qan，你的 reputation with 海胆 decreased by 100 to -600.` | 命中 | 因为他们鄙视看守 Qan，你与海胆的声望下降了 100，降至 -600。 |  |
| rep-decrease-67 | 反例 | `Your reputation with 海胆 decreased by 100 to -600.` | 未命中 |  |  |
| rep-decrease-67 | 反例 | `因为他们鄙视看守 Qan，你的 reputation with 海胆 increased by 100 to 600.` | 未命中 |  |  |
| rep-status-despised-67 | 正例 | `You are now despised by 海胆.` | 命中 | 你如今被海胆鄙视。 | 用户 2026-10-18：「你如今被海胆鄙视」 |
| rep-status-despised-67 | 反例 | `You are now 海胆.` | 未命中 |  | 用户 2026-10-18：「你如今被海胆鄙视」 |
| rep-status-despised-67 | 反例 | `You are now standing by the gate.` | 未命中 |  | 用户 2026-10-18：「你如今被海胆鄙视」 |
| rep-status-despised-67 | 反例 | `You are now despised of 海胆.` | 未命中 |  | 用户 2026-10-18：「你如今被海胆鄙视」 |
| rep-status-liked-67 | 正例 | `You are now liked by 约帕.` | 命中 | 你如今被约帕喜爱。 | 用户 2026-10-18：「你如今被约帕喜爱」 |
| rep-status-liked-67 | 反例 | `You are now liked.` | 未命中 |  | 用户 2026-10-18：「你如今被约帕喜爱」 |
| rep-status-liked-67 | 反例 | `You are now liked by.` | 未命中 |  | 用户 2026-10-18：「你如今被约帕喜爱」 |
| ritual-rep-69 | 正例 | `Your reputation with 看守联盟 is -50.` | 命中 | 你与看守联盟的声望为 -50。 |  |
| ritual-rep-69 | 正例 | `Your reputation with 海胆 is 100.` | 命中 | 你与海胆的声望为 100。 |  |
| ritual-rep-69 | 反例 | `Your reputation with 海胆 decreased by 100 to -600.` | 未命中 |  |  |
| ritual-award-69 | 正例 | `Qan can award an additional 100 reputation.` | 命中 | Qan还能再给予 100 点声望。 |  |
| ritual-award-69 | 反例 | `Qan can award an additional 100.` | 未命中 |  |  |
| ritual-award-69 | 反例 | `Qan awarded an additional 100 reputation.` | 未命中 |  |  |
