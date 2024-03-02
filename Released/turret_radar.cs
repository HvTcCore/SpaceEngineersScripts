
#region Turret Based Radar
/*
/ //// / Whip's Turret Based Radar Systems / //// /

HOW DO I USE THIS?

1. Place this script in a programmable block.
2. Place some turrets on your ship.
3. Place a seat on your ship.
4. Place some text panels with "Radar" in their name somewhere.
5. Enjoy!




=================================================
    DO NOT MODIFY VARIABLES IN THE SCRIPT!

 USE THE CUSTOM DATA OF THIS PROGRAMMABLE BLOCK!
=================================================


























HEY! DONT EVEN THINK ABOUT TOUCHING BELOW THIS LINE!

*/

#region Fields
const string Version = "35.6.3";
const string Date = "2024/03/01";
const string IgcTag = "IGC_IFF_MSG";
const string IgcPacketTag = "IGC_IFF_PKT"; // For packets of IFF messages

MyIni _ini = new MyIni();
MyIni _textSurfaceIni = new MyIni();

const string IniSectionGeneral = "Radar - General";
const string IniSectionColors = "Radar - Colors";
const string IniSectionTextSurface = "Radar - Text Surface Config";
const string IniTextSurfaceTemplate = "Show on screen {0}";
const string IniSectionMultiscreen = "Radar - Multiscreen Config";

ConfigSection[] _generalConfig;

ConfigSection 
    _generalSection = new ConfigSection(IniSectionGeneral),
    _colorSection = new ConfigSection(IniSectionColors, " Colors are defined with R,G,B,A color codes where\n values can range from 0,0,0,0 [transparent] to 255,255,255,255 [white].");

ConfigString
    _textPanelName,
    _referenceName;
ConfigBool
    _broadcastIFF,
    _networkTargets,
    _drawRunningScreen;
ConfigFloat
    _fadeOutInterval;
ConfigDeprecated<bool, ConfigBool>
    _deprecatedUseRangeOverride;
ConfigDeprecated<float, ConfigFloat>
    _deprecatedRangeOverride;
ConfigNullable<float, ConfigFloat>
    _rangeOverride;
ConfigColor
    _enemyIconColor,
    _enemyElevationColor,
    _neutralIconColor,
    _neutralElevationColor,
    _allyIconColor,
    _allyElevationColor,
    _asteroidColor,
    _asteroidElevationColor;
ConfigInt
    _rows,
    _cols;

IMyBroadcastListener _broadcastListener;
IMyBroadcastListener _broadcastListenerPacket;

float MaxRange
{
    get
    {
        return Math.Max(1, _rangeOverride.HasValue ? _rangeOverride.Value : _turretMaxRange);
    }
}

List<IMyShipController> Controllers
{
    get
    {
        return _taggedControllers.Count > 0 ? _taggedControllers : _allControllers;
    }
}

float _turretMaxRange = 800f;

Scheduler _scheduler;
RuntimeTracker _runtimeTracker;
ScheduledAction _grabBlockAction;

Dictionary<long, TargetData> _targetDataDict = new Dictionary<long, TargetData>();
Dictionary<long, TargetData> _broadcastDict = new Dictionary<long, TargetData>();
List<TurretInterface> _turrets = new List<TurretInterface>();
List<IMySensorBlock> _sensors = new List<IMySensorBlock>();
List<ISpriteSurface> _surfaces = new List<ISpriteSurface>();
List<IMyShipController> _taggedControllers = new List<IMyShipController>();
List<IMyShipController> _allControllers = new List<IMyShipController>();
HashSet<long> _myGridIds = new HashSet<long>();
HashSet<long> _connectedGridIds = new HashSet<long>();
IMyTerminalBlock _reference;
IMyShipController _lastActiveShipController = null;

const double _cycleTime = 1.0 / 60.0;
string _lastSetupResult = "";
bool _isSetup = false;
bool _clearSpriteCache = false;

readonly CompositeBoundingSphere _compositeBoundingSphere;
readonly RadarSurface _radarSurface;
readonly MyCommandLine _commandLine = new MyCommandLine();
readonly RadarRunningScreenManager _runningScreenManager;
#endregion

#region Main Routine
Program()
{
    _radarSurface = new RadarSurface();

    _generalConfig = new ConfigSection[]
    {
        _generalSection,
        _colorSection,
    };

    _generalSection.AddValues(
        _deprecatedUseRangeOverride = new ConfigDeprecated<bool, ConfigBool>(new ConfigBool("Use radar range override")),
        _textPanelName = new ConfigString("Text surface name tag", "Radar"),
        _broadcastIFF = new ConfigBool("Share own position", true),
        _networkTargets = new ConfigBool("Share targets", true),
        _rangeOverride = new ConfigNullable<float, ConfigFloat>(new ConfigFloat("Radar range (m)"), "auto"),
        _radarSurface.ProjectionAngleDeg = new ConfigFloat("Radar projection angle in degrees (0 is flat)", 55f),
        _radarSurface.DrawQuadrants = new ConfigBool("Draw quadrants", true),
        _referenceName = new ConfigString("Optional reference block name", "Reference"),
        _drawRunningScreen = new ConfigBool("Draw title screen", true),
        _fadeOutInterval = new ConfigFloat("Target fadeout interval (s)", 2f),
        _radarSurface.Amogus = new ConfigBool("Amogus", false)
    );

    _colorSection.AddValues(
        _radarSurface.TitleBarColor = new ConfigColor("Title bar", new Color(100, 30, 0, 5)),
        _radarSurface.TextColor = new ConfigColor("Text", new Color(255, 100, 0, 100)),
        _radarSurface.BackColor = new ConfigColor("Background", new Color(0, 0, 0, 255)),
        _radarSurface.LineColor = new ConfigColor("Radar lines", new Color(255, 100, 0, 50)),
        _radarSurface.PlaneColor = new ConfigColor("Radar plane", new Color(100, 30, 0, 5)),
        _enemyIconColor = new ConfigColor("Enemy icon", new Color(150, 0, 0, 255)),
        _enemyElevationColor = new ConfigColor("Enemy elevation", new Color(75, 0, 0, 255)),
        _neutralIconColor = new ConfigColor("Neutral icon", new Color(150, 150, 0, 255)),
        _neutralElevationColor = new ConfigColor("Neutral elevation", new Color(75, 75, 0, 255)),
        _allyIconColor = new ConfigColor("Friendly icon", new Color(0, 50, 150, 255)),
        _allyElevationColor = new ConfigColor("Friendly elevation", new Color(0, 25, 75, 255)),
        _asteroidColor = new ConfigColor("Asteroid icon", new Color(50, 50, 50, 255)),
        _asteroidElevationColor = new ConfigColor("Asteroid elevation", new Color(25, 25, 25, 255)),
        _radarSurface.RadarLockWarningColor = new ConfigColor("Lock on warning", new Color(150, 0, 0, 255))
    );

    _deprecatedRangeOverride = new ConfigDeprecated<float, ConfigFloat>(new ConfigFloat("Radar range override (m)"));

    // If range override is true, check if the deprecated range override float is also set. If it is, set the current range override
    _deprecatedUseRangeOverride.Callback = (v) => {
        if (v)
        {
            _deprecatedRangeOverride.Update(ref _ini, IniSectionGeneral);
        }
    };

    _deprecatedRangeOverride.Callback = (v) => {
        _rangeOverride.Value = v;
    };

    _rows = new ConfigInt("Screen rows", 1);
    _cols = new ConfigInt("Screen cols", 1);

    _runningScreenManager = new RadarRunningScreenManager(Version, this);
    _compositeBoundingSphere = new CompositeBoundingSphere(this);

    ParseCustomDataIni();

    GrabBlocks();


    Runtime.UpdateFrequency = UpdateFrequency.Update1;
    _runtimeTracker = new RuntimeTracker(this);

    // Scheduler creation
    _scheduler = new Scheduler(this);
    _grabBlockAction = new ScheduledAction(GrabBlocks, 0.1);
    _scheduler.AddScheduledAction(_grabBlockAction);
    _scheduler.AddScheduledAction(UpdateRadarRange, 1);
    _scheduler.AddScheduledAction(PrintDetailedInfo, 1);
    _scheduler.AddScheduledAction(DrawRunningScreen, 6);
    _scheduler.AddScheduledAction(_runningScreenManager.RestartDraw, 1);

    _scheduler.AddQueuedAction(GetTurretTargets, _cycleTime, false);               // cycle 1
    _scheduler.AddQueuedAction(_radarSurface.SortContacts, _cycleTime, false);     // cycle 2

    float step = 1f / 8f;
    _scheduler.AddQueuedAction(() => Draw(0 * step, 1 * step), _cycleTime, false); // cycle 3
    _scheduler.AddQueuedAction(() => Draw(1 * step, 2 * step), _cycleTime, false); // cycle 4
    _scheduler.AddQueuedAction(() => Draw(2 * step, 3 * step), _cycleTime, false); // cycle 5
    _scheduler.AddQueuedAction(() => Draw(3 * step, 4 * step), _cycleTime, false); // cycle 6
    _scheduler.AddQueuedAction(() => Draw(4 * step, 5 * step), _cycleTime, false); // cycle 7
    _scheduler.AddQueuedAction(() => Draw(5 * step, 6 * step), _cycleTime, false); // cycle 8
    _scheduler.AddQueuedAction(() => Draw(6 * step, 7 * step), _cycleTime, false); // cycle 9
    _scheduler.AddQueuedAction(() => Draw(7 * step, 8 * step), _cycleTime, false); // cycle 10

    // IGC Register
    _broadcastListener = IGC.RegisterBroadcastListener(IgcTag);
    _broadcastListener.SetMessageCallback(IgcTag);
    _broadcastListenerPacket = IGC.RegisterBroadcastListener(IgcPacketTag);
    _broadcastListenerPacket.SetMessageCallback(IgcPacketTag);
}

void Main(string arg, UpdateType updateSource)
{
    _runtimeTracker.AddRuntime();

    if (_commandLine.TryParse(arg))
        HandleArguments();

    _scheduler.Update();

    if (arg.Equals(IgcTag) || arg.Equals(IgcPacketTag))
    {
        ProcessNetworkMessage();
    }

    _runtimeTracker.AddInstructions();
}

void DrawRunningScreen()
{
    if (_drawRunningScreen)
    {
        _runningScreenManager.Draw();
    }
}

void HandleArguments()
{
    int argCount = _commandLine.ArgumentCount;

    if (argCount == 0)
        return;

    switch (_commandLine.Argument(0).ToLowerInvariant())
    {
        case "range":
            if (argCount != 2)
            {
                return;
            }

            float range = 0;
            if (float.TryParse(_commandLine.Argument(1), out range))
            {
                _rangeOverride.Value = range;
                UpdateRadarRange();
            }
            else if (string.Equals(_commandLine.Argument(1), "default") || string.Equals(_commandLine.Argument(1), "auto"))
            {
                _rangeOverride.Reset();
                UpdateRadarRange();
            }
            return;

        default:
            return;
    }
}

void Draw(float startProportion, float endProportion)
{
    int start = (int)(startProportion * _surfaces.Count);
    int end = (int)(endProportion * _surfaces.Count);

    for (int i = start; i < end; ++i)
    {
        var textSurface = _surfaces[i];
        _radarSurface.DrawRadar(textSurface, _clearSpriteCache);
    }
}

void PrintDetailedInfo()
{
    Echo($"WMI Radar System Online\n(Version {Version} - {Date})");
    Echo($"\nNext refresh in {Math.Max(_grabBlockAction.RunInterval - _grabBlockAction.TimeSinceLastRun, 0):N0} seconds\n");
    Echo($"Range: {MaxRange} m");
    Echo($"Turrets: {_turrets.Count}");
    Echo($"Sensors: {_sensors.Count}");
    Echo($"Text surfaces: {_surfaces.Count}");
    Echo($"Ship radius: {_compositeBoundingSphere.Radius:n1} m");
    Echo($"Reference:\n    \"{(_reference?.CustomName)}\"");
    Echo($"{_lastSetupResult}");
    Echo(_runtimeTracker.Write());
}

void UpdateRadarRange()
{
    _turretMaxRange = GetMaxTurretRange(_turrets);
    _radarSurface.Range = MaxRange;
}
#endregion

#region IGC Comms
void ProcessDataPacket(long sourceId, TargetRelation targetRelation, long entityId, Vector3D position)
{
    TargetRelation relationship = (targetRelation & TargetRelation.RelationMask);
    TargetRelation type = (targetRelation & TargetRelation.TypeMask);
    bool targetLock = (targetRelation & TargetRelation.Locked) != 0;

    if (_myGridIds.Contains(entityId))
    {
        if (targetLock)
        {
            _radarSurface.RadarLockWarning = true;
        }
        return;
    }

    if (_connectedGridIds.Contains(entityId))
    {
        return;
    }

    bool myLock = false;
    if (targetLock && GridTerminalSystem.GetBlockWithId(sourceId) != null)
    {
        myLock = true;
    }

    TargetData targetData;
    if (_targetDataDict.TryGetValue(entityId, out targetData))
    {
        targetData.TargetLock |= targetLock;
        targetData.MyLock |= myLock;
        targetData.Type |= type;
        if (targetData.Relation < relationship)
        {
            targetData.Relation = relationship;
        }
    }
    else
    {
        targetData.Position = position;
        targetData.TargetLock = targetLock;
        targetData.MyLock = myLock;
        targetData.Relation = relationship;
        targetData.Type = type;
    }

    _targetDataDict[entityId] = targetData;
}

void ProcessNetworkMessage()
{
    while (_broadcastListener.HasPendingMessage)
    {
        var message = _broadcastListener.AcceptMessage();
        object messageData = message.Data;

        if (messageData is MyTuple<byte, long, Vector3D, byte>) // For backwards compat.
        {
            var payload = (MyTuple<byte, long, Vector3D, byte>)messageData;
            ProcessDataPacket(message.Source, (TargetRelation)payload.Item1, payload.Item2, payload.Item3);
        }
        else if (messageData is MyTuple<byte, long, Vector3D, double>) // Item4 is ignored on ingest, it is grid radius
        {
            var payload = (MyTuple<byte, long, Vector3D, double>)messageData;
            ProcessDataPacket(message.Source, (TargetRelation)payload.Item1, payload.Item2, payload.Item3);
        }
    }

    while (_broadcastListenerPacket.HasPendingMessage)
    {
        var message = _broadcastListenerPacket.AcceptMessage();
        object messageData = message.Data;

        if (messageData is ImmutableArray<MyTuple<byte, long, Vector3D, double>>)
        {
            var payloadArray = (ImmutableArray<MyTuple<byte, long, Vector3D, double>>)messageData;

            if (payloadArray.IsDefault)
            {
                continue;
            }

            foreach (var payload in payloadArray)
            {
                ProcessDataPacket(message.Source, (TargetRelation)payload.Item1, payload.Item2, payload.Item3);
            }
        }
    }
}

ImmutableArray<MyTuple<byte, long, Vector3D, double>>.Builder _messageBuilder = ImmutableArray.CreateBuilder<MyTuple<byte, long, Vector3D, double>>();
void NetworkTargets()
{
    int capacity = (_broadcastIFF ? 1 : 0) + (_networkTargets ? _broadcastDict.Count : 0);
    if (capacity == 0)
    {
        return;
    }

    _messageBuilder.Capacity = capacity;
    if (_broadcastIFF)
    {
        _compositeBoundingSphere.Compute();
        TargetRelation type = _compositeBoundingSphere.LargestGrid.GridSizeEnum == MyCubeSize.Large ? TargetRelation.LargeGrid : TargetRelation.SmallGrid;
        var myTuple = new MyTuple<byte, long, Vector3D, double>((byte)(type | TargetRelation.Friendly), _compositeBoundingSphere.LargestGrid.EntityId, _compositeBoundingSphere.Center, _compositeBoundingSphere.Radius * _compositeBoundingSphere.Radius);
        _messageBuilder.Add(myTuple);
    }

    if (_networkTargets)
    {
        foreach (var kvp in _broadcastDict)
        {
            TargetData targetData = kvp.Value;
            var myTuple = new MyTuple<byte, long, Vector3D, double>((byte)(targetData.Relation | targetData.Type), kvp.Key, targetData.Position, 0);
            _messageBuilder.Add(myTuple);
        }
    }

    IGC.SendBroadcastMessage(IgcPacketTag, _messageBuilder.MoveToImmutable());
}
#endregion

#region Sensor Detection
List<MyDetectedEntityInfo> _sensorEntities = new List<MyDetectedEntityInfo>();
void GetSensorTargets()
{
    foreach (var sensor in _sensors)
    {
        if (sensor.Closed)
            continue;

        _sensorEntities.Clear();
        sensor.DetectedEntities(_sensorEntities);
        foreach (var target in _sensorEntities)
        {
            AddTargetData(target);
        }
    }
}
#endregion

#region Add Target Info
void AddTargetData(MyDetectedEntityInfo targetInfo)
{
    TargetData targetData;
    _targetDataDict.TryGetValue(targetInfo.EntityId, out targetData);

    switch (targetInfo.Relationship)
    {
        case MyRelationsBetweenPlayerAndBlock.Owner:
        case MyRelationsBetweenPlayerAndBlock.Friends:
        case MyRelationsBetweenPlayerAndBlock.FactionShare:
            targetData.Relation |= TargetRelation.Friendly;
            break;
        case MyRelationsBetweenPlayerAndBlock.Enemies:
            targetData.Relation |= TargetRelation.Enemy;
            break;
        default:
            targetData.Relation |= TargetRelation.Neutral;
            break;
    }

    if (targetInfo.Type == MyDetectedEntityType.LargeGrid)
    {
        targetData.Type = TargetRelation.LargeGrid;
    }
    else if (targetInfo.Type == MyDetectedEntityType.SmallGrid)
    {
        targetData.Type = TargetRelation.SmallGrid;
    }
    else if (targetInfo.Type == MyDetectedEntityType.Asteroid || targetInfo.Type == MyDetectedEntityType.Planet)
    {
        targetData.Type = TargetRelation.Asteroid;
    }
    else
    {
        targetData.Type = TargetRelation.Other;
    }
    targetData.Position = targetInfo.Position;

    _targetDataDict[targetInfo.EntityId] = targetData;
    _broadcastDict[targetInfo.EntityId] = targetData;
}
#endregion

#region Turret Detection
void GetTurretTargets()
{
    if (!_isSetup) //setup error
        return;

    _broadcastDict.Clear();
    _radarSurface.ClearContacts();

    GetSensorTargets();

    foreach (var block in _turrets)
    {
        if (block.Closed)
            continue;

        if (block.HasTarget)
        {
            var target = block.GetTargetedEntity();
            AddTargetData(target);
        }
    }

    // Define reference ship controller
    _reference = GetControlledShipController(Controllers, _lastActiveShipController); // Primary, get active controller
    if (_reference == null)
    {
        if (_reference == null && Controllers.Count != 0)
        {
            // Last case, resort to the first controller in the list
            _reference = Controllers[0];
        }
        else
        {
            _reference = Me;
        }
    }

    if (_reference is IMyShipController)
    {
        _lastActiveShipController = (IMyShipController)_reference;
    }

    foreach (var kvp in _targetDataDict)
    {
        if (kvp.Key == Me.CubeGrid.EntityId)
            continue;

        var targetData = kvp.Value;

        Color targetIconColor = _enemyIconColor;
        Color targetElevationColor = _enemyElevationColor;
        
        if ((targetData.Type & TargetRelation.Asteroid) != 0)
        {
            targetIconColor = _asteroidColor;
            targetElevationColor = _asteroidElevationColor;
        }
        else
        {
            switch (targetData.Relation)
            {
                case TargetRelation.Friendly:
                    targetIconColor = _allyIconColor;
                    targetElevationColor = _allyElevationColor;
                    break;

                case TargetRelation.Neutral:
                    targetIconColor = _neutralIconColor;
                    targetElevationColor = _neutralElevationColor;
                    break;
            }
        }

        _radarSurface.AddContact(
            targetData.Position,
            _reference.WorldMatrix,
            targetIconColor,
            targetElevationColor,
            targetData.Relation,
            targetData.Type,
            targetData.TargetLock,
            targetData.MyLock,
            kvp.Key);
    }
    NetworkTargets();

    _targetDataDict.Clear();
    _radarSurface.RadarLockWarning = false;
}
#endregion

#region Target Data Struct
struct TargetData
{
    public Vector3D Position;
    public TargetRelation Relation;
    public TargetRelation Type;
    public bool TargetLock;
    public bool MyLock;
}
#endregion

#region Radar Surface
class RadarSurface
{
    float _range = 0f;
    public float Range
    {
        get
        {
            return _range;
        }
        set
        {
            if (value == _range)
                return;
            _range = value;
            _outerRange = SufixRangeWithMetricUnits(_range, "m", 2);
        }
    }
    public bool RadarLockWarning { get; set; }

    bool _immediateFadeOut = true;
    float _fadeOutInterval = 0;
    public float FadeOutInterval
    {
        get
        {
            return _fadeOutInterval;
        }
        set
        {
            _fadeOutInterval = value;
            _immediateFadeOut = _fadeOutInterval <= 1f / 6f;
        }
    }

    public readonly StringBuilder Debug = new StringBuilder();

    const string Font = "Debug";
    const string RadarWarningText = "MISSILE LOCK";
    const float TitleTextSize = 1.5f;
    const float HudTextSize = 1.3f;
    const float RangeTextSize = 1.2f;
    const float LockTextSize = 1f;
    const float TgtElevationLineWidth = 4f;
    const float RadarRangeLineWidth = 8f;
    const float QuadrantLineWidth = 4f;
    const float TitleBarHeight = 64;
    const float RadarWarningTextSize = 1.5f;
    const float SizeToPx = 28.8f;

    public ConfigColor TitleBarColor;
    public ConfigColor BackColor;
    public ConfigColor LineColor;
    public ConfigColor PlaneColor;
    public ConfigColor TextColor;
    public ConfigBool Amogus;
    public ConfigBool DrawQuadrants;
    public ConfigFloat ProjectionAngleDeg;
    public ConfigColor RadarLockWarningColor;

    Color _textBoxBackgroundColor = new Color(0, 0, 0, 220);

    float? _lastProjectionAngleDeg;
    float _radarProjectionCos;
    float _radarProjectionSin;
    bool _showRadarWarning = true;
    string _outerRange = "";
    Vector2 _quadrantLineDirection;

    readonly StringBuilder _textMeasuringSB = new StringBuilder();
    readonly Vector2 _dropShadowOffset = new Vector2(2, 2);
    readonly Vector2 _tgtIconSize = new Vector2(20f, 20f);
    readonly Vector2 _shipIconSize = new Vector2(32, 16);
    readonly Vector2 _borderPadding = new Vector2(16f, 64f);
    readonly List<TargetInfo> _targetList = new List<TargetInfo>();
    readonly List<TargetInfo> _targetsBelowPlane = new List<TargetInfo>();
    readonly List<TargetInfo> _targetsAbovePlane = new List<TargetInfo>();

    List<long> _targetDictKeys = new List<long>();
    Dictionary<long, TargetInfo> _targetDict = new Dictionary<long, TargetInfo>();

    struct TargetInfo
    {
        public Vector3 Position;
        public Color IconColor;
        public Color ElevationColor;
        public bool TargetLock;
        public bool MyTargetLock;
        public bool AbovePlane;
        public float Rotation;
        public float Age;
        public Action<ISpriteSurface, Vector2, Color, Color, float, float> DrawFunction;
    }

    public RadarSurface()
    {
        _textMeasuringSB.Append(RadarWarningText);
    }

    void UpdateProjectionAngle()
    {
        if (!_lastProjectionAngleDeg.HasValue || _lastProjectionAngleDeg != ProjectionAngleDeg)
        {
            float rads = MathHelper.ToRadians(ProjectionAngleDeg);
            _radarProjectionCos = (float)Math.Cos(rads);
            _radarProjectionSin = (float)Math.Sin(rads);

            _quadrantLineDirection = new Vector2(0.25f * MathHelper.Sqrt2, 0.25f * MathHelper.Sqrt2 * _radarProjectionCos);
        }
        _lastProjectionAngleDeg = ProjectionAngleDeg;
    }

    public void DrawRadarLockWarning(ISpriteSurface surf, Vector2 screenCenter, Vector2 screenSize, float scale)
    {
        if (!RadarLockWarning || !_showRadarWarning)
            return;

        float textSize = RadarWarningTextSize * scale;
        Vector2 textBoxSize = surf.MeasureStringInPixels(_textMeasuringSB, "Debug", textSize);
        Vector2 padding = new Vector2(48f, 24f) * scale;
        Vector2 position = screenCenter + new Vector2(0, screenSize.Y * 0.2f);
        Vector2 textPos = position;
        textPos.Y -= textBoxSize.Y * 0.5f;

        // Draw text box bg
        var textBoxBg = new MySprite(SpriteType.TEXTURE, "SquareSimple", color: _textBoxBackgroundColor, size: textBoxSize + padding);
        textBoxBg.Position = position;
        surf.Add(textBoxBg);

        // Draw text box
        var textBox = new MySprite(SpriteType.TEXTURE, "AH_TextBox", color: RadarLockWarningColor, size: textBoxSize + padding);
        textBox.Position = position;
        surf.Add(textBox);

        // Draw text
        var text = MySprite.CreateText(RadarWarningText, "Debug", RadarLockWarningColor, scale: textSize);
        text.Position = textPos;
        surf.Add(text);
    }

    public void AddContact(Vector3D worldPosition, MatrixD worldMatrix, Color iconColor, Color elevationLineColor, TargetRelation relation, TargetRelation type, bool targetLock, bool myTargetLock, long id)
    {
        var transformedDirection = Vector3D.TransformNormal(worldPosition - worldMatrix.Translation, Matrix.Transpose(worldMatrix));
        var position = new Vector3(transformedDirection.X, transformedDirection.Z, transformedDirection.Y);
        bool inRange = position.X * position.X + position.Y * position.Y < Range * Range;
        float angle = 0f;
        bool above = position.Z >= 0;

        Action<ISpriteSurface, Vector2, Color, Color, float, float> drawFunction;
        if (inRange)
        {
            if (Amogus)
            {
                drawFunction = DrawAmogus;
            }
            else if ((type & TargetRelation.Asteroid) != 0)
            {
                drawFunction = DrawHollowCircle;
            }
            else if ((type & TargetRelation.Missile) != 0)
            {
                drawFunction = DrawMissile;
            }
            else if ((type & TargetRelation.LargeGrid) != 0)
            {
                drawFunction = DrawSquare;
            }
            else if ((type & TargetRelation.SmallGrid) != 0)
            {
                drawFunction = DrawTriangle;
            }
            else
            {
                drawFunction = DrawCircle;
            }
            position /= Range;
        }
        else
        {
            Vector3 directionFlat = position;
            directionFlat.Z = 0;
            float angleOffset = position.Z > 0 ? MathHelper.Pi : 0f;
            position = Vector3.Normalize(directionFlat);
            angle = angleOffset + MathHelper.PiOver2;
            drawFunction = DrawOutOfRangeIcon;
        }

        var targetInfo = new TargetInfo()
        {
            Position = position,
            ElevationColor = elevationLineColor,
            IconColor = iconColor,
            TargetLock = targetLock,
            MyTargetLock = myTargetLock,
            Rotation = angle,
            Age = 0f,
            DrawFunction = drawFunction,
            AbovePlane = above,
        };

        _targetDict[id] = targetInfo;
    }

    public void SortContacts()
    {
        _targetsBelowPlane.Clear();
        _targetsAbovePlane.Clear();

        _targetList.Clear();
        foreach (var value in _targetDict.Values)
        {
            _targetList.Add(value);
        }

        _targetList.Sort((a, b) => (a.Position.Y).CompareTo(b.Position.Y));

        foreach (var target in _targetList)
        {
            if (target.AbovePlane)
                _targetsAbovePlane.Add(target);
            else
                _targetsBelowPlane.Add(target);
        }

        _showRadarWarning = !_showRadarWarning;
    }

    public void ClearContacts()
    {
        _targetList.Clear();
        _targetsAbovePlane.Clear();
        _targetsBelowPlane.Clear();
        if (_immediateFadeOut)
        {
            _targetDict.Clear();
        }
        else
        {
            _targetDictKeys.Clear();

            foreach (var id in _targetDict.Keys)
            {
                _targetDictKeys.Add(id);
            }
            foreach (var id in _targetDictKeys)
            {
                TargetInfo info = _targetDict[id];
                info.Age += 1f / 6f;
                if (info.Age > _fadeOutInterval)
                {
                    _targetDict.Remove(id);
                }
                else
                {
                    _targetDict[id] = info;
                }
            }
        }
    }

    /*
    Draws a box that looks like this:
     __    __
    |        |

    |__    __|
    */
    static void DrawBoxCorners(ISpriteSurface surf, Vector2 boxSize, Vector2 centerPos, float lineLength, float lineWidth, Color color)
    {
        var horizontalSize = new Vector2(lineLength, lineWidth);
        var verticalSize = new Vector2(lineWidth, lineLength);

        Vector2 horizontalOffset = 0.5f * horizontalSize;
        Vector2 verticalOffset = 0.5f * verticalSize;

        Vector2 boxHalfSize = 0.5f * boxSize;
        Vector2 boxTopLeft = centerPos - boxHalfSize;
        Vector2 boxBottomRight = centerPos + boxHalfSize;
        Vector2 boxTopRight = centerPos + new Vector2(boxHalfSize.X, -boxHalfSize.Y);
        Vector2 boxBottomLeft = centerPos + new Vector2(-boxHalfSize.X, boxHalfSize.Y);

        MySprite sprite;

        // Top left
        sprite = new MySprite(SpriteType.TEXTURE, "SquareSimple", size: horizontalSize, position: boxTopLeft + horizontalOffset, rotation: 0, color: color);
        surf.Add(sprite);

        sprite = new MySprite(SpriteType.TEXTURE, "SquareSimple", size: verticalSize, position: boxTopLeft + verticalOffset, rotation: 0, color: color);
        surf.Add(sprite);

        // Top right
        sprite = new MySprite(SpriteType.TEXTURE, "SquareSimple", size: horizontalSize, position: boxTopRight + new Vector2(-horizontalOffset.X, horizontalOffset.Y), rotation: 0, color: color);
        surf.Add(sprite);

        sprite = new MySprite(SpriteType.TEXTURE, "SquareSimple", size: verticalSize, position: boxTopRight + new Vector2(-verticalOffset.X, verticalOffset.Y), rotation: 0, color: color);
        surf.Add(sprite);

        // Bottom left
        sprite = new MySprite(SpriteType.TEXTURE, "SquareSimple", size: horizontalSize, position: boxBottomLeft + new Vector2(horizontalOffset.X, -horizontalOffset.Y), rotation: 0, color: color);
        surf.Add(sprite);

        sprite = new MySprite(SpriteType.TEXTURE, "SquareSimple", size: verticalSize, position: boxBottomLeft + new Vector2(verticalOffset.X, -verticalOffset.Y), rotation: 0, color: color);
        surf.Add(sprite);

        // Bottom right
        sprite = new MySprite(SpriteType.TEXTURE, "SquareSimple", size: horizontalSize, position: boxBottomRight - horizontalOffset, rotation: 0, color: color);
        surf.Add(sprite);

        sprite = new MySprite(SpriteType.TEXTURE, "SquareSimple", size: verticalSize, position: boxBottomRight - verticalOffset, rotation: 0, color: color);
        surf.Add(sprite);
    }

    public void DrawRadar(ISpriteSurface surf, bool clearSpriteCache)
    {
        UpdateProjectionAngle();

        surf.ScriptBackgroundColor = BackColor;

        Vector2 surfaceSize = surf.TextureSize;
        Vector2 screenCenter = surfaceSize * 0.5f;
        Vector2 viewportSize = surf.SurfaceSize;
        Vector2 scale = viewportSize / 512f;
        float minScale = Math.Min(scale.X, scale.Y);
        Vector2 viewportCropped = viewportSize - (Vector2.UnitY * (TitleBarHeight + RangeTextSize * SizeToPx) + _borderPadding) * minScale;
        float sideLength;
        if (viewportCropped.X * _radarProjectionCos < viewportCropped.Y)
        {
            sideLength = viewportCropped.X;
        }
        else
        {
            sideLength = viewportCropped.Y / _radarProjectionCos;
        }
        //float sideLength = Math.Min(viewportSize.X, viewportSize.Y - TITLE_BAR_HEIGHT * minScale);

        Vector2 radarCenterPos = screenCenter + Vector2.UnitY * ((TitleBarHeight - RangeTextSize * SizeToPx) * 0.5f * minScale);
        var radarPlaneSize = new Vector2(sideLength, sideLength * _radarProjectionCos);

        if (clearSpriteCache)
        {
            surf.Add(new MySprite());
        }

        DrawRadarPlaneBackground(surf, radarCenterPos, radarPlaneSize, minScale);

        // Bottom Icons
        foreach (var targetInfo in _targetsBelowPlane)
        {
            DrawTargetIcon(surf, radarCenterPos, radarPlaneSize, targetInfo, minScale);
        }

        // Radar plane
        DrawRadarPlane(surf, viewportSize, screenCenter, radarCenterPos, radarPlaneSize, minScale);

        // Top Icons
        foreach (var targetInfo in _targetsAbovePlane)
        {
            DrawTargetIcon(surf, radarCenterPos, radarPlaneSize, targetInfo, minScale);
        }

        DrawRadarLockWarning(surf, screenCenter, viewportSize, minScale);


        surf.Draw();
    }

    void DrawLineQuadrantSymmetry(ISpriteSurface surf, Vector2 center, Vector2 point1, Vector2 point2, float width, Color color)
    {
        DrawLine(surf, center + point1, center + point2, width, color);
        DrawLine(surf, center - point1, center - point2, width, color);
        point1.X *= -1;
        point2.X *= -1;
        DrawLine(surf, center + point1, center + point2, width, color);
        DrawLine(surf, center - point1, center - point2, width, color);
    }

    void DrawLine(ISpriteSurface surf, Vector2 point1, Vector2 point2, float width, Color color)
    {
        Vector2 position = 0.5f * (point1 + point2);
        Vector2 diff = point1 - point2;
        float length = diff.Length();
        if (length > 0)
            diff /= length;

        var size = new Vector2(length, width);
        float angle = (float)Math.Acos(Vector2.Dot(diff, Vector2.UnitX));
        angle *= Math.Sign(Vector2.Dot(diff, Vector2.UnitY));

        var sprite = MySprite.CreateSprite("SquareSimple", position, size);
        sprite.RotationOrScale = angle;
        sprite.Color = color;
        surf.Add(sprite);
    }

    void DrawRadarPlaneBackground(ISpriteSurface surf, Vector2 screenCenter, Vector2 radarPlaneSize, float scale)
    {
        float lineWidth = RadarRangeLineWidth * scale;

        MySprite sprite = new MySprite(SpriteType.TEXTURE, "Circle", size: radarPlaneSize, color: LineColor);
        sprite.Position = screenCenter;
        surf.Add(sprite);

        sprite = new MySprite(SpriteType.TEXTURE, "Circle", size: radarPlaneSize - lineWidth * Vector2.One, color: BackColor);
        sprite.Position = screenCenter;
        surf.Add(sprite);

        sprite = new MySprite(SpriteType.TEXTURE, "Circle", size: radarPlaneSize * 0.5f, color: LineColor);
        sprite.Position = screenCenter;
        surf.Add(sprite);

        sprite = new MySprite(SpriteType.TEXTURE, "Circle", size: radarPlaneSize * 0.5f - lineWidth * Vector2.One, color: BackColor);
        sprite.Position = screenCenter;
        surf.Add(sprite);

        // Transparent plane circle
        sprite = new MySprite(SpriteType.TEXTURE, "Circle", size: radarPlaneSize, color: PlaneColor);
        sprite.Position = screenCenter;
        surf.Add(sprite);
    }

    void DrawRadarPlane(ISpriteSurface surf, Vector2 viewportSize, Vector2 screenCenter, Vector2 radarScreenCenter, Vector2 radarPlaneSize, float scale)
    {
        MySprite sprite;
        Vector2 halfScreenSize = viewportSize * 0.5f;
        float titleBarHeight = TitleBarHeight * scale;
        float titleTextSize = TitleTextSize * scale;

        Vector2 titleBarPos = screenCenter + new Vector2(0f, -halfScreenSize.Y + titleBarHeight * 0.5f);
        sprite = MySprite.CreateSprite("SquareSimple",
            titleBarPos,
            new Vector2(viewportSize.X, titleBarHeight));
        sprite.Color = TitleBarColor;
        surf.Add(sprite);

        sprite = MySprite.CreateText($"WMI Radar System", Font, TextColor, titleTextSize, TextAlignment.CENTER);
        sprite.Position = titleBarPos + new Vector2(0, -titleTextSize * SizeToPx * 0.5f);
        surf.Add(sprite);

        // Ship location
        var iconSize = _shipIconSize * scale;
        sprite = new MySprite(SpriteType.TEXTURE, "Triangle", size: iconSize, color: LineColor);
        sprite.Position = radarScreenCenter + new Vector2(0f, -0.2f * iconSize.Y);
        surf.Add(sprite);

        Vector2 quadrantLine = radarPlaneSize.X * _quadrantLineDirection;
        // Quadrant lines
        if (DrawQuadrants)
        {
            float lineWidth = QuadrantLineWidth * scale;
            Color quadrantLineColor = LineColor.Value * 0.5f;
            DrawLineQuadrantSymmetry(surf, radarScreenCenter, 0.2f * quadrantLine, 1.0f * quadrantLine, lineWidth, quadrantLineColor);
        }

        // Draw range text
        float textSize = RangeTextSize * scale;
        Color rangeColors = TextColor.Value;
        rangeColors.A /= 2;

        sprite = MySprite.CreateText($"Range: {_outerRange}", "Debug", rangeColors, textSize, TextAlignment.CENTER);
        sprite.Position = radarScreenCenter + new Vector2(0, radarPlaneSize.Y * 0.5f + scale * 4f /*+ textSize * 37f*/ );
        surf.Add(sprite);
    }

    Color ScaleColorAlpha(Color color, float scale)
    {
        if (scale > 0.999f)
        {
            return color;
        }
        float newAlpha = color.A * scale;
        color.A = (byte)Math.Round(newAlpha);
        return color;
    }

    #region Icons
    void DrawSquare(ISpriteSurface frame, Vector2 centerPos, Color color, Color shadowColor, float scale, float rotation)
    {
        float sin = (float)Math.Sin(rotation);
        float cos = (float)Math.Cos(rotation);
        scale *= 0.1f;
        frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(0f, 0f) * scale + centerPos, new Vector2(260f, 260f) * scale, shadowColor, null, TextAlignment.CENTER, rotation)); // square shadow
        frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(0f, 0f) * scale + centerPos, new Vector2(160f, 160f) * scale, color, null, TextAlignment.CENTER, rotation)); // square
    }

    void DrawCircle(ISpriteSurface frame, Vector2 centerPos, Color color, Color shadowColor, float scale, float rotation)
    {
        scale *= 0.1f;
        frame.Add(new MySprite(SpriteType.TEXTURE, "Circle", new Vector2(0f, 0f) * scale + centerPos, new Vector2(300f, 300f) * scale, shadowColor, null, TextAlignment.CENTER, 0f)); // circle shadow
        frame.Add(new MySprite(SpriteType.TEXTURE, "Circle", new Vector2(0f, 0f) * scale + centerPos, new Vector2(200f, 200f) * scale, color, null, TextAlignment.CENTER, 0f)); // circle
    }
    
    void DrawHollowCircle(ISpriteSurface frame, Vector2 centerPos, Color color, Color shadowColor, float scale, float rotation)
    {
        scale *= 0.1f;
        frame.Add(new MySprite(SpriteType.TEXTURE, "Circle", new Vector2(0f, 0f) * scale + centerPos, new Vector2(300f, 300f) * scale, shadowColor, null, TextAlignment.CENTER, 0f)); // circle shadow
        frame.Add(new MySprite(SpriteType.TEXTURE, "Circle", new Vector2(0f, 0f) * scale + centerPos, new Vector2(200f, 200f) * scale, color, null, TextAlignment.CENTER, 0f)); // circle
        frame.Add(new MySprite(SpriteType.TEXTURE, "Circle", new Vector2(0f, 0f) * scale + centerPos, new Vector2(100f, 100f) * scale, shadowColor, null, TextAlignment.CENTER, 0f)); // center
    }

    void DrawTriangle(ISpriteSurface frame, Vector2 centerPos, Color color, Color shadowColor, float scale, float rotation)
    {
        float sin = (float)Math.Sin(rotation);
        float cos = (float)Math.Cos(rotation);
        scale *= 0.1f;
        frame.Add(new MySprite(SpriteType.TEXTURE, "Triangle", new Vector2(-sin * -105f, cos * -105f) * scale + centerPos, new Vector2(400f, 400f) * scale, shadowColor, null, TextAlignment.CENTER, rotation)); // triangle shadow
        frame.Add(new MySprite(SpriteType.TEXTURE, "Triangle", new Vector2(-sin * -63f, cos * -63f) * scale + centerPos, new Vector2(200f, 200f) * scale, color, null, TextAlignment.CENTER, rotation)); // triangle
    }

    void DrawOutOfRangeIcon(ISpriteSurface frame, Vector2 centerPos, Color color, Color shadowColor, float scale, float rotation)
    {
        float sin = (float)Math.Sin(rotation);
        float cos = (float)Math.Cos(rotation);
        scale *= 0.1f;
        frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(cos * 61f - sin * -12f, sin * 61f + cos * -12f) * scale + centerPos, new Vector2(180f, 350f) * scale, shadowColor, null, TextAlignment.CENTER, -0.7854f + rotation)); // chevron rightCopy
        frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(cos * -61f - sin * -12f, sin * -61f + cos * -12f) * scale + centerPos, new Vector2(180f, 350f) * scale, shadowColor, null, TextAlignment.CENTER, 0.7854f + rotation)); // chevron leftCopy
        frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(cos * 61f - sin * -12f, sin * 61f + cos * -12f) * scale + centerPos, new Vector2(80f, 250f) * scale, color, null, TextAlignment.CENTER, -0.7854f + rotation)); // chevron right
        frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(cos * -61f - sin * -12f, sin * -61f + cos * -12f) * scale + centerPos, new Vector2(80f, 250f) * scale, color, null, TextAlignment.CENTER, 0.7854f + rotation)); // chevron left
    }

    void DrawMissile(ISpriteSurface frame, Vector2 centerPos, Color color, Color shadowColor, float scale, float rotation)
    {
        float sin = (float)Math.Sin(rotation);
        float cos = (float)Math.Cos(rotation);
        scale *= 0.1f;
        frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(-sin * 65f, cos * 65f) * scale + centerPos, new Vector2(180f, 270f) * scale, shadowColor, null, TextAlignment.CENTER, rotation)); // tube shadow
        frame.Add(new MySprite(SpriteType.TEXTURE, "SemiCircle", new Vector2(-sin * -70f, cos * -70f) * scale + centerPos, new Vector2(180f, 260f) * scale, shadowColor, null, TextAlignment.CENTER, rotation)); // nose shadow
        frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(-sin * 125f, cos * 125f) * scale + centerPos, new Vector2(240f, 120f) * scale, shadowColor, null, TextAlignment.CENTER, rotation)); // bottom fins base shadow
        frame.Add(new MySprite(SpriteType.TEXTURE, "Triangle", new Vector2(-sin * -20f, cos * -20f) * scale + centerPos, new Vector2(240f, 170f) * scale, shadowColor, null, TextAlignment.CENTER, rotation)); // bottom fins shadow
        frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(0f, 0f) * scale + centerPos, new Vector2(240f, 60f) * scale, shadowColor, null, TextAlignment.CENTER, rotation)); // top fins shadow base
        frame.Add(new MySprite(SpriteType.TEXTURE, "Triangle", new Vector2(-sin * -115f, cos * -115f) * scale + centerPos, new Vector2(240f, 170f) * scale, shadowColor, null, TextAlignment.CENTER, rotation)); // top fins shadow
        frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(-sin * 40f, cos * 40f) * scale + centerPos, new Vector2(80f, 220f) * scale, color, null, TextAlignment.CENTER, rotation)); // tube
        frame.Add(new MySprite(SpriteType.TEXTURE, "SemiCircle", new Vector2(-sin * -70f, cos * -70f) * scale + centerPos, new Vector2(80f, 160f) * scale, color, null, TextAlignment.CENTER, rotation)); // nose
        frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(-sin * 110f, cos * 110f) * scale + centerPos, new Vector2(140f, 50f) * scale, color, null, TextAlignment.CENTER, rotation)); // bottom fins base
        frame.Add(new MySprite(SpriteType.TEXTURE, "Triangle", new Vector2(-sin * 35f, cos * 35f) * scale + centerPos, new Vector2(140f, 100f) * scale, color, null, TextAlignment.CENTER, rotation)); // bottom fins
        frame.Add(new MySprite(SpriteType.TEXTURE, "Triangle", new Vector2(-sin * -70f, cos * -70f) * scale + centerPos, new Vector2(140f, 100f) * scale, color, null, TextAlignment.CENTER, rotation)); // top fins
    }

    void DrawAmogus(ISpriteSurface frame, Vector2 centerPos, Color color, Color shadowColor, float scale, float rotation)
    {
        float sin = (float)Math.Sin(rotation);
        float cos = (float)Math.Cos(rotation);
        scale *= 0.1f;
        frame.Add(new MySprite(SpriteType.TEXTURE, "Circle", new Vector2(cos * 55f - sin * -50f, sin * 55f + cos * -50f) * scale + centerPos, new Vector2(150f, 150f) * scale, shadowColor, null, TextAlignment.CENTER, rotation)); // visor right edge shadow
        frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(-sin * 85f, cos * 85f) * scale + centerPos, new Vector2(220f, 130f) * scale, shadowColor, null, TextAlignment.CENTER, rotation)); // leg l shadow
        frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(cos * -120f - sin * -75f, sin * -120f + cos * -75f) * scale + centerPos, new Vector2(60f, 50f) * scale, shadowColor, null, TextAlignment.CENTER, rotation)); // backpack shadow
        frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(cos * -20f - sin * 15f, sin * -20f + cos * 15f) * scale + centerPos, new Vector2(260f, 130f) * scale, shadowColor, null, TextAlignment.CENTER, rotation)); // body shadow
        frame.Add(new MySprite(SpriteType.TEXTURE, "Circle", new Vector2(-sin * -50f, cos * -50f) * scale + centerPos, new Vector2(220f, 220f) * scale, shadowColor, null, TextAlignment.CENTER, rotation)); // head shadow
        frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(cos * 35f - sin * 70f, sin * 35f + cos * 70f) * scale + centerPos, new Vector2(50f, 80f) * scale, color, null, TextAlignment.CENTER, rotation)); // leg r
        frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(cos * -35f - sin * 70f, sin * -35f + cos * 70f) * scale + centerPos, new Vector2(50f, 80f) * scale, color, null, TextAlignment.CENTER, rotation)); // leg l
        frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(cos * -20f - sin * -10f, sin * -20f + cos * -10f) * scale + centerPos, new Vector2(160f, 80f) * scale, color, null, TextAlignment.CENTER, rotation)); // body
        frame.Add(new MySprite(SpriteType.TEXTURE, "Circle", new Vector2(-sin * -50f, cos * -50f) * scale + centerPos, new Vector2(120f, 120f) * scale, color, null, TextAlignment.CENTER, rotation)); // head
        frame.Add(new MySprite(SpriteType.TEXTURE, "Circle", new Vector2(cos * 55f - sin * -50f, sin * 55f + cos * -50f) * scale + centerPos, new Vector2(50f, 50f) * scale, new Color(128, 128, 128, 255), null, TextAlignment.CENTER, rotation)); // visor right edge
        frame.Add(new MySprite(SpriteType.TEXTURE, "Circle", new Vector2(cos * 15f - sin * -50f, sin * 15f + cos * -50f) * scale + centerPos, new Vector2(50f, 50f) * scale, new Color(128, 128, 128, 255), null, TextAlignment.CENTER, rotation)); // visor left edge
        frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(cos * 35f - sin * -50f, sin * 35f + cos * -50f) * scale + centerPos, new Vector2(40f, 50f) * scale, new Color(128, 128, 128, 255), null, TextAlignment.CENTER, rotation)); // visor center
    }
    #endregion

    void DrawTargetIcon(ISpriteSurface surf, Vector2 screenCenter, Vector2 radarPlaneSize, TargetInfo targetInfo, float scale)
    {
        float alphaScale = _immediateFadeOut ? 1f : Math.Max(0f, 1f - (targetInfo.Age / FadeOutInterval));

        Vector3 targetPosPixels = targetInfo.Position * new Vector3(1, _radarProjectionCos, _radarProjectionSin) * radarPlaneSize.X * 0.5f;

        var targetPosPlane = new Vector2(targetPosPixels.X, targetPosPixels.Y);
        Vector2 iconPos = targetPosPlane - targetPosPixels.Z * Vector2.UnitY;

        RoundVector2(ref iconPos);
        RoundVector2(ref targetPosPlane);

        float elevationLineWidth = Math.Max(1f, TgtElevationLineWidth * scale);
        var elevationSprite = new MySprite(SpriteType.TEXTURE, "SquareSimple", color: ScaleColorAlpha(targetInfo.ElevationColor, alphaScale), size: new Vector2(elevationLineWidth, targetPosPixels.Z));
        elevationSprite.Position = screenCenter + (iconPos + targetPosPlane) * 0.5f;
        RoundVector2(ref elevationSprite.Position);
        RoundVector2(ref elevationSprite.Size);

        Vector2 iconDrawPos = screenCenter + iconPos;
        RoundVector2(ref iconDrawPos);
        float shadowThickness = 2f * (float)Math.Max(1f, Math.Round(scale * 4f));
        float shadowScale = scale * (_tgtIconSize.X + shadowThickness) / _tgtIconSize.X;
        Color iconColor = ScaleColorAlpha(targetInfo.IconColor, alphaScale);
        Color shadowColor = ScaleColorAlpha(BackColor, alphaScale);

        Vector2 iconSize = _tgtIconSize * scale;
        iconSize.Y *= _radarProjectionCos;
        var projectedIconSprite = new MySprite(SpriteType.TEXTURE, "Circle", color: ScaleColorAlpha(targetInfo.ElevationColor, alphaScale), size: iconSize);
        projectedIconSprite.Position = screenCenter + targetPosPlane;
        RoundVector2(ref projectedIconSprite.Position);

        bool showProjectedElevation = Math.Abs(iconPos.Y - targetPosPlane.Y) > iconSize.Y;

        // Changing the order of drawing based on if above or below radar plane
        if (targetInfo.AbovePlane)
        {
            if (showProjectedElevation)
            {
                surf.Add(projectedIconSprite);
                surf.Add(elevationSprite);
            }
            targetInfo.DrawFunction(surf, iconDrawPos, iconColor, shadowColor, scale, 0);
        }
        else
        {
            if (showProjectedElevation)
            {
                surf.Add(elevationSprite);
            }
            targetInfo.DrawFunction(surf, iconDrawPos, iconColor, shadowColor, scale, MathHelper.Pi);
            if (showProjectedElevation)
            {
                surf.Add(projectedIconSprite);
            }
        }

        if (targetInfo.TargetLock && alphaScale > 0.999f)
        {
            Vector2 targetBoxSize = (_tgtIconSize + 20) * scale;
            DrawBoxCorners(surf, targetBoxSize, iconDrawPos, 12 * scale, 4 * scale, targetInfo.IconColor);

            if (targetInfo.MyTargetLock)
            {
                float lockTextSizeScaled = LockTextSize * scale;
                var lockText = new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Alignment = TextAlignment.CENTER,
                    Color = TextColor,
                    Data = "LOCK",
                    FontId = "Debug",
                    Position = screenCenter + iconPos - new Vector2(0, targetBoxSize.X * 0.5f + lockTextSizeScaled * SizeToPx),
                    RotationOrScale = lockTextSizeScaled,
                    Size = null,
                };

                MySprite lockTextShadow = lockText;
                lockTextShadow.Color = BackColor;
                lockTextShadow.Position += _dropShadowOffset;

                surf.Add(lockTextShadow);
                surf.Add(lockText);
            }
        }
    }

    void RoundVector2(ref Vector2? vec)
    {
        if (vec.HasValue)
            vec = new Vector2((float)Math.Round(vec.Value.X), (float)Math.Round(vec.Value.Y));
    }

    void RoundVector2(ref Vector2 vec)
    {
        vec.X = (float)Math.Round(vec.X);
        vec.Y = (float)Math.Round(vec.Y);
    }

    string[] _prefixes = new string[]
    {
"Y",
"Z",
"E",
"P",
"T",
"G",
"M",
"k",
    };

    double[] _factors = new double[]
    {
1e24,
1e21,
1e18,
1e15,
1e12,
1e9,
1e6,
1e3,
    };

    string SufixRangeWithMetricUnits(double num, string unit, int digits)
    {
        string prefix = "";

        for (int i = 0; i < _factors.Length; ++i)
        {
            double factor = _factors[i];

            if (num >= factor)
            {
                prefix = _prefixes[i];
                num /= factor;
                break;
            }
        }

        return (prefix == "" ? num.ToString("n0") : num.ToString($"n{digits}")) + $" {prefix}{unit}";
    }
}
#endregion

#region Ini stuff
void AddTextSurfaces(IMyTerminalBlock block, List<ISpriteSurface> surfaces)
{
    bool parsed;
    string output;
    var textSurface = block as IMyTextPanel;
    if (textSurface != null)
    {
        bool multiscreen = false;

        _textSurfaceIni.Clear();
        parsed = _textSurfaceIni.TryParse(block.CustomData);

        if (parsed && _textSurfaceIni.ContainsSection(IniSectionMultiscreen))
        {
            multiscreen = true;
            _rows.ReadFromIni(ref _textSurfaceIni, IniSectionMultiscreen);
            _cols.ReadFromIni(ref _textSurfaceIni, IniSectionMultiscreen);
            _rows.Value = Math.Max(_rows, 1);
            _cols.Value = Math.Max(_cols, 1);
            _rows.WriteToIni(ref _textSurfaceIni, IniSectionMultiscreen);
            _cols.WriteToIni(ref _textSurfaceIni, IniSectionMultiscreen);
        }

        if (!parsed && !string.IsNullOrWhiteSpace(block.CustomData))
        {
            _textSurfaceIni.EndContent = block.CustomData;
        }

        output = _textSurfaceIni.ToString();
        if (!string.Equals(output, block.CustomData))
            block.CustomData = output;

        if (multiscreen)
        {
            surfaces.Add(new MultiScreenSpriteSurface(textSurface, _rows, _cols, this));
        }
        else
        {
            surfaces.Add(new SingleScreenSpriteSurface(textSurface));
        }
        return;
    }

    var surfaceProvider = block as IMyTextSurfaceProvider;
    if (surfaceProvider == null)
        return;

    _textSurfaceIni.Clear();
    parsed = _textSurfaceIni.TryParse(block.CustomData);

    if (!parsed && !string.IsNullOrWhiteSpace(block.CustomData))
    {
        _textSurfaceIni.EndContent = block.CustomData;
    }

    int surfaceCount = surfaceProvider.SurfaceCount;
    for (int i = 0; i < surfaceCount; ++i)
    {
        string iniKey = string.Format(IniTextSurfaceTemplate, i);
        bool display = _textSurfaceIni.Get(IniSectionTextSurface, iniKey).ToBoolean(i == 0 && !(block is IMyProgrammableBlock));
        if (display)
        {
            surfaces.Add(new SingleScreenSpriteSurface(surfaceProvider.GetSurface(i)));
        }

        _textSurfaceIni.Set(IniSectionTextSurface, iniKey, display);
    }

    output = _textSurfaceIni.ToString();
    if (!string.Equals(output, block.CustomData))
        block.CustomData = output;
}

void ParseCustomDataIni()
{
    _ini.Clear();
    bool parsed = _ini.TryParse(Me.CustomData);

    if (!parsed && !string.IsNullOrWhiteSpace(Me.CustomData))
    {
        _ini.EndContent = Me.CustomData;
    }

    foreach (var c in _generalConfig)
    {
        c.Update(ref _ini);
    }

    _radarSurface.FadeOutInterval = _fadeOutInterval;

    string output = _ini.ToString();
    if (!string.Equals(output, Me.CustomData))
    {
        Me.CustomData = output;
    }
}
#endregion

#region General Functions
float GetMaxTurretRange(List<TurretInterface> turrets)
{
    float maxRange = 1f;
    foreach (var block in turrets)
    {
        if (block.Closed)
            continue;

        if (!block.IsFunctional)
            continue;

        float thisRange = block.Range;
        if (thisRange > maxRange)
        {
            maxRange = thisRange;
        }
    }
    return maxRange;
}
#endregion

#region Block Fetching
bool PopulateLists(IMyTerminalBlock block)
{
    if (!block.IsSameConstructAs(Me))
    {
        _connectedGridIds.Add(block.CubeGrid.EntityId);
        return false;
    }

    _myGridIds.Add(block.CubeGrid.EntityId);

    if (StringExtensions.Contains(block.CustomName, _textPanelName))
    {
        AddTextSurfaces(block, _surfaces);
    }

    var turret = block as IMyLargeTurretBase;
    if (turret != null)
    {
        _turrets.Add(new TurretInterface(turret));
        return false;
    }

    var tcb = block as IMyTurretControlBlock;
    if (tcb != null)
    {
        _turrets.Add(new TurretInterface(tcb));
        return false;
    }

    var controller = block as IMyShipController;
    if (controller != null)
    {
        _allControllers.Add(controller);
        if (StringExtensions.Contains(block.CustomName, _referenceName))
            _taggedControllers.Add(controller);
        return false;
    }

    var sensor = block as IMySensorBlock;
    if (sensor != null)
    {
        _sensors.Add(sensor);
        return false;
    }

    return false;
}

void GrabBlocks()
{
    // This forces sprites to redraw by clearing the cache
    _clearSpriteCache = !_clearSpriteCache;

    _myGridIds.Clear();
    _connectedGridIds.Clear();
    _sensors.Clear();
    _turrets.Clear();
    _allControllers.Clear();
    _taggedControllers.Clear();
    _surfaces.Clear();

    _compositeBoundingSphere.FetchCubeGrids();
    GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, PopulateLists);

    if (_sensors.Count == 0)
        Log.Info($"No sensors found (not an error).");

    if (_turrets.Count == 0)
        Log.Warning($"No turrets found. You will only be able to see targets that are broadcast by allies.");

    if (_surfaces.Count == 0)
        Log.Error($"No text panels or text surface providers with name tag '{_textPanelName}' were found.");

    if (_allControllers.Count == 0)
        Log.Warning($"No ship controllers were found. Using orientation of this block...");
    else
    {
        if (_taggedControllers.Count == 0)
            Log.Info($"No ship controllers named \"{_referenceName.Value}\" were found. Using all available ship controllers. (This is NOT an error!)");
        else
            Log.Info($"One or more ship controllers with name tag \"{_referenceName.Value}\" were found. Using these to orient the radar.");
    }

    _lastSetupResult = Log.Write();

    if (_surfaces.Count == 0)
        _isSetup = false;
    else
    {
        _isSetup = true;
        ParseCustomDataIni();
    }
}
#endregion

public static class Log
{
    static StringBuilder _builder = new StringBuilder();
    static List<string> _errorList = new List<string>();
    static List<string> _warningList = new List<string>();
    static List<string> _infoList = new List<string>();
    const int _logWidth = 530; //chars, conservative estimate

    public static void Clear()
    {
        _builder.Clear();
        _errorList.Clear();
        _warningList.Clear();
        _infoList.Clear();
    }

    public static void Error(string text)
    {
        _errorList.Add(text);
    }

    public static void Warning(string text)
    {
        _warningList.Add(text);
    }

    public static void Info(string text)
    {
        _infoList.Add(text);
    }

    public static string Write(bool preserveLog = false)
    {
        if (_errorList.Count != 0 && _warningList.Count != 0 && _infoList.Count != 0)
            WriteLine("");

        if (_errorList.Count != 0)
        {
            for (int i = 0; i < _errorList.Count; i++)
            {
                WriteLine("");
                WriteElememt(i + 1, "ERROR", _errorList[i]);
            }
        }

        if (_warningList.Count != 0)
        {
            for (int i = 0; i < _warningList.Count; i++)
            {
                WriteLine("");
                WriteElememt(i + 1, "WARNING", _warningList[i]);
            }
        }

        if (_infoList.Count != 0)
        {
            for (int i = 0; i < _infoList.Count; i++)
            {
                WriteLine("");
                WriteElememt(i + 1, "Info", _infoList[i]);
            }
        }

        string output = _builder.ToString();

        if (!preserveLog)
            Clear();

        return output;
    }

    private static void WriteElememt(int index, string header, string content)
    {
        WriteLine($"{header} {index}:");

        string[] wrappedSplit = content.Split('\n');

        foreach (var line in wrappedSplit)
        {
            _builder.Append("  ").Append(line).Append('\n');
        }
    }

    private static void WriteLine(string text)
    {
        _builder.Append(text).Append('\n');
    }
}

class RadarRunningScreenManager
{
    readonly Color _topBarColor = new Color(25, 25, 25);
    readonly Color _white = new Color(200, 200, 200);
    readonly Color _grey = new Color(150, 150, 150);
    readonly Color _darkGrey = new Color(100, 100, 100);
    readonly Color _black = Color.Black;

    const float TextSize = 1.3f;
    const float SpriteScale = 1.5f;
    const string Font = "Debug";
    const string TitleFormat = "Whip's Turret Radar - v{0}";
    readonly string _titleText;
    readonly Vector2 _spritePosition = new Vector2(0, 20);

    Program _program;

    int _idx = 0;

    struct AnimationParams
    {
        public readonly float Angle;
        public readonly byte Alpha1;
        public readonly byte Alpha2;
        public readonly byte Alpha3;

        public AnimationParams(float angle, byte alpha1, byte alpha2, byte alpha3)
        {
            Angle = angle;
            Alpha1 = alpha1;
            Alpha2 = alpha2;
            Alpha3 = alpha3;
        }
    }

    AnimationParams[] _animSequence = new AnimationParams[] {
        new AnimationParams(  0f,   0,   0, 140),
        new AnimationParams( 15f,   0,   0, 120),
        new AnimationParams( 30f,   0,   0, 100),
        new AnimationParams( 45f, 255,   0,  80), // contact 1
        new AnimationParams( 60f, 245,   0,  60),
        new AnimationParams( 75f, 220,   0,  40),
        new AnimationParams( 90f, 200,   0,  20),
        new AnimationParams(105f, 180,   0,   0),
        new AnimationParams(120f, 160,   0,   0),
        new AnimationParams(135f, 140, 255,   0), // contact 2
        new AnimationParams(150f, 120, 245,   0),
        new AnimationParams(165f, 100, 220,   0),
        new AnimationParams(180f,  80, 200,   0),
        new AnimationParams(195f,  60, 180,   0),
        new AnimationParams(210f,  40, 160,   0),
        new AnimationParams(225f,  20, 140,   0),
        new AnimationParams(240f,   0, 120,   0),
        new AnimationParams(255f,   0, 100,   0),
        new AnimationParams(270f,   0,  80, 255), // contact 3
        new AnimationParams(285f,   0,  60, 245),
        new AnimationParams(300f,   0,  40, 220),
        new AnimationParams(315f,   0,  20, 200),
        new AnimationParams(330f,   0,   0, 180),
        new AnimationParams(345f,   0,   0, 160),
    };

    bool _clearSpriteCache = false;
    IMyTextSurface _surface = null;

    public RadarRunningScreenManager(string version, Program program)
    {
        _titleText = string.Format(TitleFormat, version);
        _program = program;
        _surface = _program.Me.GetSurface(0);
    }

    public void Draw()
    {
        if (_surface == null)
            return;

        AnimationParams anim = _animSequence[_idx];
        _idx = ++_idx % _animSequence.Length;
        float angle = MathHelper.ToRadians(anim.Angle);


        SetupDrawSurface(_surface);
        Vector2 screenCenter = _surface.TextureSize * 0.5f;
        Vector2 scale = _surface.SurfaceSize / 512f;
        float minScale = Math.Min(scale.X, scale.Y);

        using (var frame = _surface.DrawFrame())
        {
            if (_clearSpriteCache)
            {
                frame.Add(new MySprite());
            }

            Vector2 pos = _spritePosition * minScale + screenCenter;
            DrawRadarFrame(frame, pos, minScale * SpriteScale);
            DrawContacts(frame, pos, minScale * SpriteScale, anim.Alpha1, anim.Alpha2, anim.Alpha3);
            DrawRadarSweep(frame, pos, minScale * SpriteScale, angle);

            DrawTitleBar(ref frame, _surface, _topBarColor, _white, _titleText, minScale, textSize: TextSize);
        }
    }

    public void RestartDraw()
    {
        _clearSpriteCache = !_clearSpriteCache;
    }

    #region Draw Helper Functions
    void SetupDrawSurface(IMyTextSurface surface)
    {
        surface.ScriptBackgroundColor = _black;
        surface.ContentType = ContentType.SCRIPT;
        surface.Script = "";
    }

    void DrawRadarFrame(MySpriteDrawFrame frame, Vector2 centerPos, float scale = 1f)
    {
        frame.Add(new MySprite(SpriteType.TEXTURE, "Circle", new Vector2(0f, 0f) * scale + centerPos, new Vector2(200f, 200f) * scale, _white, null, TextAlignment.CENTER, 0f)); // circle
        frame.Add(new MySprite(SpriteType.TEXTURE, "Circle", new Vector2(0f, 0f) * scale + centerPos, new Vector2(190f, 190f) * scale, _black, null, TextAlignment.CENTER, 0f)); // circle inner
    }

    void DrawContacts(MySpriteDrawFrame frame, Vector2 centerPos, float scale, byte contact1Alpha, byte contact2Alpha, byte contact3Alpha)
    {
        Color contact1Color, contact2Color, contact3Color;
        contact1Color = contact2Color = contact3Color = _white;
        contact1Color.A = contact1Alpha;
        contact2Color.A = contact2Alpha;
        contact3Color.A = contact3Alpha;

        frame.Add(new MySprite(SpriteType.TEXTURE, "Circle", new Vector2(-30f, 30f) * scale + centerPos, new Vector2(10f, 10f) * scale, contact1Color, null, TextAlignment.CENTER, 0f)); // contact 1
        frame.Add(new MySprite(SpriteType.TEXTURE, "Circle", new Vector2(-50f, -50f) * scale + centerPos, new Vector2(10f, 10f) * scale, contact2Color, null, TextAlignment.CENTER, 0f)); // contact 2
        frame.Add(new MySprite(SpriteType.TEXTURE, "Circle", new Vector2(50f, 0f) * scale + centerPos, new Vector2(10f, 10f) * scale, contact3Color, null, TextAlignment.CENTER, 0f)); // contact 3
    }

    void DrawRadarSweep(MySpriteDrawFrame frame, Vector2 centerPos, float scale = 1f, float rotation = 0f)
    {
        float sin = (float)Math.Sin(rotation);
        float cos = (float)Math.Cos(rotation);
        frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(cos * 9f - sin * 44f, sin * 9f + cos * 44f) * scale + centerPos, new Vector2(10f, 90f) * scale, _darkGrey, null, TextAlignment.CENTER, -0.2094f + rotation)); // line trailing 2
        frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(cos * 5f - sin * 45f, sin * 5f + cos * 45f) * scale + centerPos, new Vector2(10f, 90f) * scale, _grey, null, TextAlignment.CENTER, -0.1047f + rotation)); // line trailing 1
        frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(cos * 0f - sin * 45f, sin * 0f + cos * 45f) * scale + centerPos, new Vector2(10f, 90f) * scale, _white, null, TextAlignment.CENTER, 0f + rotation)); // line
        frame.Add(new MySprite(SpriteType.TEXTURE, "Circle", new Vector2(cos * 0f - sin * 0f, sin * 0f + cos * 0f) * scale + centerPos, new Vector2(10f, 10f) * scale, _white, null, TextAlignment.CENTER, 0f + rotation)); // center dot
    }

    #endregion
}
#endregion

#region INCLUDES

enum TargetRelation : byte { Neutral = 0, Other = 0, Enemy = 1, Friendly = 2, Locked = 4, LargeGrid = 8, SmallGrid = 16, Missile = 32, Asteroid = 64, RelationMask = Neutral | Enemy | Friendly, TypeMask = LargeGrid | SmallGrid | Other | Missile | Asteroid }

#region Scheduler
/// <summary>
/// Class for scheduling actions to occur at specific frequencies. Actions can be updated in parallel or in sequence (queued).
/// </summary>
public class Scheduler
{
    public double CurrentTimeSinceLastRun { get; private set; } = 0;
    public long CurrentTicksSinceLastRun { get; private set; } = 0;

    QueuedAction _currentlyQueuedAction = null;
    bool _firstRun = true;
    bool _inUpdate = false;

    readonly bool _ignoreFirstRun;
    readonly List<ScheduledAction> _actionsToAdd = new List<ScheduledAction>();
    readonly List<ScheduledAction> _scheduledActions = new List<ScheduledAction>();
    readonly List<ScheduledAction> _actionsToDispose = new List<ScheduledAction>();
    readonly Queue<QueuedAction> _queuedActions = new Queue<QueuedAction>();
    readonly Program _program;

    public const long TicksPerSecond = 60;
    public const double TickDurationSeconds = 1.0 / TicksPerSecond;
    const long ClockTicksPerGameTick = 166666L;

    /// <summary>
    /// Constructs a scheduler object with timing based on the runtime of the input program.
    /// </summary>
    public Scheduler(Program program, bool ignoreFirstRun = false)
    {
        _program = program;
        _ignoreFirstRun = ignoreFirstRun;
    }

    /// <summary>
    /// Updates all ScheduledAcions in the schedule and the queue.
    /// </summary>
    public void Update()
    {
        _inUpdate = true;
        long deltaTicks = Math.Max(0, _program.Runtime.TimeSinceLastRun.Ticks / ClockTicksPerGameTick);

        if (_firstRun)
        {
            if (_ignoreFirstRun)
            {
                deltaTicks = 0;
            }
            _firstRun = false;
        }

        _actionsToDispose.Clear();
        foreach (ScheduledAction action in _scheduledActions)
        {
            CurrentTicksSinceLastRun = action.TicksSinceLastRun + deltaTicks;
            CurrentTimeSinceLastRun = action.TimeSinceLastRun + deltaTicks * TickDurationSeconds;
            action.Update(deltaTicks);
            if (action.JustRan && action.DisposeAfterRun)
            {
                _actionsToDispose.Add(action);
            }
        }

        if (_actionsToDispose.Count > 0)
        {
            _scheduledActions.RemoveAll((x) => _actionsToDispose.Contains(x));
        }

        if (_currentlyQueuedAction == null)
        {
            // If queue is not empty, populate current queued action
            if (_queuedActions.Count != 0)
                _currentlyQueuedAction = _queuedActions.Dequeue();
        }

        // If queued action is populated
        if (_currentlyQueuedAction != null)
        {
            _currentlyQueuedAction.Update(deltaTicks);
            if (_currentlyQueuedAction.JustRan)
            {
                if (!_currentlyQueuedAction.DisposeAfterRun)
                {
                    _queuedActions.Enqueue(_currentlyQueuedAction);
                }
                // Set the queued action to null for the next cycle
                _currentlyQueuedAction = null;
            }
        }
        _inUpdate = false;

        if (_actionsToAdd.Count > 0)
        {
            _scheduledActions.AddRange(_actionsToAdd);
            _actionsToAdd.Clear();
        }
    }

    /// <summary>
    /// Adds an Action to the schedule. All actions are updated each update call.
    /// </summary>
    public void AddScheduledAction(Action action, double updateFrequency, bool disposeAfterRun = false, double timeOffset = 0)
    {
        ScheduledAction scheduledAction = new ScheduledAction(action, updateFrequency, disposeAfterRun, timeOffset);
        if (!_inUpdate)
            _scheduledActions.Add(scheduledAction);
        else
            _actionsToAdd.Add(scheduledAction);
    }

    /// <summary>
    /// Adds a ScheduledAction to the schedule. All actions are updated each update call.
    /// </summary>
    public void AddScheduledAction(ScheduledAction scheduledAction)
    {
        if (!_inUpdate)
            _scheduledActions.Add(scheduledAction);
        else
            _actionsToAdd.Add(scheduledAction);
    }

    /// <summary>
    /// Adds an Action to the queue. Queue is FIFO.
    /// </summary>
    public void AddQueuedAction(Action action, double updateInterval, bool removeAfterRun = false)
    {
        if (updateInterval <= 0)
        {
            updateInterval = 0.001; // avoids divide by zero
        }
        QueuedAction scheduledAction = new QueuedAction(action, updateInterval, removeAfterRun);
        _queuedActions.Enqueue(scheduledAction);
    }

    /// <summary>
    /// Adds a ScheduledAction to the queue. Queue is FIFO.
    /// </summary>
    public void AddQueuedAction(QueuedAction scheduledAction)
    {
        _queuedActions.Enqueue(scheduledAction);
    }
}

public class QueuedAction : ScheduledAction
{
    public QueuedAction(Action action, double runInterval, bool removeAfterRun = false)
        : base(action, 1.0 / runInterval, removeAfterRun: removeAfterRun, timeOffset: 0)
    { }
}

public class ScheduledAction
{
    public bool JustRan { get; private set; } = false;
    public bool DisposeAfterRun { get; private set; } = false;
    public double TimeSinceLastRun { get { return TicksSinceLastRun * Scheduler.TickDurationSeconds; } }
    public long TicksSinceLastRun { get; private set; } = 0;
    public double RunInterval
    {
        get
        {
            return RunIntervalTicks * Scheduler.TickDurationSeconds;
        }
        set
        {
            RunIntervalTicks = (long)Math.Round(value * Scheduler.TicksPerSecond);
        }
    }
    public long RunIntervalTicks
    {
        get
        {
            return _runIntervalTicks;
        }
        set
        {
            if (value == _runIntervalTicks)
                return;

            _runIntervalTicks = value < 0 ? 0 : value;
            _runFrequency = value == 0 ? double.MaxValue : Scheduler.TicksPerSecond / _runIntervalTicks;
        }
    }

    public double RunFrequency
    {
        get
        {
            return _runFrequency;
        }
        set
        {
            if (value == _runFrequency)
                return;

            if (value == 0)
                RunIntervalTicks = long.MaxValue;
            else
                RunIntervalTicks = (long)Math.Round(Scheduler.TicksPerSecond / value);
        }
    }

    long _runIntervalTicks;
    double _runFrequency;
    readonly Action _action;

    /// <summary>
    /// Class for scheduling an action to occur at a specified frequency (in Hz).
    /// </summary>
    /// <param name="action">Action to run</param>
    /// <param name="runFrequency">How often to run in Hz</param>
    public ScheduledAction(Action action, double runFrequency, bool removeAfterRun = false, double timeOffset = 0)
    {
        _action = action;
        RunFrequency = runFrequency; // Implicitly sets RunInterval
        DisposeAfterRun = removeAfterRun;
        TicksSinceLastRun = (long)Math.Round(timeOffset * Scheduler.TicksPerSecond);
    }

    public void Update(long deltaTicks)
    {
        TicksSinceLastRun += deltaTicks;

        if (TicksSinceLastRun >= RunIntervalTicks)
        {
            _action.Invoke();
            TicksSinceLastRun = 0;

            JustRan = true;
        }
        else
        {
            JustRan = false;
        }
    }
}
#endregion

/// <summary>
/// Class that tracks runtime history.
/// </summary>
public class RuntimeTracker
{
    public int Capacity { get; set; }
    public double Sensitivity { get; set; }
    public double MaxRuntime {get; private set;}
    public double MaxInstructions {get; private set;}
    public double AverageRuntime {get; private set;}
    public double AverageInstructions {get; private set;}
    public double LastRuntime {get; private set;}
    public double LastInstructions {get; private set;}
    
    readonly Queue<double> _runtimes = new Queue<double>();
    readonly Queue<double> _instructions = new Queue<double>();
    readonly int _instructionLimit;
    readonly Program _program;
    const double MS_PER_TICK = 16.6666;
    
    const string Format = "General Runtime Info\n"
            + "- Avg runtime: {0:n4} ms\n"
            + "- Last runtime: {1:n4} ms\n"
            + "- Max runtime: {2:n4} ms\n"
            + "- Avg instructions: {3:n2}\n"
            + "- Last instructions: {4:n0}\n"
            + "- Max instructions: {5:n0}\n"
            + "- Avg complexity: {6:0.000}%";

    public RuntimeTracker(Program program, int capacity = 100, double sensitivity = 0.005)
    {
        _program = program;
        Capacity = capacity;
        Sensitivity = sensitivity;
        _instructionLimit = _program.Runtime.MaxInstructionCount;
    }

    public void AddRuntime()
    {
        double runtime = _program.Runtime.LastRunTimeMs;
        LastRuntime = runtime;
        AverageRuntime += (Sensitivity * runtime);
        int roundedTicksSinceLastRuntime = (int)Math.Round(_program.Runtime.TimeSinceLastRun.TotalMilliseconds / MS_PER_TICK);
        if (roundedTicksSinceLastRuntime == 1)
        {
            AverageRuntime *= (1 - Sensitivity); 
        }
        else if (roundedTicksSinceLastRuntime > 1)
        {
            AverageRuntime *= Math.Pow((1 - Sensitivity), roundedTicksSinceLastRuntime);
        }

        _runtimes.Enqueue(runtime);
        if (_runtimes.Count == Capacity)
        {
            _runtimes.Dequeue();
        }
        
        MaxRuntime = _runtimes.Max();
    }

    public void AddInstructions()
    {
        double instructions = _program.Runtime.CurrentInstructionCount;
        LastInstructions = instructions;
        AverageInstructions = Sensitivity * (instructions - AverageInstructions) + AverageInstructions;
        
        _instructions.Enqueue(instructions);
        if (_instructions.Count == Capacity)
        {
            _instructions.Dequeue();
        }
        
        MaxInstructions = _instructions.Max();
    }

    public string Write()
    {
        return string.Format(
            Format,
            AverageRuntime,
            LastRuntime,
            MaxRuntime,
            AverageInstructions,
            LastInstructions,
            MaxInstructions,
            AverageInstructions / _instructionLimit);
    }
}

public class CompositeBoundingSphere
{
    public double Radius
    {
        get
        {
            return _sphere.Radius;
        }
    }
    
    public Vector3D Center
    {
        get
        {
            return _sphere.Center;
        }
    }
    
    public IMyCubeGrid LargestGrid = null;
    
    BoundingSphereD _sphere;

    Program _program;
    HashSet<IMyCubeGrid> _grids = new HashSet<IMyCubeGrid>();
    Vector3D _compositePosLocal = Vector3D.Zero;
    double _compositeRadius = 0;

    public CompositeBoundingSphere(Program program)
    {
        _program = program;
    }

    public void FetchCubeGrids()
    {
        _grids.Clear();
        _grids.Add(_program.Me.CubeGrid);
        LargestGrid = _program.Me.CubeGrid;
        _program.GridTerminalSystem.GetBlocksOfType<IMyMechanicalConnectionBlock>(null, CollectGrids);
        RecomputeCompositeProperties();
    }

    public void Compute(bool fullCompute = false)
    {
        if (fullCompute)
        {
            RecomputeCompositeProperties();
        }
        Vector3D compositePosWorld = _program.Me.GetPosition() + Vector3D.TransformNormal(_compositePosLocal, _program.Me.WorldMatrix);
        _sphere = new BoundingSphereD(compositePosWorld, _compositeRadius);
    }

    void RecomputeCompositeProperties()
    {
        bool first = true;
        Vector3D compositeCenter = Vector3D.Zero;
        double compositeRadius = 0;
        foreach (var g in _grids)
        {
            Vector3D currentCenter = g.WorldVolume.Center;
            double currentRadius = g.WorldVolume.Radius;
            if (first)
            {
                compositeCenter = currentCenter;
                compositeRadius = currentRadius;
                first = false;
                continue;
            }
            Vector3D diff = currentCenter - compositeCenter;
            double diffLen = diff.Normalize();
            double newDiameter = currentRadius + diffLen + compositeRadius;
            double newRadius = 0.5 * newDiameter;
            if (newRadius > compositeRadius)
            {
                double diffScale = (newRadius - compositeRadius);
                compositeRadius = newRadius;
                compositeCenter += diffScale * diff;
            }
        }
        // Convert to local space
        Vector3D directionToCompositeCenter = compositeCenter - _program.Me.GetPosition();
        _compositePosLocal = Vector3D.TransformNormal(directionToCompositeCenter, MatrixD.Transpose(_program.Me.WorldMatrix));
        _compositeRadius = compositeRadius;
    }

    bool CollectGrids(IMyTerminalBlock b)
    {
        if (!b.IsSameConstructAs(_program.Me))
        {
            return false;
        }
        
        var mech = (IMyMechanicalConnectionBlock)b;
        if (mech.CubeGrid.WorldVolume.Radius > LargestGrid.WorldVolume.Radius)
        {
            LargestGrid = mech.CubeGrid;
        }
        _grids.Add(mech.CubeGrid);
        if (mech.IsAttached)
        {
            _grids.Add(mech.TopGrid);
        }
        return false;
    }
}

public class TurretInterface
{
    public IMyTurretControlBlock TCB { get; private set; } = null;
    public IMyLargeTurretBase T { get; private set; } = null;

    List<IMyFunctionalBlock> _tools = new List<IMyFunctionalBlock>();

    private TurretInterface() { }

    public TurretInterface(IMyLargeTurretBase t)
    {
        T = t;
    }

    public TurretInterface(IMyTurretControlBlock tcb)
    {
        TCB = tcb;
        TCB.GetTools(_tools);
    }

    public MyDetectedEntityInfo GetTargetedEntity()
    {
        return T != null ? T.GetTargetedEntity() : TCB.GetTargetedEntity();
    }

    public float Range
    {
        get
        {
            return T != null ? T.Range : TCB.Range;
        }
    }

    public bool Closed
    {
        get
        {
            return T != null ? T.Closed : TCB.Closed;
        }
    }

    public bool IsFunctional
    {
        get
        {
            return T != null ? T.IsFunctional : TCB.IsFunctional && (TCB.AzimuthRotor != null || TCB.ElevationRotor != null) && (_tools.Count > 0 || TCB.Camera != null);
        }
    }

    public bool IsWorking
    {
        get
        {
            return T != null ? T.IsWorking : TCB.IsWorking && (TCB.AzimuthRotor != null || TCB.ElevationRotor != null) && (_tools.Count > 0 || TCB.Camera != null);
        }
    }

    public bool HasTarget
    {
        get
        {
            return T != null ? T.HasTarget : TCB.HasTarget;
        }
    }

    public Vector3D WorldPos
    {
        get
        {
            if (T != null)
                return T.GetPosition();
            var ds = TCB.GetDirectionSource();
            if (ds != null)
                return ds.GetPosition();
            return Vector3D.Zero;
        }
    }
}

#region Multi-screen Sprite Surface

public interface ISpriteSurface
{
    Vector2 TextureSize { get; }
    Vector2 SurfaceSize { get; }
    Color ScriptBackgroundColor { get; set; }
    int SpriteCount { get; }
    void Add(MySprite sprite);
    void Draw();
    Vector2 MeasureStringInPixels(StringBuilder text, string font, float scale);
}

public class SingleScreenSpriteSurface : ISpriteSurface
{
    public bool IsValid
    {
        get
        {
            return Surface != null;
        }
    }

    public Vector2 TextureSize { get { return IsValid ? Surface.TextureSize : Vector2.Zero; } }
    public Vector2 SurfaceSize { get { return IsValid ? Surface.SurfaceSize : Vector2.Zero; } }
    public Color ScriptBackgroundColor
    {
        get { return IsValid ? Surface.ScriptBackgroundColor : Color.Black; }
        set { if (IsValid) { Surface.ScriptBackgroundColor = value; } }
    }
    public int SpriteCount { get; private set; } = 0;
    public Vector2 MeasureStringInPixels(StringBuilder text, string font, float scale)
    {
        return IsValid ? Surface.MeasureStringInPixels(text, font, scale) : Vector2.Zero;
    }

    public readonly IMyCubeBlock CubeBlock;
    public readonly IMyTextSurface Surface;
    public MySpriteDrawFrame? Frame = null;
    readonly List<MySprite> _sprites = new List<MySprite>(64);

    public void Add(MySprite sprite)
    {
        if (!IsValid)
        {
            return;
        }
        if (Frame == null)
        {
            Frame = Surface.DrawFrame();
        }
        Frame.Value.Add(sprite);
        SpriteCount++;
    }

    public void Draw()
    {
        Draw(Surface.ScriptBackgroundColor);
        SpriteCount = 0;
    }

    public void Draw(Color scriptBackgroundColor)
    {
        if (!IsValid)
        {
            return;
        }
        Surface.ContentType = ContentType.SCRIPT;
        Surface.Script = "";
        Surface.ScriptBackgroundColor = scriptBackgroundColor;
        if (Frame == null)
        {
            Surface.DrawFrame().Dispose();
        }
        else
        {
            Frame.Value.Dispose();
            Frame = null;
        }
    }

    public SingleScreenSpriteSurface(IMyTextSurface surf)
    {
        Surface = surf;
    }

    public SingleScreenSpriteSurface(IMyCubeGrid grid, Vector3I position)
    {
        var slim = grid.GetCubeBlock(position);
        if (slim != null && slim.FatBlock != null)
        {
            var cube = slim.FatBlock;
            var surf = cube as IMyTextSurface;
            if (surf != null)
            {
                CubeBlock = cube;
                Surface = surf;
            }
        }
    }
}

// Assumes that all text panels are the same size
public class MultiScreenSpriteSurface : ISpriteSurface
{
    public bool Initialized { get; private set; } = false;

    float Rotation
    {
        get
        {
            return _rotationAngle ?? 0f;
        }
        set
        {
            bool newAngle = !_rotationAngle.HasValue || _rotationAngle.Value != value;
            _rotationAngle = value;
            if (!newAngle)
            {
                return;
            }

            _spanVector = RotateToDisplayOrientation(new Vector2(Cols, Rows), RotationRads);
            _spanVector *= Vector2.SignNonZero(_spanVector);
            _textureSize = null;
            _basePanelSizeNoRotation = null;
            _textureSizeNoRotation = null;
            for (int r = 0; r < Rows; ++r)
            {
                for (int c = 0; c < Cols; ++c)
                {
                    UpdateSurfaceRotation(r, c);
                }
            }
        }
    }
    float RotationRads
    {
        get
        {
            return MathHelper.ToRadians(Rotation);
        }
    }
    public Vector2 TextureSize
    {
        get
        {
            if (!_textureSize.HasValue)
            {
                _textureSize = BasePanelSize * _spanVector;
            }
            return _textureSize.Value;
        }
    }
    public Vector2 SurfaceSize
    {
        get { return TextureSize; }
    }
    public int SpriteCount { get; private set; } = 0;
    public Vector2 MeasureStringInPixels(StringBuilder text, string font, float scale)
    {
        return _anchor.MeasureStringInPixels(text, font, scale);
    }
    Vector2 BasePanelSize
    {
        get { return _anchor.TextureSize; }
    }
    Vector2 BasePanelSizeNoRotation
    {
        get
        {
            if (!_basePanelSizeNoRotation.HasValue)
            {
                Vector2 size = RotateToBaseOrientation(BasePanelSize, RotationRads);
                size *= Vector2.SignNonZero(size);
                _basePanelSizeNoRotation = size;
            }
            return _basePanelSizeNoRotation.Value;
        }
    }
    Vector2 TextureSizeNoRotation
    {
        get
        {
            if (!_textureSizeNoRotation.HasValue)
            {
                _textureSizeNoRotation = BasePanelSizeNoRotation * new Vector2(Cols, Rows);
            }
            return _textureSizeNoRotation.Value;
        }
    }
    public readonly int Rows;
    public readonly int Cols;

    public Color ScriptBackgroundColor { get; set; } = Color.Black;
    StringBuilder _stringBuilder = new StringBuilder(128);
    Program _p;
    IMyTextPanel _anchor;
    ITerminalProperty<float> _rotationProp;
    float? _rotationAngle;
    Vector2? _textureSize;
    Vector2? _basePanelSizeNoRotation;
    Vector2? _textureSizeNoRotation;
    Vector2 _spanVector;

    readonly SingleScreenSpriteSurface[,] _surfaces;
    readonly Vector2[,] _screenOrigins;

    public MultiScreenSpriteSurface(IMyTextPanel anchor, int rows, int cols, Program p)
    {
        _anchor = anchor;
        _p = p;
        _surfaces = new SingleScreenSpriteSurface[rows, cols];
        _screenOrigins = new Vector2[rows, cols];
        Rows = rows;
        Cols = cols;

        _rotationProp = _anchor.GetProperty("Rotate").Cast<float>();

        Vector3I anchorPos = _anchor.Position;
        Vector3I anchorRight = -Base6Directions.GetIntVector(_anchor.Orientation.Left);
        Vector3I anchorDown = -Base6Directions.GetIntVector(_anchor.Orientation.Up);
        Vector3I anchorBlockSize = _anchor.Max - _anchor.Min + Vector3I.One;
        Vector3I stepRight = Math.Abs(Vector3I.Dot(anchorBlockSize, anchorRight)) * anchorRight;
        Vector3I stepDown = Math.Abs(Vector3I.Dot(anchorBlockSize, anchorDown)) * anchorDown;
        IMyCubeGrid grid = _anchor.CubeGrid;
        for (int r = 0; r < Rows; ++r)
        {
            for (int c = 0; c < Cols; ++c)
            {
                Vector3I blockPosition = anchorPos + r * stepDown + c * stepRight;
                var surf = new SingleScreenSpriteSurface(grid, blockPosition);
                _surfaces[r, c] = surf;
            }
        }

        UpdateRotation();
    }

    public void UpdateRotation()
    {
        Rotation = _rotationProp.GetValue(_anchor);
    }

    void UpdateSurfaceRotation(int r, int c)
    {
        SingleScreenSpriteSurface surf = _surfaces[r, c];
        if (surf.CubeBlock != null)
        {
            _rotationProp.SetValue(surf.CubeBlock, Rotation);
        }

        // Calc screen coords
        Vector2 screenCenter = BasePanelSizeNoRotation * new Vector2(c + 0.5f, r + 0.5f);
        Vector2 fromCenter = screenCenter - 0.5f * TextureSizeNoRotation;
        Vector2 fromCenterRotated = RotateToDisplayOrientation(fromCenter, RotationRads);
        Vector2 screenCenterRotated = fromCenterRotated + 0.5f * TextureSize;
        _screenOrigins[r, c] = screenCenterRotated - 0.5f * BasePanelSize;
    }

    Vector2 RotateToDisplayOrientation(Vector2 vec, float angleRad)
    {
        int caseIdx = (int)Math.Round(angleRad / MathHelper.ToRadians(90));
        switch (caseIdx)
        {
            default:
            case 0:
                return vec;
            case 1: // 90 deg
                return new Vector2(vec.Y, -vec.X);
            case 2: // 180 deg
                return -vec;
            case 3: // 270 deg
                return new Vector2(-vec.Y, vec.X);
        }
    }

    Vector2 RotateToBaseOrientation(Vector2 vec, float angleRad)
    {
        int caseIdx = (int)Math.Round(angleRad / MathHelper.ToRadians(90));
        switch (caseIdx)
        {
            default:
            case 0:
                return vec;
            case 1: // 90 deg
                return new Vector2(-vec.Y, vec.X);
            case 2: // 180 deg
                return -vec;
            case 3: // 270 deg
                return new Vector2(vec.Y, -vec.X);
        }
    }

    Vector2 GetRotatedSize(Vector2 size, float angleRad)
    {
        if (Math.Abs(angleRad) < 1e-3)
        {
            return size;
        }

        float cos = Math.Abs(MyMath.FastCos(angleRad));
        float sin = Math.Abs(MyMath.FastSin(angleRad));
        
        Vector2 rotated = Vector2.Zero;
        rotated.X = size.X * cos + size.Y * sin;
        rotated.Y = size.X * sin + size.Y * cos;
        return rotated;
    }

    public void Add(MySprite sprite)
    {
        Vector2 pos = sprite.Position ?? TextureSize * 0.5f;
        bool isText = sprite.Type == SpriteType.TEXT;
        int lowerCol, upperCol, lowerRow, upperRow;
        if (sprite.Type == SpriteType.CLIP_RECT)
        {
            lowerCol = lowerRow = 0;
            upperCol = Cols - 1;
            upperRow = Rows - 1;
        }
        else
        {
            Vector2 spriteSize;
            if (sprite.Size != null)
            {
                spriteSize = sprite.Size.Value;
            }
            else if (isText)
            {
                _stringBuilder.Clear();
                _stringBuilder.Append(sprite.Data);
                spriteSize = _anchor.MeasureStringInPixels(_stringBuilder, sprite.FontId, sprite.RotationOrScale);
            }
            else
            {
                spriteSize = TextureSize;
                sprite.Size = spriteSize;
            }

            Vector2 fromCenter = pos - (TextureSize * 0.5f);
            Vector2 fromCenterRotated = RotateToBaseOrientation(fromCenter, RotationRads);
            Vector2 basePos = TextureSizeNoRotation * 0.5f + fromCenterRotated;

            // Determine span of the sprite used for culling
            Vector2 rotatedSize = (sprite.Type == SpriteType.TEXTURE ? GetRotatedSize(spriteSize, sprite.RotationOrScale) : spriteSize);
            Vector2 topLeft, bottomRight;
            switch (sprite.Alignment)
            {
                case TextAlignment.LEFT:
                    if (isText)
                    {
                        topLeft = Vector2.Zero;
                        bottomRight = rotatedSize;
                    }
                    else
                    {
                        topLeft = new Vector2(0f, 0.5f) * rotatedSize;
                        bottomRight = new Vector2(1f, 0.5f) * rotatedSize;
                    }
                    break;
                case TextAlignment.RIGHT:
                    if (isText)
                    {
                        topLeft = new Vector2(1f, 0f) * rotatedSize;
                        bottomRight = new Vector2(0f, 1f) * rotatedSize;
                    }
                    else
                    {
                        topLeft = new Vector2(1f, 0.5f) * rotatedSize;
                        bottomRight = new Vector2(0f, 0.5f) * rotatedSize;
                    }
                    break;
                
                default:
                case TextAlignment.CENTER:
                    if (isText)
                    {
                        topLeft = new Vector2(0.5f, 0f) * rotatedSize;
                        bottomRight = new Vector2(0.5f, 1f) * rotatedSize;
                    }
                    else
                    {
                        topLeft = bottomRight = 0.5f * rotatedSize;
                    }
                    break;
            }
            topLeft = RotateToBaseOrientation(topLeft, RotationRads);
            topLeft *= Vector2.SignNonZero(topLeft);
            bottomRight = RotateToBaseOrientation(bottomRight, RotationRads);
            bottomRight *= Vector2.SignNonZero(bottomRight);

            var lowerCoords = Vector2I.Floor((basePos - topLeft) / BasePanelSizeNoRotation);
            var upperCoords = Vector2I.Floor((basePos + bottomRight) / BasePanelSizeNoRotation);

            lowerCol = Math.Max(0, lowerCoords.X);
            upperCol = Math.Min(Cols - 1, upperCoords.X);
            lowerRow = Math.Max(0, lowerCoords.Y);
            upperRow = Math.Min(Rows - 1, upperCoords.Y);
        }

        for (int r = lowerRow; r <= upperRow; ++r)
        {
            for (int c = lowerCol; c <= upperCol; ++c)
            {
                sprite.Position = pos - _screenOrigins[r, c];
                _surfaces[r, c].Add(sprite);
                SpriteCount++;
            }
        }
    }

    public void Draw()
    {
        for (int r = 0; r < Rows; ++r)
        {
            for (int c = 0; c < Cols; ++c)
            {
                _surfaces[r, c].Draw(ScriptBackgroundColor);
            }
        }
        SpriteCount = 0;
    }
}
#endregion

public interface IConfigValue
{
    void WriteToIni(ref MyIni ini, string section);
    bool ReadFromIni(ref MyIni ini, string section);
    bool Update(ref MyIni ini, string section);
    void Reset();
    string Name { get; set; }
    string Comment { get; set; }
}

public interface IConfigValue<T> : IConfigValue
{
    T Value { get; set; }
}

public abstract class ConfigValue<T> : IConfigValue<T>
{
    public string Name { get; set; }
    public string Comment { get; set; }
    protected T _value;
    public T Value
    {
        get { return _value; }
        set
        {
            _value = value;
            _skipRead = true;
        }
    }
    readonly T _defaultValue;
    bool _skipRead = false;

    public static implicit operator T(ConfigValue<T> cfg)
    {
        return cfg.Value;
    }

    public ConfigValue(string name, T defaultValue, string comment)
    {
        Name = name;
        _value = defaultValue;
        _defaultValue = defaultValue;
        Comment = comment;
    }

    public override string ToString()
    {
        return Value.ToString();
    }

    public bool Update(ref MyIni ini, string section)
    {
        bool read = ReadFromIni(ref ini, section);
        WriteToIni(ref ini, section);
        return read;
    }

    public bool ReadFromIni(ref MyIni ini, string section)
    {
        if (_skipRead)
        {
            _skipRead = false;
            return true;
        }
        MyIniValue val = ini.Get(section, Name);
        bool read = !val.IsEmpty;
        if (read)
        {
            read = SetValue(ref val);
        }
        else
        {
            SetDefault();
        }
        return read;
    }

    public void WriteToIni(ref MyIni ini, string section)
    {
        ini.Set(section, Name, this.ToString());
        if (!string.IsNullOrWhiteSpace(Comment))
        {
            ini.SetComment(section, Name, Comment);
        }
        _skipRead = false;
    }

    public void Reset()
    {
        SetDefault();
        _skipRead = false;
    }

    protected abstract bool SetValue(ref MyIniValue val);

    protected virtual void SetDefault()
    {
        _value = _defaultValue;
    }
}

class ConfigSection
{
    public string Section { get; set; }
    public string Comment { get; set; }
    List<IConfigValue> _values = new List<IConfigValue>();

    public ConfigSection(string section, string comment = null)
    {
        Section = section;
        Comment = comment;
    }

    public void AddValue(IConfigValue value)
    {
        _values.Add(value);
    }

    public void AddValues(List<IConfigValue> values)
    {
        _values.AddRange(values);
    }

    public void AddValues(params IConfigValue[] values)
    {
        _values.AddRange(values);
    }

    void SetComment(ref MyIni ini)
    {
        if (!string.IsNullOrWhiteSpace(Comment))
        {
            ini.SetSectionComment(Section, Comment);
        }
    }

    public void ReadFromIni(ref MyIni ini)
    {    
        foreach (IConfigValue c in _values)
        {
            c.ReadFromIni(ref ini, Section);
        }
    }

    public void WriteToIni(ref MyIni ini)
    {    
        foreach (IConfigValue c in _values)
        {
            c.WriteToIni(ref ini, Section);
        }
        SetComment(ref ini);
    }

    public void Update(ref MyIni ini)
    {    
        foreach (IConfigValue c in _values)
        {
            c.Update(ref ini, Section);
        }
        SetComment(ref ini);
    }
}
public class ConfigString : ConfigValue<string>
{
    public ConfigString(string name, string value = "", string comment = null) : base(name, value, comment) { }
    protected override bool SetValue(ref MyIniValue val)
    {
        if (!val.TryGetString(out _value))
        {
            SetDefault();
            return false;
        }
        return true;
    }
}

public class ConfigBool : ConfigValue<bool>
{
    public ConfigBool(string name, bool value = false, string comment = null) : base(name, value, comment) { }
    protected override bool SetValue(ref MyIniValue val)
    {
        if (!val.TryGetBoolean(out _value))
        {
            SetDefault();
            return false;
        }
        return true;
    }
}

public class ConfigFloat : ConfigValue<float>
{
    public ConfigFloat(string name, float value = 0, string comment = null) : base(name, value, comment) { }
    protected override bool SetValue(ref MyIniValue val)
    {
        if (!val.TryGetSingle(out _value))
        {
            SetDefault();
            return false;
        }
        return true;
    }
}

public class ConfigColor : ConfigValue<Color>
{
    public ConfigColor(string name, Color value = default(Color), string comment = null) : base(name, value, comment) { }
    public override string ToString()
    {
        return string.Format("{0}, {1}, {2}, {3}", Value.R, Value.G, Value.B, Value.A);
    }
    protected override bool SetValue(ref MyIniValue val)
    {
        string rgbString = val.ToString("");
        string[] rgbSplit = rgbString.Split(',');

        int r = 0, g = 0, b = 0, a = 0;
        if (rgbSplit.Length != 4 ||
            !int.TryParse(rgbSplit[0].Trim(), out r) ||
            !int.TryParse(rgbSplit[1].Trim(), out g) ||
            !int.TryParse(rgbSplit[2].Trim(), out b))
        {
            SetDefault();
            return false;
        }

        bool hasAlpha = int.TryParse(rgbSplit[3].Trim(), out a);
        if (!hasAlpha)
        {
            a = 255;
        }

        r = MathHelper.Clamp(r, 0, 255);
        g = MathHelper.Clamp(g, 0, 255);
        b = MathHelper.Clamp(b, 0, 255);
        a = MathHelper.Clamp(a, 0, 255);
        _value = new Color(r, g, b, a);
        return true;
    }
}

public class ConfigInt : ConfigValue<int>
{
    public ConfigInt(string name, int value = 0, string comment = null) : base(name, value, comment) { }
    protected override bool SetValue(ref MyIniValue val)
    {
        if (!val.TryGetInt32(out _value))
        {
            SetDefault();
            return false;
        }
        return true;
    }
}

public class ConfigNullable<T, ConfigImplementation> : IConfigValue<T>, IConfigValue
    where ConfigImplementation : IConfigValue<T>, IConfigValue
    where T : struct
{
    public string Name 
    { 
        get { return Implementation.Name; }
        set { Implementation.Name = value; }
    }

    public string Comment 
    { 
        get { return Implementation.Comment; } 
        set { Implementation.Comment = value; } 
    }
    
    public string NullString;
    public T Value
    {
        get { return Implementation.Value; }
        set 
        { 
            Implementation.Value = value;
            HasValue = true;
            _skipRead = true;
        }
    }
    public readonly ConfigImplementation Implementation;
    public bool HasValue { get; private set; }
    bool _skipRead = false;

    public ConfigNullable(ConfigImplementation impl, string nullString = "none")
    {
        Implementation = impl;
        NullString = nullString;
        HasValue = false;
    }

    public void Reset()
    {
        HasValue = false;
        _skipRead = true;
    }

    public bool ReadFromIni(ref MyIni ini, string section)
    {
        if (_skipRead)
        {
            _skipRead = false;
            return true;
        }
        bool read = Implementation.ReadFromIni(ref ini, section);
        if (read)
        {
            HasValue = true;
        }
        else
        {
            HasValue = false;
        }
        return read;
    }

    public void WriteToIni(ref MyIni ini, string section)
    {
        Implementation.WriteToIni(ref ini, section);
        if (!HasValue)
        {
            ini.Set(section, Implementation.Name, NullString);
        }
    }

    public bool Update(ref MyIni ini, string section)
    {
        bool read = ReadFromIni(ref ini, section);
        WriteToIni(ref ini, section);
        return read;
    }

    public override string ToString()
    {
        return HasValue ? Value.ToString() : NullString;
    }
}
public class ConfigDeprecated<T, ConfigImplementation> : IConfigValue where ConfigImplementation : IConfigValue<T>, IConfigValue
{
    public readonly ConfigImplementation Implementation;
    public Action<T> Callback;

    public string Name 
    { 
        get { return Implementation.Name; }
        set { Implementation.Name = value; }
    }

    public string Comment 
    { 
        get { return Implementation.Comment; } 
        set { Implementation.Comment = value; } 
    }

    public ConfigDeprecated(ConfigImplementation impl)
    {
        Implementation = impl;
    }

    public bool ReadFromIni(ref MyIni ini, string section)
    {
        bool read = Implementation.ReadFromIni(ref ini, section);
        if (read)
        {
            Callback?.Invoke(Implementation.Value);
        }
        return read;
    }

    public void WriteToIni(ref MyIni ini, string section)
    {
        ini.Delete(section, Implementation.Name);
    }

    public bool Update(ref MyIni ini, string section)
    {
        bool read = ReadFromIni(ref ini, section);
        WriteToIni(ref ini, section);
        return read;
    }

    public void Reset() {}
}

public static class StringExtensions
{
    public static bool Contains(string source, string toCheck, StringComparison comp = StringComparison.OrdinalIgnoreCase)
    {
        return source?.IndexOf(toCheck, comp) >= 0;
    }
}

public static Vector2 DrawTitleBar(ref MySpriteDrawFrame frame, IMyTextSurface surf, Color barColor, Color textColor, string text, float scale, float textSize = 1.5f, float barHeightPx = 64f, float baseTextHeightPx = 28.8f, string font = "Debug")
{
    float titleBarHeight = scale * barHeightPx;
    float scaledTextSize = textSize * scale;
    Vector2 topLeft = 0.5f * (surf.TextureSize - surf.SurfaceSize);
    Vector2 titleBarSize = new Vector2(surf.SurfaceSize.X, titleBarHeight);
    Vector2 titleBarPos = topLeft + titleBarSize * 0.5f;
    Vector2 titleBarTextPos = titleBarPos - Vector2.UnitY * (0.5f * scaledTextSize * baseTextHeightPx);

    frame.Add(new MySprite(
        SpriteType.TEXTURE,
        "SquareSimple",
        titleBarPos,
        titleBarSize,
        barColor,
        null,
        TextAlignment.CENTER));

    frame.Add(new MySprite(
        SpriteType.TEXT,
        text,
        titleBarTextPos,
        null,
        textColor,
        font,
        TextAlignment.CENTER,
        scaledTextSize));

    return titleBarPos;
}

/// <summary>
/// Selects the active controller from a list using the following priority:
/// Main controller > Oldest controlled ship controller > Any controlled ship controller.
/// </summary>
/// <param name="controllers">List of ship controlers</param>
/// <param name="lastController">Last actively controlled controller</param>
/// <returns>Actively controlled ship controller or null if none is controlled</returns>
public static IMyShipController GetControlledShipController(List<IMyShipController> controllers, IMyShipController lastController = null)
{
    IMyShipController currentlyControlled = null;
    foreach (IMyShipController ctrl in controllers)
    {
        if (ctrl.IsMainCockpit)
        {
            return ctrl;
        }

        // Grab the first seat that has a player sitting in it
        // and save it away in-case we don't have a main contoller
        if (currentlyControlled == null && ctrl != lastController && ctrl.IsUnderControl && ctrl.CanControlShip)
        {
            currentlyControlled = ctrl;
        }
    }

    // We did not find a main controller, so if the first controlled controller
    // from last cycle if it is still controlled
    if (lastController != null && lastController.IsUnderControl)
    {
        return lastController;
    }

    // Otherwise we return the first ship controller that we
    // found that was controlled.
    if (currentlyControlled != null)
    {
        return currentlyControlled;
    }

    // Nothing is under control, return the controller from last cycle.
    return lastController;
}
#endregion
