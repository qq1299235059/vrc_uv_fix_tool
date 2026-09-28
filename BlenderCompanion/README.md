# Blender UVX Exporter

Install `uvx_exporter.py` in Blender 5.1.2 as an add-on. It adds **File > Export > UVX UV Exchange (.uvx)**.

Choose the UV map to transfer as **Target UV Map** and a UV map already present on the Unity mesh as **Anchor UV Map**. Export one Blender mesh object per `.uvx` file. The exporter reads Basis positions when shape keys exist and preserves UV values per face corner.

The Blender add-on is distributed under GPL-3.0-or-later. See `LICENSE-GPL-3.0-or-later.txt`. The Unity Editor package is distributed separately in the VPM ZIP.

