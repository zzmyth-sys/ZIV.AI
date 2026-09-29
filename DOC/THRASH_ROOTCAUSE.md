# ZIV @1536 thrash 根因报告（THRASH_ROOTCAUSE）

> **定位：动管线前的必读文档（安全基线，非事后复盘）。**
> 后续任何人要改 `pipeline` / `model_loader` / `config` / comfy_aimdo 相关逻辑前，**先过第 2 节的护栏清单**。
> 范围：只读静态分析 + GPU A/B（用户授权）。本文为新增文档，不改既有 DOC 行（引用 FROZEN/DEVLOG 只加指向）。
> 调查日期：2026-09-29。

---

## 1. 一句话结论

**ZIV 进程内直连 ComfyUI 时没有复现官方 `main.py` 的 `import cuda_malloc`，因此缺少
`PYTORCH_CUDA_ALLOC_CONF=...,backend:cudaMallocAsync`；torch 退回原生 caching allocator，滞留已释放块使
驱动侧空闲显存（`dev_free`）归零，comfy_aimdo 在「显存告急」的假象下持续把权重换页到 host，造成 @1536
thrash。补上 `backend:cudaMallocAsync` 即消除（73.3s/RSS 34GB → 22.5s/RSS 11.7GB）。**

一句话机制：**allocator 决定「释放的内存是否立刻归还驱动」→ 决定 aimdo 看到的设备压力 → 决定它换不换页。**

---

## 2. 动管线前的检查清单（护栏）

动 `pipeline` / `model_loader` / `config` / aimdo 相关逻辑前，逐条确认：

**可以**
1. 对齐**官方已设**的进程 env（`PYTORCH_CUDA_ALLOC_CONF` / `MIMALLOC_PURGE_DELAY` / `CUDA_VISIBLE_DEVICES`），
   每项用 `ZIV_AI_*` 门控、可单独关闭。
2. 改动前后用 P1 探针采 `reserved / dev_free / get_free / aimdo_vram / RSS`（脚本见 §11）。
3. 一次只动一个变量做 A/B，并记录 wall / 峰值 VRAM / 峰值 RSS / 步时。
4. 动高分辨率（≥1536）路径时，先复跑 `A1` 配置确认基线健康。

**不可以**
5. **不要**因「显存/内存高」去改 `disable_smart_memory`（aimdo 下 inert，见 §9）。
6. **不要**在 aimdo 启用时用 `get_free_memory()` 的**绝对值**做决策——它含 torch 缓存口径，native 下虚高（§6）。
7. **不要**关 `use_sage_attention` 去「省内存」——实测更慢且不减 RSS（A3）。
8. **不要**改官方 `cli_args` / `model_management` / comfy_aimdo 调用；缺失逻辑用 env 在入口补齐。
9. **不要**把 pre-aimdo 时代的历史实测（如 Step 3 的 512² 数据）外推到 aimdo 架构（§9）。
10. 任何 host RSS > 28GB / 单步 > 5s 的运行，视为回归，立即停下定位。

---

## 3. 症状

| 项 | 现象 |
|---|---|
| 触发条件 | ZIV @**1536** + **双参考图**（`/换背景`）+ **Viggle** |
| wall | 73.3s（另一次 51.7s 被看门狗强杀） |
| 步时 | 首步 ~6s，随后 **15–17s/步**（正常 ~1.4s/步） |
| host RSS | **34GB**（正常 ~3–12GB） |
| 峰值 VRAM | 16019 MiB（贴近 16376 卡上限） |
| 表现 | 采样持续 PCIe 换页，机器接近卡死（历史中卡死 ×2，需强关） |

对照（均实测）：
- ZIV **@1024** + 双 ref + Viggle：**11.5s**，正常。
- 官方 ComfyUI **@1536** + 双 ref + Viggle（O2）：**19.2s / RSS 2.93GB**，正常。

---

## 4. 完整因果链（每环有证据）

```
ZIV 进程内直连 ComfyUI，未执行官方 main.py 的 __main__ 段
  ① 未 import cuda_malloc                     → 官方 main.py:44；ZIV 无等价
  ② PYTORCH_CUDA_ALLOC_CONF 缺 backend:cudaMallocAsync → cuda_malloc.py 只在官方入口设；ZIV 未设
  ③ torch 使用原生 caching allocator           → 探针 alloc_backend=native（A0b）
  ④ 已释放块被缓存池滞留 → reserved 膨胀        → 实测 reserved 峰 26708 MB（A0b）
  ⑤ 驱动侧空闲 dev_free → 0                    → 实测 dev_free_min = 0（A0b）
  ⑥ comfy_aimdo 设备压力持续「告急」            → control.init(nvml_pressure=True)，control.py:62/68/118
  ⑦ 持续 VBAR 换页，权重驻留到 host 暂存         → 实测 aimdo_vram 峰 26992 MB（A0b）
  ⑧ host staging 填满（≈2× staged 模型 ≈33GB）  → mm.py:1625-1628 pinned_hostbuf_size=min(size,ram*0.4)*2
  ⑨ host RSS 34GB                              → 实测 RSS 峰 27.9GB @51.7s 且仍在涨（A0b）
  ⑩ 每步经 PCIe 重拷整份工作集 → 15–17s/步      → steps: 15.5/22.0/35.1/50.2（间隔递增，A0b）
```

关键中间口径（`comfy/model_management.py:1814-1820`）：
```
get_free_memory = mem_free_cuda(驱动 free) + (reserved_bytes.all.current − active_bytes.all.current)
```
- native 下 `reserved` 被已释放块撑大，`reserved − active` 虚高 → `get_free_memory` 报「有很多空闲」，
  但**驱动 `dev_free`=0**；aimdo 看设备压力 → 持续换页。
- cudaMallocAsync 下释放即时归还驱动 → `reserved`≈实占、`dev_free` 非零 → aimdo 停止过度换页。

> 注：动态模型在 `free_memory()` 中 `memory_to_free=0`（`mm.py:914-918`），所以 `DISABLE_SMART_MEMORY`
> 与 `free_memory()` 的卸载决策无关；aimdo 的换页由设备压力驱动，不受该标志影响。

---

## 5. 实测数据（全表）

环境：Windows / RTX 4080 16GB / ComfyUI 0.37.0 / torch 2.13.0+cu130 / comfy-aimdo 0.5.5。
负载：Viggle 6-step + 双 ref（`REPRO_MULTI=1`）+ `REPRO_STEPS=40`（插件接管为 6 步 sigmas）。

| 组 | 配置（相对官方） | side | wall | 峰值 VRAM | 峰值 RSS | 步时 | aimdo 峰 | 结果 |
|---|---|---|---|---|---|---|---|---|
| **A0**（E1，现状基线） | 全未设 | 1536 | **73.3s** | 16019 | **33.8GB** | 15–17s | 33172 | ❌ thrash（KILL） |
| A0b | native（明确 cm 关） | 1536 | **51.7s** | 16002 | **27.9GB** | 递增 6→15s | 26992 | ❌ thrash（KILL） |
| **A1** | **+cudaMallocAsync** | 1536 | **22.5s** | 12909 | **11.69GB** | ~1.4s | 9168 | ✅ 修复 |
| A2 | A1 + MIMALLOC_PURGE_DELAY=0 | 1536 | 23.6s | 12976 | 11.66GB | ~1.4s | 8718 | ➖ 无改善 |
| A3 | A1 + SageAttention 关 | 1536 | 26.9s | 12911 | 11.81GB | ~1.5s | 8930 | ➖ 更慢 |
| A4 | A1 + A2 + A3 全对 | 1536 | 27.1s | 12778 | 12.45GB | ~1.5s | 8816 | ➖ 无改善 |
| A5 | A1 @1024 | 1024 | 10.2s | 12190 | 5.56GB | ~0.7s | 8753 | ✅ 无回归 |
| — | ZIV @1024 基线（无 cm） | 1024 | 11.5s | 13484 | ~4.2GB | ~0.7s | — | ✅ |
| 官方 O1 | 官方 1 ref | 1536 | 14.0s | 15743 | 3.08GB | — | 13744 | ✅ |
| **官方 O2** | 官方 2 ref | 1536 | **19.2s** | 15759 | **2.93GB** | — | 14405 | ✅ |
| 官方 O2b | 官方 2 ref（ref res 1536） | 1536 | 14.2s | 15783 | 2.62GB | — | 14178 | ✅ |
| 官方 O3 | 官方 2 ref | 1024 | 4.0s | 13408 | 2.80GB | — | 11492 | ✅ |
| 官方 O4 | 官方 2 ref 无 Viggle | 1536 | 6.0s | 14720 | 2.83GB | — | 13029 | ✅ |

结论：**A1 是唯一「单独开启即修复」的配置**；A2/A3/A4 均未进一步改善（mimalloc 无用、sage 关更慢）。

### 复现命令（按 §11 脚本）

```
# 进程 env（门控）
ZIV_AI_PROBE=1  ZIV_AI_PROBE_RUN=<tag>
# A1：ZIV_AI_CUDA_MALLOC_ASYNC=1  ZIV_AI_MIMALLOC_PURGE_DELAY=0  ZIV_AI_SAGE_ATTENTION=1
# A0：ZIV_AI_CUDA_MALLOC_ASYNC=0
# 负载 env（repro）
REPRO_VIGGLE=1 REPRO_MULTI=1 REPRO_SIDE=1536 REPRO_STEPS=40 REPRO_TAG=<tag> REPRO_OUT=D:\temp\repro_<tag>.png
# 运行（看门狗 watch.ps1 随附，RSS 28GB / wall 270s 强杀）
python D:\devlop\ZIV.AI\_test_step2\run_experiment.py
```

---

## 6. 探针数据（机制证实）

P1 探针字段（`_test_step2/ziv_probe.py`，0.5s 一次，全部只读、绝不抛）：

| 字段 | 来源 | 含义 |
|---|---|---|
| `alloc_backend` | `torch.cuda.get_allocator_backend()` | 实际分配器 |
| `reserved_stat_mb` / `active_stat_mb` | `torch.cuda.memory_stats()` | torch 缓存池「已保留 / 活跃」 |
| `dev_free_mb` / `dev_total_mb` | `torch.cuda.mem_get_info()` | **驱动**侧空闲（aimdo/NVML 视角） |
| `get_free_mb` / `get_free_torch_mb` | `comfy.model_management.get_free_memory(torch_free_too=True)` | comfy 的自由量口径 |
| `aimdo_vram_usage_mb` | `comfy_aimdo.control.get_total_vram_usage()`（`control.py:268`） | aimdo 托管总量 |
| `rss_gb` / `gpu_used_mb` | ctypes / nvidia-smi | 进程 RSS / 整卡占用 |

**native（A0b）vs cudaMallocAsync（A2）关键差异（同负载）：**

| 指标 | native | cudaMallocAsync |
|---|---|---|
| `alloc_backend` | native | cudaMallocAsync |
| torch `reserved` 峰 | **26708 MB** | **6976 MB** |
| 驱动 `dev_free` | **→ 0** | 2670–2883 MB（非零） |
| `get_free`（=dev_free+reserved−active） | 9845 → 22953（虚高） | ~7.8–8.5 GB |
| `aimdo_vram_usage` 峰 | **26992 MB** | 6759 MB |
| host RSS 峰 | **27.9 GB** | 11.66 GB |

形态学（native 轨迹，A0b）：
```
t=20s  dev_free=763MB   reserved=11478MB  active=2397MB  get_free=9845MB   aimdo=14002MB  rss=7.8GB
t=28s  dev_free=0MB     reserved=17408MB  active=2342MB  get_free=15066MB  aimdo=17676MB  rss=17.4GB
t=48s  dev_free=0MB     reserved=25418MB  active=2464MB  get_free=22954MB  aimdo=25686MB  rss=26.5GB
```
→ `reserved` 随每步单调膨胀、`dev_free` 归零、`get_free` 虚高，而 `aimdo_vram`/`RSS` 同步上涨。
cudaMallocAsync 下 `reserved` 稳定在 ~7GB、`dev_free` 保持非零，链式反应不启动。

---

## 7. 主根因 + 修复

**主根因**：缺失 `backend:cudaMallocAsync`（= 官方 `main.py:44 import cuda_malloc` 的等效）。

**修复**：在 ZIV 进程入口补齐官方 env（只对齐官方已设项，不碰官方逻辑）：

| env | 值 | 门控 | 默认 |
|---|---|---|---|
| `PYTORCH_CUDA_ALLOC_CONF` 追加 `backend:cudaMallocAsync` | = 官方 | `ZIV_AI_CUDA_MALLOC_ASYNC` | 1（开） |
| `MIMALLOC_PURGE_DELAY=0` | = 官方(nt) | `ZIV_AI_MIMALLOC_PURGE_DELAY` | 1（开） |
| `CUDA_VISIBLE_DEVICES=0` | = 官方(nt 单卡) | `ZIV_AI_CUDA_VISIBLE_DEVICES` | 0（关；平台/硬件绑定，单卡为 no-op） |

**落点**：`python/server/config.py` `apply_official_env()`（幂等，保留既有 `PYTORCH_CUDA_ALLOC_CONF`）；
调用点：
- `python/server/main.py`：`import config` 之后、`import handlers`（会拉 comfy/torch）**之前**；
- `python/server/model_loader.py` `prepare_environment()` 最前（in-process 路径）。

**顺序约束**：`PYTORCH_CUDA_ALLOC_CONF` 必须早于首次 CUDA 初始化（实测：晚于 `import torch`、早于首次
CUDA 分配仍生效，但入口应尽量早设）。

---

## 8. 残差分析（未完全解释的部分）

A1 后仍有差距：ZIV **11.7GB / 22.5s** vs 官方 **2.9GB / 19.2s**（残差 ≈ 8.8GB / 3.3s）。

**修正（2026-09-29 复核；取代下方「候选（未验证）」第 1/2 条的归因）**：残差中 **≈7432.5MB = Qwen-Image-2.1
prefix K/V 缓存**落 CPU pinned——`PoseBranchCache`（`comfy/ldm/wan/model_animate2.py:187-189` 调
`comfy.model_management.pin_memory`），由 `comfy/ldm/qwen_image21/model.py:250-258` `select_prefix_cache` 在
「spare VRAM ≤ 4×cache_bytes」时把 store 设为 `cpu` 触发；**32 条 = 32 个 transformer block**
（`qwen_image21/model.py:196 num_layers=32`），每条 232.3 MiB（shape `(1,2,14865,32,128)` bf16），大小随
ref token 数变化（@1024 实测 3368.5MB / 同 32 条）。**另 ≈0.35–2.6GB = DiT 模块 hostbuf pin**
（`comfy.pinned_memory`，`ops.py:231`，由 `vbar_fault` 返回 `VBAR_FAULT_OOM` 触发）。**二者均非 DiT 权重。**
（旧述「32 个权重张量」不成立；「一次带 patch 观测 `vbar_none=0`」属探针污染，作废——零污染读显示被 pin 模块
`_v_signature is None`，即 `vbar_fault` 返回 OOM，环④ pin 分支**闭环**。）

**已排除**（A/B 实测）：

| 候选 | 组 | 结果 |
|---|---|---|
| `MIMALLOC_PURGE_DELAY` | A2 | 无改善（23.6s / 11.66GB） |
| SageAttention | A3 | 关掉更慢（26.9s / 11.81GB）→ 非残差，反而有益 |
| smart memory | 见 §9 | aimdo 下 inert |

**候选（未验证）**：
1. **in-process 持有**：ZIV 在进程内加载/持有模型与中间张量（torch `reserved` 实测 6976MB），官方 server
   走 aimdo VBAR/mmap（官方探针 `HostBuffer.size=0`，RSS 2.9GB）。
2. **Viggle 插件运行时 forward-hook 侧支路**（`plugin_packs/qwen21-viggle-6step/__init__.py:82-98`
   `_run_with_lora` + `model.clone()`）对比官方 `ViggleTurboLora` 节点权重 patch。
3. **repro harness 的 `seaminstr` 插桩**（仅影响测量，不影响产品）。

**判定**：残差**不是 thrash**——RSS 11.7GB 在 §2 的 28GB 安全线内、单步 ~1.4s、wall 22.5s 可接受。
是否进一步收敛另立。

---

## 9. 教训（必记）

### 9.1 Step 3「关 smart memory」是误判
- 当年（Step 3，pre-aimdo）实测：512²/4 步，关闭 smart memory 峰值 16020 → 9144 MiB（**−43%**），遂默认关闭。
  （历史记录见 `DOC/DEVLOG.md` Step 3 段落、`DOC/FROZEN.md` Step 3.4——本文只指向，不改其原文。）
- 但在 aimdo 下 `disable_smart_memory` **几乎 inert**：
  - `mm.py:1114-1115` `if aimdo_enabled: return cpu`（早于 `:1121` 的 `DISABLE_SMART_MEMORY` 判断）；
  - `mm.py:914-918` 动态模型 `memory_to_free = 0`。
  - 全部消费点仅 `mm.py:599/601/912/1121`。
- 结论：**把 @1536 thrash 归因于 smart memory 是误判；真因是 allocator（§4–§7）。历史数据不能外推到新架构。**

### 9.2 官方 env 不是「可选」
官方 `main.py:41-114` 的每一行 env 都有意义：`import cuda_malloc`(:44) 只差一行，就让整个 allocator 行为
改变，进而引发 thrash。**对齐官方入口的运行时 env 是安全基线的一部分。**

### 9.3 数据说话
静态代码分析只能给**假设**（本文 §4 的假设最初来自静态阅读）；只有 **探针数据 + A/B 对照** 才能定位。
本次 A0b 的 `dev_free→0 / reserved→26708MB` 是关键判据。

### 9.4 显存问题不是「显存不够」，而是「口径与换页」
峰值 VRAM 16019 与官方 15759 只差 ~260MB，却导致 34GB RSS——**问题在 allocator 口径引发的换页行为，
不在峰值本身**（旁证：方案 1「VRAM 余量注入」无效，见 `DOC/DEVLOG.md` 对应段落）。

---

## 10. 未决问题

| # | 问题 | 文档标注 |
|---|---|---|
| 1 | 残差 8.8GB 的确切构成 | **已定位**（2026-09-29 复核）：prefix K/V 缓存 7432.5MB + DiT hostpin 0.35–2.6GB；见 §8 修正 |
| 2 | Machine 级 `PYTORCH_CUDA_ALLOC_CONF=max_split_size_mb:30` 在 native 下加剧碎片；是否建议移除（属机器配置，非 ZIV） | §4/§6 提及 |
| 3 | 官方启动 argv 无显式快照（结论基于 `main.py` 默认 + 日志 + `cli_args` dump） | §5 数据来源注 |
| 4 | 生产路径（`python/server/main.py`）的 `apply_official_env` 未单独实测（与 repro 调用同一函数） | §7 落点说明 |
| 5 | `DOC/THRASH_ROOTCAUSE.md` 是否落地、env 对齐是否提交 | 交付说明 |

---

## 11. 附录

### 11.1 探针与实验脚本（均在 `_test_step2/`，gitignore 内）
- `_test_step2/ziv_probe.py`：P1/P2/P3 探针（env `ZIV_AI_PROBE=1`；输出 `probe_<run>.jsonl` / `steps_<run>.jsonl`）。
- `_test_step2/run_experiment.py`：GPU 运行入口（复用 `E:\temp\opencode\repro_viggle.py` + 装探针）。
- `E:\temp\opencode\watch.ps1`：外部看门狗（RSS / RAM / commit / wall 越线即 `taskkill /T /F`；输出 `watch_<run>.tsv`）。
- 官方侧临时探针（已删）：`Comfyui/ComfyUI/custom_nodes/zzz_aimdo_probe/`，输出 `official_probe.jsonl`。

### 11.2 实验数据路径
- ZIV：`_test_step2/probe_{e1,a0b,a1,a2,a3,a4,a5}_*.jsonl`、`steps_*.jsonl`、`watch_*.tsv`、`pwsh` 同目录 `*.out.txt`。
- 官方：`_test_step2/official_probe.jsonl`（O1–O4 campaign）。

### 11.3 关键代码引用清单
| 主题 | 位置 |
|---|---|
| 官方 allocator 触发 | `Comfyui/ComfyUI/main.py:44`；`Comfyui/ComfyUI/cuda_malloc.py`（设 `PYTORCH_CUDA_ALLOC_CONF`） |
| 官方其它 env | `Comfyui/ComfyUI/main.py:41,42,52,84,87,114` |
| free_memory 口径 | `comfy/model_management.py:1781,1814-1820` |
| 动态模型不卸载 | `comfy/model_management.py:914-918` |
| aimdo 早返回 / smart memory | `comfy/model_management.py:1114-1115,1121` |
| smart memory 消费点 | `comfy/model_management.py:599,601,912,1121` |
| pinned buffer 尺寸 | `comfy/model_management.py:1613-1621,1625-1628,1485` |
| aimdo 压力/托管量 | `comfy_aimdo/control.py:62,68,118,268` |
| ZIV 运行时默认 | `python/server/model_loader.py` `_apply_runtime_defaults` / `_enable_dynamic_vram` |
| ZIV env 对齐 | `python/server/config.py` `apply_official_env`；`python/server/main.py`；`model_loader.prepare_environment` |

### 11.4 相关文档（指向，不改原文）
- `DOC/FROZEN.md`：Step 3.4（smart memory 默认关闭决策）、Step 6（DynamicVRAM）。
- `DOC/DEVLOG.md`：Step 3（512² smart memory 实测）、方案 1（VRAM 余量注入无效）段。
- `DOC/OPTIMIZATION.md`：§6.2（aimdo 下 1536 单图峰值 13091 基线）。
