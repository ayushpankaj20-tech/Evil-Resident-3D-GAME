using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Adds a ground plane and 5 cubes to the open scene.
// Runs once automatically; can be re-run from Tools > Create Ground And 5 Cubes.
[InitializeOnLoad]
public static class CreateGroundAndCubes
{
    const string MarkerPath = "Library/GroundAndCubesCreated.marker";

    static CreateGroundAndCubes()
    {
        if (File.Exists(MarkerPath)) return;
        EditorApplication.delayCall += RunOnce;
    }

    static void RunOnce()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (File.Exists(MarkerPath)) return;
        File.WriteAllText(MarkerPath, "done");
        if (GameObject.Find("Ground") != null) return;
        Create();
    }

    [MenuItem("Tools/Create Ground And 5 Cubes")]
    public static void Create()
    {
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.position = Vector3.zero;
        ground.transform.localScale = new Vector3(5f, 1f, 5f); // 50 x 50 units
        Undo.RegisterCreatedObjectUndo(ground, "Create Ground");

        var parent = new GameObject("Cubes");
        Undo.RegisterCreatedObjectUndo(parent, "Create Cubes");

        for (int i = 0; i < 5; i++)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Cube " + (i + 1);
            cube.transform.SetParent(parent.transform);
            cube.transform.position = new Vector3((i - 2) * 2.5f, 0.5f, 0f);
            Undo.RegisterCreatedObjectUndo(cube, "Create Cube");
        }

        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!string.IsNullOrEmpty(scene.path))
            EditorSceneManager.SaveScene(scene);

        Debug.Log("Created Ground and 5 cubes in scene '" + scene.name + "'.");
    }
}
