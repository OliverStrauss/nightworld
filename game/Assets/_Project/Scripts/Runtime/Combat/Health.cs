using System;
using UnityEngine;

namespace NightWorld.Combat
{
    public class Health : MonoBehaviour
    {
        public int Max { get; private set; }
        public int Current { get; private set; }
        public bool IsDead => Current <= 0;

        /// <summary>Raised after damage is applied, with the amount.</summary>
        public event Action<Health, int> Damaged;

        /// <summary>Raised once, when Current reaches 0.</summary>
        public event Action<Health> Died;

        public void Init(int max)
        {
            Max = Mathf.Max(1, max);
            Current = Max;
        }

        public void TakeDamage(int amount)
        {
            if (IsDead || amount <= 0) return;

            Current = Mathf.Max(0, Current - amount);
            Damaged?.Invoke(this, amount);
            if (IsDead) Died?.Invoke(this);
        }

        public void Heal(int amount)
        {
            if (IsDead || amount <= 0) return;
            Current = Mathf.Min(Max, Current + amount);
        }
    }
}
