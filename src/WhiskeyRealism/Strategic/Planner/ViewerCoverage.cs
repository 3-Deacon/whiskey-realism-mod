namespace WhiskeyRealism.Strategic.Planner
{
    // Every non-player alliance paints the shared fows[1] grid (CreateFOWObjects 116513-116516),
    // so a European unit can reveal a cell to the non-player AI side. An enemy therefore counts as
    // seen on fows[1] only when an own or allied viewer has it inside that viewer's own campaign
    // sight radius (spec C14; the radius is SightRadiusFormula's, the one that paints the grid).
    public static class ViewerCoverage
    {
        public static bool Covers(float enemyX, float enemyZ, float[] viewerX, float[] viewerZ, float[] viewerRadius, int count)
        {
            if (viewerX == null || viewerZ == null || viewerRadius == null) return false;
            int n = count;
            if (n > viewerX.Length) n = viewerX.Length;
            if (n > viewerZ.Length) n = viewerZ.Length;
            if (n > viewerRadius.Length) n = viewerRadius.Length;
            for (int i = 0; i < n; i++)
            {
                float r = viewerRadius[i];
                if (r <= 0f) continue;
                float dx = enemyX - viewerX[i];
                float dz = enemyZ - viewerZ[i];
                if (dx * dx + dz * dz <= r * r) return true;
            }
            return false;
        }
    }
}
