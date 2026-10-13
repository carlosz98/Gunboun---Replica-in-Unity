using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class LogicManager : MonoBehaviour
{
    public int playerCounter;
    public Text scoreText;
    public Text timerText; // Reference to the timer text UI
    public float turnDuration = 20f; // Duration of each turn in seconds
    private float currentTurnTime;
    private bool player1Turn = true;

    public Player1Script player1;
    public Player2Script player2;
    public GameObject mainMenu; // Reference to the main menu UI
    public Button startButton; // Reference to the start button

    void Start()
    {
        // Show the main menu and hide the game UI
        mainMenu.SetActive(true);
        timerText.gameObject.SetActive(false);
        scoreText.gameObject.SetActive(false);

        // Add listener to the start button
        startButton.onClick.AddListener(StartGame);
    }

    void StartGame()
    {
        // Hide the main menu and show the game UI
        mainMenu.SetActive(false);
        timerText.gameObject.SetActive(true);
        scoreText.gameObject.SetActive(true);

        currentTurnTime = turnDuration;
        UpdateTurn();
        StartCoroutine(TurnTimer());
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
        currentTurnTime = turnDuration;
        UpdateTurn();
    }

    IEnumerator TurnTimer()
    {
        while (true)
        {
            if (currentTurnTime > 0)
            {
                currentTurnTime -= Time.deltaTime;
                timerText.text = "Time: " + Mathf.Ceil(currentTurnTime).ToString();
            }
            else
            {
                EndTurn();
            }
            yield return null;
        }
    }

    public void addScore()
    {
        playerCounter += 1;
        scoreText.text = "Score: " + playerCounter.ToString();
    }
}