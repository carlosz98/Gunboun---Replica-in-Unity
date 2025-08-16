using UnityEngine;
using UnityEngine.UI;

public class HealthSystem2 : MonoBehaviour
{
    public float maxHealth = 100;
    private float currentHealth;
    [SerializeField] private Image healthBarFill;
    [SerializeField] private float damageAmount;

    private void Awake()
    {
        currentHealth = maxHealth;
        UpdateHealthBar();
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("P1Shot") || collision.CompareTag("P2Shot"))
        {
            TakeDamage(damageAmount);
        }
    }

    public void TakeDamage(float amount)
    {
        currentHealth -= amount;
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
        UpdateHealthBar();
        if (currentHealth == 0)
        {
            Die();
        }
    }

    private void UpdateHealthBar()
    {
        if (healthBarFill != null)
        {
            healthBarFill.fillAmount = currentHealth / maxHealth;
        }
    }

    private void Die()
    {
        Player2Script player2Script = GetComponent<Player2Script>();
        if (player2Script != null)
        {
            player2Script.Die();
        }
    }
}