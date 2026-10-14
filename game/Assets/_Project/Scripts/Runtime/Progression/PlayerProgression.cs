using System;

namespace NightWorld.Progression
{
    /// <summary>
    /// The player's level, XP and gold. Plain C# so it can be unit-tested without a scene.
    /// </summary>
    public class PlayerProgression
    {
        private readonly ProgressionSettings settings;

        public int Level { get; private set; } = 1;

        /// <summary>XP earned towards the next level (resets on level-up).</summary>
        public int Xp { get; private set; }

        public int TotalXp { get; private set; }

        public int Gold { get; private set; }

        /// <summary>Raised once per level gained, with the new level.</summary>
        public event Action<int> LeveledUp;

        /// <summary>Raised with the new gold total.</summary>
        public event Action<int> GoldChanged;

        public PlayerProgression(ProgressionSettings settings)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public bool IsMaxLevel => Level >= settings.levelCap;

        public int XpToNext => IsMaxLevel ? 0 : settings.XpToNext(Level);

        /// <summary>Adds XP and returns how many levels were gained.</summary>
        public int AddXp(int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));

            TotalXp += amount;
            if (IsMaxLevel) return 0;

            Xp += amount;
            int gained = 0;
            while (!IsMaxLevel && Xp >= settings.XpToNext(Level))
            {
                Xp -= settings.XpToNext(Level);
                Level++;
                gained++;
                LeveledUp?.Invoke(Level);
            }

            if (IsMaxLevel) Xp = 0;
            return gained;
        }

        public void AddGold(int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            Gold += amount;
            GoldChanged?.Invoke(Gold);
        }

        public bool TrySpendGold(int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (amount > Gold) return false;
            Gold -= amount;
            GoldChanged?.Invoke(Gold);
            return true;
        }
    }
}
