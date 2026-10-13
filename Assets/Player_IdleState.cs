using UnityEngine;

public class Player_IdleState : Player_GroundedState
{
    public Player_IdleState(Player player, StateMachine stateMachine, string stateName) : base(player, stateMachine, stateName)
    {
    }

    public override void Enter()
    {
        base.Enter();


        

    }

    public override void Update()
    {
        player.MoveTowardsVelocityX(0);

        if (rb.linearVelocity.x == 0 && player.groundDetected)
            rb.constraints = RigidbodyConstraints2D.FreezePositionX | RigidbodyConstraints2D.FreezeRotation;
        
        base.Update();

        if(player.moveInput.x != 0)
            stateMachine.ChangeState(player.moveState);
        
    }

    public override void Exit()
    {
        base.Exit();

        rb.constraints = RigidbodyConstraints2D.FreezeRotation;
    }
}
