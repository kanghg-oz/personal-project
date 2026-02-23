import { render, View, Label, Button, ScrollView } from "onejs-react";
import { useThrottledSync } from "onejs-react";

const pm = CS.UnityEngine.GameObject.Find("InputManager")
const pmInput = pm?.GetComponent("PlayerInputManager")
let towerInfoLoaded = false

function App() {
    if (pmInput?.RefreshTowerInfos && (!towerInfoLoaded || !pmInput.TowerInfosJson)) {
        pmInput.RefreshTowerInfos()
        if (pmInput.TowerInfosJson && pmInput.TowerInfosJson.length > 0) {
            towerInfoLoaded = true
        }
    }

    const hasSelection = useThrottledSync(() => pmInput.HasSelection, 100)
    const mouseX = useThrottledSync(() => pmInput.JS_MouseX, 100)
    const mouseY = useThrottledSync(() => pmInput.JS_MouseY, 100)
    const objType = useThrottledSync(() => pmInput.JS_Obj, 100)
    const gamePhase = useThrottledSync(() => pmInput.GamePhase, 100)
    const isPathBlocked = useThrottledSync(() => pmInput.IsPathBlocked, 100)
    const towerRangeType = useThrottledSync(() => pmInput.JS_TowerRangeType, 100)
    const isEditMode = useThrottledSync(() => pmInput.IsEditMode, 100)
    const towerInfosJson = useThrottledSync(() => pmInput.TowerInfosJson, 500)
    const towerInfos = towerInfosJson ? JSON.parse(towerInfosJson).Items ?? [] : []

    const handlePhaseChange = () => {
        const nextPhase = gamePhase === 0 ? 1 : 0;
        pmInput.SetGamePhase(nextPhase);
        pmInput.HasSelection = false;
    }

    // [유지] 선택이 없을 때: 좌상단 버튼만 개별 View로 렌더링
    if (!hasSelection) {
        return (
            <View style={{ position: "absolute", top: 20, left: 20, width: 200 }}>
                <Button onClick={handlePhaseChange} style={{ padding: 20, backgroundColor: gamePhase === 0 ? "#4CAF50" : "#F44336" }}>
                    <Label text={gamePhase === 0 ? "건설 모드" : "전투 모드"} />
                </Button>
            </View>
        )
    }

    return (
        <>
            <View style={{ position: "absolute", top: 20, left: 20, width: 200 }}>
                <Button onClick={handlePhaseChange} style={{ padding: 20, backgroundColor: gamePhase === 0 ? "#4CAF50" : "#F44336" }}>
                    <Label text={gamePhase === 0 ? "건설 모드" : "전투 모드"} />
                </Button>
            </View>

            {gamePhase === 0 && (
                <>
                    {hasSelection && objType === 0 && (
                        <TowerSlidePanel towerInfos={towerInfos} isBlocked={isPathBlocked} />
                    )}
                    {hasSelection && objType > 1 && (
                        <>
                            <MenuButton emoji="🗑️" x={mouseX - 40} y={mouseY - 40} onClick={() => pmInput.RemoveObjectDirect()} />
                            {(towerRangeType === 1 || towerRangeType === 3) && (
                                <MenuButton 
                                    emoji="🔄" 
                                    x={mouseX - 40} 
                                    y={mouseY - 130} 
                                    onClick={() => pmInput.IsEditMode = true} 
                                    isBlocked={false}
                                    style={{ backgroundColor: isEditMode ? "#4CAF50" : "#222" }}
                                />
                            )}
                        </>
                    )}
                </>
            )}
        </>
    )
}

function MenuButton({ emoji, x, y, onClick, isBlocked, style }: { emoji: string, x: number, y: number, onClick: () => void, isBlocked?: boolean, style?: any }) {
    return (
        <View style={{ position: "absolute", left: x, top: y, width: 80, height: 80 }}>
            <Button onClick={onClick} style={{
                width: "100%",
                height: "100%",
                backgroundColor: "#222",
                borderWidth: 3,
                borderColor: "white",
                borderRadius: 40,
                justifyContent: "center",
                alignItems: "center",
                opacity: isBlocked ? 0.3 : 1,
                ...style
            }}>
                <Label text={emoji} style={{ fontSize: 32 }} />
            </Button>
        </View>
    )
}

function TowerSlidePanel({ towerInfos, isBlocked, onRemove }: { towerInfos: any[], isBlocked: boolean, onRemove?: () => void }) {
    return (
        <View style={{ position: "absolute", left: 0, right: 0, bottom: 0, height: "25%", paddingLeft: 12, paddingRight: 12, paddingTop: 0, paddingBottom: 0, backgroundColor: "#111" }}>
            <ScrollView
                style={{ width: "100%", height: "100%" }}
            >
                <View style={{ flexDirection: "row", alignItems: "stretch", height: "100%" }}>
                    {towerInfos.map((tower, index) => (
                        <TowerButton
                            key={index}
                            label={`타워 ${tower.Name}`}
                            detailLines={[
                                ``,
                                `공격력 ${Number(tower.Damage).toFixed(1)}`,
                                `사거리 ${Number(tower.MaxRange).toFixed(1)}`,
                                `공속 ${Number(tower.AttackSpeed).toFixed(2)}`,
                                `타입 ${tower.AttackType}`
                            ]}
                            isBlocked={isBlocked}
                            onClick={() => !isBlocked && pmInput.BuildTowerDirect(tower.Index)}
                        />
                    ))}
                </View>
            </ScrollView>
        </View>
    )
}

function TowerButton({ label, detailLines, onClick, isBlocked }: { label: string, detailLines: string[], onClick: () => void, isBlocked: boolean }) {
    return (
        <View style={{ width: 200, height: "100%", marginRight: 12 }}>
            <Button onClick={onClick} style={{
                width: "100%",
                height: "100%",
                backgroundColor: "#222",
                borderWidth: 2,
                borderColor: "white",
                borderRadius: 12,
                justifyContent: "center",
                alignItems: "center",
                opacity: isBlocked ? 0.3 : 1
            }}>
                <View style={{ alignItems: "center", paddingLeft: 6, paddingRight: 6, height: "100%", justifyContent: "center" }}>
                    <Label text={label} style={{ fontSize: 14, color: "white" }} />
                    <View style={{ height: 10 }} />
                    {detailLines.map((line, idx) => (
                        <Label key={idx} text={line} style={{ fontSize: 12, color: "#ddd", marginBottom: idx < detailLines.length - 1 ? 4 : 0 }} />
                    ))}
                </View>
            </Button>
        </View>
    )
}

render(<App />, __root)
