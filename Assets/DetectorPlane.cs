using UnityEngine;
using System.Collections.Generic;
using System.IO;

[RequireComponent(typeof(MeshRenderer))]
public class DetectorPlane : MonoBehaviour
{
    [Header("Rays Settings")]
    public int numRaysPerAxis = 10;
    public int maxReflections = 5;
    public bool debugDraw = true;

    [Header("Results (Read-Only)")]
    public int raysReturned = 0;
    public int totalRays = 0;

    private List<Vector3> lineVertices = new List<Vector3>();
    private float halfWidth;
    private float halfHeight;

    // Таймер для обновления лучей каждые 0.1 секунды
    private float timeElapsed = 0f;
    private float updateInterval = 0.1f;

    // Для создания картинок
    private Texture2D rayTexture;
    private Color hitColor = Color.green;  // Цвет для точек попадания
    private Color missColor = Color.clear;   // Цвет для пустых точек
    private Color borderColor = Color.red;   // Цвет для границ текстуры
    private Color noiseColor = Color.green;  // Цвет для шума (помех)

    void Start()
    {
        UpdateDetectorSizeFromTransform();
        CreateRayTexture();
        GetComponent<Renderer>().material.mainTexture = rayTexture;
        CastAllRays();  // Первый запуск для инициализации
    }

    void Update()
    {
        timeElapsed += Time.deltaTime;
        if (timeElapsed >= updateInterval)
        {
            CastAllRays();
            timeElapsed = 0f;
        }
    }

    private void UpdateDetectorSizeFromTransform()
    {
        Vector3 scale = transform.localScale;
        halfWidth = scale.x * 0.5f;
        halfHeight = scale.y * 0.5f;
    }

    private void CreateRayTexture()
    {
        // Создаем текстуру с размером numRaysPerAxis x numRaysPerAxis
        rayTexture = new Texture2D(numRaysPerAxis, numRaysPerAxis);

        // Инициализируем все пиксели как прозрачные
        for (int i = 0; i < numRaysPerAxis; i++)
        {
            for (int j = 0; j < numRaysPerAxis; j++)
            {
                rayTexture.SetPixel(i, j, missColor);
            }
        }

        // Добавляем границы текстуры красным цветом
        AddBorders();
        rayTexture.Apply();
    }

    private void AddBorders()
    {
        // Рисуем границы текстуры (первый и последний столбцы и строки)
        for (int i = 0; i < numRaysPerAxis; i++)
        {
            rayTexture.SetPixel(i, 0, borderColor);                  // Верхняя граница
            rayTexture.SetPixel(i, numRaysPerAxis - 1, borderColor);    // Нижняя граница
            rayTexture.SetPixel(0, i, borderColor);                     // Левая граница
            rayTexture.SetPixel(numRaysPerAxis - 1, i, borderColor);     // Правая граница
        }
    }

    public void CastAllRays()
    {
        lineVertices.Clear();
        raysReturned = 0;
        totalRays = numRaysPerAxis * numRaysPerAxis;

        // Очистка старых точек на картинке (без границ)
        for (int i = 1; i < numRaysPerAxis - 1; i++)
        {
            for (int j = 1; j < numRaysPerAxis - 1; j++)
            {
                rayTexture.SetPixel(i, j, missColor);
            }
        }

        for (int i = 0; i < numRaysPerAxis; i++)
        {
            for (int j = 0; j < numRaysPerAxis; j++)
            {
                float u = (float)i / (numRaysPerAxis - 1);
                float v = (float)j / (numRaysPerAxis - 1);

                float localX = Mathf.Lerp(-halfWidth, halfWidth, u) + 47;
                float localY = Mathf.Lerp(-halfHeight, halfHeight, v) + 9;
                Vector3 startPoint = new Vector3(localX, localY, -67.9f);

                Vector3 worldDir = transform.forward;
                CastReflectiveRay(startPoint, worldDir);
            }
        }

        // Отрисовываем лучи с помощью GL
        DrawRaysUsingGL();

        // Добавляем шум (помехи) с вероятностью 5%
        AddNoise(0.05f);

        // Применяем изменения в текстуре
        rayTexture.Apply();
    }

    private void CastReflectiveRay(Vector3 startPos, Vector3 startDir)
    {
        Vector3 currentPos = startPos;
        Vector3 currentDir = startDir.normalized;
        int reflections = 0;

        while (reflections <= maxReflections)
        {
            if (Physics.Raycast(currentPos, currentDir, out RaycastHit hit, 1000f))
            {
                AddLine(currentPos, hit.point);
                currentDir = Vector3.Reflect(currentDir, hit.normal).normalized;
                reflections++;
                currentPos = hit.point + currentDir * 0.001f;
            }
            else
            {
                AddLine(currentPos, currentPos + currentDir * 100f);
                return;
            }

            if (CheckIfRayIntersectsDetector(currentPos, currentDir, out Vector3 intersection))
            {
                AddLine(currentPos, intersection);
                raysReturned++;

                // Преобразуем точку пересечения (world space) в локальные координаты детектора
                Vector3 localHit = transform.InverseTransformPoint(intersection);

                // Получаем локальные границы (bounds) меша детектора
                MeshFilter mf = GetComponent<MeshFilter>();
                if (mf == null)
                {
                    Debug.LogWarning("MeshFilter не найден!");
                    return;
                }
                Bounds bounds = mf.mesh.bounds;

                // Вычисляем UV-координаты по реальным границам:
                float u = (localHit.x - bounds.min.x) / bounds.size.x;
                float v = (localHit.y - bounds.min.y) / bounds.size.y;

                // Преобразуем UV в координаты пикселей текстуры
                int pixelX = Mathf.Clamp(Mathf.RoundToInt(u * (rayTexture.width - 1)), 0, rayTexture.width - 1);
                int pixelY = Mathf.Clamp(Mathf.RoundToInt(v * (rayTexture.height - 1)), 0, rayTexture.height - 1);

                rayTexture.SetPixel(pixelX, pixelY, hitColor);
                return;
            }
        }
    }

    private bool CheckIfRayIntersectsDetector(Vector3 origin, Vector3 dir, out Vector3 intersectionPoint)
    {
        intersectionPoint = Vector3.zero;
        Vector3 planeOrigin = transform.position;
        Vector3 planeNormal = transform.forward;

        float denom = Vector3.Dot(planeNormal, dir);
        if (Mathf.Abs(denom) < 1e-6f)
            return false;

        float t = Vector3.Dot(planeOrigin - origin, planeNormal) / denom;
        if (t < 0f)
            return false;

        Vector3 hitPoint = origin + dir * t;
        Vector3 localHit = transform.InverseTransformPoint(hitPoint);

        if (Mathf.Abs(localHit.x) <= halfWidth &&
            Mathf.Abs(localHit.y) <= halfHeight &&
            Mathf.Abs(localHit.z) < 0.01f)
        {
            intersectionPoint = hitPoint;
            return true;
        }
        return false;
    }

    private void AddLine(Vector3 start, Vector3 end)
    {
        lineVertices.Add(start);
        lineVertices.Add(end);
    }

    // Метод для добавления шума (помех) с заданной вероятностью для каждого пикселя
    private void AddNoise(float noiseProbability)
    {
        // Проходим по пикселям внутри границ (без крайних)
        for (int i = 1; i < numRaysPerAxis - 1; i++)
        {
            for (int j = 1; j < numRaysPerAxis - 1; j++)
            {
                if (Random.value < noiseProbability)
                {
                    rayTexture.SetPixel(i, j, noiseColor);
                }
            }
        }
    }

    // Отрисовка линий с помощью GL
    void DrawRaysUsingGL()
    {
        if (!debugDraw) return;
        if (lineVertices.Count == 0) return;

        Material mat = new Material(Shader.Find("Hidden/Internal-Colored"));
        mat.SetPass(0);

        GL.PushMatrix();
        GL.Begin(GL.LINES);
        GL.Color(Color.green);

        for (int i = 0; i < lineVertices.Count; i += 2)
        {
            GL.Vertex(lineVertices[i]);
            GL.Vertex(lineVertices[i + 1]);
        }

        GL.End();
        GL.PopMatrix();
    }

    // Отображение текстуры на экране
    void OnGUI()
    {
        if (rayTexture != null)
        {
            GUI.DrawTexture(new Rect(10, 10, 200, 200), rayTexture);
        }
    }

    // Метод для сохранения изображения в файл
    public void SaveTextureToFile(string path)
    {
        byte[] bytes = rayTexture.EncodeToPNG();
        File.WriteAllBytes(path, bytes);
        Debug.Log("Texture saved to " + path);
    }
}
