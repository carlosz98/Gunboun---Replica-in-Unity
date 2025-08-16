using UnityEngine;
using UnityEngine.UI;

public class PowerBarScript : MonoBehaviour
{
    public Image powerBarFill; // Reference to the power bar fill image
    public float maxPower = 100f; // Maximum power value
    public float chargeSpeed = 30f; // Speed at which the power bar charges
    private float currentPower = 0f; // Current power value

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        powerBarFill.fillAmount = currentPower / maxPower;
    }

    // Update is called once per frame
    void Update()
    {
        // Charge the power bar based on player input
        if (Input.GetKey(KeyCode.Space))
        {
            currentPower += chargeSpeed * Time.deltaTime;
            currentPower = Mathf.Clamp(currentPower, 0, maxPower);
            powerBarFill.fillAmount = currentPower / maxPower;
        }

        // Reset the power bar when the player releases the input
        if (Input.GetKeyUp(KeyCode.Space))
        {
            // Use the current power value to determine the launch force of the shot
            Player1ArrowScript player1ArrowScript = Object.FindFirstObjectByType<Player1ArrowScript>();
            if (player1ArrowScript != null)
            {
                player1ArrowScript.SetLaunchForce(currentPower);
                player1ArrowScript.Shoot();
            }

            Player2ArrowScript player2ArrowScript = Object.FindFirstObjectByType<Player2ArrowScript>();
            if (player2ArrowScript != null)
            {
                player2ArrowScript.SetLaunchForce(currentPower);
                player2ArrowScript.Shoot();
            }

            currentPower = 0f;
            powerBarFill.fillAmount = currentPower / maxPower;
        }
    }
}