using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(Terrain))]
public class ProceduralTerrainGenerator : MonoBehaviour
{
    [Header("General Settings")]
    public int terrainWidth = 512;
    public int terrainHeight = 100;
    public int terrainResolution = 512;

    [Header("Noise Settings")]
    public float noiseScale = 100f;
    public int octaves = 4;
    public float persistence = 0.5f;
    public float lacunarity = 2.0f;
    public float hillSharpness = 1.5f;
    public float maxHillHeight = 0.8f;

    [Header("Seed Settings")]
    public bool useRandomSeed = true;
    public int seed = 0;

    [Header("River Settings")]
    public bool generateRiver = true;
    [Range(1, 10)] public int numberOfRivers = 3;
    public List<float> riverWidths = new List<float>() { 0.02f, 0.015f, 0.01f };
    public int riverPoints = 100;

    [Header("Water Settings")]
    public bool generateWater = true;
    public float waterHeight = 0.25f;
    public GameObject waterPrefab;

    [Header("Texture Layers")]
    public List<LayerTexture> terrainLayers = new List<LayerTexture>();

    [Header("Forest Settings")]
    public List<GameObject> treePrefabs = new List<GameObject>();
    [Range(0f, 1f)] public float treeSpawnChance = 0.02f;
    public float forestDensityScale = 30f;

    private Terrain terrain;
    private TerrainData terrainData;
    private float[,] heights;
    private GameObject waterObject;
    private List<List<Vector2>> allRiverPaths;

    private void Start()
    {
        GenerateTerrain();
    }

    public void GenerateTerrain()
    {
        terrain = GetComponent<Terrain>();
        terrainData = new TerrainData
        {
            heightmapResolution = terrainResolution,
            size = new Vector3(terrainWidth, terrainHeight, terrainWidth)
        };
        terrain.terrainData = terrainData;

        if (useRandomSeed)
            seed = Random.Range(0, 100000);

        heights = GenerateHeights();
        SmoothHeights(2);
        terrainData.SetHeights(0, 0, heights);

        ApplyTextures();
        SpawnWater();
        SpawnTrees();
    }

    private float[,] GenerateHeights()
    {
        float[,] h = new float[terrainResolution, terrainResolution];
        Vector2[] octaveOffsets = new Vector2[octaves];
        System.Random prng = new System.Random(seed);

        for (int i = 0; i < octaves; i++)
        {
            octaveOffsets[i] = new Vector2(
                prng.Next(-100000, 100000),
                prng.Next(-100000, 100000));
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
                    float sampleX = (x + octaveOffsets[i].x) / noiseScale * frequency;
                    float sampleY = (y + octaveOffsets[i].y) / noiseScale * frequency;
                    float perlinValue = Mathf.PerlinNoise(sampleX, sampleY) * 2 - 1;

                    noiseHeight += perlinValue * amplitude;
                    amplitude *= persistence;
                    frequency *= lacunarity;
                }

                float height = (noiseHeight + 1f) / 2f;
                height = Mathf.Pow(height, hillSharpness);
                height = Mathf.Max(height, waterHeight + 0.01f); // Prevent ground below water
                h[x, y] = Mathf.Min(height, maxHillHeight);
            }
        }

        if (generateRiver)
            CarveRivers(h);

        return h;
    }

    private void CarveRivers(float[,] h)
    {
        allRiverPaths = new List<List<Vector2>>();
        System.Random prng = new System.Random(seed + 1000);

        if (riverWidths.Count < numberOfRivers)
            while (riverWidths.Count < numberOfRivers)
                riverWidths.Add(0.015f);

        // Main river
        List<Vector2> mainRiver = GenerateRiverPath(prng.Next(), fullSpan: true);
        allRiverPaths.Add(mainRiver);

        for (int i = 1; i < numberOfRivers; i++)
        {
            Vector2 branchStart = mainRiver[prng.Next(mainRiver.Count / 4, 3 * mainRiver.Count / 4)];
            List<Vector2> branch = GenerateBranchRiverPath(prng.Next(), branchStart);
            allRiverPaths.Add(branch);
        }

        for (int i = 0; i < allRiverPaths.Count; i++)
        {
            CarveRiverOnTerrain(h, allRiverPaths[i], riverWidths[i]);
        }
    }

    private List<Vector2> GenerateRiverPath(int localSeed, bool fullSpan = false)
    {
        System.Random prng = new System.Random(localSeed);
        List<Vector2> path = new List<Vector2>();

        Vector2 start = new Vector2(prng.Next(terrainResolution / 4, 3 * terrainResolution / 4), 0);
        Vector2 end = new Vector2(prng.Next(terrainResolution / 4, 3 * terrainResolution / 4), terrainResolution - 1);
        Vector2 current = start;
        path.Add(current / terrainResolution);

        for (int i = 1; i < riverPoints; i++)
        {
            float t = i / (float)(riverPoints - 1);
            Vector2 target = Vector2.Lerp(start, end, t);

            float offsetX = (Mathf.PerlinNoise(i * 0.2f, seed) - 0.5f) * terrainResolution * 0.2f;
            target.x += offsetX;

            current = Vector2.Lerp(current, target, 0.5f);
            current.x = Mathf.Clamp(current.x, 0, terrainResolution - 1);
            current.y = Mathf.Clamp(current.y, 0, terrainResolution - 1);

            path.Add(current / terrainResolution);
        }

        return path;
    }

    private List<Vector2> GenerateBranchRiverPath(int localSeed, Vector2 branchStart)
    {
        System.Random prng = new System.Random(localSeed);
        List<Vector2> path = new List<Vector2>();
        Vector2 current = branchStart * terrainResolution;
        path.Add(current / terrainResolution);

        Vector2 direction = new Vector2(
            prng.Next(-1, 2),
            prng.Next(-1, 1)  // Go in opposite y-direction (e.g., not north like main river)
        ).normalized;

        if (direction == Vector2.zero)
            direction = new Vector2(1, -1).normalized;

        for (int i = 0; i < riverPoints / 2; i++)
        {
            Vector2 noise = new Vector2(
                Mathf.PerlinNoise(i * 0.2f, localSeed) - 0.5f,
                Mathf.PerlinNoise(i * 0.2f + 1000, localSeed) - 0.5f
            ) * 20f;

            current += direction * 8f + noise;
            current.x = Mathf.Clamp(current.x, 0, terrainResolution - 1);
            current.y = Mathf.Clamp(current.y, 0, terrainResolution - 1);
            path.Add(current / terrainResolution);
        }

        return path;
    }

    private void CarveRiverOnTerrain(float[,] h, List<Vector2> path, float width)
    {
        int radius = Mathf.CeilToInt(width * terrainResolution * 0.5f);

        foreach (Vector2 point in path)
        {
            int cx = Mathf.RoundToInt(point.x * terrainResolution);
            int cy = Mathf.RoundToInt(point.y * terrainResolution);

            for (int x = -radius; x <= radius; x++)
            {
                for (int y = -radius; y <= radius; y++)
                {
                    int nx = cx + x;
                    int ny = cy + y;

                    if (nx < 0 || ny < 0 || nx >= terrainResolution || ny >= terrainResolution)
                        continue;

                    float dist = Vector2.Distance(new Vector2(nx, ny), new Vector2(cx, cy)) / radius;
                    if (dist < 1)
                        h[nx, ny] = Mathf.Min(h[nx, ny], waterHeight - 0.01f * (1 - dist));
                }
            }
        }
    }

    private void SmoothHeights(int iterations)
    {
        for (int it = 0; it < iterations; it++)
        {
            float[,] temp = new float[terrainResolution, terrainResolution];

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

                    temp[x, y] = avg;
                }
            }

            heights = temp;
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
                float h = heights[x, y];

                for (int i = 0; i < terrainLayers.Count; i++)
                {
                    if (h <= terrainLayers[i].maxHeight)
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
        if (!generateWater || waterPrefab == null) return;

        if (waterObject != null) Destroy(waterObject);

        Vector3 pos = new Vector3(terrainWidth / 2f, waterHeight * terrainHeight, terrainWidth / 2f);
        waterObject = Instantiate(waterPrefab, pos, Quaternion.identity, transform);
        waterObject.transform.localScale = new Vector3(terrainWidth / 10f, 1, terrainWidth / 10f);
    }

    private void SpawnTrees()
    {
        if (treePrefabs.Count == 0) return;

        for (int x = 0; x < terrainResolution; x += 5)
        {
            for (int y = 0; y < terrainResolution; y += 5)
            {
                float h = heights[x, y];
                if (h < waterHeight + 0.01f) continue;

                float density = Mathf.PerlinNoise((x + seed) / forestDensityScale, (y + seed) / forestDensityScale);
                if (density < 0.5f || Random.value > treeSpawnChance) continue;

                Vector3 worldPos = new Vector3(
                    (float)x / terrainResolution * terrainWidth,
                    h * terrainHeight,
                    (float)y / terrainResolution * terrainWidth
                );

                if (!Physics.Raycast(new Ray(worldPos + Vector3.up * 10, Vector3.down), out RaycastHit hit, 20f)) continue;

                GameObject tree = Instantiate(treePrefabs[Random.Range(0, treePrefabs.Count)], hit.point, Quaternion.identity, transform);
                tree.transform.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0f);
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
