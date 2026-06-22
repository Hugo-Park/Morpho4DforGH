# CHANGELOG — P0-1 / P0-2 + 유틸리티 브랜치

이 브랜치의 모든 변경은 **추가적(additive)·하위호환**을 목표로 했습니다. 기존 컴포넌트의 입력/출력
시그니처는 유지되며(출력 파라미터는 뒤에 인덱스만 추가), 기존 그래스호퍼 그래프는 그대로 동작합니다.
아래는 머지 리뷰를 위한 변경 요약입니다.

---

## 1) 수정된 기존 파일 (5개)

### `Morpho4D.Models/VoxelCell.cs`
- **추가 필드** `public Vector3d appliedLoad { get; set; } = Vector3d.Zero;`
  - LoadApplicator 유틸리티가 외력을 주입하는 곳. 기본 0이므로 기존 거동 영향 없음.

### `Morpho4D.Solver/Stimulus.cs`
- **추가 필드** `public Func<Point3d,double> temperatureField` (기본 null)
- **추가 메서드** `double TemperatureAt(VoxelCell voxel)`
  - 공간 필드가 있으면 위치 기반 온도, 없으면 기존 scalar `temperature` 반환 → **완전 하위호환**.

### `Morpho4D.Models/Material.cs`
- `SmpMat.evaluateState` 가 `stimulus.temperature` 대신 `stimulus.TemperatureAt(voxel)` 사용.
  - 공간 필드가 없으면 동일 동작. `voxel.currentTemp = temp` 기록 한 줄 추가.
  - **HydrogelMat 는 미변경** (확산 모델은 온도 비의존).

### `Morpho4D.Solver/Solver.cs`
- **[P0-2] 대칭 스프링 rest length** : `getSpringEnergy`, `getSpringForce` 두 곳에서
  `targetDist = initialDist * inputVoxels[pair.idA].expansionForce`  →
  `avgExpansion = (idA.expansionForce + idB.expansionForce) * 0.5; targetDist = initialDist * avgExpansion;`
  - 기존엔 idA 한쪽 팽창만 써서 순서 의존이었음(잠재 버그). 다재료/bilayer에서 굽힘이 올바르게 나오려면 필수.
  - 에너지/그래디언트 양쪽을 동일하게 고쳐 일관성 유지.
- **외부 하중 항** : `calculateTotalEnergy`에 `E_ext = -F·x` 추가, `calculateTotalGradient`에
  물리력 `+F` 누적(기존 isFixed 0처리 및 최종 `Multiply(-1)` 이전). `appliedLoad`가 0이면 영향 없음.
- **에너지 히스토리** : `public List<double> energyHistory` 추가. `execute()` 시작 시 `Clear()`,
  `calculateTotalEnergy` 마지막에 `Add(totalEnergy)`. 수렴 모니터링용.

### `Components.Solver/MorphoSolverComponent.cs`
- **출력 파라미터 1개 추가** (인덱스 2) : `"Energy History" (EH)` — `solver.energyHistory`를 내보냄.
  - 기존 출력 0(Mesh)·1(Points)은 그대로. 새 출력만 추가 → 기존 연결 유지.

---

## 2) 추가된 새 컴포넌트 (9개)

| 컴포넌트 | 파일 | 카테고리 | 역할 |
|---|---|---|---|
| Assign Material | `Components.Material/AssignMaterial.cs` | Material | 영역(Brep) 안 복셀에 재료 덮어쓰기 (다재료) |
| Bilayer Material | `Components.Material/BilayerMaterial.cs` | Material | 평면 기준 상/하층 두 재료 분리 (bilayer → 굽힘) |
| Simulation Timer | `Components.Solver/SimulationTimer.cs` | Solver | 전체시간을 dt로 쪼갠 시간 샘플 리스트 |
| Spatial Field Generator | `Components.Stimulus/SpatialFieldGenerator.cs` | Stimulus | 공간 온도장 생성 → Stimulus에 주입 (SMP 작용) |
| Load Applicator | `Components.Solver/LoadApplicator.cs` | Solver | 중력/적재 하중을 복셀에 주입 |
| Solver History Monitor | `Components.Solver/SolverHistoryMonitor.cs` | Solver | 에너지 수렴 진단(로그/통계/그래프) |
| Simulation Player | `Components.Animation/SimulationPlayer.cs` | Animation | 전 타임스텝 프리컴퓨트·캐싱 → 프레임 즉시 출력 |
| Viewport Recorder | `Components.Animation/ViewportRecorder.cs` | Animation | 뷰포트 PNG 시퀀스 캡처(+오버레이) |
| Media Exporter | `Components.Animation/MediaExporter.cs` | Animation | PNG 시퀀스 → GIF/MP4 (ffmpeg 호출) |

- 새 폴더 `Components.Animation/` 추가. SDK-style csproj가 `**/*.cs`를 자동 포함하므로 csproj 수정 불필요.
- 모든 새 컴포넌트 GUID는 기존과 충돌 없음(검증 완료).

---

## 3) 알려진 한계 / 주의

- **빌드/테스트 안 됨**: 이 코드는 Rhino/Grasshopper SDK가 없는 환경에서 작성되었습니다. Visual Studio에서
  빌드해야 하며, RhinoCommon API 시그니처(특히 `RhinoView.CaptureToBitmap(Size)`)에서 사소한 수정이
  필요할 수 있습니다.
- **P0-2 굽힘은 정성적(qualitative)**: 현재 복셀은 surface-mesh 정점이라 두께 방향 커플링이 경계에 한정됩니다.
  bilayer 차등 팽창으로 휘긴 하지만 메쉬 밀도 의존적입니다. **정량적 Timoshenko 검증**에는 후속 작업
  (P0-3 강성 단위 보정, 이상적으로는 volumetric voxelization + grid connectivity)이 필요합니다.
- **기존 선반영 버그(미수정)**: `ConstructHydrogel`/`ConstructSMP`의 inspection이 Poisson 비 자리에
  `materialName`을 출력하는 복붙 버그가 있습니다(기능 영향 없음). 이번 브랜치 범위 밖이라 두었습니다.
