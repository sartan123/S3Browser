# S3 Browser

Windows エクスプローラー風の操作で Amazon S3 を扱える Windows デスクトップアプリ。

- **言語/フレームワーク**: C# / .NET 10 / WPF
- **MVVM**: CommunityToolkit.Mvvm
- **AWS**: AWSSDK.S3 v4 / AWSSDK.SecurityToken v4
- **認証**: `aws login`(AWS Sign-In 一時認証)・アクセスキー直指定・名前付きプロファイル・既定の認証チェーン

## 主な機能

- 左ペイン TreeView(バケット → フォルダーを遅延ロード)+ 右ペイン ListView(名前/更新日時/種類/サイズ/ストレージクラス)
- ツールバー: 戻る / 進む / 上の階層 / 更新 / アップロード / ダウンロード / 新規フォルダー / 新規バケット / 削除 / 接続管理
- アドレスバーで `s3://bucket/prefix/` 形式の直接ジャンプ
- Windows エクスプローラーからのドラッグ&ドロップでアップロード
- `aws login` の一時認証情報を自動で読み込み、期限切れ時はバックグラウンドで自動再認証
- `~/.aws/config` の `login_session` 付きプロファイルを選択肢として表示
- ステータスバー右下に STS GetCallerIdentity の結果(現在の IAM ユーザー / アカウント)を表示

## 必要環境

### 実行
- Windows 11 (x64)
- インストーラー版を使う場合は追加ランタイム不要(.NET ランタイム同梱)
- `aws login` 連携を使う場合は [AWS CLI v2](https://docs.aws.amazon.com/cli/latest/userguide/getting-started-install.html) (2.27 以降)

### 開発・ビルド
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- 任意: Visual Studio 2026 / Rider 2026 / VS Code + C# Dev Kit

### インストーラー生成 (任意)
- [Inno Setup 6](https://jrsoftware.org/isdl.php) (`winget install JRSoftware.InnoSetup` でも可)

## ビルド方法

### 1. 開発時 (Debug ビルド + 起動)

```powershell
# リポジトリルートで
dotnet build S3Browser.slnx
dotnet run --project S3Browser
```

### 2. Release ビルド

```powershell
dotnet build S3Browser.slnx -c Release
```

出力: `S3Browser\bin\Release\net10.0-windows\S3Browser.dll` (フレームワーク依存)。

## ライセンス

[MIT License](LICENSE)
