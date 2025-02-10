using UnityEngine;
using System.Collections.Generic;

public class VirusManager : MonoBehaviour
{
    [Header("Virus Settings")]
    public int virusCount = 5;

    [Tooltip("Размер базовой фигуры (радиус сферы, высота тетраэдра и т.п.)")]
    public float baseObjectSize = 2f;

    [Tooltip("Размер (относительный) шипов")]
    public float featureSize = 0.2f;

    [Tooltip("Минимальное расстояние между шипами (для Poisson Disk Sampling)")]
    public float minFeatureDistance = 0.5f;

    [Header("Movement Settings")]
    [Tooltip("Границы области (куб) в которой двигаются объекты (центр — текущий transform.position).")]
    public Vector3 areaSize = new Vector3(10, 10, 10);

    [Tooltip("Максимальная скорость движения")]
    public float maxMoveSpeed = 2f;

    [Tooltip("Максимальная угловая скорость (град/сек)")]
    public float maxAngularSpeed = 60f;

    [Header("Shape Selection")]
    public ObjectType virusShape = ObjectType.Tetrahedron;
    // Используем enum из TetrahedronWithSpikes3D: Tetrahedron, Cube, Sphere, ...

    private List<GameObject> spawnedViruses = new List<GameObject>();
    private List<Vector3> velocities = new List<Vector3>();
    private List<Vector3> angularVelocities = new List<Vector3>();

    void Start()
    {
        SpawnViruses();
    }

    /// <summary>
    /// Основной метод спауна.
    /// </summary>
    private void SpawnViruses()
    {
        ClearAllViruses();

        for (int i = 0; i < virusCount; i++)
        {
            // Пустой объект для «Вируса»
            GameObject virusGO = new GameObject($"Virus_{i}");
            virusGO.transform.SetParent(this.transform);

            // Добавляем старый скрипт TetrahedronWithSpikes3D (но уже "урезанный" или настроенный)
            TetrahedronWithSpikes3D virusScript = virusGO.AddComponent<TetrahedronWithSpikes3D>();

            // Отключим ненужную функциональность
            virusScript.collectDataset = false;    // не собираем датасет
            virusScript.batchmode = true;         // чтобы не рисовались лучи и не было UI
            // Или, если вы добавили отдельные флаги типа enableRaycasting:
            // virusScript.enableRaycasting = false;
            // virusScript.enableDataset = false;

            // Установим нужные параметры для шипов
            virusScript.currentObjectType = virusShape;
            virusScript.baseObjectSize = baseObjectSize;
            virusScript.featureSize = featureSize;
            virusScript.minFeatureDistance = minFeatureDistance;

            // Говорим, что хотим только шипы:
            // (во «взятом» скрипте configId=2 означает только шипы)
            virusScript.configId = 2;

            // Инициализируем (создаём базовую форму, генерируем шипы)
            // Важно, что InitializeAll() внутри вызывает CreateObjectByType() и GenerateSurfaceFeatures(...)
            virusScript.InitializeAll();

            // — Случайная позиция внутри области:
            Vector3 pos = GetRandomPositionInArea();
            virusGO.transform.position = pos;

            // Случайная ориентация
            virusGO.transform.rotation = Random.rotation;

            // Случайные скорости
            Vector3 velocity = new Vector3(
                Random.Range(-maxMoveSpeed, maxMoveSpeed),
                Random.Range(-maxMoveSpeed, maxMoveSpeed),
                Random.Range(-maxMoveSpeed, maxMoveSpeed)
            );
            velocities.Add(velocity);

            Vector3 aVel = new Vector3(
                Random.Range(-maxAngularSpeed, maxAngularSpeed),
                Random.Range(-maxAngularSpeed, maxAngularSpeed),
                Random.Range(-maxAngularSpeed, maxAngularSpeed)
            );
            angularVelocities.Add(aVel);

            spawnedViruses.Add(virusGO);
        }
    }

    /// <summary>
    /// Генерирует случайную позицию внутри куба areaSize (центр в this.transform.position).
    /// </summary>
    private Vector3 GetRandomPositionInArea()
    {
        float x = Random.Range(-areaSize.x / 2f, areaSize.x / 2f);
        float y = Random.Range(-areaSize.y / 2f, areaSize.y / 2f);
        float z = Random.Range(-areaSize.z / 2f, areaSize.z / 2f);
        return this.transform.position + new Vector3(x, y, z);
    }

    /// <summary>
    /// Удаляем ранее заспавненные объекты.
    /// </summary>
    private void ClearAllViruses()
    {
        foreach (var v in spawnedViruses)
        {
            if (v != null)
            {
                Destroy(v);
            }
        }
        spawnedViruses.Clear();
        velocities.Clear();
        angularVelocities.Clear();
    }

    void Update()
    {
        float dt = Time.deltaTime;

        for (int i = 0; i < spawnedViruses.Count; i++)
        {
            GameObject virus = spawnedViruses[i];
            if (virus == null) continue;

            // Линейное движение
            Vector3 vel = velocities[i];
            Vector3 newPos = virus.transform.position + vel * dt;

            // Проверка на выход за границы и «отражение»
            Vector3 center = this.transform.position;

            if (Mathf.Abs(newPos.x - center.x) > areaSize.x / 2f) vel.x *= -1f;
            if (Mathf.Abs(newPos.y - center.y) > areaSize.y / 2f) vel.y *= -1f;
            if (Mathf.Abs(newPos.z - center.z) > areaSize.z / 2f) vel.z *= -1f;

            velocities[i] = vel;
            newPos = virus.transform.position + vel * dt;
            virus.transform.position = newPos;

            // Вращение
            Vector3 aVel = angularVelocities[i];
            virus.transform.Rotate(aVel * dt, Space.Self);
        }
    }
}
