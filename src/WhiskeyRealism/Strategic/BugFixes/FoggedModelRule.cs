using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.BugFixes
{
    // BUG-LOG-002. Regiment.UpdateCampsAndEntrenchments (decompile 115392-115470) places
    // a camp only when !iswithinfog (115408), destroys camp and entrenchment when
    // iswithinfog (115426) and places the entrenchment model only when !iswithinfog
    // (115455). For a unit outside the player's alliance iswithinfog means "hidden from
    // the player" (CheckRegimentVisibility 127209-127229), so AI armies the player cannot
    // see never encamp, never take winter quarters and never entrench. Vanilla's fog gate
    // keeps the camp model from revealing the unit; the fix runs the camp logic as if
    // the unit were visible and hides the camp and entrenchment models instead.
    //
    // BUG-LOG-002a. Regiment.CheckModelReplacement (116372-116503) is the same fog gate on
    // embarkation: the river boat, sea ship and train model, and currentmodeltype with it,
    // are created only when !iswithinfog (116383, 116399, 116446) and removed when fogged
    // (116461). A hidden AI land unit is therefore never embarked: no rail speed (133401)
    // or rail terrain cost (132891), it halts at night like marching troops (133958), uses
    // no transport capacity (116471-116501) or locomotives/ships (133900-133915), skips
    // the sea embarkation delay (SetEmbarkmentMode, 116418), eats full rations instead of
    // the transport factor (114186) and keeps receiving reinforcements (114501). The fix
    // runs the model logic as if the unit were visible and hides the model instead.
    internal static class FoggedModelRule
    {
        // Run vanilla's camp logic with iswithinfog cleared for this unit. A unit on a
        // river or the sea is left alone: when visible it shows a boat model there and
        // vanilla gives it no camp (115408 shownmodel == null; CheckModelReplacement
        // 116383-116399 skips the model only because the unit is fogged).
        internal static bool ShouldTreatAsVisibleForCamp(
            bool fixEnabled,
            bool calledFromCampaign,
            bool useFogOfWar,
            bool isPlayerAlliance,
            bool isWithinFog,
            bool onWater)
        {
            return fixEnabled && calledFromCampaign && useFogOfWar && !isPlayerAlliance && isWithinFog && !onWater;
        }

        // Run vanilla's embarkation (model) logic with iswithinfog cleared for this unit.
        // Fleets (unittyp 17) are left to the naval stage: vanilla never removes a fleet's
        // model in fog (116461 excludes them) and fleets have their own visibility rules.
        internal static bool ShouldTreatAsVisibleForEmbarkation(
            bool fixEnabled,
            bool calledFromCampaign,
            bool useFogOfWar,
            bool isPlayerAlliance,
            bool isWithinFog,
            bool isFleet)
        {
            return fixEnabled && calledFromCampaign && useFogOfWar && !isPlayerAlliance && isWithinFog && !isFleet;
        }

        // CheckModelReplacement's removal branch destroys the model and re-runs
        // CheckRegimentVisibility (116463-116467), which recomputes iswithinfog; that fresh
        // value stands and the Finalizer must not overwrite it.
        internal static bool VanillaRecomputedFog(bool hadModelBefore, bool hasModelAfter)
        {
            return hadModelBefore && !hasModelAfter;
        }

        // Ship.Initialize (141255) adds 20 ship-firing SoundObjects per transport model to
        // SoundEngine lists that never shrink (AddPurposeArray 146747-146756), but only a
        // fleet vessel's model ever fires them (AnimateFire callers 19058/19283/19672 use
        // singlevesselshipref, 18230). While the fix creates a fogged AI land unit's model,
        // those models share one array instead of adding 20 each.
        internal static bool ShouldShareFiringSounds(bool creatingFoggedLandModel, int purposeId, int shipFiringPurpose)
        {
            return creatingFoggedLandModel && purposeId == shipFiringPurpose;
        }

        // Camp, entrenchment and transport models of a unit the player cannot see stay
        // hidden; any other unit shows them as vanilla does. Player-alliance units are never
        // touched.
        internal static bool ShouldHideModels(bool isPlayerAlliance, bool isWithinFog)
        {
            return !isPlayerAlliance && isWithinFog;
        }

        // Per-frame camp placement budget. Night halt (133958) stops every hidden AI unit
        // that marched that day at the same game hour, so ~2.2 game hours later
        // (timetoopensummercamp, 115408) all of them place a camp within one regular cycle;
        // CBuilding.Place fits the camp mesh to the ground per vertex (95840-95891). A
        // fogged unit WITHOUT a camp is flipped only while the frame's budget has room: for
        // it, iswithinfog is read only by the placement test (115408; the entrenchment model
        // at 115455 needs a camp), so not flipping is exactly "no placement this cycle" and
        // vanilla places it on a later regular cycle. A unit WITH a camp is always flipped,
        // or vanilla's fog branch (115426) destroys the camp and entrenchment.
        internal static bool ShouldFlipUnderPlacementBudget(bool hasCamp, bool budgetEnabled, bool budgetHasRoom)
        {
            return hasCamp || !budgetEnabled || budgetHasRoom;
        }

        // A flipped call that took a unit from no camp to a camp spent one placement.
        internal static bool PlacedNewCamp(bool flipped, bool hadCamp, bool hasCamp)
        {
            return flipped && !hadCamp && hasCamp;
        }

        // A budget below one would never place a camp for a hidden AI unit.
        internal static int EffectivePlacementsPerFrame(int configured)
        {
            return configured < 1 ? 1 : configured;
        }

        // While the player cannot see an embarked AI land unit, its train, whistle and
        // steamboat sound objects are detached. Vanilla plays them for any unit with a model
        // (RegimentMove 133623-133649, no fog test; SoundEngine has none either, 146421-146610),
        // and before the embarkation fix a hidden unit never had one.
        internal static bool ShouldDetachTransportSounds(bool isPlayerAlliance, bool isWithinFog, bool hasModel, bool isFleet)
        {
            return !isPlayerAlliance && !isFleet && isWithinFog && hasModel;
        }

        // Ship.InitializeFlag (141278-141284) is skipped for a model the fix creates for a
        // fogged AI land unit (the Finalizer hides it at once) and replayed when the model is
        // shown. Only while the replay can run: a skip that cannot be replayed would leave
        // a visible model with the prefab's flag.
        internal static bool ShouldDeferTransportFlag(bool deferEnabled, bool creatingFoggedLandModel, bool replayAvailable)
        {
            return deferEnabled && creatingFoggedLandModel && replayAvailable;
        }
    }

    // Transport models whose flag setup was skipped, one per unit. Resolve runs after the
    // Finalizer set the model's active state: a deferred model that is active now gets its
    // flag set up (once); an entry whose model was replaced or destroyed is dropped, so the
    // map holds at most one live model per hidden embarked unit. Main thread only.
    internal sealed class DeferredFlagLedger<TUnit, TModel> where TUnit : class where TModel : class
    {
        private readonly Dictionary<TUnit, TModel> _pending;

        internal DeferredFlagLedger(int capacity)
        {
            _pending = new Dictionary<TUnit, TModel>(capacity);
        }

        internal int Count => _pending.Count;

        internal void Defer(TUnit unit, TModel model)
        {
            if (unit == null || model == null) return;
            _pending[unit] = model;
        }

        // True when the caller must replay the flag setup on currentModel now.
        internal bool Resolve(TUnit unit, TModel currentModel, bool modelActive)
        {
            if (unit == null || _pending.Count == 0 || !_pending.TryGetValue(unit, out TModel model)) return false;
            if (!ReferenceEquals(model, currentModel))
            {
                _pending.Remove(unit);
                return false;
            }
            if (!modelActive) return false;
            _pending.Remove(unit);
            return true;
        }

        internal void Clear()
        {
            _pending.Clear();
        }
    }

    // Counts camp placements within one frame (Time.frameCount); a new frame starts at zero.
    // Main thread only.
    internal sealed class PerFrameBudget
    {
        private int _frame = int.MinValue;
        private int _spent;

        internal bool HasRoom(int frame, int perFrame)
        {
            Roll(frame);
            return _spent < perFrame;
        }

        internal void Spend(int frame)
        {
            Roll(frame);
            _spent++;
        }

        private void Roll(int frame)
        {
            if (frame == _frame) return;
            _frame = frame;
            _spent = 0;
        }
    }
}
