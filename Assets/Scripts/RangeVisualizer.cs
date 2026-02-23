using UnityEngine;
using Unity.Mathematics;

public class RangeVisualizer : MonoBehaviour
{
    [Header("Visualizers")]
    public GameObject rangeQuadPrefab; // 셰이더가 적용된 Quad 프리팹

    private Transform mainRangeQuad;   // 기본 사거리 (MaxRange 경계)
    private Transform offsetRangeQuad; // OffsetCircle용 실제 타격 범위

    private Material mainMat;
    private Material offsetMat;

    private void Awake()
    {
        if (rangeQuadPrefab != null)
        {
            // 메인 Quad 생성 및 초기화
            GameObject mainObj = Instantiate(rangeQuadPrefab, transform);
            mainRangeQuad = mainObj.transform;
            mainMat = mainObj.GetComponent<Renderer>().material;
            mainObj.SetActive(false);

            // Offset Quad 생성 및 초기화
            GameObject offsetObj = Instantiate(rangeQuadPrefab, transform);
            offsetRangeQuad = offsetObj.transform;
            offsetMat = offsetObj.GetComponent<Renderer>().material;
            offsetObj.SetActive(false);
        }
    }

    // 타워를 선택했을 때 호출
    public void ShowRange(TowerData towerData, float3 towerPosition, quaternion towerRotation)
    {
        if (mainRangeQuad == null || offsetRangeQuad == null) return;

        // 1. 메인 Quad 설정 (최대 사거리 경계)
        float3 mainPos = new float3(towerPosition.x, towerPosition.y+0.01f, towerPosition.z);
        mainRangeQuad.position = mainPos;
        mainRangeQuad.rotation = towerRotation * Quaternion.Euler(90f, 0f, 0f); // 바닥에 눕히기
        
        float maxDiameter = towerData.MaxRange * 2f;
        mainRangeQuad.localScale = new Vector3(maxDiameter, maxDiameter, 1f);

        // 셰이더 파라미터 설정
        // _Angle은 라디안으로 취급되며 좌우로 벌어지므로, degree를 라디안으로 변환 후 2로 나눔
        float angleRad = math.radians(towerData.RangeAngle) / 2f;
        mainMat.SetFloat("_Angle", angleRad);
        
        float minRatio = towerData.MaxRange > 0 ? (towerData.MinRange / towerData.MaxRange) : 0f;
        
        // OffsetCircle일 경우 메인 Quad는 얇은 하얀색 경계선으로 표시
        if (towerData.RangeType == TowerRangeType.OffsetCircle)
        {
            mainMat.SetFloat("_MinRadiusRatio", 0.95f);
            mainMat.SetColor("_Color", new Color(1f, 1f, 1f, 0.3f)); // 반투명한 하얀색
        }
        else
        {
            mainMat.SetFloat("_MinRadiusRatio", minRatio);
            // 기본 색상으로 복구 (필요하다면 원래 색상을 저장해두고 복구해야 함)
            // 여기서는 임시로 파란색 계열로 설정 (원래 프리팹의 색상에 맞게 수정 필요)
            mainMat.SetColor("_Color", new Color(0f, 0.5f, 1f, 0.3f)); 
        }

        mainRangeQuad.gameObject.SetActive(true);

        // 2. Offset Quad 설정 (OffsetCircle 타입일 때만 활성화)
        if (towerData.RangeType == TowerRangeType.OffsetCircle)
        {
            // 타워 회전을 고려하여 오프셋 위치 계산
            float3 offsetPos = towerPosition + math.rotate(towerRotation, towerData.RangeOffset);
            offsetPos.y = towerPosition.y + 0.01f;
            offsetRangeQuad.position = offsetPos;
            offsetRangeQuad.rotation = towerRotation * Quaternion.Euler(90f, 0f, 0f);

            // OffsetCircle에서 실제 타격 반경을 MinRange로 사용한다고 가정
            float offsetDiameter = towerData.MinRange * 2f; 
            offsetRangeQuad.localScale = new Vector3(offsetDiameter, offsetDiameter, 1f);

            // Offset Quad는 항상 기본 원형으로 그림 (Angle은 360도에 해당하는 라디안 값, MinRadiusRatio 0)
            offsetMat.SetFloat("_Angle", math.PI); 
            offsetMat.SetFloat("_MinRadiusRatio", 0f); 
            // 실제 타격 범위는 원래 색상(예: 파란색)으로 표시
            offsetMat.SetColor("_Color", new Color(0f, 0.5f, 1f, 0.3f));
            
            offsetRangeQuad.gameObject.SetActive(true);
        }
        else
        {
            offsetRangeQuad.gameObject.SetActive(false);
        }
    }

    public void HideRange()
    {
        if (mainRangeQuad != null) mainRangeQuad.gameObject.SetActive(false);
        if (offsetRangeQuad != null) offsetRangeQuad.gameObject.SetActive(false);
    }
}