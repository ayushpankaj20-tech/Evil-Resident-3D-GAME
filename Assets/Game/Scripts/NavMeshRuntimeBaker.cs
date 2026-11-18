using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Builds the walkable area from the scene colliders when the level starts, so enemies can path through it.
[DefaultExecutionOrder(-500)]
public class NavMeshRuntimeBaker : MonoBehaviour
{
    public Vector3 center = new Vector3(-13f, 5f, -5f);
    public Vector3 size = new Vector3(104f, 40f, 156f);
    public LayerMask mask = ~0;
    public float voxelSize = 0.25f;

    NavMeshDataInstance instance;

    void Awake()
    {
        var bounds = new Bounds(center, size);
        var sources = new List<NavMeshBuildSource>();
        NavMeshBuilder.CollectSources(bounds, mask, NavMeshCollectGeometry.PhysicsColliders, 0, new List<NavMeshBuildMarkup>(), sources);
        var settings = NavMesh.GetSettingsByID(0);
        settings.overrideVoxelSize = true;
        settings.voxelSize = voxelSize;
        var data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
        if (data != null) instance = NavMesh.AddNavMeshData(data);
        else Debug.LogError("NavMesh build failed");
    }

    void OnDestroy() { instance.Remove(); }
}
