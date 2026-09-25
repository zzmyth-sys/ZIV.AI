"""Manual CPU smoke test for the WD14 tagger (L1). No GPU (Z29 / Z30).

Run from the repo root with the embedded interpreter::

    D:\\devlop\\ZIV.AI\\Comfyui\\python_embeded\\python.exe -s _test_step2\\test_tagger.py

Optionally pass an image path as the first argument (defaults to
``_test_step2/user_input.png``). Prints the matched tags; exits non-zero only on
an unexpected crash (a missing model / failed inference yields an empty list).
"""

import os
import sys

_SERVER_DIR = os.path.abspath(
    os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "python", "server")
)
sys.path.insert(0, _SERVER_DIR)

import config  # noqa: E402  (path setup must precede this import)
import tagger  # noqa: E402

_REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def main(argv):
    image_path = (
        argv[1]
        if len(argv) > 1
        else os.path.join(_REPO_ROOT, "_test_step2", "user_input.png")
    )
    print("enabled :", config.TAGGER_ENABLED)
    print("model   :", config.TAGGER_MODEL)
    print("modeldir:", config.TAGGER_MODEL_DIR)
    print("image   :", image_path)
    print("threshold: %.2f / character %.2f" % (config.TAGGER_THRESHOLD, config.TAGGER_CHARACTER_THRESHOLD))

    tags = tagger.tag_image(image_path)
    print("tags (%d):" % len(tags))
    for tag in tags:
        print(" -", tag)
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
