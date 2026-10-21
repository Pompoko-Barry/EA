using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Rat Manager / Input.
/// Connects to Twitch chat, reads commands, spawns one rat per chatter,
/// and tells each rat what to do.
///
/// Commands (case-insensitive, must be the whole message):
///   Go Faster : "r", "run", "1"
///   Attack    : "a {username}", "attack {username}", "2 {username}"   (an @ before the name is fine)
///   Defend    : "d", "defend", "3"
///
/// A chatter gets a rat the first time they send any valid command.
/// </summary>
public class Rat_Input : MonoBehaviour
{
    [Header("Twitch Login (fill these in the Inspector, not in code)")]
    [Tooltip("Twitch username of the bot account.")]
    [SerializeField] string user = "lost_my_bawls";
    [Tooltip("OAuth token from twitchtokengenerator.com. Treat it like a password.")]
    [SerializeField] string oauth = "85a3bvp34ri6tgd4hw5lqrgt8qd582";
    [Tooltip("Channel to read chat from (your channel name).")]
    [SerializeField] string channel = "lost_my_bawls";

    [Header("Rats")]
    [SerializeField] RatController ratPrefab;
    [Tooltip("Where the first rat spawns. Each new rat spawns one lane below the last.")]
    [SerializeField] Transform spawnPoint;
    [SerializeField] float laneSpacing = 1.0f;
    [Tooltip("After this many lanes, new rats wrap back to the top lane.")]
    [SerializeField] int maxLanes = 10;

    [Header("UI")]
    public TextMeshProUGUI recentCommandsText;
    [SerializeField] int recentCommandCount = 8;

    [Header("Events")]
    public UnityEvent<string, string> OnChatMessage; // (chatter, message)

    const string URL = "irc.chat.twitch.tv";
    const int PORT = 6667;
    const float ReconnectDelay = 5f;

    // Connection
    TcpClient twitch;
    StreamReader reader;
    StreamWriter writer;
    readonly object writeLock = new object();
    readonly ConcurrentQueue<string> incoming = new ConcurrentQueue<string>();
    volatile bool connected;
    int connectionId;
    float reconnectTimer;

    // Rats
    readonly Dictionary<string, RatController> rats = new Dictionary<string, RatController>();
    int nextLane;

    readonly List<string> recentCommands = new List<string>();

    static readonly HashSet<string> RunWords = new HashSet<string> { "r", "run", "1" };
    static readonly HashSet<string> AttackWords = new HashSet<string> { "a", "attack", "2" };
    static readonly HashSet<string> DefendWords = new HashSet<string> { "d", "defend", "3" };

    // Read-only access for other scripts (UI, leaderboard, finish line, etc.)
    public IReadOnlyDictionary<string, RatController> Rats => rats;

    void Awake()
    {
        Connect();
    }

    void Update()
    {
        // Chat lines are read on a background thread; handle them here on the main thread.
        while (incoming.TryDequeue(out string line))
        {
            HandleLine(line);
        }

        if (!connected)
        {
            reconnectTimer += Time.deltaTime;
            if (reconnectTimer >= ReconnectDelay)
            {
                reconnectTimer = 0f;
                Debug.Log("[Rat_Input] Reconnecting to Twitch...");
                Connect();
            }
        }
    }

    void OnDestroy()
    {
        Disconnect();
    }

    // ---------------------------------------------------------------------
    // Twitch connection
    // ---------------------------------------------------------------------

    void Connect()
    {
        Disconnect();

        if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(oauth) || string.IsNullOrEmpty(channel))
        {
            Debug.LogError("[Rat_Input] Fill in user, oauth and channel in the Inspector.");
            reconnectTimer = -999f; // don't spam reconnects until fixed
            return;
        }

        try
        {
            twitch = new TcpClient(URL, PORT);
            NetworkStream stream = twitch.GetStream();
            reader = new StreamReader(stream);
            writer = new StreamWriter(stream) { NewLine = "\r\n", AutoFlush = true };

            string pass = oauth.StartsWith("oauth:") ? oauth : "oauth:" + oauth;
            Send("PASS " + pass);
            Send("NICK " + user.ToLower());
            Send("JOIN #" + channel.ToLower());

            connected = true;
            int myId = ++connectionId;
            StreamReader myReader = reader;
            Thread readThread = new Thread(() => ReadLoop(myReader, myId)) { IsBackground = true };
            readThread.Start();

            Debug.Log("[Rat_Input] Connected to Twitch chat #" + channel.ToLower());
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Rat_Input] Could not connect: " + e.Message);
            connected = false;
        }
    }

    void Disconnect()
    {
        connected = false;
        connectionId++; // stops any old read thread from touching state
        try { twitch?.Close(); } catch { }
        twitch = null;
        reader = null;
        writer = null;
    }

    // Runs on a background thread so ReadLine never freezes the game.
    void ReadLoop(StreamReader r, int id)
    {
        try
        {
            while (id == connectionId)
            {
                string line = r.ReadLine();
                if (line == null) break; // server closed the connection

                // Twitch pings about every 5 minutes. We MUST answer or we get kicked.
                if (line.StartsWith("PING"))
                {
                    Send("PONG" + line.Substring(4));
                    continue;
                }

                incoming.Enqueue(line);
            }
        }
        catch (Exception)
        {
            // Socket closed or errored, fall through to mark disconnected.
        }

        if (id == connectionId) connected = false;
    }

    void Send(string text)
    {
        lock (writeLock)
        {
            try { writer?.WriteLine(text); }
            catch (Exception) { connected = false; }
        }
    }

    // ---------------------------------------------------------------------
    // Chat parsing
    // ---------------------------------------------------------------------

    // Typical line: ":someuser!someuser@someuser.tmi.twitch.tv PRIVMSG #channel :attack bob"
    void HandleLine(string line)
    {
        if (line.Contains("Login authentication failed") || line.Contains("Improperly formatted auth"))
        {
            Debug.LogError("[Rat_Input] Twitch rejected the login. Check your OAuth token and username.");
            return;
        }

        if (line.Contains(" RECONNECT"))
        {
            connected = false; // Update() will reconnect
            return;
        }

        int privIndex = line.IndexOf(" PRIVMSG ", StringComparison.Ordinal);
        if (privIndex < 0) return;

        int bang = line.IndexOf('!');
        if (bang < 2 || bang > privIndex) return;
        string chatter = line.Substring(1, bang - 1).ToLower();

        int msgStart = line.IndexOf(" :", privIndex, StringComparison.Ordinal);
        if (msgStart < 0) return;
        string msg = line.Substring(msgStart + 2).Trim();

        OnChatMessage?.Invoke(chatter, msg);
        HandleCommand(chatter, msg);
    }

    void HandleCommand(string chatter, string msg)
    {
        string[] parts = msg.ToLower().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;

        string cmd = parts[0];

        // 1. Go Faster
        if (RunWords.Contains(cmd) && parts.Length == 1)
        {
            RatController rat = GetOrSpawnRat(chatter);
            if (rat != null && rat.GoFaster())
                LogCommand(chatter + " RUNS!");
            return;
        }

        // 2. Attack {username}
        if (AttackWords.Contains(cmd) && parts.Length >= 2)
        {
            string targetName = parts[1].TrimStart('@');
            if (targetName == chatter) return;

            if (!rats.TryGetValue(targetName, out RatController target) || target == null)
                return; // no rat with that name

            RatController rat = GetOrSpawnRat(chatter);
            if (rat == null) return;

            if (rat.Attack(target, out bool landed))
                LogCommand(landed
                    ? chatter + " stunned " + targetName + "!"
                    : chatter + " attacked " + targetName + " (blocked)");
            return;
        }

        // 3. Defend
        if (DefendWords.Contains(cmd) && parts.Length == 1)
        {
            RatController rat = GetOrSpawnRat(chatter);
            if (rat != null && rat.Defend())
                LogCommand(chatter + " defends");
        }
    }

    // ---------------------------------------------------------------------
    // Rats
    // ---------------------------------------------------------------------

    RatController GetOrSpawnRat(string username)
    {
        if (rats.TryGetValue(username, out RatController existing))
        {
            if (existing != null) return existing;
            rats.Remove(username); // rat was destroyed somewhere else
        }

        if (ratPrefab == null)
        {
            Debug.LogError("[Rat_Input] No ratPrefab assigned.");
            return null;
        }

        Vector3 origin = spawnPoint != null ? spawnPoint.position : transform.position;
        int lane = nextLane % Mathf.Max(1, maxLanes);
        nextLane++;
        Vector3 pos = origin + Vector3.down * laneSpacing * lane;

        RatController rat = Instantiate(ratPrefab, pos, Quaternion.identity);
        rat.Init(username);
        rats[username] = rat;

        LogCommand(username + " joined the race!");
        return rat;
    }

    public RatController GetRat(string username)
    {
        rats.TryGetValue(username.ToLower(), out RatController rat);
        return rat;
    }

    void LogCommand(string text)
    {
        recentCommands.Add(text);
        while (recentCommands.Count > recentCommandCount)
            recentCommands.RemoveAt(0);

        if (recentCommandsText != null)
            recentCommandsText.text = string.Join("\n", recentCommands);
    }
}