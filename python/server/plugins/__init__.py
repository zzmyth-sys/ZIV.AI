"""可选插件包（batch 1）。

加载器在 :mod:`plugins.loader`；本包只做导出，import 时无副作用。
"""

from . import loader

__all__ = ["loader"]
