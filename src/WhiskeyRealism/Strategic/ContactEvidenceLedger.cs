using System;
using System.Collections.Generic;
using UnityEngine;

namespace WhiskeyRealism.Strategic
{
    public enum ContactEvidence
    {
        NoContact,
        EnemyPresent,
        EnemyReacted,
        SkirmishObserved,
        BattleObserved,
        FavorableContact,
        OvermatchedContact
    }

    internal sealed class ContactEvidenceInput
    {
        public int ObservingAllianceId;
        public Vector3 TargetPosition;
        public float CurrentEnemyStrength;
        public float CurrentFriendlyStrength;
        public float PreviousObservedEnemyStrength;
        public float EnemyReactionMultiplier;
        public float EscalateFriendlyRatio;
        public float WithdrawFriendlyRatio;
        public List<BattleHistoryRecord> BattleHistory;
        public float SpatialMaxDistance;
        public int CurrentDaySerial;
        public int WithinDays = 7;
    }

    internal sealed class ContactEvidenceOutput
    {
        public ContactEvidence Evidence;
        public bool AllowsEscalation;
        public string Reason;
    }

    internal static class ContactEvidenceLedger
    {
        internal static ContactEvidenceOutput Build(ContactEvidenceInput input)
        {
            var output = new ContactEvidenceOutput();
            if (input == null) return Reject(output, ContactEvidence.NoContact, "missing-input");

            float ratio = input.CurrentFriendlyStrength /
                          Math.Max(1f, input.CurrentEnemyStrength);

            BattleHistoryRecord majorNearby = null;
            BattleHistoryRecord minorNearby = null;
            if (input.BattleHistory != null)
            {
                foreach (var record in BattleHistoryQuery.Near(
                    input.BattleHistory,
                    input.TargetPosition,
                    input.SpatialMaxDistance,
                    input.CurrentDaySerial,
                    input.WithinDays))
                {
                    if (record.IsMajorResult) { majorNearby = record; break; }
                    if (minorNearby == null) minorNearby = record;
                }
            }

            if (ratio <= input.WithdrawFriendlyRatio)
                return Reject(output, ContactEvidence.OvermatchedContact, "ratio-overmatched");

            if (majorNearby != null && majorNearby.AllianceWon != input.ObservingAllianceId)
                return Reject(output, ContactEvidence.OvermatchedContact, "battle-lost");

            if (input.CurrentEnemyStrength <= 0f && majorNearby == null && minorNearby == null)
                return Reject(output, ContactEvidence.NoContact, "no-enemy-no-battles");

            float prior = Math.Max(1f, input.PreviousObservedEnemyStrength);
            if (input.CurrentEnemyStrength >= prior * input.EnemyReactionMultiplier &&
                ratio < input.EscalateFriendlyRatio)
                return Reject(output, ContactEvidence.EnemyReacted, "enemy-reaction");

            if (majorNearby != null && majorNearby.AllianceWon == input.ObservingAllianceId)
            {
                output.Evidence = ContactEvidence.BattleObserved;
                output.AllowsEscalation = ratio >= input.EscalateFriendlyRatio;
                output.Reason = output.AllowsEscalation ? "battle-won-favorable" : "battle-won-need-ratio";
                return output;
            }

            bool enemyPresent = input.CurrentEnemyStrength > 0f;
            if (enemyPresent && ratio >= input.EscalateFriendlyRatio)
            {
                output.Evidence = ContactEvidence.FavorableContact;
                output.AllowsEscalation = true;
                output.Reason = "favorable-presence";
                return output;
            }

            if (minorNearby != null)
            {
                output.Evidence = ContactEvidence.SkirmishObserved;
                output.AllowsEscalation = ratio >= input.EscalateFriendlyRatio;
                output.Reason = output.AllowsEscalation ? "skirmish-favorable" : "skirmish-need-ratio";
                return output;
            }

            if (enemyPresent)
            {
                output.Evidence = ContactEvidence.EnemyPresent;
                output.AllowsEscalation = false;
                output.Reason = "enemy-present-need-ratio-or-skirmish";
                return output;
            }

            return Reject(output, ContactEvidence.NoContact, "fallthrough");
        }

        private static ContactEvidenceOutput Reject(
            ContactEvidenceOutput output, ContactEvidence evidence, string reason)
        {
            output.Evidence = evidence;
            output.AllowsEscalation = false;
            output.Reason = reason;
            return output;
        }
    }
}
