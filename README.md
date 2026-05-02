# DJI_Action_VideoToolbox

DJI Actionシリーズ動画の **D-Log M → Rec.709変換**、**HEVC/NVENCエンコード**、**動画連結** に対応した、Windows向けGUIツールです。

本リポジトリは、ソフトウェアの透明性確保および安全性確認を目的として、ソースコードを公開しています。

---

## 📌 概要

`DJI_Action_VideoToolbox` は、DJI Actionシリーズで撮影した動画を扱いやすくするためのWindows用動画処理ツールです。

主に以下の用途を想定しています。

- DJI ActionシリーズのD-Log M動画へ `.cube` LUTを適用する
- `DJI OSMO Action 4 D-Log M to Rec.709 V1.cube` などを使ってRec.709へ変換する
- NVIDIA NVENCを利用してHEVC/H.265へ高速エンコードする
- 複数の動画ファイルを1本へ連結する
- FFmpegコマンドをGUI上で扱いやすくする

プログラミングやコマンドライン操作に慣れていない方でも使いやすいよう、WinForms GUIとして作成しています。

---

## ✨ 主な機能

### 🎨 LUT適用 / D-Log M → Rec.709変換

`.cube` 形式のLUTファイルを指定し、動画へ色変換を適用できます。

対応例：

- DJI OSMO Action 4 D-Log M to Rec.709 V1.cube
- その他の `.cube` LUTファイル

> 注意：LUTを動画へ焼き込むには、映像の再エンコードが必要です。`-c:v copy` のままLUTだけを適用することはできません。

---

### 🚀 HEVC / NVENC エンコード

NVIDIA GPU搭載PCでは、FFmpegの `hevc_nvenc` を利用して高速にHEVC/H.265エンコードできます。

主な設定：

- 10-bit Main10
- CQ指定
- Bフレーム指定
- Lookahead指定
- AQ強度指定
- 音声コピー
- Web用最適化（faststart）
- 既存出力の上書き

---

### ⚡ 高速LUT適用モード

用途に応じて、LUT適用時の速度と品質を選べます。

| 処理モード | 内容 | 用途 |
|---|---|---|
| 標準：LUT + HEVC/NVENC エンコード | 画質と圧縮のバランス重視 | 通常利用向け |
| LUTのみ適用：品質優先 | LUT適用を品質寄りで処理 | 色変換重視 |
| 高速LUT適用：trilinear + NVENC p1 | 速度と品質のバランス重視 | 実用上の高速処理 |
| 最速LUT適用：nearest + NVENC p1 | とにかく速度優先 | 確認用・速度優先 |
| HEVC変換のみ：LUTなし | LUTを使わずHEVC化 | 通常動画の再圧縮 |

---

### 🧩 LUT補間方式

LUT適用時の補間方式を選択できます。

| 補間方式 | 特徴 |
|---|---|
| trilinear | 推奨。速度と品質のバランスが良い |
| nearest | 最速。階調の滑らかさはやや不利 |
| tetrahedral | 高品質寄り。処理はやや重め |

迷った場合は **trilinear** を推奨します。

---

### 🎞️ 動画連結 / VideoConcat

複数の動画ファイルを1本へ連結できます。

主な機能：

- `.mp4` / `.mkv` 対応
- 同一形式の場合は出力拡張子を自動調整
- `.mp4` と `.mkv` が混在する場合は警告
- 高速・無劣化連結モード
- 互換性重視の再エンコード連結モード
- ファイル順序の上下移動
- ドラッグ＆ドロップ対応

---

## 🖥️ 動作環境

推奨環境：

- Windows 11
- .NET 8 以降
- FFmpeg / ffprobe
- NVIDIA GPU搭載PC（NVENC利用時）

開発・確認想定：

- Windows 11
- C# / WinForms
- Visual Studio
- FFmpeg

---

## 🧰 必要な外部ソフト

本ツールは動画処理にFFmpegを使用します。

必要なもの：

- `ffmpeg.exe`
- `ffprobe.exe`

ツール上部の共通設定欄で、`ffmpeg.exe` と `ffprobe.exe` のパスを指定してください。

空欄の場合は、WindowsのPATH上にある `ffmpeg` / `ffprobe` を使用します。

---

## 📦 ダウンロード

配布版は Releases から取得してください。

- `DJI_Action_VideoToolbox_v1.0.6.zip`

GitHubが自動生成する `Source code (zip)` / `Source code (tar.gz)` はソースコードのみです。
通常利用する場合は、Release Assets内の配布用ZIPを使用してください。

---

## 🚀 初回起動手順

1. Releaseから `DJI_Action_VideoToolbox_v1.0.6.zip` をダウンロードします。
2. ZIPを任意のフォルダへ展開します。
3. `DJI_Action_VideoToolbox_v1.0.6.exe` を起動します。
4. 上部の共通設定で `ffmpeg.exe` と `ffprobe.exe` を指定します。
5. 必要に応じて「設定保存」を押します。

---

## 🎨 LUT適用・エンコードの基本手順

1. `DJI Action / HEVC エンコード` タブを開きます。
2. `LUT.cube` に使用する `.cube` ファイルを指定します。
3. 出力先フォルダを指定します。
4. 入力動画を追加します。
5. 処理モードを選択します。
6. 必要に応じてCQ、Bフレーム、Lookahead、AQ強度を調整します。
7. `エンコード開始` を押します。

---

## 🎞️ 動画連結の基本手順

1. `動画連結 / VideoConcat` タブを開きます。
2. 連結したい動画ファイルを追加します。
3. 必要に応じて順序を上下移動します。
4. 出力先を指定します。
5. 連結方式を選択します。
6. 連結を開始します。

---

## 🎯 おすすめ設定

### 通常のD-Log M → Rec.709変換

- 処理モード：標準：LUT + HEVC/NVENC エンコード
- LUT補間方式：trilinear
- CQ：20前後
- Bフレーム：4
- Lookahead：40
- AQ強度：9

### 速度優先

- 処理モード：高速LUT適用：trilinear + NVENC p1
- LUT補間方式：trilinear

### 最速確認用

- 処理モード：最速LUT適用：nearest + NVENC p1
- LUT補間方式：nearest

### 品質寄り

- 処理モード：LUTのみ適用：品質優先
- LUT補間方式：tetrahedral

---

## 🟩 NVIDIA GPU搭載PCでの挙動

NVIDIA GPUを搭載しており、FFmpegが `hevc_nvenc` に対応している場合、NVENCによる高速エンコードが可能です。

期待できる利点：

- CPU負荷を抑えやすい
- HEVC/H.265の高速エンコードが可能
- 長時間動画の処理に向く

---

## 🟥 NVIDIA以外のGPU搭載PCでの挙動

NVIDIA以外のGPU、またはNVENC非対応環境では、`hevc_nvenc` を使用する処理は失敗する可能性があります。

該当する例：

- AMD Radeonのみ搭載
- Intel内蔵GPUのみ搭載
- NVIDIA GPUはあるがNVENC非対応
- FFmpegビルドがNVENC非対応

この場合は、以下のような対応が必要です。

- NVENCを使わないFFmpeg設定へ変更する
- CPUエンコード用の `libx265` を使う
- AMD向けの `hevc_amf` を使う
- Intel向けの `hevc_qsv` を使う

現行版では、主にNVIDIA NVENC環境を想定しています。
AMD / Intel GPU向けの専用モードは、今後の拡張候補です。

---

## 🛠️ ソースコードからビルドする場合

ソースコードは `src/` フォルダにあります。

構成例：

```text
src/
├─ DJI_Action_VideoToolbox_v1.0.6.csproj
├─ MainForm.cs
├─ Program.cs
├─ app.ico
├─ app.manifest
└─ Properties/
   └─ PublishProfiles/
      └─ SingleExe_win-x64.pubxml
```

Visual Studioで `DJI_Action_VideoToolbox_v1.0.6.csproj` を開き、Release構成でビルドしてください。

想定発行条件：

- 構成：Release
- ターゲットランタイム：win-x64
- 配置モード：自己完結
- 単一ファイル作成：有効

---

## 📁 Repository構成

```text
DJI_Action_VideoToolbox/
├─ docs/
│  └─ DJI_Action_VideoToolbox_v1.0.6_Readme.txt
├─ src/
│  ├─ Properties/
│  │  └─ PublishProfiles/
│  │     └─ SingleExe_win-x64.pubxml
│  ├─ app.ico
│  ├─ app.manifest
│  ├─ DJI_Action_VideoToolbox_v1.0.6.csproj
│  ├─ MainForm.cs
│  └─ Program.cs
├─ .gitignore
├─ LICENSE.txt
└─ README.md
```

---

## 🔐 ソースコード公開の目的

本リポジトリは、以下を目的としてソースコードを公開しています。

- 不正な処理が含まれていないことを確認できるようにするため
- 利用者が処理内容を確認できるようにするため
- ツールの透明性を確保するため

ただし、ソースコード公開は、自由な改変・再配布・商用利用を許可するものではありません。

---

## 📜 ライセンス / 利用条件

本ソフトウェアは、個人の非商用利用に限り使用できます。

以下の行為は禁止します。

- 無断改変
- 無断再配布
- 商用利用
- 販売、貸与、譲渡、転載
- 作者名やライセンス表示の削除・改変
- 自作物であるかのように表示する行為

詳細は `LICENSE.txt` を確認してください。

---

## ⚠️ 注意事項

- 本ツールはFFmpegを使用します。
- FFmpeg本体は同梱していない場合があります。
- LUT適用には映像の再エンコードが必要です。
- 入力動画・出力動画は必ずバックアップを取ってから処理してください。
- 長時間動画や高解像度動画では、処理に時間がかかる場合があります。
- 出力先の空き容量を十分に確保してください。

---

## 🧪 トラブル時の確認項目

処理に失敗する場合は、以下を確認してください。

- `ffmpeg.exe` のパスが正しいか
- `ffprobe.exe` のパスが正しいか
- LUTファイルのパスが正しいか
- 出力先フォルダに書き込み権限があるか
- NVIDIA GPU / NVENCに対応しているか
- FFmpegが `hevc_nvenc` に対応しているか
- 入力動画が破損していないか

---

## 🔄 アップデート方針

今後の更新では、以下のようにバージョン番号を管理します。

| 種類 | 例 | 内容 |
|---|---|---|
| 軽微な修正 | v1.0.7 | 不具合修正、文言修正、UI微調整 |
| 機能追加 | v1.1.0 | 新機能追加、対応形式追加 |
| 大きな仕様変更 | v2.0.0 | UI刷新、内部構造変更、互換性変更 |

---

## 🧾 免責事項

本ソフトウェアは現状のまま提供されます。

本ソフトウェアの使用、または使用不能によって生じたいかなる損害についても、作者は責任を負いません。

自己責任で使用してください。

---

## 👤 作者

Sunazuri
