using UnityEngine;

public class Player1Script : MonoBehaviour
{
    public GameObject p1shot; // Reference to the arrow prefab
    public float moveSpeed; // Speed at which the player moves
    public float launchForce;
    public Transform shotPoint;
    public GameObject shotFireEffect; // Reference to the particle system prefab
    public LogicManager logicManager; // Reference to the logic manager

    private float currentLaunchForce;
    private bool controlsEnabled = false;
    private Rigidbody2D rb;

    void Start()
    {
        logicManager = GameObject.Find("LogicManager").GetComponent<LogicManager>();
        currentLaunchForce = launchForce;
        rb = GetComponent<Rigidbody2D>();
    }

    void Update()
    {
        if (!controlsEnabled) return;

        if (Input.GetKey(KeyCode.D))
        {
            rb.linearVelocity = new Vector2(moveSpeed, rb.linearVelocity.y);
            transform.localScale = new Vector3(1, 1, 1); // Face right
        }
        else if (Input.GetKey(KeyCode.A))
        {
            rb.linearVelocity = new Vector2(-moveSpeed, rb.linearVelocity.y);
            transform.localScale = new Vector3(-1, 1, 1); // Face left
        }
        else
        {
            rb.linearVelocity = new Vector2(0, rb.linearVelocity.y); // Stop moving when no key is pressed
        }

        if (Input.GetKeyDown(KeyCode.Space))
        {
            Shoot();
            logicManager.EndTurn();
        }
    }

    public void SetLaunchForce(float power)
    {
        currentLaunchForce = power;
    }

    public void Shoot()
    {
        GameObject newShot = Instantiate(p1shot, shotPoint.position, shotPoint.rotation);
        Rigidbody2D shotRb = newShot.GetComponent<Rigidbody2D>();

        // Determine the direction to shoot based on the player's facing direction
        Vector2 shootDirection = transform.right * currentLaunchForce;
        if (transform.localScale.x < 0)
        {
            shootDirection = -transform.right * currentLaunchForce;
        }

        shotRb.linearVelocity = shootDirection;
        shotRb.gravityScale = 1; // Enable gravity for the shot

        // Instantiate the visual effect at the shot point
        if (shotFireEffect != null)
        {
            Instantiate(shotFireEffect, shotPoint.position, shotPoint.rotation);
        }
    }

    public void EnableControls(bool enable)
    {
        controlsEnabled = enable;
    }

    public void Die()
    {
        // Add any additional logic for when the player dies here
        Destroy(gameObject);
    }
}