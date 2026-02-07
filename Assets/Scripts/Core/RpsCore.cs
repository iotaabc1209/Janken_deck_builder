// Assets/Scripts/Core/RpsCore.cs
using System;
using System.Collections.Generic;

namespace RpsBuild.Core
{
    // =========================
    // Domain
    // =========================

    public enum RpsColor { Gu = 0, Choki = 1, Pa = 2 }
    public enum RpsOutcome { Win = 0, Lose = 1, Tie = 2 }

    [Serializable]
    public struct DeckProfile
    {
        public int Gu;
        public int Choki;
        public int Pa;

        public int Total => Gu + Choki + Pa;

        public DeckProfile(int gu, int choki, int pa)
        {
            Gu = gu; Choki = choki; Pa = pa;
        }

        public int Get(RpsColor c) => c switch
        {
            RpsColor.Gu => Gu,
            RpsColor.Choki => Choki,
            RpsColor.Pa => Pa,
            _ => 0
        };

        public bool Has(RpsColor c) => Get(c) > 0;

        public void Set(RpsColor c, int value)
        {
            value = Math.Max(0, value);
            switch (c)
            {
                case RpsColor.Gu: Gu = value; break;
                case RpsColor.Choki: Choki = value; break;
                case RpsColor.Pa: Pa = value; break;
            }
        }
    }

    public interface IRng
    {
        int Range(int minInclusive, int maxExclusive);
        float Value01();
    }

    // =========================
    // Deck (batch draw)
    // =========================

    /// <summary>
    /// 「内訳」から「1ラウンド分のN枚」を引く。テンポ仕様の要。
    /// 1手目だけ確定色を入れられる（任意ラウンドで権利を使う）。
    /// </summary>
    public sealed class Deck
    {
        private readonly DeckProfile _profile;

        public Deck(DeckProfile profile)
        {
            if (profile.Total != 30) throw new ArgumentException("DeckProfile total must be 30.");
            _profile = profile;
        }

        public DeckProfile Profile => _profile;

        public List<RpsColor> DrawBatch(int handCount, IRng rng, RpsColor? forcedFirst = null)
        {
            if (handCount <= 0) throw new ArgumentOutOfRangeException(nameof(handCount));

            var pool = BuildPool(); // 30枚のリスト
            Shuffle(pool, rng);

            // 1手目だけ確定
            if (forcedFirst.HasValue)
            {
                var c = forcedFirst.Value;
                // デッキにその色が入っていないなら確定できない（権利UI側で弾く想定だが安全策）
                if (_profile.Has(c))
                {
                    int idx = pool.FindIndex(x => x == c);
                    if (idx >= 0)
                    {
                        // idxのカードを先頭に持ってくる
                        var temp = pool[0];
                        pool[0] = pool[idx];
                        pool[idx] = temp;
                    }
                }
            }

            // 7枚など必要枚数だけ切り出し（山札は毎ラウンド「内訳から再生成」＝テンポ優先）
            var result = new List<RpsColor>(handCount);
            for (int i = 0; i < handCount; i++)
                result.Add(pool[i]);

            return result;
        }

        private List<RpsColor> BuildPool()
        {
            var pool = new List<RpsColor>(30);
            for (int i = 0; i < _profile.Gu; i++) pool.Add(RpsColor.Gu);
            for (int i = 0; i < _profile.Choki; i++) pool.Add(RpsColor.Choki);
            for (int i = 0; i < _profile.Pa; i++) pool.Add(RpsColor.Pa);
            if (pool.Count != 30) throw new InvalidOperationException("Pool must be 30.");
            return pool;
        }

        private static void Shuffle(List<RpsColor> list, IRng rng)
        {
            // Fisher-Yates
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }

    // =========================
    // Gauge (per color)
    // =========================

    /// <summary>
    /// 色別ゲージ。最大到達した色は「確定ドロー権あり」。
    /// 任意ラウンドで使用可能。使うと0に戻る。
    /// </summary>
    public sealed class GaugeState
    {
        private readonly Dictionary<RpsColor, float> _g = new()
        {
            { RpsColor.Gu, 0f },
            { RpsColor.Choki, 0f },
            { RpsColor.Pa, 0f },
        };

        public float Max { get; private set; } = 1f;

        public void SetMax(float max)
        {
            Max = Math.Max(0.0001f, max);
            // clamp
            foreach (var c in new[] { RpsColor.Gu, RpsColor.Choki, RpsColor.Pa })
                _g[c] = Math.Min(_g[c], Max);
        }

        public float Get(RpsColor c) => _g[c];

        public bool IsCharged(RpsColor c) => _g[c] >= Max - 1e-6f;

        public void Add(RpsColor c, float amount)
        {
            if (amount <= 0f) return;
            _g[c] = Math.Min(Max, _g[c] + amount);
        }

        public bool TryConsumeCharged(RpsColor c)
        {
            if (!IsCharged(c)) return false;
            _g[c] = 0f;
            return true;
        }
    }

    /// <summary>
    /// ゲージ増加量の式は調整弁なので Strategy で差し替え可能に。
    /// </summary>
    public interface IGaugeGainFormula
    {
        float CalcGain(RpsColor missingColor, DeckProfile playerProfile);
    }

    /// <summary>
    /// 既定：枚数/30（ただし係数と除数は後でConfig化しやすいように持たせる）
    /// </summary>
    public sealed class LinearGaugeGainFormula : IGaugeGainFormula
    {
        public float NumeratorScale = 1f;  // 調整弁：係数
        public float Denominator = 30f;    // 調整弁：除数（基本30）

        public float CalcGain(RpsColor missingColor, DeckProfile playerProfile)
        {
            float count = playerProfile.Get(missingColor);
            if (count <= 0) return 0f; // そもそも入ってない色は欠損対象外
            return (count / Denominator) * NumeratorScale;
        }
    }

    // =========================
    // Round simulation
    // =========================

    public sealed class RoundResult
    {
        public readonly int HandCount;
        public readonly List<RpsColor> PlayerHands;
        public readonly List<RpsColor> EnemyHands;
        public readonly List<RpsOutcome> Outcomes;
        public readonly int LossCount;
        public readonly bool IsClear;

        // 欠損（このラウンドで1度も引けなかった対象色）
        public readonly List<RpsColor> MissingColors;

        public RoundResult(
            int handCount,
            List<RpsColor> playerHands,
            List<RpsColor> enemyHands,
            List<RpsOutcome> outcomes,
            int lossCount,
            bool isClear,
            List<RpsColor> missingColors)
        {
            HandCount = handCount;
            PlayerHands = playerHands;
            EnemyHands = enemyHands;
            Outcomes = outcomes;
            LossCount = lossCount;
            IsClear = isClear;
            MissingColors = missingColors;
        }
    }

    public static class RoundSimulator
    {
        public static RoundResult Simulate(
            Deck playerDeck,
            Deck enemyDeck,
            int handCount,
            int loseThresholdExclusive, // 例：3回以上で失敗なら threshold=3（LossCount>=3 が失敗）
            IRng rng,
            RpsColor? forcedFirstColorForPlayer = null)
        {
            var p = playerDeck.DrawBatch(handCount, rng, forcedFirstColorForPlayer);
            var e = enemyDeck.DrawBatch(handCount, rng, forcedFirst: null);

            var outcomes = new List<RpsOutcome>(handCount);
            int losses = 0;

            // 欠損判定のための出現トラッキング
            var seen = new HashSet<RpsColor>();
            for (int i = 0; i < handCount; i++)
            {
                seen.Add(p[i]);

                var o = Judge(p[i], e[i]);
                outcomes.Add(o);
                if (o == RpsOutcome.Lose) losses++;
            }

            bool clear = losses < loseThresholdExclusive;

            // 欠損対象＝デッキに入っている色のみ
            var missing = new List<RpsColor>(3);
            foreach (var c in new[] { RpsColor.Gu, RpsColor.Choki, RpsColor.Pa })
            {
                if (playerDeck.Profile.Has(c) && !seen.Contains(c))
                    missing.Add(c);
            }

            return new RoundResult(handCount, p, e, outcomes, losses, clear, missing);
        }

        public static RpsOutcome Judge(RpsColor player, RpsColor enemy)
        {
            if (player == enemy) return RpsOutcome.Tie;

            // Gu beats Choki, Choki beats Pa, Pa beats Gu
            return (player, enemy) switch
            {
                (RpsColor.Gu, RpsColor.Choki) => RpsOutcome.Win,
                (RpsColor.Choki, RpsColor.Pa) => RpsOutcome.Win,
                (RpsColor.Pa, RpsColor.Gu) => RpsOutcome.Win,
                _ => RpsOutcome.Lose
            };
        }
    }
}
