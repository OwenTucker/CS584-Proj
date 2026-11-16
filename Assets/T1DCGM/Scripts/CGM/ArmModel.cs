using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace T1DCGM
{
    /// <summary>
    /// Upper-arm model built from three controller touches: top of the shoulder, point of the elbow, and the back of the
    /// upper arm. All points are in world (tracking) space, so the learner should keep the arm relaxed at their side and not
    /// walk away after calibrating.
    ///
    /// The arm is treated as a cylinder of radius <see cref="radius"/> whose surface passes through the back touch.
    /// A point on the arm is described by (t, angle): t runs 0 at the shoulder to 1 at the elbow, angle is degrees around
    /// the arm measured from the back touch (positive toward the outside of the arm).
    /// </summary>
    [System.Serializable]
    public class ArmModel
    {
        public Vector3 shoulder;
        public Vector3 elbow;
        public Vector3 back;
        [Tooltip("Approximate upper-arm radius in metres (a typical adult upper arm is 4 to 5.5 cm).")]
        public float radius = 0.045f;
        public bool valid;

        // Derived frame
        public Vector3 axis;        // unit, shoulder -> elbow
        public float length;        // shoulder -> elbow distance along the axis
        public Vector3 backDir;     // unit, from the arm centre line toward the back touch
        public Vector3 outDir;      // unit, perpendicular to axis and backDir, pointing away from the body
        public Vector3 centreAtShoulder;

        // Allowed zone for this CGM (back of the upper arm).
        public const float ZoneTMin = 0.3f, ZoneTMax = 0.75f, ZoneHalfAngle = 50f;
        /// <summary>How far from the skin surface (metres) still counts as touching.</summary>
        public const float ContactTolerance = 0.025f;

        /// <summary>Builds the frame. <paramref name="outwardHint"/> points away from the body (to the learner's left for the left arm).</summary>
        public string Build(Vector3 outwardHint)
        {
            valid = false;
            var se = elbow - shoulder;
            length = se.magnitude;
            if (length < 0.15f || length > 0.5f)
                return $"The shoulder and elbow touches were {length * 100f:0} cm apart. An upper arm is usually 25 to 35 cm. Try again.";
            axis = se / length;

            var radial = Vector3.ProjectOnPlane(back - shoulder, axis);
            if (radial.magnitude < 0.005f)
                return "The back-of-arm touch was in line with the shoulder and elbow. Touch the back of the arm, halfway down.";
            backDir = radial.normalized;

            float tBack = Vector3.Dot(back - shoulder, axis) / length;
            if (tBack < 0.1f || tBack > 0.95f)
                return "Touch the back of the arm between the shoulder and the elbow.";

            // The surface passes through the back touch; the centre line is one radius inside it.
            centreAtShoulder = shoulder + Vector3.Project(back - shoulder, backDir) - backDir * radius;
            outDir = Vector3.Cross(axis, backDir).normalized;
            if (Vector3.Dot(outDir, outwardHint) < 0f)
                outDir = -outDir;
            valid = true;
            return null;
        }

        public Vector3 CentreAt(float t) => centreAtShoulder + axis * (t * length);

        /// <summary>Signed degrees from the back direction toward the outside of the arm.</summary>
        public float AngleOf(Vector3 radialDir) =>
            Mathf.Atan2(Vector3.Dot(radialDir, outDir), Vector3.Dot(radialDir, backDir)) * Mathf.Rad2Deg;

        public Vector3 DirectionAt(float angle)
        {
            float a = angle * Mathf.Deg2Rad;
            return backDir * Mathf.Cos(a) + outDir * Mathf.Sin(a);
        }

        public Vector3 SurfacePoint(float t, float angle) => CentreAt(t) + DirectionAt(angle) * radius;

        /// <summary>Describes where a world point is relative to the arm.</summary>
        public Placement Locate(Vector3 point)
        {
            var p = new Placement();
            var rel = point - centreAtShoulder;
            p.t = Vector3.Dot(rel, axis) / length;
            var radial = Vector3.ProjectOnPlane(rel, axis);
            p.distanceFromCentre = radial.magnitude;
            p.normal = p.distanceFromCentre > 1e-4f ? radial / p.distanceFromCentre : backDir;
            p.angle = AngleOf(p.normal);
            p.surfaceGap = p.distanceFromCentre - radius;
            p.touching = Mathf.Abs(p.surfaceGap) <= ContactTolerance && p.t > -0.1f && p.t < 1.1f;
            p.inZone = p.t >= ZoneTMin && p.t <= ZoneTMax && Mathf.Abs(p.angle) <= ZoneHalfAngle;
            return p;
        }

        /// <summary>Distance along the skin (metres) between two (t, angle) sites.</summary>
        public float SkinDistance(Vector2 a, Vector2 b)
        {
            float along = (a.x - b.x) * length;
            float around = Mathf.DeltaAngle(a.y, b.y) * Mathf.Deg2Rad * radius;
            return Mathf.Sqrt(along * along + around * around);
        }
    }

    public struct Placement
    {
        public float t, angle, distanceFromCentre, surfaceGap;
        public Vector3 normal;
        public bool touching, inZone;
        public Vector2 site => new Vector2(t, angle);
    }

    /// <summary>Previous sensor sites, saved between sessions as (t, angle) pairs so they survive recalibration.</summary>
    public static class SiteHistory
    {
        const string k_Key = "T1DCGM.CgmSites";
        const int k_Max = 6;

        public static List<Vector2> Load()
        {
            var list = new List<Vector2>();
            var raw = PlayerPrefs.GetString(k_Key, "");
            foreach (var entry in raw.Split(';'))
            {
                var parts = entry.Split(',');
                if (parts.Length == 2 &&
                    float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float t) &&
                    float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float a))
                    list.Add(new Vector2(t, a));
            }
            return list;
        }

        public static void Add(Vector2 site)
        {
            var list = Load();
            list.Add(site);
            while (list.Count > k_Max)
                list.RemoveAt(0);
            var parts = new List<string>();
            foreach (var s in list)
                parts.Add(s.x.ToString("0.###", CultureInfo.InvariantCulture) + "," + s.y.ToString("0.#", CultureInfo.InvariantCulture));
            PlayerPrefs.SetString(k_Key, string.Join(";", parts));
            PlayerPrefs.Save();
        }

        public static void Clear()
        {
            PlayerPrefs.DeleteKey(k_Key);
            PlayerPrefs.Save();
        }
    }
}
