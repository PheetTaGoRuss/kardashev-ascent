using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace KardashevAscent.EditorTools
{
    /// <summary>
    /// Kardashev Ascent ▸ Build Windows: a 64-bit Windows player of Main.unity in Builds/Windows (gitignored),
    /// for the demo video and the upload (#15). Also puts Main in the build's scene list.
    /// </summary>
    public static class BuildWindows
    {
        public const string ScenePath = "Assets/Scenes/Main.unity";
        public const string OutputFolder = "Builds/Windows";
        public const string ExeName = "KardashevAscent.exe";
        public const string ProductName = "Kardashev Ascent";

        [MenuItem("Kardashev Ascent/Build Windows")]
        public static void BuildMenu()
        {
            BuildReport report = Build();
            if (report.summary.result == BuildResult.Succeeded)
                EditorUtility.RevealInFinder(Path.Combine(OutputFolder, ExeName));
        }

        /// <summary>Build and return the report; errors are in the console and the report.</summary>
        public static BuildReport Build(bool development = false)
        {
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            if (PlayerSettings.productName != ProductName)
                PlayerSettings.productName = ProductName;

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = Path.Combine(OutputFolder, ExeName),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = development ? BuildOptions.Development : BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary s = report.summary;
            string message = $"Windows build {s.result}: {s.totalErrors} errors, {s.totalWarnings} warnings, " +
                             $"{s.totalSize / (1024f * 1024f):0.0} MB, {s.totalTime.TotalSeconds:0} s → {s.outputPath}";
            if (s.result == BuildResult.Succeeded)
                Debug.Log(message);
            else
                Debug.LogError(message);
            return report;
        }
    }
}
