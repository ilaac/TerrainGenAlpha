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
    [Range(1, 5)]
    public int numberOfMainRivers = 1;
    [Range(0, 10)]
    public int numberOfSideRivers = 3;
    public List<float> mainRiverWidths = new List<float> { 0.35f }; // Widths for main rivers
    public List<float> sideRiverWidths = new List<float> { 0.15f, 0.15f, 0.15f }; // Widths for side rivers
    public List<float> mainRiverDepths = new List<float> { 0.2f }; // Depths for main rivers
    public List<float> sideRiverDepths = new List<float> { 0.1f, 0.1f, 0.1f }; // Depths for side rivers
    public int riverPoints = 100;
    public int sideRiverPoints = 50; // Fewer points for side rivers
    public float sideRiverCurvature = 0.05f; // Controls how much side rivers curve

    [Header("Water Settings")]
    public bool generateWater = true;
    public float waterHeight = 0.25f;
    public float waterDepth = 0.25f;
    public GameObject waterPrefab;

    [Header("Texture Layers")]
    public List<LayerTexture> terrainLayers = new List<LayerTexture>();

    [Header("Forest Settings")]
    public List<GameObject> treePrefabs = new List<GameObject>();
    [Range(0f, 5f)]
    public float treeSpawnChance = 0.02f;
    public float forestDensityScale = 30f;

    [Tooltip("Index in terrainLayers for the forest floor texture. Starts at 0.")]
    public int forestLayerIndex = 0;

    [Range(1f, 20f)]
    public float forestPaintRadius = 4f;

    private Terrain terrain;
    private TerrainData terrainData;
    private float[,] heights;
    private GameObject waterObject;
    private List<List<Vector2>> allRiverPaths;

    private void Start() => GenerateTerrain();

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
            octaveOffsets[i] = new Vector2(prng.Next(-100000, 100000), prng.Next(-100000, 100000));

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
                height = Mathf.Max(height, waterHeight + 0.01f);
                h[x, y] = Mathf.Min(height, maxHillHeight);
            }
        }

        if (generateRiver) CarveRivers(h);
        return h;
    }

    private void CarveRivers(float[,] h)
    {
        allRiverPaths = new List<List<Vector2>>();
        System.Random prng = new System.Random(seed + 1000);

        // Ensure riverWidths and riverDepths lists have correct number of entries
        while (mainRiverWidths.Count < numberOfMainRivers)
            mainRiverWidths.Add(0.35f); // Default width for main rivers
        while (sideRiverWidths.Count < numberOfSideRivers)
            sideRiverWidths.Add(0.15f); // Default width for side rivers
        while (mainRiverDepths.Count < numberOfMainRivers)
            mainRiverDepths.Add(0.2f); // Default depth for main rivers
        while (sideRiverDepths.Count < numberOfSideRivers)
            sideRiverDepths.Add(0.1f); // Default depth for side rivers

        // Generate main rivers (spanning top to bottom)
        for (int i = 0; i < numberOfMainRivers; i++)
        {
            List<Vector2> mainRiver = GenerateRiverPath(prng.Next(), fullSpan: true);
            allRiverPaths.Add(mainRiver);
            float riverWidth = (i < mainRiverWidths.Count) ? mainRiverWidths[i] : 0.35f;
            float riverDepth = (i < mainRiverDepths.Count) ? mainRiverDepths[i] : 0.2f;
            CarveRiverOnTerrain(h, mainRiver, riverWidth, riverDepth);
        }

        // Generate side rivers (randomly placed, shorter)
        for (int i = 0; i < numberOfSideRivers; i++)
        {
            List<Vector2> sideRiver = GenerateSideRiverPath(prng.Next(), h);
            allRiverPaths.Add(sideRiver);
            float riverWidth = (i < sideRiverWidths.Count) ? sideRiverWidths[i] : 0.15f;
            float riverDepth = (i < sideRiverDepths.Count) ? sideRiverDepths[i] : 0.1f;
            CarveRiverOnTerrain(h, sideRiver, riverWidth, riverDepth);
        }

        // Smooth river areas for natural transitions
        SmoothRiverAreas(h);
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

            float offsetX = (Mathf.PerlinNoise(i * 0.1f, localSeed) - 0.5f) * terrainResolution * 0.05f;
            target.x += offsetX;

            current = Vector2.Lerp(current, target, 0.5f);
            current.x = Mathf.Clamp(current.x, 0, terrainResolution - 1);
            current.y = Mathf.Clamp(current.y, 0, terrainResolution - 1);
            path.Add(current / terrainResolution);
        }

        return path;
    }

    private List<Vector2> GenerateSideRiverPath(int localSeed, float[,] heightmap)
    {
        System.Random prng = new System.Random(localSeed);
        List<Vector2> path = new List<Vector2>();

        // Random start point anywhere on the map
        Vector2 start = new Vector2(prng.Next(0, terrainResolution), prng.Next(0, terrainResolution));
        Vector2 current = start;
        path.Add(current / terrainResolution);

        // Random initial direction
        float angle = prng.Next(0, 360);
        float angleRad = angle * Mathf.Deg2Rad;
        Vector2 direction = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad));
        float maxDistance = terrainResolution / 4f;
        float distance = prng.Next(terrainResolution / 8, Mathf.RoundToInt(maxDistance));

        // Initial end point
        Vector2 end = current + direction * distance;
        end = FindDownhillEndPoint(current, end, heightmap, prng);
        end.x = Mathf.Clamp(end.x, 0, terrainResolution - 1);
        end.y = Mathf.Clamp(end.y, 0, terrainResolution - 1);
        end = AdjustEndPointToAvoidCrossings(current, end, allRiverPaths.Count);

        // Generate path with natural curves
        for (int i = 1; i < sideRiverPoints; i++)
        {
            float t = i / (float)(sideRiverPoints - 1);

            // Use Perlin noise for smooth, natural curves
            float offsetX = (Mathf.PerlinNoise(i * 0.1f, localSeed) - 0.5f) * terrainResolution * sideRiverCurvature;
            float offsetY = (Mathf.PerlinNoise(i * 0.1f + 1000, localSeed) - 0.5f) * terrainResolution * sideRiverCurvature;

            // Move towards downhill direction
            Vector2 gradient = CalculateGradient(current, heightmap);
            Vector2 target = current + (gradient.normalized + direction) * (distance / sideRiverPoints);
            target += new Vector2(offsetX, offsetY);

            current = Vector2.Lerp(current, target, 0.5f);
            current.x = Mathf.Clamp(current.x, 0, terrainResolution - 1);
            current.y = Mathf.Clamp(current.y, 0, terrainResolution - 1);

            // Update direction to follow terrain
            direction = (target - current).normalized;
            path.Add(current / terrainResolution);
        }

        return path;
    }

    private Vector2 CalculateGradient(Vector2 point, float[,] heightmap)
    {
        int x = Mathf.RoundToInt(point.x);
        int y = Mathf.RoundToInt(point.y);
        x = Mathf.Clamp(x, 1, terrainResolution - 2);
        y = Mathf.Clamp(y, 1, terrainResolution - 2);

        float dx = heightmap[x + 1, y] - heightmap[x - 1, y];
        float dy = heightmap[x, y + 1] - heightmap[x, y - 1];
        return new Vector2(-dx, -dy).normalized;
    }

    private Vector2 FindDownhillEndPoint(Vector2 start, Vector2 proposedEnd, float[,] heightmap, System.Random prng)
    {
        int x = Mathf.RoundToInt(proposedEnd.x);
        int y = Mathf.RoundToInt(proposedEnd.y);
        x = Mathf.Clamp(x, 0, terrainResolution - 1);
        y = Mathf.Clamp(y, 0, terrainResolution - 1);

        float startHeight = heightmap[Mathf.RoundToInt(start.x), Mathf.RoundToInt(start.y)];
        float currentHeight = heightmap[x, y];
        Vector2 bestEnd = proposedEnd;
        float bestHeight = currentHeight;

        // Sample more points for better downhill path
        int searchRadius = terrainResolution / 8;
        for (int i = 0; i < 20; i++)
        {
            int sx = prng.Next(-searchRadius, searchRadius) + x;
            int sy = prng.Next(-searchRadius, searchRadius) + y;
            sx = Mathf.Clamp(sx, 0, terrainResolution - 1);
            sy = Mathf.Clamp(sy, 0, terrainResolution - 1);

            float sampleHeight = heightmap[sx, sy];
            if (sampleHeight < startHeight && sampleHeight < bestHeight)
            {
                bestEnd = new Vector2(sx, sy);
                bestHeight = sampleHeight;
            }
        }

        return bestEnd;
    }

    private Vector2 AdjustEndPointToAvoidCrossings(Vector2 start, Vector2 end, int riverIndex)
    {
        Vector2 adjustedEnd = end;
        for (int i = 0; i < allRiverPaths.Count; i++)
        {
            if (i >= riverIndex) continue; // Only check against previous rivers
            List<Vector2> otherPath = allRiverPaths[i];

            // Convert to pixel coordinates
            List<Vector2> otherPathPx = new List<Vector2>();
            foreach (Vector2 p in otherPath)
                otherPathPx.Add(p * terrainResolution);

            // Check if the proposed path intersects
            for (int j = 1; j < sideRiverPoints; j++)
            {
                float t = j / (float)(sideRiverPoints - 1);
                Vector2 testPoint = Vector2.Lerp(start, end, t);
                foreach (Vector2 otherPoint in otherPathPx)
                {
                    if (Vector2.Distance(testPoint, otherPoint) < terrainResolution * 0.05f)
                    {
                        // Adjust end point by rotating slightly
                        float angle = Mathf.Atan2(end.y - start.y, end.x - start.x) * Mathf.Rad2Deg + 30f;
                        float distance = Vector2.Distance(start, end);
                        float angleRad = angle * Mathf.Deg2Rad;
                        adjustedEnd = start + new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad)) * distance;
                        adjustedEnd.x = Mathf.Clamp(adjustedEnd.x, 0, terrainResolution - 1);
                        adjustedEnd.y = Mathf.Clamp(adjustedEnd.y, 0, terrainResolution - 1);
                        break;
                    }
                }
            }
        }
        return adjustedEnd;
    }

    private void CarveRiverOnTerrain(float[,] h, List<Vector2> path, float width, float maxDepth)
    {
        int radius = Mathf.CeilToInt(width * terrainResolution * 0.5f);
        maxDepth = Mathf.Max(0.01f, maxDepth);

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
                    if (dist < 1f)
                    {
                        float depth = maxDepth * Mathf.Pow(1f - dist, 2f); // Quadratic falloff for smoother edges
                        h[nx, ny] = Mathf.Min(h[nx, ny], waterHeight - depth);
                    }
                }
            }
        }
    }

    private void SmoothRiverAreas(float[,] h)
    {
        float[,] temp = (float[,])h.Clone();
        int radius = Mathf.CeilToInt(terrainResolution * 0.05f); // Smoothing radius around rivers

        foreach (List<Vector2> path in allRiverPaths)
        {
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

                        if (nx < 1 || ny < 1 || nx >= terrainResolution - 1 || ny >= terrainResolution - 1)
                            continue;

                        float dist = Vector2.Distance(new Vector2(nx, ny), new Vector2(cx, cy)) / radius;
                        if (dist < 1f)
                        {
                            float weight = Mathf.Exp(-dist * dist); // Gaussian-like smoothing
                            float sum = 0f;
                            float weightSum = 0f;

                            for (int dx = -1; dx <= 1; dx++)
                            {
                                for (int dy = -1; dy <= 1; dy++)
                                {
                                    int sx = nx + dx;
                                    int sy = ny + dy;
                                    if (sx < 0 || sy < 0 || sx >= terrainResolution || sy >= terrainResolution)
                                        continue;

                                    float w = Mathf.Exp(-Vector2.Distance(new Vector2(sx, sy), new Vector2(nx, ny)));
                                    sum += h[sx, sy] * w;
                                    weightSum += w;
                                }
                            }

                            temp[nx, ny] = sum / weightSum;
                        }
                    }
                }
            }
        }

        for (int x = 0; x < terrainResolution; x++)
        for (int y = 0; y < terrainResolution; y++)
            h[x, y] = temp[x, y];
    }

    private void SmoothHeights(int iterations)
    {
        for (int it = 0; it < iterations; it++)
        {
            float[,] temp = new float[terrainResolution, terrainResolution];
            for (int x = 1; x < terrainResolution - 1; x++)
            for (int y = 1; y < terrainResolution - 1; y++)
                temp[x, y] = (heights[x, y] + heights[x + 1, y] + heights[x - 1, y] + heights[x, y + 1] +
                              heights[x, y - 1]) / 5f;
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

        TerrainCollider terrainCollider = GetComponent<TerrainCollider>();
        if (terrainCollider == null)
            terrainCollider = gameObject.AddComponent<TerrainCollider>();
        terrainCollider.terrainData = terrainData;

        float[,,] alphaMap = terrainData.GetAlphamaps(0, 0, terrainResolution, terrainResolution);
        System.Random prng = new System.Random(seed);

        int treesSpawned = 0;
        int maxAttempts = 200000;

        for (int i = 0; i < maxAttempts; i++)
        {
            int x = prng.Next(0, terrainResolution);
            int y = prng.Next(0, terrainResolution);

            float h = heights[x, y];
            if (h <= waterHeight + 0.01f) continue;

            float mask = Mathf.PerlinNoise((x + seed) / forestDensityScale, (y + seed) / forestDensityScale);
            if (mask < 0.45f) continue;

            if (Random.value > treeSpawnChance) continue;

            Vector3 worldPos = new Vector3(
                (x / (float)terrainResolution) * terrainWidth,
                h * terrainHeight + 10f,
                (y / (float)terrainResolution) * terrainWidth
            );

            if (Physics.Raycast(worldPos, Vector3.down, out RaycastHit hit, 20f))
            {
                if (hit.point.y <= waterHeight * terrainHeight + 0.5f) continue;

                int radius = Mathf.CeilToInt(forestPaintRadius);
                for (int dx = -radius; dx <= radius; dx++)
                for (int dy = -radius; dy <= radius; dy++)
                {
                    int px = x + dx;
                    int py = y + dy;

                    if (px < 0 || py < 0 || px >= terrainResolution || py >= terrainResolution) continue;

                    float dist = Mathf.Sqrt(dx * dx + dy * dy) / forestPaintRadius;
                    if (dist > 1f) continue;

                    float strength = Mathf.Pow(1f - dist, 1.5f);
                    for (int j = 0; j < terrainData.alphamapLayers; j++)
                    {
                        if (j == forestLayerIndex)
                            alphaMap[py, px, j] = Mathf.Max(alphaMap[py, px, j], strength);
                        else
                            alphaMap[py, px, j] *= (1f - strength);
                    }
                }

                GameObject prefab = treePrefabs[Random.Range(0, treePrefabs.Count)];
                GameObject tree = Instantiate(prefab, hit.point, Quaternion.identity, transform);
                tree.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                treesSpawned++;
            }
        }

        terrainData.SetAlphamaps(0, 0, alphaMap);
        Debug.Log($"Spawned {treesSpawned} trees.");
    }
}

[System.Serializable]
public class LayerTexture
{
    public TerrainLayer terrainLayer;
    [Range(0f, 1f)] public float maxHeight;
}