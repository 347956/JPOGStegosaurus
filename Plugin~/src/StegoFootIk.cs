using UnityEngine;

namespace JPOGStegosaurus
{
    internal sealed class StegoFootIk
    {
        private readonly Transform root;
        private readonly Leg[] legs;
        private readonly int groundMask = LayerMask.GetMask(
            "Default", "Room", "Colliders", "NavigationSurface", "MiscLevelGeometry", "Terrain");
        private readonly RaycastHit[] hits = new RaycastHit[16];
        private Vector3 previousPosition;
        private bool previousRunning;
        private bool previousStanding;

        private StegoFootIk(Transform root, Leg[] legs)
        {
            this.root = root;
            this.legs = legs;
            previousPosition = root.position;
        }

        public static StegoFootIk? Create(Animator animator)
        {
            Transform root = animator.transform;
            Transform? hips = root.Find("Steg_hi/Scene_Root/d");
            Transform? shoulders = hips?.Find("d_Spine/d_Spine1");
            if (hips == null || shoulders == null)
                return null;

            // The feet have no toes. The local sole centres come from the lowest skinned vertices
            // primarily weighted to each hand or foot. Frame ranges come from the 80-frame walk and
            // 40-frame run, following when each sole lands and begins to swing forward.
            Leg? frontLeft = Leg.Find(shoulders, "d_UpperArm.L/d_Forearm.L/d_Hand.L",
                new Vector3(0.0015454f, 0.0026085f, -0.0003369f), new Stance(68, 45, 80), new Stance(36, 20, 40));
            Leg? frontRight = Leg.Find(shoulders, "d_UpperArm.R/d_Forearm.R/d_Hand.R",
                new Vector3(-0.0016079f, 0.0026152f, 0.0000752f), new Stance(26, 4, 80), new Stance(17, 40, 40));
            Leg? hindLeft = Leg.Find(hips, "d_Thigh.L/d_Calf.L/d_Foot.L",
                new Vector3(-0.0015251f, 0.0034644f, 0.0013263f), new Stance(46, 27, 80), new Stance(25, 13, 40));
            Leg? hindRight = Leg.Find(hips, "d_Thigh.R/d_Calf.R/d_Foot.R",
                new Vector3(0.0015933f, 0.0034683f, 0.0014133f), new Stance(5, 66, 80), new Stance(2, 32, 40));
            return frontLeft != null && frontRight != null && hindLeft != null && hindRight != null
                ? new StegoFootIk(root, new[] { frontLeft, frontRight, hindLeft, hindRight })
                : null;
        }

        public void RestoreAnimatedPose()
        {
            foreach (Leg leg in legs)
                leg.RestoreAnimatedPose();
        }

        public void Reset()
        {
            foreach (Leg leg in legs)
                leg.Reset();
            previousPosition = root.position;
        }

        public void Apply(float phase, bool running, bool standing, float turn, float deltaTime)
        {
            if (deltaTime <= 0f)
                return;

            RestoreAnimatedPose();
            if (running != previousRunning || standing != previousStanding)
                Reset();
            previousRunning = running;
            previousStanding = standing;

            float scale = Mathf.Abs(root.lossyScale.y);
            Vector3 rootPosition = root.position;
            float warpDistance = 1.5f * scale;
            if ((rootPosition - previousPosition).sqrMagnitude > warpDistance * warpDistance
                || !TryGround(rootPosition, scale, out _))
            {
                Reset();
                return;
            }

            previousPosition = rootPosition;
            float lockStrength = 1f - Mathf.SmoothStep(0f, 0.85f, Mathf.Abs(turn));
            foreach (Leg leg in legs)
                leg.Apply(this, phase, running, standing, lockStrength, scale, deltaTime);
        }

        private bool TryGround(Vector3 position, float scale, out RaycastHit ground)
        {
            float top = Mathf.Max(root.position.y, position.y) + 0.65f * scale;
            float bottom = Mathf.Min(root.position.y, position.y) - 0.85f * scale;
            position.y = top;
            int count = Physics.RaycastNonAlloc(position, Vector3.down, hits, top - bottom, groundMask, QueryTriggerInteraction.Ignore);
            ground = default;
            float closest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hits[i];
                if (hit.distance < closest && hit.normal.y >= 0.64f && !hit.collider.transform.IsChildOf(root))
                {
                    closest = hit.distance;
                    ground = hit;
                }
            }
            return closest < float.PositiveInfinity;
        }

        private readonly struct Stance
        {
            private readonly float start;
            private readonly float duration;

            public Stance(int firstFrame, int liftFrame, int cycle)
            {
                start = firstFrame / (float)cycle;
                duration = Mathf.Repeat((liftFrame - firstFrame) / (float)cycle, 1f);
            }

            public float Weight(float phase)
            {
                float elapsed = Mathf.Repeat(phase - start, 1f);
                float land = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.06f, elapsed));
                float lift = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(duration - 0.09f, duration, elapsed));
                return land * lift;
            }
        }

        private sealed class Leg
        {
            private readonly Transform upper;
            private readonly Transform lower;
            private readonly Transform foot;
            private readonly Vector3 solePoint;
            private readonly Stance walk;
            private readonly Stance run;

            private Quaternion authoredUpper, authoredLower, authoredFoot;
            private bool adjusted;
            private bool anchored;
            private Vector3 anchor;
            private Vector3 offset;
            private Quaternion tilt = Quaternion.identity;
            private float weight;

            private Leg(Transform foot, Vector3 solePoint, Stance walk, Stance run)
            {
                this.foot = foot;
                lower = foot.parent;
                upper = lower.parent;
                this.solePoint = solePoint;
                this.walk = walk;
                this.run = run;
            }

            public static Leg? Find(Transform parent, string path, Vector3 solePoint, Stance walk, Stance run)
            {
                Transform? foot = parent.Find(path);
                return foot != null ? new Leg(foot, solePoint, walk, run) : null;
            }

            public void RestoreAnimatedPose()
            {
                if (!adjusted)
                    return;

                upper.localRotation = authoredUpper;
                lower.localRotation = authoredLower;
                foot.localRotation = authoredFoot;
                adjusted = false;
            }

            public void Reset()
            {
                RestoreAnimatedPose();
                anchored = false;
                offset = Vector3.zero;
                tilt = Quaternion.identity;
                weight = 0f;
            }

            public void Apply(StegoFootIk rig, float phase, bool running, bool standing, float lockStrength, float scale, float deltaTime)
            {
                float contact = standing ? 1f : (running ? run : walk).Weight(phase);
                Vector3 sole = foot.TransformPoint(solePoint);
                if (contact <= 0f)
                {
                    Reset();
                    return;
                }

                if (contact < 0.25f)
                    anchored = false;
                if (!anchored && contact >= 0.6f)
                {
                    anchor = sole;
                    anchored = true;
                }

                // Limit compensation for a stride whose authored travel differs from the AI speed.
                // Retain vertical placement while sliding the lock toward the animated foot in a turn.
                Vector3 slide = anchored ? Vector3.ProjectOnPlane(anchor - sole, Vector3.up) : Vector3.zero;
                Vector3 targetOffset = Vector3.ClampMagnitude(slide, 0.32f * scale) * lockStrength;
                Quaternion targetTilt = Quaternion.identity;
                if (rig.TryGround(sole + targetOffset, scale, out RaycastHit floor))
                {
                    targetOffset.y = Mathf.Clamp(floor.point.y - sole.y, -0.42f * scale, 0.45f * scale);
                    targetTilt = Quaternion.RotateTowards(Quaternion.identity,
                        Quaternion.FromToRotation(Vector3.up, floor.normal), 25f);
                }
                else
                {
                    anchored = false;
                    targetOffset = Vector3.zero;
                    contact = 0f;
                }

                float response = 1f - Mathf.Exp(-26f * deltaTime);
                offset = Vector3.Lerp(offset, targetOffset, response);
                tilt = Quaternion.Slerp(tilt, targetTilt, response);
                weight = Mathf.MoveTowards(weight, contact, deltaTime / 0.07f);
                if (weight <= 0.001f)
                    return;

                Quaternion slope = Quaternion.Slerp(Quaternion.identity, tilt, weight);
                Vector3 target = sole + offset * weight - slope * (sole - foot.position);
                Solve(target, slope * foot.rotation);
            }

            private void Solve(Vector3 target, Quaternion rotation)
            {
                Vector3 hip = upper.position;
                Vector3 thigh = lower.position - hip;
                Vector3 shin = foot.position - lower.position;
                float thighLength = thigh.magnitude;
                float shinLength = shin.magnitude;
                Vector3 toTarget = target - hip;
                Vector3 direction = toTarget.normalized;
                Vector3 authoredBend = Vector3.ProjectOnPlane(thigh, thigh + shin);
                Vector3 bend = Vector3.ProjectOnPlane(authoredBend, direction).normalized;
                float margin = Mathf.Min(thighLength, shinLength) * 0.01f;
                if (margin <= 0.0001f || direction == Vector3.zero || bend == Vector3.zero)
                    return;

                float distance = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(thighLength - shinLength) + margin,
                    thighLength + shinLength - margin);
                float along = (thighLength * thighLength - shinLength * shinLength + distance * distance) / (2f * distance);
                float height = Mathf.Sqrt(Mathf.Max(0f, thighLength * thighLength - along * along));
                Vector3 knee = hip + direction * along + bend * height;
                Vector3 reachableFoot = hip + direction * distance;

                authoredUpper = upper.localRotation;
                authoredLower = lower.localRotation;
                authoredFoot = foot.localRotation;
                adjusted = true;
                upper.rotation = Quaternion.FromToRotation(thigh, knee - hip) * upper.rotation;
                lower.rotation = Quaternion.FromToRotation(foot.position - lower.position, reachableFoot - lower.position) * lower.rotation;
                foot.rotation = rotation;
            }
        }
    }
}
