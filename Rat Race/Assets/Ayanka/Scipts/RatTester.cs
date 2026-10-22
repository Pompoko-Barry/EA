using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Testing helper. Pretends to be Twitch chat so you can test without going live.
///
/// How to use (in Play mode):
///   - Fill in Fake Username + Fake Message, then tick "Send Message".
///   - Tick "Spawn Fake Viewers" to add a bunch of test rats at once.
///   - Tick "Random Commands" to have the fake viewers spam run/attack/defend.
///
/// The checkboxes un-tick themselves after they fire, so you can click them again.
/// You can also right-click the component title (or use its ⋮ menu) for the same actions.
///
/// Remove this component (or leave everything unticked) before streaming.
/// </summary>
public class RatTester : MonoBehaviour
{
    [Tooltip("The Rat_Input in the scene. If empty, looks on this same GameObject.")]
    [SerializeField] Rat_Input ratInput;

    [Header("Send one fake chat message")]
    [SerializeField] string fakeUsername = "testrat1";
    [Tooltip("Exactly what the viewer would type, e.g. run, d, attack testrat2")]
    [SerializeField] string fakeMessage = "run";
    [Tooltip("Tick to send the message above.")]
    [SerializeField] bool sendMessage;

    [Header("Spawn fake viewers")]
    [SerializeField] int fakeViewerCount = 5;
    [Tooltip("Tick to add that many fake viewers (testrat1, testrat2, ...).")]
    [SerializeField] bool spawnFakeViewers;

    [Header("Random chaos")]
    [Tooltip("While ticked, fake viewers send random commands.")]
    [SerializeField] bool randomCommands;
    [SerializeField] float commandsPerSecond = 2f;

    readonly List<string> fakeViewers = new List<string>();
    int nextViewerNumber = 1;
    float randomTimer;

    void Awake()
    {
        if (ratInput == null) ratInput = GetComponent<Rat_Input>();
        if (ratInput == null) Debug.LogError("[RatTester] Drag your Rat_Input object into the Rat Input box.");
    }

    void Update()
    {
        if (ratInput == null) return;

        if (sendMessage)
        {
            sendMessage = false;
            SendFakeMessage();
        }

        if (spawnFakeViewers)
        {
            spawnFakeViewers = false;
            SpawnFakeViewers();
        }

        if (randomCommands && fakeViewers.Count > 0 && commandsPerSecond > 0f)
        {
            randomTimer += Time.deltaTime;
            float interval = 1f / commandsPerSecond;
            while (randomTimer >= interval)
            {
                randomTimer -= interval;
                SendRandomCommand();
            }
        }
    }

    [ContextMenu("Send Fake Message")]
    public void SendFakeMessage()
    {
        if (ratInput == null) return;
        Debug.Log("[RatTester] " + fakeUsername + ": " + fakeMessage);
        ratInput.SimulateChat(fakeUsername, fakeMessage);

        string name = fakeUsername.Trim().ToLower();
        if (!fakeViewers.Contains(name)) fakeViewers.Add(name);
    }

    [ContextMenu("Spawn Fake Viewers")]
    public void SpawnFakeViewers()
    {
        if (ratInput == null) return;
        for (int i = 0; i < fakeViewerCount; i++)
        {
            string name = "testrat" + nextViewerNumber++;
            fakeViewers.Add(name);
            ratInput.SimulateChat(name, "join");
        }
        Debug.Log("[RatTester] Added " + fakeViewerCount + " fake viewers.");
    }

    void SendRandomCommand()
    {
        string chatter = fakeViewers[Random.Range(0, fakeViewers.Count)];
        string msg;

        switch (Random.Range(0, 3))
        {
            case 0:
                msg = "run";
                break;
            case 1:
                msg = "attack";
                break;
            default:
                msg = "defend";
                break;
        }

        ratInput.SimulateChat(chatter, msg);
    }
}