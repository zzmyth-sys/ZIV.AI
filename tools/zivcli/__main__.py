"""Command line entry point: ``python -m tools.zivcli <command>``."""

from __future__ import annotations

import argparse
import json
import sys

from . import batch, gpu, runner, verify


def build_parser():
    parser = argparse.ArgumentParser(
        prog="ziv", description="ZIV.AI external CLI (harness wrapper)"
    )
    sub = parser.add_subparsers(dest="command", required=True)

    gp = sub.add_parser(
        "gpu-check", help="check GPU idle (exit 0=free, 1=busy)"
    )
    gp.add_argument("--threshold", type=int, default=gpu.DEFAULT_THRESHOLD_MIB)
    gp.add_argument("--large-app", type=int, default=gpu.DEFAULT_LARGE_APP_MIB)

    rn = sub.add_parser("run", help="run one pipeline scenario")
    rn.add_argument("--op", default="inpaint", choices=["inpaint", "t2i", "outpaint"])
    rn.add_argument("--side", type=int, default=1024)
    rn.add_argument("--nref", type=int, default=0)
    rn.add_argument("--steps", type=int, default=40)
    rn.add_argument("--viggle", type=int, default=0, choices=[0, 1])
    rn.add_argument("--img1", required=True)
    rn.add_argument("--img2")
    rn.add_argument("--img3")
    rn.add_argument("--out", required=True)
    rn.add_argument("--max-wall", dest="max_wall", type=float, default=120.0)
    rn.add_argument("--max-vram", dest="max_vram", type=float, default=15500.0)
    rn.add_argument("--max-rss-gb", dest="max_rss_gb", type=float, default=18.0)
    rn.add_argument("--harness", default=runner.DEFAULT_HARNESS)
    rn.add_argument(
        "--yes",
        action="store_true",
        help="caller has obtained explicit user consent (Z30)",
    )

    vf = sub.add_parser("verify", help="run a golden scenario and compare SHA256")
    vf.add_argument("golden")
    vf.add_argument("--out-dir", dest="out_dir", default=verify.DEFAULT_OUT_DIR)
    vf.add_argument(
        "--yes",
        action="store_true",
        help="caller has obtained explicit user consent (Z30)",
    )

    bt = sub.add_parser("batch", help="run a JSON list of scenarios")
    bt.add_argument("list_file")
    bt.add_argument("--out-dir", dest="out_dir", default=batch.DEFAULT_OUT_DIR)
    bt.add_argument(
        "--yes",
        action="store_true",
        help="caller has obtained explicit user consent (Z30)",
    )
    return parser


def main(argv=None):
    args = build_parser().parse_args(argv)

    if args.command == "gpu-check":
        try:
            res = gpu.check_gpu(
                threshold_mib=args.threshold, large_app_mib=args.large_app
            )
        except gpu.GpuCheckError as exc:
            print(json.dumps({"free": False, "reason": str(exc)}, ensure_ascii=False))
            return 1
        print(json.dumps(res, ensure_ascii=False))
        return 0 if res["free"] else 1

    if not args.yes:
        print("Z30: 需用户明确同意（GPU 任务），加 --yes", file=sys.stderr)
        return 2

    if args.command == "verify":
        code, verdict = verify.run_verify(
            args.golden,
            run_scenario_fn=runner.run_scenario,
            out_dir=args.out_dir,
        )
        print(json.dumps(verdict, ensure_ascii=False, indent=2))
        return code

    if args.command == "batch":
        try:
            entries = batch.load_list(args.list_file)
        except (OSError, ValueError) as exc:
            print(json.dumps({"error": "bad_list_file", "reason": str(exc)},
                             ensure_ascii=False))
            return 2
        code, summary = batch.run_batch(
            entries, out_dir=args.out_dir, run_scenario_fn=runner.run_scenario
        )
        print(json.dumps(summary, ensure_ascii=False, indent=2))
        return code

    opts = runner.RunOptions(
        op=args.op,
        side=args.side,
        nref=args.nref,
        steps=args.steps,
        viggle=args.viggle,
        img1=args.img1,
        img2=args.img2,
        img3=args.img3,
        out=args.out,
        max_wall=args.max_wall,
        max_vram=args.max_vram,
        max_rss_gb=args.max_rss_gb,
        harness=args.harness,
    )
    code, result = runner.run_scenario(opts)
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return code


if __name__ == "__main__":
    sys.exit(main())
