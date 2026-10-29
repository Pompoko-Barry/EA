using System;
using System.Collections;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;

public class GameStateManager : MonoBehaviour
{
    public enum GameState { SPAWNING, RACING, GAME_OVER, TRANSITION }
    public GameState _state { get; private set; }

    [Header("General")]
    public TMP_Text timerUI;
    public TMP_Text stateHeaderUI;
    //public TMP_Text stateControlsUI;
    private float currentTimer;
    public RatManager ratManager;
    public AudioSource bgMusic;
    public float music_defaultPitch = 0.9f;
    public float music_racingPitch = 1.3f;

    [Header("Spawning State")]
    public string spawningHeaderString;
    public string spawningControlsString;
    public float spawningTime;

    [Header("Racing State")]
    public string racingHeaderString;
    public string racingControlsString;
    public event Action onRacingBegin;
    public AudioSource countdownSFX;

    [Header("Game Over State")]
    public string gameOverHeaderString;
    public float gameOverTime;
    public event Action onGameOver;
    public Transform winnerLocation;
    public float timeToMoveToWinnerLoc;
    public float timeToTurnOnSpotlight;
    public GameObject spotlightPanel;
    public GameObject blackPanel;
    public ParticleSystem confetti;
    public AudioSource confettiSFX, hornSFX, clapSFX;

    [Header("Transition State")]
    public string transitionHeaderString;
    public float transitionTime;
    private GameState nextState;

    void Start()
    {
        spotlightPanel.SetActive(false);
        blackPanel.SetActive(false);

        SetState(GameState.SPAWNING);
    }

    void Update()
    {
        switch (_state)
        {
            case GameState. SPAWNING:
                SpawningUpdate();
                break;
            case GameState. RACING:
                RacingUpdate();
                break;
            case GameState.GAME_OVER:
                GameOverUpdate();
                break;
            case GameState.TRANSITION:
                TransitionUpdate();
                break;
            default:
                Debug.Log("Updating as unknown game state.");
                break;
        }
    }

    public void ThisRatWins(GameObject ratThatWon)
    {
        //ratThatWon.GetComponentInChildren<AnimatedRatController>().Idle();

        SetState(GameState.GAME_OVER);
        StartCoroutine(CelebrateWinningRat(ratThatWon));
    }

    IEnumerator CelebrateWinningRat(GameObject ratThatWon)
    {
        blackPanel.SetActive(true);

        Vector2 startPos = ratThatWon.transform.position;
        Vector3 startSize = ratThatWon.transform.localScale;

        float timer = 0f;
        while(timer < timeToMoveToWinnerLoc)
        {
            yield return null;

            timer += Time.deltaTime;

            ratThatWon.transform.position = Vector2.Lerp(startPos, winnerLocation.position, timer / timeToMoveToWinnerLoc);
            ratThatWon.transform.localScale = Vector3.Lerp(startSize, winnerLocation.localScale, timer / timeToMoveToWinnerLoc);
        }

        ratThatWon.transform.position = winnerLocation.position;
        ratThatWon.transform.localScale = winnerLocation.localScale;

        yield return new WaitForSeconds(timeToTurnOnSpotlight);

        // animation
        blackPanel.SetActive(false);
        spotlightPanel.SetActive(true);
        confetti.Play();
        // sfx
        confettiSFX.Play();
        hornSFX.Play();
        clapSFX.Play();
    }

    // use this to set the game state
    public void SetState(GameState state)
    {
        if (_state == GameState.GAME_OVER)
        {
            spotlightPanel.SetActive(false);
            ratManager.ClearAllRats();
        }

        _state = state;

        bgMusic.pitch = music_defaultPitch;

        switch (_state)
        {
            case GameState.SPAWNING:
                stateHeaderUI.text = spawningHeaderString;
                //stateControlsUI.text = spawningControlsString;
                currentTimer = spawningTime;
                break;
            case GameState.RACING:
                stateHeaderUI.text = racingHeaderString;
                //stateControlsUI.text = racingControlsString;
                timerUI.text = " ";
                onRacingBegin?.Invoke();
                ratManager.ActivateRats(true);
                bgMusic.pitch = music_racingPitch;
                break;
            case GameState.GAME_OVER:
                stateHeaderUI.text = gameOverHeaderString;
                //stateControlsUI.text = " ";
                currentTimer = gameOverTime;
                onGameOver?.Invoke();
                ratManager.ActivateRats(false);
                break;
            case GameState.TRANSITION:
                stateHeaderUI.text = transitionHeaderString;
                //stateControlsUI.text = " ";
                currentTimer = transitionTime;
                break;
            default:
                Debug.Log("Game state set to unknown game state.");
                break;
        }
    }

    private void SpawningUpdate()
    {
        bool timesUp = UpdateCurrentTimer();
        if (timesUp)
        {
            nextState = GameState.RACING;
            SetState(GameState.TRANSITION);
        }
    }

    private void RacingUpdate()
    {
        // i dont think there will be anything in here
    }

    private void GameOverUpdate()
    {
        bool timesUp = UpdateCurrentTimer();
        if (timesUp)
        {
            nextState = GameState.SPAWNING;
            SetState(GameState.TRANSITION);
        }
    }

    private void TransitionUpdate()
    {
        float previousTime = currentTimer;

        bool timesUp = UpdateCurrentTimer();

        if(nextState == GameState.RACING)
        {
            if(previousTime > 3f && currentTimer <= 3f)
            {
                countdownSFX.pitch = 1f;
                countdownSFX.Play();
            }
            else if(previousTime > 2f && currentTimer <= 2f)
            {
                countdownSFX.pitch = 1f;
                countdownSFX.Play();
            }
            else if(previousTime > 1f && currentTimer <= 1f)
            {
                countdownSFX.pitch = 1f;
                countdownSFX.Play();
            }
        }

        if (timesUp)
        { 
            if (nextState == GameState.RACING)
            {
                countdownSFX.pitch = 1.2f;
                countdownSFX.Play();
            }

            SetState(nextState);
        }
    }

    // updates float timer and timer ui to match
    private bool UpdateCurrentTimer()
    {
        currentTimer -= Time.deltaTime;
        timerUI.text = FormatTime(currentTimer);

        if (currentTimer <= 0f)
        {
            return true;
        }
        else
        {
            return false;
        }
    }

    // returns the float timer as a string in minutes:seconds format
    private string FormatTime(float timeToFormat)
    {
        if(timeToFormat < 0f) { timeToFormat = 0f; }

        int minutes = Mathf.FloorToInt(timeToFormat / 60);
        int seconds = Mathf.FloorToInt(timeToFormat % 60);

        return string.Format("{0:00}:{1:00}", minutes, seconds);
    }
}
