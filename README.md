# UV Tools for Unity and Blender

This package contains two independent Unity Editor modules. The Blender companion source and download are available in the [project repository](https://github.com/qq1299235059/vrc_uv_fix_tool/tree/main/BlenderCompanion).

## Module 1 Blender UVX import

1. In Blender 5.1.2 install and enable `uvx_exporter.py` from the project's `BlenderCompanion` folder or GitHub Release.
2. Select a Mesh object and choose **File > Export > UVX UV Exchange (.uvx)**.
3. Choose a Target UV Map and an Anchor UV Map. The exporter stores UVs per face corner, Basis positions, and polygon rings in a deterministic `.uvx` file.
4. In Unity, open **Tools > UV Tools > 1 Import Blender UVX**, select the imported `.uvx` asset and target renderer, then Analyze Mapping and Apply To New Mesh.

Safe import maps through Anchor UV and preserves the target Mesh's existing vertex data. If the Target UV requires a new seam or the mapping is ambiguous, Apply is blocked. Advanced vertex splitting is intentionally not included in this first import module.

## Module 2 static strain bake

Open **Tools > UV Tools > 2 Strain Bake**. This module samples the current `SkinnedMeshRenderer` shape and solves a static UV correction. It does not read `.uvx` files.

Editor-only static UV compensation for a `SkinnedMeshRenderer`.

The first implementation follows the supplied v0.2 specification:

- `BakeMesh` is sampled only for the current vertex positions.
- The reference positions and source UVs come from `sharedMesh`.
- Per-triangle intrinsic coordinates transfer local stretch and shear into target UV edges.
- A global weighted edge least-squares system is solved with Jacobi-preconditioned conjugate gradients.
- Coincident vertices are welded only when reference position, current position, and source UV agree.
- The result is written to a cloned Mesh Asset, preserving the original mesh and renderer data.

Analyze does not write an asset. Bake creates a new asset under the configured folder and can assign it to the renderer with Undo support.

The strain MVP blocks non-triangle submeshes, non-unit Transform scale, missing source UVs, degenerate inputs, and invalid vertex correspondence. UVX safe import is available separately; advanced seam splitting is not yet implemented.

