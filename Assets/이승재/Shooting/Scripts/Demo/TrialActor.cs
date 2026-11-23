using UnityEngine;

namespace Reinforcement.Shooting.Demo
{
    // Local stand-in for a team's identity, current capsule and health receipt.
    public sealed class TrialActor : ShotActor
    {
        [SerializeField] private int actorId;
        [SerializeField] private int teamId;
        [SerializeField] private Transform bodyMesh;
        [SerializeField] private Transform eye;
        [SerializeField] private Transform mount;
        [SerializeField] private Transform muzzle;

        public override int ActorId => actorId;
        public override int TeamId => teamId;
        public override bool Alive => Health > 0f;
        public float Health { get; private set; }
        public bool Crouched { get; private set; }
        public Transform Eye => eye;
        public Transform Mount => mount;
        public Transform Muzzle => muzzle;

        public void Configure(int actorId, int teamId, Transform bodyMesh, Transform eye, Transform mount, Transform muzzle)
        {
            this.actorId = actorId;
            this.teamId = teamId;
            this.bodyMesh = bodyMesh;
            this.eye = eye;
            this.mount = mount;
            this.muzzle = muzzle;
        }

        public void ResetTrial()
        {
            Health = 100f;
            SetCrouched(false);
        }

        public void SetCrouched(bool value)
        {
            Crouched = value;
            float height = value ? 1.2f : 2f;
            if (Capsule != null)
            {
                Capsule.height = height;
                Capsule.center = Vector3.up * (height * 0.5f);
            }
            if (bodyMesh != null)
            {
                bodyMesh.localPosition = Capsule != null ? Capsule.center : Vector3.up * (height * 0.5f);
                bodyMesh.localScale = new Vector3(1f, height * 0.5f, 1f);
            }
            if (eye != null) eye.localPosition = Vector3.up * (height - 0.18f);
            if (mount != null) mount.localPosition = Vector3.up * (height * 0.62f);
        }

        public override DamageReceipt ReceiveDamage(DamageRequest request)
        {
            float applied = Mathf.Clamp(request.Amount, 0f, Health);
            Health -= applied;
            return new DamageReceipt(applied, applied > 0f && !Alive);
        }
    }
}
