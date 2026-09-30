# G1 对照执行协议（ZIV.AI）

> **定位**：正式执行「ZIV vs 官方 ComfyUI @1536 同负载对照（G1）」的操作口径，供用户与 Agent
> 分步执行。**不是产品代码**；不改产品路径。
>
> **基线**：G1 重建定义与官方工作流固化见 `DOC/FROZEN.md`「重建 G1 官方对照基线」段 §G1R.1–G1R.5
> （commit `1dbd135`）。本协议的 G1 定义**直接引用 §G1R.1，不重复发明**。
>
> **适用范围**：`tools/zivcli run`（ZIV 侧）与 `tools/zivcli/official/run_official.py`（官方侧）。
> **零 GPU 约束**：本协议本身不跑 GPU；正式对照为独立任务。

---

## 1. G1 定义（写死，引用 FROZEN §G1R.1）

| 参数 | 值 |
|---|---|
| op | `inpaint` |
| side | `1536`（输出 1216×1536） |
| nref | `2`（img1 主图 + img2/img3 参考） |
| steps | `40`（非 Viggle） / Viggle 由 6-sigmas 接管 |
| sampler | `euler` |
| scheduler | `simple` |
| shift | `3.1`（`ModelSamplingAuraFlow`） |
| cfg | `1.0` |
| seed | `42` |
| denoise | `1.0` |
| Viggle | `0` / `1` 两档；Viggle=1 用 6-sigmas `1.0,0.9375,0.875,0.75,0.5,0.25` |

- prompt（两侧逐字一致）：`Replace the background of <image1> with the scene from <image2>; relight it like <image3>.`
- 图（两侧同文件）：`tools/zivcli/harness/fixtures/img1.jpg`、`img2.png`、`img3.png`。
- 分档：**G1-NV**（Viggle=0）、**G1-V**（Viggle=1），各跑 ≥3 次。

---

## 2. 测量口径

### 2.1 可比段 = 全流程含加载 `T_total`

两侧唯一可精确对齐的段：

- **ZIV**：`T_total = LOADED + wall_sec`
  - `wall_sec`：`runner.py:333-340` 从 `RESULT wall=` 解析（= `pipeline.run`：encode+sample+decode+save）。
  - `LOADED`：`harness/s5s6_scenarios.py:78-82` 打印（仅 `load_dit/te/vae`）。
- **官方**：`T_total = wall_total_sec`（`run_official.py:202-229`，`execution_start`→`execution_success`）。

> 两者均含「模型加载 + 文本/参考编码 + 采样 + 解码 + 保存」，均**不含**解释器 / 服务 import。
> 官方 `load_sec`（加载+编码）与 `wall_sec`(sample-only) 为**辅助诊断**；ZIV 无 sample-only 时间戳，
> 故官方 sample-only **不参与 Δ**。

### 2.2 已知脆弱点（加固 1：LOADED 解析）

- ZIV `runner.py:333-340` **未结构化 LOADED**；当前只能从 result JSON 的 `stdout_tail` 文本解析
  `LOADED wall=([0-9.]+)s`。
- **解析失败回退**：从 `zivcli run` 的原始 stdout 手动提取 `LOADED wall=…s` 行。
- **连续 2 次解析失败**：另立任务给 ZIV runner 补**结构化 `load_sec` 字段**（仅 runner 输出，
  不改产品语义 / IPC / contracts）。
- 登记为**已知脆弱点，不阻塞本次对照**。

### 2.3 峰值

- VRAM：`nvidia-smi memory.used` 峰值（ZIV `peak_vram_mib`；官方同，0.5s 轮询）。
- RSS：进程工作集峰值（ZIV `peak_rss_gb`；官方为**服务进程** `psutil` 工作集）。
- 须记录 GPU 基线 used_mib（开跑前）。

### 2.4 冷热

- **冷对冷（强制）**：
  - ZIV：每次 `zivcli run` 新进程（`runner.py:280-281`），天然冷。
  - 官方：**每次跑前 kill 服务**（`tools/zivcli/official/runs/g1/server.pid`），让 `run_official.py`
    重新起（默认 smart memory ON → 不 kill 则热驻留，`cli_args.py:192`）。

### 2.5 次数与方差

- 每档每侧 **≥3 次**；主指标取**中位数**，并报 min/max。
- `(max−min)/median > 10%` → 标记抖动（见 §4「不可比」）。

### 2.6 环境记录

- GPU 驱动版本 / CUDA / torch 版本 / ComfyUI 版本 / 卡基线 used_mib / **同一机器同一会话**。

---

## 3. 残留不对齐项处理

| # | 项 | 处理 |
|---|---|---|
| ① | 分段定义差异 | 用 §2.1 的 `T_total`（两侧可导出，**无需补 ZIV 采集**）；官方 sample-only 不参与 Δ。 |
| ② | 负向编码 | **不可对齐**（官方 `nodes_qwen.py:177` 无条件编码负向；改官方违反铁律 3）→ **标注 + 口径修正**：官方 `T_total` 多含负向 vision encode（≈数秒，OPTIMIZATION §12.3 1536/15536 档约 3.54s）→ 官方偏慢 → `Δ` 为「ZIV **至少**慢 X」的**保守下界**。 |
| ③ | smart memory | **冷对冷**（§2.4）：官方每跑前 kill 服务重起。 |
| ④ | 单次样本 | 每档每侧 **≥3 次**，中位数 + min/max；抖动 >10% 按 §4 处理。 |

---

## 4. 对照判据（加固 2：三档）

`Δ = median(ZIV_T_total) − median(官方_T_total)`（同档同侧 ≥3 次；报绝对值 + 百分比）。

| 条件 | 结论 |
|---|---|
| `Δ ≥ 10.0s` | **确认**「慢 ~10s」（与原始观察对齐） |
| `8.0s ≤ Δ < 10.0s` | **部分确认**（弱于观察值） |
| `Δ < 8.0s`（含负值） | **不成立** |
| 抖动 `(max−min)/median > 10%` 或任一未对齐（冷热混 / 参考分辨率 / prompt 不同） | **不可比**（不给性能结论） |

> 先决条件：全部对齐条件满足（§1 参数、§2 口径、§3 处理）。

---

## 5. 用户操作步骤

1. **Z30 授权**：确认 GPU 空闲（`gpu-check` `free=true`、无 large app），**授权** Agent 执行 GPU 任务。
2. **（无需手跑）**：Agent 会自起官方服务并按 §6 执行；用户不重复跑同一段。
3. **最终签字**：看 §7 汇总对照表，依据 §4 判 确认 / 部分确认 / 不成立 / 不可比；**视觉签**归用户。

- 期望输出：`gpu-check` JSON `free=true`；官方服务日志出现 `To see the GUI go to: http://127.0.0.1:8188`。
- 失败信号：`free=false` → 停；`node_errors` / `execution_error` → 停并报（见 §8）。

---

## 6. Agent 操作步骤

> **分工（明确）**：**Agent 跑两侧**（ZIV + 官方）；用户只做 Z30 授权与最终签字。

1. **Z30**：`python -m tools.zivcli gpu-check`；记 baseline；未过即停。
2. **ZIV 侧**（G1-NV，≥3 次）：
   ```
   python -m tools.zivcli run --op inpaint --side 1536 --nref 2 --steps 40 --viggle 0 \
     --img1 <abs>\tools\zivcli\harness\fixtures\img1.jpg \
     --img2 <abs>\tools\zivcli\harness\fixtures\img2.png \
     --img3 <abs>\tools\zivcli\harness\fixtures\img3.png \
     --prompt "Replace the background of <image1> with the scene from <image2>; relight it like <image3>." \
     --out <abs>\tools\zivcli\runs\g1\ziv_v0_r{n}.png --yes
   ```
   G1-V：`--viggle 1`（steps 由插件接管）。
3. **ZIV `LOADED` 解析**：从上一步产物 JSON 的 `stdout_tail` 解析 `LOADED wall=…s`；
   **回退**：从原始 stdout 手动提取；**连续 2 次失败** → 登记另立任务（§2.2）。
4. **官方侧**（G1-NV，≥3 次）：先 `Stop-Process` 旧服务（冷）→
   `python -m tools.zivcli.official.run_official --viggle 0 --yes` → 跑完
   `Move-Item g1_v0.result.json g1_v0_r{n}.result.json`。G1-V 同理。
5. **计算**：两侧 `T_total`、`Δ`、中位数 / min / max；落 §7 对照表。
6. **收尾**：停官方服务；GPU 回落基线后再进入下一档。全部完成后再做最终汇总。

---

## 7. 产物落点与命名

| 侧 | 路径（均 gitignore） |
|---|---|
| ZIV | `tools/zivcli/runs/g1/ziv_v{0,1}_r{n}.png` + `…results` JSON（`runner` 落 `*.result.json`） |
| 官方 | `tools/zivcli/official/runs/g1/g1_v{0,1}_r{n}.result.json` + `server.out.txt` / `server.err.txt` |
| 汇总 | 对照表入报告；若长期留档，另立任务落 `DOC/` |

- 命名规范：`v0`=非 Viggle，`v1`=Viggle，`r{n}`=第 n 次（1-based）。

---

## 8. 失败信号与对策

| 信号 | 含义 | 对策 |
|---|---|---|
| `gpu-check free=false` | GPU 忙 | 停，等回落，**不抢占** |
| `node_errors` | 工作流无效 | 停，报（**不改官方**） |
| `execution_error` / `status_str=error` | 运行失败 | 停，取 `error` 字段报告 |
| OOM / 被看门狗杀 | 显存 / 内存越线 | 停，报；**不调参** |
| 服务未就绪 / 端口 8188 占用 | 服务问题 | 停，清残留进程后重试 |
| `(max−min)/median > 10%` | 抖动 | 加跑或排除异常次（须记录原因） |
| `LOADED` 解析失败 ≥2 次 | 脆弱点触发 | 按 §2.2 回退 / 登记另立任务 |

---

## 修订记录

- 2026-09-30：首版；依据 `DOC/FROZEN.md` §G1R.*（commit `1dbd135`），含裁判加固 1（LOADED 脆弱性）
  与加固 2（三档判据）。
- 2026-09-30：img3 = `tools/zivcli/harness/fixtures/img3.png`（`683bdd1` 引入，SHA `EE31F26B…`；
  非 `DOC/ICO/logo.png`）——澄清 §1 / §6 的 img3 来源。
- 2026-09-30：追加**单图 / 25 步 / 无 Viggle**补充对照（一次性，未入协议目录；官方侧为临时脚本）：
  ZIV `T_total=33.5s` vs 官方 `41.5s`（Δ=−8.0s）。结果与缺口登记见 `DOC/FROZEN.md` §G1C.*。
