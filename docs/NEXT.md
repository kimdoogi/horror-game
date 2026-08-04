# 하강 — 남은 작업 전부 (2026-08-04 기준)

새 대화에 항목별로 붙여넣는 용도. 각 항목은 그 자체로 완결이라 이 대화의 기억 없이 작업 가능하다.

## 모든 요청 앞에 붙일 공통 머리말

```
/Users/doogi/horror-game 의 하강 프로젝트. Unity 6000.3.21f1, 20인 미로 하강 경주.
B1 외곽에서 출발, 층마다 중심의 투하구로 아래층 외곽에 착지, B8 중심에 먼저 닿으면 승리.
괴물이나 총에 잡히면 죽는 게 아니라 자기 출발점으로 돌아가서 계속 달린다 (RaceState.ReportCaught).

규칙: 소스가 아니라 결과물(씬 파일, FBX, 빌드된 DLL, 테스트 XML)에서 검증할 것.
유니티는 프로젝트를 단일 잠금하므로 병렬 실행 금지. dotnet은 잠금을 안 잡는다 (~/.dotnet/dotnet).
검증 기준선: dotnet 350/350 · PlayMode 98/99 (빨강 1 = AudioSceneTests, 기존) ·
EditMode 94/95 (빨강 1 = 에셋 묘비, 정직한 빨강) · 맵은 -forceWrite 없이 생성되고
감사 220마커/3482쌍/100%/섬8/괴물 212-212/시작 36-36 이어야 한다. 이 숫자가 움직이면 그게 회귀다.
작업 끝나면 커밋하고 origin main에 푸시.
```

---

## A. 게임 완성 (코드)

### A1. 총 배선 마무리 — 규칙과 에셋은 이미 커밋됨 (503201a)

상태: `Core/Race/Gunplay.cs`(한 발, 사거리=손전등 12 m, 맞으면 ReportCaught) 완성, 테스트 6개 통과.
`Gun_Held.fbx`/`Gun_Pickup.fbx` 생성됨 (0.261 m, 500tris, tools/blender/gen_gun.py).
`Gameplay/Race/GunPickup.cs` 작성돼 있으나 아무도 배치·참조하지 않음.

붙여넣을 요청:

```
총 배선을 마무리해줘. Gunplay 규칙과 Gun_Held/Gun_Pickup.fbx, GunPickup.cs는 커밋돼 있다.

1. 배치 — MapSceneBuilder가 층마다 몇 개의 막다른 골목(ReachProbe 표식이 이미 152개 있다)에
   Gun_Pickup을 놓게. 몇 개를 어느 층부터 놓을지는 §07 위협 곡선으로 논증해서 정하고,
   생성기 로그에 개수를 찍을 것. 드레싱 keep-out 계약(착지 기둥·문 스윙)을 침범하면 안 된다.
2. 줍기 — PlayerInteractor의 기존 조준+키 경로로 GunPickup.Take. 주운 사람 손에 Gun_Held를
   붙이고(오른손 본), HUD에 남은 한 발 표시.
3. 발사 — 마우스 좌클릭, 카메라 레이캐스트로 대상 주자 판정, §13대로 호스트가
   Gunplay.Fire로 판정. 맞은 주자는 자기 출발점으로 (MatchDirector.SendBackToTheStartLine과
   같은 값). 명중이든 빗나감이든 총은 소모되고 총성은 §06 소음 규칙에 태울 것
   — 괴물이 듣는 큰 소리다.
4. 네트워크 — NetPlayer에 '총 들고 있음' SyncVar 1비트, 발사는 Command→호스트 판정→
   맞은 클라이언트에 복귀 지시. NetSocketTests의 바이트-카운트 기법으로 인프로세스로는
   통과 못 하는 테스트를 쓸 것.
5. 든 자세 — gen_runner.py에 GunIdle/GunWalk 클립 추가 (기존 gait solver 재사용, 오른팔만
   앞으로). 다른 플레이어가 12 m에서 "총 들었다"를 실루엣으로 읽는 게 목적이다.
   재출력 후 bones=13 actions=11 을 ASSET_REPORT에서 확인.

끝나면 두 인스턴스 하네스(LocalTwoInstance.Launch)로 실제 두 프로세스에서
줍기→발사→복귀가 로그에 찍히는 것까지 확인해줘.
```

### A2. 순위·하강·잡힘이 와이어를 안 건넌다 — 멀티플레이 최대 구멍

상태: NetPlayer는 위치·회전·손전등·스태미나만 동기화. **하강(투하구)·완주·잡힘은 각 기계의
MatchDirector가 로컬로만 기록한다.** 두 기계의 순위표가 서로 다르고, §13(호스트 권위)이
경주의 핵심 데이터에 적용되지 않고 있다. RaceDirector/RaceState는 기계마다 따로 산다.

붙여넣을 요청:

```
경주 데이터(§02)를 §13대로 호스트 권위로 만들어줘. 지금은 하강·완주·잡힘을 각 기계의
MatchDirector가 자기 RaceState에 로컬로 기록해서, 두 기계의 순위표가 서로 다르다.

- 하강: 클라이언트의 투하구 진입은 Command로 호스트에 보고, 호스트가 RaceState에 기록하고
  결과(순위표 변경)를 모두에게 방송. "내가 먼저 닿았다"는 §13이 명시한 거짓말 1순위다.
- 완주: RaceDirector.CheckFinish는 호스트에서만 판정.
- 잡힘: 각 기계의 괴물이 자기 로컬 주자를 잡는 지금 구조는 클라이언트 권위라 §13 위반이다.
  괴물 시뮬레이션을 호스트로 올리든, 잡힘 보고만 호스트 확정으로 하든 정하고 논증할 것.
- HUD(RaceHud)는 호스트가 방송한 순위표만 그린다.
검증: 두 인스턴스 하네스로 한쪽이 투하구에 떨어졌을 때 양쪽 로그의 순위가 같은 것,
그리고 NetSocketTests 기법의 테스트로 하강 보고가 실제 소켓을 건넌 것.
```

### A3. 다른 플레이어가 미끄러진다 — 원격 주자 애니메이션 없음

상태: NetRunnerBody가 러너 비주얼을 복사하지만 스스로 "복사본은 애니메이션 안 된다"고 문서에
적어둠. 원격 주자는 Runner.fbx 몸으로 미끄러져 다닌다. 로컬 주자는 클립 9개로 애니메이션됨.

붙여넣을 요청:

```
원격 주자를 애니메이션시켜줘. NetRunnerBody가 만드는 복사 몸에 Animator가 없어서 다른
플레이어가 전부 미끄러져 보인다. NetPlayer가 이미 위치를 동기화하니 이동 속도를 미분해서
PlayerAnimatorDriver와 같은 클립 선택(Idle/Walk/Run, 속도비 재생)을 원격 몸에도 돌리면 된다.
클립은 Runner.fbx의 것을 로드 (SoloPlaytest.LoadModelClips 참고, GUID 918afe0115b754998910749f3f085ceb).
검증: 두 인스턴스 하네스에서 한쪽을 움직였을 때 반대쪽 화면의 몸이 걷는 것 — 스크린샷으로.
```

### A4. 근접 음성이 없다 — 마이크가 코드에 존재하지 않음

상태: 규칙(Core/Voice/VoiceRules: 속삭임 4 m·대화 12 m·외침 30 m, 괴물이 들음)과
MatchDirector.VoiceEffort 훅은 있는데, **마이크 캡처·전송 코드가 0줄이다.**
"가까운 사람끼리 마이크" 요구사항이 통째로 미구현.

붙여넣을 요청:

```
근접 음성을 실제로 구현해줘. Core/Voice/VoiceRules(속삭임/대화/외침, 감쇠, 괴물 청취)와
MatchDirector.VoiceEffort 훅은 있고 마이크·전송이 없다.
- 전송: Steam이 있으면 Steam Voice(Steamworks.NET 이미 있음), 로컬 KCP 경로에서는
  Unity Microphone → 16kHz 모노 압축 → Mirror unreliable 채널. 어느 쪽이든 VoiceRules의
  거리 감쇠와 벽 차폐(OccludedFraction 0.62)를 수신측에서 적용.
- 말하면 VoiceEffort가 §06 소음으로 들어가는 배선 확인 (괴물이 듣는다).
- HUD에 '누가 말하는 중' 표시 (RaceHud).
검증: 두 인스턴스에서 한쪽 마이크 입력이 반대쪽 스피커로 나오는 것. 헤드리스면 사인파를
마이크 대신 주입해서 수신 버퍼에 도달함을 테스트로.
```

### A5. 발소리·룸톤이 전부 무음 — AudioSceneTests가 이것 때문에 빨강

상태: 씬에 FloorSurfaceTag 컴포넌트가 0개 → FloorSurfaces.Sample이 항상 None →
"§12: 발소리가 몇 층인지 알려준다"(8층 8재질)가 통째로 죽어 있고, 로그에
"No footstep clip set for surface 'None'". PlayMode 유일한 빨강의 원인.

붙여넣을 요청:

```
발소리를 살려줘. MapSceneBuilder가 바닥 타일에 FloorSurfaceTag를 안 붙여서 씬에 0개다
(AudioSceneTests.TheSoloPlaytestScene_ComesOutAudible이 그래서 빨강).
BuildFloorSlab/타일 배치 지점에서 층 재질(Concrete/Wood/Metal/Gravel/Tile/Carpet/Water/Earth)에
맞는 FloorSurfaceTag를 붙이고, Assets/Audio/Footsteps의 기존 재질별 wav가 재생되는지,
층을 내려가면 발소리가 바뀌는지 확인. 완료 기준: AudioSceneTests 초록, PlayMode 99/99.
```

### A6. 잡힘/피격 피드백 — 지금은 무언의 순간이동

상태: 잡히면 로그 한 줄과 함께 즉시 B1. 화면 효과·소리·전환 없음. 러너의 Death 클립은
이제 안 쓰임(죽음이 없음). HUD가 TimesCaught를 안 보여줌.

붙여넣을 요청:

```
잡힘/피격 피드백을 만들어줘. 지금은 괴물이나 총에 맞으면 소리 없이 즉시 B1로 순간이동한다.
- 잡히는 순간: 괴물 grab 클립은 이미 재생됨. 화면을 0.5초 암전 → B1에서 밝아짐.
  §01의 투하구 낙하가 "반 초의 어둠"이니 같은 문법이다.
- 소리: 잡힘 스팅 하나 (tools/audio 파이프라인, 기존 gen_*.py 참고).
- 러너의 Death 클립을 '잡힘 스태거'로 용도 변경하거나 gen_runner.py에서 제거하고 재출력.
- RaceHud에 좌석별 TimesCaught 표시 — B6에 있던 사람이 갑자기 B1인 이유를 순위표가 말해야 한다.
```

---

## B. 콘텐츠·밸런스 (설계 판단 필요)

### B1. §12 규칙 3개가 면제(waiver)로 출시 중 + 주자 테스트 밴드 결정

상태: zone-diagonal(81.3 m vs 상한 40 — 구조적으로 불가능), sight-break-spacing(95 m vs 4.4),
open-adjacent-to-maze(최대 방 7.1 m vs 바닥 15 m). 셋 다 B-007 면제로 맵이 써진다.
주자 테스트 10/10 TooEasy·720/720 도망 가능은 협동 시절 계기라는 논증이 있음.
**잡히면 복귀 규칙으로 경제가 바뀌었으니 밴드 자체를 다시 정해야 한다.**

붙여넣을 요청:

```
§12를 경주 게임 기준으로 다시 써줘. 지금 zone-diagonal/sight-break-spacing/open-adjacent-to-maze
세 규칙이 B-007 면제로 우회되고 있고, 주자 테스트 밴드(50~70% 도망 가능)는 잡힘=탈락이던
협동 시절 계기다. 이제 잡힘=B1 복귀라서 괴물의 무게가 다르다.
- 두 인스턴스로 직접 플레이해보고(§14 Q1: 추격이 재밌는가) 판단할 것.
- 각 규칙을 경주 기준으로 다시 유도하거나 폐기하고, 면제 목록(MapSceneGenerator.KnownFailingRules)을
  비우는 게 목표. 규칙을 완화해서 초록 만드는 게 아니라, 경주가 실제로 요구하는 값을 §01에서
  유도해서 규칙을 다시 쓰는 것.
- docs/game-design.md §12와 GameConstants 유도 주석까지 함께.
```

### B2. 미로가 매판 똑같다

상태: 로비가 씨앗을 합의해 뿌리지만 씬이 미리 구워져 있어 씨앗은 출발 위치만 섞는다.
맵 지식이 영구 자산이 되어 반복 플레이가 죽는다.

붙여넣을 요청:

```
매판 다른 미로를 만들어줘. 지금은 씬이 미리 구워져 있어 로비 씨앗이 출발 위치만 섞는다.
두 가지 경로를 비교 논증하고 하나를 구현할 것:
(a) 미리 N벌 굽기 — MapSceneGenerator를 씨앗 N개로 돌려 씬 N개를 만들고 로비 씨앗으로 선택.
    NavMesh가 씬마다 함께 구워지므로 안전하지만 빌드가 커진다 (씬 하나 ~11 MB × N).
(b) 런타임 생성 — RadialStorey/DescentMap은 엔진 독립이라 돌릴 수 있지만, NavMesh 런타임
    베이크(NavMeshSurface.BuildNavMesh)의 비용과, 감사 게이트를 런타임에 어떻게 대체할지가 문제.
어느 쪽이든 모든 기계가 같은 씬을 로드해야 한다 (§13). 감사 기준선은 씨앗마다 통과해야 한다.
```

### B3. 에셋 묘비 잔여 10건 — 전부 이름뿐, Blender 재출력 필요

상태: Runner.fbx.meta의 Carry/CarryHeavy/CarryIdle 클립(운반 삭제됨), Dress_Crate* 이름 3종,
Dressing.manifest의 clue_faces 키(이제 로더의 거부 장치), textures.json 2개.
EditMode 유일한 빨강 = 이 파일들을 정직하게 가리키는 것.

붙여넣을 요청:

```
에셋 묘비를 초록으로 만들어줘 (검사를 약화시키지 말고 파일을 고쳐서).
- gen_runner.py에서 Carry/CarryHeavy/CarryIdle 클립을 빼고 재출력 (bones=13 유지,
  height 1.7500 ±1mm 게이트 통과 확인). A1의 GunIdle/GunWalk와 같이 하면 재출력 한 번.
- gen_dressing.py에서 Dress_Crate* 를 Dress_Case* 로 개명하고 재출력, manifest 갱신.
- Textures/Player.textures.json · Ghost.textures.json 은 참조 0이면 삭제.
완료 기준: PivotAssetTombstoneTests 초록, EditMode 95/95.
```

### B4. 트레일러가 규격 미달

상태: docs/store/party.mp4 = 1280×720·3.00초·1.62 Mbps. 밸브 요구 1920×1080·30-60fps·5,000+ Kbps.
스크린샷 10장과 캡슐 11장은 규격 일치 확인됨.

붙여넣을 요청:

```
스팀 트레일러를 다시 찍어줘. tools/render/ 의 기존 1920x1080 렌더 파이프와
docs/store/ 의 샷리스트를 하강(경주) 기준으로 갱신해서, 1920x1080·30fps·5000Kbps+ 로
30~60초. 내용: 로비→20인 출발→어둠 속 관문→투하구 낙하→괴물 조우→B1 복귀→B8 결승.
두 인스턴스 하네스로 실제 플레이 장면을 캡처할 것.
```

---

## C. 출시 인프라

### C1. IL2CPP가 이 맥에서 깨져 있음 — **사장님이 직접, 이것만 하면 빌드가 나온다**

원인: /Library/Developer/CommandLineTools/usr/include/c++/v1/ 에 낡은 항목 11개가 SDK보다
먼저 검색돼 cmath 실패. 8월 1일엔 됐으므로 회귀. 비밀번호가 필요해서 AI가 못 함.

터미널에서:

```
sudo rm -rf /Library/Developer/CommandLineTools && sudo xcode-select --install
```

끝나면 새 대화에 붙여넣을 요청:

```
IL2CPP를 고쳤으니 릴리스 빌드를 만들어줘. ./tools/ci/build.sh mac release 로 macOS 유니버설
IL2CPP, 가능하면 Windows도. 빌드 리포트의 "shippable on Steam" 줄을 그대로 인용하고,
dist/ 에 steam_appid.txt가 들어가지 않는 것(밸브가 제거 요구, 이전에 새던 버그),
ShippableOnSteam 판정이 App ID 480을 잡아내는 것까지 확인.
```

### C2. 스팀 계정 작업 — 사장님만 가능, 시간이 걸리는 순서대로

1. **오늘: 앱 크레딧 $100 결제** → 30일 의무 대기 시작 (오늘 결제해도 최속 출시 9월 초)
2. 신원 확인 2~7 영업일
3. Coming Soon 페이지 (문구는 경주용으로 다시 써져 있음, docs/store/) — 공개 최소 2주
4. 콘텐츠 설문 — **주의: tools/blender/source/monster_creature_base.glb ·
   monster_vessel_base.glb 의 출처/라이선스가 기록에 없음.** 사장님이 어디서 받았는지
   docs/ART.md에 적어야 설문에 답할 수 있다.
5. 검수 3~5 영업일 (출시 7일 전 제출 권장)
6. 출시는 수동 클릭

### C3. 20인 실증 — 아직 최대 동시 실측 2

상태: 소켓 20개 수락·21번째 거부는 테스트로 증명. 실제 사람/프로세스 20은 0회.

붙여넣을 요청:

```
20인을 실증해줘. LocalTwoInstance를 확장해서 한 기계에서 N인스턴스(-horror-client × N)를
띄우는 스트레스 모드를 만들고, 8~12인스턴스로 한 판을 완주시켜 (a) 전원 몸 있음,
(b) 출발 겹침 0, (c) 순위표 일치, (d) 프레임/대역폭 수치를 로그로 뽑아줘.
진짜 20인은 스팀 Playtest 브랜치에서 사람으로 해야 하니 그 전 단계까지.
```

### C4. CI 필수 체크 — 사장님이 GitHub에서 클릭

docs/CI.md에 절차 문서화돼 있음. GitHub 저장소 Settings → Branches → main 보호 규칙에서
"core tests (dotnet)"을 required로. (이게 없어서 빨간 스위트 위로 "초록"이라는 커밋이
세 번 푸시된 적 있음.)

---

## D. 열린 설계 결정 하나 — 괴물 기절

`MonsterAgent.Stun`이 호출자 0인 채로 빌드에 들어 있다 (두뇌·애니메이터 상태·오디오 뱅크 포함).
§04 섬광수가 유일한 발동원이었고 삭제됨. 선택지:
(a) 총의 두 번째 용도로 부활 — 사람 대신 괴물에 쏘면 몇 초 기절. "한 발을 누구에게 쓰나"가 생김.
(b) 통째로 삭제 — Stun 경로·Stunned 애니 상태·오디오 뱅크·임포트 규칙 함께.
어느 쪽이든 반쯤 남은 지금이 최악. 결정만 내려주면 한 요청으로 끝난다:

```
괴물 기절을 (살려서 총에 붙여줘 / 통째로 지워줘) — MonsterAgent.Stun 호출자 0,
Stunned 애니메이터 상태·오디오 뱅크·MonsterStunSeconds 상수까지 한 몸이다.
```

---

## 우선순위 요약

지금 게임이 되게 하는 것: **A2 (순위 동기화) > A1 (총) > A3 (원격 애니) > A5 (발소리) > A6 (피드백)**
지금 게임을 재밌게 하는 것: **B1 (밸런스 재유도) > B2 (매판 다른 미로) > A4 (음성)**
출시를 가능하게 하는 것: **C1 (sudo 한 줄) > C2 ($100, 오늘) > C3 > B4 > B3 > C4**
