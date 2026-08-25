using IndustryTycoon.Progression;
using UnityEditor;
using UnityEngine;

namespace IndustryTycoon.Editor
{
    public static class LumberCampPacingProbeMenu
    {
        [MenuItem("Industry Tycoon/Prototype/Pacing/Reset Fresh Session Probe")]
        private static void ResetProbe()
        {
            LumberCampPacingProbe probe = FindActiveProbe();
            if (probe == null)
            {
                Debug.LogWarning(
                    "Enter Play Mode in the Lumber Camp scene before resetting the pacing probe.");
                return;
            }

            probe.ResetProbe();
            Selection.activeObject = probe.gameObject;
            Debug.Log("M8 pacing probe reset at 0:00 for a fresh-session measurement.", probe);
        }

        [MenuItem("Industry Tycoon/Prototype/Pacing/Log Current Report")]
        private static void LogReport()
        {
            LumberCampPacingProbe probe = FindActiveProbe();
            if (probe == null)
            {
                Debug.LogWarning(
                    "Enter Play Mode in the Lumber Camp scene before logging a pacing report.");
                return;
            }

            probe.LogReport();
            Selection.activeObject = probe.gameObject;
        }

        private static LumberCampPacingProbe FindActiveProbe()
        {
            return EditorApplication.isPlaying
                ? Object.FindAnyObjectByType<LumberCampPacingProbe>()
                : null;
        }
    }
}
