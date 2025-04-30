using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(Terrain))]
public class ProceduralTerrainGenerator : MonoBehaviour
{
    [Header("General Settings")]
    public int terrainWidth = 512;
    [Range(0f, 200f)] public int terrainHeight = 100;
    public int terrainResolution = 512;

    [Header("Noise Settings")]
    [Range(50f, 200f)] public float noiseScale = 100f;
    [Range(1, 8)] public int octaves = 4;
    [Range(0f, 1f)] public float persistence = 0.5f;
    [Range(1f, 5f)] public float lacunarity = 2.0f;
    [Range(0.1f, 5f)] public float hillSharpness = 1.5f;
    [Range(0f, 1f)] public float maxHillHeight = 0.8f;

    [Header("Seed Settings")]
    public bool useRandomSeed = true;
    public int seed = 0;

    [Header("Texture Layers")]
    public List<LayerTexture> terrainLayers = new List<LayerTexture>();

    [Header("Water Settings")]
    public bool generateWater = true;
    [Range(0f, 1f)] public float waterHeight = 0.25f;
    public GameObject waterPrefab;

    [Header("River Settings")]
    public int riverCount = 3;
    [Range(0.01f, 0.1f)] public float riverWidth = 0.03f;
    public float riverDepth = 0.05f;
    public int riverSmoothness = 10;

    [Header("Tree & Grass Settings")]
    public List<GameObject> treePrefabs = new List<GameObject>();
    [Range(0f, 1f)] public float treeSpawnChance = 0.01f;
    public TerrainLayer grassLayer;
    public GameObject grassPrefab;

    private Terrain terrain;
    private TerrainData terrainData;
    private float[,] heights;
    private GameObject waterObject;

    private void Start()
    {
        GenerateTerrain();
    }

    public void GenerateTerrain()
    {
        terrain = GetComponent<Terrain>();
        terrainData = new TerrainData();
        terrainData.heightmapResolution = terrainResolution;
        terrainData.size = new Vector3(terrainWidth, terrainHeight, terrainWidth);

        if (useRandomSeed)
            seed = Random.Range(0, 100000);

        heights = GenerateHeights();
        ApplyRivers();
        SmoothHeights(2);
        terrainData.SetHeights(0, 0, heights);
        terrain.terrainData = terrainData;

        ApplyTextures();
        SpawnWater();
        SpawnTrees();
        SpawnGrass();
    }

    private float[,] GenerateHeights()
    {
        float[,] h = new float[terrainResolution, terrainResolution];
        System.Random prng = new System.Random(seed);
        Vector2[] offsets = new Vector2[octaves];

        for (int i = 0; i < octaves; i++)
        {
            offsets[i] = new Vector2(prng.Next(-100000, 100000), prng.Next(-100000, 100000));
        }

        for (int x = 0; x < terrainResolution; x++)
        {
            for (int y = 0; y < terrainResolution; y++)
            {
                float amplitude = 1;
                float frequency = 1;
                float noiseHeight = 0;

                for (int i = 0; i < octaves; i++)
                {
                    float sampleX = (x + offsets[i].x) / noiseScale * frequency;
                    float sampleY = (y + offsets[i].y) / noiseScale * frequency;
                    float perlin = Mathf.PerlinNoise(sampleX, sampleY) * 2 - 1;
                    noiseHeight += perlin * amplitude;

                    amplitude *= persistence;
                    frequency *= lacunarity;
                }

                float height = (noiseHeight + 1f) / 2f;
                height = Mathf.Pow(height, hillSharpness);
                h[x, y] = Mathf.Min(height, maxHillHeight);
            }
        }

        return h;
    }

    private void ApplyRivers()
    {
        for (int r = 0; r < riverCount; r++)
        {
            Vector2 start = GetEdgePoint(r, true);
            Vector2 end = GetEdgePoint(r, false);
            Vector2 direction = (end - start).normalized;

            Vector2 current = start;
            for (int i = 0; i < riverSmoothness; i++)
            {
                float t = (float)i / riverSmoothness;
                Vector2 bend = Vector2.Perpendicular(direction) * Mathf.Sin(t * Mathf.PI * 2 + seed) * 0.2f;
                current += direction + bend;
                CarveRiver(current, riverWidth, riverDepth);
            }
        }
    }

    private Vector2 GetEdgePoint(int index, bool start)
    {
        float edgeBuffer = 0.1f;
        int resolution = terrainResolution;
        System.Random rand = new System.Random(seed + index + (start ? 0 : 999));
        int side = rand.Next(0, 4);
        switch (side)
        {
            case 0: return new Vector2(0, rand.Next((int)(resolution * edgeBuffer), (int)(resolution * (1 - edgeBuffer))));
            case 1: return new Vector2(resolution - 1, rand.Next((int)(resolution * edgeBuffer), (int)(resolution * (1 - edgeBuffer))));
            case 2: return new Vector2(rand.Next((int)(resolution * edgeBuffer), (int)(resolution * (1 - edgeBuffer))), 0);
            case 3: return new Vector2(rand.Next((int)(resolution * edgeBuffer), (int)(resolution * (1 - edgeBuffer))), resolution - 1);
            default: return new Vector2(0, 0);
        }
    }

    private void CarveRiver(Vector2 center, float width, float depth)
    {
        int radius = Mathf.CeilToInt(width * terrainResolution);
        int cx = Mathf.RoundToInt(center.x);
        int cy = Mathf.RoundToInt(center.y);

        for (int x = -radius; x <= radius; x++)
        {
            for (int y = -radius; y <= radius; y++)
            {
                int tx = cx + x;
                int ty = cy + y;
                if (tx >= 0 && ty >= 0 && tx < terrainResolution && ty < terrainResolution)
                {
                    float dist = new Vector2(x, y).magnitude / radius;
                    float depthMultiplier = Mathf.Clamp01(1f - dist);
                    heights[tx, ty] = Mathf.Clamp01(heights[tx, ty] - depth * depthMultiplier);
                }
            }
        }
    }

    private void SmoothHeights(int passes)
    {
        for (int i = 0; i < passes; i++)
        {
            float[,] smoothed = new float[terrainResolution, terrainResolution];

            for (int x = 1; x < terrainResolution - 1; x++)
            {
                for (int y = 1; y < terrainResolution - 1; y++)
                {
                    float avg =
                        (heights[x, y] +
                         heights[x + 1, y] +
                         heights[x - 1, y] +
                         heights[x, y + 1] +
                         heights[x, y - 1]) / 5f;

                    smoothed[x, y] = avg;
                }
            }

            heights = smoothed;
        }
    }

    private void ApplyTextures()
    {
        if (terrainLayers.Count == 0) return;

        TerrainLayer[] layers = new TerrainLayer[terrainLayers.Count];
        for (int i = 0; i < terrainLayers.Count; i++)
        {
            layers[i] = terrainLayers[i].terrainLayer;
        }

        terrainData.terrainLayers = layers;
        float[,,] splatmap = new float[terrainResolution, terrainResolution, layers.Length];

        for (int x = 0; x < terrainResolution; x++)
        {
            for (int y = 0; y < terrainResolution; y++)
            {
                float norm = heights[x, y];
                for (int l = 0; l < terrainLayers.Count; l++)
                {
                    if (norm <= terrainLayers[l].maxHeight)
                    {
                        splatmap[x, y, l] = 1;
                        break;
                    }
                }
            }
        }

        terrainData.SetAlphamaps(0, 0, splatmap);
    }

    private void SpawnWater()
    {
        if (!generateWater || waterPrefab == null) return;

        if (waterObject != null)
            Destroy(waterObject);

        Vector3 pos = new Vector3(terrainWidth / 2f, waterHeight * terrainHeight, terrainWidth / 2f);
        waterObject = Instantiate(waterPrefab, pos, Quaternion.identity, transform);
        waterObject.transform.localScale = new Vector3(terrainWidth / 10f, 1, terrainWidth / 10f);
    }

    private void SpawnTrees()
    {
        if (treePrefabs.Count == 0) return;

        for (int x = 5; x < terrainResolution; x += 5)
        {
            for (int y = 5; y < terrainResolution; y += 5)
            {
                if (Random.value > treeSpawnChance) continue;

                float height = heights[x, y];
                if (height < waterHeight) continue;

                Vector3 worldPos = new Vector3(
                    (float)x / terrainResolution * terrainWidth,
                    height * terrainHeight,
                    (float)y / terrainResolution * terrainWidth
                );

                RaycastHit hit;
                if (Physics.Raycast(worldPos + Vector3.up * 10, Vector3.down, out hit, 20f))
                {
                    GameObject treePrefab = treePrefabs[Random.Range(0, treePrefabs.Count)];
                    GameObject treeInstance = Instantiate(treePrefab, hit.point, Quaternion.Euler(0, Random.Range(0f, 360f), 0), transform);
                }
            }
        }
    }

    private void SpawnGrass()
    {
        if (grassPrefab == null || grassLayer == null) return;

        for (int x = 0; x < terrainResolution; x += 2)
        {
            for (int y = 0; y < terrainResolution; y += 2)
            {
                float height = heights[x, y];
                Vector3 worldPos = new Vector3(
                    (float)x / terrainResolution * terrainWidth,
                    height * terrainHeight,
                    (float)y / terrainResolution * terrainWidth
                );

                int index = terrainLayers.FindIndex(t => t.terrainLayer == grassLayer);
                if (index >= 0)
                {
                    float[,,] map = terrainData.GetAlphamaps(x, y, 1, 1);
                    if (map[0, 0, index] > 0.5f)
                    {
                        Instantiate(grassPrefab, worldPos, Quaternion.identity, transform);
                    }
                }
            }
        }
    }
}

[System.Serializable]
public class LayerTexture
{
    public TerrainLayer terrainLayer;
    [Range(0f, 1f)] public float maxHeight;
}
