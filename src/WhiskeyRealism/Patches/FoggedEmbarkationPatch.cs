using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic.BugFixes;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // BUG-LOG-002a. Vanilla Regiment.CheckModelReplacement (decompile 116372-116503) runs
    // for every campaign unit each regular cycle (112225, right after
    // UpdateCampsAndEntrenchments). It creates the river boat (116383), sea ship (116399,
    // with SetEmbarkmentMode 116418) or train model (116446) and sets currentmodeltype only
    // when !iswithinfog, and removes the model when fogged (116461, currentmodeltype = -1).
    // The model is the embarkation state: rail speed (133401) and terrain cost (132891),
    // the night halt of marching troops (133958), transport-capacity use and the speed
    // limit from it (116471-116501), locomotive/ship consumption (133900-133915), the
    // transport ration factor (114186) and no reinforcements while embarked (114501) all
    // read shownmodel/currentmodeltype. A hidden AI unit was therefore never embarked.
    // Vanilla gates the model on fog because it would reveal the unit.
    //
    // For a fogged AI land unit on the campaign map the Prefix clears iswithinfog for the
    // call (UnitFogFlip); the Finalizer sets it again, even if vanilla throws, unless the
    // removal branch re-ran CheckRegimentVisibility (116463-116467) and so recomputed it.
    // It then deactivates the transport model while the player cannot see the unit and
    // reactivates it once the unit is visible. The model's Ship.Update only follows the
    // unit's position (141265, 141358); Ship.Initialize plays nothing (141228-141262).
    // A fleet (unittyp 17) and player-alliance units are never touched (FoggedModelRule).
    //
    // SharedFiringSounds: Ship.Initialize adds 20 ship-firing SoundObjects per model to
    // SoundEngine lists that never shrink (141255 -> AddPurposeArray 146747-146756). A land
    // unit's transport never fires (AnimateFire's callers 19058/19283/19672 use fleet
    // vessels' models, 18230), so while this patch creates a fogged unit's model the Prefix
    // returns one remembered array index per SoundEngine instead of adding another 20
    // (a Prefix that skips vanilla, limited to that purpose inside this patch's call).
    //
    // BUG-LOG-015, transport sounds: RegimentMove plays the train loop and whistle or the
    // steamboat loop for any campaign land unit with a model (133623-133649) with no fog
    // test, and SoundEngine has none either (UpdateSoundObject/ChangeClip 146421-146667:
    // distance to the camera and the unit GameObject's activeInHierarchy only). Those
    // sound objects sit on the unit's own GameObject (InitCampaignSounds 124179-124205),
    // not on the hidden model, so a hidden embarked AI unit would be heard. The Finalizer
    // detaches the unit's four transport sound objects (go = null: off and out of the
    // nearest-source pick, 146438-146513) while the player cannot see it with a model, and
    // reattaches them (oldgo, vanilla's own restore 146765) otherwise. The unit's other
    // sounds are vanilla's (RegimentMove turns its foot loop off while it has a model).
    // Rollback: Campaign Bug Fixes / Enable Fogged AI Embarkation Fix (models are still
    // re-shown and sounds reattached for visible units when the flag is off).
    //
    // HiddenModelFlag (perf L2, skipping Prefix approved by the user 2026-10-15: "Approve
    // (Recommended)"): Ship.Initialize sets up the model's flag last (141258-141260 ->
    // InitializeFlag 141278-141284 -> Flag.UpdateTexture 99849-99993: three GameObject.Find,
    // two Resources.Load and a Renderer.material copy per renderer, which Object.Destroy of
    // the model does not free). For a model this patch creates for a fogged AI land unit
    // (Flip active; the Finalizer hides it at once) the Prefix skips it and records the
    // model; the Finalizer replays InitializeFlag (the same private method, so the same
    // allianceid read and the naval-flag texture for the date) once the model is active,
    // which is the frame vanilla itself would have created the model and set its flag up.
    // Nothing reads the skipped state in between: Ship.flag is read only by InitializeFlag
    // (set 141241); Flag.Update (99748) does not run on an inactive model and is null-safe
    // anyway (papermapquadobj null 99832, blinkingstate 0 100019); Flag.Show/Hide/SetBlinking/
    // ActivatePaperMapSymbolFleets have no caller holding a transport model's Flag (the
    // other GetComponent<Flag> sites, 76794, 94319, 112465, are monuments, buildings and the
    // unit's own flags, found by direct child name). A model replaced or destroyed while
    // hidden is dropped from the record and never set up, as it never shows. The skip
    // happens only once the replay delegate resolved; if it cannot, vanilla runs.
    // Rollback: Campaign Bug Fixes / Enable Hidden Transport Flag Deferral (models already
    // deferred are still set up when shown).
    internal static class FoggedEmbarkationPatch
    {
        private static readonly UnitFogFlip<Regiment, object> Flip = new UnitFogFlip<Regiment, object>(
            unit => unit.iswithinfog,
            (unit, fog) => unit.iswithinfog = fog);

        private static readonly SharedArrayIndex FiringSounds = new SharedArrayIndex();

        // Hidden transport models whose flag setup waits for the model to be shown.
        private static readonly DeferredFlagLedger<Regiment, Ship> DeferredFlags = new DeferredFlagLedger<Regiment, Ship>(64);

        // Ship.InitializeFlag (private), resolved once before the first skip.
        private static Action<Ship> _initializeFlag;
        private static bool _initializeFlagResolved;
        private static bool _initializeFlagMissing;

        private static readonly int[] TransportSounds =
        {
            Regiment.csound_unitmovingtrain,
            Regiment.csound_trainwhistle,
            Regiment.csound_steamshiprivermoving,
            Regiment.csound_steamshipseamoving,
        };

        // Units whose transport sound objects are detached; one set lookup per call.
        private static readonly HashSet<Regiment> Silenced = new HashSet<Regiment>();

        // SoundEngine.soundobject (private List<SoundObject>) and SoundObject.go / oldgo,
        // resolved once on the first detach.
        private static FieldInfo _soundObjects;
        private static FieldInfo _soundGo;
        private static FieldInfo _soundOldGo;
        private static bool _soundFieldsResolved;
        private static bool _soundFieldsMissing;
        private static readonly Func<object, object> GetSoundGo = o => _soundGo.GetValue(o);
        private static readonly Func<object, object> GetSoundOldGo = o => _soundOldGo.GetValue(o);
        private static readonly Action<object, object> SetSoundGo = (o, go) => _soundGo.SetValue(o, go);

        // First-log flags: OnceLog.Info takes a lock and a set lookup, so each line is
        // formatted once per campaign instead of on every call.
        private static bool _keptLogged;
        private static bool _hiddenLogged;
        private static bool _shownLogged;
        private static bool _sharedSoundsLogged;
        private static bool _silencedLogged;
        private static bool _flagDeferredLogged;
        private static bool _flagReplayedLogged;

        static FoggedEmbarkationPatch()
        {
            CampaignScope.Register("fogged-embarkation", () =>
            {
                Silenced.Clear();
                DeferredFlags.Clear();
                _keptLogged = false;
                _hiddenLogged = false;
                _shownLogged = false;
                _sharedSoundsLogged = false;
                _silencedLogged = false;
                _flagDeferredLogged = false;
                _flagReplayedLogged = false;
            });
        }

        [HarmonyPatch(typeof(Regiment), "CheckModelReplacement")]
        internal static class ModelReplacement
        {
            [HarmonyPrefix]
            internal static void Prefix(Regiment __instance, ref bool __state)
            {
                using (TelemetryPerf.Scope("campaign.patch.fogged-embarkation.prefix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
                {
                    PatchFirstFire.Mark("campaign.patch.fogged-embarkation.prefix");
                    __state = false;
                    try
                    {
                        if (__instance == null || !__instance.iswithinfog) return;
                        if (!FoggedModelRule.ShouldTreatAsVisibleForEmbarkation(
                                Enabled(),
                                __instance.calledfromcampaign,
                                GameVars.usefow,
                                __instance.alliance == GameVars.playeralliance,
                                __instance.iswithinfog,
                                __instance.unittyp == 17))
                        {
                            return;
                        }
                        __state = (UnityEngine.Object)__instance.shownmodel != null;
                        Flip.Apply(__instance);
                    }
                    catch (Exception ex)
                    {
                        OnceLog.Warning("fogged-embarkation:prefix", "[Patch:FoggedEmbarkation] prefix failed (vanilla runs unchanged): " + ex.Message);
                    }
                }
            }

            [HarmonyFinalizer]
            internal static void Finalizer(Regiment __instance, bool __state)
            {
                using (TelemetryPerf.Scope("campaign.patch.fogged-embarkation.finalizer", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
                {
                    PatchFirstFire.Mark("campaign.patch.fogged-embarkation.finalizer");
                    try
                    {
                        bool flipped = Flip.Active;
                        bool hasModel = __instance != null && (UnityEngine.Object)__instance.shownmodel != null;
                        string failure = Flip.Restore(FoggedModelRule.VanillaRecomputedFog(__state, hasModel));
                        if (failure != null)
                            OnceLog.Warning("fogged-embarkation:restore", "[Patch:FoggedEmbarkation] restoring the fog flag failed: " + failure);
                        if (__instance == null) return;
                        if (flipped && hasModel && !_keptLogged)
                        {
                            _keptLogged = true;
                            OnceLog.Info("fogged-embarkation:kept", "[Patch:FoggedEmbarkation] a fogged AI unit is embarked (transport model hidden from the player)");
                        }

                        bool isPlayerAlliance = __instance.alliance == GameVars.playeralliance;
                        if (isPlayerAlliance || __instance.unittyp == 17) return;
                        int change = ModelActiveSync.Apply(
                            __instance.shownmodel,
                            m => (UnityEngine.Object)m == null,
                            m => ((Component)m).gameObject.activeSelf,
                            (m, on) => ((Component)m).gameObject.SetActive(on),
                            !FoggedModelRule.ShouldHideModels(isPlayerAlliance, __instance.iswithinfog));
                        if (change < 0 && !_hiddenLogged)
                        {
                            _hiddenLogged = true;
                            OnceLog.Info("fogged-embarkation:hidden", "[Patch:FoggedEmbarkation] hid the transport model of an AI unit the player cannot see");
                        }
                        else if (change > 0 && !_shownLogged)
                        {
                            _shownLogged = true;
                            OnceLog.Info("fogged-embarkation:shown", "[Patch:FoggedEmbarkation] re-showed the transport model of an AI unit the player now sees");
                        }
                        if (DeferredFlags.Count > 0)
                            ReplayDeferredFlag(__instance);

                        bool silence = FoggedModelRule.ShouldDetachTransportSounds(isPlayerAlliance, __instance.iswithinfog, hasModel, __instance.unittyp == 17);
                        if (silence != Silenced.Contains(__instance))
                            SyncTransportSounds(__instance, silence);
                    }
                    catch (Exception ex)
                    {
                        OnceLog.Warning("fogged-embarkation:finalizer", "[Patch:FoggedEmbarkation] finalizer failed: " + ex.Message);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(SoundEngine), "AddPurposeArray")]
        internal static class SharedFiringSounds
        {
            [HarmonyPrefix]
            internal static bool Prefix(SoundEngine __instance, int soundpurposeid, ref int __result)
            {
                using (TelemetryPerf.Scope("campaign.patch.fogged-embarkation.shared-sounds.prefix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
                {
                    PatchFirstFire.Mark("campaign.patch.fogged-embarkation.shared-sounds.prefix");
                    try
                    {
                        if (!FoggedModelRule.ShouldShareFiringSounds(Flip.Active, soundpurposeid, Regiment.csound_shipfiring)) return true;
                        if (!FiringSounds.TryGet(__instance, out int index)) return true;
                        __result = index;
                        if (!_sharedSoundsLogged)
                        {
                            _sharedSoundsLogged = true;
                            OnceLog.Info("fogged-embarkation:shared-sounds", "[Patch:FoggedEmbarkation] a hidden AI transport model reuses one ship-firing sound array");
                        }
                        return false;
                    }
                    catch (Exception ex)
                    {
                        OnceLog.Warning("fogged-embarkation:shared-sounds-failed", "[Patch:FoggedEmbarkation] sound sharing failed (vanilla adds its array): " + ex.Message);
                        return true;
                    }
                }
            }

            [HarmonyPostfix]
            // After the Prefix skipped vanilla, __result is the remembered index already;
            // remembering it again is a no-op.
            internal static void Postfix(SoundEngine __instance, int soundpurposeid, int __result)
            {
                using (TelemetryPerf.Scope("campaign.patch.fogged-embarkation.shared-sounds.postfix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
                {
                    PatchFirstFire.Mark("campaign.patch.fogged-embarkation.shared-sounds.postfix");
                    try
                    {
                        if (FoggedModelRule.ShouldShareFiringSounds(Flip.Active, soundpurposeid, Regiment.csound_shipfiring))
                            FiringSounds.Remember(__instance, __result);
                    }
                    catch (Exception ex)
                    {
                        OnceLog.Warning("fogged-embarkation:shared-sounds-remember", "[Patch:FoggedEmbarkation] remembering the sound array failed: " + ex.Message);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Ship), "InitializeFlag")]
        internal static class HiddenModelFlag
        {
            [HarmonyPrefix]
            internal static bool Prefix(Ship __instance)
            {
                using (TelemetryPerf.Scope("campaign.patch.fogged-embarkation.hidden-flag.prefix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
                {
                    PatchFirstFire.Mark("campaign.patch.fogged-embarkation.hidden-flag.prefix");
                    try
                    {
                        if (!Flip.Active) return true;
                        if (!FoggedModelRule.ShouldDeferTransportFlag(DeferFlagEnabled(), Flip.Active, ResolveInitializeFlag())) return true;
                        Regiment unit = Flip.Unit;
                        if (unit == null || __instance == null) return true;
                        DeferredFlags.Defer(unit, __instance);
                        if (!_flagDeferredLogged)
                        {
                            _flagDeferredLogged = true;
                            OnceLog.Info("fogged-embarkation:flag-deferred", "[Patch:FoggedEmbarkation] a hidden AI transport model sets its flag up when it is shown");
                        }
                        return false;
                    }
                    catch (Exception ex)
                    {
                        OnceLog.Warning("fogged-embarkation:flag-defer-failed", "[Patch:FoggedEmbarkation] flag deferral failed (vanilla sets the flag up): " + ex.Message);
                        return true;
                    }
                }
            }
        }

        // Runs vanilla's InitializeFlag on a deferred model that is active now (shown, or
        // left active because restoring the fog flag failed); drops a replaced model.
        private static void ReplayDeferredFlag(Regiment unit)
        {
            Ship model = unit.shownmodel;
            bool active = (UnityEngine.Object)model != null && ((Component)model).gameObject.activeSelf;
            if (!DeferredFlags.Resolve(unit, (UnityEngine.Object)model != null ? model : null, active)) return;
            try
            {
                _initializeFlag(model);
            }
            catch (Exception ex)
            {
                _initializeFlagMissing = true;
                OnceLog.Warning("fogged-embarkation:flag-replay-failed", "[Patch:FoggedEmbarkation] setting up a shown transport model's flag failed (later models set theirs up at creation): " + ex.Message);
                return;
            }
            if (!_flagReplayedLogged)
            {
                _flagReplayedLogged = true;
                OnceLog.Info("fogged-embarkation:flag-replayed", "[Patch:FoggedEmbarkation] set up the flag of a transport model the player now sees");
            }
        }

        private static bool ResolveInitializeFlag()
        {
            if (_initializeFlagMissing) return false;
            if (_initializeFlagResolved) return true;
            try
            {
                MethodInfo method = AccessTools.Method(typeof(Ship), "InitializeFlag", Type.EmptyTypes);
                if (method != null && method.ReturnType == typeof(void))
                    _initializeFlag = (Action<Ship>)Delegate.CreateDelegate(typeof(Action<Ship>), method);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("fogged-embarkation:flag-method-failed", "[Patch:FoggedEmbarkation] Ship.InitializeFlag lookup failed (flags set up at creation): " + ex.Message);
            }
            _initializeFlagResolved = _initializeFlag != null;
            if (!_initializeFlagResolved)
            {
                _initializeFlagMissing = true;
                OnceLog.Warning("fogged-embarkation:flag-method", "[Patch:FoggedEmbarkation] Ship.InitializeFlag not found (flags set up at creation)");
            }
            return _initializeFlagResolved;
        }

        // Detaches (silence) or reattaches the unit's own transport sound objects and
        // records the state; called only when the state changes.
        private static void SyncTransportSounds(Regiment unit, bool silence)
        {
            if (!ResolveSoundFields() || (UnityEngine.Object)unit.soundengine == null) return;
            int changed = SoundObjectDetach.Apply(
                _soundObjects.GetValue(unit.soundengine) as IList,
                unit.sound_objectref,
                TransportSounds,
                ((Component)unit).gameObject,
                silence,
                GetSoundGo,
                GetSoundOldGo,
                SetSoundGo);
            if (silence) Silenced.Add(unit);
            else Silenced.Remove(unit);
            if (silence && changed > 0 && !_silencedLogged)
            {
                _silencedLogged = true;
                OnceLog.Info("fogged-embarkation:silenced", "[Patch:FoggedEmbarkation] silenced the train or steamboat sounds of an AI unit the player cannot see");
            }
        }

        private static bool ResolveSoundFields()
        {
            if (_soundFieldsResolved) return true;
            if (_soundFieldsMissing) return false;
            try
            {
                _soundObjects = AccessTools.Field(typeof(SoundEngine), "soundobject");
                Type soundObject = AccessTools.Inner(typeof(SoundEngine), "SoundObject");
                _soundGo = soundObject == null ? null : AccessTools.Field(soundObject, "go");
                _soundOldGo = soundObject == null ? null : AccessTools.Field(soundObject, "oldgo");
                _soundFieldsResolved = _soundObjects != null && _soundGo != null && _soundOldGo != null;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("fogged-embarkation:sound-fields-failed", "[Patch:FoggedEmbarkation] SoundEngine field lookup failed (hidden transport sounds stay audible): " + ex.Message);
            }
            if (!_soundFieldsResolved)
            {
                _soundFieldsMissing = true;
                OnceLog.Warning("fogged-embarkation:sound-fields", "[Patch:FoggedEmbarkation] SoundEngine.soundobject or SoundObject.go/oldgo not found (hidden transport sounds stay audible)");
            }
            return _soundFieldsResolved;
        }

        private static bool DeferFlagEnabled()
        {
            try
            {
                return Plugin.Instance != null && Plugin.Instance.Enabled.Value
                    && Plugin.EnableHiddenTransportFlagDeferral != null
                    && Plugin.EnableHiddenTransportFlagDeferral.Value;
            }
            catch { return false; }
        }

        private static bool Enabled()
        {
            try
            {
                return Plugin.Instance != null && Plugin.Instance.Enabled.Value
                    && Plugin.EnableFoggedAiEmbarkationFix != null
                    && Plugin.EnableFoggedAiEmbarkationFix.Value;
            }
            catch { return false; }
        }
    }
}
