# 실행 배치파일 완료 문서 (back)

> 작업번호 02 · `run.bat` 제작 결과 요약

## 변경 파일

- `run.bat` (프로젝트 루트, 신규)

## 동작 방식

- `cd /d "%~dp0"`로 프로젝트 루트로 이동한다.
- `where dotnet`으로 확인하고, 없으면 .NET 9 SDK 설치 안내 후 `pause`, `exit /b 1`.
- `dotnet build src\EasyCapture.App\EasyCapture.App.csproj -c Release -v q`로 빌드하고, 실패하면 안내 후 `pause`, `exit /b 1`.
- `src\EasyCapture.App\bin\Release\net9.0-windows\EasyCapture.exe`가 있으면 `start ""`로 분리 실행하고 종료, 없으면 안내 후 `pause`.
- 트레이 앱이라 창이 안 뜨는 게 정상이라는 안내를 출력한다.
- 중복 실행은 앱의 SingleInstance가 처리한다.

## 검증 결과

- 프로젝트 폴더 기준 `cmd /c run.bat`: 종료코드 0, 5초 뒤 EasyCapture 프로세스 1개 생존·응답 중.
- 현재 폴더 `C:\`에서 실행: 종료코드 0, 5초 뒤 프로세스 1개 생존.
- 두 시험 모두 `Stop-Process` 종료 후 잔여 프로세스 0개.
- 파일 검사: 비ASCII 바이트 0, CRLF 줄바꿈.

## 미확인

- 빌드 실패, dotnet 미설치, exe 없음 경로는 재현하지 않았다.
- 트레이 아이콘의 화면 표시는 확인하지 않았다.

## 개선 내용 (2026-11-02 22:31)

> 실행 중인 EasyCapture를 빌드 전에 종료하고 시작 후 생존을 확인하도록 run.bat을 개선했다.

### 변경 배경

- 실행 중인 EasyCapture.exe가 출력 exe/dll을 잠가 빌드가 MSB3021/MSB3027로 실패했다.
- 앱이 SingleInstance(뮤텍스)라 구 인스턴스가 떠 있으면 새 프로세스가 알림 없이 종료되어 새 빌드가 적용되지 않았다.

### 새 동작

- 빌드 전 tasklist로 이미지 이름 EasyCapture.exe만 확인하고, 없으면 아무 출력 없이 넘어간다.
- 실행 중이면 taskkill /IM(정상 종료 요청) 후 2초 대기, 남아 있으면 taskkill /F /IM, 이후 3초까지 확인한다.
- 끝내 종료되지 않으면 [ERROR] Could not close EasyCapture... 안내 후 pause, exit /b 1.
- 시작 후 약 2초 뒤 생존을 확인해, 없으면 [WARN] EasyCapture exited right after start... 경고만 출력한다(종료코드는 0 유지).

### 안전성 판단

- 종료 대상은 이미지 이름이 EasyCapture.exe인 프로세스뿐이다(Debug 빌드 실행본도 같은 이름이라 함께 종료됨).
- 정상 종료 요청은 창 없는 트레이 앱(ShutdownMode=OnExplicitShutdown)에서 효과가 없어(요청은 성공하나 프로세스 유지) 실제로는 2초 뒤 강제 종료로 간다.
- 종료 시점에만 저장하는 데이터는 없다: App.OnExit/ExitApp은 자원 해제만 하고, SettingsStore.Save와 CaptureLibrary(캡처 파일·색인)는 변경 즉시 임시 파일 후 교체 방식으로 디스크에 쓴다.
- 저장 도중 강제 종료되어도 임시 파일 후 교체 방식이라 기존 파일은 유지된다.

### 검증 결과

- 정적 검사: 비ASCII 바이트 0, CR 92개·LF 92개 일치.
- 미실행 상태에서 cmd /c run.bat: 종료코드 0, 5초 뒤 프로세스 1개.
- 실행 중 상태에서 재실행: "Closing it before the build..."·"EasyCapture closed." 출력, 파일 잠금 없이 빌드 성공, 이전 PID와 다른 새 PID로 실행, 종료코드 0.
- 시험 후 Stop-Process로 정리해 잔여 0개, EasyCapture 외 프로세스는 종료하지 않았다.

### 미확인

- 종료 실패(권한 등) 경로와 [WARN] 경고 경로는 재현하지 않았다.
- 한글 외 로케일에서의 tasklist 동작은 확인하지 못했다(이미지 이름 매칭만 사용하므로 문제없을 것으로 판단).