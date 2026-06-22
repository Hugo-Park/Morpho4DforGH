# Morpho4D — P0-1 / P0-2 + 유틸리티 사용 가이드

이 문서는 이번 브랜치에서 추가된 컴포넌트의 사용법과 워크플로를 정리합니다.

> ⚠️ **먼저 읽기**: 이 코드는 Rhino SDK가 없는 환경에서 작성되어 **컴파일·테스트되지 않았습니다.**
> Visual Studio에서 빌드 후 사용하세요. 일부 RhinoCommon API에서 사소한 수정이 필요할 수 있습니다.

---

## 0. 한눈에 보기

- **P0-1 다재료**: `Assign Material`, `Bilayer Material` 로 복셀마다 다른 재료 지정.
- **P0-2 차등 굽힘**: 솔버의 스프링 rest length를 양 끝점 팽창의 **대칭 평균**으로 고침 → bilayer가 휨.
- **유틸리티**: 시간(Timer), 공간 자극장(Spatial Field), 외력(Load), 수렴 진단(History Monitor),
  애니메이션 프리컴퓨트(Player) + 캡처(Recorder) + 인코딩(Exporter).

---

## 1. P0-1 + P0-2 : Bilayer 굽힘 데모 (가장 중요)

자기변형(self-morphing) 시트를 만드는 정석 흐름입니다.

```
[얇은 판형 Brep]──┐
                  ├─ Brep to Voxel (Size, 기본재료) ── Voxels
[Size]────────────┘
[기본 재료(아무거나)]

Voxels ── Bilayer Material ── Voxels
            │  Base Plane = 판 두께의 중앙 (예: XY plane을 Z로 살짝 올림)
            │  Top Material    = 팽창 Hydrogel (Max Swelling Ratio > 1, 예 1.5)
            │  Bottom Material = 비팽창층 (Hydrogel Max Swelling Ratio = 1.0,
            │                    또는 SMP를 Tg 미만 온도로 두기)

Voxels ── Anchor (한쪽 모서리 Points) ── Voxels   ← 날아가지 않게 고정
[Base Breps]─┐
Voxels ──────┼─ Morpho Solver ── Mesh / Points / Energy History
[Stimulus]───┤
[Time]───────┘
```

**왜 휘는가**: 위층 스프링은 1.5배 길어지려 하고 아래층은 그대로 → 미스매치를 최소화하려고 시트가 만다.
P0-2의 대칭 rest length 덕분에 위/아래 경계 스프링의 목표 길이가 일관되게 계산됩니다.

**현실적 기대치**: 굽힘은 **정성적**으로 나옵니다(메쉬 밀도 의존). 정량 검증(Timoshenko 곡률 일치)은
P0-3(강성 단위 보정) 이후로 미뤄둔 상태입니다.

> 팁: 얇고(두께 ≈ 1~2 voxel) 넓은 판일수록 굽힘이 잘 보입니다. Voxel Size를 충분히 작게.

---

## 2. 다재료 (영역 지정) — `Assign Material`

부분 영역에만 다른 재료를 주고 싶을 때. `Brep to Voxel`로 기본 재료를 깐 뒤 체인으로 연결합니다.

```
Voxels ── Assign Material ── Voxels
            Material = 덮어쓸 재료
            Region   = 닫힌 Brep(예: 박스). 그 안에 들어오는 복셀만 재료가 바뀜
```
여러 번 체인하면 3가지 이상 재료도 구성 가능(마지막에 쓴 게 그 복셀에 적용).

---

## 3. 시간 — `Simulation Timer`

```
Total Time, Time Step, Include Zero ── Simulation Timer ── Times(list), Count
```
`Times`를 `Simulation Player`의 Time 입력으로 사용. Hydrogel은 시간이 지날수록 확산이 진행되어 점점 팽창합니다.

---

## 4. 공간 자극장 — `Spatial Field Generator`

자극(온도)이 공간적으로 불균일할 때.
```
Voxels, Min/Max, Direction(또는 Source Point) ── Spatial Field Generator ── Stimulus
```
- **선형 모드**: Direction 축을 따라 Min→Max 그라디언트.
- **방사 모드**: Source Point를 연결하면 그 점이 가장 뜨겁고 멀수록 식음.
- ⚠️ 현재 모델에서 **온도는 SMP(sigmoid)에만** 작용합니다. Hydrogel 확산은 온도 비의존이라 영향 없음
  (Hydrogel용 습도장은 확산 모델 확장이 필요 — 향후 과제).

---

## 5. 외력 — `Load Applicator`

자체 변형 외에 중력/적재 하중을 더할 때. 솔버가 `E = -F·x`로 목적함수에 반영합니다.
```
Voxels, Force(Vector), [Points] ── Load Applicator ── Voxels
```
- Points 비우면 **전 복셀 균일 하중**(중력), Points 주면 그 근처 복셀만.
- SET(덮어쓰기) 시맨틱 → 재실행해도 누적되지 않음. 하중 제거는 영벡터를 전체 적용.

---

## 6. 수렴 진단 — `Solver History Monitor`

L-BFGS가 잘 수렴했는지 확인.
```
Morpho Solver.Energy History ── Solver History Monitor ── Log / E0 / Ef / Reduction% / Converged / Graph
```
- `Graph`(점들: x=평가 인덱스, y=에너지)를 그대로 뷰포트에 찍거나 Polyline으로 이으면 수렴 곡선.
- 단위는 L-BFGS 이터레이션이 아니라 **목적함수 평가(line search 포함)** → 곡선이 톱니처럼 보일 수 있음(정상).

---

## 7. 애니메이션 — Player / Recorder / Exporter

### 핵심 아이디어
`Simulation Player`가 **모든 시점을 미리 계산해 캐싱**합니다. 그래서 Frame 슬라이더에 라이노 기본
**Animate**를 걸어도 솔버가 루프 안에서 돌지 않아 **멈춤·프레임 깨짐이 없습니다**. (요청하신 문제의 해결책)

### 7-1. `Simulation Player`
```
Base Breps, Voxels, Stimulus, Times, Frame, Recompute
   ── Mesh / Points / Frame Count / Current Time / All Meshes
```
- `Recompute`를 False→True로 토글하면 (재)베이크. 입력 값을 바꾼 뒤엔 토글하세요.
- `Frame`에 **Integer 슬라이더**를 연결(0 ~ Frame Count-1). 슬라이더 우클릭 → Animate로 시퀀스 재생.
- 진행형(progressive): 각 프레임은 이전 프레임 형상에서 이어서 최적화(warm-start)됩니다.

### 7-2. `Viewport Recorder` (PNG 시퀀스)
```
Folder, File Prefix, Frame Index, Width, Height, Overlay Text, Capture ── File Path / Status
```
- `Frame Index`에 Player와 **같은 Frame 슬라이더**를 연결, `Overlay Text`에 `Current Time` 등을 문자열로.
- `Capture=True`로 두고 Frame 슬라이더 Animate → 매 프레임 `prefix_0000.png` 저장.
- PNG 시퀀스는 어떤 영상툴에도 들어가는 가장 견고한 형식입니다.

### 7-3. `Media Exporter` (GIF/MP4)
```
Frame Folder, File Prefix, Output Path, Format(mp4/gif), FPS, FFmpeg Path, Export ── Status / Command
```
- **ffmpeg가 설치되어 있어야** 합니다(PATH 또는 경로 지정). 자체 인코더를 만들지 않았습니다.
- ffmpeg가 없으면: 출력된 `Command`를 터미널에서 직접 실행하거나, **PNG 시퀀스를 Premiere Pro의
  이미지 시퀀스 기능**으로 합치세요(고화질 MP4엔 이 편이 낫습니다).
- 인코딩 동안 GH 캔버스가 잠깐 멈춥니다(수동 Export 버튼이므로 의도된 동작).

> 솔직한 평가: 애니메이션 3종 중 **진짜 가치는 Player의 프리컴퓨트·캐싱**입니다(멈춤 문제 해결).
> Recorder(PNG)는 견고합니다. Exporter는 편의용 래퍼일 뿐이라, 본격 영상은 Premiere가 정답입니다.
> (요청하신 대로, 평가 후 Exporter는 안 써도 무방합니다.)
