using OneJS;
using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

// 타일 정보를 담을 구조체
public struct TileInfo
{
    public int2 pos;
    public int floorType;
    public int objType;
    public float distanceToGoal;
    public int2 preTile;
}

[Serializable]
public struct TowerUiInfo
{
    public int Index;
    public string Name;
    public float Damage;
    public float AttackSpeed;
    public float MaxRange;
    public float MinRange;
    public string AttackType;
}

[Serializable]
public class TowerUiInfoList
{
    public List<TowerUiInfo> Items = new List<TowerUiInfo>();
}

public class PlayerInputManager : MonoBehaviour
{
    [HideInInspector] public static PlayerInputManager Instance;

    // js용 변수
    [HideInInspector] public bool HasSelection 
    {
        get => _hasSelection;
        set
        {
            // false로 바꿀 때, IsEditMode가 true가 된 지 0.1초가 지나지 않았다면 무시
            if (!value && Time.time < _editModeStartTime + 0.1f)
            {
                return;
            }

            // 만약 선택이 해제되는 상황이라면 (기존에 선택이 있었는데 false가 됨)
            if (_hasSelection && !value)
            {
                RestoreOriginalPath(); // 경로 복구
            }

            _hasSelection = value;

            if (_hasSelection)
            {
                if (_markerInstance != null)
                {
                    _markerInstance.transform.position = new Vector3(JS_X, 0.2f, JS_Z);
                    _markerInstance.SetActive(true);
                }
                // 2. 마커가 생기자마자 경로 미리보기 시뮬레이션 실행
                SimulatePathWithMarker(JS_X, JS_Z);
            }
            else
            {
                if (_markerInstance != null) _markerInstance.SetActive(false);
                _lastSelectedPos = new int2(-1, -1);
            }
        }
    }
    [HideInInspector] public int JS_X { get; set; }
    [HideInInspector] public int JS_Z { get; set; }
    [HideInInspector] public int JS_Floor { get; set; }
    [HideInInspector] public int JS_Obj { get; set; }
    [HideInInspector] public float JS_MouseX { get; set; }
    [HideInInspector] public float JS_MouseY { get; set; }
    [HideInInspector] public int JS_TowerRangeType { get; set; } = -1;
    
    private bool _isEditMode = false;
    private float _editModeStartTime = -1f;
    [HideInInspector] public bool IsEditMode 
    { 
        get => _isEditMode; 
        set
        {
            // false로 바꿀 때, true가 된 지 0.1초가 지나지 않았다면 무시
            if (!value && Time.time < _editModeStartTime + 0.1f)
            {
                return;
            }

            if (value)
            {
                _editModeStartTime = Time.time; // true가 된 시간 기록
                HasSelection = true; // HasSelection도 같이 true로 변경
                RestoreTowerSelectionAndRange(); // 빠른 클릭으로 인해 해제되었을 수 있는 타워 선택 및 사거리 표시 복구
            }

            _isEditMode = value;
        }
    }

    private void RestoreTowerSelectionAndRange()
    {
        if (JS_Obj >= 100 && _rangeVisualizer != null)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;
            var em = world.EntityManager;
            var configQuery = em.CreateEntityQuery(typeof(MapConfig));
            if (!configQuery.IsEmptyIgnoreFilter)
            {
                Entity configEntity = configQuery.GetSingletonEntity();
                var objEntities = em.GetBuffer<ObjectEntityElement>(configEntity);
                int dataIndex = JS_Z * _mapWidth + JS_X;
                if (dataIndex >= 0 && dataIndex < objEntities.Length)
                {
                    Entity towerEntity = objEntities[dataIndex].Value;

                    if (towerEntity != Entity.Null && (em.HasComponent<TowerStats>(towerEntity)) && em.HasComponent<LocalTransform>(towerEntity))
                    {
                        _selectedTowerEntity = towerEntity;
                        var towerStats = em.GetComponentData<TowerStats>(towerEntity);
                        var towerTransform = em.GetComponentData<LocalTransform>(towerEntity);
                        
                        float maxR = 0, minR = 0, angle = 360f;
                        float3 offset = float3.zero;
                        TowerRangeType type = TowerRangeType.Default;

                        if (em.HasComponent<TowerRangeDefault>(towerEntity)) maxR = em.GetComponentData<TowerRangeDefault>(towerEntity).MaxRange;
                        else if (em.HasComponent<TowerRangeSector>(towerEntity)) { var r = em.GetComponentData<TowerRangeSector>(towerEntity); maxR = r.MaxRange; angle = r.Angle; type = TowerRangeType.Sector; }
                        else if (em.HasComponent<TowerRangeAnnulus>(towerEntity)) { var r = em.GetComponentData<TowerRangeAnnulus>(towerEntity); maxR = r.MaxRange; minR = r.MinRange; type = TowerRangeType.Annulus; }
                        else if (em.HasComponent<TowerRangeOffset>(towerEntity)) { var r = em.GetComponentData<TowerRangeOffset>(towerEntity); maxR = r.MaxRange + r.AttackRadius; minR = r.AttackRadius; offset = r.Offset; type = TowerRangeType.OffsetCircle; }

                        JS_TowerRangeType = (int)type;
                        _rangeVisualizer.ShowRange(towerTransform.Position, towerStats.LogicalRotation, maxR, minR, angle, offset, type);
                    }
                }
            }
        }
    }
    
    [HideInInspector] public bool IsBuildRequestPending = false;
    [HideInInspector] public int PendingTowerIndex = -1;
    [HideInInspector] public int GamePhase { get; set; } = 0;
    [HideInInspector] public bool IsPathBlocked { get; private set; } = false;
    [HideInInspector] public bool IsRemoveRequestPending = false;
    [HideInInspector] public string TowerInfosJson { get; private set; } = string.Empty;

    [Header("Input Actions")]
    public InputActionReference clickAction;
    public InputActionReference positionAction;

    [Header("Settings")]
    public Camera pickingCam;

    [Header("Marker")]
    public GameObject selectionMarker;
    private GameObject _markerInstance;
    private RangeVisualizer _rangeVisualizer;

    [Header("Visualization")]
    public GameObject pathLinePrefab;
    public Color normalPathColor = Color.white;
    public Color previewPathColor = new Color(0, 1, 0, 0.5f);
    public Color blockedPathColor = Color.red;
    private List<LineRenderer> _pathLines = new List<LineRenderer>();
    private List<LineRenderer> _previewLines = new List<LineRenderer>();

    private List<int2> _spawnPoints = new List<int2>();

    private bool _hasSelection;
    private int2 _lastSelectedPos = new int2(-1, -1);
    private Entity _selectedTowerEntity = Entity.Null;

    private TileInfo[,] _realMapData;
    private TileInfo[,] _simMapData;
    public TileInfo[,] MapData => _realMapData;
    private int2 _goalPos;
    private Queue<int2> _pathQueue = new Queue<int2>();
    private List<Vector3> _drawPoints = new List<Vector3>(1000);
    private int _mapWidth;
    private int _mapHeight;
    private int _totalTileCount => _mapWidth * _mapHeight;

    private Queue<int2> _invalidQueue = new Queue<int2>();
    private Queue<int2> _repairQueue = new Queue<int2>();
    private List<int2> _affectedTilesList = new List<int2>();
    private bool[,] _isAffectedMask;

    private readonly int[] dx8 = { 0, 0, 1, -1, 1, 1, -1, -1 };
    private readonly int[] dz8 = { 1, -1, 0, 0, 1, -1, 1, -1 };
    private readonly float[] baseCosts8 = { 1f, 1f, 1f, 1f, 1.1f, 1.1f, 1.1f, 1.1f };

    private RenderTexture m_PickingRT;
    private Texture2D m_ReadbackTex;
    private int _pickingSampleSize = 5;
    private Dictionary<int, int> _idCounts = new Dictionary<int, int>();
    private int m_LastW, m_LastH;

    private VisualElement _rootElement;

    void Awake()
    {
        if (Instance == null) Instance = this;
    }

    void Start()
    {
        if (pickingCam != null) pickingCam.enabled = false;
        m_ReadbackTex = new Texture2D(_pickingSampleSize, _pickingSampleSize, TextureFormat.RGBA32, false);

        var uiDocument = GetComponent<UIDocument>();
        if (uiDocument != null)
        {
            _rootElement = uiDocument.rootVisualElement;
            _rootElement.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        }

        ResizeRenderTexture();

        if (selectionMarker != null)
        {
            _markerInstance = Instantiate(selectionMarker);
            _markerInstance.SetActive(false);
        }

        _rangeVisualizer = GetComponent<RangeVisualizer>();

        var spawnText = Resources.Load<TextAsset>("spawn_pos");
        if (spawnText != null) LoadSpawnPointsForVisual(spawnText.text);
        RefreshTowerInfos();
    }

    public void RefreshTowerInfos()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null) return;

        var em = world.EntityManager;
        var query = em.CreateEntityQuery(typeof(MapConfig));
        if (query.IsEmptyIgnoreFilter) return;

        var configEntity = query.GetSingletonEntity();
        var towerBuffer = em.GetBuffer<TowerPrefabElement>(configEntity);
        var list = new TowerUiInfoList();

        for (int i = 0; i < towerBuffer.Length; i++)
        {
            var towerEntity = towerBuffer[i].Value;
            if (!em.HasComponent<TowerStats>(towerEntity)) continue;

            var stats = em.GetComponentData<TowerStats>(towerEntity);
            float maxR = 0, minR = 0;
            string attackTypeStr = "Projectile";

            if (em.HasComponent<TowerRangeDefault>(towerEntity)) maxR = em.GetComponentData<TowerRangeDefault>(towerEntity).MaxRange;
            else if (em.HasComponent<TowerRangeSector>(towerEntity)) maxR = em.GetComponentData<TowerRangeSector>(towerEntity).MaxRange;
            else if (em.HasComponent<TowerRangeAnnulus>(towerEntity)) { var r = em.GetComponentData<TowerRangeAnnulus>(towerEntity); maxR = r.MaxRange; minR = r.MinRange; }
            else if (em.HasComponent<TowerRangeOffset>(towerEntity)) { var r = em.GetComponentData<TowerRangeOffset>(towerEntity); maxR = r.MaxRange + r.AttackRadius; minR = r.AttackRadius; }

            if (em.HasComponent<AttackHitData>(towerEntity)) attackTypeStr = "Hit";
            else if (em.HasComponent<AttackDirectData>(towerEntity)) attackTypeStr = "Direct";

            bool isAoe = em.HasComponent<AoEHitAttack>(towerEntity);

            list.Items.Add(new TowerUiInfo
            {
                Index = i,
                Name = stats.Name.ToString(),
                Damage = stats.Damage,
                AttackSpeed = stats.AttackSpeed,
                MaxRange = maxR,
                MinRange = minR,
                AttackType = attackTypeStr + (isAoe ? " (AoE)" : "")
            });
        }

        TowerInfosJson = JsonUtility.ToJson(list);
    }

    public void RemoveObject()
    {
        if (JS_Obj <= 1) return;
        IsRemoveRequestPending = true;
        HasSelection = false;
    }

    public void HandleEditDrag(Vector2 delta)
    {
        if (_selectedTowerEntity == Entity.Null) return;
        var world = World.DefaultGameObjectInjectionWorld;
        var em = world.EntityManager;
        if (!em.Exists(_selectedTowerEntity) || !em.HasComponent<TowerStats>(_selectedTowerEntity)) return;

        var stats = em.GetComponentData<TowerStats>(_selectedTowerEntity);
        var transform = em.GetComponentData<LocalTransform>(_selectedTowerEntity);
        Vector3 camRight = Camera.main.transform.right;
        Vector3 camForward = Camera.main.transform.forward;
        camForward.y = 0; camForward.Normalize();
        Vector3 dragDir = camRight * delta.x + camForward * delta.y;

        if (em.HasComponent<TowerRangeSector>(_selectedTowerEntity))
        {
            var range = em.GetComponentData<TowerRangeSector>(_selectedTowerEntity);
            if (dragDir.sqrMagnitude > 0.01f)
            {
                quaternion targetRot = quaternion.LookRotationSafe(dragDir, math.up());
                float angleDiff = math.degrees(math.acos(math.clamp(math.dot(stats.LogicalRotation.value, targetRot.value), -1f, 1f)) * 2f);
                float maxAngle = 10f;
                quaternion newLogicalRot = (angleDiff > maxAngle) ? math.slerp(stats.LogicalRotation, targetRot, maxAngle / angleDiff) : targetRot;
                stats.LogicalRotation = newLogicalRot;
                em.SetComponentData(_selectedTowerEntity, stats);
                if (stats.Rotationable) { transform.Rotation = newLogicalRot; em.SetComponentData(_selectedTowerEntity, transform); }
                if (_rangeVisualizer != null) _rangeVisualizer.ShowRange(transform.Position, stats.LogicalRotation, range.MaxRange, 0, range.Angle, float3.zero, TowerRangeType.Sector);
            }
        }
        else if (em.HasComponent<TowerRangeOffset>(_selectedTowerEntity))
        {
            var range = em.GetComponentData<TowerRangeOffset>(_selectedTowerEntity);
            float3 currentWorldOffset = math.rotate(stats.LogicalRotation, range.Offset);
            float moveSpeed = 0.05f; 
            float3 newWorldOffset = currentWorldOffset + (float3)(dragDir * moveSpeed);
            float dist = math.length(newWorldOffset);
            if (dist > range.MaxRange) { newWorldOffset = math.normalize(newWorldOffset) * range.MaxRange; dist = range.MaxRange; }
            if (dist > 0.001f)
            {
                quaternion targetRot = quaternion.LookRotationSafe(math.normalize(newWorldOffset), math.up());
                stats.LogicalRotation = targetRot;
                range.Offset = new float3(0, 0, dist);
                if (stats.Rotationable) { transform.Rotation = targetRot; em.SetComponentData(_selectedTowerEntity, transform); }
            }
            else { range.Offset = float3.zero; }
            em.SetComponentData(_selectedTowerEntity, stats);
            em.SetComponentData(_selectedTowerEntity, range);
            float maxReachable = range.MaxRange + range.AttackRadius;
            if (_rangeVisualizer != null) _rangeVisualizer.ShowRange(transform.Position, stats.LogicalRotation, maxReachable, range.AttackRadius, 360f, range.Offset, TowerRangeType.OffsetCircle);
        }
    }

    public void LoadSpawnPointsForVisual(string csvText)
    {
        if (string.IsNullOrEmpty(csvText)) return;
        _spawnPoints.Clear();
        string[] coords = csvText.Trim().Split(',');
        for (int i = 0; i < coords.Length; i += 2)
        {
            if (i + 1 >= coords.Length) break;
            int x = Mathf.RoundToInt(float.Parse(coords[i]));
            int z = Mathf.RoundToInt(float.Parse(coords[i + 1]));
            _spawnPoints.Add(new int2(x, z));
        }
        while (_pathLines.Count < _spawnPoints.Count)
        {
            GameObject go = Instantiate(pathLinePrefab, transform);
            _pathLines.Add(go.GetComponent<LineRenderer>());
            GameObject po = Instantiate(pathLinePrefab, transform);
            po.name = $"PreviewLine_{_previewLines.Count}";
            var lr = po.GetComponent<LineRenderer>();
            lr.startColor = previewPathColor; lr.endColor = previewPathColor;
            lr.widthMultiplier = 0.15f;
            po.SetActive(false);
            _previewLines.Add(lr);
        }
    }

    public void DrawAllPaths(TileInfo[,] targetMap)
    {
        for (int i = 0; i < _spawnPoints.Count; i++) DrawPath(_spawnPoints[i], _pathLines[i], targetMap);
    }
    public void DrawAllPaths() => DrawAllPaths(_realMapData);

    private void DrawPath(int2 startNode, LineRenderer lr, TileInfo[,] targetMap)
    {
        _drawPoints.Clear();
        int2 current = startNode;
        int safety = 0;
        float height = (_previewLines.Contains(lr)) ? 0.45f : 0.5f;
        if (current.x < 0 || current.x >= _mapWidth || current.y < 0 || current.y >= _mapHeight)
        {
            _drawPoints.Add(new Vector3(current.x, height, current.y));
            current = new int2(Mathf.Clamp(current.x, 0, _mapWidth - 1), Mathf.Clamp(current.y, 0, _mapHeight - 1));
        }
        while (safety++ < 1000)
        {
            _drawPoints.Add(new Vector3(current.x, height, current.y));
            TileInfo info = targetMap[current.x, current.y];
            if (info.distanceToGoal == 0) break;
            if (info.preTile.x == -1) break;
            current = info.preTile;
        }
        lr.positionCount = _drawPoints.Count;
        lr.SetPositions(_drawPoints.ToArray());
    }

    public void OnJSClickEvent(string targetName, float x, float z)
    {
        Debug.Log($"<color=cyan>[C# Receive] JS에서 {targetName} 클릭됨. 타일 위치: ({x}, {z})</color>");
    }

    private void SimulatePathWithMarker(int x, int z)
    {
        if (x < 0 || x >= _mapWidth || z < 0 || z >= _mapHeight) return;
        if (_lastSelectedPos.x == x && _lastSelectedPos.y == z) return;
        _lastSelectedPos = new int2(x, z);
        Array.Copy(_realMapData, _simMapData, _realMapData.Length);
        if (_realMapData[x, z].objType == 0) _simMapData[x, z].objType = 2;
        else if (_realMapData[x, z].objType > 1) _simMapData[x, z].objType = 0;
        UpdatePathAt(x, z, _simMapData);
        IsPathBlocked = CheckIfAnyPathBlocked(_simMapData);
        if (IsPathBlocked) { UpdatePathVisualizationColor(_pathLines, _simMapData, true); HidePreviewLines(); }
        else { UpdatePathVisualizationColor(_pathLines, _realMapData, false); ShowPreviewPaths(_simMapData); }
    }

    private bool CheckIfAnyPathBlocked(TileInfo[,] targetMap)
    {
        foreach (var spawn in _spawnPoints)
        {
            int sx = Mathf.Clamp(spawn.x, 0, _mapWidth - 1);
            int sz = Mathf.Clamp(spawn.y, 0, _mapHeight - 1);
            if (targetMap[sx, sz].distanceToGoal == -1f) return true;
        }
        return false;
    }

    private void UpdatePathVisualizationColor(List<LineRenderer> lines, TileInfo[,] targetMap, bool isSimulation)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            var lr = lines[i]; if (i >= _spawnPoints.Count) continue;
            int2 spawn = _spawnPoints[i];
            int sx = Mathf.Clamp(spawn.x, 0, _mapWidth - 1);
            int sz = Mathf.Clamp(spawn.y, 0, _mapHeight - 1);
            bool isBlocked = targetMap[sx, sz].distanceToGoal == -1f;
            Color color = isBlocked ? blockedPathColor : (lines == _previewLines ? previewPathColor : normalPathColor);
            lr.startColor = color; lr.endColor = color;
        }
    }

    private void ShowPreviewPaths(TileInfo[,] targetMap)
    {
        for (int i = 0; i < _spawnPoints.Count; i++) { _previewLines[i].gameObject.SetActive(true); DrawPath(_spawnPoints[i], _previewLines[i], targetMap); }
    }

    private void HidePreviewLines() { foreach (var lr in _previewLines) lr.gameObject.SetActive(false); }

    private void RestoreOriginalPath()
    {
        if (_lastSelectedPos.x != -1) { HidePreviewLines(); UpdatePathVisualizationColor(_pathLines, _realMapData, false); }
        _lastSelectedPos = new int2(-1, -1); IsPathBlocked = false;
        if (_rangeVisualizer != null) _rangeVisualizer.HideRange();
        _selectedTowerEntity = Entity.Null; IsEditMode = false;
    }

    public void OnJSMenuAction(string actionName) { Debug.Log($"<color=orange>[C# Action] 실행된 명령: {actionName}</color>"); }

    private void OnGeometryChanged(GeometryChangedEvent evt)
    {
        if (evt.oldRect.width != evt.newRect.width || evt.oldRect.height != evt.newRect.height) ResizeRenderTexture();
    }

    public void AllocateMap(int width, int height)
    {
        _mapWidth = width; _mapHeight = height;
        _realMapData = new TileInfo[width, height]; _simMapData = new TileInfo[width, height];
        _isAffectedMask = new bool[width, height];
        for (int x = 0; x < width; x++) { for (int z = 0; z < height; z++) { _realMapData[x, z].distanceToGoal = -1f; _realMapData[x, z].preTile = new int2(-1, -1); _simMapData[x, z] = _realMapData[x, z]; } }
    }

    public void SetTileData(int x, int z, int floor, int obj)
    {
        if (x < 0 || x >= _mapWidth || z < 0 || z >= _mapHeight) return;
        _realMapData[x, z].pos = new int2(x, z); _realMapData[x, z].floorType = floor; _realMapData[x, z].objType = obj;
        if (obj == 1) _goalPos = new int2(x, z);
    }

    public void FullSyncPathToECS()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null) return;
        var em = world.EntityManager;
        var configQuery = em.CreateEntityQuery(typeof(MapConfig));
        if (configQuery.IsEmptyIgnoreFilter) return;
        Entity configEntity = configQuery.GetSingletonEntity();
        var distBuffer = em.GetBuffer<DistDataElement>(configEntity);
        var preTileBuffer = em.GetBuffer<PreTileDataElement>(configEntity);
        if (distBuffer.Length == 0) { for (int i = 0; i < _totalTileCount; i++) { distBuffer.Add(new DistDataElement()); preTileBuffer.Add(new PreTileDataElement()); } }
        for (int z = 0; z < _mapHeight; z++) { for (int x = 0; x < _mapWidth; x++) { int idx = z * _mapWidth + x; distBuffer[idx] = new DistDataElement { Value = _realMapData[x, z].distanceToGoal }; preTileBuffer[idx] = new PreTileDataElement { Value = _realMapData[x, z].preTile }; } }
    }

    private void SyncSingleTileToECS(int x, int z)
    {
        var world = World.DefaultGameObjectInjectionWorld; if (world == null) return;
        var em = world.EntityManager;
        var query = em.CreateEntityQuery(typeof(MapConfig));
        if (!query.IsEmptyIgnoreFilter) { var configEntity = query.GetSingletonEntity(); var distBuffer = em.GetBuffer<DistDataElement>(configEntity); var preTileBuffer = em.GetBuffer<PreTileDataElement>(configEntity); int idx = z * _mapWidth + x; if (idx >= 0 && idx < distBuffer.Length) { distBuffer[idx] = new DistDataElement { Value = _realMapData[x, z].distanceToGoal }; preTileBuffer[idx] = new PreTileDataElement { Value = _realMapData[x, z].preTile }; } }
    }

    void ResizeRenderTexture()
    {
        m_LastW = Screen.width; m_LastH = Screen.height;
        int w = Mathf.Max(1, m_LastW / 10); int h = Mathf.Max(1, m_LastH / 10);
        if (m_PickingRT != null) m_PickingRT.Release();
        m_PickingRT = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        m_PickingRT.filterMode = FilterMode.Point; m_PickingRT.antiAliasing = 1; pickingCam.targetTexture = m_PickingRT;
    }

    public void FullUpdatePath(bool shouldDraw = true) => FullUpdatePath(_realMapData, shouldDraw);

    public void UpdatePathAt(int x, int z) => UpdatePathAt(x, z, _realMapData);

    public void FullUpdatePath(TileInfo[,] targetMap, bool shouldDraw = true)
    {
        for (int x = 0; x < _mapWidth; x++) for (int z = 0; z < _mapHeight; z++) { targetMap[x, z].distanceToGoal = -1f; targetMap[x, z].preTile = new int2(-1, -1); }
        _pathQueue.Clear(); _pathQueue.Enqueue(_goalPos); targetMap[_goalPos.x, _goalPos.y].distanceToGoal = 0f;
        while (_pathQueue.Count > 0)
        {
            int2 curr = _pathQueue.Dequeue();
            for (int i = 0; i < 8; i++)
            {
                int nx = curr.x + dx8[i]; int nz = curr.y + dz8[i];
                if (nx >= 0 && nx < _mapWidth && nz >= 0 && nz < _mapHeight)
                {
                    if (targetMap[nx, nz].objType <= 1 && IsValidDiagonalMove(curr.x, curr.y, nx, nz, targetMap))
                    {
                        float moveCost = baseCosts8[i] + BiasForTile(nx, nz);
                        if (targetMap[nx, nz].distanceToGoal == -1f) { targetMap[nx, nz].distanceToGoal = targetMap[curr.x, curr.y].distanceToGoal + moveCost; targetMap[nx, nz].preTile = curr; _pathQueue.Enqueue(new int2(nx, nz)); }
                    }
                }
            }
        }
        if (shouldDraw) DrawAllPaths(targetMap);
    }

    public void UpdatePathAt(int x, int z, TileInfo[,] targetMap)
    {
        bool isRealMap = (targetMap == _realMapData);
        _invalidQueue.Clear(); _repairQueue.Clear(); _affectedTilesList.Clear(); Array.Clear(_isAffectedMask, 0, _isAffectedMask.Length);
        targetMap[x, z].distanceToGoal = -1f; if (isRealMap) targetMap[x, z].preTile = new int2(-1, -1);
        _invalidQueue.Enqueue(new int2(x, z)); _affectedTilesList.Add(new int2(x, z)); _isAffectedMask[x, z] = true;
        if (targetMap[x, z].objType > 1)
        {
            for (int i = 0; i < 8; i++)
            {
                int nx = x + dx8[i]; int nz = z + dz8[i];
                if (nx >= 0 && nx < _mapWidth && nz >= 0 && nz < _mapHeight)
                {
                    if (!_isAffectedMask[nx, nz] && targetMap[nx, nz].distanceToGoal != -1f)
                    {
                        int2 parent = targetMap[nx, nz].preTile;
                        if (parent.x != -1 && !IsValidDiagonalMove(parent.x, parent.y, nx, nz, targetMap)) { _isAffectedMask[nx, nz] = true; targetMap[nx, nz].distanceToGoal = -1f; if (isRealMap) targetMap[nx, nz].preTile = new int2(-1, -1); _affectedTilesList.Add(new int2(nx, nz)); _invalidQueue.Enqueue(new int2(nx, nz)); if (isRealMap) SyncSingleTileToECS(nx, nz); }
                    }
                }
            }
        }
        while (_invalidQueue.Count > 0)
        {
            int2 curr = _invalidQueue.Dequeue();
            for (int i = 0; i < 8; i++) { int nx = curr.x + dx8[i]; int nz = curr.y + dz8[i]; if (nx >= 0 && nx < _mapWidth && nz >= 0 && nz < _mapHeight) { if (targetMap[nx, nz].preTile.x == curr.x && targetMap[nx, nz].preTile.y == curr.y) { if (!_isAffectedMask[nx, nz]) { _isAffectedMask[nx, nz] = true; targetMap[nx, nz].distanceToGoal = -1f; if (isRealMap) targetMap[nx, nz].preTile = new int2(-1, -1); _affectedTilesList.Add(new int2(nx, nz)); _invalidQueue.Enqueue(new int2(nx, nz)); if (isRealMap) SyncSingleTileToECS(nx, nz); } } } }
        }
        foreach (var pos in _affectedTilesList) { for (int i = 0; i < 8; i++) { int nx = pos.x + dx8[i]; int nz = pos.y + dz8[i]; if (nx >= 0 && nx < _mapWidth && nz >= 0 && nz < _mapHeight) if (!_isAffectedMask[nx, nz] && targetMap[nx, nz].distanceToGoal != -1f) _repairQueue.Enqueue(new int2(nx, nz)); } }
        while (_repairQueue.Count > 0)
        {
            int2 curr = _repairQueue.Dequeue();
            for (int i = 0; i < 8; i++)
            {
                int nx = curr.x + dx8[i]; int nz = curr.y + dz8[i];
                if (nx >= 0 && nx < _mapWidth && nz >= 0 && nz < _mapHeight)
                {
                    if (targetMap[nx, nz].objType <= 1 && IsValidDiagonalMove(curr.x, curr.y, nx, nz, targetMap))
                    {
                        float newDist = targetMap[curr.x, curr.y].distanceToGoal + baseCosts8[i] + BiasForTile(nx, nz);
                        if (targetMap[nx, nz].distanceToGoal == -1f || newDist < targetMap[nx, nz].distanceToGoal) { targetMap[nx, nz].distanceToGoal = newDist; targetMap[nx, nz].preTile = curr; _repairQueue.Enqueue(new int2(nx, nz)); if (isRealMap) SyncSingleTileToECS(nx, nz); }
                    }
                }
            }
        }
        if (isRealMap) { DrawAllPaths(_realMapData); UpdatePathVisualizationColor(_pathLines, _realMapData, false); }
    }

    private bool IsValidDiagonalMove(int currX, int currZ, int nx, int nz, TileInfo[,] targetMap) { if (currX == nx || currZ == nz) return true; if (targetMap[nx, currZ].objType > 1 || targetMap[currX, nz].objType > 1) return false; return true; }
    private float BiasForTile(int x, int z) { return (x + z * _mapWidth) / (_mapWidth * _mapHeight * _mapWidth * _mapHeight * 2.0f); }

    public void BuildTowerDirect(int towerIdx)
    {
        int x = this.JS_X; int z = this.JS_Z; if (CheckIfAnyPathBlocked(this._simMapData)) return;
        var world = World.DefaultGameObjectInjectionWorld; var em = world.EntityManager;
        var configQuery = em.CreateEntityQuery(typeof(MapConfig)); Entity configEntity = configQuery.GetSingletonEntity();
        var prefabBuffer = em.GetBuffer<TowerPrefabElement>(configEntity); Entity prefabEntity = prefabBuffer[towerIdx].Value; Entity towerEntity = em.Instantiate(prefabEntity);
        var objData = em.GetBuffer<ObjectDataElement>(configEntity); var objEntities = em.GetBuffer<ObjectEntityElement>(configEntity);
        var prefabTr = em.GetComponentData<LocalTransform>(prefabEntity); em.SetComponentData(towerEntity, prefabTr.WithPosition(new float3(x, 0.2f, z)));
        int dataIndex = z * _mapWidth + x; int pickingId = dataIndex + 1;
        em.SetComponentData(towerEntity, new PickingIdColor { Value = IndexToColor(pickingId) });
        objData[dataIndex] = new ObjectDataElement { Value = 100 + towerIdx };
        objEntities[dataIndex] = new ObjectEntityElement { Value = towerEntity };
        this.SetTileData(x, z, this.JS_Floor, 100 + towerIdx); this.UpdatePathAt(x, z); this.HasSelection = false;
    }

    public void RemoveObjectDirect()
    {
        if (JS_Obj <= 1) return;
        var world = World.DefaultGameObjectInjectionWorld; var em = world.EntityManager;
        var configQuery = em.CreateEntityQuery(typeof(MapConfig)); Entity configEntity = configQuery.GetSingletonEntity();
        int dataIndex = JS_Z * _mapWidth + JS_X; var objEntitiesInitial = em.GetBuffer<ObjectEntityElement>(configEntity); Entity targetEntity = objEntitiesInitial[dataIndex].Value;
        if (targetEntity != Entity.Null && em.Exists(targetEntity)) em.DestroyEntity(targetEntity);
        var objData = em.GetBuffer<ObjectDataElement>(configEntity); var objEntities = em.GetBuffer<ObjectEntityElement>(configEntity);
        objData[dataIndex] = new ObjectDataElement { Value = 0 }; objEntities[dataIndex] = new ObjectEntityElement { Value = Entity.Null };
        this.JS_Obj = 0; this.SetTileData(JS_X, JS_Z, JS_Floor, 0); this.UpdatePathAt(JS_X, JS_Z); this.HasSelection = false;
    }

    public void SetGamePhase(int phase) { GamePhase = phase; }

    void OnEnable() { if (clickAction != null) clickAction.action.performed += OnClickPerformed; if (positionAction != null) positionAction.action.Enable(); }
    void OnDisable() { if (clickAction != null) clickAction.action.performed -= OnClickPerformed; }

    private void OnClickPerformed(InputAction.CallbackContext ctx)
    {
        if (_rootElement == null) return;
        Vector2 screenPos = Pointer.current != null ? Pointer.current.position.ReadValue() : positionAction.action.ReadValue<Vector2>();
        IPanel panel = _rootElement.panel; 
        if (panel == null) return;
        Vector2 panelPos = RuntimePanelUtils.ScreenToPanel(panel, screenPos);
        VisualElement pickedElement = panel.Pick(panelPos);
        if (pickedElement != null && pickedElement != _rootElement) return;
        if (HasSelection) { HasSelection = false; return; }
        DoPick(screenPos, panelPos);
    }

    void DoPick(Vector2 screenPos, Vector2 correctedPos)
    {
        if (pickingCam == null || m_PickingRT == null) return;
        pickingCam.Render();
        int px = Mathf.FloorToInt(screenPos.x / Screen.width * m_PickingRT.width);
        int py = Mathf.FloorToInt(screenPos.y / Screen.height * m_PickingRT.height);
        int halfSize = _pickingSampleSize / 2;
        int startX = Mathf.Clamp(px - halfSize, 0, m_PickingRT.width - _pickingSampleSize);
        int startY = Mathf.Clamp(py - halfSize, 0, m_PickingRT.height - _pickingSampleSize);
        RenderTexture old = RenderTexture.active; RenderTexture.active = m_PickingRT;
        m_ReadbackTex.ReadPixels(new Rect(startX, startY, _pickingSampleSize, _pickingSampleSize), 0, 0); m_ReadbackTex.Apply();
        RenderTexture.active = old; _idCounts.Clear();
        for (int y = 0; y < _pickingSampleSize; y++) { for (int x = 0; x < _pickingSampleSize; x++) { Color32 c = m_ReadbackTex.GetPixel(x, y); int id = (c.r) | (c.g << 8) | (c.b << 16); if (id <= 0) continue; if (_idCounts.ContainsKey(id)) _idCounts[id]++; else _idCounts[id] = 1; } }
        int finalId = 0; int maxCount = 0; foreach (var pair in _idCounts) { if (pair.Value > maxCount) { maxCount = pair.Value; finalId = pair.Key; } }
        if (finalId > 0) ProcessPickedID(finalId); else HasSelection = false;
    }

    void ProcessPickedID(int id)
    {
        int index = id - 1; int x = index % _mapWidth; int z = index / _mapWidth;
        if (index < _totalTileCount)
        {
            if (x >= 0 && x < _mapWidth && z >= 0 && z < _mapHeight)
            {
                IsBuildRequestPending = false; PendingTowerIndex = -1;
                TileInfo info = _realMapData[x, z];
                Vector3 tileWorldPos = new Vector3(x, 0.2f, z); Vector3 screenPos = Camera.main.WorldToScreenPoint(tileWorldPos);
                var layout = _rootElement.layout; 
                JS_MouseX = (screenPos.x / Screen.width) * layout.width; 
                JS_MouseY = layout.height - ((screenPos.y / Screen.height) * layout.height);
                JS_X = x; JS_Z = z; JS_Floor = info.floorType; JS_Obj = info.objType; JS_TowerRangeType = -1; IsEditMode = false; _selectedTowerEntity = Entity.Null; HasSelection = true;
                if (info.objType >= 100 && _rangeVisualizer != null)
                {
                    var world = World.DefaultGameObjectInjectionWorld; var em = world.EntityManager;
                    var configQuery = em.CreateEntityQuery(typeof(MapConfig));
                    if (!configQuery.IsEmptyIgnoreFilter)
                    {
                        Entity configEntity = configQuery.GetSingletonEntity();
                        var objEntities = em.GetBuffer<ObjectEntityElement>(configEntity);
                        int dataIndex = z * _mapWidth + x; Entity towerEntity = objEntities[dataIndex].Value;
                        if (towerEntity != Entity.Null && em.HasComponent<TowerStats>(towerEntity) && em.HasComponent<LocalTransform>(towerEntity))
                        {
                            _selectedTowerEntity = towerEntity;
                            var towerStats = em.GetComponentData<TowerStats>(towerEntity);
                            var towerTransform = em.GetComponentData<LocalTransform>(towerEntity);
                            float maxR = 0, minR = 0, angle = 360f; float3 offset = float3.zero; TowerRangeType type = TowerRangeType.Default;
                            if (em.HasComponent<TowerRangeDefault>(towerEntity)) maxR = em.GetComponentData<TowerRangeDefault>(towerEntity).MaxRange;
                            else if (em.HasComponent<TowerRangeSector>(towerEntity)) { var r = em.GetComponentData<TowerRangeSector>(towerEntity); maxR = r.MaxRange; angle = r.Angle; type = TowerRangeType.Sector; }
                            else if (em.HasComponent<TowerRangeAnnulus>(towerEntity)) { var r = em.GetComponentData<TowerRangeAnnulus>(towerEntity); maxR = r.MaxRange; minR = r.MinRange; type = TowerRangeType.Annulus; }
                            else if (em.HasComponent<TowerRangeOffset>(towerEntity)) { var r = em.GetComponentData<TowerRangeOffset>(towerEntity); maxR = r.MaxRange + r.AttackRadius; minR = r.AttackRadius; offset = r.Offset; type = TowerRangeType.OffsetCircle; }
                            JS_TowerRangeType = (int)type; _rangeVisualizer.ShowRange(towerTransform.Position, towerStats.LogicalRotation, maxR, minR, angle, offset, type);
                        }
                    }
                }
                else if (_rangeVisualizer != null) _rangeVisualizer.HideRange();
            }
        }
        else ProcessMonsterClick(id);
    }

    public void ProcessMonsterClick(int id) { Debug.Log($"<color=red>[Monster Clicked] ID: {id}</color>"); }

    private void OnDestroy() { if (_rootElement != null) _rootElement.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged); }

    public void PrintDebugPath(string label, TileInfo[,] targetMap)
    {
        if (_spawnPoints == null || _spawnPoints.Count == 0) return;
        int2 start = _spawnPoints[0]; System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine($"<color=yellow>=== [{label}] Path Check ===</color>"); int2 current = start;
        if (current.x < 0 || current.x >= _mapWidth || current.y < 0 || current.y >= _mapHeight) { sb.Append($"Spawn({current.x},{current.y}) -> "); current = new int2(Mathf.Clamp(current.x, 0, _mapWidth - 1), Mathf.Clamp(current.y, 0, _mapHeight - 1)); }
        int safety = 0; while (safety++ < 1000) { TileInfo info = targetMap[current.x, current.y]; sb.Append($"({current.x},{current.y})[C:{info.distanceToGoal:F5}] -> "); if (info.distanceToGoal == 0) { sb.Append("GOAL"); break; } if (info.preTile.x == -1) { sb.Append("BROKEN"); break; } current = info.preTile; }
        Debug.Log(sb.ToString());
    }

    private static float4 IndexToColor(int id)
    {
        float r = (id & 0xFF) / 255f;
        float g = ((id >> 8) & 0xFF) / 255f;
        float b = ((id >> 16) & 0xFF) / 255f;
        return new float4(r, g, b, 1f);
    }
}
