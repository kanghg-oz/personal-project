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
    public void ShowRange(float3 towerPosition, quaternion towerRotation, float maxRange, float minRange, float rangeAngle, float3 rangeOffset, TowerRangeType rangeType)
    {
        if (mainRangeQuad == null || offsetRangeQuad == null) return;

        // 1. 메인 Quad 설정 (최대 사거리 경계)
        float3 mainPos = new float3(towerPosition.x, towerPosition.y+0.01f, towerPosition.z);
        mainRangeQuad.position = mainPos;
        mainRangeQuad.rotation = towerRotation * Quaternion.Euler(90f, 0f, 0f); // 바닥에 눕히기
        
        float maxDiameter = maxRange * 2f;
        mainRangeQuad.localScale = new Vector3(maxDiameter, maxDiameter, 1f);

        // 셰이더 파라미터 설정
        float angleRad = math.radians(rangeAngle) / 2f;
        mainMat.SetFloat("_Angle", angleRad);
        
        float minRatio = maxRange > 0 ? (minRange / maxRange) : 0f;
        
        // OffsetCircle일 경우 메인 Quad는 얇은 하얀색 경계선으로 표시
        if (rangeType == TowerRangeType.OffsetCircle)
        {
            mainMat.SetFloat("_MinRadiusRatio", 0.95f);
            mainMat.SetColor("_Color", new Color(1f, 1f, 1f, 0.3f)); // 반투명한 하얀색
        }
        else
        {
            mainMat.SetFloat("_MinRadiusRatio", minRatio);
            mainMat.SetColor("_Color", new Color(0f, 0.5f, 1f, 0.3f)); 
        }

        mainRangeQuad.gameObject.SetActive(true);

        // 2. Offset Quad 설정 (OffsetCircle 타입일 때만 활성화)
        if (rangeType == TowerRangeType.OffsetCircle)
        {
            float3 worldOffset = math.rotate(towerRotation, rangeOffset);
            float3 offsetPos = towerPosition + new float3(worldOffset.x, 0, worldOffset.z);
            offsetPos.y = towerPosition.y + 0.01f;
            offsetRangeQuad.position = offsetPos;
            offsetRangeQuad.rotation = towerRotation * Quaternion.Euler(90f, 0f, 0f);

            // OffsetCircle에서 실제 타격 반경을 minRange로 사용 (authoring에서 radius로 저장했었음)
            float offsetDiameter = minRange * 2f; 
            offsetRangeQuad.localScale = new Vector3(offsetDiameter, offsetDiameter, 1f);

            offsetMat.SetFloat("_Angle", math.PI); 
            offsetMat.SetFloat("_MinRadiusRatio", 0f); 
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