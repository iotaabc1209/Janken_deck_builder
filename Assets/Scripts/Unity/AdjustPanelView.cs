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
    [SerializeField] private TMP_Text gaugeText;   // "G/C/P"

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
    private readonly int[] _gaugeAdd = new int[3];
    private readonly int[] _gaugeSub = new int[3];

    // Adjust中の「確定ドロー予約」ドラフト
    private RpsColor? _draftForcedFirstColor = null;

    // ForcedFirstToggleView から参照する用（Adjust中表示）
    public RpsColor? DraftForcedFirstColor => _draftForcedFirstColor;


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
        _draftForcedFirstColor = presenter.Run.ReservedForcedFirst; // 基本nullのはずだが保険
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


        // 4. ポイント・ゲージなど既存表示
        if (pointsText != null) pointsText.text = $"　　x{PointsLeft()} / {_pointsBudget}";

        // ゲージ表示（ドラフト購入分を含めた「見かけ」） ---
        float buy = run.Tuning.GaugeBuyAmount;

        float gGu = Mathf.Min(run.Gauge.Max,
            run.Gauge.Get(RpsColor.Gu)
            + (_gaugeAdd[(int)RpsColor.Gu] - _gaugeSub[(int)RpsColor.Gu]) * buy);

        float gCh = Mathf.Min(run.Gauge.Max,
            run.Gauge.Get(RpsColor.Choki)
            + (_gaugeAdd[(int)RpsColor.Choki] - _gaugeSub[(int)RpsColor.Choki]) * buy);

        float gPa = Mathf.Min(run.Gauge.Max,
            run.Gauge.Get(RpsColor.Pa)
            + (_gaugeAdd[(int)RpsColor.Pa] - _gaugeSub[(int)RpsColor.Pa]) * buy);


        if (gaugeText != null)
            gaugeText.text = "";


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
            bool canGaugeAdd = CanGaugeAdd(run);
            bool canGaugeSub = CanGaugeSub(run);

            // 相殺できるなら「ポイント0でも押せる」（デッキと同型）
            bool canGaugeCancelPlusGu    = _gaugeSub[(int)RpsColor.Gu] > 0;
            bool canGaugeCancelPlusChoki = _gaugeSub[(int)RpsColor.Choki] > 0;
            bool canGaugeCancelPlusPa    = _gaugeSub[(int)RpsColor.Pa] > 0;

            bool canGaugeCancelMinusGu    = _gaugeAdd[(int)RpsColor.Gu] > 0;
            bool canGaugeCancelMinusChoki = _gaugeAdd[(int)RpsColor.Choki] > 0;
            bool canGaugeCancelMinusPa    = _gaugeAdd[(int)RpsColor.Pa] > 0;

            // interactable（押せる/押せない）
            if (buyGaugePlusGu != null)
                buyGaugePlusGu.interactable =
                    (canGaugeAdd && CanGaugeActuallyIncrease(RpsColor.Gu, run))
                    || canGaugeCancelPlusGu;

            if (buyGaugePlusChoki != null)
                buyGaugePlusChoki.interactable =
                    (canGaugeAdd && CanGaugeActuallyIncrease(RpsColor.Choki, run))
                    || canGaugeCancelPlusChoki;

            if (buyGaugePlusPa != null)
                buyGaugePlusPa.interactable =
                    (canGaugeAdd && CanGaugeActuallyIncrease(RpsColor.Pa, run))
                    || canGaugeCancelPlusPa;


            // GaugeMinus は
            // 1) 本当に減らせる  OR
            // 2) 同色の + を相殺できる
            if (buyGaugeMinusGu != null)
                buyGaugeMinusGu.interactable =
                    (canGaugeSub && CanGaugeActuallyDecrease(RpsColor.Gu, run))
                    || canGaugeCancelMinusGu;

            if (buyGaugeMinusChoki != null)
                buyGaugeMinusChoki.interactable =
                    (canGaugeSub && CanGaugeActuallyDecrease(RpsColor.Choki, run))
                    || canGaugeCancelMinusChoki;

            if (buyGaugeMinusPa != null)
                buyGaugeMinusPa.interactable =
                    (canGaugeSub && CanGaugeActuallyDecrease(RpsColor.Pa, run))
                    || canGaugeCancelMinusPa;

            RefreshGaugePreview();
            ValidateDraftForcedFirst();
            OnDraftChanged?.Invoke();


    }

    // RoundFlowUI から「次へ」を押す前にチェックする用
    public bool CanProceedToNextRound()
    {
        if (presenter == null || presenter.Run == null) return false;

        var run = presenter.Run;
        var shown = GetShownProfile(run.PlayerProfile);

        // 30枚制約
        if (shown.Total != 30) return false;

        // 予算は Adjust開始時点の固定分母で判定（Commitと揃える）
        if (DraftCost() > _pointsBudget) return false;

        // B1: 予約ドラフトがあるなら成立条件を満たしている必要
        if (_draftForcedFirstColor.HasValue)
        {
            var c = _draftForcedFirstColor.Value;
            if (!shown.Has(c)) return false;
            if (!IsShownGaugeCharged(c, run)) return false;
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
        int deckCost = DeckDraftCost();
        int gaugeCost = GaugeDraftCost();
        int totalCost = deckCost + gaugeCost;

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
            int delta = _gaugeAdd[i] - _gaugeSub[i];
            if (delta > 0)
            {
                run.Gauge.Add((RpsColor)i, delta * buy);
            }
        }

        // --- 2.5) 確定ドロー予約を確定（ゲージ確定後なので成功する） ---
        if (_draftForcedFirstColor.HasValue)
        {
            var c = _draftForcedFirstColor.Value;
            bool ok = run.TryReserveForcedFirst(c);
            if (!ok) return false;
        }


        // --- 3) ドラフトリセット ---
        ResetDraft();

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



    private int DeckDraftCost()
    {
        int sumAdd = _add[0] + _add[1] + _add[2];
        int sumSub = _sub[0] + _sub[1] + _sub[2];
        return (sumAdd > sumSub) ? sumAdd : sumSub;
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

        int i = (int)c;

        // ★ 相殺：同色の - 予約があれば先に打ち消す
        if (_gaugeSub[i] > 0)
        {
            _gaugeSub[i] -= 1;
            Refresh();
            return;
        }

        // ★ 通常の + 予約（予算チェック）
        if (!CanGaugeAdd(run)) return;

        // ★ すでに満タンなら増やせない（見かけゲージ基準）
        if (!CanGaugeActuallyIncrease(c, run)) return;

        _gaugeAdd[i] += 1;
        Refresh();
    }

    private void OnBuyGaugeMinus(RpsColor c)
    {
        if (presenter == null || presenter.Run == null) return;
        var run = presenter.Run;

        int i = (int)c;

        // 相殺：同色の + 予約があれば先に打ち消す
        if (_gaugeAdd[i] > 0)
        {
            _gaugeAdd[i] -= 1;
            Refresh();
            return;
        }

        // マイナス予約（＝買いを取り消す）を入れる
        // ※0より下には行かせない（見かけゲージがマイナスにならないように）
        float buy = run.Tuning.GaugeBuyAmount;
        float current = run.Gauge.Get(c);
        float after = current + (_gaugeAdd[i] - (_gaugeSub[i] + 1)) * buy;
        if (after < 0f) return;

        if (!CanGaugeSub(run)) return;
        _gaugeSub[i] += 1;
        Refresh();
    }


    private int GaugeDraftCost()
    {
        int sumAdd = _gaugeAdd[0] + _gaugeAdd[1] + _gaugeAdd[2];
        int sumSub = _gaugeSub[0] + _gaugeSub[1] + _gaugeSub[2];
        return Mathf.Max(sumAdd, sumSub);
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
        int gaugeCost = GaugeDraftCost();
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
            _gaugeAdd[i] = 0;
            _gaugeSub[i] = 0;
        }
        _draftForcedFirstColor = null;

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
        int sumAdd = _gaugeAdd[0] + _gaugeAdd[1] + _gaugeAdd[2];
        int sumSub = _gaugeSub[0] + _gaugeSub[1] + _gaugeSub[2];

        // 1増やした後のゲージコスト
        int gaugeCostAfter = Mathf.Max(sumAdd + 1, sumSub);

        // デッキ側の現コスト
        int deckCost = Mathf.Max(_add[0] + _add[1] + _add[2], _sub[0] + _sub[1] + _sub[2]);

        return deckCost + gaugeCostAfter <= _pointsBudget;
    }

    private bool CanGaugeSub(RunState run)
    {
        int sumAdd = _gaugeAdd[0] + _gaugeAdd[1] + _gaugeAdd[2];
        int sumSub = _gaugeSub[0] + _gaugeSub[1] + _gaugeSub[2];

        int gaugeCostAfter = Mathf.Max(sumAdd, sumSub + 1);

        int deckCost = Mathf.Max(_add[0] + _add[1] + _add[2], _sub[0] + _sub[1] + _sub[2]);

        return deckCost + gaugeCostAfter <= _pointsBudget;
    }

    private bool CanGaugeActuallyDecrease(RpsColor c, RunState run)
    {
        int i = (int)c;
        float buy = run.Tuning.GaugeBuyAmount;

        float current =
            run.Gauge.Get(c)
            + (_gaugeAdd[i] - _gaugeSub[i]) * buy;

        return current >= buy; // 1回分は減らせるか
    }

    private void RefreshGaugePreview()
    {
        var run = presenter.Run;
        float buy = run.Tuning.GaugeBuyAmount;

        float gu = run.Gauge.Get(RpsColor.Gu)
                   + _gaugeAdd[(int)RpsColor.Gu] * buy
                   - _gaugeSub[(int)RpsColor.Gu] * buy;

        float ch = run.Gauge.Get(RpsColor.Choki)
                   + _gaugeAdd[(int)RpsColor.Choki] * buy
                   - _gaugeSub[(int)RpsColor.Choki] * buy;

        float pa = run.Gauge.Get(RpsColor.Pa)
                   + _gaugeAdd[(int)RpsColor.Pa] * buy
                   - _gaugeSub[(int)RpsColor.Pa] * buy;

        // ドラフト予約中は「その色は次ラウンド開始で消費される」見せ方
        if (_draftForcedFirstColor == RpsColor.Gu) gu = 0f;
        if (_draftForcedFirstColor == RpsColor.Choki) ch = 0f;
        if (_draftForcedFirstColor == RpsColor.Pa) pa = 0f;


        gaugeBarView.SetGauge(gu, ch, pa, run.Gauge.Max);
        OnDraftChanged?.Invoke();

    }

    public bool ToggleDraftForcedFirst(RpsColor c)
    {
        if (presenter == null || presenter.Run == null) return false;
        var run = presenter.Run;

        if (_draftForcedFirstColor == c)
        {
            _draftForcedFirstColor = null;
            Refresh();
            return true;
        }

        var shownDeck = GetShownProfile(run.PlayerProfile);
        if (!shownDeck.Has(c))
        {
            Debug.Log($"[ForcedDraft] blocked: deck has no {c} (shown)");
            return false;
        }

        if (!IsShownGaugeCharged(c, run))
        {
            Debug.Log($"[ForcedDraft] blocked: gauge not charged {c} (shown={GetShownGaugeValue(c, run):0.###}, max={run.Gauge.Max:0.###})");
            return false;
        }

        _draftForcedFirstColor = c;
        Refresh();
        return true;
    }


    private bool IsShownGaugeCharged(RpsColor c, RunState run)
    {
        float v = GetShownGaugeValue(c, run);
        return v >= run.Gauge.Max - 1e-6f;
    }

    private float GetShownGaugeValue(RpsColor c, RunState run)
    {
        float buy = run.Tuning.GaugeBuyAmount;
        int i = (int)c;

        float v = run.Gauge.Get(c) + (_gaugeAdd[i] - _gaugeSub[i]) * buy;
        if (v < 0f) v = 0f;
        if (v > run.Gauge.Max) v = run.Gauge.Max;
        return v;
    }

    private bool CanGaugeActuallyIncrease(RpsColor c, RunState run)
    {
        // 0.95でも「まだ満タンじゃない」なら買える
        return GetShownGaugeValue(c, run) < run.Gauge.Max - 1e-6f;
    }




    private void ValidateDraftForcedFirst()
    {
        if (! _draftForcedFirstColor.HasValue) return;
        if (presenter == null || presenter.Run == null) { _draftForcedFirstColor = null; return; }

        var run = presenter.Run;
        var shownDeck = GetShownProfile(run.PlayerProfile);
        var c = _draftForcedFirstColor.Value;

        if (!shownDeck.Has(c) || !IsShownGaugeCharged(c, run))
            _draftForcedFirstColor = null;
    }

}
