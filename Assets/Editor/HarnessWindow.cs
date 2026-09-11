using System;
using System.Collections.Generic;
using System.Diagnostics;
using KpopManager.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace KpopManager.Editor
{
    /// <summary>
    /// The developer's interface to the simulation until Phase 7 builds a real one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Drives a <see cref="SimEngine"/>: seed it, tick it, and read the <see cref="SimLog"/> it
    /// produces. This window only ever reads simulation state; the one-way dependency rule holds
    /// here exactly as it will in the runtime UI.
    /// </para>
    /// <para>
    /// Built with UI Toolkit rather than IMGUI, both because the log needs a virtualised
    /// <see cref="ListView"/> to stay responsive after a 50-year run, and because it is the API
    /// Phase 7 uses.
    /// </para>
    /// </remarks>
    public sealed class HarnessWindow : EditorWindow
    {
        private const int CategoryCount = 8;

        // ---- Survives domain reload -------------------------------------------------------
        // The engine itself cannot: GameState is not a Unity-serialisable type. Instead the two
        // facts that define a run are serialised, and the engine is rebuilt by replaying from the
        // seed. Determinism makes that replay exact, so the window comes back showing the same
        // world it had before the reload.
        [SerializeField] private string _seedText = "12345";
        [SerializeField] private ulong _seed = 12345UL;
        [SerializeField] private int _weeksSimulated;
        [SerializeField] private bool _hasGame;
        [SerializeField] private int _runYears = 10;
        [SerializeField] private bool[] _categoryEnabled;
        [SerializeField] private int _minSeverity = (int)LogSeverity.Debug;
        [SerializeField] private double _lastRunMs;
        [SerializeField] private int _lastRunWeeks;

        // ---- Rebuilt after domain reload --------------------------------------------------
        private SimEngine _engine;
        private readonly List<SimLogEntry> _visible = new List<SimLogEntry>();

        private TextField _seedField;
        private IntegerField _runYearsField;
        private Label _statusLabel;
        private Label _timingLabel;
        private ListView _logList;

        [MenuItem("KpopManager/Harness")]
        public static void Open()
        {
            HarnessWindow window = GetWindow<HarnessWindow>();
            window.titleContent = new GUIContent("KM Harness");
            window.minSize = new Vector2(560f, 360f);
            window.Show();
        }

        public void CreateGUI()
        {
            EnsureFilterArray();
            EnsureEngine();

            VisualElement root = rootVisualElement;
            root.style.paddingLeft = 8f;
            root.style.paddingRight = 8f;
            root.style.paddingTop = 6f;
            root.style.paddingBottom = 6f;

            root.Add(BuildSeedRow());
            root.Add(BuildTickRow());
            root.Add(BuildStatusRow());
            root.Add(BuildFilterRow());
            root.Add(BuildTickOrderFoldout());
            root.Add(BuildLogList());

            RefreshAll();
        }

        // -----------------------------------------------------------------------------------
        // UI construction
        // -----------------------------------------------------------------------------------

        private VisualElement BuildSeedRow()
        {
            VisualElement row = MakeRow();

            _seedField = new TextField("Seed") { value = _seedText };
            _seedField.style.flexGrow = 1f;
            _seedField.labelElement.style.minWidth = 40f;
            _seedField.RegisterValueChangedCallback(evt => _seedText = evt.newValue);
            row.Add(_seedField);

            row.Add(MakeButton("Random Seed", () =>
            {
                // The one place a non-deterministic number is legitimate: picking the seed that
                // every later number will be derived from. Never inside Core.
                ulong seed = (ulong)UnityEngine.Random.Range(1, int.MaxValue);
                _seedText = seed.ToString();
                _seedField.SetValueWithoutNotify(_seedText);
            }));

            row.Add(MakeButton("New Game", NewGame));

            return row;
        }

        private VisualElement BuildTickRow()
        {
            VisualElement row = MakeRow();

            row.Add(MakeButton("Tick Week", () => Run(1)));
            row.Add(MakeButton("Tick Year", () => Run(SimDate.WeeksPerYear)));
            row.Add(MakeButton("Run N Years", () => Run(Math.Max(0, _runYears) * SimDate.WeeksPerYear)));

            _runYearsField = new IntegerField { value = _runYears };
            _runYearsField.style.width = 56f;
            _runYearsField.RegisterValueChangedCallback(evt => _runYears = evt.newValue);
            row.Add(_runYearsField);

            VisualElement spacer = new VisualElement();
            spacer.style.flexGrow = 1f;
            row.Add(spacer);

            row.Add(MakeButton("Clear Log", () =>
            {
                if (_engine == null) return;
                _engine.State.Log.Clear();
                RefreshAll();
            }));

            return row;
        }

        private VisualElement BuildStatusRow()
        {
            VisualElement row = MakeRow();

            _statusLabel = new Label();
            _statusLabel.style.flexGrow = 1f;
            _statusLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            row.Add(_statusLabel);

            _timingLabel = new Label();
            _timingLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            _timingLabel.style.color = new Color(0.55f, 0.55f, 0.55f);
            row.Add(_timingLabel);

            return row;
        }

        private VisualElement BuildFilterRow()
        {
            VisualElement row = MakeRow();
            row.style.flexWrap = Wrap.Wrap;

            for (int i = 0; i < CategoryCount; i++)
            {
                int index = i;
                Toggle toggle = new Toggle(((LogCategory)i).ToString()) { value = _categoryEnabled[i] };
                toggle.style.marginRight = 10f;
                toggle.RegisterValueChangedCallback(evt =>
                {
                    _categoryEnabled[index] = evt.newValue;
                    RefreshLog();
                });
                row.Add(toggle);
            }

            List<string> severities = new List<string>
            {
                LogSeverity.Debug.ToString(),
                LogSeverity.Info.ToString(),
                LogSeverity.Notable.ToString(),
                LogSeverity.Major.ToString()
            };

            DropdownField severityField = new DropdownField("Min severity", severities, Mathf.Clamp(_minSeverity, 0, 3));
            severityField.style.minWidth = 200f;
            severityField.RegisterValueChangedCallback(evt =>
            {
                _minSeverity = severities.IndexOf(evt.newValue);
                RefreshLog();
            });
            row.Add(severityField);

            return row;
        }

        /// <summary>
        /// Shows the registered tick order. Collapsed by default; it is here so the spine of the
        /// project can be eyeballed against ARCHITECTURE.md without reading code.
        /// </summary>
        private VisualElement BuildTickOrderFoldout()
        {
            Foldout foldout = new Foldout { text = "Tick order", value = false };
            foldout.style.marginBottom = 4f;

            if (_engine != null)
            {
                for (int i = 0; i < _engine.Systems.Count; i++)
                {
                    Label label = new Label((i + 1) + ". " + _engine.Systems[i].Name);
                    label.style.marginLeft = 12f;
                    label.style.color = new Color(0.6f, 0.6f, 0.6f);
                    foldout.Add(label);
                }
            }
            else
            {
                foldout.Add(new Label("  (start a game to see the registered systems)"));
            }

            return foldout;
        }

        private VisualElement BuildLogList()
        {
            _logList = new ListView
            {
                itemsSource = _visible,
                fixedItemHeight = 18f,
                selectionType = SelectionType.Single,
                showBorder = true,
                makeItem = () =>
                {
                    Label label = new Label();
                    label.style.paddingLeft = 4f;
                    label.style.unityTextAlign = TextAnchor.MiddleLeft;
                    label.style.overflow = Overflow.Hidden;
                    return label;
                },
                bindItem = (element, index) =>
                {
                    SimLogEntry entry = _visible[index];
                    Label label = (Label)element;
                    label.text = entry.Date + "  [" + entry.Category + "]  " + entry.Message;
                    label.style.color = ColorFor(entry.Severity);
                }
            };

            _logList.style.flexGrow = 1f;
            _logList.style.marginTop = 4f;

            return _logList;
        }

        private static VisualElement MakeRow()
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 4f;
            return row;
        }

        private static Button MakeButton(string text, Action action)
        {
            Button button = new Button(action) { text = text };
            button.style.marginLeft = 4f;
            return button;
        }

        /// <summary>Severity read as colour, so a 50-year log can be skimmed rather than read.</summary>
        private static Color ColorFor(LogSeverity severity)
        {
            bool pro = EditorGUIUtility.isProSkin;

            switch (severity)
            {
                case LogSeverity.Debug:
                    return pro ? new Color(0.45f, 0.45f, 0.45f) : new Color(0.55f, 0.55f, 0.55f);
                case LogSeverity.Notable:
                    return pro ? new Color(0.55f, 0.80f, 1.00f) : new Color(0.10f, 0.35f, 0.65f);
                case LogSeverity.Major:
                    return pro ? new Color(1.00f, 0.78f, 0.35f) : new Color(0.65f, 0.40f, 0.00f);
                default:
                    return pro ? new Color(0.80f, 0.80f, 0.80f) : new Color(0.15f, 0.15f, 0.15f);
            }
        }

        // -----------------------------------------------------------------------------------
        // Engine control
        // -----------------------------------------------------------------------------------

        private void EnsureFilterArray()
        {
            if (_categoryEnabled != null && _categoryEnabled.Length == CategoryCount) return;

            _categoryEnabled = new bool[CategoryCount];
            for (int i = 0; i < CategoryCount; i++)
            {
                _categoryEnabled[i] = true;
            }
        }

        /// <summary>
        /// Rebuilds the engine after a domain reload by replaying the run from its seed. Exact,
        /// because the sim is deterministic, and fast enough that even a 50-year run is unnoticeable.
        /// </summary>
        private void EnsureEngine()
        {
            if (_engine != null || !_hasGame) return;

            _engine = new SimEngine(_seed);
            if (_weeksSimulated > 0)
            {
                _engine.AdvanceWeeks(_weeksSimulated);
            }
        }

        private void NewGame()
        {
            if (!ulong.TryParse(_seedText, out ulong seed))
            {
                UnityEngine.Debug.LogWarning("[KM Harness] Seed must be a non-negative whole number. Keeping " + _seed + ".");
                _seedText = _seed.ToString();
                _seedField.SetValueWithoutNotify(_seedText);
                return;
            }

            _seed = seed;
            _weeksSimulated = 0;
            _hasGame = true;
            _lastRunMs = 0d;
            _lastRunWeeks = 0;
            _engine = new SimEngine(_seed);

            RefreshAll();
        }

        private void Run(int weeks)
        {
            if (weeks <= 0) return;

            if (_engine == null)
            {
                NewGame();
                if (_engine == null) return;
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            _engine.AdvanceWeeks(weeks);
            stopwatch.Stop();

            _lastRunMs = stopwatch.Elapsed.TotalMilliseconds;
            _lastRunWeeks = weeks;
            _weeksSimulated = _engine.WeeksElapsed;

            RefreshAll();
        }

        // -----------------------------------------------------------------------------------
        // Refresh
        // -----------------------------------------------------------------------------------

        /// <summary>Re-filters the log and repaints every readout. Cheap enough to call on any change.</summary>
        private void RefreshAll()
        {
            RefreshLog();
        }

        private void RefreshStatus()
        {
            if (_statusLabel == null) return;

            if (_engine == null)
            {
                _statusLabel.text = "No game. Set a seed and press New Game.";
                _timingLabel.text = string.Empty;
                return;
            }

            _statusLabel.text =
                "Seed " + _seed +
                "   ·   " + _engine.State.Date +
                "   ·   week " + _engine.WeeksElapsed +
                "   ·   " + _engine.State.Log.Count + " entries" +
                "   ·   showing " + _visible.Count;

            if (_lastRunWeeks > 0)
            {
                double perWeek = _lastRunMs / _lastRunWeeks;
                _timingLabel.text =
                    "last run: " + _lastRunWeeks + " weeks in " + _lastRunMs.ToString("F2") + " ms" +
                    " (" + perWeek.ToString("F4") + " ms/week)";
            }
            else
            {
                _timingLabel.text = string.Empty;
            }
        }

        /// <summary>Rebuilds the filtered view, then refreshes the status line that reports its size.</summary>
        private void RefreshLog()
        {
            _visible.Clear();

            if (_engine != null)
            {
                IReadOnlyList<SimLogEntry> entries = _engine.State.Log.Entries;
                for (int i = 0; i < entries.Count; i++)
                {
                    SimLogEntry entry = entries[i];
                    if ((int)entry.Severity < _minSeverity) continue;
                    if (!_categoryEnabled[(int)entry.Category]) continue;

                    _visible.Add(entry);
                }
            }

            if (_logList != null)
            {
                _logList.RefreshItems();

                // Newest at the bottom, so land the view there the way a console would.
                if (_visible.Count > 0)
                {
                    _logList.ScrollToItem(_visible.Count - 1);
                }
            }

            RefreshStatus();
        }
    }
}
