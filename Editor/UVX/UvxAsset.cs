using UnityEngine;

namespace UVStrainBaker
{
    public sealed class UvxAsset : ScriptableObject
    {
        [SerializeField] private int majorVersion;
        [SerializeField] private int minorVersion;
        [SerializeField] private string targetUvName;
        [SerializeField] private string anchorUvName;
        [SerializeField] private int vertexCount;
        [SerializeField] private int loopCount;
        [SerializeField] private int polygonCount;
        [SerializeField] private int triangleCount;
        [SerializeField] private string validationState;

        public int MajorVersion => majorVersion;
        public int MinorVersion => minorVersion;
        public string TargetUvName => targetUvName;
        public string AnchorUvName => anchorUvName;
        public int VertexCount => vertexCount;
        public int LoopCount => loopCount;
        public int PolygonCount => polygonCount;
        public int TriangleCount => triangleCount;
        public string ValidationState => validationState;

        internal void SetMetadata(int major, int minor, string target, string anchor,
            int vertices, int loops, int polygons, int triangles, string state)
        {
            majorVersion = major; minorVersion = minor;
            targetUvName = target; anchorUvName = anchor;
            vertexCount = vertices; loopCount = loops;
            polygonCount = polygons; triangleCount = triangles;
            validationState = state;
        }
    }
}

