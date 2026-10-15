namespace WhiskeyRealism.Tactical
{
    public static class TacticalArtilleryInputAdapter
    {
        // Remaining artillery ammunition, 0..1. Regiment.ammo (111498) holds one
        // 0..1 fill level per slot (solid 0, explosive 1, canister 2); vanilla
        // reads a battery's supply as the mean of its slots
        // (GetROF: Tools.SumUp(ammo) / 3, 21038). Non-finite slots are skipped;
        // an unreadable array counts as full so missing data never makes a
        // battery cease fire.
        public static float ArtilleryAmmoRatio01(float[] ammo)
        {
            if (ammo == null) return 1f;
            float total = 0f;
            int count = 0;
            for (int i = 0; i < ammo.Length; i++)
            {
                float slot = ammo[i];
                if (float.IsNaN(slot) || float.IsInfinity(slot)) continue;
                total += slot < 0f ? 0f : slot;
                count++;
            }
            if (count == 0) return 1f;
            float ratio = total / count;
            return ratio > 1f ? 1f : ratio;
        }
    }
}
