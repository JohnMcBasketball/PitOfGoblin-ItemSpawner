using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using Unity.Netcode;
using Il2Cpp;
using Il2CppEntities;
using Il2CppFriendlyFoe.Debug;

[assembly: MelonInfo(typeof(PitOfGoblinSpawner.SpawnerMod), "POG Item Spawner", "1.0.0", "YourName")]
[assembly: MelonGame(null, null)]

namespace PitOfGoblinSpawner
{
    public class SpawnerMod : MelonMod
    {
        private bool showMenu = false;
        private string searchQuery = "";
        private Vector2 scrollPos = Vector2.zero;

        private SpawnerPickups pickupSpawner = null;
        private SpawnerDatabase database = null;

        private class SpawnableEntry
        {
            public string Name;
            public int DbIndex = -1;
            public GameObject Prefab = null;
        }

        private List<SpawnableEntry> itemList = new List<SpawnableEntry>();

        public override void OnUpdate()
        {
            if (Input.GetKeyDown(KeyCode.F5))
            {
                showMenu = !showMenu;
                if (showMenu)
                {
                    RefreshItems();
                }
            }

            if (showMenu)
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;

                foreach (char c in Input.inputString)
                {
                    if (c == '\b')
                    {
                        if (searchQuery.Length > 0)
                            searchQuery = searchQuery.Substring(0, searchQuery.Length - 1);
                    }
                    else if (c != '\n' && c != '\r')
                    {
                        searchQuery += c;
                    }
                }
            }
        }

        private void RefreshItems()
        {
            itemList.Clear();

            // 1. Try loading from the game's SpawnerDatabase first
            if (database == null)
            {
                var dbs = Resources.FindObjectsOfTypeAll<SpawnerDatabase>();
                if (dbs != null && dbs.Length > 0) database = dbs[0];
            }

            if (pickupSpawner == null)
            {
                var existing = Resources.FindObjectsOfTypeAll<SpawnerPickups>();
                if (existing != null && existing.Length > 0)
                {
                    pickupSpawner = existing[0];
                }
                else
                {
                    var go = new GameObject("CustomPOGSpawner");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    pickupSpawner = go.AddComponent<SpawnerPickups>();
                    if (database != null) pickupSpawner.m_database = database;
                }
            }

            if (database != null && database.m_pickups != null && database.m_pickups.Count > 0)
            {
                for (int i = 0; i < database.m_pickups.Count; i++)
                {
                    var assetInfo = database.m_pickups[i];
                    if (assetInfo == null) continue;

                    string title = pickupSpawner.GetCellTitle(assetInfo);
                    if (string.IsNullOrEmpty(title)) title = $"Item #{i}";
                    itemList.Add(new SpawnableEntry { Name = title, DbIndex = i });
                }
                MelonLogger.Msg($"Loaded {itemList.Count} items from SpawnerDatabase.");
                return;
            }

            // 2. Fallback: Scan Unity Netcode's registered NetworkPrefabs for all InventoryItems
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.NetworkConfig != null)
            {
                var netPrefabs = NetworkManager.Singleton.NetworkConfig.Prefabs.Prefabs;
                int count = netPrefabs.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<NetworkPrefab>>().Count;
                for (int i = 0; i < count; i++)
                {
                    var netPrefab = netPrefabs[i];
                    if (netPrefab == null || netPrefab.Prefab == null) continue;

                    var invItem = netPrefab.Prefab.GetComponent<InventoryItem>();
                    if (invItem != null)
                    {
                        itemList.Add(new SpawnableEntry
                        {
                            Name = netPrefab.Prefab.name,
                            Prefab = netPrefab.Prefab
                        });
                    }
                }
                MelonLogger.Msg($"Loaded {itemList.Count} items from NetworkManager prefabs.");
            }
            else
            {
                MelonLogger.Warning("NetworkManager not ready yet! Load into a match first.");
            }
        }

        private void SpawnEntry(SpawnableEntry entry, bool onPlayer)
        {
            if (entry.DbIndex >= 0 && pickupSpawner != null)
            {
                if (onPlayer) pickupSpawner.OnSpawnOnPlayer(entry.DbIndex);
                else pickupSpawner.OnSpawnInWorld(entry.DbIndex);
                return;
            }

            if (entry.Prefab != null)
            {
                if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
                {
                    MelonLogger.Warning("You must be the host/server to spawn networked items!");
                    return;
                }

                // Find local player position
                Vector3 spawnPos = Vector3.zero;
                var services = Resources.FindObjectsOfTypeAll<SpawnedObjectsService>();
                if (services != null && services.Length > 0 && services[0].LocalPlayer != null)
                {
                    Transform t = services[0].LocalPlayer.transform;
                    spawnPos = onPlayer ? t.position + Vector3.up * 1.2f : t.position + t.forward * 1.5f + Vector3.up * 0.5f;
                }
                else if (Camera.main != null)
                {
                    Transform cam = Camera.main.transform;
                    spawnPos = cam.position + cam.forward * 2.0f;
                }

                GameObject spawned = UnityEngine.Object.Instantiate(entry.Prefab, spawnPos, Quaternion.identity);
                var netObj = spawned.GetComponent<NetworkObject>();
                if (netObj != null)
                {
                    netObj.Spawn(true);
                    MelonLogger.Msg($"Spawned {entry.Name} at {spawnPos}");
                }
            }
        }

        public override void OnGUI()
        {
            if (!showMenu) return;

            GUI.Box(new Rect(20, 20, 360, 520), "Pit of Goblin - Item Spawner (F5)");

            GUI.Label(new Rect(35, 50, 60, 25), "Search:");
            GUI.Box(new Rect(95, 50, 205, 25), "");
            GUI.Label(new Rect(100, 52, 195, 22), string.IsNullOrEmpty(searchQuery) ? "<Type to filter...>" : searchQuery);

            if (GUI.Button(new Rect(305, 50, 60, 25), "Clear"))
            {
                searchQuery = "";
            }

            if (itemList.Count == 0)
            {
                if (GUI.Button(new Rect(35, 90, 330, 30), "Reload Items"))
                {
                    RefreshItems();
                }
                GUI.Label(new Rect(35, 130, 330, 40), "No items loaded yet. Enter a match and click Reload.");
                return;
            }

            List<SpawnableEntry> filtered = new List<SpawnableEntry>();
            foreach (var item in itemList)
            {
                if (string.IsNullOrEmpty(searchQuery) ||
                    item.Name.IndexOf(searchQuery, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    filtered.Add(item);
                }
            }

            scrollPos = GUI.BeginScrollView(
                new Rect(30, 85, 340, 440),
                scrollPos,
                new Rect(0, 0, 315, filtered.Count * 32)
            );

            int y = 0;
            foreach (var item in filtered)
            {
                GUI.Label(new Rect(5, y + 4, 185, 25), item.Name);

                if (GUI.Button(new Rect(190, y, 60, 26), "World"))
                {
                    SpawnEntry(item, false);
                }

                if (GUI.Button(new Rect(255, y, 60, 26), "Player"))
                {
                    SpawnEntry(item, true);
                }

                y += 32;
            }

            GUI.EndScrollView();
        }
    }
}
