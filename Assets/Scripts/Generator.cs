using UnityEngine;

public class WorldGenerator : MonoBehaviour
{
    [Header("Terrain Settings")]
    public int width = 256;
    public int height = 256;
    public int depth = 50;
    public float scale = 20f;

    [Header("Offsets (random seed)")]
    public float offsetX = 100f;
    public float offsetY = 100f;

    void Start()
    {
        offsetX = Random.Range(0f, 9999f);
        offsetY = Random.Range(0f, 9999f);
        GenerateTerrain();
    }

    void GenerateTerrain()
    {
        Terrain terrain = FindObjectOfType<Terrain>();

        if (terrain == null)
        {
            GameObject t = Terrain.CreateTerrainGameObject(new TerrainData());
            t.name = "ProceduralTerrain";
            terrain = t.GetComponent<Terrain>();
        }
        else
        {
            // Reuse existing terrain instead of spawning new
            terrain.terrainData = new TerrainData();
        }

        TerrainData terrainData = terrain.terrainData;
        terrainData.heightmapResolution = width + 1;
        terrainData.size = new Vector3(width, depth, height);
        terrainData.SetHeights(0, 0, GenerateHeights());
    }

    float[,] GenerateHeights()
    {
        float[,] heights = new float[width, height];
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                float xCoord = (float)x / width * scale + offsetX;
                float yCoord = (float)y / height * scale + offsetY;
                float sample = Mathf.PerlinNoise(xCoord, yCoord);
                heights[x, y] = sample;
            }
        }
        return heights;
    }
}