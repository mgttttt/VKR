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
        // 1) ��������� �����
        EditorSceneManager.OpenScene(scenePath);

        // 2) ������� ��� ������ TetrahedronWithSpikes3D
        var spikesGO = GameObject.Find(spikesObjectName);
        if (spikesGO == null)
        {
            Debug.LogError($"�� ������ GameObject '{spikesObjectName}' � �����.");
            EditorApplication.Exit(1);
            return;
        }
        var spikesScript = spikesGO.GetComponent<TetrahedronWithSpikes3D>();
        if (spikesScript == null)
        {
            Debug.LogError($"�� ������� '{spikesObjectName}' �� ������ ��������� TetrahedronWithSpikes3D.");
            EditorApplication.Exit(1);
            return;
        }

        // �������� ���� ��������
        spikesScript.collectDataset = true;

        // ���� ����� ��������� ������
        var customPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(customPrefabPath);
        if (customPrefab == null)
        {
            Debug.LogWarning("customPrefab �� ������. ���� objectType=Custom, �� ����� fallback to Cube.");
        }
        else
        {
            spikesScript.customObjectPrefab = customPrefab;
        }

        // �����������, ��� �� ������� ������������ objectType
        // (��� ����� ������� ������ � ���� ���������)
        spikesScript.currentObjectType = ObjectType.Tetrahedron;
        spikesScript.batchmode = true;

        // configId: 1..3
        int[] configIds = { 1, 2, 3 };

        // featureSize �� 0.1 �� 0.3 (��� 0.05)
        float[] featureSizes = { 0.1f, 0.15f, 0.2f, 0.25f, 0.3f };

        // minFeatureDistance �� 0.1 �� 0.4 (��� 0.1)
        float[] minDists = { 0.1f, 0.2f, 0.3f, 0.4f };

        // ��� 5 ��������
        int angleStep = 5;

        // ���� ��� ������ baseObject (����� ����� ������� � �������������, ���� �����).
        // � ����������� �� ����� ������ � ����� �� ������
        // ������ ��� ������� ��������� ������.
        bool first = true;
        // ���������� ���������
        foreach (int cfg in configIds)
        {
            foreach (float fs in featureSizes)
            {
                foreach (float md in minDists)
                {
                    // ������� ������ ���� ����� ����� ����������
                    if (!first)
                    {
                        spikesScript.ClearExistingFeatures();
                    }
                    
                   
                    // ����������� ����
                    spikesScript.configId = cfg;
                    spikesScript.featureSize = fs;
                    spikesScript.minFeatureDistance = md;

                    // ���������� ����������� (����/���������)
                    spikesScript.InitializeAll();

                    // ���������� ���� (��� ��������� ����� ������ �������� � �������)
                    for (int ax = 0; ax < 360; ax += angleStep)
                    {
                        for (int ay = 0; ay < 360; ay += angleStep)
                        {
                            for (int az = 0; az < 360; az += angleStep)
                            {
                                spikesGO.transform.localEulerAngles = new Vector3(ax, ay, az);
                                Physics.SyncTransforms();

                                // ��������� CastRays � ����� � CSV
                                spikesScript.CastRays();
                                spikesScript.RecordCurrentState();
                            }
                        }
                    }
                    first = false;
                    // �� ������� � �������� baseObject, ���� ���������� � ����
                    // spikesScript.ClearBaseObject();
                    // spikesScript.CreateObjectByType();
                }
            }
        }

        // ��������� Editor
        EditorApplication.Exit(0);
    }
}
#endif
