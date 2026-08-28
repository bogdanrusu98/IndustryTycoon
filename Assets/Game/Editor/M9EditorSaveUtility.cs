using System;
using System.Collections.Generic;
using System.IO;
using IndustryTycoon.Persistence;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace IndustryTycoon.Editor
{
    /// <summary>
    /// Editor-only save reset entry point used by manual QA and by fresh-state smoke tests.
    /// It deliberately targets only the shared M9/M10 primary/temp/backup/corrupt
    /// save artifacts; M10 meta-progression lives inside the same schema-v2 file.
    /// </summary>
    public static class M9EditorSaveUtility
    {
        public const string PrototypeScenePath =
            "Assets/Game/Scenes/Prototype_LumberCamp.unity";

        [MenuItem("Industry Tycoon/Prototype/Reset Save / Fresh Start")]
        private static void ResetFromMenu()
        {
            if (!EditorUtility.DisplayDialog(
                    "Reset Save / Fresh Start",
                    "Delete the local Industry Tycoon save and reload the Lumber Camp "
                    + "at its exact fresh-start state?",
                    "Reset Save",
                    "Cancel"))
            {
                return;
            }

            if (!EditorApplication.isPlaying
                && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            ResetSaveAndReload();
        }

        public static void PrepareFreshSmokeTest()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException(
                    "Fresh smoke-test preparation must run before entering Play Mode.");
            }

            if (!TryDeleteAllSaveArtifacts(out string failureReason))
            {
                throw new IOException(failureReason);
            }
        }

        public static void ResetSaveAndReload()
        {
            if (!TryDeleteAllSaveArtifacts(out string failureReason))
            {
                throw new IOException(failureReason);
            }

            if (EditorApplication.isPlaying)
            {
                LocalPersistenceService service =
                    Object.FindAnyObjectByType<LocalPersistenceService>();
                if (service != null)
                {
                    if (!service.ResetSaveAndReload())
                    {
                        throw new InvalidOperationException(
                            "The active persistence service could not reset the save.");
                    }

                    return;
                }

                Scene activeScene = SceneManager.GetActiveScene();
                if (!activeScene.IsValid() || activeScene.buildIndex < 0)
                {
                    throw new InvalidOperationException(
                        "No reloadable Play Mode scene is active after save reset.");
                }

                SceneManager.LoadScene(activeScene.buildIndex);
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(PrototypeScenePath) == null)
            {
                throw new FileNotFoundException(
                    "The Lumber Camp prototype scene is missing.",
                    PrototypeScenePath);
            }

            EditorSceneManager.OpenScene(PrototypeScenePath, OpenSceneMode.Single);
            Debug.Log(
                "M10 save reset complete. Lumber Camp reloaded at the exact fresh start.");
        }

        public static bool TryDeleteAllSaveArtifacts(out string failureReason)
        {
            failureReason = null;
            string directoryPath;
            try
            {
                directoryPath = Path.GetFullPath(Application.persistentDataPath);
            }
            catch (Exception exception)
            {
                failureReason = $"Could not resolve the persistent-data directory: "
                                + exception.Message;
                return false;
            }

            if (string.IsNullOrWhiteSpace(directoryPath)
                || !Path.IsPathRooted(directoryPath))
            {
                failureReason = "The persistent-data directory is not a safe absolute path.";
                return false;
            }

            try
            {
                if (!Directory.Exists(directoryPath))
                {
                    return true;
                }

                var failures = new List<string>();
                string searchPattern = M9LocalSaveStore.DefaultFileName + "*";
                string[] candidates = Directory.GetFiles(
                    directoryPath,
                    searchPattern,
                    SearchOption.TopDirectoryOnly);
                for (int i = 0; i < candidates.Length; i++)
                {
                    string candidate = Path.GetFullPath(candidates[i]);
                    if (!IsKnownM9SaveArtifact(directoryPath, candidate))
                    {
                        continue;
                    }

                    try
                    {
                        File.Delete(candidate);
                    }
                    catch (Exception exception)
                    {
                        failures.Add($"{Path.GetFileName(candidate)}: {exception.Message}");
                    }
                }

                if (failures.Count == 0)
                {
                    return true;
                }

                failureReason = "Could not remove all M9 save artifacts: "
                                + string.Join(" | ", failures);
                return false;
            }
            catch (Exception exception)
            {
                failureReason = $"Save reset failed: {exception.Message}";
                return false;
            }
        }

        private static bool IsKnownM9SaveArtifact(
            string expectedDirectory,
            string candidatePath)
        {
            string candidateDirectory = Path.GetDirectoryName(candidatePath);
            if (!string.Equals(
                    Path.GetFullPath(candidateDirectory ?? string.Empty)
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    expectedDirectory.TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string fileName = Path.GetFileName(candidatePath);
            string primary = M9LocalSaveStore.DefaultFileName;
            return string.Equals(fileName, primary, StringComparison.Ordinal)
                   || string.Equals(fileName, primary + ".tmp", StringComparison.Ordinal)
                   || string.Equals(fileName, primary + ".bak", StringComparison.Ordinal)
                   || fileName.StartsWith(primary + ".corrupt.", StringComparison.Ordinal)
                   || fileName.StartsWith(primary + ".tmp.corrupt.", StringComparison.Ordinal)
                   || fileName.StartsWith(primary + ".bak.corrupt.", StringComparison.Ordinal);
        }
    }
}
