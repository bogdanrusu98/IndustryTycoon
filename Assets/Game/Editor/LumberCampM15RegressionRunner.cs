using System;
using UnityEditor;
using UnityEngine;

namespace IndustryTycoon.Editor
{
    public static class LumberCampM15RegressionRunner
    {
        public static void RunFromCommandLine()
        {
            try
            {
                LumberCampM9PersistenceTests.RunAllForValidator();
                LumberCampM10ProgressionTests.RunAllForValidator();
                LumberCampM11Tests.RunAllForValidator();
                LumberCampM12Tests.RunAllForValidator();
                LumberCampM13Tests.RunAllForValidator();
                LumberCampM14Tests.RunAllForValidator();
                Debug.Log("M9-M15 deterministic regression PASS.");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }
    }
}
