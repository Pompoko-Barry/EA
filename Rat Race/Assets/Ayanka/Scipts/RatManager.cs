using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Rat Manager.
/// Spawns one rat per Twitch chatter, keeps track of every rat in the race,
/// gives each rat its own lane, and (optionally) removes rats whose owner
/// has stopped chatting.
///
/// Rat_Input asks this script for rats. Other scripts (finish line,
/// leaderboard, camera) can use it to find rats too.
/// </summary>
public class RatManager : MonoBehaviour
{
    [Header("Spawning")]
    [Tooltip("The rat prefab. Must have RatController on its root object.")]
    [SerializeField] RatController ratPrefab;
    [Tooltip("Where lane 0 is. Each next lane is placed below it.")]
    [SerializeField] Transform spawnPoint;
    [SerializeField] float laneSpacing = 1.0f;
    [Tooltip("How many lanes fit on the track. When all are full, rats share lanes.")]
    [SerializeField] int maxLanes = 10;

    [Header("Limits")]
    [Tooltip("Most rats allowed in the race at once. 0 = no limit.")]
    [SerializeField] int maxRats = 0;
    [Tooltip("Remove a rat if its owner hasn't sent a command for this many seconds. 0 = never.")]
    [SerializeField] float removeIdleAfter = 0f;

    [Header("Joining")]
    [Tooltip("If on, viewers must type a join word before they get a rat. If off, any command spawns a rat.")]
    [SerializeField] bool requireJoinCommand = false;

    [Header("Events")]
    public UnityEvent<RatController> OnRatSpawned;
    public UnityEvent<RatController> OnRatRemoved;

    class RatEntry
    {
        public RatController rat;
        public int lane;
        public float lastActiveTime;
    }

    // The phonebook: username -> their rat
    readonly Dictionary<string, RatEntry> rats = new Dictionary<string, RatEntry>();
    readonly List<string> toRemove = new List<string>();

    public bool RequireJoinCommand => requireJoinCommand;
    public int RatCount => rats.Count;

    /// All rats currently in the race.
    public IEnumerable<RatController> AllRats
    {
        get
        {
            foreach (RatEntry e in rats.Values)
                if (e.rat != null) yield return e.rat;
        }
    }

    void Update()
    {
        CleanUp();
    }

    // ---------------------------------------------------------------------
    // Getting / spawning rats
    // ---------------------------------------------------------------------

    /// Returns this viewer's rat, or null if they don't have one.
    public RatController GetRat(string username)
    {
        if (string.IsNullOrEmpty(username)) return null;
        if (rats.TryGetValue(username.ToLower(), out RatEntry e) && e.rat != null)
            return e.rat;
        return null;
    }

    /// Returns this viewer's rat, making a new one if needed.
    /// isNew is true if a rat was just spawned. Returns null if the race is full.
    public RatController GetOrSpawnRat(string username, out bool isNew)
    {
        isNew = false;
        username = username.ToLower();

        RatController existing = GetRat(username);
        if (existing != null)
        {
            MarkActive(username);
            return existing;
        }

        rats.Remove(username); // clear out a destroyed rat, if any

        if (ratPrefab == null)
        {
            Debug.LogError("[RatManager] No Rat Prefab assigned in the Inspector.");
            return null;
        }

        if (maxRats > 0 && rats.Count >= maxRats)
            return null; // race is full

        int lane = FindFreeLane();
        Vector3 origin = spawnPoint != null ? spawnPoint.position : transform.position;
        Vector3 pos = origin + Vector3.down * laneSpacing * lane;

        RatController rat = Instantiate(ratPrefab, pos, Quaternion.identity);
        rat.Init(username);

        rats[username] = new RatEntry { rat = rat, lane = lane, lastActiveTime = Time.time };
        isNew = true;
        OnRatSpawned?.Invoke(rat);
        return rat;
    }

    /// Resets this viewer's idle timer.
    public void MarkActive(string username)
    {
        if (rats.TryGetValue(username.ToLower(), out RatEntry e))
            e.lastActiveTime = Time.time;
    }

    /// Removes one viewer's rat from the race.
    public void RemoveRat(string username)
    {
        username = username.ToLower();
        if (!rats.TryGetValue(username, out RatEntry e)) return;

        rats.Remove(username);
        if (e.rat != null)
        {
            OnRatRemoved?.Invoke(e.rat);
            Destroy(e.rat.gameObject);
        }
    }

    /// Removes every rat (e.g. to start a new race).
    public void ClearAllRats()
    {
        foreach (string name in new List<string>(rats.Keys))
            RemoveRat(name);
    }

    // ---------------------------------------------------------------------

    // Picks the emptiest lane, so rats spread out and reuse lanes freed by
    // rats that left. When every lane has a rat, lanes get shared evenly.
    int FindFreeLane()
    {
        int lanes = Mathf.Max(1, maxLanes);
        int[] count = new int[lanes];
        foreach (RatEntry e in rats.Values)
            if (e.rat != null && e.lane < lanes) count[e.lane]++;

        int best = 0;
        for (int i = 1; i < lanes; i++)
            if (count[i] < count[best]) best = i;
        return best;
    }

    void CleanUp()
    {
        toRemove.Clear();
        foreach (KeyValuePair<string, RatEntry> kv in rats)
        {
            bool destroyed = kv.Value.rat == null;
            bool idle = removeIdleAfter > 0f && Time.time - kv.Value.lastActiveTime > removeIdleAfter;
            if (destroyed || idle) toRemove.Add(kv.Key);
        }

        foreach (string name in toRemove)
            RemoveRat(name);
    }
}