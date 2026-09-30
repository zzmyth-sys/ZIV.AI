"""CPU-only unit tests for the LoRA registry reader (Step 8-1). No torch / comfy.

Run: ``python -m unittest test_loras`` from ``python/server``.
"""

import json
import os
import tempfile
import unittest
from unittest import mock

import config
import loras


def _write(directory, text):
    path = os.path.join(directory, "loras.json")
    with open(path, "w", encoding="utf-8") as handle:
        handle.write(text)
    return path


class LorasRegistryTests(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        # Pin LORA_ROOT empty so the relative-path join added in the config layer cannot
        # make these (root-agnostic) cases depend on an ambient ZIV_AI_LORA_ROOT.
        patcher = mock.patch.object(config, "LORA_ROOT", "")
        patcher.start()
        self.addCleanup(patcher.stop)

    def test_resolve_id_to_entry_and_path(self):
        path = _write(
            self._tmp.name,
            json.dumps(
                {
                    "loras": [
                        {
                            "id": "anime_v2",
                            "path": "D:/lora/anime.safetensors",
                            "default_strength_model": 0.8,
                            "default_strength_clip": 0.8,
                        }
                    ]
                }
            ),
        )
        registry = loras.load_registry(path)
        self.assertIn("anime_v2", registry)
        self.assertEqual("D:/lora/anime.safetensors", loras.resolve_path("anime_v2", registry))
        self.assertEqual(0.8, loras.resolve("anime_v2", registry)["default_strength_model"])

    def test_literal_path_passes_through(self):
        registry = loras.load_registry(_write(self._tmp.name, json.dumps({"loras": []})))
        self.assertEqual("D:/x/y.safetensors", loras.resolve_path("D:/x/y.safetensors", registry))
        self.assertIsNone(loras.resolve("D:/x/y.safetensors", registry))

    def test_missing_file_yields_empty_registry(self):
        self.assertEqual({}, loras.load_registry(os.path.join(self._tmp.name, "nope.json")))
        # An unknown id is passed through as a literal path (so literal paths still work).
        self.assertEqual("anime_v2", loras.resolve_path("anime_v2", {}))

    def test_entries_without_id_are_skipped(self):
        registry = loras.load_registry(
            _write(
                self._tmp.name,
                json.dumps({"loras": [{"path": "x"}, {"id": ""}, {"id": "ok", "path": "p"}]}),
            )
        )
        self.assertEqual(["ok"], list(registry.keys()))

    def test_empty_or_blank_resolves_none(self):
        registry = loras.load_registry(_write(self._tmp.name, json.dumps({"loras": []})))
        self.assertIsNone(loras.resolve_path("", registry))
        self.assertIsNone(loras.resolve_path(None, registry))

    def test_malformed_json_yields_empty_registry(self):
        self.assertEqual({}, loras.load_registry(_write(self._tmp.name, "{not json")))

    def test_top_level_comment_is_ignored(self):
        registry = loras.load_registry(
            _write(
                self._tmp.name,
                json.dumps(
                    {
                        "_comment": "hello",
                        "version": "1",
                        "loras": [{"id": "x", "path": "p"}],
                    }
                ),
            )
        )
        self.assertEqual({"x": {"id": "x", "path": "p"}}, registry)

    def test_validate_passes_for_existing_file(self):
        weight = os.path.join(self._tmp.name, "lora.safetensors")
        with open(weight, "wb") as handle:
            handle.write(b"")
        registry = loras.load_registry(_write(self._tmp.name, json.dumps({"loras": []})))
        # Literal absolute path that exists -> returned unchanged.
        self.assertEqual(weight, loras.resolve_path(weight, registry, validate=True))

    def test_validate_raises_for_missing_file(self):
        missing = os.path.join(self._tmp.name, "nope.safetensors")
        with self.assertRaises(ValueError) as ctx:
            loras.resolve_path(missing, {}, validate=True)
        self.assertIn("LoRA 文件不存在", str(ctx.exception))
        self.assertIn(missing, str(ctx.exception))

    def test_validate_raises_for_registry_id_pointing_to_missing(self):
        registry = loras.load_registry(
            _write(
                self._tmp.name,
                json.dumps({"loras": [{"id": "gone", "path": "Z:/missing/lora.safetensors"}]}),
            )
        )
        with self.assertRaises(ValueError):
            loras.resolve_path("gone", registry, validate=True)

    def test_validate_off_by_default_keeps_pass_through(self):
        # Backward compatible: without validate, a missing path is returned as-is (the plugin
        # relies on this and declines on its own isfile check).
        missing = os.path.join(self._tmp.name, "nope.safetensors")
        self.assertEqual(missing, loras.resolve_path(missing, {}))

    def test_unused_entries_are_not_validated(self):
        # Loading the registry must not touch the filesystem for unused entries.
        registry = loras.load_registry(
            _write(
                self._tmp.name,
                json.dumps({"loras": [{"id": "unused", "path": "Z:/nope/lora.safetensors"}]}),
            )
        )
        self.assertIn("unused", registry)

    def test_resolve_strength_preserves_explicit_zero(self):
        entry = {"default_strength_model": 0.5}
        self.assertEqual(0.0, loras.resolve_strength(0, entry, "default_strength_model"))

    def test_resolve_strength_uses_registry_default_when_absent(self):
        entry = {"default_strength_model": 0.5}
        self.assertEqual(0.5, loras.resolve_strength(None, entry, "default_strength_model"))

    def test_resolve_strength_falls_back_to_one(self):
        self.assertEqual(1.0, loras.resolve_strength(None, {}, "default_strength_model"))


class LoraRootTests(unittest.TestCase):
    """``config.LORA_ROOT`` (设置窗口 [models] lora_root) 对相对路径的拼接语义。"""

    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self._registry = loras.load_registry(
            _write(self._tmp.name, json.dumps({"loras": []}))
        )
        self._root = os.path.join(self._tmp.name, "loras")
        self._relative = os.path.join("sub", "w.safetensors")

    def test_relative_path_joined_with_lora_root(self):
        with mock.patch.object(config, "LORA_ROOT", self._root):
            self.assertEqual(
                os.path.join(self._root, self._relative),
                loras.resolve_path(self._relative, self._registry),
            )

    def test_absolute_path_ignores_lora_root(self):
        absolute = os.path.join(self._tmp.name, "abs.safetensors")
        with mock.patch.object(config, "LORA_ROOT", self._root):
            self.assertEqual(absolute, loras.resolve_path(absolute, self._registry))

    def test_registry_relative_path_is_joined(self):
        registry = loras.load_registry(
            _write(
                self._tmp.name,
                json.dumps({"loras": [{"id": "r", "path": self._relative}]}),
            )
        )
        with mock.patch.object(config, "LORA_ROOT", self._root):
            self.assertEqual(
                os.path.join(self._root, self._relative),
                loras.resolve_path("r", registry),
            )

    def test_relative_path_without_root_passes_through(self):
        with mock.patch.object(config, "LORA_ROOT", ""):
            self.assertEqual(
                self._relative,
                loras.resolve_path(self._relative, self._registry),
            )

    def test_relative_path_without_root_and_validate_raises(self):
        with mock.patch.object(config, "LORA_ROOT", ""):
            with self.assertRaises(ValueError) as ctx:
                loras.resolve_path(self._relative, self._registry, validate=True)
        self.assertIn("lora_root", str(ctx.exception))

    def test_relative_path_with_root_and_validate_checks_joined_file(self):
        os.makedirs(os.path.join(self._root, "sub"), exist_ok=True)
        weight = os.path.join(self._root, self._relative)
        with open(weight, "wb") as handle:
            handle.write(b"")
        with mock.patch.object(config, "LORA_ROOT", self._root):
            self.assertEqual(
                weight, loras.resolve_path(self._relative, self._registry, validate=True)
            )


class LegacyAliasTests(unittest.TestCase):
    """2026-09-30 两类分离：迁出 loras.json 的模板私有 LoRA，旧 id 仍解析到统一目录相对名。"""

    def test_legacy_face_swap_maps_to_unified_relative_name(self):
        with mock.patch.object(config, "LORA_ROOT", r"D:\c\models\loras"):
            self.assertEqual(
                os.path.join(r"D:\c\models\loras", "bfs_head_v1.1_qwen_2.1.safetensors"),
                loras.resolve_path("face-swap", {}),
            )

    def test_unknown_id_still_passes_through(self):
        with mock.patch.object(config, "LORA_ROOT", ""):
            self.assertEqual("no-such-id", loras.resolve_path("no-such-id", {}))


class OwnerRoutingTests(unittest.TestCase):
    """2026-09-30 owner 归属：缺失 / 未知一律 none（归公共管理器）。"""

    def test_explicit_owners(self):
        registry = {
            "m": {"id": "m", "owner": "model", "path": "p"},
            "p": {"id": "p", "owner": "Plugin", "path": "p"},
            "n": {"id": "n", "owner": "none", "path": "p"},
        }
        self.assertEqual("model", loras.resolve_owner("m", registry))
        self.assertEqual("Plugin", loras.resolve_owner("p", registry))
        self.assertEqual("none", loras.resolve_owner("n", registry))

    def test_absent_owner_is_none(self):
        self.assertEqual("none", loras.resolve_owner("x", {"x": {"id": "x", "path": "p"}}))
        self.assertEqual("none", loras.resolve_owner("unknown", {}))
        self.assertEqual("none", loras.resolve_owner("", {}))


class UnifiedViolationTests(unittest.TestCase):
    """2026-09-30 目录唯一强约束：统一目录之外即违规（谓词；调用方硬失败）。"""

    def test_outside_root_is_violation(self):
        with mock.patch.object(config, "LORA_ROOT", r"D:\root\loras"):
            self.assertIsNotNone(loras.unified_violation(r"D:\other\x.safetensors"))
            self.assertIsNone(loras.unified_violation(r"D:\root\loras\x.safetensors"))

    def test_no_root_is_no_violation(self):
        with mock.patch.object(config, "LORA_ROOT", ""):
            self.assertIsNone(loras.unified_violation(r"D:\other\x.safetensors"))


class StrongDirectoryHardFailTests(unittest.TestCase):
    """2026-09-30 目录唯一强约束（硬失败）：handlers 注册统一目录之外的 LoRA 必须抛异常。

    上一轮仅 warning（降级）；本批堆 hard-fail：`_register_loras` 对非统一目录权重抛
    ``ValueError``，由 `_run_submit` 上报为 `lora_unavailable`（不再静默 warning）。
    """

    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.root = os.path.join(self._tmp.name, "loras")
        os.makedirs(self.root)

    def _touch(self, path):
        with open(path, "wb") as handle:
            handle.write(b"")
        return path

    def test_register_outside_root_raises(self):
        import handlers

        outside = self._touch(os.path.join(self._tmp.name, "outside.safetensors"))
        with mock.patch.object(config, "LORA_ROOT", self.root):
            with self.assertRaises(ValueError) as ctx:
                handlers._register_loras({"loras": [{"path": outside}]})
        self.assertIn("统一目录", str(ctx.exception))

    def test_register_inside_root_registers_hook(self):
        import handlers

        inside = self._touch(os.path.join(self.root, "ok.safetensors"))
        with mock.patch.object(config, "LORA_ROOT", self.root), mock.patch.object(
            handlers.pipeline_hooks, "register_pre_sampling_hook"
        ) as register:
            handlers._register_loras({"loras": [{"path": inside}]})
        register.assert_called_once()

    def test_register_without_root_is_not_a_violation(self):
        # 未配置 LORA_ROOT = 无统一目录可违反（与 unified_violation 语义一致）-> 不抛。
        import handlers

        weight = self._touch(os.path.join(self._tmp.name, "any.safetensors"))
        with mock.patch.object(config, "LORA_ROOT", ""), mock.patch.object(
            handlers.pipeline_hooks, "register_pre_sampling_hook"
        ) as register:
            handlers._register_loras({"loras": [{"path": weight}]})
        register.assert_called_once()


class SourcePathSplitTests(unittest.TestCase):
    """2026-09-30 source/path 分离：path=统一目录相对加载名；source=源绝对（供复制）。"""

    def test_get_source_returns_origin(self):
        registry = {
            "x": {"id": "x", "path": "x.safetensors", "source": "C:/src/x.safetensors"},
            "n": {"id": "n", "path": "n.safetensors"},
        }
        self.assertEqual("C:/src/x.safetensors", loras.get_source("x", registry))
        self.assertIsNone(loras.get_source("n", registry))
        self.assertIsNone(loras.get_source("unknown", registry))

    def test_registry_path_is_relative_load_name_joined_with_root(self):
        registry = {"x": {"id": "x", "path": "x.safetensors", "source": "C:/src/x.safetensors"}}
        with mock.patch.object(config, "LORA_ROOT", r"D:\c\models\loras"):
            self.assertEqual(
                os.path.join(r"D:\c\models\loras", "x.safetensors"),
                loras.resolve_path("x", registry),
            )

    def test_legacy_face_swap_id_resolves_to_unified_relative_name(self):
        # Old commands.user.json may still reference the id "face-swap"; with the source/path
        # split it resolves through the registry entry's relative `path`, not a source absolute.
        registry = {
            "face-swap": {
                "id": "face-swap",
                "owner": "model",
                "path": "bfs_head_v1.1_qwen_2.1.safetensors",
                "source": r"C:\AI\ComfyUI_PIC\ComfyUI\models\loras\qwen_image2\bfs_head_v1.1_qwen_2.1.safetensors",
            }
        }
        with mock.patch.object(config, "LORA_ROOT", r"D:\c\models\loras"):
            resolved = loras.resolve_path("face-swap", registry)
        self.assertEqual(
            os.path.join(r"D:\c\models\loras", "bfs_head_v1.1_qwen_2.1.safetensors"),
            resolved,
        )
        self.assertIsNone(loras.unified_violation(resolved))


if __name__ == "__main__":
    unittest.main()
