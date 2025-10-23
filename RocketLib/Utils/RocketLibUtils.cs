using System.IO;
using System.Reflection;
using UnityEngine;

namespace RocketLib.Utils
{
    public static class RocketLibUtils
    {
        public static void MakeObjectUnpausable(string gameObjectName)
        {
            MakeObjectUnpausable(GameObject.Find(gameObjectName));
        }

        public static void MakeObjectUnpausable(GameObject gameObject)
        {
            if (gameObject != null)
            {
                gameObject.tag = "Unpausable";
            }
        }

        internal static string rootDirectoryPath = string.Empty;

        public static string GetRootDirectory()
        {
            if (rootDirectoryPath != string.Empty)
            {
                return rootDirectoryPath;
            }
            string assemblyPath = Assembly.GetExecutingAssembly().Location;
            DirectoryInfo dir = Directory.GetParent(assemblyPath);

            // Find mods directory and use parent as root
            if (dir.Parent.Name == "Mods")
            {
                rootDirectoryPath = dir.Parent.Parent.FullName;
            }
            else if (dir.Parent.Parent.Name == "Mods")
            {
                rootDirectoryPath = dir.Parent.Parent.Parent.FullName;
            }

            return rootDirectoryPath;
        }
    }
}
