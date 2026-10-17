using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Orchestrator;

// BC14 task 3: the BC13 detector's isolated-cluster openings over random observation sets, as a digest. The digest is the
// parity oracle of the clustering extraction (CLUSTER-PARITY): the golden value was taken from the inline union-find that
// OpeningDetector.ClusterPass carried before it called SingleLinkClusters.Label.
internal static partial class CommanderOpportunityTests
{
    // The sets: four centres with a spread of 120 m plus uniform noise, all four observation states, unit types -1 to 3 and
    // reinforcement columns, so the sample filter and the clustering are both exercised.
    internal static string IsolatedClusterDigest(int sets)
    {
        var all = new StringBuilder();
        int clusterOpenings = 0;
        uint state = 0x9E3779B9u;
        float Next()
        {
            state ^= state << 13; state ^= state >> 17; state ^= state << 5;
            return (state & 0xFFFFFFu) / (float)0x1000000;
        }
        for (int set = 0; set < sets; set++)
        {
            var fx = new DetFixture();
            int n = 6 + (int)(Next() * 34);
            float[] cx = { -900f + Next() * 300f, -250f + Next() * 300f, 350f + Next() * 300f, 900f + Next() * 300f };
            for (int i = 0; i < n; i++)
            {
                float x, z;
                if (Next() < 0.8f)
                {
                    int c = (int)(Next() * 4f) % 4;
                    x = cx[c] + (Next() - 0.5f) * 240f;
                    z = 500f + c * 120f + (Next() - 0.5f) * 240f;
                }
                else
                {
                    x = -1500f + Next() * 3000f;
                    z = 300f + Next() * 1200f;
                }
                float strength = 50f + Next() * 850f;
                float morale = 0.2f + Next() * 0.8f;
                int unitTyp = (int)(Next() * 6f) - 1;   // -1 .. 4
                bool visible = Next() < 0.7f;
                int id;
                if (visible)
                {
                    id = fx.Inf(x, z, strength: strength, morale: morale);
                    int at = fx.Obs.Count - 1;
                    EnemyObservation o = fx.Obs[at];
                    o.UnitTyp = unitTyp;
                    fx.Obs[at] = o;
                }
                else
                {
                    id = fx.Remembered(unitTyp, x, z, Next(), morale, strength);
                }
                if (Next() < 0.1f)
                {
                    int at = fx.Obs.Count - 1;
                    EnemyObservation o = fx.Obs[at];
                    o.IsReinforcement = true;
                    fx.Obs[at] = o;
                }
            }
            var d = new OpeningDetector();
            Detect(d, fx);
            all.Append("set").Append(set).Append(':');
            foreach (Opening o in d.Openings.ToArray())
            {
                if (o.Kind != OpeningKind.IsolatedCluster) continue;
                clusterOpenings++;
                all.Append(Fmt(o.AimX)).Append(',').Append(Fmt(o.AimZ)).Append(',').Append(Fmt(o.ClusterValue)).Append(',')
                   .Append(Fmt(o.ClusterNearestMeters)).Append(',').Append(Fmt(o.Confidence01)).Append(',')
                   .Append(o.Shaken ? 's' : 'n').Append(',').Append(o.TargetObservation).Append(';');
            }
            all.Append('\n');
        }
        using (SHA256 sha = SHA256.Create())
        {
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(all.ToString()));
            var sb = new StringBuilder();
            foreach (byte b in hash) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString() + "|sets=" + sets + "|clusterOpenings=" + clusterOpenings;
        }
    }

    private static string Fmt(float v) => v.ToString("0.000", CultureInfo.InvariantCulture);
}
