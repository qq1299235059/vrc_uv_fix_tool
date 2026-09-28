# SPDX-License-Identifier: GPL-3.0-or-later
"""Blender 5.1 UVX exporter. Keeps UV data on mesh face corners."""

bl_info = {
    "name": "UVX Exporter for Unity UV Tools",
    "author": "niu11",
    "version": (0, 1, 0),
    "blender": (5, 1, 0),
    "location": "File > Export > UVX UV Exchange (.uvx)",
    "description": "Export target and anchor UV maps as face-corner UVX data",
    "category": "Import-Export",
}

import hashlib
import math
import struct

import bpy
from bpy_extras.io_utils import ExportHelper
from bpy.props import EnumProperty, StringProperty


def _text(value):
    encoded = value.encode("utf-8")
    if len(encoded) > 4096:
        raise ValueError("UV map name exceeds 4096 UTF-8 bytes")
    return struct.pack("<I", len(encoded)) + encoded


def _chunk(fourcc, payload):
    if len(fourcc) != 4:
        raise ValueError("UVX FourCC must contain four ASCII characters")
    return struct.pack("<4sHHQQQ", fourcc.encode("ascii"), 1, 0,
                       len(payload), len(payload), 0) + payload


def _finite_vector(value):
    if not all(math.isfinite(component) for component in value):
        raise ValueError("Mesh contains NaN or Infinity in a position or UV")


def build_uvx(obj, target_name, anchor_name):
    if obj is None or obj.type != "MESH":
        raise ValueError("Select a Mesh object")
    if obj.mode == "EDIT":
        obj.update_from_editmode()
    mesh = obj.data
    target = mesh.uv_layers.get(target_name)
    anchor = mesh.uv_layers.get(anchor_name) if anchor_name else None
    if target is None:
        raise ValueError("Target UV map is missing")
    if anchor is None:
        raise ValueError("Choose an Anchor UV map for safe Unity mapping")

    basis = (mesh.shape_keys.key_blocks[0].data
             if mesh.shape_keys and mesh.shape_keys.key_blocks else mesh.vertices)
    if len(basis) != len(mesh.vertices):
        raise ValueError("Basis shape key vertex count differs from the mesh")
    vertices = bytearray()
    for vertex in basis:
        co = vertex.co
        _finite_vector(co)
        vertices.extend(struct.pack("<3f", co.x, co.y, co.z))

    loops = bytearray()
    target_uv = bytearray()
    anchor_uv = bytearray()
    for index, loop in enumerate(mesh.loops):
        loops.extend(struct.pack("<I", loop.vertex_index))
        tu = target.uv[index].vector
        au = anchor.uv[index].vector
        _finite_vector(tu)
        _finite_vector(au)
        target_uv.extend(struct.pack("<2f", tu.x, tu.y))
        anchor_uv.extend(struct.pack("<2f", au.x, au.y))

    polygons = bytearray()
    expected_start = 0
    expected_triangle_count = 0
    for poly in mesh.polygons:
        if poly.loop_start != expected_start or poly.loop_total < 3:
            raise ValueError("Polygon loop layout is not contiguous or contains a non-face")
        polygons.extend(struct.pack("<III", poly.loop_start,
                                    poly.loop_total, poly.material_index))
        expected_start += poly.loop_total
        expected_triangle_count += poly.loop_total - 2
    if expected_start != len(mesh.loops):
        raise ValueError("Polygon loops do not cover the entire loop array")

    info = (struct.pack("<IIIII", len(mesh.vertices), len(mesh.loops),
                        len(mesh.polygons), expected_triangle_count, 0)
            + _text(target_name) + _text(anchor_name))
    payload = b"".join((
        _chunk("INFO", info),
        _chunk("VERT", vertices),
        _chunk("LOOP", loops),
        _chunk("POLY", polygons),
        _chunk("UVTG", target_uv),
        _chunk("UVAN", anchor_uv),
    ))
    header = struct.pack("<4sHHIIQ32sII", b"UVXB", 1, 0, 64, 0,
                         64 + len(payload), hashlib.sha256(payload).digest(), 1, 0)
    return header + payload


def _uv_items(self, context):
    obj = context.active_object
    if obj is None or obj.type != "MESH":
        return [("", "No mesh selected", "Select a Mesh object")]
    return [(layer.name, layer.name, "") for layer in obj.data.uv_layers]


class EXPORT_OT_uvx(bpy.types.Operator, ExportHelper):
    bl_idname = "export_mesh.uvx"
    bl_label = "Export UVX"
    bl_options = {"REGISTER"}
    filename_ext = ".uvx"
    filter_glob: StringProperty(default="*.uvx", options={"HIDDEN"})
    target_uv: EnumProperty(name="Target UV Map", items=_uv_items)
    anchor_uv: EnumProperty(name="Anchor UV Map", items=_uv_items)

    @classmethod
    def poll(cls, context):
        return context.active_object is not None and context.active_object.type == "MESH"

    def invoke(self, context, event):
        layers = context.active_object.data.uv_layers
        if layers:
            self.target_uv = layers.active.name if layers.active else layers[0].name
            self.anchor_uv = layers.active.name if layers.active else layers[0].name
        return super().invoke(context, event)

    def execute(self, context):
        try:
            data = build_uvx(context.active_object, self.target_uv, self.anchor_uv)
            with open(self.filepath, "wb") as stream:
                stream.write(data)
        except (OSError, ValueError) as exc:
            self.report({"ERROR"}, str(exc))
            return {"CANCELLED"}
        self.report({"INFO"}, "Exported UVX: %d bytes" % len(data))
        return {"FINISHED"}


def _menu_export(self, context):
    self.layout.operator(EXPORT_OT_uvx.bl_idname, text="UVX UV Exchange (.uvx)")


def register():
    bpy.utils.register_class(EXPORT_OT_uvx)
    bpy.types.TOPBAR_MT_file_export.append(_menu_export)


def unregister():
    bpy.types.TOPBAR_MT_file_export.remove(_menu_export)
    bpy.utils.unregister_class(EXPORT_OT_uvx)


if __name__ == "__main__":
    register()

