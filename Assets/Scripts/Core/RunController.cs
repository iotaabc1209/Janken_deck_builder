// Assets/Scripts/Core/RunController.cs
using System;
using System.Collections.Generic;
using UnityEngine;

namespace RpsBuild.Core
{
    [Serializable]
    public struct Tuning
    {
        public int HandCount;               // 例：7
        public int LoseThresholdExclusive;  // 例：3（3以上で失敗）
        public int MaxMiss;                 // 例：N回で終了
        public int InitialPoints;      // 初期ポイント（例：10）
        public int PointsPerClear;     // クリア報酬（例：1）
        public float GaugeBuyAmount;   // 1ptでゲージ+（例：0.1）
        public float GaugeMax;              // 例：1
        public bool UniqueMainAcrossArchetypes; // Heavy/TwinTop/Balance(看板色) の主軸色を重複禁止にする
        public bool ShuffleEnvWeightsAcrossArchetypes; // Run開始時に heavy/balance/twinTop の重み割当をシャッフル
        public ArchetypeWeights EnvWeights; // 例：Heavy60%...


        // 将来ここに「デッキ調整幅(+2/-2)」なども集約
    }

    public sealed class RunState
    {
        public int RoundIndex { get; private set; } = 0;
        public int MissCount { get; private set; } = 0;

        public int Points { get; private set; }
        public int Score { get; private set; } = 0;

        public float LastGaugeGainGu { get; private set; } = 0f;
        public float LastGaugeGainChoki { get; private set; } = 0f;
        public float LastGaugeGainPa { get; private set; } = 0f;

        public EnvironmentState Environment { get; private set; }
        public DeckProfile PlayerProfile { get; private set; }

        public GaugeState Gauge { get; } = new GaugeState();

        // 任意ラウンドで使える「確定ドローの予約」
        public RpsColor? ReservedForcedFirstColor { get; private set; } = null;

        public Tuning Tuning => _tuning;
        public RpsColor? ReservedForcedFirst => ReservedForcedFirstColor;

        public bool IsGameOver => MissCount >= _tuning.MaxMiss;

        public bool LastRoundWasIntro { get; private set; } = false;
        public bool LastRoundIntroForcedClear { get; private set; } = false;

        private readonly Tuning _tuning;
        private readonly IRng _rng;
        private readonly ArchetypeDeckGenerator _enemyGen;
        private readonly IGaugeGainFormula _gainFormula;

        // 最新の敵（表示用）
        public EnemyArchetype LastEnemyArchetype { get; private set; }
        public DeckProfile LastEnemyProfile { get; private set; }

        // 次ラウンド用の敵プレビュー（UIに出してOK）
        public EnemyArchetype PreviewEnemyArchetype { get; private set; }
        // 内部用：次ラウンドで実際に使う敵デッキ
        private DeckProfile _previewEnemyDeck;

        // ---- Run-fixed enemy profiles (per archetype) ----
        private readonly Dictionary<EnemyArchetype, DeckProfile> _enemyProfilesByArchetype = new();
        private readonly Dictionary<EnemyArchetype, RpsColor> _enemyMainColorByArchetype = new();
        private readonly Dictionary<EnemyArchetype, RpsColor> _enemySecondColorByArchetype = new(); // TwinTop用（表示/将来用）

        public RpsColor PreviewEnemyMainColor { get; private set; } = RpsColor.Gu;
        public RpsColor LastEnemyMainColor { get; private set; } = RpsColor.Gu;


        // ---- Archetype Stats (Core) ----

        public struct ArchetypeHandStat
        {
            public int N;
            public int Gu;
            public int Choki;
            public int Pa;

            public void AddHands(IReadOnlyList<RpsColor> hands)
            {
                N++;
                for (int i = 0; i < hands.Count; i++)
                {
                    switch (hands[i])
                    {
                        case RpsColor.Gu: Gu++; break;
                        case RpsColor.Choki: Choki++; break;
                        case RpsColor.Pa: Pa++; break;
                    }
                }
            }
        }

        private readonly Dictionary<EnemyArchetype, ArchetypeHandStat> _archetypeStats = new();



        public RunState(
            Tuning tuning,
            DeckProfile initialPlayer,
            IRng rng,
            ArchetypeDeckGenerator enemyGen,
            IGaugeGainFormula gainFormula)
        {
            _tuning = tuning;
            _rng = rng;
            _enemyGen = enemyGen;
            _gainFormula = gainFormula;

            if (initialPlayer.Total != 30) throw new ArgumentException("Player deck must total 30.");
            PlayerProfile = initialPlayer;

            var envWeights = _tuning.EnvWeights;

            if (_tuning.ShuffleEnvWeightsAcrossArchetypes)
                envWeights = EnvironmentGenerator.ShuffleWeightsAcrossArchetypes(envWeights, _rng);

            Environment = EnvironmentGenerator.CreateDefault(envWeights);

            Gauge.SetMax(_tuning.GaugeMax);

            Points = _tuning.InitialPoints;

            BuildRunFixedEnemyProfiles();
            GenerateEnemyPreview();

        }

        /// <summary>
        /// UIから呼ぶ：確定ドロー権を「予約」する。
        /// 予約は次のラウンド1手目にだけ使われ、実行時にゲージが0になる。
        /// </summary>
        public bool TryReserveForcedFirst(RpsColor color)
        {
            if (!Gauge.IsCharged(color)) return false;
            if (!PlayerProfile.Has(color)) return false; // デッキに入ってない色は確定できない
            ReservedForcedFirstColor = color;
            return true;
        }

        /// <summary>
        /// ラウンドを1回進める（7枚自動ドロー→判定→ミス更新→欠損勝利→ゲージ加算）。
        /// </summary>
        public RoundResult PlayNextRound()
        {
            if (IsGameOver) throw new InvalidOperationException("Game is over.");

            // ★次ラウンド用プレビューを「消費」して今回の敵にする
            var archetype = PreviewEnemyArchetype;
            var enemyProfile = _previewEnemyDeck;

            LastEnemyArchetype = archetype;
            LastEnemyProfile = enemyProfile;
            LastEnemyMainColor = PreviewEnemyMainColor;

            var playerDeck = new Deck(PlayerProfile);
            var enemyDeck = new Deck(enemyProfile);
;

            // 予約された確定ドロー色（あれば）を適用
            RpsColor? forced = ReservedForcedFirstColor;

            // 実際の消費は「ラウンド開始で使用した」とみなし、ここで確定させる
            if (forced.HasValue)
            {
                // ゲージを消費して0に戻す
                // (予約だけして未使用のままにするケースは基本ない想定。必要ならキャンセルUIも作れる)
                Gauge.TryConsumeCharged(forced.Value);
                ReservedForcedFirstColor = null;
            }

            // ---- Intro handling (Round 0): force CLEAR by rerolling (bounded) ----
            bool isIntro = (RoundIndex == 0);
            LastRoundWasIntro = isIntro;
            LastRoundIntroForcedClear = false;

            RoundResult result;

            if (!isIntro)
            {
                result = RoundSimulator.Simulate(
                    playerDeck,
                    enemyDeck,
                    _tuning.HandCount,
                    _tuning.LoseThresholdExclusive,
                    _rng,
                    forcedFirstColorForPlayer: forced
                );
            }
            else
            {
                const int MaxIntroTries = 30;
                RoundResult last = default;

                for (int t = 0; t < MaxIntroTries; t++)
                {
                    // ★毎回デッキを作り直す（引き直し）
                    var pDeck = new Deck(PlayerProfile);
                    var eDeck = new Deck(enemyProfile);

                    last = RoundSimulator.Simulate(
                        pDeck,
                        eDeck,
                        _tuning.HandCount,
                        _tuning.LoseThresholdExclusive,
                        _rng,
                        forcedFirstColorForPlayer: forced
                    );

                    if (last.IsClear)
                    {
                        if (t > 0) LastRoundIntroForcedClear = true;
                        result = last;
                        goto INTRO_DONE;
                    }
                }

                // 上限回しても勝てなかったら最後の結果を採用（ここに来るのはほぼ無いはず）
                result = last;

            INTRO_DONE: ;
            }

            // ---- stats update: "enemy actual hands" cumulative ----
            if (!_archetypeStats.TryGetValue(LastEnemyArchetype, out var st))
                st = default;

            st.AddHands(result.EnemyHands);
            _archetypeStats[LastEnemyArchetype] = st;



            // クリア失敗ならミス増加
            if (!result.IsClear) MissCount++;

            // クリア成功＆欠損あり → 欠損色ごとにゲージ増加
            // // クリア成功ならポイント報酬
            if (result.IsClear)
            {
                Points += _tuning.PointsPerClear;
                Score += 1;
            }

            LastGaugeGainGu = 0f;
            LastGaugeGainChoki = 0f;
            LastGaugeGainPa = 0f;


            if (result.IsClear && result.MissingColors.Count > 0)
            {
                foreach (var missing in result.MissingColors)
                {
                    float gain = _gainFormula.CalcGain(missing, PlayerProfile);
                    Gauge.Add(missing, gain);

                    switch (missing)
                    {
                        case RpsColor.Gu:    LastGaugeGainGu += gain; break;
                        case RpsColor.Choki: LastGaugeGainChoki += gain; break;
                        case RpsColor.Pa:    LastGaugeGainPa += gain; break;
                    }

                }
            }

            RoundIndex++;

            // 次ラウンド用プレビューを作っておく（Resolve/Adjustで表示するため）
            GenerateEnemyPreview();

            return result;
        }

        /// <summary>
        /// ラウンド間のデッキ調整（最小限の雛形）。
        /// 例：+2/-2の具体UIはここをラップする。
        /// </summary>
        public void SetPlayerProfile(DeckProfile profile)
        {
            if (profile.Total != 30) throw new ArgumentException("Player deck must total 30.");
            PlayerProfile = profile;
        }

        private void GenerateEnemyPreview()
        {
            PreviewEnemyArchetype = EnvironmentGenerator.RollArchetype(Environment, _rng);

            // 保険：辞書に無ければ作り直す（初期化漏れ/順序ミスでも落ちない）
            if (!_enemyProfilesByArchetype.TryGetValue(PreviewEnemyArchetype, out var deck))
            {
                BuildRunFixedEnemyProfiles();
                deck = _enemyProfilesByArchetype[PreviewEnemyArchetype];
            }

            _previewEnemyDeck = deck;

            if (!_enemyMainColorByArchetype.TryGetValue(PreviewEnemyArchetype, out var main))
                main = RpsColor.Gu;

            PreviewEnemyMainColor = main;
        }

        public bool TryGetEnemyMainColor(EnemyArchetype archetype, out RpsColor mainColor)
        {
            return _enemyMainColorByArchetype.TryGetValue(archetype, out mainColor);
        }




        private bool TrySpendPoints(int cost)
        {
            if (cost <= 0) return true;
            if (Points < cost) return false;
            Points -= cost;
            return true;
        }

        /// <summary>
        /// 旧：即時にデッキを確定変更する（デバッグ/プロト用）
        /// 本番UIは AdjustPanelView のドラフト→TryCommitDeckProfileByPoints を使用
        /// </summary>
        public bool TryAdjustDeckByPoint(RpsColor addColor, RpsColor subColor, int amount = 1)
        {
            if (amount <= 0) return true;
            if (Points < amount) return false;

            var cur = PlayerProfile;

            // 1pt=1枚なので add/sub amount は同じ
            bool ok = DeckAdjust.TryAdjust(ref cur,
                addColor: addColor, addAmount: amount,
                subColor: subColor, subAmount: amount);

            if (!ok) return false;

            // ここで消費確定
            Points -= amount;
            PlayerProfile = cur;
            return true;
        }

        public bool TryCommitDeckProfileByPoints(DeckProfile profile, int cost)
        {
            if (profile.Total != 30) return false;
            if (cost < 0) return false;
            if (!TrySpendPoints(cost)) return false;

            PlayerProfile = profile;
            return true;
        }


        /// <summary>
        /// 1ptでゲージを +GaugeBuyAmount（任意色）
        /// — amount回買える（=ポイントamount消費）
        /// </summary>
        public bool TryBuyGaugeByPoint(RpsColor color, int amount = 1)
        {
            Debug.Log($"[BuyGauge] color={color}, amount={amount}, points(before)={Points}");

            if (amount <= 0) return true;
            if (!TrySpendPoints(amount))
            {
                Debug.Log("[BuyGauge] failed: not enough points");
                return false;
            }

            float add = _tuning.GaugeBuyAmount * amount;
            Debug.Log($"[BuyGauge] gauge add = {add}");

            Gauge.Add(color, add);

            Debug.Log($"[BuyGauge] points(after)={Points}, gauge={Gauge.Get(color)}");
            return true;
        }



        /// <summary>
        /// 予約を解除（ユーザに優しく：やっぱやめる）
        /// — ゲージは消費しない
        /// </summary>
        public bool TryCancelReservedForcedFirst()
        {
            if (!ReservedForcedFirstColor.HasValue) return false;
            ReservedForcedFirstColor = null;
            return true;
        }

        public ArchetypeHandStat GetArchetypeHandStat(EnemyArchetype archetype)
        {
            if (!_archetypeStats.TryGetValue(archetype, out var st))
            {
                st = default;
                _archetypeStats[archetype] = st;
            }
            return st;
        }


        private void BuildRunFixedEnemyProfiles()
        {
            _enemyProfilesByArchetype.Clear();
            _enemyMainColorByArchetype.Clear();
            _enemySecondColorByArchetype.Clear();

            // 主軸色を決める（UniqueMainAcrossArchetypes なら被りなし）
            // HeavyMain, BalanceBanner, TwinTopMain を割り当てる
            var colors = new List<RpsColor> { RpsColor.Gu, RpsColor.Choki, RpsColor.Pa };

            RpsColor PickAny() => colors[_rng.Range(0, colors.Count)];

            RpsColor heavyMain;
            RpsColor balanceBanner;
            RpsColor twinMain;

            if (_tuning.UniqueMainAcrossArchetypes)
            {
                // 3色をシャッフルして割当
                for (int i = colors.Count - 1; i > 0; i--)
                {
                    int j = _rng.Range(0, i + 1);
                    (colors[i], colors[j]) = (colors[j], colors[i]);
                }
                heavyMain = colors[0];
                balanceBanner = colors[1];
                twinMain = colors[2];
            }
            else
            {
                heavyMain = PickAny();
                balanceBanner = PickAny();
                twinMain = PickAny();
            }

            // TwinTopの secondColor は main以外の2色から選ぶ（必要ならUniqueを強める拡張点）
            var twinOthers = new List<RpsColor> { RpsColor.Gu, RpsColor.Choki, RpsColor.Pa };
            twinOthers.Remove(twinMain);
            var twinSecond = twinOthers[_rng.Range(0, twinOthers.Count)];

            // ---- Heavy ----
            _enemyMainColorByArchetype[EnemyArchetype.Heavy] = heavyMain;
            _enemyProfilesByArchetype[EnemyArchetype.Heavy] = _enemyGen.GenerateHeavyFixed(_rng, heavyMain);

            // ---- Balance ---- (bannerColorは表示用。生成にも渡して割当を安定させる)
            _enemyMainColorByArchetype[EnemyArchetype.Balance] = balanceBanner;
            _enemyProfilesByArchetype[EnemyArchetype.Balance] = _enemyGen.GenerateBalanceFixed(_rng, balanceBanner);

            // ---- TwinTop ----
            _enemyMainColorByArchetype[EnemyArchetype.TwinTop] = twinMain;
            _enemySecondColorByArchetype[EnemyArchetype.TwinTop] = twinSecond;
            _enemyProfilesByArchetype[EnemyArchetype.TwinTop] = _enemyGen.GenerateTwinTopFixed(_rng, twinMain, twinSecond);
        }



    }
}
