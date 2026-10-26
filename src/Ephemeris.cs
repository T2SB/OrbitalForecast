using System;
using UnityEngine;

namespace OrbitalForecast
{
    // Orbital position math. Everything is computed by walking up each body's
    // referenceBody chain so that moons, planets and bodies in other star
    // systems (interstellar Kopernicus packs) all resolve correctly.
    public static class Ephemeris
    {
        // Absolute world-space position of a body at universal time 'ut'.
        // We defer to KSP's own Orbit.getPositionAtUT so that our ghost markers
        // land in exactly the same frame the game uses for the body itself and
        // for its closest-approach / encounter markers — no hand-rolled swizzle
        // that could drift from the stock convention.
        public static Vector3d AbsolutePositionAtUT(CelestialBody body, double ut)
        {
            if (body == null) return Vector3d.zero;

            // Root body (the Sun, or a galactic core in interstellar setups)
            // has no orbit; treat it as fixed at its current world position.
            if (body.orbit == null || body.referenceBody == null || body.referenceBody == body)
                return body.position;

            return body.orbit.getPositionAtUT(ut);
        }

        // The body that 'body' ultimately orbits (its star / system root).
        public static CelestialBody RootOf(CelestialBody body)
        {
            CelestialBody b = body;
            while (b != null && b.orbit != null && b.referenceBody != null && b.referenceBody != b)
                b = b.referenceBody;
            return b;
        }

        // How many orbits deep a body sits from its root.
        // Root = 0, stock planet = 1, stock moon = 2.
        public static int Depth(CelestialBody body)
        {
            int d = 0;
            CelestialBody b = body;
            while (b != null && b.orbit != null && b.referenceBody != null && b.referenceBody != b)
            {
                d++;
                b = b.referenceBody;
            }
            return d;
        }

        // Longitude (degrees, 0-360) of 'body' as seen from 'center', in the
        // world XZ plane. Used only for relative phase-angle comparisons.
        public static double LongitudeAround(CelestialBody body, CelestialBody center, double ut)
        {
            Vector3d rel = AbsolutePositionAtUT(body, ut) - AbsolutePositionAtUT(center, ut);
            double ang = Math.Atan2(rel.z, rel.x) * 180.0 / Math.PI;
            if (ang < 0.0) ang += 360.0;
            return ang;
        }

        // Signed phase angle (-180..180) of 'target' relative to 'origin',
        // measured around the body 'origin' orbits. This is the number you
        // line up when planning a Hohmann transfer.
        public static double PhaseAngle(CelestialBody origin, CelestialBody target, double ut)
        {
            if (origin == null || target == null) return 0.0;
            CelestialBody center = origin.referenceBody != null ? origin.referenceBody : RootOf(origin);
            if (center == null) return 0.0;

            double lo = LongitudeAround(origin, center, ut);
            double lt = LongitudeAround(target, center, ut);
            return Normalize180(lt - lo);
        }

        // The star a body belongs to: nearest ancestor flagged isStar (or the
        // root if none is). A star returns itself.
        public static CelestialBody StarOf(CelestialBody body)
        {
            CelestialBody b = body;
            while (b != null)
            {
                if (b.isStar) return b;
                if (b.orbit == null || b.referenceBody == null || b.referenceBody == b) return b;
                b = b.referenceBody;
            }
            return body;
        }

        // The ideal phase angle (target relative to origin, degrees) at the
        // moment of a Hohmann transfer burn. Line the current angle up with
        // this and it's time to go. NaN if the two bodies don't share a parent.
        public static double IdealTransferPhaseAngle(CelestialBody origin, CelestialBody target)
        {
            if (origin == null || target == null) return double.NaN;
            if (origin.orbit == null || target.orbit == null) return double.NaN;
            if (origin.referenceBody == null || origin.referenceBody != target.referenceBody) return double.NaN;

            double a1 = origin.orbit.semiMajorAxis;
            double a2 = target.orbit.semiMajorAxis;
            if (a1 <= 0.0 || a2 <= 0.0) return double.NaN;

            // Fraction of the target's period taken by the half-ellipse transfer,
            // expressed as the angle the target sweeps during the trip.
            double transferFraction = System.Math.Pow((a1 + a2) / (2.0 * a2), 1.5);
            return Normalize180(180.0 * (1.0 - transferFraction));
        }

        public static double Normalize180(double deg)
        {
            while (deg <= -180.0) deg += 360.0;
            while (deg > 180.0) deg -= 360.0;
            return deg;
        }

        // Seconds until the next transfer window: the next time the current
        // phase angle will line up with the ideal Hohmann angle. 0 means the
        // window is open now. NaN if a Hohmann window isn't well-defined.
        public static double TimeToTransferWindow(CelestialBody origin, CelestialBody target, double now)
        {
            double ideal = IdealTransferPhaseAngle(origin, target);
            if (double.IsNaN(ideal)) return double.NaN;

            double to = origin.orbit.period;
            double tt = target.orbit.period;
            if (to <= 0.0 || tt <= 0.0 || double.IsInfinity(to) || double.IsInfinity(tt)) return double.NaN;

            // Rate of change of (target longitude - origin longitude), deg/sec.
            double rate = 360.0 / tt - 360.0 / to;
            if (System.Math.Abs(rate) < 1e-12) return double.NaN;

            double current = PhaseAngle(origin, target, now);
            double synodic = 360.0 / System.Math.Abs(rate);
            double t = ((ideal - current) % 360.0) / rate;
            while (t < 0.0) t += synodic;
            return t;
        }
    }
}
