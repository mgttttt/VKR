using UnityEngine;
using System.Collections.Generic;
using System.IO;
using UnityEngine.UI;

public enum ObjectType
{
    Tetrahedron,
    Cube,
    Sphere,
    Capsule,
    Cylinder,
    Custom
}

public class TetrahedronWithSpikes3D : MonoBehaviour
{
    [Header("General Settings")]
    public Color objectColor = Color.white;
    public Vector3 lightSource = new Vector3(0, 7, 0);
    public float detectorY = 7.76f;
    public float detectorSize = 4f;
    public int numRaysPerAxis = 15;
    public int maxReflections = 5;
    public Text rayInfoText;

    [Header("Dataset Settings")]
    public bool collectDataset = false;
    public string datasetFileName = "dataset.csv";
    [Tooltip("1 - Flat, 2 - Spikes, 3 - Hemispheres, 4 - Mixed")]
    public int configId = 1; // 1 - Flat, 2 - Spikes, 3 - Hemispheres, 4 - Mixed

    [Header("Object Settings")]
    public ObjectType currentObjectType = ObjectType.Tetrahedron;
    public GameObject customObjectPrefab; // For custom object
    public float baseObjectSize = 2f;

    [Header("Feature Settings")]
    [Tooltip("Размер фич (шипов и полусфер)")]
    public float featureSize = 0.2f; // Новый параметр для контроля размера фич
    [Tooltip("Минимальное расстояние между фичами")]
    public float minFeatureDistance = 0.5f; // Параметр для Poisson Disk Sampling

    [Header("Sampling Settings")]
    [Tooltip("Максимальное количество попыток для генерации точек")]
    public int maxSamplingAttempts = 30;

    private Vector3 lastMousePosition;
    private float rotationSpeed = 100f;
    public GameObject baseObject;
    private List<GameObject> rayLines = new List<GameObject>();

    private float lastPercentage = 0f;

    private bool withSpikes = false;        // For configId=2 and 4
    private bool withHemispheres = false;   // For configId=3 и 4
    private Mesh baseMesh;

    // For dataset
    private StreamWriter datasetWriter;
    private Vector3 lastRecordedRotation;
    private bool voxelGridWritten = false;

    private float absoluteFeatureSize;
    public bool batchmode = false;
    private bool first = true;

    private void Start()
    {
        InitializeAll();
    }

    public void InitializeAll()
    {
        // Аналогично тому, что в Start:
        if (first)
        {
            CreateObjectByType();
        }
        
        ApplySurfaceConfiguration(configId);

        CastRays();

        // Если нужно собираем датасет
        if (collectDataset && first)
        {
            // Инициализируем датасет (запишем правильный заголовок)
            InitializeDataset();

            // Пишем в датасет voxelGrid
            int[,,] voxelGrid = GetVoxelGrid(configId);
            string voxelData = VoxelGridToString(voxelGrid);
            datasetWriter.WriteLine("CONFIG_VOXELS;" + configId + ";" + voxelData);

            voxelGridWritten = true;

            // И сразу пишем текущее состояние (с первыми углами, если оно нужно)
            RecordCurrentState();
        }

        lastRecordedRotation = baseObject.transform.rotation.eulerAngles;
        first = false;
    }


    public void CreateObjectByType()
    {
        if (baseObject != null) Destroy(baseObject);

        switch (currentObjectType)
        {
            case ObjectType.Tetrahedron:
                baseObject = CreateTetrahedronBase();
                break;
            case ObjectType.Cube:
                baseObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
                break;
            case ObjectType.Sphere:
                baseObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                break;
            case ObjectType.Capsule:
                baseObject = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                break;
            case ObjectType.Cylinder:
                baseObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                break;
            case ObjectType.Custom:
                if (customObjectPrefab != null)
                {
                    baseObject = Instantiate(customObjectPrefab);
                }
                else
                {
                    Debug.LogWarning("Custom object prefab is not assigned. Falling back to Cube.");
                    baseObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
                }
                break;
        }

        if (baseObject != null)
        {
            baseObject.transform.SetParent(transform);
            baseObject.transform.localPosition = Vector3.zero;

            var rend = baseObject.GetComponent<Renderer>();
            if (rend != null) rend.material.color = objectColor;

            // Устанавливаем масштаб базового объекта
            float baseMaxSide = 1f;
            if (rend != null)
            {
                Bounds b = rend.bounds;
                baseMaxSide = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            }

            // 2) Переводим featureSize (которое теперь считается «относительным») в абсолютное
            //    Предположим, что если featureSize = 0.2f, это значит «20% от размера объекта».
            //    Тогда абсолютный размер будет:
            absoluteFeatureSize = baseMaxSide * featureSize;
            ScaleToUnit(baseObject, baseObjectSize);
            Physics.SyncTransforms();
        }
    }

    public void ScaleToUnit(GameObject obj, float desiredSize)
    {
        Renderer rend = obj.GetComponent<Renderer>();
        if (rend != null)
        {
            Bounds bounds = rend.bounds;
            Vector3 size = bounds.size;
            float maxSize = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            if (Mathf.Approximately(maxSize, 0f))
            {
                Debug.LogWarning("Object has zero size. Skipping scaling.");
                return;
            }

            // Если хотим, чтобы bounding box имел размер desiredSize
            float scaleFactor = desiredSize / maxSize;
            obj.transform.localScale = obj.transform.localScale * scaleFactor;
        }
        else
        {
            Debug.LogWarning("Renderer not found for object. Skipping scaling.");
        }
    }


    private GameObject CreateTetrahedronBase()
    {
        float h = 1f;
        Vector3[] vertices = new Vector3[]
        {
            new Vector3(-1, 0, -1),
            new Vector3(1, 0, -1),
            new Vector3(1, 0, 1),
            new Vector3(-1, 0, 1),
            new Vector3(0, h, 0)
        };

        int[] triangles = new int[]
        {
            // Base face
            0, 1, 2,
            0, 2, 3,

            // Sides
            0, 4, 1,
            1, 4, 2,
            2, 4, 3,
            3, 4, 0
        };

        GameObject obj = new GameObject("Tetrahedron");
        obj.transform.SetParent(transform);
        obj.transform.localPosition = Vector3.zero;

        MeshFilter meshFilter = obj.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = obj.AddComponent<MeshRenderer>();
        meshRenderer.material = new Material(Shader.Find("Standard")) { color = objectColor };

        Mesh mesh = new Mesh();
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();

        meshFilter.mesh = mesh;
        baseMesh = mesh;

        MeshCollider collider = obj.AddComponent<MeshCollider>();
        collider.sharedMesh = mesh;

        return obj;
    }

    public void ApplySurfaceConfiguration(int configId)
    {
        withSpikes = (configId == 2 || configId == 4);
        withHemispheres = (configId == 3 || configId == 4);

        if (configId == 1)
            return; // Flat surface, no features

        GenerateSurfaceFeaturesForAll();
    }

    private void GenerateSurfaceFeaturesForAll()
    {
        MeshFilter mf = baseObject.GetComponent<MeshFilter>();
        if (mf == null || mf.mesh == null)
        {
            Debug.LogWarning("No MeshFilter found on the base object. Surface features not generated.");
            return;
        }

        Mesh mesh = mf.mesh;
        GenerateSurfaceFeatures(mesh);
    }

    private void GenerateSurfaceFeatures(Mesh mesh)
    {
        // Сначала удалим старые фичи
        ClearExistingFeatures();

        // 1) Рассчитываем максимальный размер базового объекта (после ScaleToUnit).
        Renderer rend = baseObject.GetComponent<Renderer>();
        float baseMaxSide = 1f;
        if (rend != null)
        {
            Bounds b = rend.bounds;
            baseMaxSide = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        }

        // 2) Переводим featureSize (которое теперь считается «относительным») в абсолютное
        //    Предположим, что если featureSize = 0.2f, это значит «20% от размера объекта».
        //    Тогда абсолютный размер будет
        Debug.Log($"baseMaxSide={baseMaxSide}, featureSize={featureSize}, absoluteFeatureSize={absoluteFeatureSize}");

        // Далее всё остальное остаётся как было
        // Распределение фич на основе плотности...
        List<Vector3> sampledPoints = PoissonDiskSampling(mesh, minFeatureDistance, maxSamplingAttempts);

        Debug.Log($"Sampled Points Count: {sampledPoints.Count}");

        // Для смешанной конфигурации будем случайно выбирать тип фичи для каждой точки
        foreach (Vector3 point in sampledPoints)
        {
            Vector3 normal = GetNormalAtPoint(mesh, point);

            if (withSpikes && withHemispheres)
            {
                // Случайным образом выбираем тип фичи
                if (Random.value < 0.5f)
                {
                    CreateSpike(point, normal, mesh, absoluteFeatureSize);
                }
                else
                {
                    CreateHemisphere(point, normal, mesh, absoluteFeatureSize);
                }
            }
            else if (withSpikes)
            {
                CreateSpike(point, normal, mesh, absoluteFeatureSize);
            }
            else if (withHemispheres)
            {
                CreateHemisphere(point, normal, mesh, absoluteFeatureSize);
            }
        }

        Debug.Log("Surface feature generation completed.");
    }


    /// <summary>
    /// Удаляет все существующие фичи (шипы и полусферы) на объекте.
    /// </summary>
    public void ClearExistingFeatures()
    {
        foreach (Transform child in baseObject.transform)
        {
            if (child.name.StartsWith("Spike") || child.name.StartsWith("Hemisphere"))
            {
                Destroy(child.gameObject);
            }
        }
    }

    /// <summary>
    /// Реализация Poisson Disk Sampling для поверхностей меша.
    /// </summary>
    /// <param name="mesh">Меш объекта.</param>
    /// <param name="minDistance">Минимальное расстояние между фичами.</param>
    /// <param name="maxAttempts">Максимальное количество попыток для генерации каждой точки.</param>
    /// <returns>Список точек для размещения фич.</returns>
    private List<Vector3> PoissonDiskSampling(Mesh mesh, float minDistance, int maxAttempts)
    {
        List<Vector3> points = new List<Vector3>();
        List<Vector3> activeList = new List<Vector3>();

        // Генерируем начальную точку
        Vector3 initialPoint = GetRandomPointOnMesh(mesh);
        points.Add(initialPoint);
        activeList.Add(initialPoint);

        while (activeList.Count > 0)
        {
            // Выбираем случайную активную точку
            int randomIndex = Random.Range(0, activeList.Count);
            Vector3 currentPoint = activeList[randomIndex];

            bool found = false;
            for (int i = 0; i < maxAttempts; i++)
            {
                // Генерируем случайную точку вокруг текущей точки
                Vector3 newPoint = GenerateRandomPointAround(currentPoint, minDistance, mesh);

                if (newPoint != Vector3.zero && IsFarEnough(newPoint, points, minDistance))
                {
                    points.Add(newPoint);
                    activeList.Add(newPoint);
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                activeList.RemoveAt(randomIndex);
            }
        }

        return points;
    }


    private Vector3 ClosestPointOnMesh(Mesh mesh, Vector3 point)
    {
        Vector3 closestPoint = Vector3.zero;
        float minDist = float.MaxValue;

        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        Matrix4x4 localToWorld = baseObject.transform.localToWorldMatrix;

        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 a = localToWorld.MultiplyPoint3x4(vertices[triangles[i]]);
            Vector3 b = localToWorld.MultiplyPoint3x4(vertices[triangles[i + 1]]);
            Vector3 c = localToWorld.MultiplyPoint3x4(vertices[triangles[i + 2]]);

            Vector3 cp = ClosestPointOnTriangle(point, a, b, c);
            float dist = Vector3.Distance(point, cp);

            if (dist < minDist)
            {
                minDist = dist;
                closestPoint = cp;
            }
        }

        return closestPoint;
    }

    private Vector3 GenerateRandomPointAround(Vector3 point, float minDistance, Mesh mesh)
    {
        int retryCount = 0;
        int maxRetries = 10; // Максимальное количество попыток для генерации точки

        while (retryCount < maxRetries)
        {
            float radius = minDistance;
            float angle = Random.Range(0f, Mathf.PI * 2);
            float elevation = Random.Range(-Mathf.PI / 4, Mathf.PI / 4); // Ограничение угла наклона для более равномерного распределения

            Vector3 direction = new Vector3(Mathf.Cos(angle) * Mathf.Cos(elevation), Mathf.Sin(elevation), Mathf.Sin(angle) * Mathf.Cos(elevation));
            Vector3 newPoint = point + direction * radius;

            // Проверяем, находится ли новая точка на поверхности меша с адаптивным порогом
            float adaptiveThreshold = minDistance * 0.1f; // Порог адаптируется в зависимости от minDistance

            if (IsPointOnMesh(mesh, newPoint, adaptiveThreshold))
            {
                return newPoint;
            }

            retryCount++;
        }

        Debug.LogWarning($"Failed to generate a valid point around {point} after {maxRetries} attempts.");
        return Vector3.zero;
    }

    private bool IsPointOnMesh(Mesh mesh, Vector3 point, float threshold)
    {
        Vector3 closestPoint = ClosestPointOnMesh(mesh, point);
        float distance = Vector3.Distance(point, closestPoint);
        return distance <= threshold;
    }

    /// <summary>
    /// Проверяет, достаточно ли новая точка удалена от всех существующих точек.
    /// </summary>
    private bool IsFarEnough(Vector3 newPoint, List<Vector3> points, float minDistance)
    {
        foreach (Vector3 p in points)
        {
            if (Vector3.Distance(newPoint, p) < minDistance)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Получает случайную точку на поверхности меша.
    /// </summary>
    private Vector3 GetRandomPointOnMesh(Mesh mesh)
    {
        float totalArea = 0f;
        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;

        // Вычисляем площади всех треугольников
        List<float> areas = new List<float>();
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 a = mesh.vertices[triangles[i]];
            Vector3 b = mesh.vertices[triangles[i + 1]];
            Vector3 c = mesh.vertices[triangles[i + 2]];

            float area = Vector3.Cross(b - a, c - a).magnitude / 2f;
            areas.Add(area);
            totalArea += area;
        }

        // Выбираем случайный треугольник с вероятностью, пропорциональной его площади
        float rand = Random.Range(0f, totalArea);
        float cumulative = 0f;
        int triangleIndex = 0;
        for (int i = 0; i < areas.Count; i++)
        {
            cumulative += areas[i];
            if (rand <= cumulative)
            {
                triangleIndex = i;
                break;
            }
        }

        // Генерируем случайную точку внутри выбранного треугольника
        Vector3 va = mesh.vertices[triangles[triangleIndex * 3]];
        Vector3 vb = mesh.vertices[triangles[triangleIndex * 3 + 1]];
        Vector3 vc = mesh.vertices[triangles[triangleIndex * 3 + 2]];

        float u = Random.Range(0f, 1f);
        float v = Random.Range(0f, 1f);

        if (u + v > 1f)
        {
            u = 1f - u;
            v = 1f - v;
        }

        Vector3 randomPoint = va + u * (vb - va) + v * (vc - va);

        // Преобразуем в мировые координаты
        return baseObject.transform.TransformPoint(randomPoint);
    }

    private Vector3 GetNormalAtPoint(Mesh mesh, Vector3 point)
    {
        // Находим ближайший треугольник и возвращаем его нормаль

        Vector3 closestNormal = Vector3.up;
        float minDist = float.MaxValue;

        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        Matrix4x4 localToWorld = baseObject.transform.localToWorldMatrix;

        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 a = localToWorld.MultiplyPoint3x4(vertices[triangles[i]]);
            Vector3 b = localToWorld.MultiplyPoint3x4(vertices[triangles[i + 1]]);
            Vector3 c = localToWorld.MultiplyPoint3x4(vertices[triangles[i + 2]]);

            Vector3 closestPoint = ClosestPointOnTriangle(point, a, b, c);
            float dist = Vector3.Distance(point, closestPoint);

            if (dist < minDist)
            {
                minDist = dist;
                Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                closestNormal = normal;
            }
        }

        return closestNormal;
    }

    private Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        // Алгоритм ближайшей точки на треугольнике
        Vector3 ab = b - a;
        Vector3 ac = c - a;
        Vector3 ap = p - a;

        float d1 = Vector3.Dot(ab, ap);
        float d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f) return a;

        Vector3 bp = p - b;
        float d3 = Vector3.Dot(ab, bp);
        float d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3) return b;

        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0f && d1 >= 0f && d3 <= 0f)
        {
            float v = d1 / (d1 - d3);
            return a + v * ab;
        }

        Vector3 cp = p - c;
        float d5 = Vector3.Dot(ab, cp);
        float d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6) return c;

        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0f && d2 >= 0f && d6 <= 0f)
        {
            float w = d2 / (d2 - d6);
            return a + w * ac;
        }

        float va = d3 * d6 - d5 * d4;
        if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
        {
            float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
            return b + w * (c - b);
        }

        float denom = 1f / (va + vb + vc);
        float vFinal = vb * denom;
        float wFinal = vc * denom;
        return a + ab * vFinal + ac * wFinal;
    }

    private void CreateSpike(Vector3 point, Vector3 normal, Mesh mesh, float absSize)
    {
        GameObject spike = new GameObject("Spike");
        spike.transform.SetParent(baseObject.transform);
        spike.transform.position = point;
        spike.transform.up = normal;

        Mesh spikeMesh = CreateSpikeMesh(); // меш высотой 1.0f (см. ниже)

        MeshFilter meshFilter = spike.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = spike.AddComponent<MeshRenderer>();
        meshRenderer.material = new Material(Shader.Find("Unlit/Color")) { color = objectColor };

        meshFilter.mesh = spikeMesh;
        MeshCollider collider = spike.AddComponent<MeshCollider>();
        collider.sharedMesh = spikeMesh;

        // Теперь масштабируем шип, основываясь на absSize:
        // допустим, меш высотой 1, значит:
        spike.transform.localScale = Vector3.one * absSize;
    }

    private void CreateHemisphere(Vector3 point, Vector3 normal, Mesh mesh, float absSize)
    {
        GameObject hemisphere = new GameObject("Hemisphere");
        hemisphere.transform.SetParent(baseObject.transform);
        hemisphere.transform.position = point;
        hemisphere.transform.up = normal;

        Mesh hemisphereMesh = CreateHemisphereMesh(1f);

        MeshFilter meshFilter = hemisphere.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = hemisphere.AddComponent<MeshRenderer>();
        meshRenderer.material = new Material(Shader.Find("Unlit/Color")) { color = objectColor };

        meshFilter.mesh = hemisphereMesh;
        MeshCollider collider = hemisphere.AddComponent<MeshCollider>();
        collider.sharedMesh = hemisphereMesh;

        // Точно так же — меш «единичной высоты»,
        // значит масштаб = absSize
        hemisphere.transform.localScale = Vector3.one * absSize;

        Debug.Log($"Hemisphere created at {hemisphere.transform.position} with size {absSize}");
    }


    private Mesh CreateSpikeMesh()
    {
        Vector3[] vertices = new Vector3[]
        {
            new Vector3(-0.2f, 0, -0.2f),
            new Vector3(0.2f, 0, -0.2f),
            new Vector3(0.2f, 0, 0.2f),
            new Vector3(-0.2f, 0, 0.2f),
            new Vector3(0, 0.6f, 0)
        };

        int[] triangles = new int[]
        {
            0, 1, 2,
            0, 2, 3,

            0, 4, 1,
            1, 4, 2,
            2, 4, 3,
            3, 4, 0
        };

        Mesh mesh = new Mesh();
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();

        return mesh;
    }

    private Mesh CreateHemisphereMesh(float radius)
    {
        // Создаём стандартную сферу и обрезаем её наполовину
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        MeshFilter mf = sphere.GetComponent<MeshFilter>();
        Mesh sphereMesh = mf.mesh;
        Destroy(sphere);

        // Обрезаем полусферу (верхнюю половину)
        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();

        Matrix4x4 localToWorld = baseObject.transform.localToWorldMatrix;

        for (int i = 0; i < sphereMesh.triangles.Length; i += 3)
        {
            Vector3 a = localToWorld.MultiplyPoint3x4(sphereMesh.vertices[sphereMesh.triangles[i]] * radius);
            Vector3 b = localToWorld.MultiplyPoint3x4(sphereMesh.vertices[sphereMesh.triangles[i + 1]] * radius);
            Vector3 c = localToWorld.MultiplyPoint3x4(sphereMesh.vertices[sphereMesh.triangles[i + 2]] * radius);

            // Проверяем, находятся ли все вершины выше или на уровне нуля по y
            if (a.y >= 0 && b.y >= 0 && c.y >= 0)
            {
                int baseIndex = vertices.Count;
                vertices.Add(a);
                vertices.Add(b);
                vertices.Add(c);

                triangles.Add(baseIndex);
                triangles.Add(baseIndex + 1);
                triangles.Add(baseIndex + 2);
            }
        }

        if (vertices.Count == 0)
        {
            Debug.LogWarning("No triangles found for hemisphere mesh.");
        }

        Mesh hemisphereMesh = new Mesh();
        hemisphereMesh.vertices = vertices.ToArray();
        hemisphereMesh.triangles = triangles.ToArray();
        hemisphereMesh.RecalculateNormals();

        return hemisphereMesh;
    }

    public void InitializeDataset()
    {
        string path = Path.Combine(Application.dataPath, datasetFileName);
        datasetWriter = new StreamWriter(path, append: true); // Append to avoid overwriting

        // Если файл пуст — пишем полный заголовок
        if (new FileInfo(path).Length == 0)
        {
            datasetWriter.WriteLine("Формат файла:");
            datasetWriter.WriteLine("CONFIG_VOXELS;config_id;voxels");
            // Добавляем ВСЕ нужные колонки (включая featureSize, minFeatureDistance):
            datasetWriter.WriteLine("config_id;rotationX;rotationY;rotationZ;percentage;featureSize;minFeatureDistance");
            datasetWriter.WriteLine();
        }
    }


    private void ClearRays()
    {
        foreach (GameObject ray in rayLines)
        {
            Destroy(ray);
        }
        rayLines.Clear();
    }

    public void CastRays()
    {
        ClearRays();

        int raysHitDetector = 0;
        int totalRays = numRaysPerAxis * numRaysPerAxis;

        float areaSize = 2f;
        float halfArea = areaSize / 2f;

        for (int i = 0; i < numRaysPerAxis; i++)
        {
            for (int j = 0; j < numRaysPerAxis; j++)
            {
                float x = lightSource.x - halfArea + (areaSize / (numRaysPerAxis - 1)) * i;
                float z = lightSource.z - halfArea + (areaSize / (numRaysPerAxis - 1)) * j;
                Vector3 startPoint = new Vector3(x, lightSource.y, z);
                Vector3 rayDir = Vector3.down;

                int reflections = 0;
                bool hitDetector = false;
                Vector3 currentPoint = startPoint;

                while (reflections < maxReflections && !hitDetector)
                {
                    RaycastHit hit;
                    if (Physics.Raycast(currentPoint, rayDir, out hit, Mathf.Infinity))
                    {
                        DrawRay(currentPoint, hit.point, Color.red);

                        Vector3 normal = hit.normal.normalized;
                        if (normal == Vector3.zero)
                        {
                            Debug.LogWarning("Collision normal is zero, reflection impossible");
                            break;
                        }

                        rayDir = Vector3.Reflect(rayDir, normal).normalized;
                        currentPoint = hit.point + rayDir * 0.0001f;
                        reflections++;
                    }
                    else
                    {
                        if (Mathf.Approximately(rayDir.y, 0f))
                            break;

                        float distanceToDetector = (detectorY - currentPoint.y) / rayDir.y;
                        Vector3 detectorPoint = currentPoint + rayDir * distanceToDetector;

                        if (distanceToDetector >= 0 && Mathf.Abs(detectorPoint.x) <= detectorSize / 2 && Mathf.Abs(detectorPoint.z) <= detectorSize / 2)
                        {
                            DrawRay(currentPoint, detectorPoint, Color.green);
                            raysHitDetector++;
                            hitDetector = true;
                        }
                        else
                        {
                            if (reflections == 0)
                            {
                                DrawRay(currentPoint, currentPoint + rayDir * 1000f, Color.red);
                            }
                            else
                            {
                                DrawRay(currentPoint, currentPoint + rayDir * 1000f, Color.green);
                            }
                            break;
                        }
                    }
                }
            }
        }

        float percentage = (float)raysHitDetector / totalRays * 100f;
        lastPercentage = percentage;
        if (rayInfoText != null)
        {
            rayInfoText.text = $"Rays Hit Detector: {raysHitDetector}/{totalRays} ({percentage:F1}%)";
        }
    }

    public float GetLastPercentage()
    {
        return lastPercentage;
    }

    private void Update()
    {
        HandleMouseRotation();

        if (collectDataset && voxelGridWritten)
        {
            Vector3 currentRot = baseObject.transform.rotation.eulerAngles;
            if (Vector3.Distance(currentRot, lastRecordedRotation) > 0.01f)
            {
                CastRays();
                RecordCurrentState();
                lastRecordedRotation = currentRot;
            }
        }
    }

    private void HandleMouseRotation()
    {
        if (Input.GetMouseButtonDown(0))
        {
            lastMousePosition = Input.mousePosition;
        }
        else if (Input.GetMouseButton(0))
        {
            Vector3 delta = Input.mousePosition - lastMousePosition;
            lastMousePosition = Input.mousePosition;

            float rotationX = delta.y * rotationSpeed * Time.deltaTime;
            float rotationY = -delta.x * rotationSpeed * Time.deltaTime;

            baseObject.transform.Rotate(rotationX, rotationY, 0, Space.World);
            Physics.SyncTransforms();

            CastRays();

            if (collectDataset && voxelGridWritten)
                RecordCurrentState();
        }
    }

    public void RecordCurrentState()
    {
        if (datasetWriter == null) return;

        float rotX = baseObject.transform.rotation.eulerAngles.x;
        float rotY = baseObject.transform.rotation.eulerAngles.y;
        float rotZ = baseObject.transform.rotation.eulerAngles.z;
        float percentage = GetLastPercentage();

        // Пишем: config_id;rotX;rotY;rotZ;percentage;featureSize;minFeatureDistance
        // featureSize - относительный размер (число от 0 до 1+)
        // minFeatureDistance - как признак «плотности»
        datasetWriter.WriteLine(configId + ";" +
                                rotX.ToString("F3") + ";" +
                                rotY.ToString("F3") + ";" +
                                rotZ.ToString("F3") + ";" +
                                percentage.ToString("F3") + ";" +
                                featureSize.ToString("F3") + ";" +
                                minFeatureDistance.ToString("F3"));
    }

    private void OnDestroy()
    {
        if (datasetWriter != null)
        {
            datasetWriter.Flush();
            datasetWriter.Close();
            datasetWriter = null;
        }
    }

    private int[,,] GetVoxelGrid(int configId)
    {
        // Voxelization for all objects
        MeshFilter mf = baseObject.GetComponent<MeshFilter>();
        if (mf == null || mf.mesh == null)
        {
            // No mesh - return empty grid
            int N = 16;
            return new int[N, N, N];
        }

        Mesh mesh = mf.mesh;
        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;

        // Calculate bounding box in world coordinates
        Bounds bounds = mesh.bounds;
        Matrix4x4 localToWorld = baseObject.transform.localToWorldMatrix;

        Vector3 min = localToWorld.MultiplyPoint3x4(bounds.min);
        Vector3 max = localToWorld.MultiplyPoint3x4(bounds.max);

        int N2 = 16;
        int[,,] voxelGrid = new int[N2, N2, N2];

        // Define voxel size
        float dx = (max.x - min.x) / N2;
        float dy = (max.y - min.y) / N2;
        float dz = (max.z - min.z) / N2;

        // Prepare triangles in world coordinates
        List<(Vector3, Vector3, Vector3)> faces = new List<(Vector3, Vector3, Vector3)>();
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 A = localToWorld.MultiplyPoint3x4(vertices[triangles[i]]);
            Vector3 B = localToWorld.MultiplyPoint3x4(vertices[triangles[i + 1]]);
            Vector3 C = localToWorld.MultiplyPoint3x4(vertices[triangles[i + 2]]);
            faces.Add((A, B, C));
        }

        float threshold = 0.2f; // Увеличен порог для лучшей вокселизации

        for (int x = 0; x < N2; x++)
        {
            for (int y = 0; y < N2; y++)
            {
                for (int z = 0; z < N2; z++)
                {
                    float X = min.x + (x + 0.5f) * dx;
                    float Y = min.y + (y + 0.5f) * dy;
                    float Z = min.z + (z + 0.5f) * dz;

                    Vector3 p = new Vector3(X, Y, Z);

                    float minDist = float.MaxValue;
                    foreach (var (a, b, c) in faces)
                    {
                        float dist = PointTriangleDistance(p, a, b, c);
                        if (dist < minDist)
                            minDist = dist;
                    }

                    if (minDist < threshold)
                    {
                        int val = 1;
                        if (configId == 2) val = 2;
                        if (configId == 3) val = 3;
                        if (configId == 4) val = 4; // Mixed, can differentiate if needed
                        voxelGrid[x, y, z] = val;
                    }
                    else
                    {
                        voxelGrid[x, y, z] = 0;
                    }
                }
            }
        }

        return voxelGrid;
    }

    private string VoxelGridToString(int[,,] voxelGrid)
    {
        int N = voxelGrid.GetLength(0);
        List<string> vals = new List<string>();
        for (int x = 0; x < N; x++)
        {
            for (int y = 0; y < N; y++)
            {
                for (int z = 0; z < N; z++)
                {
                    vals.Add(voxelGrid[x, y, z].ToString());
                }
            }
        }
        return string.Join(";", vals);
    }

    private float PointTriangleDistance(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 v0 = b - a;
        Vector3 v1 = c - a;
        Vector3 v2 = p - a;

        float dot00 = Vector3.Dot(v0, v0);
        float dot01 = Vector3.Dot(v0, v1);
        float dot02 = Vector3.Dot(v0, v2);
        float dot11 = Vector3.Dot(v1, v1);
        float dot12 = Vector3.Dot(v1, v2);

        float denom = (dot00 * dot11 - dot01 * dot01);
        if (Mathf.Approximately(denom, 0f)) // Degenerate triangle
        {
            // Distance to edges
            float dA = DistPointToSegment(p, a, b);
            float dB = DistPointToSegment(p, b, c);
            float dC = DistPointToSegment(p, c, a);
            return Mathf.Min(dA, Mathf.Min(dB, dC));
        }

        float invDenom = 1f / denom;
        float u = (dot11 * dot02 - dot01 * dot12) * invDenom;
        float v = (dot00 * dot12 - dot01 * dot02) * invDenom;

        if (u >= 0 && v >= 0 && u + v <= 1)
        {
            Vector3 proj = a + u * v0 + v * v1;
            return (p - proj).magnitude;
        }
        else
        {
            float dA = DistPointToSegment(p, a, b);
            float dB = DistPointToSegment(p, b, c);
            float dC = DistPointToSegment(p, c, a);
            return Mathf.Min(dA, Mathf.Min(dB, dC));
        }
    }

    private float DistPointToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float t = Vector3.Dot(p - a, ab) / ab.sqrMagnitude;
        t = Mathf.Clamp01(t);
        Vector3 proj = a + t * ab;
        return (p - proj).magnitude;
    }

    private void DrawRay(Vector3 start, Vector3 end, Color color)
    {
        if (batchmode)
        {
            return;
        }
        else
        {
            GameObject rayLine = new GameObject("Ray");
            rayLine.transform.SetParent(transform);
            LineRenderer lineRenderer = rayLine.AddComponent<LineRenderer>();
            lineRenderer.positionCount = 2;
            lineRenderer.SetPosition(0, start);
            lineRenderer.SetPosition(1, end);
            lineRenderer.startWidth = 0.01f;
            lineRenderer.endWidth = 0.01f;
            lineRenderer.material = new Material(Shader.Find("Unlit/Color")) { color = color };
            lineRenderer.useWorldSpace = true;

            rayLines.Add(rayLine);
        }
    }
}
