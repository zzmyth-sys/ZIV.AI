"""Temporary I2V prompt-enhancer probe (corrected template).

Sends the official I2V/editing system prompt + input image + user's edit
instruction to the local llama-server (Qwythos-9B, multimodal) and parses the
rewritten editing directive (3 fields: rewritten_prompt / wh_ratio /
ratio_follow). Read-only w.r.t. the project.
"""

import base64
import json
import os
import sys
import urllib.request

sys.stdout.reconfigure(encoding="utf-8")

REPO = r"D:\devlop\ZIV.AI"
SYSTEM_PROMPT = os.path.join(REPO, "temp", "system_prompt_i2v.txt")
IMAGE = os.path.join(REPO, "_test_step2", "user_input_1024.png")
OUT_DIR = os.path.join(REPO, "_test_step2", "prompt_optimize_result")
USER_INSTRUCTION = "把背景替换为古代中式茶肆，保留人物与前景"
ENDPOINT = "http://127.0.0.1:8080/v1/chat/completions"
ENABLE_THINKING = (len(sys.argv) < 2) or (sys.argv[1].lower() != "false")
SUFFIX = sys.argv[2] if len(sys.argv) > 2 else ""


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    with open(SYSTEM_PROMPT, "r", encoding="utf-8") as handle:
        system_text = handle.read()
    with open(IMAGE, "rb") as handle:
        image_b64 = base64.b64encode(handle.read()).decode("ascii")

    payload = {
        "model": "qwythos-9b",
        "messages": [
            {"role": "system", "content": system_text},
            {
                "role": "user",
                "content": [
                    {"type": "image_url", "image_url": {"url": "data:image/png;base64," + image_b64}},
                    {"type": "text", "text": USER_INSTRUCTION},
                ],
            },
        ],
        "temperature": 1.0,
        "top_p": 0.95,
        "top_k": 20,
        "max_tokens": 4096,
        "stream": False,
        "chat_template_kwargs": {"enable_thinking": ENABLE_THINKING},
    }

    request = urllib.request.Request(
        ENDPOINT,
        data=json.dumps(payload).encode("utf-8"),
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    print("POST", ENDPOINT, "system_chars=", len(system_text), "enable_thinking=", ENABLE_THINKING)
    with urllib.request.urlopen(request, timeout=600) as response:
        raw = response.read().decode("utf-8")
    with open(os.path.join(OUT_DIR, ("rewrite_i2v_raw%s.json" % SUFFIX)), "w", encoding="utf-8") as handle:
        handle.write(raw)

    message = json.loads(raw)["choices"][0]["message"]
    content = message.get("content") or ""
    reasoning = message.get("reasoning_content") or ""

    print("\n===== RAW content (first 2000 chars) =====")
    print(content[:2000])

    cleaned = content
    end = cleaned.rfind("</think")
    if end >= 0:
        close = cleaned.find(">", end)
        if close >= 0:
            cleaned = cleaned[close + 1:].strip()

    parsed = None
    try:
        parsed = json.loads(cleaned)
    except Exception as exc:  # noqa: BLE001
        print("\nJSON PARSE FAILED:", type(exc).__name__, exc)
        start, stop = cleaned.find("{"), cleaned.rfind("}")
        if start >= 0 and stop > start:
            try:
                parsed = json.loads(cleaned[start:stop + 1])
                print("JSON recovered by slicing braces.")
            except Exception as exc2:  # noqa: BLE001
                print("JSON slice parse failed:", type(exc2).__name__, exc2)

    if isinstance(parsed, dict):
        print("\n===== PARSED =====")
        print("keys:", list(parsed.keys()))
        rewritten = parsed.get("rewritten_prompt")
        print("wh_ratio   :", repr(parsed.get("wh_ratio")))
        print("ratio_follow:", repr(parsed.get("ratio_follow")))
        print("rewritten_prompt length:", len(rewritten) if isinstance(rewritten, str) else None)
        print("rewritten_prompt:")
        print(rewritten)
        with open(os.path.join(OUT_DIR, ("rewrite_i2v%s.json" % SUFFIX)), "w", encoding="utf-8") as handle:
            json.dump(parsed, handle, ensure_ascii=False, indent=2)
        with open(os.path.join(OUT_DIR, ("rewritten_i2v_prompt%s.txt" % SUFFIX)), "w", encoding="utf-8") as handle:
            handle.write(rewritten or "")
    else:
        print("\nNo JSON object parsed.")


if __name__ == "__main__":
    main()
