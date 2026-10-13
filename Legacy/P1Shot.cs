using UnityEngine;
using System.Collections;

public class P1Shot : MonoBehaviour
{
    public float deadZone = -1.49f;
    bool hasHit;
    public float rotationSpeed = 360f; // Rotation speed in degrees per second
    private Rigidbody2D rb;
    public float damageAmount = 10f; // Amount of damage the shot deals

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 1; // Enable gravity for the shot immediately
    }

    // Update is called once per frame
    void Update()
    {
        // Destroy the shot if it goes beyond the dead zone
        if (transform.position.y < deadZone)
        {
            Destroy(gameObject);
        }

        // Rotate the shot object around its z-axis
        transform.Rotate(Vector3.forward * rotationSpeed * Time.deltaTime);

        // Adjust the rotation based on velocity if it hasn't hit anything
        if (!hasHit)
        {
            float angle = Mathf.Atan2(rb.linearVelocity.y, rb.linearVelocity.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
        }
    }

    // This method is called when the collider attached to this object collides with another collider
    void OnCollisionEnter2D(Collision2D collision)
    {
        // Check if the shot collided with a player, obstacle, or the floor
        if (collision.gameObject.CompareTag("Player") || collision.gameObject.CompareTag("Obstacle") || collision.gameObject.CompareTag("Floor") || collision.gameObject.CompareTag("Player2"))
        {
            hasHit = true;
            rb.linearVelocity = Vector2.zero; // Stop the shot from moving
            rb.bodyType = RigidbodyType2D.Kinematic; // Change the body type to Kinematic

            // If the shot hits a player, decrease the player's health
            HealthSystem2 healthSystem2 = collision.gameObject.GetComponent<HealthSystem2>();
            if (healthSystem2 != null)
            {
                healthSystem2.TakeDamage(damageAmount);
            }

            // Destroy the shot object
            Destroy(gameObject);
        }
    }
}