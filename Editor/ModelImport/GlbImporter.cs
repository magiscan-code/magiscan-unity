using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Magiscan.Editor.ModelImport
{
    /// <summary>
    /// Downloads a scan's <c>glb</c> model into the project and adds it to the open scene. The model
    /// becomes a normal project asset, imported by glTFast's scripted importer.
    /// </summary>
    public static class GlbImporter
    {
        /// <summary>Project-relative folder where imported models are written.</summary>
        public const string ModelsFolder = "Assets/Magiscan/Models";

        /// <summary>True when the glTFast package is present (required to import glb).</summary>
        public static bool IsGltfastInstalled =>
#if MAGISCAN_GLTFAST
            true;
#else
            false;
#endif

        /// <summary>
        /// Downloads the model for <paramref name="task"/>, imports it, and instantiates it into the
        /// active scene. Returns the created instance. Must be called on the main thread.
        /// </summary>
        public static async Task<GameObject> ImportIntoSceneAsync(
            MagiscanClient client,
            MagiscanTask task,
            MagiscanModel model,
            IProgress<float> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            if (task == null) throw new ArgumentNullException(nameof(task));
            if (model == null || string.IsNullOrEmpty(model.Url))
                throw new ArgumentException("Task has no downloadable model.", nameof(model));

            byte[] bytes = await client.DownloadAsync(model.Url, progress, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            EnsureFolder(ModelsFolder);
            string assetPath = $"{ModelsFolder}/{BuildFileName(task)}";
            string fullPath = Path.Combine(Directory.GetCurrentDirectory(), assetPath);
            File.WriteAllBytes(fullPath, bytes);

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null)
            {
                throw new MagiscanException(
                    "The model was downloaded but Unity did not import it as a GameObject. " +
                    "Make sure the glTFast package (com.unity.cloud.gltfast) is installed.");
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (!string.IsNullOrEmpty(task.Name))
                instance.name = task.Name;

            Undo.RegisterCreatedObjectUndo(instance, "Import Magiscan Model");
            Selection.activeGameObject = instance;
            EditorGUIUtility.PingObject(instance);
            return instance;
        }

        static string BuildFileName(MagiscanTask task)
        {
            string name = Sanitize(task.Name);
            string idPart = task.Id != null && task.Id.Length > 8 ? task.Id.Substring(0, 8) : task.Id ?? "scan";
            if (string.IsNullOrEmpty(name))
                return $"magiscan_{idPart}.glb";
            return $"{name}_{idPart}.glb";
        }

        static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            var sb = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                if (char.IsLetterOrDigit(c)) sb.Append(c);
                else if (c == ' ' || c == '-' || c == '_') sb.Append('_');
            }
            return sb.Length == 0 ? null : sb.ToString();
        }

        static void EnsureFolder(string projectRelativePath)
        {
            if (AssetDatabase.IsValidFolder(projectRelativePath))
                return;

            string[] parts = projectRelativePath.Split('/');
            string current = parts[0]; // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
