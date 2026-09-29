# zivcli

ZIV.AI GPU 任务 CLI——封装 harness，供 Agent 自主跑金标验证。

## 用法

```
python -m tools.zivcli gpu-check [--threshold 1600]
python -m tools.zivcli run --op X --side N --nref N --steps N \
                            --viggle 0|1 --img1 P [--img2 P] [--img3 P] \
                            [--prompt TEXT] --out P --yes
python -m tools.zivcli verify <golden> --yes [--out-dir DIR]
python -m tools.zivcli batch <list-file> --yes [--out-dir DIR]
```

## 金标

- `goldens.json` 定义 B3 / B4
- SHA256 大写存储；比对大小写归一
- 输入钉死为 `harness/fixtures/` 下文件

## 1969（9C.5D.14）多图引用跑法

`ACCEPTANCE.MD:1969` 验收 `<image2>` / `<image3>` 引用生效。`run` 用 `--prompt` 传引用文本；
`--img1` 为主图，`--img2` / `--img3` 为附加参考图（配 `--nref 2`）。**图片路径必须绝对**——
`run` 原样把路径交给 harness，且 harness 子进程 cwd 为 `harness/`：

```
python -m tools.zivcli run --op inpaint --side 1536 --nref 2 \
  --img1 D:\devlop\ZIV.AI\tools\zivcli\harness\fixtures\img1.jpg \
  --img2 D:\devlop\ZIV.AI\tools\zivcli\harness\fixtures\img2.png \
  --img3 D:\devlop\ZIV.AI\tools\zivcli\harness\fixtures\img1.jpg \
  --prompt "Replace the background of <image1> with the scene from <image2>; relight it like <image3>." \
  --out D:\temp\1969.png --yes
```

- 省略 `--prompt` = 用 harness 内置 prompt（B3/B4 金标走此路径，env 与 SHA 不变）。
- 让 `--img3` 指向与 `--img2` 不同的图，才能看出 `<image2>` 与 `<image3>` 的差异。
- 空 `--prompt ""` 会被 harness 回退为内置 prompt（非静默丢弃）。
- 批量：list-file 内联条目加 `"prompt": "..."` 字段即可（见 `batch.py`）。

## harness vendor

`harness/` 为 `E:\temp\opencode` 与 `D:\devlop\ZIV.AI\_test_step2`
的冻结快照；不随产品同步；更新须走 `DOC/FROZEN.md` 修订。
来源见 `harness/README.md`。

## Z30

所有 GPU 命令需 `--yes`（编码用户授权）；调用前自动 `gpu-check`（双判据）。

## 退出码

- `gpu-check`: 0=空闲 / 1=占用
- `run`: 0=成功 / 1=失败 / 2=Z30 未授权
- `verify`: 0=PASS / 1=FAIL / 2=Z30 未授权或未知 golden
- `batch`: 0=全 PASS / 1=有 FAIL / 2=Z30 未授权或 list-file 非法

## 环境

- Python：`D:\devlop\ZIV.AI\Comfyui\python_embeded\python.exe`
- 输出：`runs/`（已 gitignore）
