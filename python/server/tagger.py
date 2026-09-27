"""WD14 tagger for the ZIV.AI backend (L1: Python-side capability).

状态：L1 已实现（``tag_image``）；L2（IPC 接入 / C# 调用）**未接**。
触发条件：当 ``/tag`` 命令或批量打标需求落地时接入 IPC。
依赖：onnxruntime（CPU）+ ComfyUI ``custom_nodes/comfyui-wd14-tagger``（外置）。
当前无生产调用者（grep 0 命中）；保留为能力 seam。

Reimplements the inference core of the external WD14 Tagger custom node
(``custom_nodes/comfyui-wd14-tagger/wd14tagger.py``) so ZIV.AI can tag an image
in-process without the node's web / ``PromptServer`` dependencies (route A; see
the read-only investigation). The node directory is an external module: it
stores the ONNX model and the reference implementation and may be removed or
updated by the user. Every failure degrades to an empty tag list so tagging can
never fail an otherwise valid task.

CPU only (onnxruntime ``CPUExecutionProvider``); this never touches the GPU
(Z29 / Z30).
"""

import csv
import logging
import os
import threading

import config

_LOG = logging.getLogger("zivai.server")

# onnxrt InferenceSession cache keyed by the .onnx path; sessions are expensive
# to build and safe to reuse. Guarded because the backend may tag from threads.
_SESSION_CACHE = {}
_SESSION_LOCK = threading.Lock()


def _model_paths(model_name):
    base = os.path.join(config.TAGGER_MODEL_DIR, model_name)
    return base + ".onnx", base + ".csv"


def _get_session(onnx_path):
    with _SESSION_LOCK:
        session = _SESSION_CACHE.get(onnx_path)
        if session is not None:
            return session
        import onnxruntime as ort

        session = ort.InferenceSession(onnx_path, providers=["CPUExecutionProvider"])
        _SESSION_CACHE[onnx_path] = session
        return session


def _read_tags(csv_path):
    """Return ``(tags, general_index, character_index)`` from a selected_tags csv.

    Mirrors the node: category ``0`` is general, ``4`` is character; underscores
    in tag names become spaces. Indices fall back to the whole / empty range if
    a category is absent so a malformed csv still yields something usable.
    """
    tags = []
    general_index = None
    character_index = None
    with open(csv_path, newline="", encoding="utf-8-sig") as handle:
        reader = csv.reader(handle)
        next(reader, None)  # header: tag_id,name,category,count
        for row in reader:
            if len(row) < 3:
                continue
            if general_index is None and row[2] == "0":
                general_index = reader.line_num - 2
            elif character_index is None and row[2] == "4":
                character_index = reader.line_num - 2
            tags.append(row[1].replace("_", " "))
    if general_index is None:
        general_index = 0
    if character_index is None:
        character_index = len(tags)
    return tags, general_index, character_index


def tag_image(image_path, model_name=None, threshold=None, character_threshold=None):
    """Tag ``image_path`` with a WD14 model and return the matched tags.

    ``model_name`` defaults to ``config.TAGGER_MODEL`` (``wd-vit-tagger-v3``) and
    the two thresholds to ``config.TAGGER_THRESHOLD`` / ``..._CHARACTER_THRESHOLD``.
    Returns a list of tag strings (character tags first, then general), or an
    empty list when tagging is disabled, the model is missing, or inference fails.
    """
    if not config.TAGGER_ENABLED:
        return []

    model_name = model_name or config.TAGGER_MODEL
    threshold = config.TAGGER_THRESHOLD if threshold is None else threshold
    character_threshold = (
        config.TAGGER_CHARACTER_THRESHOLD
        if character_threshold is None
        else character_threshold
    )

    onnx_path, csv_path = _model_paths(model_name)
    if not os.path.isfile(onnx_path) or not os.path.isfile(csv_path):
        _LOG.warning(
            "WD14 model not found (%s / %s); returning no tags", onnx_path, csv_path
        )
        return []

    try:
        import numpy as np
        from PIL import Image

        session = _get_session(onnx_path)
        model_input = session.get_inputs()[0]
        height = int(model_input.shape[1])

        # Reduce to the model's square input and pad the shorter side with white.
        image = Image.open(image_path).convert("RGB")
        ratio = float(height) / max(image.size)
        new_size = tuple(int(dim * ratio) for dim in image.size)
        image = image.resize(new_size, Image.LANCZOS)
        square = Image.new("RGB", (height, height), (255, 255, 255))
        square.paste(image, ((height - new_size[0]) // 2, (height - new_size[1]) // 2))

        array = np.asarray(square, dtype=np.float32)[:, :, ::-1]  # RGB -> BGR
        array = np.ascontiguousarray(array)
        array = np.expand_dims(array, 0)

        tags, general_index, character_index = _read_tags(csv_path)
        label_name = session.get_outputs()[0].name
        probs = session.run([label_name], {model_input.name: array})[0][0]

        general = [
            tag
            for tag, score in zip(
                tags[general_index:character_index], probs[general_index:character_index]
            )
            if score > threshold
        ]
        character = [
            tag
            for tag, score in zip(tags[character_index:], probs[character_index:])
            if score > character_threshold
        ]
        return character + general
    except Exception as exc:
        _LOG.warning("WD14 tagging failed for %s (%s); returning no tags", image_path, exc)
        return []
