using OneJS;
using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
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
                _markerInstance.transform.position = new Vector3(JS_X, 0.2f, JS_Z);
                _markerInstance.SetActive(true);
                // 2. 마커가 생기자마자 경로 미리보기 시뮬레이션 실행
                SimulatePathWithMarker(JS_X, JS_Z);
            }
            else
            {
                _markerInstance.SetActive(false);
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

                    if (towerEntity != Entity.Null && em.HasComponent<TowerData>(towerEntity) && em.HasComponent<LocalTransform>(towerEntity))
                    {
                        _selectedTowerEntity = towerEntity;
                        var towerData = em.GetComponentData<TowerData>(towerEntity);
                        var towerTransform = em.GetComponentData<LocalTransform>(towerEntity);
                        JS_TowerRangeType = (int)towerData.RangeType;
                        _rangeVisualizer.ShowRange(towerData, towerTransform.Position, towerTransform.Rotation);
                    }
                }
            }
        }
    }
    
    [HideInInspector] public bool IsBuildRequestPending = false;
    [HideInInspector] public int PendingTowerIndex = -1;
    // UI 페이즈 관리용 (0: 건설 모드, 1: 웨이브/스폰 모드)
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
    public GameObject pathLinePrefab; // LineRenderer가 달린 프리팹 할당
    public Color normalPathColor = Color.white;
    public Color previewPathColor = new Color(0, 1, 0, 0.5f);
    public Color blockedPathColor = Color.red;
    private List<LineRenderer> _pathLines = new List<LineRenderer>();
    private List<LineRenderer> _previewLines = new List<LineRenderer>();

    // 스폰 지점 목록 (csv에서 읽어온 값들을 저장해두었다가 사용)
    private List<int2> _spawnPoints = new List<int2>();

    private bool _hasSelection;
    private int _originalObjType; // 마커가 올라간 타일의 원래 상태 저장용
    private int2 _lastSelectedPos = new int2(-1, -1);
    private Entity _selectedTowerEntity = Entity.Null;

    private TileInfo[,] _realMapData; // 실제 게임 데이터 (타워 건설 시에만 변경)
    private TileInfo[,] _simMapData;
    public TileInfo[,] MapData => _realMapData; // 현재 사용 중인 맵 데이터 (시뮬레이션 중에는 _simMapData로 교체)
    private int2 _goalPos;
    private Queue<int2> _pathQueue = new Queue<int2>();
    private List<Vector3> _drawPoints = new List<Vector3>(1000);
    private int _mapWidth;
    private int _mapHeight;
    private int _totalTileCount => _mapWidth * _mapHeight; // 몬스터 클릭과 타일 클릭을 구분하기 위해 맵 전체 크기 저장

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
        pickingCam.enabled = false;
        m_ReadbackTex = new Texture2D(_pickingSampleSize, _pickingSampleSize, TextureFormat.RGBA32, false);

        var uiDocument = GetComponent<UIDocument>();
        _rootElement = uiDocument.rootVisualElement;
        // 지오메트리(크기, 위치 등) 변경 시 호출될 콜백 등록
        _rootElement.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);

        ResizeRenderTexture();

        _markerInstance = Instantiate(selectionMarker);
        _markerInstance.SetActive(false);

        _rangeVisualizer = GetComponent<RangeVisualizer>();

        LoadSpawnPointsForVisual(Resources.Load<TextAsset>("spawn_pos").text);
        RefreshTowerInfos();
    }

    public void RefreshTowerInfos()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null)
        {
            TowerInfosJson = string.Empty;
            return;
        }

        var em = world.EntityManager;
        var query = em.CreateEntityQuery(typeof(MapConfig));
        if (query.IsEmptyIgnoreFilter)
        {
            TowerInfosJson = string.Empty;
            return;
        }

        var configEntity = query.GetSingletonEntity();
        var towerBuffer = em.GetBuffer<TowerPrefabElement>(configEntity);
        var list = new TowerUiInfoList();

        for (int i = 0; i < towerBuffer.Length; i++)
        {
            var towerEntity = towerBuffer[i].Value;
            if (!em.HasComponent<TowerData>(towerEntity))
            {
                continue;
            }

            var data = em.GetComponentData<TowerData>(towerEntity);
            list.Items.Add(new TowerUiInfo
            {
                Index = i,
                Name = data.Name.ToString(),
                Damage = data.Damage,
                AttackSpeed = data.AttackSpeed,
                MaxRange = data.MaxRange,
                MinRange = data.MinRange,
                AttackType = data.RangeType.ToString()
            });
        }

        TowerInfosJson = JsonUtility.ToJson(list);
    }

    public void RemoveObject()
    {
        if (JS_Obj <= 1) return; // 빈칸(0)이나 목표(1)는 제거 불가
        IsRemoveRequestPending = true;
        HasSelection = false; // 메뉴 닫기
    }

    public void HandleEditDrag(Vector2 delta)
    {
        if (_selectedTowerEntity == Entity.Null) return;

        var world = World.DefaultGameObjectInjectionWorld;
        var em = world.EntityManager;
        if (!em.Exists(_selectedTowerEntity) || !em.HasComponent<TowerData>(_selectedTowerEntity)) return;

        var towerData = em.GetComponentData<TowerData>(_selectedTowerEntity);
        var transform = em.GetComponentData<LocalTransform>(_selectedTowerEntity);

        Vector3 camRight = Camera.main.transform.right;
        Vector3 camForward = Camera.main.transform.forward;
        camForward.y = 0;
        camForward.Normalize();

        Vector3 dragDir = camRight * delta.x + camForward * delta.y;

        if (towerData.RangeType == TowerRangeType.Sector)
        {
            if (dragDir.sqrMagnitude > 0.1f)
            {
                // 목표 회전값 계산
                quaternion targetRot = quaternion.LookRotationSafe(dragDir, math.up());
                
                // 현재 회전값과 목표 회전값 사이의 각도 차이 계산
                float angleDiff = math.degrees(math.acos(math.dot(transform.Rotation.value, targetRot.value)) * 2f);
                
                // 최대 회전 각도를 10도로 제한
                float maxAngle = 10f;
                if (angleDiff > maxAngle)
                {
                    // Slerp를 사용하여 최대 각도만큼만 회전
                    float t = maxAngle / angleDiff;
                    transform.Rotation = math.slerp(transform.Rotation, targetRot, t);
                }
                else
                {
                    transform.Rotation = targetRot;
                }
                
                em.SetComponentData(_selectedTowerEntity, transform);
                
                if (_rangeVisualizer != null) _rangeVisualizer.ShowRange(towerData, transform.Position, transform.Rotation);
            }
        }
        else if (towerData.RangeType == TowerRangeType.OffsetCircle)
        {
            // OffsetCircle의 경우 드래그 방향으로 오프셋을 이동시킴
            // delta는 화면 픽셀 단위이므로 적절한 스케일링 필요
            float moveSpeed = 0.05f; 
            float3 newOffset = towerData.RangeOffset + (float3)(dragDir * moveSpeed);
            
            // 최대 사거리(MaxRange)를 벗어나지 않도록 제한
            if (math.length(newOffset) > towerData.MaxRange)
            {
                newOffset = math.normalize(newOffset) * towerData.MaxRange;
            }
            
            towerData.RangeOffset = newOffset;
            em.SetComponentData(_selectedTowerEntity, towerData);
            
            if (_rangeVisualizer != null) _rangeVisualizer.ShowRange(towerData, transform.Position, transform.Rotation);
        }
    }

    // 1. CSV 로드 시 스폰 지점 저장 (MapSpawnSystem 등에서 호출하거나 직접 읽음)
    public void LoadSpawnPointsForVisual(string csvText)
    {
        _spawnPoints.Clear();
        string[] coords = csvText.Trim().Split(',');
        for (int i = 0; i < coords.Length; i += 2)
        {
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

            // 프리뷰용 색상 및 투명도 설정
            lr.startColor = previewPathColor;
            lr.endColor = previewPathColor;

            // 겹쳐도 잘 보이게 굵기를 약간 조절하거나 sorting order 조정 가능
            lr.widthMultiplier = 0.15f;

            po.SetActive(false);
            _previewLines.Add(lr);
        }
    }

    // 2. 모든 경로 그리기 (타워 건설 등으로 경로가 바뀔 때마다 호출)
    public void DrawAllPaths(TileInfo[,] targetMap)
    {
        // 경로 그리기 루프
        for (int i = 0; i < _spawnPoints.Count; i++)
        {
            DrawPath(_spawnPoints[i], _pathLines[i], targetMap);
        }

    }
    public void DrawAllPaths() => DrawAllPaths(_realMapData);

    private void DrawPath(int2 startNode, LineRenderer lr, TileInfo[,] targetMap)
    {
        _drawPoints.Clear();
        int2 current = startNode;
        int safety = 0;

        float height = (_previewLines.Contains(lr)) ? 0.45f : 0.5f;

        // 시작점이 맵 밖인 경우 처리
        if (current.x < 0 || current.x >= _mapWidth || current.y < 0 || current.y >= _mapHeight)
        {
            // 1. 스폰 위치(맵 밖) 점 추가
            _drawPoints.Add(new Vector3(current.x, height, current.y));

            // 2. 가장 가까운 진입점(Entry Point) 계산
            int entryX = Mathf.Clamp(current.x, 0, _mapWidth - 1);
            int entryY = Mathf.Clamp(current.y, 0, _mapHeight - 1);
            current = new int2(entryX, entryY);

            // 진입점은 아래 while 루프의 첫 시작에서 points에 추가
        }

        while (safety++ < 1000)
        {
            _drawPoints.Add(new Vector3(current.x, height, current.y));

            TileInfo info = targetMap[current.x, current.y];

            // 도착했거나(-1이 아님 & 거리 0), 길이 끊긴 경우(-1)
            if (info.distanceToGoal == 0) break;

            int2 next = info.preTile;
            if (next.x == -1) break; // 길이 끊김

            current = next;
        }

        lr.positionCount = _drawPoints.Count;
        lr.SetPositions(_drawPoints.ToArray());
    }

    // JS에서 버튼이나 배경을 클릭했을 때 호출할 메서드들
    public void OnJSClickEvent(string targetName, float x, float z)
    {
        Debug.Log($"<color=cyan>[C# Receive] JS에서 {targetName} 클릭됨. 타일 위치: ({x}, {z})</color>");
    }

    // 마커 위치를 장애물로 가정하고 경로 재계산
    private void SimulatePathWithMarker(int x, int z)
    {
        // 이미 같은 곳을 선택 중이면 무시
        if (_lastSelectedPos.x == x && _lastSelectedPos.y == z) return;

        _lastSelectedPos = new int2(x, z);

        // 실제 데이터를 시뮬레이션 맵으로 복사
        Array.Copy(_realMapData, _simMapData, _realMapData.Length);

        // 빈 땅일 때 건설 시뮬레이션
        //if (_simMapData[x, z].objType == 0)
        //{
        //    _simMapData[x, z].objType = 2; // 가상 장애물
        //    //FullUpdatePath(_simMapData, false); // 시뮬레이션 맵만 연산 (그리지는 않음)
        //    UpdatePathAt(x, z, _simMapData);

        //    IsPathBlocked = CheckIfAnyPathBlocked(_simMapData);
        //    if (IsPathBlocked)
        //    {
        //        // 막혔다면 기존 선(DrawAllPaths 안함) 유지하고 색상만 변경
        //        UpdatePathVisualizationColor(_simMapData);
        //    }
        //    else
        //    {
        //        // 뚫렸다면 시뮬레이션 결과대로 새로 그림
        //        DrawAllPaths(_simMapData);
        //        UpdatePathVisualizationColor(_simMapData);
        //    }
        //}
        //// 구조물일 때 제거 시뮬레이션
        //else if (_simMapData[x, z].objType > 1)
        //{
        //    _simMapData[x, z].objType = 0; // 가상 빈 땅 (제거)
        //    UpdatePathAt(x, z, _simMapData);

        //    // 제거 시에는 보통 길이 뚫리므로 IsPathBlocked가 false가 될 확률이 높음
        //    IsPathBlocked = CheckIfAnyPathBlocked(_simMapData);

        //    // 제거 후에도 여전히 막혀있을 수 있으므로(다른 벽 때문에) 동일하게 분기 처리
        //    if (IsPathBlocked)
        //    {
        //        UpdatePathVisualizationColor(_simMapData);
        //    }
        //    else
        //    {
        //        // 길이 뚫렸으므로 예상 경로를 초록색(또는 흰색)으로 다시 그림
        //        DrawAllPaths(_simMapData);
        //        UpdatePathVisualizationColor(_simMapData);
        //    }
        //}

        if (_realMapData[x, z].objType == 0)
            _simMapData[x, z].objType = 2; // 건설 가정
        else if (_realMapData[x, z].objType > 1)
            _simMapData[x, z].objType = 0; // 제거 가정

        // 시뮬레이션 맵 경로 갱신
        UpdatePathAt(x, z, _simMapData);

        // 경로 막힘 여부 체크
        IsPathBlocked = CheckIfAnyPathBlocked(_simMapData);
        if (IsPathBlocked)
        {
            // [Case 1] 길이 막힘 -> 기존 라인을 빨간색으로 경고 & 프리뷰 숨김
            UpdatePathVisualizationColor(_pathLines, _simMapData, true); // true: 막힘 강제 표시
            HidePreviewLines();
        }
        else
        {
            // [Case 2] 길이 뚫림 -> 기존 라인 유지(흔색) & 프리뷰 라인 반투명 표시

            // 1. 기존 라인은 실제 맵 데이터(_realMapData) 기준 원래 색으로 복구
            UpdatePathVisualizationColor(_pathLines, _realMapData, false);

            // 2. 프리뷰 라인 활성화 및 그리기
            ShowPreviewPaths(_simMapData);
        }
    }

    // 모든 스폰 지점에서 목표까지 도달 가능한지 체크
    private bool CheckIfAnyPathBlocked(TileInfo[,] targetMap)
    {
        foreach (var spawn in _spawnPoints)
        {
            // 맵 범위 내의 시작 타일 좌표 계산
            int sx = Mathf.Clamp(spawn.x, 0, _mapWidth - 1);
            int sz = Mathf.Clamp(spawn.y, 0, _mapHeight - 1);

            // 목표까지의 거리가 -1이면 경로가 끊긴 것
            if (targetMap[sx, sz].distanceToGoal == -1f) return true;
        }
        return false;
    }

    // 경로 상태에 따라 LineRenderer 색상 변경
    private void UpdatePathVisualizationColor(List<LineRenderer> lines, TileInfo[,] targetMap, bool isSimulation)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            var lr = lines[i];
            if (i >= _spawnPoints.Count) continue;

            // 해당 라인의 스폰 지점 좌표 가져오기
            int2 spawn = _spawnPoints[i];
            int sx = Mathf.Clamp(spawn.x, 0, _mapWidth - 1);
            int sz = Mathf.Clamp(spawn.y, 0, _mapHeight - 1);

            // 해당 스폰 지점에서 목표까지의 거리가 -1이면 막힌 것
            bool isBlocked = targetMap[sx, sz].distanceToGoal == -1f;

            // 색상 결정
            Color targetColor;
            if (isBlocked)
            {
                targetColor = blockedPathColor; // 막힌 경로는 무조건 빨간색
            }
            else
            {
                // 뚫린 경로: 프리뷰 라인이면 반투명 초록, 일반 라인은 흰색
                targetColor = (lines == _previewLines) ? previewPathColor : normalPathColor;
            }

            lr.startColor = targetColor;
            lr.endColor = targetColor;
        }
    }

    // 프리뷰 라인 그리기
    private void ShowPreviewPaths(TileInfo[,] targetMap)
    {
        for (int i = 0; i < _spawnPoints.Count; i++)
        {
            _previewLines[i].gameObject.SetActive(true);
            DrawPath(_spawnPoints[i], _previewLines[i], targetMap);

        }
    }

    // 프리뷰 라인 숨기기
    private void HidePreviewLines()
    {
        foreach (var lr in _previewLines)
        {
            lr.gameObject.SetActive(false);
        }
    }

    // 원래 상태로 되돌리기
    private void RestoreOriginalPath()
    {
        if (_lastSelectedPos.x != -1)
        {
            HidePreviewLines();

            UpdatePathVisualizationColor(_pathLines, _realMapData, false);

            _lastSelectedPos = new int2(-1, -1);
            IsPathBlocked = false;
        }
        if (_rangeVisualizer != null)
        {
            _rangeVisualizer.HideRange();
        }
        _selectedTowerEntity = Entity.Null;
        IsEditMode = false;
    }

    public void OnJSMenuAction(string actionName)
    {
        Debug.Log($"<color=orange>[C# Action] 실행된 명령: {actionName}</color>");
    }

    private void OnGeometryChanged(GeometryChangedEvent evt)
    {
        // 이전 크기와 현재 크기를 비교하여 다를 때만 리사이즈 (불필요한 호출 방지)
        if (evt.oldRect.width != evt.newRect.width || evt.oldRect.height != evt.newRect.height)
        {
            ResizeRenderTexture();
        }
    }

    public void AllocateMap(int width, int height)
    {
        _mapWidth = width;
        _mapHeight = height;
        _realMapData = new TileInfo[width, height];
        _simMapData = new TileInfo[width, height];
        _isAffectedMask = new bool[width, height];

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                _realMapData[x, z].distanceToGoal = -1f;
                _realMapData[x, z].preTile = new int2(-1, -1);
                _simMapData[x, z] = _realMapData[x, z];
            }
        }
    }

    public void SetTileData(int x, int z, int floor, int obj)
    {
        MapData[x, z].pos = new int2(x, z);
        MapData[x, z].floorType = floor;
        MapData[x, z].objType = obj;
        if (obj == 1) _goalPos = new int2(x, z);
    }

    public void FullSyncPathToECS()
    {
        var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
        var entityManager = world.EntityManager;
        var configEntity = entityManager.CreateEntityQuery(typeof(MapConfig)).GetSingletonEntity();

        var distBuffer = entityManager.GetBuffer<DistDataElement>(configEntity);
        var preTileBuffer = entityManager.GetBuffer<PreTileDataElement>(configEntity);

        // 버퍼 크기 맞추기 (초기 1회)
        if (distBuffer.Length == 0)
        {
            for (int i = 0; i < _totalTileCount; i++)
            {
                distBuffer.Add(new DistDataElement());
                preTileBuffer.Add(new PreTileDataElement());
            }
        }

        // 데이터 복사
        for (int z = 0; z < _mapHeight; z++)
        {
            for (int x = 0; x < _mapWidth; x++)
            {
                int idx = z * _mapWidth + x;
                distBuffer[idx] = new DistDataElement { Value = MapData[x, z].distanceToGoal };
                preTileBuffer[idx] = new PreTileDataElement { Value = MapData[x, z].preTile };
            }
        }
    }

    private void SyncSingleTileToECS(int x, int z)
    {
        var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
        if (world == null) return;

        var entityManager = world.EntityManager;
        // MapConfig 싱글톤 엔티티 가져오기
        var query = entityManager.CreateEntityQuery(typeof(MapConfig));
        if (!query.IsEmptyIgnoreFilter)
        {
            var configEntity = query.GetSingletonEntity();
            var distBuffer = entityManager.GetBuffer<DistDataElement>(configEntity);
            var preTileBuffer = entityManager.GetBuffer<PreTileDataElement>(configEntity);

            int idx = z * _mapWidth + x;
            // 변경된 값만 덮어쓰기
            distBuffer[idx] = new DistDataElement { Value = MapData[x, z].distanceToGoal };
            preTileBuffer[idx] = new PreTileDataElement { Value = MapData[x, z].preTile };
        }
    }

    void OnEnable()
    {
        clickAction.action.performed += OnClickPerformed;
        positionAction.action.Enable();
    }

    void OnDisable()
    {
        clickAction.action.performed -= OnClickPerformed;
    }

    private void OnClickPerformed(InputAction.CallbackContext ctx)
    {
        // 최신 마우스/터치 위치를 직접 가져와서 딜레이로 인한 픽킹 오류 방지
        Vector2 screenPos = Pointer.current != null ? Pointer.current.position.ReadValue() : positionAction.action.ReadValue<Vector2>();
        IPanel panel = _rootElement.panel;
        Vector2 panelPos = RuntimePanelUtils.ScreenToPanel(panel, screenPos);
        VisualElement pickedElement = panel.Pick(panelPos);

        // 1. UI 클릭인지 가장 먼저 확인하여 무시
        if (pickedElement != null && pickedElement != _rootElement)
        {
            return; // UI 클릭이므로 타일 픽킹 안 함
        }

        // 2. UI 클릭이 아닐 때만 선택 해제 로직 실행 (빈 땅 클릭 시 이동모드 종료 및 UI 닫기)
        if (HasSelection)
        {
            HasSelection = false;
            return;
        }

        DoPick(screenPos, panelPos);
    }

    void DoPick(Vector2 screenPos, Vector2 correctedPos)
    {
        pickingCam.Render();

        int px = Mathf.FloorToInt(screenPos.x / Screen.width * m_PickingRT.width);
        int py = Mathf.FloorToInt(screenPos.y / Screen.height * m_PickingRT.height);
        int halfSize = _pickingSampleSize / 2;

        int startX = Mathf.Clamp(px - halfSize, 0, m_PickingRT.width - _pickingSampleSize);
        int startY = Mathf.Clamp(py - halfSize, 0, m_PickingRT.height - _pickingSampleSize);

        RenderTexture old = RenderTexture.active;
        RenderTexture.active = m_PickingRT;
        m_ReadbackTex.ReadPixels(new Rect(startX, startY, _pickingSampleSize, _pickingSampleSize), 0, 0);
        m_ReadbackTex.Apply();
        RenderTexture.active = old;

        _idCounts.Clear();

        for (int y = 0; y < _pickingSampleSize; y++)
        {
            for (int x = 0; x < _pickingSampleSize; x++)
            {
                Color32 c = m_ReadbackTex.GetPixel(x, y);
                int id = (c.r) | (c.g << 8) | (c.b << 16);
                if (id <= 0) continue;

                if (_idCounts.ContainsKey(id)) _idCounts[id]++;
                else _idCounts[id] = 1;
            }
        }

        // 최빈값 찾기
        int finalId = 0;
        int maxCount = 0;
        foreach (var pair in _idCounts)
        {
            if (pair.Value > maxCount)
            {
                maxCount = pair.Value;
                finalId = pair.Key;
            }
        }

        if (finalId > 0)
        {
            ProcessPickedID(finalId);
        }
        else
        {
            HasSelection = false;
        }
    }

    void ProcessPickedID(int id)
    {
        int index = id - 1;

        int x = index % _mapWidth;
        int z = index / _mapWidth;

        if (index < _totalTileCount)
        {
            // 범위 체크
            if (x >= 0 && x < _mapWidth && z >= 0 && z < _mapHeight)
            {
                IsBuildRequestPending = false;
                PendingTowerIndex = -1;
                TileInfo info = MapData[x, z];
                Debug.Log($"<color=green>[Clicked] ID:{id} -> Pos:({x},{z}) Floor:{info.floorType} Obj:{info.objType}</color>");

                Vector3 tileWorldPos = new Vector3(x, 0.2f, z);
                Vector3 screenPos = Camera.main.WorldToScreenPoint(tileWorldPos);
                var layout = _rootElement.layout;
                JS_MouseX = (screenPos.x / Screen.width) * layout.width;
                JS_MouseY = layout.height - ((screenPos.y / Screen.height) * layout.height);

                JS_X = x;
                JS_Z = z;
                JS_Floor = info.floorType;
                JS_Obj = info.objType;
                JS_TowerRangeType = -1;
                IsEditMode = false;
                _selectedTowerEntity = Entity.Null;
                HasSelection = true;

                if (info.objType >= 100 && _rangeVisualizer != null)
                {
                    var world = World.DefaultGameObjectInjectionWorld;
                    var em = world.EntityManager;
                    var configQuery = em.CreateEntityQuery(typeof(MapConfig));
                    if (!configQuery.IsEmptyIgnoreFilter)
                    {
                        Entity configEntity = configQuery.GetSingletonEntity();
                        var objEntities = em.GetBuffer<ObjectEntityElement>(configEntity);
                        int dataIndex = z * _mapWidth + x;
                        Entity towerEntity = objEntities[dataIndex].Value;

                        if (towerEntity != Entity.Null && em.HasComponent<TowerData>(towerEntity) && em.HasComponent<LocalTransform>(towerEntity))
                        {
                            _selectedTowerEntity = towerEntity;
                            var towerData = em.GetComponentData<TowerData>(towerEntity);
                            var towerTransform = em.GetComponentData<LocalTransform>(towerEntity);
                            JS_TowerRangeType = (int)towerData.RangeType;
                            _rangeVisualizer.ShowRange(towerData, towerTransform.Position, towerTransform.Rotation);
                        }
                    }
                }
                else if (_rangeVisualizer != null)
                {
                    _rangeVisualizer.HideRange();
                }
            }
        }
        else
        {
            ProcessMonsterClick(id);
        }
    }

    // 몬스터 클릭 처리용 (임시 로그)
    public void ProcessMonsterClick(int id)
    {
        Debug.Log($"<color=red>[Monster Clicked] ID: {id}</color>");
    }

    void ResizeRenderTexture()
    {
        m_LastW = Screen.width;
        m_LastH = Screen.height;
        int w = Mathf.Max(1, m_LastW / 10);
        int h = Mathf.Max(1, m_LastH / 10);
        if (m_PickingRT != null) m_PickingRT.Release();
        m_PickingRT = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        m_PickingRT.filterMode = FilterMode.Point;
        m_PickingRT.antiAliasing = 1;
        pickingCam.targetTexture = m_PickingRT;
    }


    // 1. 초기 맵 생성 후 호출할 전체 갱신 함수
    public void FullUpdatePath(TileInfo[,] targetMap, bool shouldDraw = true)
    {
        // 초기화
        for (int x = 0; x < _mapWidth; x++)
        {
            for (int z = 0; z < _mapHeight; z++)
            {
                targetMap[x, z].distanceToGoal = -1f;
                targetMap[x, z].preTile = new int2(-1, -1);
            }
        }

        _pathQueue.Clear();
        _pathQueue.Enqueue(_goalPos);
        targetMap[_goalPos.x, _goalPos.y].distanceToGoal = 0f;

        while (_pathQueue.Count > 0)
        {
            int2 curr = _pathQueue.Dequeue();
            for (int i = 0; i < 8; i++)
            {
                int nx = curr.x + dx8[i];
                int nz = curr.y + dz8[i];

                if (nx >= 0 && nx < _mapWidth && nz >= 0 && nz < _mapHeight)
                {
                    if (targetMap[nx, nz].objType <= 1 && IsValidDiagonalMove(curr.x, curr.y, nx, nz, targetMap))
                    {
                        float bias = BiasForTile(nx,nz);
                        float moveCost = baseCosts8[i] + bias;

                        // 장애물 체크 및 방문 체크
                        if (targetMap[nx, nz].distanceToGoal == -1f)
                        {
                            targetMap[nx, nz].distanceToGoal = targetMap[curr.x, curr.y].distanceToGoal + moveCost;
                            targetMap[nx, nz].preTile = curr;
                            _pathQueue.Enqueue(new int2(nx, nz));
                        }
                    }
                }
            }
        }

        if (shouldDraw) DrawAllPaths(targetMap);
    }

    public void FullUpdatePath(bool shouldDraw = true) => FullUpdatePath(_realMapData, shouldDraw);

    private Queue<int2> _invalidQueue = new Queue<int2>();
    private HashSet<int2> _affectedTiles = new HashSet<int2>();
    private Queue<int2> _repairQueue = new Queue<int2>();
    private bool[,] _isAffectedMask;
    private List<int2> _affectedTilesList = new List<int2>();

    private readonly int[] dx8 = { 0, 0, 1, -1, 1, 1, -1, -1 };
    private readonly int[] dz8 = { 1, -1, 0, 0, 1, -1, 1, -1 };
    private readonly float[] baseCosts8 = { 1f, 1f, 1f, 1f, 1.1f, 1.1f, 1.1f, 1.1f };

    public void UpdatePathAt(int x, int z, TileInfo[,] targetMap)
    {
        bool isRealMap = (targetMap == _realMapData);

        // 디버그용
        //if (isRealMap)
        //{
        //    Debug.Log($"<color=cyan><b>[Path Debug]</b> 구조물 제거 시작 - 위치: ({x}, {z})</color>");
        //    // 제거 전 스폰 지점 경로 샘플링 (첫 번째 스폰 지점 기준)
        //    PrintDebugPath("제거 전(Before)", targetMap);
        //}

        _invalidQueue.Clear();
        _repairQueue.Clear();
        _affectedTilesList.Clear();
        Array.Clear(_isAffectedMask, 0, _isAffectedMask.Length);

        // 1. 초기 설정: 현재 클릭한 타일을 막힘 처리
        targetMap[x, z].distanceToGoal = -1f;
        // 실제 건설이 아닐 때(시뮬레이션)는 preTile을 남겨둬야 빨간 선이라도 그릴 수 있습니다.
        if (isRealMap) targetMap[x, z].preTile = new int2(-1, -1);

        // ---------------------------------------------------------
        // [단계 1] Invalidate: 끊긴 길 전파
        // ---------------------------------------------------------
        _invalidQueue.Enqueue(new int2(x, z)); 
        _affectedTilesList.Add(new int2(x, z));
        _isAffectedMask[x, z] = true;

        if (targetMap[x, z].objType > 1) // 벽이 생겼을 때만 체크
        {
            for (int i = 0; i < 8; i++)
            {
                int nx = x + dx8[i];
                int nz = z + dz8[i];
                if (nx >= 0 && nx < _mapWidth && nz >= 0 && nz < _mapHeight)
                {
                    // 아직 영향을 받지 않은 이웃 타일 확인
                    if (!_isAffectedMask[nx, nz] && targetMap[nx, nz].distanceToGoal != -1f)
                    {
                        int2 parent = targetMap[nx, nz].preTile;
                        // 그 타일의 부모가 존재할 때
                        if (parent.x != -1)
                        {
                            // "그 타일(nx,nz)이 부모(parent)로부터 올 때, 현재 위치(x,z)가 막혀서 못 오게 되었는가?"
                            // IsValidDiagonalMove가 false를 반환하면 대각선 경로가 깨진 것임
                            if (!IsValidDiagonalMove(parent.x, parent.y, nx, nz, targetMap))
                            {
                                _isAffectedMask[nx, nz] = true;
                                targetMap[nx, nz].distanceToGoal = -1f;
                                if (isRealMap) targetMap[nx, nz].preTile = new int2(-1, -1);

                                _affectedTilesList.Add(new int2(nx, nz));
                                _invalidQueue.Enqueue(new int2(nx, nz));
                                if (isRealMap) SyncSingleTileToECS(nx, nz);
                            }
                        }
                    }
                }
            }
        }

        while (_invalidQueue.Count > 0)
        {
            int2 curr = _invalidQueue.Dequeue();

            for (int i = 0; i < 8; i++)
            {
                int nx = curr.x + dx8[i];
                int nz = curr.y + dz8[i];

                if (nx >= 0 && nx < _mapWidth && nz >= 0 && nz < _mapHeight)
                {
                    // "네 부모가 나(curr)냐?" -> 나를 따라오던 자식 타일들도 모두 끊김 처리
                    if (targetMap[nx, nz].preTile.x == curr.x && targetMap[nx, nz].preTile.y == curr.y)
                    {
                        if (!_isAffectedMask[nx, nz])
                        {
                            _isAffectedMask[nx, nz] = true;
                            targetMap[nx, nz].distanceToGoal = -1f;
                            // 실제 건설일 때만 경로 데이터(preTile)를 완전히 지웁니다.
                            if (isRealMap) targetMap[nx, nz].preTile = new int2(-1, -1);

                            _affectedTilesList.Add(new int2(nx, nz));
                            _invalidQueue.Enqueue(new int2(nx, nz));
                            if (isRealMap) SyncSingleTileToECS(nx, nz);
                        }
                    }
                }
            }
        }

        // ---------------------------------------------------------
        // [단계 2] 복구 시작점 찾기
        // ---------------------------------------------------------
        foreach (var pos in _affectedTilesList)
        {
            for (int i = 0; i < 8; i++)
            {
                int nx = pos.x + dx8[i];
                int nz = pos.y + dz8[i];

                if (nx >= 0 && nx < _mapWidth && nz >= 0 && nz < _mapHeight)
                {
                    // 영향받지 않은(정상 경로가 남아있는) 타일을 주변에서 찾아 복구의 시작점으로 삼음
                    if (!_isAffectedMask[nx, nz] && targetMap[nx, nz].distanceToGoal != -1f)
                    {
                        _repairQueue.Enqueue(new int2(nx, nz));
                    }
                }
            }
        }

        // ---------------------------------------------------------
        // [단계 3] Dijkstra: 길 다시 잇기
        // ---------------------------------------------------------
        while (_repairQueue.Count > 0)
        {
            int2 curr = _repairQueue.Dequeue();

            for (int i = 0; i < 8; i++)
            {
                int nx = curr.x + dx8[i];
                int nz = curr.y + dz8[i];

                if (nx >= 0 && nx < _mapWidth && nz >= 0 && nz < _mapHeight)
                {
                    if (targetMap[nx, nz].objType <= 1 && IsValidDiagonalMove(curr.x, curr.y, nx, nz, targetMap))
                    {
                        float bias = BiasForTile(nx, nz);
                        float newDist = targetMap[curr.x, curr.y].distanceToGoal + baseCosts8[i] + bias;

                        if (targetMap[nx, nz].distanceToGoal == -1f || newDist < targetMap[nx, nz].distanceToGoal)
                        {
                            targetMap[nx, nz].distanceToGoal = newDist;
                            targetMap[nx, nz].preTile = curr;

                            _repairQueue.Enqueue(new int2(nx, nz));
                            if (isRealMap) SyncSingleTileToECS(nx, nz);
                        }
                    }
                }
            }
        }

        if (isRealMap)
        {
            DrawAllPaths(_realMapData);
            UpdatePathVisualizationColor(_pathLines, _realMapData, false);
        }

        // 디버그용
        //if (isRealMap)
        //{
        //    Debug.Log($"<color=orange><b>[Path Debug]</b> 구조물 제거 완료 - 위치: ({x}, {z})</color>");
        //    // 제거 후 스폰 지점 경로 샘플링
        //    PrintDebugPath("제거 후(After)", targetMap);
        //}
    }

    public void UpdatePathAt(int x, int z) => UpdatePathAt(x, z, _realMapData);

    private void PropagatePathUpdate(int2 pos)
    {

        float minDist = float.MaxValue;
        int2 bestPre = new int2(-1, -1);

        for (int i = 0; i < 8; i++)
        {
            int nx = pos.x + dx8[i];
            int nz = pos.y + dz8[i];
            if (nx >= 0 && nx < _mapWidth && nz >= 0 && nz < _mapHeight)
            {
                float d = MapData[nx, nz].distanceToGoal;
                if (d != -1f && IsValidDiagonalMove(pos.x, pos.y, nx, nz, MapData))
                {
                    float bias = BiasForTile(nx, nz);
                    float totalCost = d + baseCosts8[i] + bias;

                    if (totalCost < minDist)
                    {
                        minDist = totalCost;
                        bestPre = new int2(nx, nz);
                    }
                }
            }
        }

        float oldDist = MapData[pos.x, pos.y].distanceToGoal;

        if (bestPre.x != -1)
        {
            MapData[pos.x, pos.y].distanceToGoal = minDist;
            MapData[pos.x, pos.y].preTile = bestPre;
        }
        else
        {
            MapData[pos.x, pos.y].distanceToGoal = -1f;
            MapData[pos.x, pos.y].preTile = new int2(-1, -1);
        }

        if (oldDist != MapData[pos.x, pos.y].distanceToGoal)
        {
            SyncSingleTileToECS(pos.x, pos.y);
            for (int i = 0; i < 8; i++)
            {
                int nx = pos.x + dx8[i];
                int nz = pos.y + dz8[i];
                if (nx >= 0 && nx < _mapWidth && nz >= 0 && nz < _mapHeight)
                {
                    if (MapData[nx, nz].preTile.x == pos.x && MapData[nx, nz].preTile.y == pos.y)
                    {
                        PropagatePathUpdate(new int2(nx, nz));
                    }
                }
            }
        }
    }

    // 대각선 이동 시 양옆이 벽인지 체크하는 함수
    private bool IsValidDiagonalMove(int currX, int currZ, int nx, int nz, TileInfo[,] targetMap)
    {
        // 1. 직선 이동인 경우(x나 z가 하나라도 같음) 무조건 통과
        if (currX == nx || currZ == nz) return true;

        // 2. 대각선 이동일 때 인접타일이 하나라도 벽(objType > 1)이면 이동 불가
        if (targetMap[nx, currZ].objType > 1 || targetMap[currX, nz].objType > 1)
        {
            return false;
        }

        return true;
    }

    private float BiasForTile(int x, int z)
    {
        return (x + z * _mapWidth) / (_mapWidth*_mapHeight* _mapWidth * _mapHeight * 2);
    }


    public void BuildTowerDirect(int towerIdx)
    {
        int x = this.JS_X;
        int z = this.JS_Z;
        if (CheckIfAnyPathBlocked(this._simMapData)) return;

        var world = World.DefaultGameObjectInjectionWorld;
        var em = world.EntityManager;

        var configQuery = em.CreateEntityQuery(typeof(MapConfig));
        Entity configEntity = configQuery.GetSingletonEntity();
        var prefabBuffer = em.GetBuffer<TowerPrefabElement>(configEntity);

        // 1. 프리팹 엔티티 복제 
        Entity prefabEntity = prefabBuffer[towerIdx].Value;
        Entity towerEntity = em.Instantiate(prefabEntity);

        // 2. 구조적 변화 후 버퍼 재확보
        var objData = em.GetBuffer<ObjectDataElement>(configEntity);
        var objEntities = em.GetBuffer<ObjectEntityElement>(configEntity);

        // 3. 위치 설정 
        var prefabTr = em.GetComponentData<LocalTransform>(prefabEntity);
        em.SetComponentData(towerEntity, prefabTr.WithPosition(new float3(x, 0.2f, z)));

        // 4. 맵 데이터 및 상태 업데이트
        int dataIndex = z * _mapWidth + x;
        int pickingId = dataIndex + 1; // ProcessPickedID의 index = id - 1 대응
        em.SetComponentData(towerEntity, new PickingIdColor { Value = IndexToColor(pickingId) });
        objData[dataIndex] = new ObjectDataElement { Value = 100 + towerIdx };
        objEntities[dataIndex] = new ObjectEntityElement { Value = towerEntity };

        this.SetTileData(x, z, this.JS_Floor, 100 + towerIdx);
        this.UpdatePathAt(x, z);
        this.HasSelection = false;
    }

    public void RemoveObjectDirect()
    {
        if (JS_Obj <= 1) return;

        var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
        var em = world.EntityManager;
        var configQuery = em.CreateEntityQuery(typeof(MapConfig));
        Entity configEntity = configQuery.GetSingletonEntity();

        // 1. 구조적 변화 전: 타겟 엔티티와 인덱스 미리 확보
        int dataIndex = JS_Z * _mapWidth + JS_X;
        var objEntitiesInitial = em.GetBuffer<ObjectEntityElement>(configEntity);
        Entity targetEntity = objEntitiesInitial[dataIndex].Value;

        // 2. 구조적 변화 발생: 엔티티 파괴
        if (targetEntity != Entity.Null && em.Exists(targetEntity))
        {
            em.DestroyEntity(targetEntity);
        }

        // 3. [중요] 파괴 직후: 무효화된 버퍼들을 다시 가져와서 업데이트
        var objData = em.GetBuffer<ObjectDataElement>(configEntity);
        var objEntities = em.GetBuffer<ObjectEntityElement>(configEntity);

        objData[dataIndex] = new ObjectDataElement { Value = 0 };
        objEntities[dataIndex] = new ObjectEntityElement { Value = Entity.Null };

        // 4. 경로 및 데이터 초기화
        this.JS_Obj = 0;
        this.SetTileData(JS_X, JS_Z, JS_Floor, 0);
        this.UpdatePathAt(JS_X, JS_Z);

        this.HasSelection = false;
    }

    public void SetGamePhase(int phase)
    {
        GamePhase = phase;
    }

    private static float4 IndexToColor(int id)
    {
        float r = (id & 0xFF) / 255f;
        float g = ((id >> 8) & 0xFF) / 255f;
        float b = ((id >> 16) & 0xFF) / 255f;
        return new float4(r, g, b, 1f);
    }

    void OnDestroy()
    {
        _rootElement.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
    }

    public void PrintDebugPath(string label, TileInfo[,] targetMap)
    {
        if (_spawnPoints == null || _spawnPoints.Count == 0) return;

        int2 start = _spawnPoints[0]; // 첫 번째 스폰 지점 기준
        System.Text.StringBuilder sb = new System.Text.StringBuilder();

        sb.AppendLine($"<color=yellow>=== [{label}] Path Check ===</color>");

        int2 current = start;

        // [수정] 스폰 지점이 맵 밖일 경우 진입점으로 클램핑하여 에러 방지
        if (current.x < 0 || current.x >= _mapWidth || current.y < 0 || current.y >= _mapHeight)
        {
            sb.Append($"Spawn({current.x},{current.y}) -> ");
            current = new int2(Mathf.Clamp(current.x, 0, _mapWidth - 1), Mathf.Clamp(current.y, 0, _mapHeight - 1));
        }

        int safety = 0;
        while (safety++ < 1000)
        {
            // 848번 라인 에러 지점 해결
            TileInfo info = targetMap[current.x, current.y];
            sb.Append($"({current.x},{current.y})[C:{info.distanceToGoal:F5}] -> ");

            if (info.distanceToGoal == 0) { sb.Append("GOAL"); break; }
            if (info.preTile.x == -1) { sb.Append("BROKEN"); break; }

            current = info.preTile;
        }
        Debug.Log(sb.ToString());
    }

}
