# 마이크로소프트 스토어 제출 자료

파트너 센터에 붙여 넣을 내용입니다. (언어: 한국어)

## 1. 가격 및 사용 가능 여부
- 가격: **무료**
- 시장: 대한민국 (원하면 전체 시장)
- 공개 범위: 처음에는 **비공개 대상(Private audience)** 으로 베타 참가자에게만 → 베타 후 공개

## 2. 속성
- 범주: **유틸리티 및 도구**
- 개인정보 처리방침 URL: https://github.com/jomwalimu/wi-pass/blob/main/docs/PRIVACY.md
- 웹 사이트: https://github.com/jomwalimu/wi-pass
- 지원 연락처: https://github.com/jomwalimu/wi-pass/issues
- 시스템 요구 사항: 무선랜(와이파이) 장치 필요
- 이 제품은 개인 정보에 액세스하거나 수집하나요? → **예** (와이파이 비밀번호를 이 PC에만 암호화 저장, 위치 권한 사용) — 그래서 개인정보 처리방침 URL을 넣습니다.

## 3. 연령 등급 (IARC 설문)
- 범주: 앱(유틸리티) / 폭력·성·약물·도박·사용자 간 소통·위치 공유·디지털 구매 모두 **아니요**
- 예상 결과: 전체 이용가 (3+)

## 4. 스토어 등록 정보 (한국어)

**제품 이름**: 와이패스

**짧은 설명** (검색 결과에 보임)
> 이름은 같고 비밀번호만 다른 학교 와이파이, 장소에 도착하면 자동으로 바꿔 줍니다.

**설명**
> 여러 학교·기관을 오가며 일하는데, 장소마다 와이파이 이름은 같고 비밀번호만 다르신가요?
> (예: 교육청 공통 무선망 JBEDU_WiFi_T)
>
> 윈도우는 같은 이름의 와이파이를 하나로 기억해서, 장소를 옮길 때마다 저장된 와이파이를 지우고 비밀번호를 다시 입력해야 합니다. 와이패스가 이 일을 대신합니다.
>
> ■ 도착하면 자동 전환 — 주변 공유기를 보고 어느 장소인지 판단해 그 장소 비밀번호로 연결합니다. 처음 가는 곳에서는 저장된 비밀번호를 차례로 시도하고, 한 번 연결되면 기억합니다.
> ■ 한 번에 직접 전환 — 작업표시줄 아이콘을 누르면 장소 버튼이 나옵니다.
> ■ 노트북 핫스팟 자동 켜기 (선택) — 학교 와이파이에 연결되면 스마트폰이 노트북을 통해 인터넷을 쓰게 합니다. 기관의 무선망 보안 지침을 먼저 확인하세요.
> ■ 윈도우 11 스타일 화면, 어두운 모드 지원
>
> 개인정보: 인터넷으로 아무 정보도 보내지 않습니다. 비밀번호는 이 PC에만 암호화해 저장합니다.
>
> 지원하지 않는 경우: 아이디와 비밀번호를 함께 쓰는 와이파이(802.1X), 장소마다 와이파이 이름이 다른 경우(윈도우가 원래 알아서 처리)
>
> 위치 권한: 윈도우 11은 위치 권한이 켜져 있어야 주변 와이파이 정보를 프로그램에 알려 줍니다. 와이패스는 위치를 기록하지 않습니다.
>
> 개인이 만든 무료 오픈소스 프로그램이며, 교육청 공식 프로그램이 아닙니다. 소스 코드와 문의: github.com/jomwalimu/wi-pass

**제품 기능** (항목별)
- 장소에 도착하면 그 장소 비밀번호로 자동 연결
- 작업표시줄 빠른 창에서 장소 한 번에 바꾸기
- 연결 중 잘못 누른 장소 바로 바로잡기
- 학교 와이파이 연결 시 노트북 핫스팟 자동 켜기 (선택)
- 비밀번호는 이 PC에만 암호화 저장, 인터넷 전송 없음
- 밝게/어둡게 테마

**검색어** (최대 7개): 와이파이, 학교 와이파이, 와이파이 자동 전환, 비밀번호, 핫스팟, 교사, 늘봄

**스크린샷** (1366×768 이상, 최소 1장 · 권장 4장) — 윈도우에서 캡처
1. 빠른 창 (장소에 연결된 상태)
2. 설정 창 — 장소
3. 시작하기 1단계 — 와이파이 고르기
4. 자동 전환 알림
⚠️ 비밀번호가 보이는 화면은 쓰지 않습니다.

**저작권**: © 2026 jomwalimu. MIT License.

## 5. 제출 옵션 → 인증 참고 사항 (Notes for certification, 영어로)

```
WiPass is a free, open-source (MIT) Windows Forms tray utility: https://github.com/jomwalimu/wi-pass

Purpose: some organizations (e.g. Korean public schools) use the SAME Wi-Fi name (SSID) at several sites
but a DIFFERENT password at each site. Windows remembers only one password per SSID, so users must forget
and re-enter the network every time they move. WiPass stores one password per place (encrypted with DPAPI,
on this PC only) and switches the saved Wi-Fi profile automatically when the user arrives at a place.

Capabilities:
- runFullTrust: classic .NET Framework 4.8 desktop app (Windows Forms) that calls the Native Wi-Fi API
  (wlanapi.dll: WlanSetProfile, WlanConnect, WlanGetNetworkBssList) to replace the Wi-Fi profile.
- wiFiControl: required by NetworkOperatorTetheringManager for the optional "turn on mobile hotspot" feature
  (off by default) and for Wi-Fi access.
- location: on Windows 11 24H2+, nearby access point (BSSID) information requires location consent.
  WiPass never reads or stores geographic coordinates.

No network communication, no account, no telemetry. Privacy policy:
https://github.com/jomwalimu/wi-pass/blob/main/docs/PRIVACY.md

How to test: the app opens a 3-step setup window on first run. Pick any Wi-Fi network name, add a place
with that network's password, finish. Left-click the tray icon to open the quick panel and switch places.
Full automatic switching needs several sites sharing one SSID, which a test lab usually doesn't have.
```

## 6. 제출 순서
1. 파트너 센터 → 와이패스 → **제출 시작**
2. 가격 및 사용 가능 여부 → 속성 → 연령 등급 → **패키지**: `WiPass-Store-버전.msix` 업로드
3. 스토어 등록 정보(한국어) → 제출 옵션(인증 참고 사항) → **스토어에 제출**
4. 인증 결과 메일 (보통 1~3일). 반려되면 사유를 보내 주세요.
