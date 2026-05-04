using System;
using UnityEngine;

namespace WhiskeyRealism.Strategic
{
    internal static class CampaignMapTheaterRuntime
    {
        internal static Theater FromPosition(Vector3 position)
        {
            int stateId = TryStateId(position);
            var ledger = StrategicCoordinator.Instance?.CampaignMap;
            if (ledger != null && ledger.TryGetStateTheater(stateId, out var theater))
                return theater;

            string stateName = TryStateName(stateId);
            theater = TheaterClassifier.FromStateName(stateName);
            if (theater != Theater.Unknown) return theater;

            return TheaterClassifier.FromPosition(position.x, position.z);
        }

        private static int TryStateId(Vector3 position)
        {
            try
            {
                return BattlefieldSetup.GetStateOfField(position, usecloseststate: true);
            }
            catch
            {
                return -1;
            }
        }

        private static string TryStateName(int state)
        {
            try
            {
                if (GameVars.nation == null || state < 0 || state >= GameVars.nation.Length)
                    return null;
                return GameVars.nation[state]?.name;
            }
            catch { return null; }
        }
    }
}
