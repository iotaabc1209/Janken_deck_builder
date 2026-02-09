using UnityEngine;
using UnityEngine.UI;
using TMPro;
using RpsBuild.Core;

public sealed class AdjustPanelView : MonoBehaviour
{
    [Header("Core")]
    [SerializeField] private RunPresenter presenter;

    [Header("Texts")]
    [SerializeField] private TMP_Text pointsText;
    [SerializeField] private TMP_Text deckText;    // "G/C/P"
    [SerializeField] private TMP_Text playerArchetypeText;

    [Header("Deck Adjust (Draft) : +1 / -1 per color")]
    [SerializeField] private Button deckPlusGu;
    [SerializeField] private Button deckMinusGu;
    [SerializeField] private Button deckPlusChoki;
    [SerializeField] private Button deckMinusChoki;
    [SerializeField] private Button deckPlusPa;
    [SerializeField] private Button deckMinusPa;

    [Header("Deck Column Counts (shown)")]
    [SerializeField] private TMP_Text countGuText;
    [SerializeField] private TMP_Text countChokiText;
    [SerializeField] private TMP_Text countPaText;

    [Header("Buy Gauge (Draft) : +1 / -1 per color")]
    [SerializeField] private Button buyGaugePlusGu;
    [SerializeField] private Button buyGaugeMinusGu;
    [SerializeField] private Button buyGaugePlusChoki;
    [SerializeField] private Button buyGaugeMinusChoki;
    [SerializeField] private Button buyGaugePlusPa;
    [SerializeField] private Button buyGaugeMinusPa;

    [SerializeField] private GaugeBarView gaugeBarView;




    private bool _bound = false;

    // Adjust中だけ使うドラフト（+予約 / -予約）
    private readonly int[] _add = new int[3];
    private readonly int[] _sub = new int[3];
    // Adjust中の「ゲージ購入」ドラフト（色ごとの購入回数）
    // Gauge draft（デッキの _add/_sub と同型）
    private readonly int[] _gaugeBuy = new int[3];

    // Adjust中の「確定ドロー予約」ドラフト
    private readonly System.Collections.Generic.List<RpsColor> _draftForcedOrder = new();



    // Adjust開始時点のポイント（分母固定用）
    private int _pointsBudget = 0;

    public System.Action OnDraftChanged;




    public void Open(RunPresenter p)
    {
        presenter = p;
        BindButtonsOnce();

        // ★ Adjust開始時点のポイントを分母として固定
        _pointsBudget = presenter.Run.Points;

        // ★ 調整前デッキを表示（固定）
        var prof = presenter.Run.PlayerProfile;
        if (deckText != null)
        deckText.text = $"デッキ：グー/チョキ/パー： {prof.Gu}/{prof.Choki}/{prof.Pa}";

        ResetDraft();   // Adjustに入るたびドラフトを初期化（ここは好みで変更可）
        _draftForcedOrder.Clear(); // 基本nullのはずだが保険
        Refresh();
    }

    public void Refresh()
    {
        if (presenter == null || presenter.Run == null) return;

        var run = presenter.Run;

        // 1. shown を計算
        // 現在の“見かけ”のデッキ（base + add - sub）
        var shown = GetShownProfile(run.PlayerProfile);

        // 2. 枚数表示
        if (countGuText != null)    countGuText.text = shown.Gu.ToString();
        if (countChokiText != null) countChokiText.text = shown.Choki.ToString();
        if (countPaText != null)    countPaText.text = shown.Pa.ToString();

        // 3. 30枚じゃないなら赤色に
        bool okTotal = (shown.Total == 30);
        var col = okTotal ? Color.white : new Color(1f, 0.6f, 0.6f);
        if (countGuText != null)    countGuText.color = col;
        if (countChokiText != null) countChokiText.color = col;
        if (countPaText != null)    countPaText.color = col;

        if (playerArchetypeText != null)
        {
            playerArchetypeText.color = col;

            if (!okTotal)
            {
                playerArchetypeText.text = "自分：—（合計30で確定）";
            }
            else
            {
                // ★30枚のときだけ判定して表示
                // ここは「あなたの PlayerArchetype.cs の判定API」に合わせて1行差し替えてOK。

                // 例A：Classifyが (arch, main, second) を返す/埋める想定
                // var info = PlayerArchetypeClassifier.Classify(shown);
                // playerArchetypeText.text = $"自分：{info.ToJaLabel()}";

                // 例B：outで返す想定（よくある形）
                // PlayerArchetypeClassifier.Classify(shown, out var arch, out var main, out var second);
                // playerArchetypeText.text = $"自分：{arch.ToJaLabel(main, second)}";

                var info = PlayerArchetypeClassifier.Classify(shown);
                playerArchetypeText.text = $"自分：{info.ToJaLabel()}";


            }
        }


        // 4. ポイント・ゲージなど既存表示
        if (pointsText != null) pointsText.text = $"　　x{PointsLeft()} / {_pointsBudget}";

        // ゲージ表示（ドラフト購入分を含めた「見かけ」） ---
        float buy = run.Tuning.GaugeBuyAmount;


            // ボタン押下可否（ユーザに優しく）
            // ※ A方式：Cost=max(sumAdd,sumSub) なので、PointsLeft>0 では判定しない
            bool canAdd = CanAdd(run);
            bool canSub = CanSub(run);

            // 相殺できるなら「ポイント0でも押せる」
            bool canCancelPlusGu    = _sub[(int)RpsColor.Gu] > 0;
            bool canCancelPlusChoki = _sub[(int)RpsColor.Choki] > 0;
            bool canCancelPlusPa    = _sub[(int)RpsColor.Pa] > 0;

            bool canCancelMinusGu    = _add[(int)RpsColor.Gu] > 0;
            bool canCancelMinusChoki = _add[(int)RpsColor.Choki] > 0;
            bool canCancelMinusPa    = _add[(int)RpsColor.Pa] > 0;

            // + は「追加できる」OR「同色の-予約を相殺できる」
            if (deckPlusGu != null)    deckPlusGu.interactable = canAdd || canCancelPlusGu;
            if (deckPlusChoki != null) deckPlusChoki.interactable = canAdd || canCancelPlusChoki;
            if (deckPlusPa != null)    deckPlusPa.interactable = canAdd || canCancelPlusPa;

            // - は「減らせる」OR「同色の+予約を相殺できる」
            // ※減らす(予約)の時だけ shown>0 を要求。相殺は shown>0 条件いらない
            if (deckMinusGu != null)    deckMinusGu.interactable = (canSub && shown.Gu > 0) || canCancelMinusGu;
            if (deckMinusChoki != null) deckMinusChoki.interactable = (canSub && shown.Choki > 0) || canCancelMinusChoki;
            if (deckMinusPa != null)    deckMinusPa.interactable = (canSub && shown.Pa > 0) || canCancelMinusPa;

            // ---- BuyGauge buttons (same policy as deck adjust) ----

            // canBuyGaugeAdd/CanBuyGaugeSub はデッキと同じく「予算内か」で判定（黄色なし）
            // ---- BuyGauge buttons (gauge buy only; minus = cancel only) ----
            bool canGaugeAdd = CanGaugeAdd(run);

            // + は「予算OK」かつ「その色のremが満タンじゃない」なら押せる
            if (buyGaugePlusGu != null)    buyGaugePlusGu.interactable    = CanGaugeAdd(run) && CanGaugeActuallyIncrease(RpsColor.Gu, run);
            if (buyGaugePlusChoki != null) buyGaugePlusChoki.interactable = CanGaugeAdd(run) && CanGaugeActuallyIncrease(RpsColor.Choki, run);
            if (buyGaugePlusPa != null)    buyGaugePlusPa.interactable    = CanGaugeAdd(run) && CanGaugeActuallyIncrease(RpsColor.Pa, run);

            // - は「その色の購入予約がある」なら押せる（返金=コスト減）
            if (buyGaugeMinusGu != null)    buyGaugeMinusGu.interactable    = CanGaugeActuallyDecrease(RpsColor.Gu, run);
            if (buyGaugeMinusChoki != null) buyGaugeMinusChoki.interactable = CanGaugeActuallyDecrease(RpsColor.Choki, run);
            if (buyGaugeMinusPa != null)    buyGaugeMinusPa.interactable    = CanGaugeActuallyDecrease(RpsColor.Pa, run);

            ValidateDraftForcedOrder();
            RefreshGaugePreview();
            OnDraftChanged?.Invoke();


    }

    // RoundFlowUI から「次へ」を押す前にチェックする用
    public bool CanProceedToNextRound()
    {
        if (presenter == null || presenter.Run == null) { Debug.Log("[CanProceed] presenter/run null"); return false; }

        ValidateDraftForcedOrder(); // 入れてるなら残す

        var run = presenter.Run;
        var shown = GetShownProfile(run.PlayerProfile);

        if (shown.Total != 30) { Debug.Log($"[CanProceed] blocked: deck total {shown.Total} != 30"); return false; }

        int cost = DraftCost();
        if (cost > _pointsBudget) { Debug.Log($"[CanProceed] blocked: cost {cost} > budget {_pointsBudget}"); return false; }

        if (_draftForcedOrder.Count > run.Tuning.HandCount)
        {
            Debug.Log($"[CanProceed] blocked: forced count {_draftForcedOrder.Count} > hand {run.Tuning.HandCount}");
            return false;
        }

        if (_draftForcedOrder.Count > 0)
        {
            // 色がデッキにあるか
            for (int i = 0; i < _draftForcedOrder.Count; i++)
            {
                var c = _draftForcedOrder[i];
                if (!shown.Has(c)) { Debug.Log($"[CanProceed] blocked: deck has no {c} (shown)"); return false; }
            }

            // ★ここが怪しい本丸：色別上限
            int maxGu = GetShownChargedCount(RpsColor.Gu, run);
            int maxCh = GetShownChargedCount(RpsColor.Choki, run);
            int maxPa = GetShownChargedCount(RpsColor.Pa, run);

            int nGu = GetDraftForcedCount(RpsColor.Gu);
            int nCh = GetDraftForcedCount(RpsColor.Choki);
            int nPa = GetDraftForcedCount(RpsColor.Pa);

            Debug.Log($"[CanProceed] forced Gu {nGu}/{maxGu} Ch {nCh}/{maxCh} Pa {nPa}/{maxPa}  gaugeNoClamp Gu={GetShownGaugeValue_NoClamp(RpsColor.Gu, run):0.###}");

            if (nGu > maxGu) return false;
            if (nCh > maxCh) return false;
            if (nPa > maxPa) return false;
        }

        return true;
    }





    // RoundFlowUI から「次ラウンド開始」直前に呼ぶ（確定）
    public bool TryCommitDraft()
    {
        if (presenter == null || presenter.Run == null) return false;
        var run = presenter.Run;

        // 見かけデッキを確定候補として取得
        var shown = GetShownProfile(run.PlayerProfile);
        if (shown.Total != 30) return false;

        // コスト計算（デッキ + ゲージ）
        int totalCost = DraftCost();
        int gaugeCost = GaugeDraftCost();
        int deckCost = totalCost - gaugeCost;


        // 予算オーバー防止（分母は _pointsBudget）
        if (totalCost > _pointsBudget) return false;

        // --- 1) デッキ確定（ポイント消費はここでまとめて） ---
        if (totalCost > 0)
        {
            // DeckDraftCost 分だけデッキを確定しつつポイント消費
            // ※ RunState 側は「指定ポイント分だけ消費してデッキをセット」する想定
            bool ok = run.TryCommitDeckProfileByPoints(shown, totalCost);
            if (!ok) return false;
        }
        else
        {
            // コスト0でもデッキ内容を反映したい場合
            run.SetPlayerProfile(shown);
        }

        // --- 2) ゲージ確定（ポイントは既に消費済み） ---
        float buy = run.Tuning.GaugeBuyAmount;

        for (int i = 0; i < 3; i++)
        {
            int buyCount = _gaugeBuy[i];
            if (buyCount > 0)
                run.Gauge.Add((RpsColor)i, buyCount * buy);
        }



        run.SetReservedForcedOrder(
            new System.Collections.Generic.List<RpsColor>(_draftForcedOrder)
        );

        // --- 3) ドラフトリセット ---
        ResetDraft();
        _draftForcedOrder.Clear();

        // ★ 実ゲージを直接表示させる
        gaugeBarView.SetGauge(
            run.Gauge.Get(RpsColor.Gu),
            run.Gauge.Get(RpsColor.Choki),
            run.Gauge.Get(RpsColor.Pa),
            run.Gauge.Max
        );

        Refresh();
        return true;
    }



    // GaugeDraftCost：買った分だけ課金
    private int GaugeDraftCost()
    {
        return _gaugeBuy[0] + _gaugeBuy[1] + _gaugeBuy[2];
    }


    private void BindButtonsOnce()
    {

        if (_bound) return;
        _bound = true;

        if (deckPlusGu != null)    deckPlusGu.onClick.AddListener(() => OnDeckPlus(RpsColor.Gu));
        if (deckMinusGu != null)   deckMinusGu.onClick.AddListener(() => OnDeckMinus(RpsColor.Gu));

        if (deckPlusChoki != null)  deckPlusChoki.onClick.AddListener(() => OnDeckPlus(RpsColor.Choki));
        if (deckMinusChoki != null) deckMinusChoki.onClick.AddListener(() => OnDeckMinus(RpsColor.Choki));

        if (deckPlusPa != null)    deckPlusPa.onClick.AddListener(() => OnDeckPlus(RpsColor.Pa));
        if (deckMinusPa != null)   deckMinusPa.onClick.AddListener(() => OnDeckMinus(RpsColor.Pa));

        // Buy Gauge (+/-)
        if (buyGaugePlusGu != null)      buyGaugePlusGu.onClick.AddListener(() => OnBuyGaugePlus(RpsColor.Gu));
        if (buyGaugeMinusGu != null)     buyGaugeMinusGu.onClick.AddListener(() => OnBuyGaugeMinus(RpsColor.Gu));

        if (buyGaugePlusChoki != null)   buyGaugePlusChoki.onClick.AddListener(() => OnBuyGaugePlus(RpsColor.Choki));
        if (buyGaugeMinusChoki != null)  buyGaugeMinusChoki.onClick.AddListener(() => OnBuyGaugeMinus(RpsColor.Choki));

        if (buyGaugePlusPa != null)      buyGaugePlusPa.onClick.AddListener(() => OnBuyGaugePlus(RpsColor.Pa));
        if (buyGaugeMinusPa != null)     buyGaugeMinusPa.onClick.AddListener(() => OnBuyGaugeMinus(RpsColor.Pa));


    }

    private void OnDeckPlus(RpsColor c)
    {
        if (presenter == null || presenter.Run == null) return;
        var run = presenter.Run;

        // ★相殺：同色の - 予約があれば 먼저打ち消す
        int i = (int)c;
        if (_sub[i] > 0)
        {
            _sub[i] -= 1;
            Refresh();
            return;
        }

        // 相殺できないなら通常の + 予約
        if (!CanAdd(run)) return;
        _add[i] += 1;
        Refresh();
    }

    private void OnDeckMinus(RpsColor c)
    {
        if (presenter == null || presenter.Run == null) return;
        var run = presenter.Run;

        int i = (int)c;

        // ★相殺：同色の + 予約があれば 먼저打ち消す
        if (_add[i] > 0)
        {
            _add[i] -= 1;
            Refresh();
            return;
        }

        // 相殺できないなら通常の - 予約
        if (!CanSub(run)) return;

        var shown = GetShownProfile(run.PlayerProfile);
        if (shown.Get(c) <= 0) return;

        _sub[i] += 1;
        Refresh();
    }

    private void OnBuyGaugePlus(RpsColor c)
    {
        if (presenter == null || presenter.Run == null) return;
        var run = presenter.Run;

        if (!CanGaugeAdd(run)) return;
        if (!CanGaugeActuallyIncrease(c, run)) return;

        _gaugeBuy[(int)c] += 1;
        Refresh();
    }

    private void OnBuyGaugeMinus(RpsColor c)
    {
        int i = (int)c;
        if (_gaugeBuy[i] <= 0) return;   // 取り消す物がない
        _gaugeBuy[i] -= 1;               // ←取り消し
        Refresh();
    }

    private DeckProfile GetShownProfile(DeckProfile baseProfile)
    {
        int gu = baseProfile.Gu + _add[(int)RpsColor.Gu] - _sub[(int)RpsColor.Gu];
        int ch = baseProfile.Choki + _add[(int)RpsColor.Choki] - _sub[(int)RpsColor.Choki];
        int pa = baseProfile.Pa + _add[(int)RpsColor.Pa] - _sub[(int)RpsColor.Pa];
        return new DeckProfile(gu, ch, pa);
    }

    private int DraftCost()
    {
        int deckCost = Mathf.Max(_add[0] + _add[1] + _add[2], _sub[0] + _sub[1] + _sub[2]);
        int gaugeCost = GaugeDraftCost(); // ←ここが「合計」になってる
        return deckCost + gaugeCost;
    }


    private int PointsLeft()
    {
        return _pointsBudget - DraftCost();
    }



    private void ResetDraft()
    {
        for (int i = 0; i < 3; i++)
        {
            _add[i] = 0;
            _sub[i] = 0;
            _gaugeBuy[i] = 0; // ←これ
        }
        _draftForcedOrder.Clear(); // あなたの現行に合わせて
    }




    private bool CanAdd(RunState run)
    {
        // --- デッキ側 ---
        int sumAdd = _add[0] + _add[1] + _add[2];
        int sumSub = _sub[0] + _sub[1] + _sub[2];
        int deckCostAfter = Mathf.Max(sumAdd + 1, sumSub);

        // --- ゲージ側（max方式） ---
        int gaugeCost = GaugeDraftCost();

        // 合計コスト
        int totalAfter = deckCostAfter + gaugeCost;

        return totalAfter <= _pointsBudget;
    }


    private bool CanSub(RunState run)
    {
        int sumAdd = _add[0] + _add[1] + _add[2];
        int sumSub = _sub[0] + _sub[1] + _sub[2];
        int deckCostAfter = Mathf.Max(sumAdd, sumSub + 1);

        int gaugeCost = GaugeDraftCost();
        int totalAfter = deckCostAfter + gaugeCost;

        return totalAfter <= _pointsBudget;
    }



    private bool CanGaugeAdd(RunState run)
    {
        int gaugeCostAfter = GaugeDraftCost() + 1;

        int deckCost =
            Mathf.Max(_add[0] + _add[1] + _add[2],
                      _sub[0] + _sub[1] + _sub[2]);

        return deckCost + gaugeCostAfter <= _pointsBudget;
    }



    private bool CanGaugeActuallyDecrease(RpsColor c, RunState run)
    {
        int i = (int)c;
        return _gaugeBuy[i] > 0; // 取り消せる購入があるか
    }

    private void RefreshGaugePreview()
    {
        var run = presenter.Run;
        float buy = run.Tuning.GaugeBuyAmount;

        float gu = run.Gauge.Get(RpsColor.Gu) + _gaugeBuy[(int)RpsColor.Gu] * buy;
        float ch = run.Gauge.Get(RpsColor.Choki) + _gaugeBuy[(int)RpsColor.Choki] * buy;
        float pa = run.Gauge.Get(RpsColor.Pa) + _gaugeBuy[(int)RpsColor.Pa] * buy;

        // 予約中は「次で消費される」見せ方（Maxぶん減らす）
        gu -= GetDraftForcedCount(RpsColor.Gu) * run.Gauge.Max;
        ch -= GetDraftForcedCount(RpsColor.Choki) * run.Gauge.Max;
        pa -= GetDraftForcedCount(RpsColor.Pa) * run.Gauge.Max;

        if (gu < 0f) gu = 0f;
        if (ch < 0f) ch = 0f;
        if (pa < 0f) pa = 0f;

        gaugeBarView.SetGauge(gu, ch, pa, run.Gauge.Max);
    }


    public bool ToggleDraftForcedFirst(RpsColor c)
    {
        if (presenter == null || presenter.Run == null) return false;
        var run = presenter.Run;

        var shownDeck = GetShownProfile(run.PlayerProfile);
        if (!shownDeck.Has(c)) return false;

        // この色で “今” 予約できる最大回数（ゲージとデッキから計算）
        int maxByGauge = Mathf.FloorToInt(GetShownGaugeValue_NoClamp(c, run) / run.Gauge.Max);
        int maxByDeck  = shownDeck.Get(c);
        int maxByHand  = run.Tuning.HandCount;

        int maxCount = Mathf.Min(maxByGauge, maxByDeck, maxByHand);
        if (maxCount <= 0)
        {
            // 予約不能なら、この色の予約だけ消す（クリックでやり直しできる）
            RemoveAllFromDraftOrder(c);
            Refresh();
            return true;
        }

        int cur = GetDraftForcedCount(c);

        if (cur < maxCount)
        {
            // 0->1->2...：末尾に追加（順番を記録）
            _draftForcedOrder.Add(c);
        }
        else
        {
            // max->0：その色を全消し（順番ミスった時のやり直し）
            RemoveAllFromDraftOrder(c);
        }

        Refresh();
        return true;
    }

    private void RemoveAllFromDraftOrder(RpsColor c)
    {
        for (int i = _draftForcedOrder.Count - 1; i >= 0; i--)
            if (_draftForcedOrder[i] == c)
                _draftForcedOrder.RemoveAt(i);
    }



    private bool IsShownGaugeCharged(RpsColor c, RunState run)
    {
        float v = GetShownGaugeValue_NoClamp(c, run);
        return v >= run.Gauge.Max - 1e-6f;
    }

    private float GetShownGaugeValue(RpsColor c, RunState run)
    {
        float buy = run.Tuning.GaugeBuyAmount;
        float v = run.Gauge.Get(c) + _gaugeBuy[(int)c] * buy;
        if (v < 0f) v = 0f;
        if (v > run.Gauge.Max) v = run.Gauge.Max;
        return v;
    }


    private float GetShownGaugeValue_NoClamp(RpsColor c, RunState run)
    {
        float buy = run.Tuning.GaugeBuyAmount;
        float v = run.Gauge.Get(c) + _gaugeBuy[(int)c] * buy;
        if (v < 0f) v = 0f;
        return v;
    }



    private bool CanGaugeActuallyIncrease(RpsColor c, RunState run)
    {
        // 0.95でも「まだ満タンじゃない」なら買える
        // スタックがあっても「次の1回分が満タンか？」で増やせる/増やせないを決めたいなら余りを見る
        float v = GetShownGaugeValue_NoClamp(c, run);
        float rem = v - Mathf.Floor(v / run.Gauge.Max) * run.Gauge.Max;
        return rem < run.Gauge.Max - 1e-6f;
    }


    // 色ごとの予約数（表示用）
    public int GetDraftForcedCount(RpsColor c)
    {
        int n = 0;
        for (int i = 0; i < _draftForcedOrder.Count; i++)
            if (_draftForcedOrder[i] == c) n++;
        return n;
    }

    public string GetDraftForcedOrderLabelJa()
    {
        if (_draftForcedOrder == null || _draftForcedOrder.Count <= 0)
            return "";

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < _draftForcedOrder.Count; i++)
        {
            if (i > 0) sb.Append("→");
            sb.Append(ToJpColor(_draftForcedOrder[i]));
        }
        return sb.ToString();
    }

    // AdjustPanelView 内に ToJpColor が無ければ追加（既にあるなら流用）
    private static string ToJpColor(RpsColor c)
    {
        return c switch
        {
            RpsColor.Gu => "グー",
            RpsColor.Choki => "チョキ",
            _ => "パー"
        };
    }

    private void ValidateDraftForcedOrder()
    {
        if (_draftForcedOrder == null || _draftForcedOrder.Count <= 0) return;
        if (presenter == null || presenter.Run == null) { _draftForcedOrder.Clear(); return; }

        var run = presenter.Run;
        var shownDeck = GetShownProfile(run.PlayerProfile);

        // 色ごとの上限（ゲージスタック・デッキ枚数・手数）
        int maxGu = Mathf.Min(
            Mathf.FloorToInt(GetShownGaugeValue_NoClamp(RpsColor.Gu, run) / run.Gauge.Max),
            shownDeck.Get(RpsColor.Gu),
            run.Tuning.HandCount
        );
        int maxCh = Mathf.Min(
            Mathf.FloorToInt(GetShownGaugeValue_NoClamp(RpsColor.Choki, run) / run.Gauge.Max),
            shownDeck.Get(RpsColor.Choki),
            run.Tuning.HandCount
        );
        int maxPa = Mathf.Min(
            Mathf.FloorToInt(GetShownGaugeValue_NoClamp(RpsColor.Pa, run) / run.Gauge.Max),
            shownDeck.Get(RpsColor.Pa),
            run.Tuning.HandCount
        );

        // まず「デッキに無い色」は全部消す
        for (int i = _draftForcedOrder.Count - 1; i >= 0; i--)
        {
            var c = _draftForcedOrder[i];
            if (!shownDeck.Has(c)) _draftForcedOrder.RemoveAt(i);
        }

        // 次に「色ごとの上限」を超えた分を末尾から削る
        while (GetDraftForcedCount(RpsColor.Gu) > maxGu) RemoveLastOf(RpsColor.Gu);
        while (GetDraftForcedCount(RpsColor.Choki) > maxCh) RemoveLastOf(RpsColor.Choki);
        while (GetDraftForcedCount(RpsColor.Pa) > maxPa) RemoveLastOf(RpsColor.Pa);

        // 最後に「手数上限」を超えたら末尾から削る（安全）
        while (_draftForcedOrder.Count > run.Tuning.HandCount)
            _draftForcedOrder.RemoveAt(_draftForcedOrder.Count - 1);
    }

    private void RemoveLastOf(RpsColor c)
    {
        for (int i = _draftForcedOrder.Count - 1; i >= 0; i--)
        {
            if (_draftForcedOrder[i] == c)
            {
                _draftForcedOrder.RemoveAt(i);
                return;
            }
        }
    }

    private int GetShownChargedCount(RpsColor c, RunState run)
    {
        // 実ゲージ + 購入ドラフト の合計値（Maxでclampしない）
        float v = GetShownGaugeValue_NoClamp(c, run);

        // Core側と同じ ε で floor
        return (int)System.Math.Floor((v + 1e-6f) / run.Gauge.Max);
    }





}
