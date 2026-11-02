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
