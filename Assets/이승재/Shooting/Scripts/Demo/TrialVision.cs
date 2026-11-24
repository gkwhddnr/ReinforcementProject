using System;
using System.Collections.Generic;
using UnityEngine;

namespace Reinforcement.Shooting.Demo
{
    // Trial sight provider. Only the explicitly assigned rivals can enter a frame.
    public sealed class TrialVision : MonoBehaviour
    {
        [Min(0)] public float chunkSize = VisibilityFrame.TrialChunkSize;
        [Range(0, 360)] public float horizontalFov = 90f;
        public float MaxDetectionDistance => 2f * chunkSize;

        private ShotActor observer;
        private Transform eye;
        private ShotActor[] rivals = Array.Empty<ShotActor>();

        public void Configure(ShotActor observer, Transform eye, params ShotActor[] rivals)
        {
            this.observer = observer;
            this.eye = eye;
            this.rivals = rivals ?? Array.Empty<ShotActor>();
        }

        public VisibilityFrame Sample(int episodeId, int tick, int mask)
        {
            List<ShotActor> visible = null;
            if (observer && observer.isActiveAndEnabled && observer.Alive && eye && chunkSize > 0f)
            {
                Vector3 forward = Vector3.ProjectOnPlane(eye.forward, Vector3.up);
                foreach (ShotActor rival in rivals)
                {
                    if (!rival || rival == observer || !rival.isActiveAndEnabled || !rival.Alive ||
                        rival.TeamId == observer.TeamId || !rival.Capsule || !rival.Capsule.enabled)
                        continue;

                    Vector3 offset = rival.AimPoint - eye.position;
                    Vector3 horizontal = Vector3.ProjectOnPlane(offset, Vector3.up);
                    if (horizontal.magnitude > MaxDetectionDistance ||
                        Vector3.Angle(forward, horizontal) > horizontalFov * 0.5f)
                        continue;

                    ShotContact hit = ShotPhysics.Trace(observer, eye.position, offset.normalized,
                        offset.magnitude, mask);
                    if (hit.Kind == ContactKind.Actor && hit.Actor == rival)
                    {
                        if (visible == null) visible = new List<ShotActor>();
                        visible.Add(rival);
                    }
                }
            }
            // Earlier frames keep their own list when a later tick changes visibility.
            IReadOnlyList<ShotActor> snapshot = visible;
            return new VisibilityFrame(episodeId, tick, snapshot ?? Array.Empty<ShotActor>(), MaxDetectionDistance);
        }
    }
}
