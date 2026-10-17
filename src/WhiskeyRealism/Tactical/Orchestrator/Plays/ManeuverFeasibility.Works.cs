using System;
using WhiskeyRealism.Tactical.Battle;

namespace WhiskeyRealism.Tactical.Orchestrator.Plays
{
    // BC14W requirement-works-pricing and requirement-turn-first (spec § 7A): the evaluation's works terms. Assaulting held works takes about
    // three times the odds; a plan that turns an open end prices its strike beyond the end as open ground and its holding fix against only the
    // enemy outside works. Everything here is gated on the works picture: with no held works (or Enable Enemy Works Doctrine off) every term
    // reads exactly as the core's.
    public static partial class ManeuverFeasibility
    {
        /// <summary>
        /// Builds the evaluation's works picture once, at <see cref="FeasibilityJob.Begin"/>. An Attacker or Meeting side reads the enemy's works
        /// (the enemy bodies are built for it too, for the junctions); a Defender's own works are the station's (requirement-defender-works-reserve).
        /// </summary>
        internal static void BuildWorks(FeasibilityJob job)
        {
            job.WorksActive = false;
            job.Works.Clear();
            if (!Plugin.EnemyWorksDoctrineEnabled() || !job.FrameValid || job.Input.Stance == BattleStance.Defender) return;
            job.WorksActive = true;
            DefenderPicture bodies = job.Defender;
            if (job.Enemy.HasSamples) BuildEnemyBodies(job, bodies);
            else bodies.BodyCount = 0;
            job.Works.BuildEnemy(job.Input.Snapshot, in job.Input.Axis, bodies.BodyX, bodies.BodyZ, bodies.BodyCount, job.Input.Works, job.Input.Picture);
            job.Enemy.ClassifyOpen(job.Works);
        }
    }
}
