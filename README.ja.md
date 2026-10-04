# lol-match-alert

[English](README.md) · 日本語

League of Legends でマッチが見つかった瞬間にキーボードを光らせ、Accept 画面が終わったら元のライティングに戻すアプリです。
別のウィンドウを見ていても、席を離れていても、キーボードを見れば気づけます。

> **Riot Games とは無関係の非公式ツールです。** League クライアントのローカル API（非公式）を読み取るだけで、Accept を代わりに押したり、クライアントの動作を変えたりはしません。

## インストール

[Releases](https://github.com/penguinwokrs/lol-match-alert/releases) から `lol-match-alert-<バージョン>-setup.exe` をダウンロードして実行してください。
管理者権限も、ランタイムの追加インストールも要りません。インストール後はタスクトレイに常駐し、チェックを入れたままにすれば Windows の起動時にも自動で開始します。

インストーラを使いたくない場合は、同じ exe が入った `win-x64.zip` も使えます。

## 使い方

トレイに置いておくだけです。アイコンの色で状態が分かります。

| アイコン | 状態 |
|---|---|
| 灰色のキー | League クライアントの起動待ち |
| 青いキー | 接続済み、マッチ待ち |
| 赤いキー | マッチが見つかり、キーボードが点滅中 |

Accept 画面が出た瞬間から、Accept / Decline / 時間切れまで点滅し、その後は元のライティングに戻ります。
カスタムゲームには Accept 画面がないので光りません。

トレイメニューの **テスト点灯**（またはアイコンの左クリック）で 3 秒間光らせて確認できます。

### 動作条件

- Windows 10 1809 以降（x64）
- キーボードは **USB ケーブルで接続** してください。無線（2.4 GHz）や Bluetooth ではコマンドが届きません。
- 動作中は VIA アプリや Keychron Launcher を閉じてください。2 つのアプリが同時にライトを変えると干渉します。
  [kbd-signal](https://github.com/Sora-bluesky/kbd-signal) は入れたままで構いませんが、両方が同時にライトを変えようとすると後から書いた方が勝ちます。

キーボードに保存されたライティング設定には一切書き込みません。変更はキーボードのメモリ上だけなので、抜き差しすれば必ず自分の設定に戻ります。

## 対応キーボード

| キーボード | 状態 |
|---|---|
| Keychron Q1 HE 8K | 実機で確認済み |
| Pulsar PCMK 2HE TKL | **未検証**（Pulsar 公式の設定ツールの実装から作成。実機では未確認） |
| Pulsar XBOARD MS | **未検証**（同上） |
| 同じ方式のその他の Pulsar キーボード | ウィザードが自動で設定（未検証） |
| その他の VIA 対応キーボード | ウィザードで設定（下記） |

### Pulsar のキーボード

Pulsar の現行キーボードは VIA ではなく、Pulsar のウェブアプリ「Bibimbap」で設定します。
PCMK 2HE TKL と XBOARD MS は、VIA と同じ通信口で独自の照明プロトコルを使っており、このアプリは Bibimbap と同じ方法で通信します。
まだ誰も実機で試していないため、慎重な動作にしています。Bibimbap の「保存」命令は送らず、ブートローダにも触れません。
既定ではキーボード自身の明滅エフェクトを使うので、キーボードへの書き込みは 1 回の通知につき 1 回だけです。

**お持ちなら 1 分で検証できます**（先に Bibimbap を閉じてください）。

1. コマンドプロンプトで次の行を実行します。キーボードが光り、前後のライティングの値が表示されます。最後に `restored` と出れば元に戻っています。
   ```
   "%LOCALAPPDATA%\Programs\lol-match-alert\lol-match-alert.exe" --test
   ```
2. キーボードを抜いて挿し直します。自分のライティングに戻っていれば、アプリの書き込みは保存されていません。アプリの金色のままなら保存されています。
3. `--test` の表示と、挿し直した後の様子を添えて [Issue](https://github.com/penguinwokrs/lol-match-alert/issues/new?template=keyboard-support.yml) を作成してください。その報告で「検証済み」にでき、速いパターンも使えるようになります。

### 一覧にないキーボードを設定する

トレイメニューの **キーボードを設定…** を開きます。ウィザードが自動で分かることを調べたうえで、キーボードを光らせて「緑で点灯」「明滅している」「それ以外」のどれに見えるかを聞きます（たいてい 2 問で終わります）。
終わるとライティングは元に戻り、結果は `%APPDATA%\lol-match-alert\devices\` にプロファイルとして保存されます。

「まだ対応していません」と表示された場合は、**詳細をコピー** を押して
[Issue](https://github.com/penguinwokrs/lol-match-alert/issues/new?template=keyboard-support.yml) を作成してください。

## カスタマイズ

トレイメニューの **設定フォルダーを開く** から `settings.json` を編集します。保存した瞬間に反映されます。
書き間違いがあればトレイがどこがおかしいかを表示し、直前の正しい設定のまま動き続けます。

```jsonc
{
  "pattern": "my-blink",
  "patterns": {
    "my-blink": {
      "steps": [
        { "color": "#00A0FF", "brightness": 100, "durationMs": 250 },
        { "color": "#000000", "durationMs": 250 }
      ]
    }
  },
  "devices": { "keychron-q1-he-8k": { "pattern": "pulse" } },
  "maxAlertSeconds": 30,
  "language": "auto"
}
```

| 項目 | 意味 |
|---|---|
| `pattern` | すべてのキーボードで再生するパターン。組み込みか自分で定義したものの名前 |
| `patterns.<名前>.steps` | 順番に再生し、最後まで行ったら繰り返す。ステップが 1 つなら一度書いてそのまま保持 |
| `color` | `#RRGGBB`。`#000000` は消灯、暗い色ほど暗く光る |
| `brightness` | 0-100（既定 100） |
| `durationMs` | そのステップを表示する時間（ミリ秒）。ステップが複数あるときは必須 |
| `effect` | `solid`（既定）か `breathing`、またはキーボードのプロファイルが定義する名前 |
| `speed` | 0-255。アニメーションするエフェクト用 |
| `repeat` | `"untilStopped"`（既定）か回数。回数を終えたら最後のステップを保持 |
| `devices.<id>` | キーボードごとの設定。`pattern` で上書き、`"enabled": false` で光らせない。id はトレイメニューの「キーボード」に表示（クリックでコピー） |
| `maxAlertSeconds` | 安全のための停止時間（既定 30 秒） |
| `language` | メニューとウィザードの言語。`"auto"`（Windows に合わせる、既定）、`"en"`、`"ja"`。次回起動時に反映 |

組み込みパターン:

| 名前 | 光り方 |
|---|---|
| `match-found`（既定） | 赤と白が 300 ms ごとに入れ替わる |
| `pulse` | 金色でゆっくり明滅 |
| `steady` | 金色で点灯 |

キーボードのプロファイルも上書きできます。同じ `id` で `devices\` にコピーし、変えたいところだけ書き換えてください。
組み込みのプロファイルは [`src/MatchAlert.App/BuiltIn/devices`](src/MatchAlert.App/BuiltIn/devices) にあります。

## 困ったとき

- **マッチしても光らない。** クライアントを開いている間、アイコンが青になっているか確認してください。灰色のままならクライアントを見つけられていません。ログは `%LOCALAPPDATA%\lol-match-alert\log.txt` にあります。
- **テスト点灯で「キーボードが接続されていません」と出る。** ケーブルで接続し、「キーボード」の一覧に出ているか確認してください。出ていなければウィザードで設定します。
- **元に戻るかを画面を見ずに確かめたい。** `lol-match-alert.exe --test` でパターンを再生し、ライティングを戻して読み戻し、前後の値を表示します。終了コード 0 なら元どおりです。

## 開発者向け

仕組み、プロジェクト構成、キーボードやドライバの追加方法は [README（英語）](README.md#how-it-works) を参照してください。

## クレジット

VIA プロトコルの詳細と Keychron Q1 HE 8K の癖は、[kbd-signal](https://github.com/Sora-bluesky/kbd-signal)（MIT）が実機で解明したものです。このアプリはその知見を C# で再実装しています。
HID の列挙処理は [OpenInzone](https://github.com/penguinwokrs/openinzone) から流用しています。詳しくは [NOTICE](NOTICE) を参照してください。

## ライセンス

GPL-3.0。[LICENSE](LICENSE) を参照してください。
