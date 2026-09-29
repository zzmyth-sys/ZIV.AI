"""Minimal S5/S6 regression scenario driver (clean, no instrumentation).

Thin wrapper over the REAL pipeline entry points (``pipeline.run`` /
``pipeline.run_outpaint``); no product code is touched. Env-driven:

  SCN_OP       inpaint | t2i | outpaint   (default inpaint)
  SCN_SIDE     long side for side mode     (default 1024)
  SCN_NREF     number of additional reference images (0..3)
  SCN_IMG3/4   extra reference paths (default: reuse IMG2)
  SCN_VIGGLE   1/0  -> ZIV_AI_PLUGIN_QWEN21_VIGGLE_6STEP (set before repro import)
  SCN_OUT      output png path
  SCN_STEPS    steps (default 40)
  SCN_W/SCN_H  explicit target size (outpaint)
  SCN_ANCHOR   outpaint anchor (default center)

Output: one RESULT line with wall + result dict. Model load is included in the
separate LOADED log line; RESULT wall measures pipeline.run only (like run_clean).
"""

# vendored from D:\devlop\ZIV.AI\_test_step2\s5s6_scenarios.py; CLI 本地化改动：
#   REPRO_HARNESS 默认值 r"E:\temp\opencode" -> HERE（本目录），使 import
#   repro_viggle 不依赖 E:\temp\opencode。其余逻辑与源文件一致。

import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.environ.get("REPRO_HARNESS", HERE))

# repro_viggle reads these at import time to configure the run; set ours first.
if "SCN_VIGGLE" in os.environ:
    os.environ["REPRO_VIGGLE"] = os.environ["SCN_VIGGLE"]
if "SCN_SIDE" in os.environ:
    os.environ["REPRO_SIDE"] = os.environ["SCN_SIDE"]

import repro_viggle as R  # noqa: E402  (sets ZIV_AI_* env + IMG1/IMG2/PROMPT)


def main():
    import logging

    import config

    config.setup_logging("INFO")
    logging.getLogger("zivai.server").setLevel(logging.INFO)

    import model_loader
    import pipeline

    op = os.environ.get("SCN_OP", "inpaint").strip().lower()
    side = int(os.environ.get("SCN_SIDE", "1024"))
    nref = int(os.environ.get("SCN_NREF", "0"))
    steps = int(os.environ.get("SCN_STEPS", "40"))
    out = os.environ.get("SCN_OUT", r"D:\temp\s5s6_%s.png" % op)

    # B5: register LoRA pre-sampling hooks exactly like handlers._register_loras
    # (direct pipeline.run does not go through _configure_pre_sampling_hooks).
    lora_ids = [x.strip() for x in os.environ.get("SCN_LORAS", "").split(",") if x.strip()]
    if lora_ids:
        import loras as loras_mod
        import pipeline_hooks

        pipeline_hooks.clear_pre_sampling_hooks()
        for lid in lora_ids:
            entry = loras_mod.resolve(lid)
            path = loras_mod.resolve_path(lid, validate=True)
            pipeline_hooks.register_pre_sampling_hook(
                pipeline_hooks.make_lora_hook(
                    path,
                    loras_mod.resolve_strength(None, entry, "default_strength_model"),
                    loras_mod.resolve_strength(None, entry, "default_strength_clip"),
                )
            )
            print("LORA registered %s -> %s" % (lid, path), flush=True)

    t_load = time.time()
    model, _ = model_loader.load_dit()
    clip, _ = model_loader.load_text_encoder()
    vae, _ = model_loader.load_vae()
    print("LOADED wall=%.1fs" % (time.time() - t_load), flush=True)

    refs = []
    if nref >= 1:
        refs.append(os.environ.get("SCN_IMG2", R.IMG2))
    if nref >= 2:
        refs.append(os.environ.get("SCN_IMG3", R.IMG2))
    if nref >= 3:
        refs.append(os.environ.get("SCN_IMG4", R.IMG2))
    main = os.environ.get("SCN_IMG1") or R.IMG1

    request = {
        "prompt": os.environ.get("SCN_PROMPT")
        or (R.PROMPT if op != "t2i"
            else "a serene mountain lake at sunrise, photorealistic"),
        "image_path": None if op == "t2i" else main,
        "mask_path": None,
        "additional_images": refs,
        "denoise": 1.0,
        "seed": 42,
        "steps": steps,
        "resolution": {"mode": "side", "side": side, "max_pixels": 4700000},
        "model_id": "qwen-image-2.1",
        "output_path": out,
    }
    if op == "outpaint":
        request["resolution"] = {
            "mode": "explicit",
            "width": int(os.environ.get("SCN_W", "1536")),
            "height": int(os.environ.get("SCN_H", "1536")),
            "max_pixels": 4700000,
        }
        request["anchor"] = os.environ.get("SCN_ANCHOR", "center")

    t0 = time.time()
    if op == "outpaint":
        result = pipeline.run_outpaint(model, clip, vae, request, on_progress=None)
    else:
        result = pipeline.run(model, clip, vae, request, on_progress=None, op=op)
    print("RESULT wall=%.1fs %s" % (time.time() - t0, result), flush=True)


if __name__ == "__main__":
    main()
