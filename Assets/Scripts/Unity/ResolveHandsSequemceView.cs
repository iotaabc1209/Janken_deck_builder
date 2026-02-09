// Assets/Scripts/Unity/ResolveHandsSequenceView.cs
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using RpsBuild.Core;

public sealed class ResolveHandsSequenceView : MonoBehaviour
{
    [Header("Enemy / Player Icons (0..6)")]
    [SerializeField] private List<Image> enemyIcons = new();
    [SerializeField] private List<Image> playerIcons = new();

    [Header("Result Texts (0..6)")]
    [SerializeField] private List<TMP_Text> resultTexts = new();

    [Header("Sprites")]
    [SerializeField] private Sprite gu;
    [SerializeField] private Sprite choki;
    [SerializeField] private Sprite pa;

    [Header("Result Colors")]
    [SerializeField] private Color winColor  = new(0.2f, 0.8f, 0.9f);
    [SerializeField] private Color tieColor  = new(0.8f, 0.8f, 0.8f);
    [SerializeField] private Color loseColor = new(0.9f, 0.3f, 0.3f);
    [SerializeField] private Color bonusColor = new(1.0f, 0.85f, 0.25f); // 金系（好みで）


    [Header("Timing")]
    [SerializeField] private float stepDelay = 0.15f;

    private Coroutine _co;

    // ★追加：状態管理
    private bool _isPlaying = false;
    private bool _skipRequested = false;

    // ★追加：外から参照（RoundFlowUIで「スキップなら遷移しない」に使える）
    public bool IsPlaying => _isPlaying;

    // ★追加：外からスキップ要求（再生中のみ有効）
    public bool RequestSkip()
    {
        if (!_isPlaying) return false;
        _skipRequested = true;
        return true;
    }

    // ===== Public API =====

    public void Play(
        IReadOnlyList<RpsColor> enemyHands,
        IReadOnlyList<RpsColor> playerHands,
        IReadOnlyList<RpsOutcome> outcomes,
        IReadOnlyList<int> highlightIndices = null)
    {
        StopSequence();     // ★ まず必ず止める
        ResetView();        // ★ 表示を完全初期化

        _skipRequested = false;
        _isPlaying = true;

        // HandsPanel が active であることは呼び元（RoundFlowUI）で保証
        _co = StartCoroutine(Sequence(enemyHands, playerHands, outcomes, highlightIndices));
    }

    // ===== Core =====

    private IEnumerator Sequence(
        IReadOnlyList<RpsColor> e,
        IReadOnlyList<RpsColor> p,
        IReadOnlyList<RpsOutcome> o,
        IReadOnlyList<int> highlightIndices)
    {
        int n = Mathf.Min(7,
            e.Count, p.Count, o.Count,
            enemyIcons.Count, playerIcons.Count, resultTexts.Count);

        for (int i = 0; i < n; i++)
        {
            bool highlight = IsHighlighted(i, highlightIndices);
            ShowOne(i, e[i], p[i], o[i], highlight);

            if (_skipRequested)
            {
                for (int j = i + 1; j < n; j++)
                {
                    bool h2 = IsHighlighted(j, highlightIndices);
                    ShowOne(j, e[j], p[j], o[j], h2);
                }
                break;
            }

            yield return new WaitForSeconds(stepDelay);
        }

        _co = null;
        _skipRequested = false;
        _isPlaying = false;
    }


    private void ShowOne(int i, RpsColor e, RpsColor p, RpsOutcome o, bool highlight)
    {
        // Enemy
        if (i < enemyIcons.Count && enemyIcons[i] != null)
        {
            enemyIcons[i].sprite = ToSprite(e);
            enemyIcons[i].gameObject.SetActive(true);
        }

        // Player
        if (i < playerIcons.Count && playerIcons[i] != null)
        {
            playerIcons[i].sprite = ToSprite(p);
            playerIcons[i].gameObject.SetActive(true);
        }

        // Result
        var t = resultTexts[i];
            if (t != null)
            {
                ApplyResult(t, o, highlight);
            }
    }

    // ===== Helpers =====

    private void ResetView()
    {
        for (int i = 0; i < enemyIcons.Count; i++)
        {
            if (enemyIcons[i] != null)
                enemyIcons[i].gameObject.SetActive(false);
        }

        for (int i = 0; i < playerIcons.Count; i++)
        {
            if (playerIcons[i] != null)
                playerIcons[i].gameObject.SetActive(false);
        }

        // ★ Resultは Active を切らず、アルファで隠す（Layoutが安定する）
        for (int i = 0; i < resultTexts.Count; i++)
        {
            var t = resultTexts[i];
            if (t == null) continue;

            t.text = "";            // 念のため空
            t.color = Color.white;  // 色リセット
            t.alpha = 0f;           // ★ 見えなくする
        }
    }

    private void StopSequence()
    {
        if (_co != null)
        {
            StopCoroutine(_co);
            _co = null;
        }
        _isPlaying = false;
        _skipRequested = false;
    }

    void ApplyResult(TMP_Text t, RpsOutcome o, bool highlight)
    {
        if (t == null) return;

        if (highlight)
            {
                // outcomeに合わせて文字だけ変える（BalanceでDもあり得るので）
                t.text = o switch
                {
                    RpsOutcome.Win  => "★W",
                    RpsOutcome.Tie  => "★D",
                    _               => "★L"
                };
                t.color = bonusColor;
                t.alpha = 1f;
                return;
            }

        switch (o)
        {
            case RpsOutcome.Win:
                t.text = "W"; t.color = winColor; break;
            case RpsOutcome.Tie:
                t.text = "D"; t.color = tieColor; break;
            case RpsOutcome.Lose:
                t.text = "L"; t.color = loseColor; break;
        }

        t.alpha = 1f; // ★ ここで初めて見せる
    }

    private Sprite ToSprite(RpsColor c) => c switch
    {
        RpsColor.Gu => gu,
        RpsColor.Choki => choki,
        RpsColor.Pa => pa,
        _ => null
    };

    private static bool IsHighlighted(int index, IReadOnlyList<int> list)
    {
        if (list == null) return false;
        for (int i = 0; i < list.Count; i++)
            if (list[i] == index) return true;
        return false;
    }

}
