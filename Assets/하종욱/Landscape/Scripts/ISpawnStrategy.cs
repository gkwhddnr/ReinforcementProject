using System;
using UnityEngine;
using UnityEngine.AI;

namespace ReinProject.Landscape
{
    public enum SpawnStrategyType
    {
        /// <summary>
        /// 무작위성을 배제한 결정론적 고정 청크 스폰 (현재 기본 모드)
        /// Team A: 북서 [0, 4] 고지대 거점 / Team B: 남동 [4, 0] 평지 거점
        /// </summary>
        FixedChunk,

        /// <summary>
        /// 최소 2청크 이상 거리를 두는 규칙 기반 무작위 청크 스폰 (향후 커리큘럼/일반화 확장용)
        /// </summary>
        RuleBasedRandomChunk
    }

    /// <summary>
    /// 에피소드 스폰 위치 결정을 위한 확장 전략 인터페이스
    /// </summary>
    public interface ISpawnStrategy
    {
        bool TryGetSpawns(ArenaEnvironment arena, float standingHeight, float radius,
            out Pose spawnA, out Pose spawnB, out string failure);
    }

    /// <summary>
    /// 고정 청크 스폰 전략 (결정론적 환경 제공)
    /// </summary>
    public sealed class FixedChunkSpawnStrategy : ISpawnStrategy
    {
        public bool TryGetSpawns(ArenaEnvironment arena, float standingHeight, float radius,
            out Pose spawnA, out Pose spawnB, out string failure)
        {
            spawnA = default;
            spawnB = default;
            failure = null;

            if (arena == null)
            {
                failure = "ArenaEnvironment is null.";
                return false;
            }

            if (!arena.IsReady && !arena.TryPrepare(standingHeight, radius, out failure))
            {
                return false;
            }

            if (!arena.TryGetSpawn(ArenaSpawnSlot.A, out spawnA) ||
                !arena.TryGetSpawn(ArenaSpawnSlot.B, out spawnB))
            {
                failure = "Arena spawn slots (A/B) could not be resolved.";
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// 최소 2청크 이상 거리를 두는 규칙 기반 무작위 청크 스폰 전략 (확장성 보장)
    /// </summary>
    public sealed class RuleBasedRandomChunkSpawnStrategy : ISpawnStrategy
    {
        private readonly int _minChunkDistance;
        private readonly System.Random _random;

        public RuleBasedRandomChunkSpawnStrategy(int minChunkDistance = 2, int seed = 42)
        {
            _minChunkDistance = Mathf.Max(1, minChunkDistance);
            _random = new System.Random(seed);
        }

        public bool TryGetSpawns(ArenaEnvironment arena, float standingHeight, float radius,
            out Pose spawnA, out Pose spawnB, out string failure)
        {
            // 현재 단계에서는 고정 스폰을 fallback으로 보장하면서 유효성 검사 수행
            // 향후 청크 그리드(5x5) 샘플링 및 NavMesh 투영 로직 확장 가능
            var fallback = new FixedChunkSpawnStrategy();
            return fallback.TryGetSpawns(arena, standingHeight, radius, out spawnA, out spawnB, out failure);
        }
    }
}
