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
    [Range(0f, 0.2f)] public float riverWidth = 0.05f;

    [Header("Forest Settings")]
    public List<GameObject> treePrefabs = new List<GameObject>();
    [Range(0f, 1f)] public float forestDensity = 0.3f;
    public float forestNoiseScale = 20f;
    public TerrainLayer grassLayer;

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
        SmoothHeights(2);
        terrainData.SetHeights(0, 0, heights);
        terrain.terrainData = terrainData;

        ApplyTextures();
        SpawnWater();
        SpawnTrees();
    }

    private float[,] GenerateHeights()
    {
        float[,] heights = new float[terrainResolution, terrainResolution];
        System.Random prng = new System.Random(seed);
        Vector2[] octaveOffsets = new Vector2[octaves];

        for (int i = 0; i < octaves; i++)
            octaveOffsets[i] = new Vector2(prng.Next(-100000, 100000), prng.Next(-100000, 100000));

        // Create river path using a bezier-like line
        Vector2 riverStart = new Vector2(0, prng.Next(terrainResolution / 4, 3 * terrainResolution / 4));
        Vector2 riverEnd = new Vector2(terrainResolution - 1, prng.Next(terrainResolution / 4, 3 * terrainResolution / 4));
        List<Vector2> riverPath = GenerateBezierRiverPath(riverStart, riverEnd, prng);

        for (int x = 0; x < terrainResolution; x++)
        {
            for (int y = 0; y < terrainResolution; y++)
            {
                float amplitude = 1;
                float frequency = 1;
                float noiseHeight = 0;

                for (int i = 0; i < octaves; i++)
                {
                    float sampleX = (x + octaveOffsets[i].x) / noiseScale * frequency;
                    float sampleY = (y + octaveOffsets[i].y) / noiseScale * frequency;
                    float perlinValue = Mathf.PerlinNoise(sampleX, sampleY) * 2 - 1;
                    noiseHeight += perlinValue * amplitude;

                    amplitude *= persistence;
                    frequency *= lacunarity;
                }

                float height = (noiseHeight + 1f) / 2f;
                height = Mathf.Pow(height, hillSharpness);
                height = Mathf.Min(height, maxHillHeight);

                float riverEffect = GetRiverInfluence(x, y, riverPath);
                height -= riverEffect;

                heights[x, y] = Mathf.Clamp01(height);
            }
        }

        return heights;
    }

    private List<Vector2> GenerateBezierRiverPath(Vector2 start, Vector2 end, System.Random prng)
    {
        Vector2 control = new Vector2(terrainResolution / 2f, prng.Next(terrainResolution / 4, 3 * terrainResolution / 4));
        List<Vector2> points = new List<Vector2>();

        for (float t = 0; t <= 1f; t += 1f / terrainResolution)
        {
            Vector2 point = Mathf.Pow(1 - t, 2) * start + 2 * (1 - t) * t * control + Mathf.Pow(t, 2) * end;
            points.Add(point);
        }

        return points;
    }

    private float GetRiverInfluence(int x, int y, List<Vector2> riverPath)
    {
        float minDist = float.MaxValue;
        foreach (var point in riverPath)
        {
            float dist = Vector2.Distance(new Vector2(x, y), point);
            if (dist < minDist) minDist = dist;
        }

        return Mathf.Clamp01(1 - minDist / (riverWidth * terrainResolution)) * 0.05f;
    }

    private void SmoothHeights(int iterations)
    {
        for (int it = 0; it < iterations; it++)
        {
            float[,] smoothed = new float[terrainResolution, terrainResolution];

            for (int x = 1; x < terrainResolution - 1; x++)
            {
                for (int y = 1; y < terrainResolution - 1; y++)
                {
                    float sum =
                        heights[x, y] +
                        heights[x + 1, y] +
                        heights[x - 1, y] +
                        heights[x, y + 1] +
                        heights[x, y - 1];

                    smoothed[x, y] = sum / 5f;
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
            layers[i] = terrainLayers[i].terrainLayer;

        terrainData.terrainLayers = layers;

        float[,,] splatmap = new float[terrainResolution, terrainResolution, terrainLayers.Count];

        for (int x = 0; x < terrainResolution; x++)
        {
            for (int y = 0; y < terrainResolution; y++)
            {
                float height = heights[x, y];
                for (int i = 0; i < terrainLayers.Count; i++)
                {
                    if (height <= terrainLayers[i].maxHeight)
                    {
                        splatmap[x, y, i] = 1;
                        break;
                    }
                }
            }
        }

        terrainData.SetAlphamaps(0, 0, splatmap);
    }

    private void SpawnWater()
    {
        if (!generateWater || waterPrefab == null)
            return;

        if (waterObject != null)
            Destroy(waterObject);

        Vector3 waterPos = new Vector3(terrainWidth / 2f, waterHeight * terrainHeight, terrainWidth / 2f);
        waterObject = Instantiate(waterPrefab, waterPos, Quaternion.identity, transform);
        waterObject.transform.localScale = new Vector3(terrainWidth / 10f, 1, terrainWidth / 10f);
    }

    private void SpawnTrees()
    {
        if (treePrefabs.Count == 0) return;

        for (int x = 0; x < terrainResolution; x += 3)
        {
            for (int y = 0; y < terrainResolution; y += 3)
            {
                float height = heights[x, y];
                if (height < waterHeight) continue;

                float forestNoise = Mathf.PerlinNoise((x + seed) / forestNoiseScale, (y + seed) / forestNoiseScale);
                if (forestNoise < forestDensity)
                {
                    Vector3 pos = new Vector3(
                        (float)x / terrainResolution * terrainWidth,
                        height * terrainHeight,
                        (float)y / terrainResolution * terrainWidth
                    );

                    Vector3 worldPos = transform.position + pos;
                    if (!Physics.Raycast(worldPos + Vector3.up * 10f, Vector3.down, out RaycastHit hit, 20f)) continue;

                    GameObject tree = treePrefabs[Random.Range(0, treePrefabs.Count)];
                    GameObject instance = Instantiate(tree, hit.point, Quaternion.identity, transform);
                    instance.transform.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0f);
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
