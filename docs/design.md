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

## 参照

- https://learn.microsoft.com/en-us/dotnet/api/system.windows.window.allowstransparency
- https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features
- https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview
