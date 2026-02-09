using UnityEngine;
using UnityEngine.UI;
using TMPro;
using RpsBuild.Core;


public sealed class RoundFlowUI : MonoBehaviour
{
    public enum Phase { Resolve, Adjust }

    [Header("Core")]
    [SerializeField] private RunPresenter presenter;

    [Header("Bottom Proceed Panel")]
    [SerializeField] private Button bottomPanelButton;
    [SerializeField] private TMP_Text bottomHintText; // 任意

    [SerializeField] private Button resolveTapCatcherButton;

    [Header("SafeRect Children")]
    [SerializeField] private GameObject resolveRoot;
    [SerializeField] private GameObject adjustPanel;

    [SerializeField] private AdjustPanelView adjustView;

    private Phase _phase = Phase.Resolve;

    private void Reset()
    {
        presenter = FindFirstObjectByType<RunPresenter>();
    }

    private void Start()
    {
        if (bottomPanelButton != null)
            bottomPanelButton.onClick.AddListener(OnProceed);

        if (adjustView != null)
            adjustView.OnDraftChanged += HandleAdjustChanged;

        if (resolveTapCatcherButton != null)
            resolveTapCatcherButton.onClick.AddListener(OnProceed);


        SetPhase(Phase.Resolve);
        RefreshProceedInteractable();
    }

    private void OnProceed()
        {
            if (presenter == null) return;

            if (presenter != null && presenter.Run != null && presenter.Run.IsGameOver)
            {
                SceneFlow.GoToResult(presenter.Run.Score);
                return;
            }


            if (_phase == Phase.Resolve)
            {
                // ★追加：結果表示アニメ中のタップは「遷移」ではなく「スキップ」
                if (presenter.TrySkipResolveSequence())
                    return;

                // Resolve → Adjust
                SetPhase(Phase.Adjust);

                if (adjustView != null)
                    adjustView.Open(presenter);

                RefreshAdjustRoundLog();
                presenter.RefreshHud();
                RefreshProceedInteractable();
                return;
            }

            if (adjustView != null)
            {
                if (!adjustView.TryCommitDraft())
                {
                    RefreshProceedInteractable();
                    return;
                }
            }

            SetPhase(Phase.Resolve);
            presenter.RefreshHud();
            presenter.PlayNextRound();
            presenter.RefreshHud();
            RefreshProceedInteractable();
        }

    private void SetPhase(Phase p)
    {
        _phase = p;

        if (resolveRoot != null)
            resolveRoot.SetActive(p == Phase.Resolve);

        bool showAdjust = (p == Phase.Adjust);
        if (adjustPanel != null)
            adjustPanel.SetActive(showAdjust);

        if (bottomHintText != null)
            bottomHintText.text = (p == Phase.Resolve)
                ? "次へ（調整）"
                : "次ラウンド開始";

        // ★Adjust中は確定ドロー予約状況をRoundLogに出す
        if (p == Phase.Adjust)
        RefreshAdjustRoundLog();

    }

    private void RefreshProceedInteractable()
    {
        if (bottomPanelButton == null) return;

        if (_phase == Phase.Resolve)
        {
            bottomPanelButton.interactable = true;
            return;
        }

        bottomPanelButton.interactable =
            adjustView == null || adjustView.CanProceedToNextRound();
    }

    private void OnDestroy()
    {
        if (adjustView != null)
            adjustView.OnDraftChanged -= HandleAdjustChanged;
    }

    private void HandleAdjustChanged()
    {
        if (_phase == Phase.Adjust)
        {
            RefreshProceedInteractable();

            if (presenter != null && presenter.Run != null && !presenter.Run.LastRoundWasIntro)
                        RefreshAdjustRoundLog();// ★ドラフト変化で表示更新
        }
    }

    private void RefreshAdjustRoundLog()
    {
        if (presenter == null) return;

        // ★チュートリアル直後（Round0の後）は、説明文を残したいので上書きしない
        if (presenter.Run.LastRoundWasIntro) return;

        presenter.SetRoundLog(BuildAdjustRoundLog());
    }

    private string BuildAdjustRoundLog()
    {
        if (adjustView == null) return "確定ドロー：予約なし";

        int gu = adjustView.GetDraftForcedCount(RpsColor.Gu);
        int ch = adjustView.GetDraftForcedCount(RpsColor.Choki);
        int pa = adjustView.GetDraftForcedCount(RpsColor.Pa);

        if (gu + ch + pa <= 0) return "確定ドロー：予約なし";

        // 1) まとめ表示：グー×2 / チョキ×1
        var sb = new System.Text.StringBuilder("確定ドロー：");
        bool first = true;

        void Add(RpsColor c, int n)
        {
            if (n <= 0) return;
            if (!first) sb.Append(" / ");
            first = false;
            sb.Append($"{ToJpColor(c)}×{n}");
        }

        Add(RpsColor.Gu, gu);
        Add(RpsColor.Choki, ch);
        Add(RpsColor.Pa, pa);

        // 2) 順番表示：グー→グー→チョキ
        //    ※ AdjustPanelView に GetDraftForcedOrderLabelJa() を追加して使う（推奨）
        string order = adjustView.GetDraftForcedOrderLabelJa();
        if (!string.IsNullOrEmpty(order))
            sb.Append($"\n順番：{order}");

        return sb.ToString();
    }



    private static string ToJpColor(RpsBuild.Core.RpsColor c)
    {
        return c switch
        {
            RpsBuild.Core.RpsColor.Gu => "グー",
            RpsBuild.Core.RpsColor.Choki => "チョキ",
            _ => "パー"
        };
    }




}
