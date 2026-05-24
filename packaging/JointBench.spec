# -*- mode: python ; coding: utf-8 -*-

from pathlib import Path
import importlib.util

project_root = Path(SPECPATH).parent

datas = [
    (str(project_root / "configs"), "configs"),
    (str(project_root / "docs"), "docs"),
    (str(project_root / "twincat"), "twincat"),
    (str(project_root / "JointBench_Product_Design_Report.md"), "."),
    (str(project_root / "README.md"), "."),
]

hiddenimports = [
    "PySide6.QtCore",
    "PySide6.QtGui",
    "PySide6.QtWidgets",
    "pyqtgraph",
]

if importlib.util.find_spec("pyads") is not None:
    hiddenimports.append("pyads")


a = Analysis(
    [str(project_root / "packaging" / "jointbench_launcher.py")],
    pathex=[str(project_root / "src"), str(project_root)],
    binaries=[],
    datas=datas,
    hiddenimports=hiddenimports,
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=[],
    noarchive=False,
    optimize=0,
)
pyz = PYZ(a.pure)

exe = EXE(
    pyz,
    a.scripts,
    [],
    exclude_binaries=True,
    name="JointBench",
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=True,
    console=False,
    disable_windowed_traceback=False,
    argv_emulation=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
)
coll = COLLECT(
    exe,
    a.binaries,
    a.datas,
    strip=False,
    upx=True,
    upx_exclude=[],
    name="JointBench",
)
