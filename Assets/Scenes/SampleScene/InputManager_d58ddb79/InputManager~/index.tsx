import { render, View, Label, Button, ScrollView } from "onejs-react";
import { useThrottledSync } from "onejs-react";

const pm = CS.UnityEngine.GameObject.Find("InputManager")
const pmInput = pm?.GetComponent("PlayerInputManager") as any
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
    const selDamage = useThrottledSync(() => pmInput.JS_SelectedDamage, 100)
    const selRange = useThrottledSync(() => pmInput.JS_SelectedRange, 100)
    const playerGold = useThrottledSync(() => pmInput.JS_PlayerGold, 100)
    const playerHP = useThrottledSync(() => pmInput.JS_PlayerHP, 100)
    const selDamageCost = useThrottledSync(() => pmInput.JS_SelectedDamageUpgradeCost, 100)
    const selRangeCost = useThrottledSync(() => pmInput.JS_SelectedRangeUpgradeCost, 100)
    const towerInfosJson = useThrottledSync(() => pmInput.TowerInfosJson, 500)
    const towerInfos = towerInfosJson ? JSON.parse(towerInfosJson).Items ?? [] : []

    const handlePhaseChange = () => {
        const nextPhase = gamePhase === 0 ? 1 : 0;
        pmInput.SetGamePhase(nextPhase);
        pmInput.HasSelection = false;
    }

    return (
        <>
            {/* Top Info Bar */}
            <View style={{ position: "absolute", top: 20, left: "50%", marginLeft: -150, width: 300, flexDirection: "row", justifyContent: "space-between", backgroundColor: "rgba(0,0,0,0.6)", padding: 10, borderRadius: 10 }}>
                <View style={{ flexDirection: "row", alignItems: "center" }}>
                    <Label text="💰" style={{ fontSize: 24, marginRight: 8 }} />
                    <Label text={playerGold ? playerGold.toString() : "0"} style={{ color: "#FFD700", fontSize: 24 }} />
                </View>
                <View style={{ flexDirection: "row", alignItems: "center" }}>
                    <Label text="❤️" style={{ fontSize: 24, marginRight: 8 }} />
                    <Label text={playerHP ? playerHP.toString() : "0"} style={{ color: "#FF4444", fontSize: 24 }} />
                </View>
            </View>

            <View style={{ position: "absolute", top: 20, left: 20, width: 200 }}>
                <Button 
                    onClick={handlePhaseChange} 
                    style={{ padding: 20, backgroundColor: gamePhase === 0 ? "#4CAF50" : "#F44336" }}
                >
                    <Label text={gamePhase === 0 ? "건설 모드" : "전투 모드"} />
                </Button>
            </View>

            {gamePhase === 0 && (
                <>
                    {hasSelection && objType === 0 && (
                        <TowerSlidePanel towerInfos={towerInfos} isBlocked={isPathBlocked} playerGold={playerGold} />
                    )}
                    {hasSelection && objType > 1 && (
                        <>
                            {objType >= 100 && (
                                <View style={{ position: "absolute", left: mouseX + 80, top: mouseY - 40, backgroundColor: "rgba(0,0,0,0.7)", padding: 10, borderRadius: 10 }}>
                                    <Label text={`공격력: ${selDamage}`} style={{ color: "white", fontSize: 18 }} />
                                    <Label text={`사거리: ${selRange.toFixed(1)}`} style={{ color: "white", fontSize: 18 }} />
                                    <View style={{ height: 4, backgroundColor: "white", marginTop: 4, marginBottom: 4, opacity: 0.3 }} />
                                    <Label text={`공격력 업: ${selDamageCost === -1 ? "MAX" : selDamageCost + "G"}`} style={{ color: selDamageCost === -1 ? "#AAA" : (playerGold >= selDamageCost ? "#FFD700" : "#FF6666"), fontSize: 14 }} />
                                    <Label text={`사거리 업: ${selRangeCost === -1 ? "MAX" : selRangeCost + "G"}`} style={{ color: selRangeCost === -1 ? "#AAA" : (playerGold >= selRangeCost ? "#FFD700" : "#FF6666"), fontSize: 14 }} />
                                </View>
                            )}

                            <MenuButton emoji="🗑️" x={mouseX - 130} y={mouseY + 50} onClick={() => pmInput.RemoveObjectDirect()} />

                            {objType >= 100 && (
                                <>
                                    {(towerRangeType === 1 || towerRangeType === 3) && (
                                        <MenuButton 
                                            emoji="🔄" 
                                            x={mouseX - 40} 
                                            y={mouseY - 130} 
                                            stayOpen={true}
                                            onClick={() => pmInput.IsEditMode = true} 
                                            isBlocked={false}
                                            style={{ backgroundColor: isEditMode ? "#4CAF50" : "#222" }}
                                        />
                                    )}
                                    <MenuButton 
                                        emoji="⚔️" 
                                        x={mouseX - 40} 
                                        y={mouseY - 40} 
                                        onClick={() => pmInput.UpgradeDamage()} 
                                        stayOpen={true} 
                                        isBlocked={selDamageCost === -1 || playerGold < selDamageCost} 
                                    />
                                    <MenuButton 
                                        emoji="📏" 
                                        x={mouseX + 50} 
                                        y={mouseY + 50} 
                                        onClick={() => pmInput.UpgradeRange()} 
                                        stayOpen={true} 
                                        isBlocked={selRangeCost === -1 || playerGold < selRangeCost} 
                                    />
                                </>
                            )}
                        </>
                    )}
                </>
            )}
        </>
    )
}

function MenuButton({ emoji, x, y, onClick, isBlocked, style, stayOpen }: { emoji: string, x: number, y: number, onClick: () => void, isBlocked?: boolean, style?: any, stayOpen?: boolean }) {
    return (
        <View style={{ position: "absolute", left: x, top: y, width: 80, height: 80 }}>
            <Button 
                onPointerDown={() => stayOpen && pmInput.RefreshInteractionTimer()}
                onClick={onClick} 
                style={{
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

function TowerSlidePanel({ towerInfos, isBlocked, onRemove, playerGold }: { towerInfos: any[], isBlocked: boolean, onRemove?: () => void, playerGold: number }) {
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
                                `가격 ${tower.BuildCost}G`
                            ]}
                            isBlocked={isBlocked || playerGold < tower.BuildCost}
                            onClick={() => !isBlocked && playerGold >= tower.BuildCost && pmInput.BuildTowerDirect(tower.Index)}
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
            <Button 
                onClick={onClick} 
                style={{
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
