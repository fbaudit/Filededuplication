<p align="center">
  <img src="src/FileDedup.App/Assets/icon.png" width="96" alt="FileDedup 아이콘" />
</p>

# FileDedup

**Windows 중복 파일 정리 + 초고속 파일 검색** — 디지털포렌식팀 내부 배포용

DoubleKiller(중복 검사)와 Everything(고속 파일명 검색)을 벤치마킹해 만든 통합 도구입니다.
자세한 설계는 [개발 계획서](docs/PLAN.md)를 참고하세요.

## 주요 기능

### ① 중복 검사
- 폴더 선택(복수, 하위 폴더 포함) 후 전체 스캔
- 판별 기준 조합: **파일명 / 크기 / 만든일자 / 수정일자 / CRC32 / MD5 / SHA-256**
  — 선택된 기준을 *모두* 만족해야 중복으로 판정
- 3단계 최적화: 크기 그룹핑 → 선두 64KB 부분해시 → 전체 해시 (대용량 폴더에서도 빠름)
- 자동 선택 규칙: 최신/오래된 파일 보존, 짧은 경로 보존, 지정 폴더 우선 보존
  — **어떤 규칙이든 그룹당 1개는 반드시 보존**
- 처리 방식: 휴지통 이동(기본) / 지정 폴더로 이동 / 영구 삭제(확인 필수)
- 포렌식 감사용 **CSV 보고서**: 경로·크기·생성/수정일·CRC32/MD5/SHA-256·처리 결과 (UTF-8 BOM, Excel 호환)

### ② 파일명 검색 (Everything 방식)
- **관리자 권한 + NTFS**: MFT를 직접 읽어(`FSCTL_ENUM_USN_DATA`) 전체 드라이브를 수 초 내 인덱싱
- 비관리자: 폴더 선택 인덱싱으로 자동 폴백
- 타이핑 즉시 검색. 문법: 공백=AND, `*`/`?` 와일드카드, `path:폴더명`, `ext:hwp`

### ③ 내용 검색
- 문서 텍스트 추출 → SQLite FTS5(trigram) 전문검색, **한국어 부분일치 지원**
- 지원 형식: txt·log·csv·md·소스코드(UTF-8/CP949 자동감지), docx·xlsx·pptx, PDF(텍스트 레이어), **HWP 5.0·HWPX**
- 증분 인덱싱(크기+수정일 변경 감지), 결과에 매칭 스니펫 표시

## 설치 및 실행

1. [Actions](../../actions) 최신 빌드의 `FileDedup-win-x64` 아티팩트(또는 릴리스)에서 `FileDedup.exe` 다운로드
2. 실행 — 설치 불필요 (self-contained 단일 exe)
3. MFT 전체 드라이브 검색을 쓰려면 **관리자 권한으로 실행** (관리자 계정이면 UAC 승격이 자동 요청됨)

설정과 내용 검색 인덱스는 `%LocalAppData%\FileDedup\`에 저장됩니다.

## 소스에서 빌드

```bash
dotnet test                                   # 45개 유닛/통합 테스트
dotnet publish src/FileDedup.App -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

## 프로젝트 구조

| 프로젝트 | 내용 |
|---|---|
| `src/FileDedup.Core` | 스캔·해시·중복판별·자동선택·삭제/보고서·파일명 인덱스·FTS5 콘텐츠 인덱스·문서 텍스트 추출기 (플랫폼 독립) |
| `src/FileDedup.Windows` | MFT 열거기(P/Invoke), 휴지통(SHFileOperation) |
| `src/FileDedup.App` | Avalonia 11 GUI (한국어, 4개 탭) |
| `tests/FileDedup.Core.Tests` | xunit 테스트 |

## Windows 실환경 수동 테스트 체크리스트

자동 테스트가 커버하지 못하는 Windows 전용 기능은 배포 전 실물 PC에서 확인하세요.

- [ ] 관리자 권한 실행 시 "전체 드라이브 인덱싱 (MFT)" 버튼이 보이고, C: 인덱싱이 수 초 내 완료되는지
- [ ] 비관리자 실행 시 MFT 버튼이 숨겨지고 폴더 인덱싱으로 동작하는지
- [ ] 휴지통 이동 후 휴지통에서 복원 가능한지
- [ ] 대용량 폴더(10만 파일 이상) 검사 시 진행률 표시·취소가 동작하는지
- [ ] CSV 보고서가 Excel에서 한글 깨짐 없이 열리는지
- [ ] HWP/HWPX/PDF 문서가 내용 검색에서 검색되는지

## 주의사항

- **영구 삭제는 복구할 수 없습니다.** 기본값(휴지통 이동 + CSV 보고서)을 유지하세요.
- 스캔 이미지 PDF(OCR 필요), 암호화/DRM 문서는 내용 추출이 되지 않습니다.
- 파일이 변경된 뒤에는 파일명/내용 인덱스를 다시 실행해 갱신하세요.
- 아이콘은 클로드 스타일을 참고해 자체 제작한 것으로, Anthropic 공식 로고가 아닙니다.
