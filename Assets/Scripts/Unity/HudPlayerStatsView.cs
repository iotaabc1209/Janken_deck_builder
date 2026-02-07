using UnityEngine;
using UnityEngine.UI;   // ← これを追加
using TMPro;
using RpsBuild.Core;

public sealed class HudPlayerStatsView : MonoBehaviour
{
    [Header("Life (Hearts)")]
    [SerializeField] private Image heart1;
    [SerializeField] private Image heart2;
    [SerializeField] private Image heart3;

    [Header("Values")]
    [SerializeField] private TMP_Text pointsValueText; // xN
    [SerializeField] private TMP_Text scoreValueText;  // xN

    private Image[] _hearts;

    private void Awake()
    {
        _hearts = new[] { heart1, heart2, heart3 };
    }

    public void Render(RunState run)
    {
        if (run == null)
        {
            SetHearts(0);
            if (pointsValueText != null) pointsValueText.text = "x0";
            if (scoreValueText != null)  scoreValueText.text = "x0";
            return;
        }

        int livesLeft = Mathf.Max(0, run.Tuning.MaxMiss - run.MissCount);
        SetHearts(livesLeft);

        if (pointsValueText != null)
            pointsValueText.text = $"x{run.Points}";

        if (scoreValueText != null)
            scoreValueText.text = $"x{run.Score}";

        Debug.Log($"[HUD ScoreText] obj={gameObject.name} id={GetInstanceID()} scoreText={(scoreValueText!=null?scoreValueText.name:"null")} shown={scoreValueText?.text} score={run.Score} round={run.RoundIndex}");

    }

    private void SetHearts(int lives)
    {
        for (int i = 0; i < _hearts.Length; i++)
        {
            if (_hearts[i] == null) continue;

            bool on = i < lives;
            _hearts[i].color = on
                ? Color.white
                : new Color(1f, 1f, 1f, 0.25f);
        }
    }
}
