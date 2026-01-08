using UnityEngine;

namespace TowerDefense
{
    /// <summary>
    /// Ensures the tower defense runtime is available even when the sample scene is unchanged.
    /// </summary>
    public static class GameBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            if (Object.FindObjectOfType<GameController>() != null)
            {
                return;
            }

            var root = new GameObject("TowerDefenseRoot");
            root.AddComponent<GameController>();
        }
    }
}
