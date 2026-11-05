# 와이패스(WiPass) 개인정보 처리방침

최종 수정: 2026-11-05 · 제작: jomwalimu (개인 제작, 교육청 공식 프로그램 아님)

## 요약
와이패스는 **인터넷으로 아무 정보도 보내지 않습니다.** 서버도, 계정도, 광고도, 사용 통계 수집도 없습니다.

## 이 PC에만 저장하는 정보
| 정보 | 쓰는 곳 | 저장 방식 |
|---|---|---|
| 학교 와이파이 이름, 장소 이름 | 장소 전환 | 설정 파일 (`%APPDATA%\WiPass\settings.json`) |
| 장소별 와이파이 비밀번호 | 그 장소 와이파이 연결 | 윈도우 데이터 보호(DPAPI)로 **현재 윈도우 사용자만 풀 수 있게 암호화** |
| 장소에서 본 공유기 주소(BSSID) | 도착한 장소 알아보기 | 설정 파일 |
| 동작 기록 | 문제 해결 | `%APPDATA%\WiPass\logs` (비밀번호는 기록하지 않음) |

## 위치 권한
윈도우 11은 주변 와이파이·공유기 정보를 프로그램에 알려 주기 전에 **위치 권한**을 확인합니다. 와이패스는 이 권한으로 주변 공유기 주소만 읽어 어느 장소인지 판단하며, **위도·경도 같은 위치는 읽지도 저장하지도 않습니다.**

## 다른 곳으로 보내는 정보
없습니다. 사용자가 직접 "문의용 진단 정보 저장"을 눌러 만든 파일을 GitHub 문의에 올리는 경우에만 그 파일 내용이 공개됩니다. 이 파일에는 비밀번호가 들어 있지 않지만 장소 이름이 포함될 수 있으니 올리기 전에 확인해 주세요.

## 삭제
앱을 제거하면 저장된 정보가 함께 지워집니다. (GitHub 설치판은 제거할 때 데이터 삭제 여부를 묻습니다)

## 문의
https://github.com/jomwalimu/wi-pass/issues

---

# WiPass Privacy Policy (English)

WiPass sends **no data over the internet**: no servers, accounts, ads or analytics.
It stores, on this PC only: the shared Wi-Fi name, place names, per-place Wi-Fi passwords (encrypted with Windows DPAPI for the current user), access point addresses (BSSIDs) seen at each place, and a local activity log without passwords.
Windows location permission is used only so Windows reports nearby Wi-Fi access points; WiPass never reads or stores geographic coordinates.
Uninstalling the app removes the stored data. Contact: https://github.com/jomwalimu/wi-pass/issues
