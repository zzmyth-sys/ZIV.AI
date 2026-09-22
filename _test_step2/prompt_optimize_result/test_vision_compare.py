"""Temporary vision comparison: ask the local multimodal model to compare the
two QW21edit outputs. Read-only w.r.t. the project."""

import base64
import json
import os
import sys
import urllib.request

sys.stdout.reconfigure(encoding="utf-8")

OUT_DIR = r"D:\devlop\ZIV.AI\_test_step2\prompt_optimize_result"
ORIG = os.path.join(OUT_DIR, "orig_prompt_output.png")
OPT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(OUT_DIR, "optimized_prompt_output.png")
RAW_OUT = sys.argv[2] if len(sys.argv) > 2 else "vision_compare_raw.json"
ENDPOINT = "http://127.0.0.1:8080/v1/chat/completions"


def data_url(path):
    with open(path, "rb") as handle:
        return "data:image/png;base64," + base64.b64encode(handle.read()).decode("ascii")


def main():
    question = (
        "下面两张图是对同一张输入照片（一位穿深青汉服的女性在古典室内读书）做背景替换的编辑结果。"
        "图1是使用原始简短提示词生成的，图2是使用经过提示词重写增强后的编辑指令生成的。"
        "请分别观察并回答（中文，分点简洁）：\n"
        "1) 两张图的背景是否呈现为古代中式茶肆（木质、灯笼/茶具、暖色室内）？\n"
        "2) 人物与前景（读书的女性、桌案）是否保留？\n"
        "3) 是否出现水印、文字伪影或明显畸变？\n"
        "4) 哪一张更符合“把背景替换为古代中式茶肆，保留人物与前景”？给出理由。"
    )
    payload = {
        "model": "qwythos-9b",
        "messages": [
            {"role": "system", "content": "You are a careful image observer. Reply in Chinese."},
            {
                "role": "user",
                "content": [
                    {"type": "text", "text": "图1（原始提示词）："},
                    {"type": "image_url", "image_url": {"url": data_url(ORIG)}},
                    {"type": "text", "text": "图2（重写提示词）："},
                    {"type": "image_url", "image_url": {"url": data_url(OPT)}},
                    {"type": "text", "text": question},
                ],
            },
        ],
        "temperature": 0.2,
        "max_tokens": 2048,
        "stream": False,
        "chat_template_kwargs": {"enable_thinking": False},
    }

    request = urllib.request.Request(
        ENDPOINT,
        data=json.dumps(payload).encode("utf-8"),
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    with urllib.request.urlopen(request, timeout=600) as response:
        raw = response.read().decode("utf-8")
    with open(os.path.join(OUT_DIR, RAW_OUT), "w", encoding="utf-8") as handle:
        handle.write(raw)

    message = json.loads(raw)["choices"][0]["message"]
    content = message.get("content") or ""
    end = content.rfind("</think")
    if end >= 0:
        close = content.find(">", end)
        if close >= 0:
            content = content[close + 1:]
    print("===== VISION COMPARE (Qwythos-9B) =====")
    print(content.strip())


if __name__ == "__main__":
    main()
