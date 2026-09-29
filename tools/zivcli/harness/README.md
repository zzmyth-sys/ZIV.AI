# harness（冻结快照）

本目录为 harness 冻结快照，来源见各文件头注释 / 下表。**不随产品代码同步**；
如需更新，须走 `DOC/FROZEN.md` 修订。

| 文件 | 来源 | 说明 |
|---|---|---|
| `s5s6_scenarios.py` | `D:\devlop\ZIV.AI\_test_step2\s5s6_scenarios.py` | 场景驱动；本地化改动：`REPRO_HARNESS` 默认值改为本目录 |
| `repro_viggle.py` | `E:\temp\opencode\repro_viggle.py` | 逐字节快照，未改动 |
| `seaminstr.py` | `E:\temp\opencode\seaminstr.py` | 逐字节快照；s5s6 路径不 import，保底留存 |
| `fixtures/img1.jpg` | `D:\temp\剪贴板图片 (1).jpg` | 金标主图（3868008 B 尺寸精确匹配） |
| `fixtures/img2.png` | `D:\devlop\ZIV.AI\_test_step2\viggle_aspect_640x1024.png` | 金标替代参考图（原 IMG2 已丢失） |

数据文件（`fixtures/`）用于 `goldens.json` 钉死输入，不依赖 `D:\temp`。
