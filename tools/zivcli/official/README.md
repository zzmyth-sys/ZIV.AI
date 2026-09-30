# zivcli.official — 官方 ComfyUI 侧 G1 对照基线（tracked）

本目录把「官方 ComfyUI 侧跑重建 G1 同负载」固化为 **tracked 脚本**，作为与 ZIV
`tools/zivcli run` 对照的可回归基线。**不属产品路径**，纯 stdlib（RSS 采样用可选 `psutil`）。

> **Z30**：本脚本会起官方 ComfyUI（GPU）。执行前先 `gpu-check`，并必须传 `--yes`
> （调用方已取得用户明确同意）。脚本内部也会再 `check_gpu`，不空闲即拒绝。

## 重建 G1 定义（裁判担责，写死）

`op=inpaint`、`side=1536`（输出 **1216×1536**）、`nref=2`（img1 主图 + img2/img3 参考）、
`steps=40`、`sampler=euler`、`scheduler=simple`、`shift=3.1`（`ModelSamplingAuraFlow`）、
`cfg=1.0`、`seed=42`、`denoise=1.0`；`Viggle=0/1`，Viggle 档 steps 由 6-sigmas
`1.0,0.9375,0.875,0.75,0.5,0.25` 接管。

- 非 Viggle：`UNETLoader → ModelSamplingAuraFlow(3.1) → KSampler(euler/simple/40/cfg1/denoise1) → VAEDecode`。
  （`KSampler` 内部走 `comfy.sample.sample`，与 ZIV 同路径。）
- Viggle：`UNETLoader → ViggleTurboLora(1.0)` + `ViggleTurboSigmas(6-sigmas)` +
  `KSamplerSelect(euler)` + `BasicGuider` + `RandomNoise(42)` + `SamplerCustomAdvanced`（无 AuraFlow）。
- 参考图分辨率：`TextEncodeQwenImage21.resolution=1536`。

模型 / LoRA 名从 `Template/models.json` / `Template/loras.json` 推导（数据驱动，机器无关）；
缺失时回退到本机已知名。

## 用法

```powershell
# 1) 生成工作流 JSON（纯 CPU，可 headless）
python -m tools.zivcli.official.g1_workflow --viggle 0                 # 打印到 stdout
python -m tools.zivcli.official.g1_workflow --viggle 1 --out g1v1.json # 写文件

# 2) 跑官方 G1（GPU；起服务 + 提交 + 计时 + 采峰值 + 落 JSON）
python -m tools.zivcli.official.run_official --viggle 0 --yes
python -m tools.zivcli.official.run_official --viggle 1 --yes
#   默认产物：tools/zivcli/official/runs/g1/g1_v0.result.json（gitignore）
#   服务日志：tools/zivcli/official/runs/g1/server.out.txt / server.err.txt
```

- 服务已在 `127.0.0.1:8188` → 直接复用（`server.started=false`，RSS 不回采）。
- `--no-start`：要求服务已起；`--host/--port/--out-dir/--prompt` 可覆盖。

## 与 ZIV 侧字段对照

| 字段 | ZIV（`tools/zivcli/runner.py`） | 官方（`run_official.py`） |
|---|---|---|
| `sha256` | 输出 PNG 哈希 | 同 |
| `wall_sec` | `pipeline.run`（encode+sample+decode，**不含加载**） | 采样段（`executing`→`executed` 采样节点） |
| `load_sec` | `LOADED`（dit+te+vae 加载；另记） | `execution_start`→采样节点开始（**加载 + encode + 参考视觉塔**） |
| `peak_vram_mib` | `nvidia-smi` 采样峰值 | 同（0.5s 轮询） |
| `peak_rss_gb` | 进程工作集峰值 | 官方**服务进程**工作集峰值（`psutil`） |
| `exit_code` / `killed` / `output_path` / `input_env` | 同名字段 | 同（`killed` 恒 false；`input_env` 为本脚本的等效旋钮） |

> **口径注意**：两侧 `wall_sec`/`load_sec` 的分界不完全相同（ZIV `LOADED` 只含模型加载；
> 官方 `load_sec` 还含 `TextEncodeQwenImage21` 的正/负向编码）。正式对照时需按同一段对齐，
> 见 `DOC/FROZEN.md` 的测量口径段。另：官方 `KSampler` 在 `cfg=1.0` 仍编码负向 prompt，
> 而 ZIV `SKIP_NEGATIVE_AT_CFG1` 会跳过 → 官方 wall 略偏大（结构性差异，记录在案）。

## 与 `_test_step2/off_submit.py` 的关系

`_test_step2/off_submit.py` 是**未入库的开发探针**（含 `ReadProbe` 临时节点、无 shift/scheduler
对齐、WALL 含加载）。本目录是它的 **tracked、参数对齐、口径统一** 的正式基线；两者并存，
探针原地保留不动。
