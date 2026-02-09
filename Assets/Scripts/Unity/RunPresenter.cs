// Assets/Scripts/Unity/RunPresenter.cs
using UnityEngine;
using RpsBuild.Core;
using TMPro;

public sealed class RunPresenter : MonoBehaviour
{
    [Header("Config")]
    [SerializeField] private TuningAsset tuningAsset;

    [Header("Initial Player Deck (must total 30)")]
    [SerializeField] private int initialGu = 0;
    [SerializeField] private int initialChoki = 0;
    [SerializeField] private int initialPa = 30;

    [Header("Enemy Generator Tuning")]
    [SerializeField] private int heavyMain = 20;
    [SerializeField] private int heavySub1 = 5;
    [SerializeField] private int heavySub2 = 5;
    [SerializeField] private RpsColor heavyMainColor = RpsColor.Gu;
    [SerializeField] private int balanceEach = 10;
    [SerializeField] private int twinTopA = 14;
    [SerializeField] private int twinTopB = 14;
    [SerializeField] private int twinTopC = 2;
    //範囲ランダムの場合
    [SerializeField] private bool useRangeTuning = true;
    // Heavy
    [SerializeField] private int heavyMainMin = 16;
    [SerializeField] private int heavyMainMax = 24;
    [SerializeField] private int heavySubMin = 0;
    [SerializeField] private int heavySubMax = 14;
    // Balance
    [SerializeField] private int balanceMin = 11;
    [SerializeField] private int balanceMax = 14;
    // TwinTop
    [SerializeField] private int twinTopMainMin = 12;
    [SerializeField] private int twinTopMainMax = 20;
    [SerializeField] private int twinTopDeltaMin = 1;
    [SerializeField] private int twinTopDeltaMax = 6;
    [SerializeField] private int twinTopCMin = 0;
    [SerializeField] private int twinTopCMax = 10;


    [SerializeField] private GameHudView hud;
    [SerializeField] private TMP_Text outcomeText;
    [SerializeField] private GaugeBarView gaugeBarView;
    [SerializeField] private ResolveHandsSequenceView handsSequenceView;
    [SerializeField] private ForcedFirstToggleView forcedFirstToggleView;
    [SerializeField] private ResolveOutcomeRowView outcomeRowView;





    private RunState _run;
    public RunState Run => _run;


    private void Start()
    {
        ResetRun();

    }

    [ContextMenu("Reset Run")]
    public void ResetRun()
    {
        if (tuningAsset == null)
        {
            Debug.LogError("TuningAsset is missing.");
            return;
        }

        var tuning = tuningAsset.ToTuning();

        var player = new DeckProfile(initialGu, initialChoki, initialPa);
        if (player.Total != 30)
        {
            Debug.LogError($"Initial player deck must total 30. Current={player.Total}");
            return;
        }

        var rng = new UnityRng();

        var enemyGen = new ArchetypeDeckGenerator
        {
            // 既存の固定値（残してOK）
            HeavyMain = heavyMain,
            HeavySub1 = heavySub1,
            HeavySub2 = heavySub2,
            DefaultHeavyMainColor = heavyMainColor,
            BalanceEach = balanceEach,
            TwinTopA = twinTopA,
            TwinTopB = twinTopB,
            TwinTopC = twinTopC,

            // NEW: Range
            UseRangeTuning = useRangeTuning,
            HeavyMainMin = heavyMainMin,
            HeavyMainMax = heavyMainMax,
            HeavySubMin = heavySubMin,
            HeavySubMax = heavySubMax,
            BalanceMin = balanceMin,
            BalanceMax = balanceMax,
            TwinTopMainMin = twinTopMainMin,
            TwinTopMainMax = twinTopMainMax,
            TwinTopDeltaMin = twinTopDeltaMin,
            TwinTopDeltaMax = twinTopDeltaMax,
            TwinTopCMin = twinTopCMin,
            TwinTopCMax = twinTopCMax
        };


        var gainFormula = tuningAsset.CreateGainFormula();

        _run = new RunState(tuning, player, rng, enemyGen, gainFormula);

        Debug.Log($"[Run Start] Env Weights: Heavy={tuning.EnvWeights.Heavy}, Balance={tuning.EnvWeights.Balance}, TwinTop={tuning.EnvWeights.TwinTop}");
        Debug.Log($"[Player Deck] Gu={player.Gu}, Choki={player.Choki}, Pa={player.Pa}");
        LogGauge();

        if (hud != null) hud.Render(this);
        if (hud != null) hud.SetRoundLog("Press Enter to play a round.\n1/2/3: reserve forced draw (if charged). R: reset.");

        // ★Intro（Round0）を先に1回だけ回す：以後の表示ロジックは PlayNextRound に集約
        PlayIntroIfNeeded();
    }

    [ContextMenu("Play Next Round")]
    public void PlayNextRound()
    {
        if (_run == null)
        {
            Debug.LogWarning("Run not initialized. ResetRun first.");
            return;
        }
        if (_run.IsGameOver)
        {
            Debug.Log($"[GameOver] Round={_run.RoundIndex}, Miss={_run.MissCount}");
            return;
        }

        // ★この呼び出しが Intro かどうか（PlayNextRound内でRoundIndexが増える前に判定）
        bool isIntroRound = (_run.RoundIndex == 0);


        var rr = _run.PlayNextRound();

        // ここで RunState.Gauge が増えている可能性がある
        gaugeBarView.SetGauge(
            _run.Gauge.Get(RpsColor.Gu),
            _run.Gauge.Get(RpsColor.Choki),
            _run.Gauge.Get(RpsColor.Pa),
            _run.Gauge.Max
        );
        // 予約が消費されてる可能性があるのでトグルも更新
        forcedFirstToggleView.RefreshVisual();

        // 7手×2の表示更新（Resolve画面）
        if (handsSequenceView != null)
        {
            // ★追加：ボーナスで勝敗が変わった箇所を強調
            var hi = new System.Collections.Generic.List<int>(4);

            // Heavy
            if (_run.LastHeavyBonusApplied)
                hi.Add(_run.LastHeavyBonusIndex);

            // TwinTop（あなたのRunStateの変数名に合わせてここだけ調整）
            // 例：単発なら
            if (_run.LastTwinTopBonusIndices != null && _run.LastTwinTopBonusIndices.Count > 0)
                hi.AddRange(_run.LastTwinTopBonusIndices);


            // 例：複数なら（List<int> 等を持ってるなら）
            // hi.AddRange(_run.LastTwinTopBonusIndices);

            // Balance（あなたのRunStateの変数名に合わせて）
            if (_run.LastBalanceBonusApplied)
                hi.Add(_run.LastBalanceBonusIndex);

            handsSequenceView.Play(rr.EnemyHands, rr.PlayerHands, rr.Outcomes, hi);
        }
        else
        {
            Debug.LogWarning("[SeqView] handsSequenceView is null (not assigned in Inspector)");
        }


        // ★OutcomeText：Intro以外だけ WIN/LOSE 強調表示
            if (outcomeText != null)
            {
                if (isIntroRound)
                {
                    // チュートリアルは勝敗を煽らない（好みで "TUTORIAL" にしてもOK）
                    outcomeText.text = "チュートリアル";
                }
                else
                {
                    outcomeText.text = rr.IsClear ? "WIN" : (_run.IsGameOver ? "GAME OVER" : "LOSE");
                }
            }



        if (hud != null)
        {
            if (_run.LastRoundWasIntro)
                {
                    hud.SetRoundLog(
                        "30枚の山札から7枚同時じゃんけん！負け数が2以下なら勝利です。\n" +
                        "勝つためのポイントは「デッキ構築」と「確定ドロー」。\n" +
                        "コインを使ってデッキを調整したり、確定ドローゲージを溜めよう。\n" +
                        "ゲージを使うと、次の試合でその手を確定で引けます。\n" +
                        "また、試合で出なかった手があると、その色のゲージがたまります。\n" +
                        "最強のデッキを作りながら、3敗する前にどこまで勝てるか挑戦だ！"
                    );

                }
                else
                {
                    CountOutcomes(rr.Outcomes, out int w, out int l, out int t);

                    string resultLabel = rr.IsClear ? "勝ち" : (_run.IsGameOver ? "ゲームオーバー" : "負け");
                    string summary = $"結果：{resultLabel}（{w}勝{l}敗{t}分）";

                    // 欠損勝利（クリア成功＆欠損あり）だけ出す
                    string missingLine = "";
                    if (rr.IsClear && rr.MissingColors.Count > 0)
                    {
                        // 色別ゲージ増加量（RunStateに記録した値を使う）
                        var sb = new System.Text.StringBuilder();
                        sb.Append("欠損：");

                        bool first = true;
                        void AddOne(string name, float gain)
                        {
                            if (gain <= 0f) return;
                            if (!first) sb.Append(" / ");
                            first = false;
                            sb.Append($"{name}(+{gain:0.00})");
                        }

                        AddOne("グー", _run.LastGaugeGainGu);
                        AddOne("チョキ", _run.LastGaugeGainChoki);
                        AddOne("パー", _run.LastGaugeGainPa);

                        missingLine = first ? "" : ("\n" + sb.ToString());
                    }

                    // Assets/Scripts/Unity/RunPresenter.cs
                    // PlayNextRound() の非Intro側：hud.SetRoundLog(summary + missingLine); を置換

                    string bonusLine = BuildBonusLogJa(rr);
                    hud.SetRoundLog(summary + missingLine + bonusLine);

                    hud.Render(this);
                }

        }


        Debug.Log($"[Round {_run.RoundIndex}] Enemy={_run.LastEnemyArchetype} Deck(G/C/P)=({_run.LastEnemyProfile.Gu}/{_run.LastEnemyProfile.Choki}/{_run.LastEnemyProfile.Pa})");
        Debug.Log($"  Hands: P={HandsToString(rr.PlayerHands)} vs E={HandsToString(rr.EnemyHands)}");
        Debug.Log($"  Outcomes: {OutcomesToString(rr.Outcomes)}  Losses={rr.LossCount}  Clear={rr.IsClear}");
        if (rr.MissingColors.Count > 0)
            Debug.Log($"  MissingColors: {string.Join(",", rr.MissingColors)}");


    }

    // ---- Gauge actions ----

    [ContextMenu("Reserve Forced First: Gu")]
    public void ReserveForcedGu() => ReserveForced(RpsColor.Gu);

    [ContextMenu("Reserve Forced First: Choki")]
    public void ReserveForcedChoki() => ReserveForced(RpsColor.Choki);

    [ContextMenu("Reserve Forced First: Pa")]
    public void ReserveForcedPa() => ReserveForced(RpsColor.Pa);

    private void ReserveForced(RpsColor c)
    {
        if (_run == null) return;

        bool ok = _run.TryReserveForcedFirst(c);
        Debug.Log(ok
            ? $"[Force Reserved] {c} (will be used on next round's first hand; then gauge resets)"
            : $"[Force Failed] {c} (need charged gauge and color must exist in deck)");
        LogGauge();
    }

    // ---- Deck adjustment helpers (example) ----
    // “+2/-2”のUIができたらここを呼ぶだけにする想定

    [ContextMenu("Example Adjust: +2 Pa, -2 Gu")]
    public void ExampleAdjustDeck()
    {
        if (_run == null) return;

        var current = _run.PlayerProfile;
        var ok = DeckAdjust.TryAdjust(ref current,
            addColor: RpsColor.Pa, addAmount: 2,
            subColor: RpsColor.Gu, subAmount: 2);

        if (!ok)
        {
            Debug.Log("[Adjust Failed] (would break constraints)");
            return;
        }

        _run.SetPlayerProfile(current);
        Debug.Log($"[Player Deck Updated] Gu={current.Gu}, Choki={current.Choki}, Pa={current.Pa}");
    }

    // ---- logging helpers ----

    private void LogGauge()
    {
        if (_run == null) return;
        Debug.Log($"[Gauge] Gu={_run.Gauge.Get(RpsColor.Gu):0.###}  Choki={_run.Gauge.Get(RpsColor.Choki):0.###}  Pa={_run.Gauge.Get(RpsColor.Pa):0.###}");
    }

    private int GetMaxMissUnsafe()
    {
        // presenter用の簡易。必要ならRunStateに公開Getterを追加してOK。
        // ここはログ表示だけなので雑でOKという割り切り。
        return tuningAsset != null ? tuningAsset.maxMiss : -1;
    }

    private static string HandsToString(System.Collections.Generic.IReadOnlyList<RpsColor> hands)
    {
        var s = new System.Text.StringBuilder();
        for (int i = 0; i < hands.Count; i++)
        {
            if (i > 0) s.Append(' ');
            s.Append(hands[i]);
        }
        return s.ToString();
    }

    private static string OutcomesToString(System.Collections.Generic.IReadOnlyList<RpsOutcome> os)
    {
        var s = new System.Text.StringBuilder();
        for (int i = 0; i < os.Count; i++)
        {
            if (i > 0) s.Append(' ');
            s.Append(os[i]);
        }
        return s.ToString();
    }

    public string GetNextEnemyArchetypeLabelSafe()
    {
        return _run != null
            ? _run.PreviewEnemyArchetype.ToJaLabel(_run.PreviewEnemyMainColor)
            : "不明";
    }


    public void PlayIntroIfNeeded()
    {
        if (_run == null) return;
        if (_run.RoundIndex != 0) return; // もう1回でも回ってたら何もしない

        PlayNextRound(); // Round0をここで発生させ、Resolve表示を作る
        RefreshHud();     // ★Intro直後に右下HUDも確実に更新
    }

    // RunPresenter.cs に追加（どこでもOK）
    public void RefreshHud()
    {
        if (hud != null) hud.Render(this);
    }

    private static void CountOutcomes(
        System.Collections.Generic.IReadOnlyList<RpsOutcome> os,
        out int win, out int lose, out int tie)
    {
        win = 0; lose = 0; tie = 0;
        for (int i = 0; i < os.Count; i++)
        {
            switch (os[i])
            {
                case RpsOutcome.Win:  win++;  break;
                case RpsOutcome.Lose: lose++; break;
                case RpsOutcome.Tie:  tie++;  break;
            }
        }
    }

    public void SetRoundLog(string text)
    {
        if (hud != null) hud.SetRoundLog(text);
    }
    private static string ToJpColor(RpsColor c)
    {
        return c switch
        {
            RpsColor.Gu => "グー",
            RpsColor.Choki => "チョキ",
            _ => "パー"
        };
    }

    public bool TrySkipResolveSequence()
        {
            if (handsSequenceView == null) return false;
            return handsSequenceView.RequestSkip();
        }

        private string BuildBonusLogJa(RoundResult rr)
        {
            if (_run == null) return "";
            if (rr == null) return "";

            var sb = new System.Text.StringBuilder();

            // 偏重
            if (_run.LastHeavyBonusApplied)
            {
                sb.Append('\n');
                sb.Append($"偏重ボーナス：一度だけ{ToJpColor(_run.LastHeavyBonusPlayerColor)}での負けが勝ちになりました");
            }

            // ツートップ
            if (_run.LastTwinTopBonusWinCount > 0)
            {
                sb.Append('\n');
                sb.Append($"２トップボーナス：３連で交互に出せた時の負けが{_run.LastTwinTopBonusWinCount}回勝ちになりました");
            }

            // バランス
            if (_run.LastBalanceBonusApplied)
            {
                sb.Append('\n');

                int handNo = _run.LastBalanceBonusIndex + 1; // 1..7表示
                string from = ToJpColor(_run.LastBalanceBonusFrom);
                string to = ToJpColor(_run.LastBalanceBonusTo);

                string outcomeJa = _run.LastBalanceBonusResult switch
                {
                    RpsOutcome.Win => "勝ち",
                    RpsOutcome.Tie => "引き分け",
                    _ => "負け" // 念のため
                };

                sb.Append($"バランス：{handNo}手目の{from}が{to}に変わり、{outcomeJa}になりました");

                // 欠損勝利→ゲージ増加は「クリア成功＆欠損あり」でRunState側が加算する仕様なので、それに合わせて表示
                if (_run.LastBalanceBonusCreatedMissing && rr.IsClear && rr.MissingColors != null && rr.MissingColors.Count > 0)
                {
                    sb.Append("（これにより欠損勝利が起き、ゲージが溜まりました）");
                }
            }

            return sb.ToString();
        }






    private void Update()
    {
        // Enterで次ラウンド
        if (Input.GetKeyDown(KeyCode.Return))
        PlayNextRound();

        // Rでリセット
        if (Input.GetKeyDown(KeyCode.R))
            ResetRun();

        // 1/2/3で確定ドロー予約（ゲージが満タンの色だけ成功）
        if (Input.GetKeyDown(KeyCode.Alpha1))
            ReserveForcedGu();
        if (Input.GetKeyDown(KeyCode.Alpha2))
            ReserveForcedChoki();
        if (Input.GetKeyDown(KeyCode.Alpha3))
            ReserveForcedPa();
    }

}
