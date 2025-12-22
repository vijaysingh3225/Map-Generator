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

            var mesh = BuildGridDiskMesh(settings.radius, settings.cellSizeWorld, settings.clipToCircle, settings.useFlatShading);
            mf.sharedMesh = mesh;

            if (settings.terrainMaterial != null)
                mr.sharedMaterial = settings.terrainMaterial;

            SyncWireframe(go, mesh, settings);

            ctx.TerrainGO = go;
            ctx.TerrainMesh = mesh;
        }

        private static Mesh BuildGridDiskMesh(float radius, float cellSizeWorld, bool clipToCircle, bool flatShade)
        {
            radius = Mathf.Max(0.01f, radius);
            cellSizeWorld = Mathf.Max(0.1f, cellSizeWorld);

            // Uniform grid in XZ, clipped to a circle. Triangle size is consistent everywhere.
            var steps = Mathf.Max(1, Mathf.CeilToInt((radius * 2f) / cellSizeWorld));
            var size = steps + 1;
            var vertCount = size * size;

            var verts = new Vector3[vertCount];
            var uvs = new Vector2[vertCount];
            var inside = new bool[vertCount];

            // Slight epsilon so we don't create tiny holes near the boundary due to float error.
            var eps = cellSizeWorld * 0.25f;
            var r2 = (radius + eps) * (radius + eps);

            int vi = 0;
            for (int z = 0; z < size; z++)
            {
                var zz = -radius + z * cellSizeWorld;
                var tz = (zz + radius) / (radius * 2f);

                for (int x = 0; x < size; x++)
                {
                    var xx = -radius + x * cellSizeWorld;
                    var tx = (xx + radius) / (radius * 2f);

                    verts[vi] = new Vector3(xx, 0f, zz);
                    uvs[vi] = new Vector2(tx, tz);
                    inside[vi] = (xx * xx + zz * zz) <= r2;
                    vi++;
                }
            }

            var tris = new System.Collections.Generic.List<int>(steps * steps * 6);
            for (int z = 0; z < steps; z++)
            {
                for (int x = 0; x < steps; x++)
                {
                    int i0 = (z * size) + x;
                    int i1 = i0 + 1;
                    int i2 = i0 + size;
                    int i3 = i2 + 1;

                    // Two triangles per quad, winding so normals point +Y:
                    // A: (i0, i2, i1), B: (i1, i2, i3)
                    if (!clipToCircle || (inside[i0] && inside[i2] && inside[i1]))
                    {
                        tris.Add(i0); tris.Add(i2); tris.Add(i1);
                    }

                    if (!clipToCircle || (inside[i1] && inside[i2] && inside[i3]))
                    {
                        tris.Add(i1); tris.Add(i2); tris.Add(i3);
                    }
                }
            }

            var mesh = new Mesh();
            mesh.name = "DiskTerrainMesh_Grid";

            // If you ever increase resolution a lot, switch to UInt32 indices:
            if (verts.Length > 65535)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris.ToArray();

            if (flatShade)
                mesh = MakeFlatShaded(mesh);
            else
                mesh.RecalculateNormals();

            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh MakeFlatShaded(Mesh src)
        {
            var srcVerts = src.vertices;
            var srcUv = src.uv;
            var srcTris = src.triangles;

            var hasUv = srcUv != null && srcUv.Length == srcVerts.Length;

            var vCount = srcTris.Length;
            var newVerts = new Vector3[vCount];
            var newNormals = new Vector3[vCount];
            var newUv = new Vector2[vCount];
            var newTris = new int[vCount];

            for (int t = 0; t < srcTris.Length; t += 3)
            {
                var i0 = srcTris[t + 0];
                var i1 = srcTris[t + 1];
                var i2 = srcTris[t + 2];

                var v0 = srcVerts[i0];
                var v1 = srcVerts[i1];
                var v2 = srcVerts[i2];

                var baseIdx = t;
                newVerts[baseIdx + 0] = v0;
                newVerts[baseIdx + 1] = v1;
                newVerts[baseIdx + 2] = v2;

                if (hasUv)
                {
                    newUv[baseIdx + 0] = srcUv[i0];
                    newUv[baseIdx + 1] = srcUv[i1];
                    newUv[baseIdx + 2] = srcUv[i2];
                }

                var n = Vector3.Cross(v1 - v0, v2 - v0);
                if (n.sqrMagnitude > 0f) n.Normalize();
                else n = Vector3.up;

                newNormals[baseIdx + 0] = n;
                newNormals[baseIdx + 1] = n;
                newNormals[baseIdx + 2] = n;

                newTris[baseIdx + 0] = baseIdx + 0;
                newTris[baseIdx + 1] = baseIdx + 1;
                newTris[baseIdx + 2] = baseIdx + 2;
            }

            var mesh = new Mesh();
            mesh.name = src.name + "_Flat";
            if (newVerts.Length > 65535)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            mesh.vertices = newVerts;
            if (hasUv) mesh.uv = newUv;
            mesh.normals = newNormals;
            mesh.triangles = newTris;
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void SyncWireframe(GameObject terrainGo, Mesh terrainMesh, WorldGenSettings settings)
        {
            var existing = terrainGo.transform.Find("Wireframe");

            if (!settings.showWireframe)
            {
                if (existing != null)
                    DestroyImmediate(existing.gameObject);
                return;
            }

            GameObject wfGo;
            if (existing != null) wfGo = existing.gameObject;
            else
            {
                wfGo = new GameObject("Wireframe");
                wfGo.transform.SetParent(terrainGo.transform, worldPositionStays: false);
            }

            var mf = wfGo.GetComponent<MeshFilter>();
            if (mf == null) mf = wfGo.AddComponent<MeshFilter>();

            var mr = wfGo.GetComponent<MeshRenderer>();
            if (mr == null) mr = wfGo.AddComponent<MeshRenderer>();

            mr.sharedMaterial = settings.wireframeMaterial != null ? settings.wireframeMaterial : CreateDefaultWireframeMaterial();

            // Build a line mesh that mirrors the terrain vertex layout so it can be updated later.
            mf.sharedMesh = BuildWireframeMesh(terrainMesh);
        }

        private static Mesh BuildWireframeMesh(Mesh terrainMesh)
        {
            var verts = terrainMesh.vertices;
            var tris = terrainMesh.triangles;

            // Lines: 3 edges per triangle. This may draw duplicate edges, but it's simple and readable.
            var lineIdx = new int[tris.Length * 2];
            int li = 0;
            for (int t = 0; t < tris.Length; t += 3)
            {
                var a = tris[t + 0];
                var b = tris[t + 1];
                var c = tris[t + 2];

                lineIdx[li++] = a; lineIdx[li++] = b;
                lineIdx[li++] = b; lineIdx[li++] = c;
                lineIdx[li++] = c; lineIdx[li++] = a;
            }

            var mesh = new Mesh();
            mesh.name = terrainMesh.name + "_Wireframe";
            if (verts.Length > 65535)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            mesh.vertices = verts;
            mesh.SetIndices(lineIdx, MeshTopology.Lines, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Material CreateDefaultWireframeMaterial()
        {
            var shader =
                Shader.Find("Universal Render Pipeline/Unlit") ??
                Shader.Find("Unlit/Color") ??
                Shader.Find("Sprites/Default");

            var mat = new Material(shader);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(0f, 0f, 0f, 1f));
            else if (mat.HasProperty("_Color")) mat.SetColor("_Color", new Color(0f, 0f, 0f, 1f));
            return mat;
        }
    }
}
