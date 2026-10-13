using UnityEngine;

public class MoveTrail : MonoBehaviour
{
    [SerializeField] ParticleSystem trailParticles;
    [SerializeField] private Player player;
    [SerializeField] private float minSpeed = 0.1f;

    private void Update()
    {
        bool onGround = player.groundDetected;

        bool isMoving = Mathf.Abs(player.rb.linearVelocity.x) > minSpeed;

        if (onGround && isMoving)
        {
            if (trailParticles.isEmitting == false)
                trailParticles.Play();
        }
        else
        {
            if (trailParticles.isEmitting)
                trailParticles.Stop();
        }
    }
}