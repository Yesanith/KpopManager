using System;
using System.Collections.Generic;
using System.Diagnostics;
using KpopManager.Core;
using KpopManager.Core.Systems.Generation;
using KpopManager.Editor.Generation;
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

        // Fixed widths for the attribute columns shared by the Trainees and Members tables, so
        // the two line up and the eye doesn't have to re-learn the layout between them.
        private static readonly (string Label, float Width)[] AttributeColumns =
        {
            ("Vocal", 46f), ("Rap", 42f), ("Dance", 46f), ("Stage", 46f),
            ("Visual", 46f), ("Charisma", 58f), ("Variety", 52f), ("FanConn", 54f),
            ("Song", 42f), ("Comp", 42f), ("Choreo", 48f)
        };

        // ---- Survives domain reload -------------------------------------------------------
        // The engine itself cannot: GameState is not a Unity-serialisable type. Instead the facts
        // that define a run are serialised, and the engine is rebuilt by replaying from the seed:
        // construct, regenerate the world if there was one, then re-tick to where it was.
        // Determinism makes that replay exact, so the window comes back showing the same world it
        // had before the reload.
        [SerializeField] private string _seedText = "12345";
        [SerializeField] private ulong _seed = 12345UL;
        [SerializeField] private int _weeksSimulated;
        [SerializeField] private bool _hasGame;
        [SerializeField] private bool _hasWorld;
        [SerializeField] private int _runYears = 10;
        [SerializeField] private bool[] _categoryEnabled;
        [SerializeField] private int _minSeverity = (int)LogSeverity.Debug;
        [SerializeField] private double _lastRunMs;
        [SerializeField] private int _lastRunWeeks;
        [SerializeField] private int _activeTab; // 0 = Log, 1 = World

        // ---- Rebuilt after domain reload --------------------------------------------------
        private SimEngine _engine;
        private readonly List<SimLogEntry> _visible = new List<SimLogEntry>();

        private TextField _seedField;
        private IntegerField _runYearsField;
        private Label _statusLabel;
        private Label _timingLabel;
        private ListView _logList;

        private VisualElement _logTabRoot;
        private VisualElement _worldTabRoot;
        private VisualElement _worldTreeContainer;
        private VisualElement _personDetailContainer;
        private Label _worldStatusLabel;

        [MenuItem("KpopManager/Harness")]
        public static void Open()
        {
            HarnessWindow window = GetWindow<HarnessWindow>();
            window.titleContent = new GUIContent("KM Harness");
            window.minSize = new Vector2(720f, 420f);
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
            root.style.flexGrow = 1f;

            root.Add(BuildSeedRow());
            root.Add(BuildTickRow());
            root.Add(BuildStatusRow());
            root.Add(BuildTabRow());

            _logTabRoot = new VisualElement();
            _logTabRoot.style.flexGrow = 1f;
            _logTabRoot.Add(BuildFilterRow());
            _logTabRoot.Add(BuildTickOrderFoldout());
            _logTabRoot.Add(BuildLogList());
            root.Add(_logTabRoot);

            _worldTabRoot = BuildWorldTab();
            root.Add(_worldTabRoot);

            SetActiveTab(_activeTab);
            RefreshAll();
        }

        // -----------------------------------------------------------------------------------
        // UI construction — shared / Log tab (Phase 1)
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

        private VisualElement BuildTabRow()
        {
            VisualElement row = MakeRow();
            row.style.marginTop = 4f;
            row.style.marginBottom = 4f;

            row.Add(MakeButton("Log", () => SetActiveTab(0)));
            row.Add(MakeButton("World", () => SetActiveTab(1)));

            return row;
        }

        private void SetActiveTab(int tab)
        {
            _activeTab = tab;
            if (_logTabRoot != null) _logTabRoot.style.display = tab == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (_worldTabRoot != null) _worldTabRoot.style.display = tab == 1 ? DisplayStyle.Flex : DisplayStyle.None;
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
        // UI construction — World tab (Phase 2)
        // -----------------------------------------------------------------------------------

        private VisualElement BuildWorldTab()
        {
            VisualElement root = new VisualElement();
            root.style.flexGrow = 1f;
            root.style.flexDirection = FlexDirection.Column;

            VisualElement topRow = MakeRow();
            topRow.Add(MakeButton("Generate World", GenerateWorld));

            _worldStatusLabel = new Label();
            _worldStatusLabel.style.marginLeft = 8f;
            _worldStatusLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            topRow.Add(_worldStatusLabel);
            root.Add(topRow);

            VisualElement splitRow = new VisualElement();
            splitRow.style.flexDirection = FlexDirection.Row;
            splitRow.style.flexGrow = 1f;

            ScrollView leftScroll = new ScrollView();
            leftScroll.style.flexGrow = 2f;
            leftScroll.style.borderRightWidth = 1f;
            leftScroll.style.borderRightColor = new Color(0.4f, 0.4f, 0.4f);
            leftScroll.style.paddingRight = 6f;
            _worldTreeContainer = leftScroll;
            splitRow.Add(leftScroll);

            ScrollView rightScroll = new ScrollView();
            rightScroll.style.flexGrow = 1f;
            rightScroll.style.minWidth = 240f;
            rightScroll.style.paddingLeft = 8f;
            _personDetailContainer = rightScroll;
            splitRow.Add(rightScroll);

            root.Add(splitRow);

            ShowPersonDetailPlaceholder();

            return root;
        }

        /// <summary>Rebuilds the engine from the current seed and regenerates the world for it — the
        /// safest way to guarantee "same seed → identical world" regardless of any prior ticking.</summary>
        private void GenerateWorld()
        {
            NewGame();
            if (_engine == null) return;

            if (!TryLoadWorldData(out WorldData data)) return;

            WorldGenerator.Generate(_engine.State, data);
            _hasWorld = true;

            RefreshAll();
        }

        private static bool TryLoadWorldData(out WorldData data)
        {
            try
            {
                data = ContentLoader.LoadWorldData();
                return true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[KM Harness] Failed to load world content from Assets/SimData: " + ex.Message);
                data = null;
                return false;
            }
        }

        private void RefreshWorldTab()
        {
            if (_worldTreeContainer == null) return;

            _worldTreeContainer.Clear();

            if (_engine == null || !_hasWorld)
            {
                _worldStatusLabel.text = "(no world yet — press Generate World)";
                _worldTreeContainer.Add(new Label("Press \"Generate World\" to build the starting world for this seed."));
                return;
            }

            GameState state = _engine.State;
            _worldStatusLabel.text =
                state.Centers.Count + " centers · " + state.Groups.Count + " groups · " +
                state.People.Count + " people   (seed " + _seed + ")";

            for (int i = 0; i < state.Centers.Count; i++)
            {
                _worldTreeContainer.Add(BuildCenterFoldout(state, state.Centers[i]));
            }

            _worldTreeContainer.Add(BuildIndustrySection(state));
        }

        private VisualElement BuildCenterFoldout(GameState state, ProductionCenter center)
        {
            string header = center.Name + (center.IsPlayer ? "  [PLAYER]" : "") +
                "   ·   " + center.Tier + "   ·   " + center.GroupIds.Count + " group(s)";
            if (center.TraineeIds.Count > 0) header += "   ·   " + center.TraineeIds.Count + " trainees";

            Foldout foldout = new Foldout { text = header, value = center.IsPlayer };
            foldout.style.marginBottom = 4f;

            if (center.TraineeIds.Count > 0)
            {
                Foldout traineesFoldout = new Foldout { text = "Trainees (" + center.TraineeIds.Count + ")", value = true };
                traineesFoldout.style.marginLeft = 12f;
                traineesFoldout.Add(BuildTraineesTable(state, center));
                foldout.Add(traineesFoldout);
            }

            for (int i = 0; i < center.GroupIds.Count; i++)
            {
                Group group = state.GetGroup(center.GroupIds[i]);
                if (group == null) continue;

                Foldout groupFoldout = new Foldout
                {
                    text = group.Name + "   ·   " + group.Tier + "   ·   " + group.Gender +
                        "   ·   debuted " + group.DebutDate + "   ·   fandom " + group.Fandom.Size.ToString("N0"),
                    value = false
                };
                groupFoldout.style.marginLeft = 12f;
                groupFoldout.Add(BuildMembersTable(state, group));
                foldout.Add(groupFoldout);
            }

            return foldout;
        }

        /// <summary>The visible attribute categories only (Performance/Star/Creative) — Hidden
        /// stats including Potential are deliberately reserved for the Person Detail panel, per
        /// DESIGN.md's own taxonomy of what "hidden" means.</summary>
        private VisualElement BuildTraineesTable(GameState state, ProductionCenter center)
        {
            VisualElement container = new VisualElement();

            List<(string Label, float Width)> header = new List<(string, float)> { ("Name", 100f), ("Age", 32f), ("Yrs", 32f) };
            header.AddRange(AttributeColumns);
            container.Add(BuildTableHeaderRow(header));

            for (int i = 0; i < center.TraineeIds.Count; i++)
            {
                Person p = state.GetPerson(center.TraineeIds[i]);
                if (p == null) continue;

                List<(string Text, float Width)> cells = new List<(string, float)>
                {
                    (p.DisplayName, 100f), (p.Age(state.Date).ToString(), 32f), (p.YearsTraining.ToString(), 32f),
                    (p.Vocal.ToString("F0"), AttributeColumns[0].Width), (p.Rap.ToString("F0"), AttributeColumns[1].Width),
                    (p.Dance.ToString("F0"), AttributeColumns[2].Width), (p.StagePresence.ToString("F0"), AttributeColumns[3].Width),
                    (p.Visual.ToString("F0"), AttributeColumns[4].Width), (p.Charisma.ToString("F0"), AttributeColumns[5].Width),
                    (p.Variety.ToString("F0"), AttributeColumns[6].Width), (p.FanConnection.ToString("F0"), AttributeColumns[7].Width),
                    (p.Songwriting.ToString("F0"), AttributeColumns[8].Width), (p.Composition.ToString("F0"), AttributeColumns[9].Width),
                    (p.Choreography.ToString("F0"), AttributeColumns[10].Width)
                };

                Person captured = p;
                container.Add(BuildClickableTableRow(cells, () => ShowPersonDetail(captured)));
            }

            return container;
        }

        private VisualElement BuildMembersTable(GameState state, Group group)
        {
            VisualElement container = new VisualElement();

            List<(string Label, float Width)> header = new List<(string, float)> { ("Name", 90f), ("Role", 140f), ("Age", 32f) };
            header.AddRange(AttributeColumns);
            container.Add(BuildTableHeaderRow(header));

            for (int i = 0; i < group.MemberIds.Count; i++)
            {
                Person p = state.GetPerson(group.MemberIds[i]);
                if (p == null) continue;

                List<(string Text, float Width)> cells = new List<(string, float)>
                {
                    (p.DisplayName, 90f), (string.Join("/", p.Positions), 140f), (p.Age(state.Date).ToString(), 32f),
                    (p.Vocal.ToString("F0"), AttributeColumns[0].Width), (p.Rap.ToString("F0"), AttributeColumns[1].Width),
                    (p.Dance.ToString("F0"), AttributeColumns[2].Width), (p.StagePresence.ToString("F0"), AttributeColumns[3].Width),
                    (p.Visual.ToString("F0"), AttributeColumns[4].Width), (p.Charisma.ToString("F0"), AttributeColumns[5].Width),
                    (p.Variety.ToString("F0"), AttributeColumns[6].Width), (p.FanConnection.ToString("F0"), AttributeColumns[7].Width),
                    (p.Songwriting.ToString("F0"), AttributeColumns[8].Width), (p.Composition.ToString("F0"), AttributeColumns[9].Width),
                    (p.Choreography.ToString("F0"), AttributeColumns[10].Width)
                };

                Person captured = p;
                container.Add(BuildClickableTableRow(cells, () => ShowPersonDetail(captured)));
            }

            return container;
        }

        /// <summary>Every group in the game, tier descending (Legendary first) then fandom size
        /// descending within a tier.</summary>
        private VisualElement BuildIndustrySection(GameState state)
        {
            Foldout foldout = new Foldout { text = "Industry — all " + state.Groups.Count + " groups", value = true };

            List<Group> sorted = new List<Group>(state.Groups);
            sorted.Sort((a, b) =>
            {
                int tierCompare = b.Tier.CompareTo(a.Tier);
                return tierCompare != 0 ? tierCompare : b.Fandom.Size.CompareTo(a.Fandom.Size);
            });

            List<(string Label, float Width)> header = new List<(string, float)>
            {
                ("Group", 140f), ("Tier", 90f), ("Center", 170f), ("Debut", 70f), ("Members", 60f), ("Fandom Size", 90f)
            };
            foldout.Add(BuildTableHeaderRow(header));

            for (int i = 0; i < sorted.Count; i++)
            {
                Group g = sorted[i];
                ProductionCenter center = state.GetCenter(g.CenterId);

                List<(string Text, float Width)> cells = new List<(string, float)>
                {
                    (g.Name, 140f), (g.Tier.ToString(), 90f), (center != null ? center.Name : "?", 170f),
                    (g.DebutDate.ToString(), 70f), (g.MemberIds.Count.ToString(), 60f), (g.Fandom.Size.ToString("N0"), 90f)
                };

                Group captured = g;
                foldout.Add(BuildClickableTableRow(cells, () => ShowGroupDetail(captured, state)));
            }

            return foldout;
        }

        private static VisualElement BuildTableHeaderRow(List<(string Label, float Width)> cells)
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.borderBottomWidth = 1f;
            row.style.borderBottomColor = new Color(0.45f, 0.45f, 0.45f);
            row.style.marginBottom = 2f;
            row.style.paddingBottom = 2f;

            for (int i = 0; i < cells.Count; i++)
            {
                Label label = new Label(cells[i].Label);
                label.style.width = cells[i].Width;
                label.style.fontSize = 10f;
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
                row.Add(label);
            }

            return row;
        }

        private static VisualElement BuildTableRowPlain(List<(string Text, float Width)> cells)
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.paddingTop = 1f;
            row.style.paddingBottom = 1f;

            for (int i = 0; i < cells.Count; i++)
            {
                Label label = new Label(cells[i].Text);
                label.style.width = cells[i].Width;
                label.style.fontSize = 10f;
                label.style.overflow = Overflow.Hidden;
                row.Add(label);
            }

            return row;
        }

        private static VisualElement BuildClickableTableRow(List<(string Text, float Width)> cells, Action onClick)
        {
            VisualElement row = BuildTableRowPlain(cells);
            row.RegisterCallback<ClickEvent>(_ => onClick());
            row.RegisterCallback<MouseEnterEvent>(_ => row.style.backgroundColor = new Color(0.5f, 0.5f, 0.5f, 0.2f));
            row.RegisterCallback<MouseLeaveEvent>(_ => row.style.backgroundColor = new Color(0f, 0f, 0f, 0f));
            return row;
        }

        private void ShowPersonDetailPlaceholder()
        {
            if (_personDetailContainer == null) return;
            _personDetailContainer.Clear();
            _personDetailContainer.Add(new Label("Click a person to see their full attribute block, including Potential."));
        }

        private void ShowPersonDetail(Person person)
        {
            if (_personDetailContainer == null || _engine == null) return;
            if (person == null)
            {
                ShowPersonDetailPlaceholder();
                return;
            }

            _personDetailContainer.Clear();
            SimDate now = _engine.State.Date;

            AddDetailTitle(person.DisplayName);
            _personDetailContainer.Add(new Label(person.FullName));
            _personDetailContainer.Add(new Label(person.Nationality + " " + person.Gender +
                " · age " + person.Age(now) + " (b. " + person.BirthYear + ")"));
            _personDetailContainer.Add(new Label("Status: " + person.Status));
            _personDetailContainer.Add(new Label("Positions: " + string.Join(", ", person.Positions)));

            AddDetailSection("Performance");
            AddDetailStat("Vocal", person.Vocal);
            AddDetailStat("Rap", person.Rap);
            AddDetailStat("Dance", person.Dance);
            AddDetailStat("Stage Presence", person.StagePresence);

            AddDetailSection("Star");
            AddDetailStat("Visual", person.Visual);
            AddDetailStat("Charisma", person.Charisma);
            AddDetailStat("Variety", person.Variety);
            AddDetailStat("Fan Connection", person.FanConnection);

            AddDetailSection("Creative");
            AddDetailStat("Songwriting", person.Songwriting);
            AddDetailStat("Composition", person.Composition);
            AddDetailStat("Choreography", person.Choreography);

            AddDetailSection("Hidden (never shown to the player once scouting fog exists)");
            AddDetailStat("Work Ethic", person.WorkEthic);
            AddDetailStat("Mental Resilience", person.MentalResilience);
            AddDetailStat("Ambition", person.Ambition);
            AddDetailStat("Professionalism", person.Professionalism);
            AddDetailStat("Potential", person.Potential, highlight: true);

            AddDetailSection("Dynamic");
            AddDetailStat("Morale", person.Morale);
            AddDetailStat("Fatigue", person.Fatigue);
            AddDetailStat("Health", person.Health);
            AddDetailStat("In-Group Popularity", person.InGroupPopularity);

            AddDetailSection("Trainee");
            _personDetailContainer.Add(new Label("Years Training: " + person.YearsTraining));
            _personDetailContainer.Add(new Label("Joined: " + person.JoinedDate));

            AddDetailSection("Language proficiency");
            foreach (KeyValuePair<Nationality, int> entry in person.LanguageProficiency)
            {
                _personDetailContainer.Add(new Label(entry.Key + ": " + entry.Value));
            }
        }

        private void ShowGroupDetail(Group group, GameState state)
        {
            if (_personDetailContainer == null) return;

            _personDetailContainer.Clear();
            AddDetailTitle(group.Name);

            _personDetailContainer.Add(new Label(group.Tier + " · " + group.Gender + " · " + group.MemberIds.Count + " members"));
            _personDetailContainer.Add(new Label("Debuted: " + group.DebutDate));
            _personDetailContainer.Add(new Label("Contract expiry: " + group.ContractExpiry));

            AddDetailSection("Fandom");
            _personDetailContainer.Add(new Label("Size: " + group.Fandom.Size.ToString("N0")));
            _personDetailContainer.Add(new Label("Sentiment: " + group.Fandom.Sentiment.ToString("F0")));
            _personDetailContainer.Add(new Label("Public Awareness: " + group.Fandom.PublicAwareness.ToString("F0")));

            AddDetailSection("Members (click for full detail)");
            for (int i = 0; i < group.MemberIds.Count; i++)
            {
                Person p = state.GetPerson(group.MemberIds[i]);
                if (p == null) continue;

                Person captured = p;
                Label row = new Label(p.DisplayName + " — " + string.Join("/", p.Positions));
                row.RegisterCallback<ClickEvent>(_ => ShowPersonDetail(captured));
                _personDetailContainer.Add(row);
            }
        }

        private void AddDetailTitle(string text)
        {
            Label title = new Label(text);
            title.style.fontSize = 14f;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 2f;
            _personDetailContainer.Add(title);
        }

        private void AddDetailSection(string title)
        {
            Label label = new Label(title);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginTop = 8f;
            label.style.color = new Color(0.55f, 0.72f, 1f);
            _personDetailContainer.Add(label);
        }

        private void AddDetailStat(string label, float value, bool highlight = false)
        {
            Label row = new Label(label + ": " + value.ToString("F0"));
            if (highlight)
            {
                row.style.unityFontStyleAndWeight = FontStyle.Bold;
                row.style.color = new Color(1f, 0.82f, 0.35f);
            }
            _personDetailContainer.Add(row);
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
        /// Rebuilds the engine after a domain reload by replaying the run from its seed: build,
        /// regenerate the world if there was one, then re-tick to where it was. Exact, because the
        /// sim is deterministic, and fast enough that even a 50-year run is unnoticeable.
        /// </summary>
        private void EnsureEngine()
        {
            if (_engine != null || !_hasGame) return;

            _engine = new SimEngine(_seed);

            if (_hasWorld)
            {
                if (TryLoadWorldData(out WorldData data))
                {
                    WorldGenerator.Generate(_engine.State, data);
                }
                else
                {
                    _hasWorld = false;
                }
            }

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
            _hasWorld = false;
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

        /// <summary>Re-filters the log, rebuilds the world tab, and repaints every readout.</summary>
        private void RefreshAll()
        {
            RefreshLog();
            RefreshWorldTab();
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
