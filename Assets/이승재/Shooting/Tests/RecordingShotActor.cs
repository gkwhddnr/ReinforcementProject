using UnityEngine;

namespace Reinforcement.Shooting.Tests
{
    public sealed class RecordingShotActor : ShotActor
    {
        public int id;
        public int team;
        public float health = float.PositiveInfinity;
        public int Requests { get; private set; }
        public DamageRequest LastRequest { get; private set; }

        public override int ActorId => id;
        public override int TeamId => team;
        public override bool Alive => health > 0f;

        public override DamageReceipt ReceiveDamage(DamageRequest request)
        {
            Requests++;
            LastRequest = request;
            float applied = Mathf.Min(health, request.Amount);
            health -= applied;
            return new DamageReceipt(applied, health == 0f);
        }
    }
}
