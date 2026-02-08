using UnityEngine;
using UnityEngine.InputSystem;

public class CameraController : MonoBehaviour
{
    [Header("Input Actions")]
    public InputActionReference panAction;
    public InputActionReference rotateAction;
    public InputActionReference zoomAction;

    [Header("Control Settings")]
    public float panSpeed = 10f;
    public float rotateSpeed = 0.2f;
    public float zoomSpeed = 5.0f;

    float mapWidth;
    float mapHeight;
    float mapDiagonal;

    float cameraUpLimit;
    float cameraDownLimit;

    private Vector3 viewPointOfGround;
    private Camera _cam;

    const float FIXED_FOV = 30f;
    const float FIXED_ANGLE = 40f;

    void Awake()
    {
        _cam = GetComponent<Camera>();
        LoadMapSize();
    }

    void Start()
    {
        CalculateCameraLimits();

        Vector3 startPos = transform.position;
        startPos.y = cameraUpLimit;
        Quaternion startRot = Quaternion.Euler(FIXED_ANGLE, -20f, 0f);
        transform.SetPositionAndRotation(startPos, startRot);

        UpdateGroundPoint();
    }

    void Update()
    {
        HandleInput();
        UpdateGroundPoint();
        LimitCameraMove();
    }

    void HandleInput()
    {
        // 1. 회전
        float rotateVal = rotateAction.action.ReadValue<float>();
        DoRotate(rotateVal * rotateSpeed);

        // 2. 줌
        float zoomVal = zoomAction.action.ReadValue<float>();
        DoZoom(zoomVal * zoomSpeed);

        // 터치 2개 이상이면 이동 금지
        if (Touchscreen.current != null && Touchscreen.current.touches.Count > 1) return;

        // 3. 이동
        Vector2 panVal = panAction.action.ReadValue<Vector2>();
        DoPan(panVal);
    }

    void DoZoom(float delta)
    {
        // 휠을 밀면(Positive) -> 전진(Zoom In)
        // 휠을 당기면(Negative) -> 후진(Zoom Out)
        Vector3 targetPos = transform.position + transform.forward * delta;

        // 목표 높이가 제한을 벗어나면 Clamp
        float clampedY = Mathf.Clamp(targetPos.y, cameraDownLimit, cameraUpLimit);

        // 이동해야 할 거리 = (목표높이 - 현재높이) / forward.y
        float forwardY = transform.forward.y;
        float moveAmount = (clampedY - transform.position.y) / forwardY;

        transform.position = transform.position + transform.forward * moveAmount;
    }

    void DoPan(Vector2 delta)
    {
        Vector3 moveX = transform.right * -delta.x;
        Vector3 moveZ = transform.forward * -delta.y;
        moveZ.y = 0f;

        float heightFactor = transform.position.y / 15f;
        transform.position += (moveX + moveZ) * panSpeed * 0.01f * heightFactor;
    }

    void DoRotate(float deltaX)
    {
        Vector3 offset = transform.position - viewPointOfGround;
        Quaternion orbitRotation = Quaternion.AngleAxis(deltaX, Vector3.up);
        Vector3 newPos = viewPointOfGround + (orbitRotation * offset);
        transform.position = newPos;
        transform.Rotate(0, deltaX, 0, Space.World);
    }

    void LoadMapSize()
    {
        TextAsset csv = Resources.Load<TextAsset>("map_floor");
        string[] rows = csv.text.Trim().Split('\n');

        mapHeight = rows.Length;
        mapWidth = rows[0].Split(',').Length;
    }

    void CalculateCameraLimits()
    {
        float halfFov = FIXED_FOV * 0.5f * Mathf.Deg2Rad;

        // Distance = (TargetSize / 2) / Tan(Half_FOV)
        float maxDistance = (mapWidth * 0.5f) / Mathf.Tan(halfFov); // 맵 전체가 다 보이는 거리

        float minDistance = (1.5f) / Mathf.Tan(halfFov); // 3칸의 타일이 보이는 거리

        // 높이(Y) : Distance * Sin(Pitch)
        float pitchRad = FIXED_ANGLE * Mathf.Deg2Rad;
        float sinPitch = Mathf.Sin(pitchRad);

        cameraUpLimit = maxDistance * sinPitch;
        cameraDownLimit = minDistance * sinPitch;
    }

    void UpdateGroundPoint()
    {
        float distance3D = -transform.position.y / transform.forward.y; // 코사인
        Vector3 viewPoint = transform.position + transform.forward * distance3D;
        viewPointOfGround = new Vector3(viewPoint.x, 0, viewPoint.z);
    }

    void LimitCameraMove()
    {
        float currentMinX = 0; float currentMaxX = mapWidth - 1;
        float currentMinZ = 0; float currentMaxZ = mapHeight - 1;

        float cameraY = transform.position.y;
        float ratio = (cameraY - cameraDownLimit) / (cameraUpLimit - cameraDownLimit);

        float margin = (mapWidth - 3) / 2 * ratio;

        currentMinX += margin; currentMaxX -= margin;
        currentMinZ += margin; currentMaxZ -= margin;

        if (currentMinX > currentMaxX) currentMaxX = currentMinX = mapWidth / 2f - 0.5f;
        if (currentMinZ > currentMaxZ) currentMaxZ = currentMinZ = mapHeight / 2f - 0.5f;

        Vector3 updatePos = transform.position;

        if (viewPointOfGround.x < currentMinX) { updatePos.x += currentMinX - viewPointOfGround.x; }
        else if (viewPointOfGround.x > currentMaxX) { updatePos.x -= viewPointOfGround.x - currentMaxX; }
        if (viewPointOfGround.z < currentMinZ) { updatePos.z += currentMinZ - viewPointOfGround.z; }
        else if (viewPointOfGround.z > currentMaxZ) { updatePos.z -= viewPointOfGround.z - currentMaxZ; }

        updatePos.y = Mathf.Clamp(updatePos.y, cameraDownLimit, cameraUpLimit);
        transform.position = updatePos;
        UpdateGroundPoint();
    }

    void OnEnable()
    {
        panAction.action.Enable();
        rotateAction.action.Enable();
        zoomAction.action.Enable();

        panAction.action.started += ctx => CloseSelectionUI();
        rotateAction.action.started += ctx => CloseSelectionUI();
        zoomAction.action.started += ctx => CloseSelectionUI();
    }

    void OnDisable()
    {
        panAction.action.started -= ctx => CloseSelectionUI();
        rotateAction.action.started -= ctx => CloseSelectionUI();
        zoomAction.action.started -= ctx => CloseSelectionUI();

        panAction.action.Disable();
        rotateAction.action.Disable();
        zoomAction.action.Disable();
    }

    private void CloseSelectionUI()
    {
        if (PlayerInputManager.Instance != null)
        {
            PlayerInputManager.Instance.HasSelection = false;
        }
    }

}