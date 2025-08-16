using UnityEngine;

public class GameController : MonoBehaviour
{
    public Player1Script player1;
    public Player2Script player2;
    private bool player1Turn = true;

    void Start()
    {
        UpdateTurn();
    }

    void UpdateTurn()
    {
        if (player1Turn)
        {
            player1.EnableControls(true);
            player2.EnableControls(false);
        }
        else
        {
            player1.EnableControls(false);
            player2.EnableControls(true);
        }
    }

    public void EndTurn()
    {
        player1Turn = !player1Turn;
        UpdateTurn();
    }
}