# nmoonfox Desktop 初期設計

## 要求と今回の範囲

独立したWindowsアプリ。nmoonfoxが自律移動し、0〜12匹を表示できる。サイズ96〜240 DIP、速度20〜140 DIP/秒を変更。現在の192×208セル、9状態+16視線を再利用し、耳2つ・接地したなでられ反応を維持。眠る姿、呼吸、ハート演出を追加。

初期値は3匹・幅144 DIP・速度55。主ディスプレイの作業領域を使用。標準は画面下部を歩き、全体散歩モードでは平面上を自由に移動。移動・待機・手を振る・好奇心・なでられ・お昼寝をランダムに選ぶ。ドラッグ中は自律移動を止め、離すと喜ぶ。ホバーは進入時1回だけ反応し、内部移動では再発火しない。

## 構成と判断（ADR-001）

.NET 10 WPFを採用。Windowsの透明ウィンドウとトレイを追加依存なしで利用できるため。Electronはクロスプラットフォーム性に優れるが初版の配布サイズ・透明入力制御に追加対応を要する。Unityは今回の常駐小型アプリには重い。ロジックはUIから分離し、将来の移植に備える。

- Core: Settings, SettingsStore, PetWorld, PetActor, AnimationCatalog。時間差分を受け取り、座標と状態を返す。
- Desktop: 小さな透明Windowを各個体に1枚。共有デコード済み画像キャッシュ。非アクティブ表示で作業のフォーカスを奪わない。
- Control window: 日本語設定、ライブ反映、休憩/再開、お昼寝/起こす、呼び戻し、終了。
- Tray: 設定、休憩、クリック透過、終了。ウィンドウを閉じるとトレイに残る。
- Settings: LocalApplicationData/nmoonfox-desktop/settings.json、原子的保存。破損値や範囲外値は安全な初期値/範囲へ。単一起動。
- Publish: Windows x64 self-contained zip。開発用ビルドも提供。未署名。自動起動・更新・テレメトリーなし。

## Core契約（テスト用）

namespace Nmoonfox.Core。PetSettings record: Count(int=3), Size(double=144), Speed(double=55), AlwaysOnTop(bool=true), RoamEverywhere(bool=false), ClickThrough(bool=false), Normalize() -> PetSettings。
DesktopBounds record struct (double X,Y,Width,Height)。PetState enum Idle, WalkLeft, WalkRight, Wave, Petting, Curious, Thinking, Review, Sleep, Dragged。
PetWorld(DesktopBounds bounds, PetSettings settings, int seed=1)。Settings, Bounds, Pets(IReadOnlyList<PetActor>), Paused(bool set), ApplySettings(PetSettings), ResizeBounds(DesktopBounds), Tick(double seconds), Pet(int id), BeginDrag(int id), DragTo(int id,double x,double y), EndDrag(int id), SleepAll(), WakeAll(), Gather(), Remove(int id)。
PetActor: Id(int), X/Y(double), State(PetState), StateTime(double), Affection(double seconds remaining), LookDirection(int0..15), Frame(int)。Frameは現在stateの有効セル内。
AnimationCatalog.Get(PetState) -> AnimationClip(Row,Frames,Fps)。LookDirectionは0=right、4=up、8=left、12=down。Sleep/Draggedは通常セルなどのfallbackを持つ。
SettingsStore(string path), Load()->PetSettings, Save(PetSettings)。

## 実装順

1. Core契約と設定保存、境界・時間・個体数・なでられ重複抑止のテスト。
2. アトラス読込、透明ペットウィンドウ、30fps更新、ホバー/ドラッグ、トレイ。
3. 設定画面と永続化、sleep/breath/hearts、終了/単一起動。
4. 独立レビュー、Coreテスト、GUI smoke（設定変更、個体数、描画、終了）、自立実行版のビルドと起動確認。

## 0.2: マウスを振って道を空ける

- 通常の移動・静止・単発の素早い移動では反応せず、約0.65秒以内の速い切り返し2回と十分な移動量で認識。単位はDIP。30Hzの既存ポインター取得を使用し、グローバル入力フックは追加しない。
- 設定 `YieldToMouse` は初期値true、保存する。設定画面操作中・マウスボタン押下中・休憩中は検出をリセット。
- Core `MouseShakeDetector.Sample(double x,double y,double timestamp)` -> bool、`Reset()`。時刻は単調増加秒。無効値、時間飛び、座標ワープは検出履歴を破棄する。
- Core `PetWorld.MakeSpace(double x,double y)` -> 反応個体数。近くの起きている個体が対象。Hoverが先に発火してPetting中でも退避を優先。Dragged/Sleep/休憩/設定OFFは除外。`PetActor.IsYielding` で状態を公開。
- 有効な作業領域内の候補から、カーソルへ近づく軌道を避けつつ遠ざかる位置を選ぶ。端では横や上へ逃がす。画面下部モードでも退避時のみ上への移動を許す。
- 既存歩行モーションで早足に退避。合計6秒は再退避やなでられを抑制し、到着後は待機。退避中は一時的にクリックを通す。その後は通常動作に戻る。
- お昼寝/起こす/呼び戻す/明示的なドラッグ/設定OFFで退避状態を解除。純粋Coreテスト、独立レビュー、GUI経由検出テスト、配布EXEの検証を行う。

切り返しの自然な減速を認識するため、0.12秒以下の短い低速サンプルは保持する。角でどの方向も初めはカーソルに近づいてしまう場合のみ、移動線分上でカーソルと最も距離を保てる候補を使い、隅で動けなくなるのを防ぐ。

## 0.3: 複数ディスプレイとドラッグ演出（ADR-002）

- Coreの座標は仮想デスクトップの**物理px**へ統一。DesktopDisplayは安定ID、作業領域、DPI倍率、メイン判定を持つ。サイズ・速度の設定はDIPのまま、所属画面の倍率で換算する。全画面の外接矩形にはクランプしない。各個体を1つの実在画面へ所属させ、歩行・退避・ターゲットをその作業領域内へ収める。
- `UseAllDisplays=true`が初期値。従来JSONで未指定でもtrue。初期配置/Spreadはメインから順に分配、falseはメインだけ。Gatherは対象画面を指定でき、省略時メイン。総個体数は変えない。
- DragPointerは物理pxカーソルと0〜1のつかみ位置を受け取り、カーソル側の画面へ所属変更する。画面の隙間では最寄の作業領域へクランプ。DPI変化後の寸法でつかみ位置を計算する。
- 同IDの画面移設は原点差分を現在地と移動先へ適用。画面切断時は最寄の残存画面へ回収。極小作業領域では全身が収まるよう縮小する。ホットプラグで新しい画面が増えたらユーザーがSpreadで再配置できる。
- WPFはPerMonitorV2。Win32 EnumDisplayMonitors/GetMonitorInfoで作業領域を列挙し、各画面の非表示1pxネイティブウィンドウにGetDpiForWindowを使って倍率を得る。カスタム倍率にも対応するため段階的なscale enumで代用しない。GetCursorPos/SetWindowPosは同じ物理座標を使い、WPF Left/Topへ直接代入しない。WPFに描画のDPI変更を任せる。
- 表示変更イベントはUIスレッドへ通知。2秒周期の再列挙でも変更を補完し、同一トポロジーでは再配置しない。設定画面も切断後に到達可能な位置へ収める。マウスを振る検出は所属画面内のDIPに換算し、画面またぎ/DPI変更では検出履歴をリセット。
- ドラッグ中は既存Curious行を再利用し、時計を進めて表情を変える。自律移動は停止。水平移動量をDIPに換算して±6度の傾きを滑らかに適用し、描画に±1.5度の揺れと浮遊を加える。耳・尾を切らないよう0.86倍で描き、画像自体は変更しない。休憩中でも明示的ドラッグの演出は進める。離すと接地した喜ぶ反応へ戻す。

実装順と検証項目は [multi-monitor-plan.md](multi-monitor-plan.md)。

## 参照（DPI）

- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getdpiforwindow
- https://github.com/microsoft/WPF-Samples/blob/main/PerMonitorDPI/readme.md
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos

- https://learn.microsoft.com/en-us/dotnet/api/system.windows.window.allowstransparency
- https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features
- https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview
