using UnityEngine;

public class Player2ArrowScript : MonoBehaviour
{
    public GameObject p2shot; // Reference to the arrow prefab
    public float rotationSpeed; // Speed at which the arrow rotates
    public float launchForce;
    public Transform shotPoint;
    public GameObject shotFireEffect; // Reference to the particle system prefab

    private float currentLaunchForce;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        currentLaunchForce = launchForce;
    }

    // Update is called once per frame
    void Update()
    {
        Vector2 shotPosition = transform.position;
        Vector2 mousePosition = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector2 direction = mousePosition - shotPosition;
        transform.right = direction;
    }

    public void SetLaunchForce(float power)
    {
        currentLaunchForce = power;
    }

    public void Shoot()
    {
        GameObject newShot = Instantiate(p2shot, shotPoint.position, shotPoint.rotation);
        Rigidbody2D rb = newShot.GetComponent<Rigidbody2D>();

        // Determine the direction to shoot based on the arrow's facing direction
        Vector2 shootDirection = transform.right * currentLaunchForce;

        rb.linearVelocity = shootDirection;

        // Instantiate the visual effect at the shot point
        if (shotFireEffect != null)
        {
            Instantiate(shotFireEffect, shotPoint.position, shotPoint.rotation);
        }
    }
}