using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using System.Net.Sockets;
using System.IO;
using UnityEngine.SceneManagement;
using TMPro;

public class Rat_Input : MonoBehaviour
{
    public TextMeshProUGUI recentCommandsText;
    public UnityEvent<string, string> OnChatMessage;

    [SerializeField] PlayerController playerController;

    TcpClient Twitch;
    StreamReader Reader;
    StreamWriter Writer;

    const string URL = "irc.chat.twitch.tv";
    const int PORT = 6667;

    //put your twitch username here - make a new account for security reasons. DO NOT PUBLISH YOUR CODE AS PUBLIC ON GITHUB.
    string User = "Lost_My_Bawls";

    //copy and paste OAuth Access Token from     https://twitchtokengenerator.com
    string OAuth = "oauth:pkgcr7pyhl4ougosb8vut02jyn2vch";  //your OAuth is basically as good as a password, so you SHOULD make a new account before doing this. I STRESS AGAIN, DO NOT SHARE THIS CODE.
    //oh and, do NOT emit the "oauth:" at the beginning of this define. It should be string OAuth = "oauth: (paste your Oauth here)"; 

    //this is the channel you want to connect to, use your userid.
    string Channel = "Lost_My_Bawls";

    public float pingCounter;

    private List<string> recentCommands = new List<string>();
    private void Start()
    {

    }
    private void ConnectToTwitch()
    {
        Twitch = new TcpClient(URL, PORT);
        Reader = new StreamReader(Twitch.GetStream());
        Writer = new StreamWriter(Twitch.GetStream());

        Writer.WriteLine("PASS " + OAuth);
        Writer.WriteLine("NICK " + User.ToLower()); //"NICK" = nickname
        Writer.WriteLine("JOIN #" + Channel.ToLower());
        Writer.Flush(); // sends all the stuff you wrote to the tcp so it actually connects
    }

    void Awake()
    {
        ConnectToTwitch();
    }

    void Update()
    {
        pingCounter += Time.deltaTime;
        if (pingCounter > 60)
        {
            Writer.WriteLine("PING " + URL);
            Writer.Flush();
            pingCounter = 0;
        }

        if (!Twitch.Connected)
        {
            ConnectToTwitch();
        }

        if (Twitch.Available > 0)
        {
            // Respond to server PINGs (critical)
            // Typical form: "PING :tmi.twitch.tv"
            //if (line.StartsWith("PING"))
            //{
            //    writer?.WriteLine(line.Replace("PING", "PONG"));
            //    continue;
            //}

            Debug.Log("Twitch is Available");
            //if something is available in the TCP that we can grab with the stream reader
            string message = Reader.ReadLine(); //reads the next available line in our TCP

            if (message.Contains("PRIVMSG"))    //code that will come with any message that's written by a user
            {


                int splitPoint = message.IndexOf("!");  //notice the '!' in front of the message - we can use this '!' to isolate the username, emphsising on could because I didn't use it.
                string chatter = message.Substring(1, splitPoint - 1);  //extracts the first word...
                                                                        //aka the person speaking

                splitPoint = message.IndexOf(":", 1);   //so everything after that colon is the message that the user typed... in this case, 'hello world'
                string msg = message.Substring(splitPoint + 1);    //anything that's passed that colon, bring it in to that string

                //This UnityEvent is what will look at the messager, and their message - then it will invoke a method from another script that we assign! 
                //You can assign the method it invokes in the inspector.
                OnChatMessage?.Invoke(chatter, msg);
                print(msg);

                //This part is where script reads twitch chat msgs and compare them to your trigger words
                if (msg == "w" || msg == "W" || msg == "UP" || msg == "up" || msg == "Up" ||
                    msg == "q" || msg == "Q" || msg == "e" || msg == "E" ||
                    msg == "r" || msg == "R" || msg == "t" || msg == "T" || msg == "y" || msg == "Y")
                {
                    Debug.Log("UP Registered");
                    playerController.GoUP();
                    //call your keyword method here!
                }
                if (msg == "s" || msg == "S" || msg == "DOWN" || msg == "down" || msg == "Down" ||
                    msg == "a" || msg == "A" || msg == "d" || msg == "D" ||
                    msg == "f" || msg == "F" || msg == "g" || msg == "G" || msg == "h" || msg == "H")
                {
                    Debug.Log("Down Registered");
                    playerController.GoDown();
                    //call your keyword method here!
                }

            }
        }
    }
}