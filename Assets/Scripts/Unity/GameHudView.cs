using UnityEngine;
using TMPro;
using RpsBuild.Core;

public sealed class GameHudView : MonoBehaviour
{
    [Header("TMP")]
    [SerializeField] private TMP_Text headerText;   // 左上：敵/環境/次の敵
    [SerializeField] private TMP_Text roundLogText; // ログ（任意）

    [Header("Player HUD (Bottom Right)")]
    [SerializeField] private HudPlayerStatsView playerStatsView;

    public void Render(RunPresenter p)
    {
        if (p == null || p.Run == null)
        {
            if (headerText != null)
                headerText.text = "HUD: presenter/run not set";

            if (playerStatsView != null)
                playerStatsView.Render(null);

            return;
        }

        var run = p.Run;
        var tuning = run.Tuning;

        // 左上：敵関連（次の敵＋環境＋統計）
        if (headerText != null)
        {
            var env = run.Environment.Weights;

            string indent = "　"; // 全角スペース1つ

            // 次の敵（手＋アーキタイプ）
            string nextEnemyLabel =
                run.PreviewEnemyArchetype.ToJaLabel(run.PreviewEnemyMainColor);

            // 環境 行（1行分）
            string EnvLine(EnemyArchetype a, float w)
            {
                if (!run.TryGetEnemyMainColor(a, out var main))
                    main = RpsColor.Gu;

                return $"{a.ToJaLabel(main)} {w:0.00}";
            }

            // 統計（2行構成）
            string StatBlock(EnemyArchetype a)
            {
                if (!run.TryGetEnemyMainColor(a, out var main))
                    main = RpsColor.Gu;

                var st = run.GetArchetypeHandStat(a);

                // 1行目：グー偏重（N=4）
                string line1 = $"{a.ToJaLabel(main)}（N={st.N}）";

                // 2行目：インデント＋内訳
                string line2 = $"{indent}グー:{st.Gu}  チョキ:{st.Choki}  パー:{st.Pa}";

                return $"{line1}\n{line2}";
            }

            // --- 重み降順で並べ替えた順を、環境も統計も共通で使う ---
            var items = new System.Collections.Generic.List<(EnemyArchetype a, float w)>
            {
                (EnemyArchetype.Heavy, env.Heavy),
                (EnemyArchetype.Balance, env.Balance),
                (EnemyArchetype.TwinTop, env.TwinTop),
            };

            items.Sort((x, y) =>
            {
                int c = y.w.CompareTo(x.w);   // 降順
                if (c != 0) return c;
                return x.a.CompareTo(y.a);    // 同値なら安定化
            });

            // 環境テキスト（頻度順）
            var envSb = new System.Text.StringBuilder();
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0) envSb.Append('\n');
                envSb.Append(indent);
                envSb.Append(EnvLine(items[i].a, items[i].w));
            }
            string envText = envSb.ToString();

            // 統計テキスト（同じ順）
            var statSb = new System.Text.StringBuilder();
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0) statSb.Append('\n');
                statSb.Append(StatBlock(items[i].a));
            }
            string statsText = statSb.ToString();

            headerText.text =
                $"次の敵：{nextEnemyLabel}\n" +
                "\n" +
                $"環境：\n{envText}\n" +
                "\n" +
                $"{statsText}";
        }



        // 右下：自分のリソース（縦3行）
        if (playerStatsView != null)
            playerStatsView.Render(run);
    }

    public void SetRoundLog(string text)
    {
        if (roundLogText != null)
            roundLogText.text = text;
    }
}
