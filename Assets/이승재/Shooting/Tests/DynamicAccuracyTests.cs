using NUnit.Framework;
using UnityEngine;

namespace Reinforcement.Shooting.Tests
{
    [TestFixture]
    public sealed class DynamicAccuracyTests
    {
        [Test]
        public void LegacyFixedAccuracyRemainsIntactWhenToggleIsOff()
        {
            var settings = new ShootingSettings
            {
                useDynamicAccuracy = false,
                accuracy = 0.25f
            };

            Assert.That(settings.IsHitRoll(0.1d, lockDuration: 0.0d), Is.True);
            Assert.That(settings.IsHitRoll(0.249d, lockDuration: 1.5d), Is.True);
            Assert.That(settings.IsHitRoll(0.251d, lockDuration: 5.0d), Is.False);
        }

        [Test]
        public void DynamicAccuracySuppressesFireDuringReactionDelay()
        {
            var settings = new ShootingSettings
            {
                useDynamicAccuracy = true,
                reactionDelay = 0.22f,
                baseAccuracy = 0.25f,
                maxAccuracy = 0.80f,
                aimConvergence = 3.8f
            };

            // 0.22초 이전(식별 및 반사신경 시간)에는 명중률 0% 및 사격 불가 판정
            Assert.That(settings.CalculateDynamicAccuracy(0.0d), Is.EqualTo(0f));
            Assert.That(settings.CalculateDynamicAccuracy(0.15d), Is.EqualTo(0f));
            Assert.That(settings.CalculateDynamicAccuracy(0.219d), Is.EqualTo(0f));

            Assert.That(settings.IsHitRoll(0.01d, lockDuration: 0.20d), Is.False);
        }

        [Test]
        public void DynamicAccuracyStartsAtBaseAccuracyAndReachesAroundEightyPercentAtOneSecond()
        {
            var settings = new ShootingSettings
            {
                useDynamicAccuracy = true,
                reactionDelay = 0.22f,
                baseAccuracy = 0.25f,
                maxAccuracy = 0.80f,
                aimConvergence = 3.8f
            };

            // 0.22초 직후: 기본 명중률 25% (0.25)
            float accAtStart = settings.CalculateDynamicAccuracy(0.22d);
            Assert.That(accAtStart, Is.EqualTo(0.25f).Within(0.001f));

            // 1.0초 시점 (조준 유지 0.78초 경과): 약 77%~80% 도달
            float accAtOneSec = settings.CalculateDynamicAccuracy(1.0d);
            Assert.That(accAtOneSec, Is.InRange(0.77f, 0.80f));

            // 2.0초 이상 장시간 조준 시: 최대 80%에 완전 수렴
            float accLongTerm = settings.CalculateDynamicAccuracy(2.5d);
            Assert.That(accLongTerm, Is.EqualTo(0.80f).Within(0.005f));
        }

        [Test]
        public void AccuracyCurveIsConcaveShowingFastEarlyGainThenPlateau()
        {
            var settings = new ShootingSettings
            {
                useDynamicAccuracy = true,
                reactionDelay = 0.22f,
                baseAccuracy = 0.25f,
                maxAccuracy = 0.80f,
                aimConvergence = 3.8f
            };

            // 구간별 증가폭 계산
            float acc0 = settings.CalculateDynamicAccuracy(0.22d); // 0.25
            float acc1 = settings.CalculateDynamicAccuracy(0.42d); // +0.20s 경과
            float acc2 = settings.CalculateDynamicAccuracy(0.62d); // +0.40s 경과
            float acc3 = settings.CalculateDynamicAccuracy(0.82d); // +0.60s 경과

            float delta1 = acc1 - acc0; // 첫 0.2초간 증가량
            float delta2 = acc2 - acc1; // 다음 0.2초간 증가량
            float delta3 = acc3 - acc2; // 그 다음 0.2초간 증가량

            // 위로 볼록한 곡선이므로 초반 증가량이 후반 증가량보다 커야 함 (delta1 > delta2 > delta3)
            Assert.That(delta1, Is.GreaterThan(delta2), "초반 0.2초 증가폭이 중반 증가폭보다 커야 함");
            Assert.That(delta2, Is.GreaterThan(delta3), "중반 증가폭이 후반 증가폭보다 커야 함");
        }
    }
}
