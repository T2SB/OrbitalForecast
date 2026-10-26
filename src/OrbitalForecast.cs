using System;
using System.Collections.Generic;
using KSP.UI.Screens;
using UnityEngine;

namespace OrbitalForecast
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class OrbitalForecastFlight : OrbitalForecastBase { }

    [KSPAddon(KSPAddon.Startup.TrackingStation, false)]
    public class OrbitalForecastTrackingStation : OrbitalForecastBase { }

    public class BodyRow
    {
        public CelestialBody body;
        public int indent;
        public BodyRow(CelestialBody b, int i) { body = b; indent = i; }
    }

    public abstract class OrbitalForecastBase : MonoBehaviour
    {
        const string LockId = "OrbitalForecast_lock";
        const string PickLockId = "OrbitalForecast_pick";
        static readonly int[] RangeYears = { 1, 5, 25, 100 };

        static readonly Color Accent = new Color(0.40f, 0.80f, 0.95f);
        static readonly Color Go = new Color(0.40f, 0.92f, 0.55f);
        static readonly Color Warn = new Color(0.98f, 0.82f, 0.35f);
        static readonly Color TextCol = new Color(0.86f, 0.90f, 0.95f);
        static readonly Color Subtle = new Color(0.56f, 0.61f, 0.69f);

        ForecastSettings settings;
        ApplicationLauncherButton button;
        Texture2D icon;

        bool windowOpen;
        Rect windowRect = new Rect(70f, 70f, 400f, 0f);
        Vector2 bodyScroll;

        double forecastUT;
        int rangeIdx = 1;
        bool pickMode;
        string dStr = "0", hStr = "0", mStr = "0", sStr = "0";

        readonly Dictionary<string, bool> show = new Dictionary<string, bool>();
        readonly Dictionary<string, bool> starOpen = new Dictionary<string, bool>();
        List<CelestialBody> stars = new List<CelestialBody>();
        readonly Dictionary<string, List<BodyRow>> systemRows = new Dictionary<string, List<BodyRow>>();

        // styling
        GUISkin uiSkin;
        GUIStyle stHeader, stCaption, stBig, stGo, stWarn, stDim, stMini, stFold;
        Texture2D texWindow, texPanel, texBtn, texBtnHover, texBtnActive, texBtnOn, texScroll, texThumb, texBar;

        double DayLen { get { return KSPUtil.dateTimeFormatter.Day; } }
        double HourLen { get { return KSPUtil.dateTimeFormatter.Hour; } }
        double MinLen { get { return KSPUtil.dateTimeFormatter.Minute; } }
        double YearLen { get { return KSPUtil.dateTimeFormatter.Year; } }
        double YearDays { get { return YearLen / DayLen; } }

        // ---------------------------------------------------------------- life

        void Start()
        {
            settings = ForecastSettings.Load();
            double now = Planetarium.GetUniversalTime();
            forecastUT = now + settings.defaultStepDays * DayLen;

            foreach (CelestialBody b in FlightGlobals.Bodies)
            {
                if (b == null || b.orbit == null) continue;
                bool isPlanet = b.referenceBody != null && b.referenceBody.isStar;
                show[b.bodyName] = isPlanet || (settings.showMoons && !isPlanet);
            }

            BuildSystems();
            GameEvents.onGUIApplicationLauncherReady.Add(AddButton);
            AddButton();
        }

        void OnDestroy()
        {
            GameEvents.onGUIApplicationLauncherReady.Remove(AddButton);
            RemoveButton();
            InputLockManager.RemoveControlLock(LockId);
            InputLockManager.RemoveControlLock(PickLockId);
        }

        void Update()
        {
            // Pick mode must NOT lock the camera — you need to orbit/zoom the map
            // to line a point up. And when our window is closed we must hold no
            // lock at all: the GUI lock is otherwise only released from OnGUI,
            // which doesn't run while closed, so a leftover lock freezes the map.
            InputLockManager.RemoveControlLock(PickLockId);
            if (!windowOpen) InputLockManager.RemoveControlLock(LockId);
        }

        // ------------------------------------------------------------- systems

        void BuildSystems()
        {
            stars = new List<CelestialBody>();
            systemRows.Clear();
            foreach (CelestialBody b in FlightGlobals.Bodies)
                if (b != null && b.isStar) stars.Add(b);

            CelestialBody home = FlightGlobals.GetHomeBody();
            CelestialBody homeStar = home != null ? Ephemeris.StarOf(home) : null;
            stars.Sort(delegate(CelestialBody x, CelestialBody y)
            {
                if (x == homeStar) return -1;
                if (y == homeStar) return 1;
                return string.Compare(x.bodyName, y.bodyName, StringComparison.Ordinal);
            });

            foreach (CelestialBody star in stars)
            {
                List<BodyRow> rows = new List<BodyRow>();
                AddChildren(star, 0, rows);
                systemRows[star.bodyName] = rows;
                if (!starOpen.ContainsKey(star.bodyName))
                    starOpen[star.bodyName] = (star == homeStar);
            }
        }

        void AddChildren(CelestialBody parent, int indent, List<BodyRow> rows)
        {
            List<CelestialBody> kids = new List<CelestialBody>();
            foreach (CelestialBody b in FlightGlobals.Bodies)
            {
                if (b == null || b.orbit == null || b.isStar) continue;
                if (b.referenceBody == parent) kids.Add(b);
            }
            kids.Sort(delegate(CelestialBody a, CelestialBody c)
            { return a.orbit.semiMajorAxis.CompareTo(c.orbit.semiMajorAxis); });
            foreach (CelestialBody k in kids)
            {
                rows.Add(new BodyRow(k, indent));
                AddChildren(k, indent + 1, rows);
            }
        }

        // -------------------------------------------------------------- button

        void AddButton()
        {
            if (button != null || ApplicationLauncher.Instance == null) return;
            if (icon == null) icon = MakeIcon();
            button = ApplicationLauncher.Instance.AddModApplication(
                OnOpen, OnClose, null, null, null, null,
                ApplicationLauncher.AppScenes.FLIGHT | ApplicationLauncher.AppScenes.MAPVIEW | ApplicationLauncher.AppScenes.TRACKSTATION,
                icon);
        }

        void RemoveButton()
        {
            if (button != null && ApplicationLauncher.Instance != null)
                ApplicationLauncher.Instance.RemoveModApplication(button);
            button = null;
        }

        void OnOpen() { windowOpen = true; }
        void OnClose()
        {
            windowOpen = false;
            pickMode = false;
            InputLockManager.RemoveControlLock(PickLockId);
            InputLockManager.RemoveControlLock(LockId);
        }

        void CloseWindow()
        {
            if (button != null) button.SetFalse(true);
            else OnClose();
        }

        static bool InMapContext()
        {
            if (HighLogic.LoadedSceneIsFlight) return MapView.MapIsEnabled;
            return HighLogic.LoadedScene == GameScenes.TRACKSTATION;
        }

        // ---------------------------------------------------------------- GUI

        void OnGUI()
        {
            if (!windowOpen) return;
            BuildSkin();
            GUI.skin = uiSkin;

            if (InMapContext())
            {
                Camera cam = PlanetariumCamera.Camera;
                if (cam != null)
                {
                    DrawMarkers(cam);
                    if (pickMode) DrawPickPath(cam);
                }
            }

            windowRect = GUILayout.Window(GetInstanceID(), windowRect, DrawWindow, "Orbital Forecast");
            HandleLock();
        }

        void HandleLock()
        {
            Vector2 m = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            if (windowOpen && windowRect.Contains(m))
                InputLockManager.SetControlLock(
                    ControlTypes.MAP | ControlTypes.CAMERACONTROLS | ControlTypes.TARGETING |
                    ControlTypes.VESSEL_SWITCHING | ControlTypes.TIMEWARP, LockId);
            else
                InputLockManager.RemoveControlLock(LockId);
        }

        void DrawMarkers(Camera cam)
        {
            foreach (CelestialBody b in FlightGlobals.Bodies)
            {
                if (b == null || b.orbit == null) continue;
                bool sel;
                if (!show.TryGetValue(b.bodyName, out sel) || !sel) continue;

                Vector3 cs = cam.WorldToScreenPoint((Vector3)ScaledSpace.LocalToScaledSpace(b.position));
                Vector3 fs = cam.WorldToScreenPoint((Vector3)ScaledSpace.LocalToScaledSpace(Ephemeris.AbsolutePositionAtUT(b, forecastUT)));
                Vector2 cur = new Vector2(cs.x, Screen.height - cs.y);
                Vector2 fut = new Vector2(fs.x, Screen.height - fs.y);

                bool cok = cs.z > 0f && Finite(cs);
                bool fok = fs.z > 0f && Finite(fs);

                if (settings.showLines && cok && fok)
                    DrawUtils.Line(cur, fut, 1.5f, settings.lineColor);
                if (cok)
                    DrawUtils.Dot(cur, settings.markerSize * 0.5f, settings.currentColor);
                if (fok)
                {
                    DrawUtils.Dot(fut, settings.markerSize, settings.ghostColor);
                    Color old = GUI.color;
                    GUI.color = settings.ghostColor;
                    GUI.Label(new Rect(fut.x + settings.markerSize, fut.y - 9f, 130f, 20f), b.bodyName);
                    GUI.color = old;
                }
            }
        }

        // Draws the active vessel's current + planned trajectory as sampled dots
        // and lets the user click the nearest point to set the forecast time.
        void DrawPickPath(Camera cam)
        {
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null || v.orbit == null) return;
            double now = Planetarium.GetUniversalTime();

            List<Orbit> orbits = new List<Orbit>();
            orbits.Add(v.orbit);
            if (v.patchedConicSolver != null && v.patchedConicSolver.flightPlan != null)
                foreach (Orbit o in v.patchedConicSolver.flightPlan)
                    if (o != null && o != v.orbit) orbits.Add(o);

            Vector2 mouseUp = new Vector2(Event.current.mousePosition.x, Screen.height - Event.current.mousePosition.y);
            Color dotCol = new Color(Accent.r, Accent.g, Accent.b, 0.55f);

            float best = 32f;
            bool found = false;
            double bestUT = 0.0;
            Vector2 bestGui = Vector2.zero;

            foreach (Orbit o in orbits)
            {
                double s, e;
                SampleRange(o, now, out s, out e);
                const int N = 160;
                for (int i = 0; i <= N; i++)
                {
                    double t = s + (e - s) * (i / (double)N);
                    Vector3 sp = cam.WorldToScreenPoint((Vector3)ScaledSpace.LocalToScaledSpace(o.getPositionAtUT(t)));
                    if (sp.z <= 0f || !Finite(sp)) continue;
                    Vector2 gui = new Vector2(sp.x, Screen.height - sp.y);
                    if (i % 2 == 0) DrawUtils.Dot(gui, 3f, dotCol);
                    float d = Vector2.Distance(new Vector2(sp.x, sp.y), mouseUp);
                    if (d < best) { best = d; found = true; bestUT = t; bestGui = gui; }
                }
            }

            if (found)
            {
                DrawUtils.Dot(bestGui, 13f, new Color(Accent.r, Accent.g, Accent.b, 0.30f));
                DrawUtils.Dot(bestGui, 6f, Accent);
                Color old = GUI.color;
                GUI.color = Accent;
                GUI.Label(new Rect(bestGui.x + 11f, bestGui.y - 10f, 170f, 20f),
                    "T+ " + ShortDelta(Math.Max(0.0, bestUT - now)), stCaption);
                GUI.color = old;

                if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
                {
                    Vector2 guiMouse = Event.current.mousePosition;
                    if (!(windowOpen && windowRect.Contains(guiMouse)))
                    {
                        forecastUT = bestUT;
                        pickMode = false;   // one-shot: hand the map back to the camera
                        InputLockManager.RemoveControlLock(PickLockId);
                        ScreenMessages.PostScreenMessage(
                            "Forecast set to T+ " + ShortDelta(Math.Max(0.0, bestUT - now)),
                            3f, ScreenMessageStyle.UPPER_CENTER);
                        Event.current.Use();
                    }
                }
            }
        }

        void SampleRange(Orbit o, double now, out double s, out double e)
        {
            s = o.StartUT;
            e = o.EndUT;
            if (double.IsNaN(s) || double.IsInfinity(s) || s < now) s = now;
            double p = o.period;
            if (double.IsNaN(e) || double.IsInfinity(e) || e <= s)
                e = (p > 0.0 && !double.IsInfinity(p) && !double.IsNaN(p)) ? s + p : s + 100.0 * DayLen;
            double cap = s + 50.0 * YearLen;
            if (e > cap) e = cap;
        }

        // ------------------------------------------------------------- window

        void DrawWindow(int id)
        {
            double now = Planetarium.GetUniversalTime();
            double dt = forecastUT - now;

            GUILayout.BeginVertical();

            GUILayout.BeginHorizontal();
            GUILayout.Label("FORECAST TIME", stHeader);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("✕", stMini, GUILayout.Width(26f))) { CloseWindow(); }
            GUILayout.EndHorizontal();

            GUILayout.Label(KSPUtil.PrintDate(forecastUT, true, false), stBig);
            GUILayout.Label((dt < 0.0 ? "T- " : "T+ ") + ShortDelta(Math.Abs(dt)), stCaption);

            // D / H / M / S offset entry.
            bool editing = IsOffsetFocused();
            if (!editing) RecomputeOffsetStrings(now);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Ahead", stCaption, GUILayout.Width(40f));
            dStr = OffsetField("ofD", dStr, "d");
            hStr = OffsetField("ofH", hStr, "h");
            mStr = OffsetField("ofM", mStr, "m");
            sStr = OffsetField("ofS", sStr, "s");
            GUILayout.EndHorizontal();
            if (editing) ApplyOffsetStrings(now);

            // Log slider + range (= slider's maximum timeframe).
            double curDays = (forecastUT - now) / DayLen;
            float maxDays = (float)(RangeYears[rangeIdx] * YearDays);
            float logMax = Mathf.Log(maxDays + 1f);
            float u = curDays <= 0.0 ? 0f : Mathf.Clamp01(Mathf.Log((float)curDays + 1f) / logMax);
            GUILayout.BeginHorizontal();
            float nu = GUILayout.HorizontalSlider(u, 0f, 1f);
            if (GUILayout.Button("max " + RangeYears[rangeIdx] + "y", stMini, GUILayout.Width(66f)))
                rangeIdx = (rangeIdx + 1) % RangeYears.Length;
            GUILayout.EndHorizontal();
            if (!editing && Mathf.Abs(nu - u) > 0.0005f)
            {
                float nd = Mathf.Exp(nu * logMax) - 1f;
                forecastUT = now + nd * DayLen;
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("-1y")) forecastUT -= YearLen;
            if (GUILayout.Button("-10d")) forecastUT -= 10.0 * DayLen;
            if (GUILayout.Button("-1d")) forecastUT -= DayLen;
            if (GUILayout.Button("Now")) forecastUT = now;
            if (GUILayout.Button("+1d")) forecastUT += DayLen;
            if (GUILayout.Button("+10d")) forecastUT += 10.0 * DayLen;
            if (GUILayout.Button("+1y")) forecastUT += YearLen;
            GUILayout.EndHorizontal();

            Vessel v = FlightGlobals.ActiveVessel;
            if (v != null && v.patchedConicSolver != null && v.patchedConicSolver.maneuverNodes.Count > 0)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Node", stCaption, GUILayout.Width(40f));
                List<ManeuverNode> nodes = v.patchedConicSolver.maneuverNodes;
                for (int i = 0; i < nodes.Count && i < 5; i++)
                    if (GUILayout.Button("#" + (i + 1))) forecastUT = nodes[i].UT;
                GUILayout.EndHorizontal();
            }

            if (v != null)
            {
                bool np = GUILayout.Toggle(pickMode,
                    pickMode ? "  Click a point on your trajectory (in map view)" : "  Pick time from orbit",
                    uiSkin.button);
                if (np != pickMode) { pickMode = np; if (!pickMode) InputLockManager.RemoveControlLock(PickLockId); }
            }

            CelestialBody origin = (v != null) ? v.mainBody : FlightGlobals.GetHomeBody();
            GUILayout.Space(6f);
            GUILayout.Label("BODIES  ·  TRANSFER FROM " + (origin != null ? origin.bodyName.ToUpper() : "?"), stHeader);
            GUILayout.Label("φ = phase now   T− = time to window   green = go", stCaption);
            GUILayout.Label("node = transfer leaving now (cheapest near T−)", stCaption);

            bodyScroll = GUILayout.BeginScrollView(bodyScroll, GUILayout.Height(235f));
            foreach (CelestialBody star in stars)
            {
                bool open;
                starOpen.TryGetValue(star.bodyName, out open);
                GUILayout.BeginHorizontal();
                open = GUILayout.Toggle(open, (open ? "▼ " : "▶ ") + star.bodyName, stFold, GUILayout.ExpandWidth(true));
                starOpen[star.bodyName] = open;
                if (GUILayout.Button("planets", stMini, GUILayout.Width(56f))) SelectSystem(star, true, true);
                if (GUILayout.Button("none", stMini, GUILayout.Width(42f))) SelectSystem(star, false, false);
                GUILayout.EndHorizontal();
                if (!open) continue;

                DrawBodyRow(star, 0, origin, now, v);
                List<BodyRow> rows;
                if (systemRows.TryGetValue(star.bodyName, out rows))
                    foreach (BodyRow r in rows) DrawBodyRow(r.body, r.indent + 1, origin, now, v);
            }
            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("All")) SetAll(true);
            if (GUILayout.Button("None")) SetAll(false);
            if (GUILayout.Button("Planets")) SelectPlanets();
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        void DrawBodyRow(CelestialBody b, int indent, CelestialBody origin, double now, Vessel v)
        {
            GUILayout.BeginHorizontal();
            if (indent > 0) GUILayout.Space(14f * indent);

            bool sel;
            show.TryGetValue(b.bodyName, out sel);
            show[b.bodyName] = GUILayout.Toggle(sel, " " + b.bodyName, GUILayout.Width(132f));

            if (origin != null && b != origin && !b.isStar)
            {
                GUILayout.FlexibleSpace();
                double cur = Ephemeris.PhaseAngle(origin, b, now);
                double tw = Ephemeris.TimeToTransferWindow(origin, b, now);
                if (double.IsNaN(tw))
                {
                    GUILayout.Label("φ " + cur.ToString("0") + "°", stDim);
                }
                else
                {
                    GUIStyle st = tw < DayLen ? stGo : (tw < 20.0 * DayLen ? stWarn : stDim);
                    GUILayout.Label("φ " + cur.ToString("0") + "°  T− " + ShortDelta(tw), st);
                }
            }

            if (v != null && b != origin && b.orbit != null)
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("node", stMini, GUILayout.Width(44f)))
                    MakeTransferNode(v, b);
            }
            GUILayout.EndHorizontal();
        }

        // Drops a transfer maneuver node for 'target' and makes it the active
        // target. For interplanetary transfers the node is placed at the correct
        // ejection point in the parking orbit (not just "now"), so a pure-prograde
        // burn sends the escape asymptote the right way.
        void MakeTransferNode(Vessel v, CelestialBody target)
        {
            if (v == null || target == null || v.orbit == null || v.patchedConicSolver == null) return;

            double now = Planetarium.GetUniversalTime();
            double nodeUT;
            Vector3d dvInt;      // full Δv in KSP's internal (orbit) frame
            string how;
            if (!PlanTransfer(v, v.mainBody, target, now, out nodeUT, out dvInt, out how) || !IsFinite(dvInt))
            {
                ScreenMessages.PostScreenMessage(
                    "Orbital Forecast: can't plan a transfer to " + target.bodyName + " from here",
                    5f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }

            PatchedConicSolver solver = v.patchedConicSolver;
            ManeuverNode node = solver.AddManeuverNode(nodeUT);

            // Express the burn in the node's own (radial, normal, prograde) axes.
            // Rather than guess KSP's normal/radial sign conventions, probe the
            // node's basis directly: GetBurnVector gives the world-space burn for
            // a unit component, so dotting the desired world Δv against each axis
            // yields the right components regardless of convention.
            Vector3d dvWorld = Swiz(dvInt);
            node.OnGizmoUpdated(new Vector3d(1.0, 0.0, 0.0), nodeUT);
            Vector3d rW = node.GetBurnVector(v.orbit).normalized;
            node.OnGizmoUpdated(new Vector3d(0.0, 1.0, 0.0), nodeUT);
            Vector3d nW = node.GetBurnVector(v.orbit).normalized;
            node.OnGizmoUpdated(new Vector3d(0.0, 0.0, 1.0), nodeUT);
            Vector3d pW = node.GetBurnVector(v.orbit).normalized;

            Vector3d comp;
            if (IsFinite(rW) && IsFinite(nW) && IsFinite(pW))
                comp = new Vector3d(Vector3d.Dot(dvWorld, rW),
                                    Vector3d.Dot(dvWorld, nW),
                                    Vector3d.Dot(dvWorld, pW));
            else
                comp = new Vector3d(0.0, 0.0, dvWorld.magnitude);   // fallback: prograde

            node.OnGizmoUpdated(comp, nodeUT);
            solver.UpdateFlightPlan();

            try { FlightGlobals.fetch.SetVesselTarget(target, true); }
            catch { try { FlightGlobals.fetch.SetVesselTarget(target); } catch { } }

            // Refine the analytic node against KSP's own patched-conic solver to
            // shrink the real closest approach — this closes the gap a single
            // analytic impulse leaves, the way a node-editor nudge would.
            double miss = double.NaN;
            try { miss = RefineNode(v, node, target, nodeUT); } catch { }

            string missTxt = (!double.IsNaN(miss) && !double.IsInfinity(miss))
                ? ", approach " + FormatDistance(miss) : "";
            ScreenMessages.PostScreenMessage(
                string.Format("Transfer node to {0}: {1:0} m/s ({2}){3} — fine-tune by dragging",
                    target.bodyName, node.DeltaV.magnitude, how, missTxt),
                7f, ScreenMessageStyle.UPPER_CENTER);
        }

        // Coordinate-descent refinement of a node's Δv (in its own axes) to
        // minimise the closest approach KSP's patched-conic solver actually
        // predicts. Returns the final miss distance (m), or +inf if it could not
        // be measured (in which case the analytic node is left untouched).
        double RefineNode(Vessel v, ManeuverNode node, CelestialBody target, double nodeUT)
        {
            PatchedConicSolver solver = v.patchedConicSolver;
            if (solver == null) return double.PositiveInfinity;

            Vector3d best = node.DeltaV;
            double bestF = EncounterMiss(solver, target, nodeUT);
            if (double.IsInfinity(bestF) || double.IsNaN(bestF)) return double.PositiveInfinity;

            double soi = target.sphereOfInfluence;
            double step = Math.Max(5.0, best.magnitude * 0.05);   // m/s
            for (int iter = 0; iter < 60 && step > 0.05; iter++)
            {
                bool improved = false;
                for (int axis = 0; axis < 3 && !improved; axis++)
                {
                    for (int s = 0; s < 2; s++)
                    {
                        double d = (s == 0) ? step : -step;
                        Vector3d trial = best;
                        if (axis == 0) trial.x += d;
                        else if (axis == 1) trial.y += d;
                        else trial.z += d;

                        node.OnGizmoUpdated(trial, nodeUT);
                        solver.UpdateFlightPlan();
                        double f = EncounterMiss(solver, target, nodeUT);
                        if (f < bestF) { bestF = f; best = trial; improved = true; break; }
                    }
                }
                if (!improved) step *= 0.5;
                if (soi > 0.0 && bestF < soi * 0.2) break;   // tight enough
            }

            node.OnGizmoUpdated(best, nodeUT);
            solver.UpdateFlightPlan();
            return bestF;
        }

        // Smallest distance between the planned trajectory and 'target', measured
        // over whichever flight-plan patch shares the target's reference body.
        // Returns 0 if a patch is already inside the target's SoI, +inf if no
        // comparable patch exists.
        double EncounterMiss(PatchedConicSolver solver, CelestialBody target, double afterUT)
        {
            double best = double.PositiveInfinity;
            if (solver == null || solver.flightPlan == null || target.orbit == null) return best;

            foreach (Orbit patch in solver.flightPlan)
            {
                if (patch == null) continue;
                if (patch.referenceBody == target) return 0.0;             // captured
                if (patch.referenceBody != target.referenceBody) continue; // different frame

                double s = patch.StartUT, e = patch.EndUT;
                if (double.IsNaN(s) || double.IsInfinity(s) || s < afterUT) s = afterUT;
                if (double.IsNaN(e) || double.IsInfinity(e) || e <= s)
                {
                    double p = target.orbit.period;
                    e = s + ((p > 0.0 && !double.IsInfinity(p) && !double.IsNaN(p)) ? p : 100.0 * DayLen);
                }
                const int N = 120;
                for (int i = 0; i <= N; i++)
                {
                    double t = s + (e - s) * (i / (double)N);
                    double dd = (patch.getPositionAtUT(t) - target.orbit.getPositionAtUT(t)).magnitude;
                    if (dd < best) best = dd;
                }
            }
            return best;
        }

        static string FormatDistance(double m)
        {
            if (m >= 1e9) return (m / 1e9).ToString("0.00") + " Gm";
            if (m >= 1e6) return (m / 1e6).ToString("0.0") + " Mm";
            if (m >= 1e3) return (m / 1e3).ToString("0") + " km";
            return m.ToString("0") + " m";
        }

        // Plans an *anytime* transfer to 'target', leaving now. A Lambert solver
        // finds the transfer orbit between the departure point now and where the
        // target will be after a flight time; a scan over flight times picks the
        // cheapest one. Near the phase window this converges on the Hohmann burn;
        // away from it, it spends extra Δv to go directly. Two regimes:
        //   • Vessel orbits the target's parent directly (moons, or a ship already
        //     in solar orbit → a planet): a direct burn, placed where the parking
        //     orbit's velocity points along the required departure velocity.
        //   • Vessel orbits a planet that orbits the target's parent
        //     (interplanetary): an ejection burn, placed at the ejection point so
        //     the hyperbolic escape asymptote matches the Lambert v-infinity.
        // Assumes a parking orbit small vs the transfer and near-coplanar with it;
        // drag the node to refine inclination / fine timing.
        bool PlanTransfer(Vessel v, CelestialBody origin, CelestialBody target,
                          double now, out double nodeUT, out Vector3d dvInt, out string how)
        {
            nodeUT = now; dvInt = Vector3d.zero; how = "";
            CelestialBody parent = target.referenceBody;
            if (parent == null || target.orbit == null || v.orbit == null) return false;

            double mus = parent.gravParameter;
            double r2 = target.orbit.semiMajorAxis;
            if (mus <= 0.0 || r2 <= 0.0) return false;

            // --- Case 1: vessel already orbits the parent directly. ---
            // Depart at 'now' from the current position; the full 3-D Δv is then
            // exactly the Lambert departure velocity minus the current velocity.
            if (origin == parent)
            {
                double r1 = v.orbit.semiMajorAxis;
                if (r1 <= 0.0) return false;
                double atH = (r1 + r2) / 2.0;
                double hoh = Math.PI * Math.Sqrt(atH * atH * atH / mus);
                Vector3d rDep = v.orbit.getRelativePositionAtUT(now);
                Vector3d vNow = v.orbit.getOrbitalVelocityAtUT(now);
                Vector3d nrm = Vector3d.Cross(rDep, vNow);

                double bestCost = double.MaxValue, bestTof = 0.0;
                Vector3d bestVdep = Vector3d.zero;
                for (int i = 1; i <= 160; i++)
                {
                    double tof = hoh * (0.15 + 2.85 * (i / 160.0));
                    Vector3d rArr = target.orbit.getRelativePositionAtUT(now + tof);
                    Vector3d vd, va;
                    if (!Lambert(rDep, rArr, tof, mus, nrm, out vd, out va)) continue;
                    double cost = (vd - vNow).magnitude;    // actual burn cost
                    if (cost < bestCost) { bestCost = cost; bestTof = tof; bestVdep = vd; }
                }
                if (bestTof <= 0.0 || bestVdep.magnitude <= 0.0) return false;

                nodeUT = now;
                dvInt = bestVdep - vNow;      // full vector, inclination included
                how = "direct, " + TofLabel(bestTof);
                return true;
            }

            // --- Case 2: interplanetary ejection from a planet. ---
            if (origin == null || origin.orbit == null || origin.referenceBody != parent) return false;

            double r1p = origin.orbit.semiMajorAxis;
            if (r1p <= 0.0) return false;
            double atp = (r1p + r2) / 2.0;
            double hoh2 = Math.PI * Math.Sqrt(atp * atp * atp / mus);
            Vector3d rPlanet = origin.orbit.getRelativePositionAtUT(now);
            Vector3d vPlanet = origin.orbit.getOrbitalVelocityAtUT(now);
            Vector3d planeN = Vector3d.Cross(rPlanet, vPlanet);

            // Scan flight times for the minimum v-infinity transfer leaving now,
            // then refine around the coarse minimum for a cleaner v-infinity.
            double bestVinf = double.MaxValue, bestT = 0.0;
            Vector3d bestVinfVec = Vector3d.zero;
            for (int i = 1; i <= 160; i++)
            {
                double tof = hoh2 * (0.15 + 2.85 * (i / 160.0));
                Vector3d rArr = target.orbit.getRelativePositionAtUT(now + tof);
                Vector3d vd, va;
                if (!Lambert(rPlanet, rArr, tof, mus, planeN, out vd, out va)) continue;
                Vector3d vinfVec = vd - vPlanet;
                double vinf = vinfVec.magnitude;
                if (vinf < bestVinf) { bestVinf = vinf; bestT = tof; bestVinfVec = vinfVec; }
            }
            if (bestT <= 0.0 || bestVinfVec.magnitude <= 0.0) return false;

            double span = hoh2 * 2.85 / 160.0;
            double loT = Math.Max(hoh2 * 0.05, bestT - span), hiT = bestT + span;
            for (int i = 0; i <= 80; i++)
            {
                double tof = loT + (hiT - loT) * (i / 80.0);
                Vector3d rArr = target.orbit.getRelativePositionAtUT(now + tof);
                Vector3d vd, va;
                if (!Lambert(rPlanet, rArr, tof, mus, planeN, out vd, out va)) continue;
                Vector3d vinfVec = vd - vPlanet;
                double vinf = vinfVec.magnitude;
                if (vinf < bestVinf) { bestVinf = vinf; bestT = tof; bestVinfVec = vinfVec; }
            }

            double mub = origin.gravParameter;
            if (mub <= 0.0) return false;
            double period2 = v.orbit.period;
            if (period2 <= 0.0 || double.IsInfinity(period2) || double.IsNaN(period2)) return false;

            // Build the ejection as a full 3-D escape hyperbola from the v-infinity
            // vector (both magnitude and direction). At the hyperbola's periapsis
            // the position makes the asymptote angle with the v-infinity direction:
            //   cos(nu_inf) = r_hat · uinf = -1/e.
            // The periapsis velocity is perpendicular to r, in the plane spanned by
            // r and uinf, pointing toward uinf — so the orbit's inclination to the
            // target drops out of the geometry instead of being approximated.
            Vector3d uinf = bestVinfVec / bestVinfVec.magnitude;
            double rpNow = v.orbit.getRelativePositionAtUT(now).magnitude;
            if (rpNow <= 0.0) return false;
            double eApprox = 1.0 + rpNow * bestVinf * bestVinf / mub;
            double cTarget = -1.0 / eApprox;                     // desired r_hat · uinf

            // Find the parking-orbit point matching cTarget whose periapsis
            // velocity runs prograde (an outbound ejection, not a capture).
            double bestErr = double.MaxValue, bUT2 = now;
            double bestErrAny = double.MaxValue, bUTany = now;
            for (int i = 0; i <= 2880; i++)
            {
                double t = now + period2 * (i / 2880.0);
                Vector3d rr = v.orbit.getRelativePositionAtUT(t);
                double m = rr.magnitude;
                if (m <= 0.0) continue;
                Vector3d rh = rr / m;
                double err = Math.Abs(Vector3d.Dot(rh, uinf) - cTarget);
                if (err < bestErrAny) { bestErrAny = err; bUTany = t; }

                Vector3d perp = uinf - rh * Vector3d.Dot(uinf, rh);
                if (perp.magnitude <= 1e-9) continue;
                if (Vector3d.Dot(perp, v.orbit.getOrbitalVelocityAtUT(t)) <= 0.0) continue;
                if (err < bestErr) { bestErr = err; bUT2 = t; }
            }
            if (bestErr == double.MaxValue) bUT2 = bUTany;

            Vector3d rNode = v.orbit.getRelativePositionAtUT(bUT2);
            Vector3d vNode = v.orbit.getOrbitalVelocityAtUT(bUT2);
            double rpAt = rNode.magnitude, vNodeMag = vNode.magnitude;
            if (rpAt <= 0.0 || vNodeMag <= 0.0) return false;
            Vector3d rhAt = rNode / rpAt;

            double veject = Math.Sqrt(bestVinf * bestVinf + 2.0 * mub / rpAt);
            Vector3d perpAt = uinf - rhAt * Vector3d.Dot(uinf, rhAt);
            if (perpAt.magnitude <= 1e-9) return false;
            Vector3d vejVec = (perpAt / perpAt.magnitude) * veject;   // hyperbola periapsis velocity

            dvInt = vejVec - vNode;                               // full 3-D ejection burn
            nodeUT = bUT2;
            how = "ejection, " + TofLabel(bestT);
            return true;
        }

        // Universal-variable Lambert solver (Bate/Mueller/White, zero-rev).
        // Returns departure velocity v1 and arrival velocity v2 in the same frame
        // as r1/r2, for a prograde transfer about 'planeNormal'. false if the
        // geometry is degenerate (≈180° transfer) or no solution is bracketed.
        static bool Lambert(Vector3d r1, Vector3d r2, double dt, double mu,
                            Vector3d planeNormal, out Vector3d v1, out Vector3d v2)
        {
            v1 = Vector3d.zero; v2 = Vector3d.zero;
            double R1 = r1.magnitude, R2 = r2.magnitude;
            if (R1 <= 0.0 || R2 <= 0.0 || dt <= 0.0 || mu <= 0.0) return false;

            double cosdnu = Vector3d.Dot(r1, r2) / (R1 * R2);
            if (cosdnu > 1.0) cosdnu = 1.0;
            if (cosdnu < -1.0) cosdnu = -1.0;
            double dm = (Vector3d.Dot(Vector3d.Cross(r1, r2), planeNormal) >= 0.0) ? 1.0 : -1.0;
            double A = dm * Math.Sqrt(R1 * R2 * (1.0 + cosdnu));
            if (Math.Abs(A) < 1e-9) return false;   // ≈180°, transfer plane undefined

            double zlow = -4.0 * Math.PI * Math.PI;
            double zhigh = 4.0 * Math.PI * Math.PI;
            double z = 0.0, y = 0.0;
            for (int it = 0; it < 200; it++)
            {
                z = 0.5 * (zlow + zhigh);
                double C = StumpffC(z), S = StumpffS(z);
                if (C <= 0.0) { zlow = z; continue; }
                y = R1 + R2 + A * (z * S - 1.0) / Math.Sqrt(C);
                if (y < 0.0) { zlow = z; continue; }
                double x = Math.Sqrt(y / C);
                double dtc = (x * x * x * S + A * Math.Sqrt(y)) / Math.Sqrt(mu);
                if (dtc <= dt) zlow = z; else zhigh = z;
            }

            double Cf = StumpffC(z);
            if (Cf <= 0.0) return false;
            y = R1 + R2 + A * (z * StumpffS(z) - 1.0) / Math.Sqrt(Cf);
            if (y < 0.0) return false;
            double g = A * Math.Sqrt(y / mu);
            if (Math.Abs(g) < 1e-12) return false;
            double f = 1.0 - y / R1;
            double gdot = 1.0 - y / R2;
            v1 = (r2 - f * r1) / g;
            v2 = (gdot * r2 - r1) / g;
            return true;
        }

        static double StumpffC(double z)
        {
            if (z > 1e-6) return (1.0 - Math.Cos(Math.Sqrt(z))) / z;
            if (z < -1e-6) { double s = Math.Sqrt(-z); return (Math.Cosh(s) - 1.0) / (-z); }
            return 0.5 - z / 24.0 + z * z / 720.0;
        }

        static double StumpffS(double z)
        {
            if (z > 1e-6) { double s = Math.Sqrt(z); return (s - Math.Sin(s)) / (s * s * s); }
            if (z < -1e-6) { double s = Math.Sqrt(-z); return (Math.Sinh(s) - s) / (s * s * s); }
            return 1.0 / 6.0 - z / 120.0 + z * z / 5040.0;
        }

        static string TofLabel(double seconds)
        {
            return "TOF " + KSPUtil.PrintDateDeltaCompact(Math.Max(0.0, seconds), false, false);
        }

        // Rodrigues rotation of vector 'x' about unit axis 'k' by angle 'a' (rad).
        static Vector3d Rotate(Vector3d x, Vector3d k, double a)
        {
            double c = Math.Cos(a), s = Math.Sin(a);
            return x * c + Vector3d.Cross(k, x) * s + k * (Vector3d.Dot(k, x) * (1.0 - c));
        }

        static bool Finite(Vector3 v)
        {
            return !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
                     float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
        }

        static bool IsFinite(Vector3d v)
        {
            return !(double.IsNaN(v.x) || double.IsNaN(v.y) || double.IsNaN(v.z) ||
                     double.IsInfinity(v.x) || double.IsInfinity(v.y) || double.IsInfinity(v.z));
        }

        // KSP's orbit (internal) frame → world frame: swap the y and z axes. This
        // is the same swizzle Orbit.getPositionAtUT applies internally.
        static Vector3d Swiz(Vector3d v) { return new Vector3d(v.x, v.z, v.y); }

        // --------------------------------------------------------- selection

        void SelectSystem(CelestialBody star, bool planetsOnly, bool val)
        {
            List<BodyRow> rows;
            if (!systemRows.TryGetValue(star.bodyName, out rows)) return;
            foreach (BodyRow r in rows)
            {
                if (planetsOnly && r.indent != 0) continue;
                show[r.body.bodyName] = val;
            }
            if (!planetsOnly) show[star.bodyName] = val;
        }

        void SetAll(bool val)
        {
            List<string> keys = new List<string>(show.Keys);
            foreach (string k in keys) show[k] = val;
        }

        void SelectPlanets()
        {
            foreach (CelestialBody b in FlightGlobals.Bodies)
            {
                if (b == null || b.orbit == null) continue;
                show[b.bodyName] = b.referenceBody != null && b.referenceBody.isStar;
            }
        }

        // -------------------------------------------------------- time entry

        bool IsOffsetFocused()
        {
            string f = GUI.GetNameOfFocusedControl();
            return f == "ofD" || f == "ofH" || f == "ofM" || f == "ofS";
        }

        string OffsetField(string name, string val, string unit)
        {
            GUI.SetNextControlName(name);
            string r = GUILayout.TextField(val, 5, GUILayout.Width(42f));
            GUILayout.Label(unit, stCaption, GUILayout.Width(14f));
            return r;
        }

        void RecomputeOffsetStrings(double now)
        {
            double off = forecastUT - now;
            if (off < 0.0) off = 0.0;
            long days = (long)(off / DayLen); off -= days * DayLen;
            long hrs = (long)(off / HourLen); off -= hrs * HourLen;
            long mins = (long)(off / MinLen); off -= mins * MinLen;
            long secs = (long)Math.Round(off);
            dStr = days.ToString();
            hStr = hrs.ToString();
            mStr = mins.ToString();
            sStr = secs.ToString();
        }

        void ApplyOffsetStrings(double now)
        {
            double total = ParseD(dStr) * DayLen + ParseD(hStr) * HourLen + ParseD(mStr) * MinLen + ParseD(sStr);
            forecastUT = now + total;
        }

        static double ParseD(string s)
        {
            double d;
            return double.TryParse(s, out d) ? d : 0.0;
        }

        static string ShortDelta(double seconds)
        {
            if (seconds < 0.0) seconds = 0.0;
            return KSPUtil.PrintDateDeltaCompact(seconds, true, false);
        }

        // ------------------------------------------------------------ styling

        void BuildSkin()
        {
            if (uiSkin != null) return;

            texWindow = Border(new Color(0.09f, 0.11f, 0.15f, 0.97f), new Color(0.28f, 0.55f, 0.66f, 1f));
            texPanel = Solid(new Color(0.12f, 0.14f, 0.18f, 1f));
            texBtn = Solid(new Color(0.17f, 0.20f, 0.26f, 1f));
            texBtnHover = Solid(new Color(0.24f, 0.29f, 0.37f, 1f));
            texBtnActive = Solid(new Color(0.22f, 0.47f, 0.60f, 1f));
            texBtnOn = Solid(new Color(0.18f, 0.44f, 0.57f, 1f));
            texScroll = Solid(new Color(0.07f, 0.08f, 0.11f, 1f));
            texThumb = Solid(Accent);
            texBar = Solid(new Color(0.25f, 0.28f, 0.34f, 1f));

            uiSkin = (GUISkin)Instantiate(GUI.skin);

            uiSkin.window.normal.background = texWindow;
            uiSkin.window.onNormal.background = texWindow;
            uiSkin.window.border = new RectOffset(4, 4, 4, 4);
            uiSkin.window.padding = new RectOffset(12, 12, 22, 12);
            uiSkin.window.normal.textColor = Accent;
            uiSkin.window.onNormal.textColor = Accent;
            uiSkin.window.fontStyle = FontStyle.Bold;
            uiSkin.window.alignment = TextAnchor.UpperCenter;

            StyleButton(uiSkin.button);
            uiSkin.label.normal.textColor = TextCol;
            uiSkin.label.wordWrap = false;
            uiSkin.toggle.normal.textColor = TextCol;
            uiSkin.toggle.onNormal.textColor = Accent;
            uiSkin.toggle.hover.textColor = Color.white;
            uiSkin.toggle.onHover.textColor = Accent;
            uiSkin.box.normal.background = texPanel;
            uiSkin.scrollView.normal.background = texScroll;
            uiSkin.horizontalSlider.normal.background = texBar;
            uiSkin.horizontalSlider.border = new RectOffset(2, 2, 2, 2);
            uiSkin.horizontalSliderThumb.normal.background = texThumb;
            uiSkin.horizontalSliderThumb.border = new RectOffset(2, 2, 2, 2);

            stHeader = new GUIStyle(uiSkin.label);
            stHeader.normal.textColor = Accent;
            stHeader.fontStyle = FontStyle.Bold;
            stHeader.fontSize = 13;

            stCaption = new GUIStyle(uiSkin.label);
            stCaption.normal.textColor = Subtle;
            stCaption.fontSize = 10;

            stBig = new GUIStyle(uiSkin.label);
            stBig.normal.textColor = TextCol;
            stBig.fontStyle = FontStyle.Bold;
            stBig.fontSize = 15;

            stGo = new GUIStyle(uiSkin.label); stGo.normal.textColor = Go; stGo.fontStyle = FontStyle.Bold; stGo.alignment = TextAnchor.MiddleRight;
            stWarn = new GUIStyle(uiSkin.label); stWarn.normal.textColor = Warn; stWarn.alignment = TextAnchor.MiddleRight;
            stDim = new GUIStyle(uiSkin.label); stDim.normal.textColor = Subtle; stDim.alignment = TextAnchor.MiddleRight;

            stMini = new GUIStyle(uiSkin.button);
            StyleButton(stMini);
            stMini.fontSize = 10;
            stMini.padding = new RectOffset(5, 5, 2, 2);
            stMini.margin = new RectOffset(2, 2, 2, 2);

            stFold = new GUIStyle(uiSkin.button);
            StyleButton(stFold);
            stFold.alignment = TextAnchor.MiddleLeft;
            stFold.fontStyle = FontStyle.Bold;
            stFold.normal.textColor = Accent;
            stFold.hover.textColor = Color.white;
        }

        void StyleButton(GUIStyle b)
        {
            b.normal.background = texBtn;
            b.hover.background = texBtnHover;
            b.active.background = texBtnActive;
            b.onNormal.background = texBtnOn;
            b.onHover.background = texBtnHover;
            b.focused.background = texBtn;
            b.normal.textColor = TextCol;
            b.hover.textColor = Color.white;
            b.onNormal.textColor = Color.white;
            b.border = new RectOffset(3, 3, 3, 3);
            b.padding = new RectOffset(6, 6, 4, 4);
            b.margin = new RectOffset(3, 3, 3, 3);
        }

        static Texture2D Solid(Color c)
        {
            Texture2D t = new Texture2D(4, 4, TextureFormat.ARGB32, false);
            Color[] px = new Color[16];
            for (int i = 0; i < px.Length; i++) px[i] = c;
            t.SetPixels(px);
            t.wrapMode = TextureWrapMode.Clamp;
            t.Apply();
            return t;
        }

        static Texture2D Border(Color fill, Color edge)
        {
            int s = 12;
            Texture2D t = new Texture2D(s, s, TextureFormat.ARGB32, false);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                    t.SetPixel(x, y, (x == 0 || y == 0 || x == s - 1 || y == s - 1) ? edge : fill);
            t.wrapMode = TextureWrapMode.Clamp;
            t.Apply();
            return t;
        }

        // --------------------------------------------------------------- icon

        Texture2D MakeIcon()
        {
            int size = 38;
            Texture2D t = new Texture2D(size, size, TextureFormat.ARGB32, false);
            Color clear = new Color(0f, 0f, 0f, 0f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    t.SetPixel(x, y, clear);

            float cx = size * 0.5f, cy = size * 0.5f, r = 13f;
            for (float a = 0f; a < 360f; a += 1.5f)
                Disc(t, (int)(cx + r * Mathf.Cos(a * Mathf.Deg2Rad)), (int)(cy + r * Mathf.Sin(a * Mathf.Deg2Rad)), 1, new Color(0.70f, 0.80f, 0.85f, 1f));
            Disc(t, (int)cx, (int)cy, 3, new Color(1f, 0.85f, 0.30f, 1f));
            Disc(t, (int)(cx + r), (int)cy, 3, new Color(0.40f, 0.90f, 1f, 1f));
            t.Apply();
            return t;
        }

        static void Disc(Texture2D t, int cx, int cy, int rad, Color col)
        {
            for (int y = -rad; y <= rad; y++)
                for (int x = -rad; x <= rad; x++)
                {
                    if (x * x + y * y > rad * rad) continue;
                    int px = cx + x, py = cy + y;
                    if (px >= 0 && py >= 0 && px < t.width && py < t.height) t.SetPixel(px, py, col);
                }
        }
    }
}
