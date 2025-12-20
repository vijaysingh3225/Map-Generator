using UnityEngine;

public class TerrainColor : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void ApplyColorByHeight(Terrain terrain)
    {
        TerrainData data = terrain.terrainData;
        int w = data.alphamapWidth;
        int h = data.alphamapHeight;
        float[,,] maps = new float[w, h, 2];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float normY = (float)y / h;
                float normX = (float)x / w;
                float height = data.GetHeight(Mathf.FloorToInt(normX * data.heightmapResolution),
                                              Mathf.FloorToInt(normY * data.heightmapResolution)) / data.size.y;

                float grass = Mathf.Clamp01(1f - height * 2f);
                float rock = 1f - grass;

                maps[x, y, 0] = grass; // channel 0
                maps[x, y, 1] = rock;  // channel 1
            }
        }

        data.alphamapResolution = w;
        data.SetAlphamaps(0, 0, maps);
    }
}