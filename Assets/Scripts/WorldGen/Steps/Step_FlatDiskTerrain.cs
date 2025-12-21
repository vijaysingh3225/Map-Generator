using UnityEngine;
using WorldGen.Core;

namespace WorldGen.Steps
{
    public sealed class Step_FlatDiskTerrain : MonoBehaviour, IGenerationStep
    {
        public string Name => "Flat Disk Terrain";

        public void Generate(WorldGenSettings settings, WorldContext ctx)
        {
            var go = new GameObject("Terrain_Disk");
            go.transform.SetParent(ctx.WorldRoot.transform, false);

            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();

            var mesh = BuildDiskMesh(settings.radius, settings.ringSegments, settings.radialSegments);
            mf.sharedMesh = mesh;

            if (settings.terrainMaterial != null)
                mr.sharedMaterial = settings.terrainMaterial;

            ctx.TerrainGO = go;
            ctx.TerrainMesh = mesh;
        }

        private static Mesh BuildDiskMesh(float radius, int rings, int radial)
        {
            // Grid in polar coordinates: rings from center outward, radial around the circle.
            // Vertices: (rings+1) * (radial+1) (duplicate seam to make UVs clean)

            int vertCount = (rings + 1) * (radial + 1);
            var verts = new Vector3[vertCount];
            var uvs = new Vector2[vertCount];

            int triCount = rings * radial * 2;
            var tris = new int[triCount * 3];

            int vi = 0;
            for (int r = 0; r <= rings; r++)
            {
                float tR = (float)r / rings;
                float curRadius = tR * radius;

                for (int a = 0; a <= radial; a++)
                {
                    float tA = (float)a / radial;
                    float ang = tA * Mathf.PI * 2f;

                    float x = Mathf.Cos(ang) * curRadius;
                    float z = Mathf.Sin(ang) * curRadius;

                    verts[vi] = new Vector3(x, 0f, z);

                    // Simple UV: center = (0.5,0.5), disk mapped to [0..1]
                    uvs[vi] = new Vector2(0.5f + (x / (radius * 2f)), 0.5f + (z / (radius * 2f)));
                    vi++;
                }
            }

            int ti = 0;
            for (int r = 0; r < rings; r++)
            {
                for (int a = 0; a < radial; a++)
                {
                    int row0 = r * (radial + 1);
                    int row1 = (r + 1) * (radial + 1);

                    int i0 = row0 + a;
                    int i1 = row0 + a + 1;
                    int i2 = row1 + a;
                    int i3 = row1 + a + 1;

                    // Two triangles per quad
                    // Winding order matters: use clockwise winding when looking from +Y so normals point up.
                    tris[ti++] = i0; tris[ti++] = i1; tris[ti++] = i2;
                    tris[ti++] = i1; tris[ti++] = i3; tris[ti++] = i2;
                }
            }

            var mesh = new Mesh();
            mesh.name = "DiskTerrainMesh";

            // If you ever increase resolution a lot, switch to UInt32 indices:
            if (verts.Length > 65535)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
