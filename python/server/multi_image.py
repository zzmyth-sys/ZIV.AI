"""Multi-image editing helpers (Step 9C.5-D).

Pure helpers shared by the inference pipeline: they normalize the optional
``additional_images`` payload field and build the ordered image list consumed by
:func:`pipeline._encode`. No ``torch`` / ``comfy`` import here so the module stays
importable on a CPU-only host (and unit-testable without the heavy stack).

The first image (the main image) is always ``<image1>``; each additional reference
becomes ``<image2>``, ``<image3>``, ... in payload order. The user prompt references
those positional markers verbatim; the tokenizer inserts them automatically.
"""


def normalize_additional_images(value):
    """Return the ordered, non-blank reference image paths from a payload value.

    ``None`` / a non-list value yields an empty list; blank entries are dropped and
    the remaining order is preserved.
    """
    if not isinstance(value, (list, tuple)):
        return []
    result = []
    for entry in value:
        if entry is None:
            continue
        text = str(entry).strip()
        if text:
            result.append(text)
    return result


def reference_paths(main_path, additional_images):
    """Return the ordered pipeline image list: ``[main] + extras`` (main first).

    ``main_path`` is included only when non-blank; extras are normalized through
    :func:`normalize_additional_images`, so blanks are dropped and order is kept.
    """
    paths = []
    if main_path is not None and str(main_path).strip():
        paths.append(str(main_path).strip())
    paths.extend(normalize_additional_images(additional_images))
    return paths
