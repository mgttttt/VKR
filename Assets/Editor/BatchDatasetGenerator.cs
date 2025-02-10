#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections;
using System.Collections.Generic;

public static class BatchDatasetGenerator
{
    private static string scenePath = "Assets/Scenes/SampleScene.unity";
    private static string spikesObjectName = "Controller";
    private static string customPrefabPath = "Assets/Prefabs/MyCustomObject.prefab";

    public static void GenerateDataset()
    {
        // 1) Загружаем сцену
        EditorSceneManager.OpenScene(scenePath);

        // 2) Находим ваш скрипт TetrahedronWithSpikes3D
        var spikesGO = GameObject.Find(spikesObjectName);
        if (spikesGO == null)
        {
            Debug.LogError($"Не найден GameObject '{spikesObjectName}' в сцене.");
            EditorApplication.Exit(1);
            return;
        }
        var spikesScript = spikesGO.GetComponent<TetrahedronWithSpikes3D>();
        if (spikesScript == null)
        {
            Debug.LogError($"На объекте '{spikesObjectName}' не найден компонент TetrahedronWithSpikes3D.");
            EditorApplication.Exit(1);
            return;
        }

        // Включаем сбор датасета
        spikesScript.collectDataset = true;

        // Если нужен кастомный префаб
        var customPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(customPrefabPath);
        if (customPrefab == null)
        {
            Debug.LogWarning("customPrefab не найден. Если objectType=Custom, то будет fallback to Cube.");
        }
        else
        {
            spikesScript.customObjectPrefab = customPrefab;
        }

        // Предположим, что мы выбрали единственный objectType
        // (или можно сделать массив и тоже перебрать)
        spikesScript.currentObjectType = ObjectType.Tetrahedron;
        spikesScript.batchmode = true;

        // configId: 1..3
        int[] configIds = { 1, 2, 3 };

        // featureSize от 0.1 до 0.3 (шаг 0.05)
        float[] featureSizes = { 0.1f, 0.15f, 0.2f, 0.25f, 0.3f };

        // minFeatureDistance от 0.1 до 0.4 (шаг 0.1)
        float[] minDists = { 0.1f, 0.2f, 0.3f, 0.4f };

        // Шаг 5 градусов
        int angleStep = 5;

        // Один раз создаём baseObject (затем будем чистить и пересоздавать, если нужно).
        // В зависимости от вашей логики — можно всё делать
        // внутри или снаружи вложенных циклов.
        bool first = true;
        // Перебираем параметры
        foreach (int cfg in configIds)
        {
            foreach (float fs in featureSizes)
            {
                foreach (float md in minDists)
                {
                    // Очищаем старые фичи перед новой генерацией
                    if (!first)
                    {
                        spikesScript.ClearExistingFeatures();
                    }
                    
                   
                    // Настраиваем поля
                    spikesScript.configId = cfg;
                    spikesScript.featureSize = fs;
                    spikesScript.minFeatureDistance = md;

                    // Генерируем поверхность (шипы/полусферы)
                    spikesScript.InitializeAll();

                    // Перебираем углы (три вложенных цикла вместо хранения в массиве)
                    for (int ax = 0; ax < 360; ax += angleStep)
                    {
                        for (int ay = 0; ay < 360; ay += angleStep)
                        {
                            for (int az = 0; az < 360; az += angleStep)
                            {
                                spikesGO.transform.localEulerAngles = new Vector3(ax, ay, az);
                                Physics.SyncTransforms();

                                // Прогоняем CastRays и пишем в CSV
                                spikesScript.CastRays();
                                spikesScript.RecordCurrentState();
                            }
                        }
                    }
                    first = false;
                    // По желанию — очистить baseObject, если необходимо с нуля
                    // spikesScript.ClearBaseObject();
                    // spikesScript.CreateObjectByType();
                }
            }
        }

        // Завершаем Editor
        EditorApplication.Exit(0);
    }
}
#endif
