# S3 Browser インストーラー

Windows 用インストーラー (.exe) を生成するためのスクリプト一式。

## 前提

- Windows 10/11 (x64)
- .NET 10 SDK (ビルド時)
- [Inno Setup 6](https://jrsoftware.org/isdl.php) (インストーラー生成時)
  - 既定の場所 (`C:\Program Files\Inno Setup 6\` または `C:\Program Files (x86)\Inno Setup 6\`) にインストールされていれば自動検出
  - `winget install JRSoftware.InnoSetup` でも入手可能

## ビルド

リポジトリルートで:

```powershell
pwsh installer\build.ps1
```

実行内容:

1. `dotnet publish` で `S3Browser.exe` を **self-contained / single-file / ReadyToRun** で生成 → `publish\win-x64\S3Browser.exe`
2. Inno Setup でインストーラー化 → `dist\S3Browser-1.0.0-Setup.exe`

`publish` のみ行う場合:

```powershell
pwsh installer\build.ps1 -SkipInstaller
```

## 出力

| パス | 内容 |
| --- | --- |
| `publish\win-x64\S3Browser.exe` | ランタイム同梱の実行ファイル(約150–200MB)。コピーするだけで動作する |
| `dist\S3Browser-1.0.0-Setup.exe` | Windows インストーラー |

## インストーラーの動作

- 既定では **ユーザー領域**にインストール(`%LocalAppData%\Programs\S3 Browser\`、管理者権限不要)
- インストールウィザードで「全ユーザー / 現在のユーザー」を選択可能
- スタートメニューにショートカット作成、デスクトップアイコンはオプション
- アンインストーラーが「アプリと機能」に登録される
- 言語は日本語/英語

## バージョン更新

新リリースのたびに `installer\setup.iss` の `MyAppVersion` を更新してください。`MyAppId` の GUID は **変更しないこと**(変更すると別アプリ扱いになり、古いバージョンが残ります)。
