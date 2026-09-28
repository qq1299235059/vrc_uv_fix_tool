using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace UVStrainBaker
{
    internal sealed class UvxData
    {
        public string TargetName, AnchorName;
        public Vector3[] Positions;
        public int[] LoopVertex;
        public UvxPolygon[] Polygons;
        public Vector2[] TargetUv, AnchorUv;
        public int TriangleCount;
    }

    internal readonly struct UvxPolygon
    {
        public readonly int FirstLoop, LoopCount, MaterialSlot;
        public UvxPolygon(int firstLoop, int loopCount, int materialSlot)
        {
            FirstLoop = firstLoop; LoopCount = loopCount; MaterialSlot = materialSlot;
        }
    }

    internal static class UvxBinaryReader
    {
        private const int MaxFileBytes = 512 * 1024 * 1024;
        private const int MaxElements = 20_000_000;

        public static UvxData Read(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                throw new InvalidDataException("Choose an existing .uvx file.");
            var fileInfo = new FileInfo(path);
            if (fileInfo.Length < 64 || fileInfo.Length > MaxFileBytes)
                throw new InvalidDataException("UVX file size is outside the supported range.");
            byte[] bytes = File.ReadAllBytes(path);
            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
            {
                if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "UVXB")
                    throw new InvalidDataException("UVX magic is invalid.");
                ushort major = reader.ReadUInt16(), minor = reader.ReadUInt16();
                if (major != 1) throw new InvalidDataException("Unsupported UVX major version: " + major);
                uint headerSize = reader.ReadUInt32();
                uint flags = reader.ReadUInt32();
                ulong fileSize = reader.ReadUInt64();
                byte[] expectedHash = reader.ReadBytes(32);
                uint meshCount = reader.ReadUInt32();
                reader.ReadUInt32(); // reserved
                if (headerSize != 64 || flags != 0 || fileSize != (ulong)bytes.Length || meshCount != 1)
                    throw new InvalidDataException("UVX header size, flags, file size or mesh count is invalid.");
                using (var sha = SHA256.Create())
                {
                    byte[] hash = sha.ComputeHash(bytes, 64, bytes.Length - 64);
                    for (int i = 0; i < 32; i++)
                        if (hash[i] != expectedHash[i]) throw new InvalidDataException("UVX payload SHA-256 mismatch.");
                }

                var chunks = new Dictionary<string, byte[]>();
                while (stream.Position < stream.Length)
                {
                    if (stream.Length - stream.Position < 32)
                        throw new InvalidDataException("UVX chunk header is truncated.");
                    string fourcc = Encoding.ASCII.GetString(reader.ReadBytes(4));
                    ushort chunkVersion = reader.ReadUInt16();
                    ushort codec = reader.ReadUInt16();
                    ulong rawSize = reader.ReadUInt64();
                    ulong storedSize = reader.ReadUInt64();
                    reader.ReadUInt64(); // reserved hash field
                    if (storedSize > (ulong)(stream.Length - stream.Position) ||
                        rawSize > MaxFileBytes || storedSize > MaxFileBytes)
                        throw new InvalidDataException("UVX chunk size exceeds the file or safety limit.");
                    bool known = fourcc == "INFO" || fourcc == "VERT" || fourcc == "LOOP" ||
                                 fourcc == "POLY" || fourcc == "UVTG" || fourcc == "UVAN";
                    if (!known)
                    {
                        if (minor == 0) throw new InvalidDataException("Unknown UVX chunk in version 1.0: " + fourcc);
                        stream.Position += (long)storedSize;
                        continue;
                    }
                    if (chunkVersion != 1 || codec != 0 || rawSize != storedSize)
                        throw new InvalidDataException("Unsupported UVX chunk version or compression: " + fourcc);
                    if (chunks.ContainsKey(fourcc)) throw new InvalidDataException("Duplicate UVX chunk: " + fourcc);
                    chunks.Add(fourcc, reader.ReadBytes((int)storedSize));
                }
                foreach (string key in new[] { "INFO", "VERT", "LOOP", "POLY", "UVTG", "UVAN" })
                    if (!chunks.ContainsKey(key)) throw new InvalidDataException("Required UVX chunk missing: " + key);
                return Decode(chunks);
            }
        }

        private static UvxData Decode(Dictionary<string, byte[]> chunks)
        {
            int vertices, loops, polygons, triangles;
            string targetName, anchorName;
            using (var reader = Open(chunks["INFO"]))
            {
                vertices = Count(reader.ReadUInt32(), "vertex");
                loops = Count(reader.ReadUInt32(), "loop");
                polygons = Count(reader.ReadUInt32(), "polygon");
                triangles = Count(reader.ReadUInt32(), "triangle");
                if (reader.ReadUInt32() != 0) throw new InvalidDataException("Unsupported UVX identity mode.");
                targetName = ReadText(reader);
                anchorName = ReadText(reader);
                End(reader);
            }
            Exact(chunks["VERT"], vertices, 12);
            Exact(chunks["LOOP"], loops, 4);
            Exact(chunks["POLY"], polygons, 12);
            Exact(chunks["UVTG"], loops, 8);
            Exact(chunks["UVAN"], loops, 8);
            var data = new UvxData
            {
                TargetName = targetName, AnchorName = anchorName,
                Positions = new Vector3[vertices], LoopVertex = new int[loops],
                Polygons = new UvxPolygon[polygons], TargetUv = new Vector2[loops],
                AnchorUv = new Vector2[loops], TriangleCount = triangles
            };
            using (var reader = Open(chunks["VERT"]))
                for (int i = 0; i < vertices; i++)
                    data.Positions[i] = ReadFiniteVector3(reader);
            using (var reader = Open(chunks["LOOP"]))
                for (int i = 0; i < loops; i++)
                {
                    uint vertex = reader.ReadUInt32();
                    if (vertex >= vertices) throw new InvalidDataException("UVX loop vertex is out of range.");
                    data.LoopVertex[i] = (int)vertex;
                }
            int expectedFirst = 0, triangleSum = 0;
            using (var reader = Open(chunks["POLY"]))
                for (int i = 0; i < polygons; i++)
                {
                    uint first = reader.ReadUInt32(), count = reader.ReadUInt32(), material = reader.ReadUInt32();
                    if (first != expectedFirst || count < 3 || count > loops - expectedFirst || material > int.MaxValue)
                        throw new InvalidDataException("UVX polygon ring is invalid.");
                    data.Polygons[i] = new UvxPolygon((int)first, (int)count, (int)material);
                    expectedFirst += (int)count;
                    triangleSum = checked(triangleSum + (int)count - 2);
                }
            if (expectedFirst != loops || triangleSum != triangles)
                throw new InvalidDataException("UVX polygon/triangle counts do not agree.");
            using (var reader = Open(chunks["UVTG"]))
                for (int i = 0; i < loops; i++) data.TargetUv[i] = ReadFiniteVector2(reader);
            using (var reader = Open(chunks["UVAN"]))
                for (int i = 0; i < loops; i++) data.AnchorUv[i] = ReadFiniteVector2(reader);
            return data;
        }

        private static BinaryReader Open(byte[] bytes) =>
            new BinaryReader(new MemoryStream(bytes, false), Encoding.UTF8);

        private static int Count(uint value, string name)
        {
            if (value > MaxElements) throw new InvalidDataException("UVX " + name + " count exceeds safety limit.");
            return (int)value;
        }

        private static void Exact(byte[] bytes, int count, int stride)
        {
            if (bytes.Length != checked((long)count * stride))
                throw new InvalidDataException("UVX chunk payload length does not match its count.");
        }

        private static string ReadText(BinaryReader reader)
        {
            uint length = reader.ReadUInt32();
            if (length > 4096 || length > reader.BaseStream.Length - reader.BaseStream.Position)
                throw new InvalidDataException("UVX string length is invalid.");
            return new UTF8Encoding(false, true).GetString(reader.ReadBytes((int)length));
        }

        private static void End(BinaryReader reader)
        {
            if (reader.BaseStream.Position != reader.BaseStream.Length)
                throw new InvalidDataException("UVX INFO has unexpected trailing data.");
        }

        private static Vector2 ReadFiniteVector2(BinaryReader reader)
        {
            float x = reader.ReadSingle(), y = reader.ReadSingle();
            if (!Finite(x) || !Finite(y)) throw new InvalidDataException("UVX UV contains NaN or Infinity.");
            return new Vector2(x, y);
        }

        private static Vector3 ReadFiniteVector3(BinaryReader reader)
        {
            float x = reader.ReadSingle(), y = reader.ReadSingle(), z = reader.ReadSingle();
            if (!Finite(x) || !Finite(y) || !Finite(z))
                throw new InvalidDataException("UVX position contains NaN or Infinity.");
            return new Vector3(x, y, z);
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}

