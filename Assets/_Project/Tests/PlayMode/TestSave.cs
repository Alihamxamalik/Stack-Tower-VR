using System.IO;
using UnityEngine;

namespace StackTower.Tests
{
    /// <summary>Keeps test runs away from the real save file.</summary>
    internal static class TestSave
    {
        private static string Path => System.IO.Path.Combine(Application.temporaryCachePath, "stacktower-test-save.json");

        public static void Begin()
        {
            Delete();
            SaveService.UseFile(Path);
        }

        public static void End()
        {
            SaveService.UseFile(null);
            Delete();
        }

        private static void Delete()
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
    }
}
