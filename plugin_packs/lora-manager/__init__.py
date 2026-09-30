"""LoRA public manager plugin (owner model, 2026-09-30).

Ownership model: ``Template/loras.json`` is the LoRA data table; each entry carries an
``owner`` (``model`` / ``Plugin`` / ``none``). Any LoRA physically present in the unified
directory (``<comfy_root>/models/loras`` = ``config.LORA_ROOT``) that is **not logged with
``owner=model`` or ``owner=Plugin``** is **public** and owned by this manager. A weight with no
``owner`` counts as ``none`` -> public.

Responsibilities: scan the unified directory, diff against the log, and expose the public list
(default OFF). It does **not** serialize (the pipeline owns the single apply path) and it never
writes ``loras.json``.

Import-time side-effect free: ``config`` / ``loras`` are imported lazily inside the callables.
The scan is (re)run from the ``before_encode`` seam; every failure degrades to a warning.
"""

import logging
import os

PLUGIN_META = {
    "id": "lora-manager",
    "display_name": "LoRA 公共管理器",
    "version": "0.1.0",
    "capabilities": ["before_encode"],
}

_LOG = logging.getLogger("zivai.server")

_PUBLIC_EXTS = (".safetensors", ".ckpt", ".pt", ".sft")
_OWNED = ("model", "plugin")

# Cache keyed by the unified dir mtime so a scan is only redone when the directory changes.
_CACHE = {"mtime": None, "public": (), "known": ()}


def _unified_root():
    try:
        import config

        return (getattr(config, "LORA_ROOT", "") or "").strip()
    except Exception:  # noqa: BLE001 - config unavailable -> nothing to manage
        return ""


def _logged_basenames():
    """Basenames of LoRAs that are already owned in the log (``model`` / ``Plugin``)."""
    try:
        import loras

        registry = loras.load_registry()
    except Exception:  # noqa: BLE001 - no log -> everything is public
        return set()

    owned = set()
    for entry in registry.values():
        if not isinstance(entry, dict):
            continue
        owner = entry.get("owner")
        owner = owner.strip().lower() if isinstance(owner, str) else ""
        if owner not in _OWNED:
            continue
        path = entry.get("path")
        if isinstance(path, str) and path.strip():
            owned.add(os.path.basename(path.strip()).lower())
    return owned


def scan_public(refresh=False):
    """Return ``(public, known)`` filename tuples from the unified directory.

    ``public`` = present in the dir but not owned in the log (default OFF). ``known`` = every
    ``.safetensors``-ish file seen. Re-scans only when the directory mtime changed (or
    ``refresh``). Never raises.
    """
    root = _unified_root()
    if not root or not os.path.isdir(root):
        return (), ()
    try:
        mtime = os.path.getmtime(root)
        files = sorted(
            name
            for name in os.listdir(root)
            if os.path.isfile(os.path.join(root, name))
            and name.lower().endswith(_PUBLIC_EXTS)
        )
    except OSError:
        return (), ()

    if not refresh and _CACHE["mtime"] == mtime:
        return _CACHE["public"], _CACHE["known"]

    owned = _logged_basenames()
    public = tuple(name for name in files if name.lower() not in owned)
    known = tuple(files)
    _CACHE.update({"mtime": mtime, "public": public, "known": known})
    return public, known


def list_public():
    """The public (unowned) LoRA filenames, default OFF."""
    public, _ = scan_public()
    return list(public)


def before_encode(context):
    """Seam hook: refresh the public scan; never patches (no serialization here)."""
    try:
        scan_public(refresh=True)
    except Exception as exc:  # noqa: BLE001 - the manager must never fail the task
        _LOG.warning("lora-manager: scan failed (%s)", exc)
    return None
