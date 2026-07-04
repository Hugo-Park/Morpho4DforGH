# feature-Solver (2) 대비 변경사항 정리

원본은 `Morpho4DforGH-feature-Solver (2)`, 비교 대상은 `00_SOURCE_feature-Solver`. `diff -u`로 직접 대조한 내용이라 라인 번호랑 코드는 실제 파일 기준.

## 새로 생긴 컴포넌트들

전체 18개 파일이 새로 추가됐다.

재료 쪽 (`Components.Material`)
- `ConstructPLA.cs` — PLA 같은 수동 재료(PassiveMat) 만드는 컴포넌트
- `BilayerMaterial.cs` — 복셀을 평면 기준으로 위/아래 나눠서 재료 다르게 할당
- `SetFiberDirection.cs` — 활성 복셀에 섬유 방향이랑 최대변형률 지정
- `AssignMaterial.cs` — Brep 영역 기준으로 재료 한 번에 할당

분석/검증 (`Components.Analysis`, `Components.Validation`)
- `MorphMetrics.cs` — 곡률이랑 반경 측정 (3점 원 피팅)
- `CalibrationSolver.cs` — 실측 곡률 넣으면 epsMax 역산 (golden-section search)
- `MeasurementImporter.cs` — Fiji에서 뽑은 CSV 불러오기
- `ValidationOverlay.cs` — Kabsch 정렬해서 RMSE 계산

제작 (`Components.Fabrication`)
- `PrintPathPlanner.cs` — 레이어별 지그재그 경로 생성
- `GCodeExporter.cs` — Bambu X2D 듀얼노즐용 G-code 뽑기

진단 (`Components.Diagnostics`)
- `GradientCheck.cs` — 유한차분으로 gradient 맞는지 검증 (아래 마지막 항목 참고, 아직 문제 있음)
- `SolverHistoryMonitor.cs` — L-BFGS 에너지 수렴 곡선 확인용

애니메이션/유틸 (`Components.Utility`)
- `LoadApplicator.cs` — 외부 하중(중력 등) 설정
- `SpatialFieldGenerator.cs` — 위치별로 다른 온도 분포 만들기
- `SimulationTimer.cs`, `SimulationPlayer.cs` — 타임스텝 애니메이션 재생
- `ViewportRecorder.cs`, `MediaExporter.cs` — 뷰포트 캡처해서 GIF로 뽑기

## Solver.cs — 실제로 바뀐 부분

590줄에서 714줄로 늘었다(+124줄). diff 순서대로.

**53행 근처** — `energyHistory` 필드 하나 추가됨.
```csharp
public List<double> energyHistory { get; } = new List<double>();
```
SolverHistoryMonitor가 이거 읽어서 수렴 곡선 그림.

**222행 근처** — 여기가 제일 크게 바뀐 부분인데, `setUpFromGrid`랑 `buildGridConnectivity`가 통째로 추가됐다(82줄). 기존 `setUp(voxels, referenceMesh)`은 mesh 위상으로 이웃/힌지를 잡는데, 문제는 voxel(체적 격자)이랑 referenceMesh(별도로 만든 테셀레이션)가 인덱스 대응이 될 이유가 없다는 거였다. 그래서 mesh 없이 RTree로 순수 거리 기반으로 연결성 잡는 경로를 새로 만든 거다. 6-이웃(거리≈size)은 스프링pair+neighbor로 등록하고, 18-이웃(대각, size·√2)은 스프링pair만, 힌지는 6-이웃 중에 벡터합이 0에 가까운 정반대쌍만. 기존 `setUp`이랑 mesh 기반 메서드 3개는 안 지우고 그대로 남겨뒀다 — 대체가 아니라 병행.

**291행, 333행 근처** — execute() 시작할 때 energyHistory 초기화하고, L-BFGS 콜백 안에서 매 iteration 에너지 값을 여기 쌓는 코드 두 줄 추가.

**393행, 426행 근처** — calculateTotalEnergy랑 calculateTotalGradient에 외부 하중 항 추가. 에너지 쪽은 `-F·x` 포텐셜, gradient 쪽은 그 미분인 `-F`를 그대로 반영. LoadApplicator가 세팅하는 appliedLoad 값 쓰는 거다.

**453행, 492행 근처** — getSpringEnergy랑 getSpringForce에 비등방 eigenstrain 항 추가한 부분. 원래 등방 팽창(avgExpansion)만 있던 targetDist 계산에 방향성 있는 변형(aniso)을 더했다.
```csharp
Vector3d pairDir = vb.initialPoint - va.initialPoint;
if (pairDir.Length > 1e-9) pairDir.Unitize();
double aniso = 0.0;
if (va.isActive) { double d = pairDir * va.fiberDir; aniso += 0.5 * va.epsMax * va.activationFraction * d * d; }
if (vb.isActive) { double d = pairDir * vb.fiberDir; aniso += 0.5 * vb.epsMax * vb.activationFraction * d * d; }
double targetDist = initialDist * (avgExpansion + aniso);
```
이 수식이 에너지랑 힘 계산 양쪽에 토씨 하나 안 틀리고 똑같이 들어가 있다. 그래야 gradient가 안 어긋나니까.

**542행** — 이게 진짜 버그였던 지점.
```csharp
double k = E * Math.Pow(s, 3);
```
이게
```csharp
double k = E * Math.Pow(s, 3) / 12.0;
```
로 바뀜. getBendingEnergy는 원래부터 /12.0이 있었는데 getBendingForce만 빠져 있어서, 힘이 에너지보다 12배 뻣뻣하게 계산되고 있었다. 한 줄짜리 수정인데 파급력은 컸던 버그.

**572행 근처** — DebugTotalEnergy, DebugTotalGradient 두 개 public 래퍼 추가. 원래 private였던 calculateTotalEnergy/Gradient를 GradientCheck에서 부를 수 있게 열어준 거고, 원본 메서드 자체는 여전히 private으로 캡슐화 유지.

## 다른 파일들

**VoxelCell.cs** — 필드 6개 추가: fiberDir, epsMax, activationFraction, isActive (비등방 관련), isGridVoxel (격자 모드 플래그), appliedLoad (하중).

**Material.cs** — SmpMat.evaluateState가 크게 바뀜. 원래는 Tg 넘으면 모듈러스를 강제로 0.01로 덮어쓰고 힌지 목표각을 현재각으로 고정해버리는 코드가 있었는데, 이게 있으면 활성도 계산 자체가 안 되고 곡률 구동도 막혀버린다. 이 블록 통째로 걷어내고 대신 sigmoid 값을 그대로 유지하면서 activationFraction(0~1 정규화)을 계산하는 걸로 바꿨다. 그리고 PassiveMat 클래스가 파일 맨 끝에 새로 추가됨 — 변형 안 하는 수동 재료용.

**Stimulus.cs** — temperatureField, TemperatureAt() 추가. SpatialFieldGenerator가 위치별 온도 넣어줄 때 쓰는 거.

**MorphoSolverComponent.cs** — solver.setUp(voxels, referenceMesh) 대신 solver.setUpFromGrid(voxels) 쓰도록 바뀌었고, 출력 메쉬 만드는 방식도 바뀜. 원래는 referenceMesh 정점에다 voxel 결과를 그대로 대입하는 식이었는데(인덱스 대응 보장 안 됨), 이제는 voxel 하나하나를 독립적인 박스 메쉬로 만들어서 합치는 BuildDeformedBoxMesh를 쓴다. 기존 방식은 "이론상 도달 안 하는 안전망"으로 남겨둠.

## 짚고 넘어가야 할 것

GradientCheck.cs 234행, 이거 지난번에 고치기로 했던 부분인데 이번 zip 확인해보니 그대로 남아있다.
```csharp
var analytic = solver.DebugTotalGradient(x0).Multiply(-1.0);
```
DebugTotalGradient가 이미 +∇E를 반환하니까 여기서 -1 곱하면 부호가 뒤집혀서 수치 gradient(+∇E)랑 정반대가 돼버린다. 로컬에서 고친 게 이 zip에 반영이 안 됐거나 재압축 전 걸 올린 것 같은데, `.Multiply(-1.0)` 부분만 지우면 된다.

---

파일 수는 17개에서 35개로, Solver.cs는 590줄에서 714줄로 늘었다. 물리 버그는 굽힘 힘 계산 쪽 하나 확정 수정됐고, GradientCheck 부호 문제 하나가 아직 남아있다.
