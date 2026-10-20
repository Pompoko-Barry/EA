using UnityEngine;

public class AnimatedRatController : MonoBehaviour
{
    public Sprite[] possibleSprites;
    public Color[] possibleColors;
    private Animator anim;
    private SpriteRenderer sr;
    private GameStateManager gsm;

    void Start()
    {
        anim = GetComponent<Animator>();
        sr = GetComponent<SpriteRenderer>();
        gsm = FindAnyObjectByType<GameStateManager>();

        if(gsm != null)
        {
            gsm.onRacingBegin += Run;
            gsm.onGameOver += Idle;
        }
        
        GiveRandomAppearance();
    }

    private void GiveRandomAppearance()
    {
        int randSprite = Random.Range(0, possibleSprites.Length);
        sr.sprite = possibleSprites[randSprite];

        int randColor = Random.Range(0, possibleColors.Length);
        sr.color = possibleColors[randColor];
    }

    public void Idle()
    {
        anim.SetBool("isRunning", false);
    }

    public void Run()
    {
        anim.SetBool("isRunning", true);
    }

    private void OnDisable()
    {
        if (gsm != null)
        {
            gsm.onRacingBegin -= Run;
            gsm.onGameOver -= Idle;
        }
    }
}
