// Command-line APK builder for the Quest target.
// Usage:
//   Unity.exe -batchmode -quit -projectPath <project> -buildTarget Android \
//             -executeMethod QuestCLIBuild.BuildAPK -logFile <log>
//
// Builds every scene enabled in Build Settings, writes an APK into Builds/,
// and exits with 0 on success / 1 on failure so external scripts can react.

using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class QuestCLIBuild
{
    const string OutputDir = "Builds";

    public static void BuildAPK()
    {
        int exitCode = 0;

        try
        {
            string[] scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

            if (scenes.Length == 0)
                throw new Exception("No enabled scenes in Build Settings.");

            Debug.Log($"[QuestCLIBuild] scenes: {string.Join(", ", scenes)}");

            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
                throw new Exception("Could not switch active build target to Android.");

            EditorUserBuildSettings.buildAppBundle = false;

            Directory.CreateDirectory(OutputDir);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmm");
            string outPath = Path.Combine(OutputDir, $"AICardTutor_Quest_{stamp}.apk");

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outPath,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            Debug.Log(
                $"[QuestCLIBuild] result={summary.result} " +
                $"output={summary.outputPath} " +
                $"size={summary.totalSize} bytes " +
                $"errors={summary.totalErrors} warnings={summary.totalWarnings} " +
                $"duration={summary.totalTime}");

            if (summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"[QuestCLIBuild] build did not succeed: {summary.result}");
                exitCode = 1;
            }
        }
        catch (Exception e)
        {
            Debug.LogError("[QuestCLIBuild] FAILED: " + e);
            exitCode = 1;
        }
        finally
        {
            EditorApplication.Exit(exitCode);
        }
    }
}
