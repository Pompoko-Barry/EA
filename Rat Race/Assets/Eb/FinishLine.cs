using UnityEngine;

public class FinishLine : MonoBehaviour
{
    public GameStateManager gsm;

    void OnTriggerEnter2D(Collider2D col)
    {
        if (gsm._state == GameStateManager.GameState.RACING && col.gameObject.GetComponent<RatController>())
        {
            gsm.ThisRatWins(col.gameObject);
        }
    }
}
