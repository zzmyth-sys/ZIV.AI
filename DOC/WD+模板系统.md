# WD 标签 + 提示词模板系统

> 记录 2026-09 起在 `_test_step2/` 做的三轮 WD（WD14 Tagger）标签与提示词模板实验，
> 以及它与产品侧 `Template/commands.json` 命令模板系统的衔接方案。
> 只记录已实测的事实；推断处均标注。

---

## 0. 结论速览（TL;DR）

1. **短自然语言模板（A）≥ WD 标签串（B）**，三轮七组对比中没有一次 B 明显胜出。
2. **WD 标签直接拼接最稳**；LLM 重写（B）在复杂主体上反而会丢细节（衣服图案 / 手持物 / 牌子 / 麦克风）。
3. **A1（保留质量标签）与 A2（清洗质量标签）肉眼无差**；`realistic` / `photorealistic` 等质量词可留可去。
4. **多图角色场景必须做角色级过滤**：WD 会把夜景误标成 `multiple boys` / `6+boys` / `people`，把塔标成 `building` / `city`，把人物标签混进服装图。不过滤就会往 prompt 里注入错误元素。
5. **尾部加“光影融合”句**可以让主体染色更贴环境光，但会轻度污染白衣服的白平衡；属于风格开关，不是质量提升。
6. **MAD / PSNR / SSIM 与主观保真不一致**：batch1b 里 B 明显换脸，SSIM 却是三组最高。指标只能当参考，人眼为准。
7. 推荐落地：**模板系统里主体用短自然语言，WD 标签只作为可选的“补充描述/兜底”，并强制角色过滤**。

---

## 1. 目标

验证在 Qwen-Image-Edit（QW21edit）图像编辑中，两种提示词构造方式的效果差异：

- **A：WD 标签 + 模板短句**（标签由 WD14 Tagger 自动打出）
- **B：把 WD 标签交给 LLM，重写成一段自然语言**（第一、二轮）；或**按角色组装 WD 标签**（第三轮）

并判断 WD 标签是否值得引入产品模板系统。

---

## 2. 实验装置

### 2.1 固定参数

| 项 | 值 |
|---|---|
| steps | 30 |
| seed | 42 |
| denoise | 1.0 |
| 分辨率 | `ZIV_AI_RESOLUTION_MODE=side` + `ZIV_AI_RESOLUTION_SIDE=1024`（须在 import 前设置） |
| 输出 | 长边 1024，短边按主图比例 |
| Python | `Comfyui\python_embeded\python.exe -s` |

### 2.2 Tagger 行为（`python/server/tagger.py`）

- 签名：`tag_image(image_path, model_name=None, threshold=None, character_threshold=None) -> list[str]`
- 返回 **character + general** 标签，下划线已转空格，**不含 rating 类**。
- 默认阈值：`config.TAGGER_THRESHOLD=0.35`，`config.TAGGER_CHARACTER_THRESHOLD=0.85`；
  第三轮 general threshold 用到 **0.5**。

### 2.3 多图槽位规则（`python/server/multi_image.py` + `pipeline.py`）

- 主图（`image_path`）= `<image1>`，且**决定输出宽高比与编辑基准**。
- `additional_images` 依次映射 `<image2>` / `<image3>` …。
- 因此槽位顺序是一等公民：主图应放“要保留身份的主体”（第三轮即人物）。

第三轮采用的顺序：

```
IMAGE_ORDER = ["person", "outfit", "scene"]
-> <image1>=person(fig3), <image2>=outfit(fig1), <image3>=scene(fig2)
```

### 2.4 脚本流水线（`_test_step2/`）

| 阶段 | 脚本 | 设备 | 产物 |
|---|---|---|---|
| 打标 | `wd_ab3_tags.py` | CPU | `wd_ab3_tags.json` |
| 构造 prompt | `wd_ab3_prompts.py` | CPU | `wd_ab3_prompts.json` |
| 扩散 | `wd_ab3.py [key]` | GPU | `wd_ab3*_A/B.png`, `*_result.json` |
| 拼图 + 指标 | `wd_ab3_analyze.py [key]` | CPU | `*_sheet.png`, `*_metrics.json` |

`key` = `round3`（tag `wd_ab3`）或 `round3_light`（tag `wd_ab3b`，追加光影句）。

指标定义：MAD / PSNR / SSIM（灰度 11×11 窗），对“源图缩放后”比较，
分 **full**（整幅）与 **center**（中心 50% 裁剪，近似主体区）。

---

## 3. 三轮实验记录

### 3.1 第一轮：单图换背景（batch1 / batch1b）

任务：`把背景换成中式园林（白墙黑瓦）`。

- **batch1**（fig4_grass，576×1024）
- **batch1b**（fig1_stage，672×1024，主体身上有文字/图案，用来测标签污染）
- 变体：`A1`（标签原样 + 模板句）、`A2`（标签去质量词 + 模板句）、`B`（LLM 重写）、`C`（纯指令，无主体描述）

现象：

- `A1 ≈ A2`，肉眼无差异；质量词 `realistic` / `photorealistic` 可去掉。
- **batch1b 的 B 换脸**，并丢失 T 恤图案 / 牌子 / 麦克风。
- `C`（无主体描述）背景换成功，但主体漂移最大。

### 3.2 第二轮：双图（人物 + 参考场景）（batch2）

任务：`保留 <image1> 人物，用 <image2> 场景作为新背景`。

- `A1`（标签 + 指令）对 `B`（LLM 重写）：A1 肉眼更稳，B 无增益。
- 指标：A1 center SSIM 0.0558，B 0.0672（数值 B 略高，但两者都很低，说明换了背景后“像源图”本身不是目标）。

### 3.3 第三轮：三图合成（人 + 衣 + 景）

任务：**图3人物 + 图1汉服 + 图2大雁塔夜景 + 挥手微笑**。

打标（threshold 0.5）：

```
person: 1girl, solo, long hair, looking at viewer, smile, black hair, brown eyes, upper body, clothes writing, realistic, asian, photorealistic
outfit: 1girl, solo, black hair, long sleeves, outdoors, parted lips, chinese clothes, red lips, hanfu
scene : outdoors, multiple boys, tree, night, building, scenery, 6+boys, city, lamppost, people, real world location, tokyo (city)
```

角色过滤（`ROLE_DROP`）后保留：

```
person: 1girl, solo, long hair, looking at viewer, smile, black hair, brown eyes, upper body, clothes writing, asian
outfit: long sleeves, chinese clothes, red lips, hanfu
scene : outdoors, tree, night, building, scenery, city, lamppost, real world location, tokyo (city)
```

对比：

- **A**：`Make the girl in <image1> wear the clothes from <image2> and place her in the scene from <image3>, waving and smiling at the camera.`
- **B**：把上表 `person` / `outfit` / `scene` 标签按角色嵌进同一句式。

结果：**A / B 四项意图全中**（人物=图3、服装=图1汉服、背景=图2夜景、动作=挥手微笑）；
A 的脸更贴原图，B 的脸略“美化/偏尖”；肉眼几乎无差异。

### 3.4 第三轮补测：尾部光照句（round3_light）

在 A、B 末尾同时加：

```
Blend the lighting and shadows naturally between the person and the background.
```

结果：

- 四项意图仍全中。
- 人物身上出现环境暖光（脸 / 肩 / 白袍被夜景灯染色），过渡更自然；
  **代价**：白汉服的“白”被环境光偏移（略偏黄），人脸被暖光染色。
- prompt 变长后耗时 +0.3~0.8s（A 24.14s，B 24.76s）。

---

## 4. 数据汇总

SSIM 对比源图（center = 中心 50% 裁剪；越高 ≠ 越好）。

| 轮次 | 尺寸 | A1 | A2 | B | C |
|---|---|---|---|---|---|
| batch1 | 576×1024 | .1582 | .1632 | **.6024** | .3877 |
| batch1b | 672×1024 | .3768 | .3856 | **.3980** | — |
| batch2 | 576×1024 | .0558 | — | .0672 | — |
| round3 | 1024×1024 | .1611 | — | .1733 | — |
| round3_light | 1024×1024 | .1630 | — | .1650 | — |

**关键反例**：batch1 的 B center SSIM 最高（.6024），但实际是它过度贴合源图（少改动）；
batch1b 的 B 换脸却 SSIM 最高。结论：**SSIM 反映“像不像源图”，不反映“编辑是否正确/主体是否保真”**。

耗时（1024 长边，steps 30）：单图约 9~11s，双图约 11s，三图约 24s；三图 + 光照句约 24.8s。

---

## 5. WD Tagger 局限清单（实测）

1. **夜景颠覆主体**：无人夜景打出 `multiple boys` / `6+boys` / `people`。
2. **地标误判**：大雁塔 → `building` / `city`（未识别 `pagoda`）。
3. **跨角色污染**：服装图混入 `1girl` / `solo` / `black hair`。
4. **质量词冗余**：`realistic` / `photorealistic` 对编辑无正向作用。
5. 本轮 fig1 **未打出** `watermark` / `text`，故“清洗文字标签”的价值未验证。

应对：**按角色分配 + 黑名单过滤**（`wd_ab3_prompts.py` 的 `ROLE_DROP`），
A 侧则可只保留人物/服装/场景的少量关键词。

---

## 6. 与模板系统（`Template/commands.json`）的衔接

现有命令模板（QW21edit，支持 `single` / `multi` 变体、`{description}` 占位、`<imageN>` 引用）：

| 命令 | 语义 |
|---|---|
| `/换背景` | 保留人物姿势，单图直接换 / 双图参考场景 |
| `/换装` | 单图文字换装 / 双图参考服装 |
| `/合照` | 多主体合成，要求各自身份独立 |
| `/生成` | 文生图（t2i） |
| `/去水印` `去物体` … | 固定指令 |

**WD 标签的定位建议**：

- 模板的**主体骨架仍用短自然语言**（实测 A 最稳），WD 标签只作为 `{description}` 的**可选补充**。
- 若要引入 WD，应新增“带标签”变体，并**由角色过滤后的标签**填充，例如：

```json
{
  "name": "/三图合成",
  "params": ["description"],
  "variadic": true,
  "tool": "QW21edit",
  "defaultVariant": "main",
  "variants": {
    "main": "Use the person from <image1> as the subject. Dress her in the clothes from <image2>. Place her in the scene from <image3>. {description}. Keep the facial identity and pose.",
    "main_light": "Use the person from <image1> as the subject. Dress her in the clothes from <image2>. Place her in the scene from <image3>. {description}. Keep the facial identity and pose. Blend the lighting and shadows naturally between the person and the background."
  },
  "description": "三图合成：人物 + 服装 + 场景（可选光影融合）"
}
```

- 若确实要用 WD：模板层只接受 **person / outfit / scene 分桶**的标签，
  并在拼接前跑一遍 `ROLE_DROP` 级别的过滤；**绝不把整串 WD 标签直接喂给模型**。

---

## 7. 推荐落地提示词（经实测）

**主模板（三图人+衣+景）**

```
Use the person from <image1> as the subject.
Dress her in the clothes from <image2>.
Place her in the scene from <image3>.
{动作/表情描述}.
Keep the original facial identity and body shape.
```

**可选尾缀（风格开关）**

```
Blend the lighting and shadows naturally between the person and the background.
```

**约束**

- 主图（`<image1>`）必须是“要保身份的主体”，它决定输出比例与基准。
- 主体描述用短句；动作/表情写清楚（`waving and smiling at the camera`）。
- 不要把分辨率、画幅、`realistic` 等元信息写进 prompt。

---

## 8. 注意事项 / 项目铁律

- Z29 / Z30：**未经确认不得启动 GPU 工作**；GPU 启动前必须确认空闲；LLM 与 diffusion 不同时运行。
- 仅追加文档：`FROZEN` / `DEVLOG` / `ACCEPTANCE` 既有行不得修改。
- Z8：单文件 < 600 行。
- 三轮实验均**不改产品代码**，只在 `_test_step2/` 写实验脚本。
- 本轮 B 为“WD 标签组装”，**全程未调用 LLM**（无需 `llmctl`）。

---

## 9. 产物清单（`_test_step2/`）

**第三轮主实验**

- `wd_ab3_src/{ab3_person_3.jpg, ab3_outfit_1.jpg, ab3_scene_2.jpg}` 源图（**已清理**，见文末「源图清理说明」）
- `wd_ab3_tags.py` / `.json`，`wd_ab3_prompts.py` / `.json`
- `wd_ab3.py`（GPU）/ `wd_ab3_result.json`
- `wd_ab3_{A,B}.png` / `wd_ab3_sheet.png` / `wd_ab3_metrics.json` / `wd_ab3_analyze.py`

**第三轮光照补测**

- `wd_ab3b_{A,B}.png` / `wd_ab3b_sheet.png` / `wd_ab3b_metrics.json` / `wd_ab3b_result.json`

**第一、二轮**

- `wd_ab_tags.py` / `.json`，`wd_ab_prompts.py` / `.json`，`wd_ab_llm.py`
- `wd_ab.py` / `wd_ab_result.json`，`wd_ab_analyze.py` / `wd_ab_metrics.json`
- `wd_ab_sheet_{batch1,batch1b,batch2}.png` 及各变体输出

---

## 10. 源图清理说明（2026-09-27，只增）

- **已清理**（WD 实验链退役）：`_test_step2/wd_ab_src/*`（fig1–4）、`_test_step2/wd_ab3_src/*`（ab3_*）、`_test_step2/WD/360.jpg` 源图，以及 `wd_ab*` / `wd_ab3*` / `360_ab*` 的 `_A`/`_B`/`_sheet`/`_warmup` 输出 PNG 与 `danbooru_groups/` 缓存、`360_*` 词表链。
- **本文 §3 引用的图名**（fig1_stage / fig2_garden_v / fig3_garden_h / fig4_grass、ab3_outfit_1 / ab3_scene_2 / ab3_person_3）与 **§9 产物图**（`wd_ab3_{A,B}.png` / `*_sheet.png` 等）对应文件已删除。
- **重现**：保留的脚本（`wd_ab*.py` / `wd_ab3*.py` / `wd_ab_llm.py`）与结论 JSON（`*_result/metrics/tags/prompts.json`）仍在 `_test_step2/`；重现时需**自备类似尺寸图片**（源图描述见 §3 实验记录）。
- **未删**：`python/server/tagger.py` + `config.py` 的 `TAGGER_*`（L1 能力代码）与本文档（历史记录）保留。
