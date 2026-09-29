# 管线安全基线（PIPELINE_SAFETY）

> **定位：动管线前的护栏（长期约束），不是一次性事件复盘。**
> 适用读者：要改 `pipeline` / `model_loader` / `config`、要加插件、要调 comfy / torch / comfy_aimdo 参数的人。
> **动手前先花 5 分钟过第 2 节检查清单。**
> 本文为新增文档，不改既有 DOC 行（引用 FROZEN / DEVLOG / OPTIMIZATION 只加指向）。
> 记录日期：2026-09-29。数据均来自本仓库 `_test_step2/` 下的实测（gitignore）。

---

## 1. 一句话结论 + 闭环状态

**主根因（闭环）**：ZIV 进程内直连 ComfyUI 时**未复现官方 `main.py:44 import cuda_malloc`**，缺少
`PYTORCH_CUDA_ALLOC_CONF=…backend:cudaMallocAsync`；torch 退回 native caching allocator，释放块被缓存池滞留
（`reserved →26708MB`、驱动 `dev_free→0`），comfy_aimdo 在「显存告急」假象下持续把权重换页到 host
（`aimdo_vram →26992MB`），host RSS 34GB、每步 15–17s → **1536 thrash**。补上 `backend:cudaMallocAsync`
即消除（73.3s/34GB → 22.5s/11.7GB）。

**残差（闭环，非 thrash）**：ZIV @1536 干净残差 **host-pinned ≈ 7977MB**，RSS 11.9GB vs 官方 3.2GB。
构成（2026-09-29 复核修正）：**≈7432.5MB = Qwen-Image-2.1 prefix K/V 缓存**
（`PoseBranchCache`，`comfy/ldm/wan/model_animate2.py:187-189`；**32 条 = 32 个 transformer block**
× 232.3 MiB，`comfy/ldm/qwen_image21/model.py:196 num_layers=32`）；**≈0.35–2.6GB = DiT 模块 hostbuf pin**
（`comfy.pinned_memory`，`ops.py:231`）。margin 在 §2 安全线（28GB）内，不 thrash。

**闭环状态一览**

| 环 | 状态 |
|---|---|
| 缺 cudaMallocAsync → native → reserved 滞留 → dev_free→0 → 换页 → thrash | **闭环**（代码 + 实测 A0b/A1） |
| 补 cudaMallocAsync → 修复 | **闭环**（A1） |
| 残差 = host-pinned 7977MB / 32 张量 | **闭环**（零污染读 + A/B） |
| 「为什么 ZIV 走 pinned、官方走 VBAR（`signature is None`）」 | **闭环**（见 §3 环④；`vbar_fault` 返回 `VBAR_FAULT_OOM`） |

---

## 2. 动管线前检查清单（核心）

| 动作 | 判断 | 理由 / 证据 |
|---|---|---|
| 对齐官方 `main.py` 已设的 env（`PYTORCH_CUDA_ALLOC_CONF` 等），且每项 `ZIV_AI_*` 门控、可单关 | ✅ 可动 | `Comfyui/ComfyUI/main.py:44`；`config.apply_official_env()`；实测 A1：73→22.5s、RSS 34→11.7GB |
| 因「显存/内存高」去改 `disable_smart_memory` | ❌ 不要 | aimdo 下 inert：`model_management.py:1114-1115`（aimdo 先返回 cpu）、`:914-918`（动态模型 `memory_to_free=0`）；实测 V3a smart 开 23.3s ≈ smart 关 22.5s |
| 在 aimdo 下用 `get_free_memory()` **绝对值**做决策 | ❌ 不要 | 口径含 torch 缓存：`model_management.py:1814-1820`（`= mem_get_info + reserved − active`），native 下虚高 |
| 关 `use_sage_attention` 去「省内存」 | ❌ 不要 | 实测 A3：关掉更慢（26.9s vs 22.5s），RSS 不变（11.81 vs 11.69） |
| 关 `disable_pinned_memory` 去省 RSS | ⚠️ 慎重 | 实测 V5b：RSS 11.7→9.6GB 但 wall 22.5→25.1s；且不解决 thrash |
| 挂 Python 函数钩子/patch 去量 comfy 内部状态 | ⚠️ 会污染 | 实测：marks-only（零 patch）pinned=0、RSS 4.26；含 patch 时 pinned=3368；早期全开 8172 |
| 在 `control.init()` 之前 import `comfy.pinned_memory` / `comfy.model_management` | ❌ 不要 | `comfy_aimdo.host_buffer` 在 import 期绑定 `lib = control.lib`，过早 import 会冻结成 `None` → 后续 `hostbuf_allocate` AttributeError（本轮实测）；须先走 `model_loader.prepare_environment()` |
| 用「只读模块全局变量」观测（`TOTAL_PINNED_MEMORY` / `len(PINNED_MEMORY)`） | ✅ 零污染 | 实测 A/B：22.6s/12.0GB vs 22.4s/11.9GB（差 0.8%） |
| 用外部 `watch.ps1` 观测 RSS / commit / nvidia-smi | ✅ 零污染 | `E:\temp\opencode\watch.ps1`；越线 `taskkill /T /F` |
| 用 forward-hook 侧支路给模型加插件（如 Viggle） | ⚠️ 额外驻留 | 实测：Viggle 侧支路 +2.27GB host；`plugin_packs/qwen21-viggle-6step/__init__.py:82-98` |
| @1536 使用多参考图（`/换背景`） | ⚠️ 成本高 | 实测：ZIV 第 2 张 ref +5.76GB host（11.69 vs 5.93GB） |
| 动 `pipeline._encode` / `model_loader` 加载路径前 | ⚠️ 必读本文 + 对照官方 | 加载/pin 路径敏感；`annotate_state_dict`（`utils.py:204`）与 `fast_disk`（`storage.py:104-111`）影响 |
| 高分辨率（≥1536）改动必须真机对照 + 看门狗 | ⚠️ 必须 | ZIV@1536 会触发 pin/换页；见 §4 |
| 改官方 `cli_args` / `model_management` / comfy_aimdo 调用 | ❌ 不要 | 一律用 env 在入口对齐，不改官方逻辑 |
| 运行无过滤 `dotnet test` | ❌ 禁止 | 会拉 GPU；只用非 GPU filter（`FullyQualifiedName!~Ipc`） |
| 一次只动一个变量做 A/B，并先 dump 当前 env 快照 | ✅ 必须 | 交互效应（如 cm × temporaral pin）会误导结论 |

（共 16 条：可动 4 / 不可 6 / 需慎重 6。）

---

## 3. 完整因果链（每环三层证据；闭环/推断显式标注）

| 环 | 机制 | 代码证据 | 实测数据 | 社区证据 | 状态 |
|---|---|---|---|---|---|
| ① 缺 cudaMallocAsync | ZIV 无官方入口的 `import cuda_malloc` | `main.py:44`；`cuda_malloc.py`（设 `PYTORCH_CUDA_ALLOC_CONF`） | 官方 `cuda_malloc=True`、conf 含 backend；ZIV 未设 | PR #12557（见 §6 注） | **闭环** |
| ② native allocator 滞留 | 原生缓存池滞留已释放块 | — | A0b：`reserved`→26708MB | — | **闭环** |
| ③ 驱动 dev_free→0 | 设备侧无空闲 | `mm.py:1814-1820` 口径 | A0b 时间序列 `dev_free→0` | — | **闭环** |
| ④ comfy_aimdo 换页 + host-pin 回退 | aimdo 压力下换页；ops 时 pin 回退 | `ops.py:227-236 handle_pin`；`:231 if signature is None or not fast_disk or high_ram`；`pinned_memory.py:71/107/125` | A0b `aimdo_vram`→26992；零污染读 ZIV pinned **7977MB** ＝ prefix K/V 缓存 7432.5MB（32 block × 232.3MiB）＋ DiT hostbuf pin 0.35–2.6GB | 见 §6 注 | 换页闭环；**pin 分支闭环**（`vbar_fault`→`VBAR_FAULT_OOM`） |
| ⑤ host RSS 膨胀 | 权重落 host | `mm.py:1592-1594` 计数 | A0b RSS→27.9GB；ZIV@1536 11.9GB | — | **闭环** |
| ⑥ 每步 PCIe 重拷 thrash | 工作集反复换入换出 | — | A0b 步时 15–17s；@1024 ~0.7s/步 | — | **闭环** |
| ⑦ 补 cudaMallocAsync 修复 | 释放即时归还驱动 | `apply_official_env` | A1：22.5s/11.7GB；A0b/A1 `reserved` 26708→6976 | — | **闭环** |

> 环④的「pin 回退」（2026-09-29 复核修正）：分支持续 pin 的条件在 `ops.py:231`。零污染读实测：pin 落 subset
> **`weights-fast`**（`ops.py:188` 仅在 `fast_disk=true` 时选它）→ `not fast_disk` 排除；`high_ram=false` 排除
> → 只剩 `signature is None`。被 pin 的 DiT 模块（step1 22 个 / pre_decode 78 个）其 `_v_signature` **全为 `None`**
> （`ops.py:292` 由 load 期 `vbar_fault` 结果赋值）→ 即 `vbar_fault` 返回 `VBAR_FAULT_OOM(1)`（`model_vbar.py:84-94`）。
> **闭环**。旧「一次带 patch 观测 `vbar_none=0`」属**探针污染**，不作为反证（见 §6 教训 3）。

### 为什么「pin」不等于「thrash」（重要）

- **native（cm=0）**：pinned 9562MB，**且**另有 native 的 `reserved`/host-staging 失控 → RSS 34GB → thrash。
- **cudaMallocAsync（cm=1）**：pinned **仍有 7977MB**（不消除 pin），但 `reserved` 不失控 → RSS 11.9GB，不 thrash。
- → **pin（~8GB）是残差**；**thrash 是 native 的 reserved 失控**（cudaMallocAsync 修的是后者）。

---

## 4. 实测数据（关键几组）

环境：Windows / RTX 4080 16GB / ComfyUI 0.37.0 / torch 2.13.0+cu130 / comfy-aimdo 0.5.5。
负载：Viggle 6-step + 双 ref；看门狗 RSS 28GB / wall 270s。

**A0 vs A1（@1536 双 ref + Viggle）**

| 组 | allocator | wall | 峰值 VRAM | 峰值 RSS | reserved 峰 | dev_free | 步时 |
|---|---|---|---|---|---|---|---|
| A0 | native | 73.3s（KILL）/ A0b 51.7s（KILL） | 16019 | 34.1 / 27.9GB | 26708 | →0 | 15–17s |
| **A1** | cudaMallocAsync | **22.5s** | 12909 | **11.69GB** | 6976 | 2.6–3.3GB | ~1.4s |

**M1–M3 时间序列**：native → `reserved` 8478→16298、`dev_free`→0、`aimdo`→16566；
cudaMallocAsync → `reserved` 恒 6976、`dev_free` 2.6–3.3GB、`aimdo` 停在 ~6.7GB。

**零污染读（只读 `TOTAL_PINNED_MEMORY` / `len(PINNED_MEMORY)`，A/B 无污染）**
（2026-09-29 复核：`N` 是 `len(mm.PINNED_MEMORY)`——即 **Qwen prefix K/V 缓存条数 = transformer block 数 32**，
不是权重张量数；`pinned` 为该缓存 + DiT 模块 hostbuf pin 之和。）

| 组 | allocator | pinned @step1 | N（=prefix K/V 条数） | RSS 峰 | wall |
|---|---|---|---|---|---|
| ZIV @1536 | cudaMallocAsync | **7977.5MB** | **32** | 11.9GB | 22.4s |
| ZIV @1024 | cudaMallocAsync | **3368.5MB** | **32** | 5.6GB | 10.6s |
| 官方 @1536 | cudaMallocAsync | **0** | **0** | 3.2GB | 14.0s |
| ZIV @1536 | native | 9562.7MB | 32 | →28.2GB **thrash** | KILL |

**官方参照**：O1（1 ref）14.0s/3.08GB；O2（2 ref）19.2s/2.93GB；O2b（ref res 1536）14.2s/2.62GB；
O3（@1024）4.0s/2.80GB；O4（2 ref 无 Viggle）6.0s/2.83GB。

**残差分解（@1536，cudaMallocAsync）**：ZIV 11.69GB = 官方 2.93GB + 第2 ref 5.76GB + Viggle 2.27GB（≈8.0GB）。
二者均 ZIV 管线特有（官方对应项 ≈0）。**（2026-09-29 复核修正）**：其中「第2 ref 5.76GB」主要是
**prefix K/V 缓存**（`PoseBranchCache`）落 CPU pinned——`qwen_image21/model.py:250-258` 在 spare VRAM
不足时把 store 设为 `cpu`，与 ref token 数成正比（`@1024` pinned 3368.5MB vs `@1536` 7977.5MB 同 32 条即可见）；
**不是 DiT 权重**。真正的环④产物只有 DiT 模块 hostbuf pin（0.35–2.6GB）。

**探针污染实测（@1024）**：marks-only（零 patch）= pinned 0 / RSS 4.26；含 patch = pinned 3368；
早期全开 = pinned 8172 / RSS 12.62。→ 量 pin 必须用零污染读法。

---

## 5. 官方 vs ZIV 差异表（当前状态）

| 项 | 官方 | ZIV | 差异性质 | 处理 |
|---|---|---|---|---|
| `PYTORCH_CUDA_ALLOC_CONF` | `…backend:cudaMallocAsync` | 同（`apply_official_env`） | **主根因** | **已对齐**（默认开，`ZIV_AI_CUDA_MALLOC_ASYNC=0` 可关） |
| `MIMALLOC_PURGE_DELAY` | 0 | 0（门控默认开） | 无实质影响（A2） | 已对齐 |
| `CUDA_VISIBLE_DEVICES` | 0（nt 单卡） | 门控默认关 | 平台/硬件绑定 | **保留默认关**（单卡 no-op） |
| `disable_smart_memory` | False | True | aimdo 下 inert | **已保留**（不改，见 §6 教训） |
| `use_sage_attention` | False | True | ZIV 独有且有益 | **已保留** |
| `disable_pinned_memory` | False | False | 一致 | 一致 |
| `async_offload` | None(→2 streams) | None | 一致 | 一致 |
| `vram_headroom`/`reserve_vram` | 0/None | 0/None | 一致（方案 1 注入实测无效） | 一致 |
| prefix K/V host-pin 行为 | `pinned=0` | `pinned≈7.4GB`＝prefix K/V 缓存（32 block；`model_animate2.py:187-189`） | ZIV 余量紧 → store 落 cpu pinned；**非 DiT 权重** | **待观察**（§6 未决 1/2） |
| 插件 LoRA 实现 | 节点 weight-patch | 运行时 forward-hook 侧支路 | +2.27GB | **待观察** |

---

## 6. 教训 + 未决

### 教训（必记）

1. **历史数据不能外推新架构**：Step 3「关 smart memory 省 43% 显存」是 **pre-aimdo**（512²）结论；
   aimdo 下 `disable_smart_memory` 几乎 inert（`mm.py:1114-1115`、`:914-918`）。把 1536 thrash 归因于它是**误判**。
   （历史记录见 `DOC/DEVLOG.md` Step 3 段、`DOC/FROZEN.md` Step 3.4——仅指向。）
2. **官方入口每一行 env 都有意义**：缺 `cudaMallocAsync` 一行 → allocator 行为改变 → thrash。
3. **探针会污染**：挂 Python 钩子量 comfy 内部状态本身改变行为（marks-only 0 vs patch 3368 vs 全开 8172）。
   量内部量用「只读模块全局变量」或外部观测。
4. **静态分析只能给假设，数据才能定位**：`signature is None` 曾因一次**带 patch** 的观测（`vbar_none=0`）
   被误判为冲突；零污染读（pinned 模块 `_v_signature is None`）证实它成立（§3 环④ 已闭环）。
5. **社区是参照**：aimdo 的 allocator/pinned 决策有公开讨论与改动历史。

### 未决（诚实标注）

| # | 未决项 | 状态 |
|---|---|---|
| 1 | ZIV 为何走 pinned 分支（`vbar_fault → None` 的微观机制） | **闭环**（2026-09-29 复核）：`vbar_fault` 返回 `VBAR_FAULT_OOM`；pin 落 `weights-fast`、`fast_disk=true`、`high_ram=false` 已实测排除 |
| 2 | 那 32 个 pinned 张量属 DiT 还是 TE（`_pins` key） | **已定位**：32 = Qwen-Image-2.1 的 32 个 transformer block 的 **prefix K/V 缓存**（`PoseBranchCache`），非权重；`_pins`（模块 hostbuf）另属 DiT |
| 3 | 生产路径（`python/server/main.py` IPC 端到端）`apply_official_env` 生效性 | 仅验证「import main 后 env 正确」，未跑端到端 |
| 4 | 残差在其他负载（单 ref / 无 Viggle / 其他分辨率）下是否变化 | 部分实测（1 ref 5.93 / 无 Viggle 9.42），未系统化 |
| 5 | 社区 PR/Issue 编号的具体内容 | **用户提供编号，本轮未联网独立核实**（见下注） |

> 社区参照（**未核实**，勿据此下结论）：comfy-aimdo PR #12557（cudaMallocAsync 支持 / dynamic_vram 决策）、
> ComfyUI Issue #11106（cudaMallocAsync + pinned 冲突）、Issue #14250（pinned budget 溢出）。
> 上述编号由用户提供；本报告的结论**不依赖**它们，仅作指向。

---

## 附录

### A. 关键代码引用

| 主题 | 位置 |
|---|---|
| 官方 env / allocator | `Comfyui/ComfyUI/main.py:41-114`（`:44 import cuda_malloc`、`:71-81 control.init`）；`cuda_malloc.py` |
| pin 分支 | `comfy/ops.py:167 vbar_fault`、`:227-236 handle_pin`、`:231 条件` |
| pin 执行 | `comfy/pinned_memory.py:71`（入口）、`:107`（`cudaHostRegister`）、`:125`（`TOTAL_PINNED_MEMORY+=size`） |
| 全局计数 / 上限 | `comfy/model_management.py:1592-1594`、`:1613-1621`（MAX=ram*0.40≈39285MB） |
| free_memory 口径 | `comfy/model_management.py:1781`、`:1814-1820` |
| smart memory 消费点 | `comfy/model_management.py:599/601/912/1121`（aimdo 早返回 `:1114-1115`；动态 `:914-918`） |
| Dynamic 不在 base pin | `comfy/model_patcher.py:1819-1820`（抛异常，"deferred to ops time"） |
| fast_disk 判定 | `comfy/storage.py:104-111`；`comfy/utils.py:204 annotate_state_dict` |
| ZIV 运行时默认 | `python/server/model_loader.py:70-88`（apply）、`:136-181`（aimdo 引导）、`:186-227`（load_*） |
| ZIV env 对齐 | `python/server/config.py` `apply_official_env`；`python/server/main.py`；`model_loader.prepare_environment` |
| 插件 forward-hook | `plugin_packs/qwen21-viggle-6step/__init__.py:82-98` |

### B. 复现（脚本在 `_test_step2/`，gitignore）

```
# 干净（零 Python 钩子，仅外部 watch.ps1）
ZIV_AI_CUDA_MALLOC_ASYNC=1 REPRO_VIGGLE=1 REPRO_MULTI=1 REPRO_SIDE=1536 REPRO_STEPS=40 \
REPRO_TAG=<tag> REPRO_OUT=D:\temp\repro_<tag>.png python _test_step2\run_clean.py
# 零污染读 pinned（on_progress 回调只读模块全局变量）
python _test_step2\run_clean_read.py
# 看门狗
pwsh -File E:\temp\opencode\watch.ps1 -TargetPid <pid> -MaxRssGB 28 -MaxWallSec 270
```
官方侧：`_test_step2/start_official.py` + `off_submit.py`（临时 custom node 记录全局变量，事后删除）。

### C. 相关文档（指向，不改原文）
- `DOC/THRASH_ROOTCAUSE.md` —— 1536 thrash 根因报告（本文的详细版）。
- `DOC/FROZEN.md` —— Step 3.4（smart memory 决策）、Step 6（DynamicVRAM）。
- `DOC/DEVLOG.md` —— Step 3（512² smart memory 实测）、方案 1（VRAM 余量注入无效）段。
- `DOC/OPTIMIZATION.md` —— §6.2（aimdo 下 1536 单图峰值 13091）。

---

## 7. E_E2 修正（2026-09-29，尾部追加；不改既有行）

> **更正：§4「零污染读」表中「官方 @1536 pinned=0 / 3.2GB」、§5「prefix K/V host-pin 行为」中
> 「官方 pinned=0」、§6 未决 4 中「官方对应项 ≈0」均为无效数据（见下）。**

- 无效原因：官方 harness（`_test_step2/off_submit.py`）的 `TextEncodeQwenImage21` 参考图用**嵌套** dict，
  而 canonical `/prompt` 输入 id 为**扁平** `images.image_N`（`nodes_qwen.py:125-133`；`execution.py:296`）
  → ref 未进模型 → prefix 仅 64 token → 官方跑成文生图。
- E_E2（修正 harness、真 2-ref）：**官方 `store=cpu` / `prefix_len=18480` / `pinned_n=32` /
  TOTAL_PINNED 峰 11597MB / 峰值 RSS 14.0GB**（对照 ZIV E_A：store=cpu / 14865 / 32 / 7977.5MB / 11.9GB）。
- **结论**：官方也落 CPU pinned → 残差 = **16GB 卡固有限制**（`get_free_memory(cuda) > 4×cache_bytes`
  永不满足，`comfy/ldm/qwen_image21/model.py:250-258`）；ZIV 无特有问题。
- **决策**：不引入 `qwen_image21_cache` 门控，保持 `auto`（备选 int4 / off 仅在明确需要时）。
