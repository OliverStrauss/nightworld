using NightWorld.Data;
using NightWorld.World;
using NUnit.Framework;
using UnityEngine;

namespace NightWorld.Tests
{
    public class GroundHeightTests
    {
        private const float HalfX = 40f;
        private const float HalfZ = 30f;

        [Test]
        public void EdgeBlend_FlattensEveryBorderPointToZero()
        {
            var ground = Ground(edgeBlend: 10f);
            float unblendedSum = 0f;

            for (int i = 0; i <= 40; i++)
            {
                float t = i / 20f - 1f; // -1 .. 1, corners included
                var border = new[]
                {
                    new Vector2(t * HalfX, -HalfZ),
                    new Vector2(t * HalfX, HalfZ),
                    new Vector2(-HalfX, t * HalfZ),
                    new Vector2(HalfX, t * HalfZ),
                };
                foreach (var p in border)
                {
                    Assert.AreEqual(0f, ZoneBuilder.SampleGroundHeight(ground, p.x, p.y), 0.001f, $"border point {p} isn't flat");
                    unblendedSum += Mathf.Abs(CurrentFormula(ground, p.x, p.y));
                }
            }

            Assert.Greater(unblendedSum, 0.1f, "test ground is flat at the border anyway, so it proves nothing");
        }

        [Test]
        public void EdgeBlend_LeavesTheCentreUnchanged()
        {
            var ground = Ground(edgeBlend: 10f);

            Assert.AreEqual(CurrentFormula(ground, 0f, 0f), ZoneBuilder.SampleGroundHeight(ground, 0f, 0f), 1e-6f);
            Assert.AreEqual(CurrentFormula(ground, 5f, -3f), ZoneBuilder.SampleGroundHeight(ground, 5f, -3f), 1e-6f);
        }

        [Test]
        public void NoEdgeBlend_MatchesTheUnblendedFormulaEverywhere()
        {
            var ground = Ground(edgeBlend: 0f);
            Assert.AreEqual(0f, new GroundSettings().edgeBlend, "default edgeBlend must be 0");

            for (float x = -HalfX - 2f; x <= HalfX + 2f; x += 2f)
            {
                for (float z = -HalfZ - 2f; z <= HalfZ + 2f; z += 2f)
                {
                    Assert.AreEqual(CurrentFormula(ground, x, z), ZoneBuilder.SampleGroundHeight(ground, x, z), 1e-6f, $"at ({x}, {z})");
                }
            }
        }

        private static GroundSettings Ground(float edgeBlend)
        {
            return new GroundSettings
            {
                size = new Vector2(HalfX * 2f, HalfZ * 2f),
                heightAmplitude = 3f,
                noiseScale = 0.025f,
                seed = 7,
                edgeBlend = edgeBlend,
            };
        }

        /// <summary>The height formula from before edgeBlend existed, copied so a change to it fails here.</summary>
        private static float CurrentFormula(GroundSettings ground, float x, float z)
        {
            float ox = 1000f + ground.seed * 17.13f;
            float oz = 1000f + ground.seed * 31.71f;
            float n = Mathf.PerlinNoise(x * ground.noiseScale + ox, z * ground.noiseScale + oz);
            return (n - 0.5f) * 2f * ground.heightAmplitude;
        }
    }
}
