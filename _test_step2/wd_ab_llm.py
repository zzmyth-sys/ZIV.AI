"""WD A/B experiment - stage 1: rewrite tags into a natural-language prompt via llama-server.

Assumes the LLM is already running (started with `llmctl start ... qwythos-9b`).
Pure HTTP (no llama-server lifecycle here). Fills the `B` field of every batch in
`_test_step2/wd_ab_prompts.json`.
"""

import json
import os
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
ENDPOINT = "http://127.0.0.1:8080/v1/chat/completions"
MODEL = "qwythos-9b"

SYSTEM = (
    "You are an expert prompt engineer for a text-to-image EDIT model (Qwen-Image). "
    "You receive a list of WD/booru tags describing the CURRENT image and one edit instruction. "
    "Rewrite them into ONE natural-language English paragraph describing the FINISHED image "
    "after the edit:\n"
    "- Preserve the person's stable attributes (person count, hair, clothing, pose, key props).\n"
    "- Apply the edit instruction exactly.\n"
    "- Ignore rendering/metadata tags such as realistic, photorealistic, blurry, logo, "
    "watermark, text, copyright name, depth of field.\n"
    "- Never write a resolution, aspect ratio, or pixel size.\n"
    "- Keep any <image1> / <image2> reference tokens exactly as written in the edit instruction.\n"
    "- Output ONLY the paragraph: no quotes, no JSON, no headings, no commentary."
)


def strip_thinking(text):
    if not text:
        return text
    end = text.rfind("</think")
    if end >= 0:
        close = text.find(">", end)
        if close >= 0:
            return text[close + 1:].strip()
    return text.strip()


def rewrite(tags, edit):
    user = "Current-image tags: " + ", ".join(tags) + "\nEdit instruction: " + edit
    payload = {
        "model": MODEL,
        "messages": [
            {"role": "system", "content": SYSTEM},
            {"role": "user", "content": user},
        ],
        "temperature": 0.6,
        "top_p": 0.95,
        "top_k": 20,
        "max_tokens": 1024,
        "stream": False,
    }
    body = json.dumps(payload).encode("utf-8")
    request = urllib.request.Request(
        ENDPOINT, data=body, headers={"Content-Type": "application/json"}, method="POST"
    )
    with urllib.request.urlopen(request, timeout=600) as response:
        raw = response.read().decode("utf-8")
    data = json.loads(raw)
    message = data["choices"][0]["message"]
    return strip_thinking(message.get("content") or "")


def main():
    import sys

    path = os.path.join(HERE, "wd_ab_prompts.json")
    with open(path, encoding="utf-8") as handle:
        prompts = json.load(handle)

    keys = sys.argv[1:] or list(prompts.keys())
    for key in keys:
        data = prompts[key]
        b = rewrite(data["tags"], data["edit"])
        data["B"] = b
        print("==", key, "B (%d chars):" % len(b))
        print("   ", b)
        print()

    with open(path, "w", encoding="utf-8") as handle:
        json.dump(prompts, handle, ensure_ascii=False, indent=2)
    print("wrote", path)


if __name__ == "__main__":
    main()
