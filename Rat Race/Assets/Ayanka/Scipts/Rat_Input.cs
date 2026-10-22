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
/// Rat Input.
/// Connects to Twitch chat, reads commands, and tells the right rat what to do.
/// Spawning and tracking rats is handled by RatManager.
///
/// Commands (case-insensitive, must be the whole message):
///   Join      : "join", "!join"   (only needed if RatManager has Require Join Command on)
///   Go Faster : "r", "run", "1"
///   Attack    : "a {username}", "attack {username}", "2 {username}"   (an @ before the name is fine)
///   Defend    : "d", "defend", "3"
/// </summary>
public class Rat_Input : MonoBehaviour
{
    [Header("Twitch Login (fill these in the Inspector, not in code)")]
    [Tooltip("Twitch username of the bot account.")]
    [SerializeField] string user = "lost_my_bawls";
    [Tooltip("OAuth token from twitchtokengenerator.com. Treat it like a password.")]
    [SerializeField] string oauth = "dxvhnh1asokl1u40f1w66edouke1q5";
    [Tooltip("Channel to read chat from (your channel name).")]
    [SerializeField] string channel = "lost_my_bawls";

    [Header("Testing")]
    [Tooltip("Tick this to test without Twitch. Use RatTester to send fake chat messages.")]
    [SerializeField] bool offlineMode = false;

    [Header("Rats")]
    [Tooltip("The RatManager in the scene. If empty, looks on this same GameObject.")]
    [SerializeField] RatManager ratManager;

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

    readonly List<string> recentCommands = new List<string>();

    static readonly HashSet<string> JoinWords = new HashSet<string> { "join", "!join", "j", "JOIN", "J" };
    static readonly HashSet<string> RunWords = new HashSet<string> { "r", "run", "1", "R", "RUN", "FAST", "BOOST" };
    static readonly HashSet<string> AttackWords = new HashSet<string> { "a", "attack", "2", "A", "ATTACK", "attak", "atack"};
    static readonly HashSet<string> DefendWords = new HashSet<string> { "d", "defend", "3", "D", "DEFEND", "defen", "defends" };

    void Awake()
    {
        if (ratManager == null) ratManager = GetComponent<RatManager>();
        if (ratManager == null)
            Debug.LogError("[Rat_Input] No RatManager assigned. Drag your RatManager object into the Rat Manager box.");

        if (offlineMode)
            Debug.Log("[Rat_Input] Offline Mode is on: not connecting to Twitch.");
        else
            Connect();
    }

    /// Pretend a viewer typed a message in chat. Used by RatTester.
    public void SimulateChat(string chatter, string msg)
    {
        if (string.IsNullOrWhiteSpace(chatter) || string.IsNullOrWhiteSpace(msg)) return;
        chatter = chatter.Trim().ToLower();
        msg = msg.Trim();

        OnChatMessage?.Invoke(chatter, msg);
        HandleCommand(chatter, msg);
    }

    void Update()
    {
        // Chat lines are read on a background thread; handle them here on the main thread.
        while (incoming.TryDequeue(out string line))
        {
            HandleLine(line);
        }

        if (!connected && !offlineMode)
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
        if (ratManager == null) return;

        string[] parts = msg.ToLower().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;

        string cmd = parts[0];

        // Join
        if (JoinWords.Contains(cmd) && parts.Length == 1)
        {
            GetRatFor(chatter, allowSpawn: true);
            return;
        }

        bool canSpawn = !ratManager.RequireJoinCommand;

        // 1. Go Faster
        if (RunWords.Contains(cmd) && parts.Length == 1)
        {
            RatController rat = GetRatFor(chatter, canSpawn);
            if (rat != null && rat.GoFaster())
                LogCommand(chatter + " RUNS!");
            return;
        }

        // 2. Attack {username}
        if (AttackWords.Contains(cmd) && parts.Length >= 2)
        {
            string targetName = parts[1].TrimStart('@');
            if (targetName == chatter) return;

            RatController target = ratManager.GetRat(targetName);
            if (target == null) return; // no rat with that name

            RatController rat = GetRatFor(chatter, canSpawn);
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
            RatController rat = GetRatFor(chatter, canSpawn);
            if (rat != null && rat.Defend())
                LogCommand(chatter + " defends");
        }
    }

    // Gets the chatter's rat. If they don't have one and allowSpawn is true,
    // asks RatManager to spawn one.
    RatController GetRatFor(string chatter, bool allowSpawn)
    {
        if (!allowSpawn)
        {
            RatController rat = ratManager.GetRat(chatter);
            if (rat != null) ratManager.MarkActive(chatter);
            return rat;
        }

        RatController r = ratManager.GetOrSpawnRat(chatter, out bool isNew);
        if (isNew) LogCommand(chatter + " joined the race!");
        return r;
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