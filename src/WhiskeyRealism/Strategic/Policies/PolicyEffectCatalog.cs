using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.Policies
{
    // What a government can need from a policy. Each value of a NeedVector is in [0, 1]
    // (CampaignNeedBuilder); each catalog weight says how much one policy answers that need.
    public enum NeedKind
    {
        Credit,
        Treasury,
        Manpower,
        ContractStability,
        Morale,
        Support,
        Recognition,
        Blockade,
        Industry,
        Agriculture,
        Logistics,
        Supply
    }

    public sealed class PolicyEffect
    {
        private readonly float[] _weights = new float[PolicyEffectCatalog.NeedKindCount];
        private readonly float[] _costs = new float[PolicyEffectCatalog.NeedKindCount];

        internal PolicyEffect(int policyId)
        {
            PolicyId = policyId;
        }

        public int PolicyId { get; }

        // Non-null when the policy has a scripted, one-way effect the scorer must not
        // trigger by accident (units spawned, war declared, contracts reset, slavery ended,
        // cotton burned). Every act is permanent once researched; this marks the ones whose
        // side effects reach beyond the alliance's own economy.
        public string IrreversibleReason { get; private set; }

        public float Weight(NeedKind kind) => _weights[(int)kind];

        public float Cost(NeedKind kind) => _costs[(int)kind];

        internal PolicyEffect Meets(NeedKind kind, float weight)
        {
            _weights[(int)kind] = weight;
            return this;
        }

        internal PolicyEffect Costs(NeedKind kind, float cost)
        {
            _costs[(int)kind] = cost;
            return this;
        }

        internal PolicyEffect Irreversible(string reason)
        {
            IrreversibleReason = reason;
            return this;
        }
    }

    // What each researchable policy does, from vanilla Policies.LaunchPolicyEffect
    // (decompile 209681-210955; one case per id) with magnitudes from the default
    // Config/campaignprefs.txt "Policies & Subsidies" block. Weights are relative effect
    // strengths in [0, 1]; costs are need kinds the policy makes worse. Grand policies
    // (70-79, 170-179: ResearchDuration 0, chosen once before the war through
    // Policies.UpdateGrandStrategies 209353) are never researched in play and have no entry.
    // Intervention: AdjustInterventionLevel raises the actor's own level, and
    // GetInterventionProbability (62822) is CSA level − Union level, so a positive change
    // helps the actor on either side; a negative one is a Recognition cost.
    public static class PolicyEffectCatalog
    {
        public const int NeedKindCount = 12;

        private static readonly Dictionary<int, PolicyEffect> Effects = Build();

        public static PolicyEffect Get(int policyId)
        {
            return Effects.TryGetValue(policyId, out var effect) ? effect : null;
        }

        // Policy.GetAllianceFromPolicy (211392) reads the policy's UI folder; the ids of
        // Config/policies.dat follow it: below 100 Union, 100-199 CSA.
        public static int AllianceOf(int policyId)
        {
            if (policyId >= 0 && policyId < 100) return 0;
            if (policyId >= 100 && policyId < 200) return 1;
            return -1;
        }

        private static Dictionary<int, PolicyEffect> Build()
        {
            var e = new Dictionary<int, PolicyEffect>();

            // Shared ladders: the same LaunchPolicyEffect case serves both sides (Union id, CSA id + 100).
            // Government Funding I-III — economy subsidy cap 0.25/0.5/1.0 (economysubsidies, 209769-209780); unlocks the credit acts.
            Both(e, 0, x => x.Meets(NeedKind.Treasury, 0.4f).Meets(NeedKind.Credit, 0.2f));
            Both(e, 1, x => x.Meets(NeedKind.Treasury, 0.5f).Meets(NeedKind.Credit, 0.3f));
            Both(e, 2, x => x.Meets(NeedKind.Treasury, 0.6f).Meets(NeedKind.Credit, 0.3f));
            // Industrialization I-IV — industry + transport subsidy caps (industrialpolicyadj, 209800-209815).
            Both(e, 7, x => x.Meets(NeedKind.Industry, 0.4f).Meets(NeedKind.Logistics, 0.2f));
            Both(e, 8, x => x.Meets(NeedKind.Industry, 0.5f).Meets(NeedKind.Logistics, 0.3f));
            Both(e, 9, x => x.Meets(NeedKind.Industry, 0.6f).Meets(NeedKind.Logistics, 0.3f));
            Both(e, 10, x => x.Meets(NeedKind.Industry, 0.7f).Meets(NeedKind.Logistics, 0.4f));
            // Military I-IV — recruitment, civil order and trade-war subsidy caps, rail speed (militarysubsidies, 209816-209831).
            Both(e, 11, x => x.Meets(NeedKind.Manpower, 0.4f).Meets(NeedKind.Support, 0.1f).Meets(NeedKind.Blockade, 0.1f).Meets(NeedKind.Logistics, 0.1f));
            Both(e, 12, x => x.Meets(NeedKind.Manpower, 0.5f).Meets(NeedKind.Support, 0.2f).Meets(NeedKind.Blockade, 0.2f).Meets(NeedKind.Logistics, 0.2f));
            Both(e, 13, x => x.Meets(NeedKind.Manpower, 0.6f).Meets(NeedKind.Support, 0.2f).Meets(NeedKind.Blockade, 0.2f).Meets(NeedKind.Logistics, 0.3f));
            Both(e, 14, x => x.Meets(NeedKind.Manpower, 0.7f).Meets(NeedKind.Support, 0.3f).Meets(NeedKind.Blockade, 0.3f).Meets(NeedKind.Logistics, 0.3f));
            // Diplomacy I-V — diplomacy subsidy cap; diplomacy subsidies raise the intervention level and trade prices (diplomacysubsidyadj, 209832-209851).
            Both(e, 15, x => x.Meets(NeedKind.Recognition, 0.4f).Meets(NeedKind.Treasury, 0.1f));
            Both(e, 16, x => x.Meets(NeedKind.Recognition, 0.5f).Meets(NeedKind.Treasury, 0.1f));
            Both(e, 17, x => x.Meets(NeedKind.Recognition, 0.6f).Meets(NeedKind.Treasury, 0.1f));
            Both(e, 18, x => x.Meets(NeedKind.Recognition, 0.7f).Meets(NeedKind.Treasury, 0.2f));
            Both(e, 19, x => x.Meets(NeedKind.Recognition, 0.8f).Meets(NeedKind.Treasury, 0.2f));
            // Print Notes I-II — +50M treasury a year each, public wealth ×0.9 (209852-209861).
            Both(e, 20, x => x.Meets(NeedKind.Treasury, 0.7f).Costs(NeedKind.Treasury, 0.2f));
            Both(e, 21, x => x.Meets(NeedKind.Treasury, 0.7f).Costs(NeedKind.Treasury, 0.2f));
            // War Bonds — credit rating −2 notches (better), public wealth ×0.9 (209862-209866).
            Both(e, 22, x => x.Meets(NeedKind.Credit, 0.8f).Meets(NeedKind.Treasury, 0.2f).Costs(NeedKind.Treasury, 0.1f));
            // Bank Act (I) — rating −2 notches, +120M a year, state support −0.03 (209867-209875).
            Both(e, 23, x => x.Meets(NeedKind.Credit, 0.8f).Meets(NeedKind.Treasury, 0.6f).Costs(NeedKind.Support, 0.3f));
            // Tariff Act — tariff tax cap 0.5; own intervention level −0.1 (209876-209890).
            Both(e, 24, x => x.Meets(NeedKind.Treasury, 0.5f).Costs(NeedKind.Recognition, 0.3f));
            // Revenue Acts I-III — income/corporate tax caps; state support −0.01/−0.02/−0.03 (209903-209929).
            Both(e, 26, x => x.Meets(NeedKind.Treasury, 0.5f).Costs(NeedKind.Support, 0.1f));
            Both(e, 27, x => x.Meets(NeedKind.Treasury, 0.6f).Costs(NeedKind.Support, 0.2f));
            Both(e, 28, x => x.Meets(NeedKind.Treasury, 0.7f).Costs(NeedKind.Support, 0.3f));
            // Arming of Civilian Ships — ship resource cost ×0.25 for types 0-3; port transport capacity ×0.93 (210017-210021).
            Both(e, 35, x => x.Meets(NeedKind.Blockade, 0.6f).Costs(NeedKind.Logistics, 0.1f));
            // Bounties I-II — volunteers +0.12 each and +0.10/yr; $300/$600 per volunteer (210369-210386).
            Both(e, 45, x => x.Meets(NeedKind.Manpower, 0.6f).Costs(NeedKind.Credit, 0.3f).Costs(NeedKind.Treasury, 0.3f));
            Both(e, 46, x => x.Meets(NeedKind.Manpower, 0.7f).Costs(NeedKind.Credit, 0.5f).Costs(NeedKind.Treasury, 0.5f));
            // Letters of Marque — trade-war subsidy cap ×1.2, floor on enemy port blockade; own intervention −0.1 (210353-210360).
            Both(e, 43, x => x.Meets(NeedKind.Blockade, 0.6f).Costs(NeedKind.Recognition, 0.3f));

            // Union ladders.
            // Bread Basket I-IV — agriculture subsidy cap (agriculturalsubsidies, 209781-209799).
            Add(e, 3).Meets(NeedKind.Agriculture, 0.4f).Meets(NeedKind.Treasury, 0.2f);
            Add(e, 4).Meets(NeedKind.Agriculture, 0.5f).Meets(NeedKind.Treasury, 0.2f);
            Add(e, 5).Meets(NeedKind.Agriculture, 0.6f).Meets(NeedKind.Treasury, 0.3f);
            Add(e, 6).Meets(NeedKind.Agriculture, 0.7f).Meets(NeedKind.Treasury, 0.3f);
            // Confiscation Act — supply ×1.15 on enemy soil; CSA support +0.03/yr in slave states (209891-209902).
            Add(e, 25).Meets(NeedKind.Supply, 0.5f).Costs(NeedKind.Support, 0.1f);
            // Bank Act II — rating −2 notches, +150M a year, support −0.03 (209930-209937).
            Add(e, 29).Meets(NeedKind.Credit, 0.8f).Meets(NeedKind.Treasury, 0.6f).Costs(NeedKind.Support, 0.3f);
            // Northern Routes — crops ×1.25 in seven northern states; Midwest volunteers ×0.9 (209938-209948).
            Add(e, 30).Meets(NeedKind.Agriculture, 0.5f).Meets(NeedKind.Logistics, 0.3f).Costs(NeedKind.Manpower, 0.1f);
            // Feed Europe I-II — European crop demand, intervention +0.1/+0.2, land sales tax; II worsens rating +1 notch (209949-209980).
            Add(e, 31).Meets(NeedKind.Recognition, 0.4f).Meets(NeedKind.Agriculture, 0.3f).Meets(NeedKind.Treasury, 0.3f);
            Add(e, 32).Meets(NeedKind.Recognition, 0.6f).Meets(NeedKind.Agriculture, 0.3f).Meets(NeedKind.Treasury, 0.3f).Costs(NeedKind.Credit, 0.2f);
            // USCT — spawns a corps of three brigades; volunteers +0.05/yr; CSA morale +0.02 (209981-210008).
            Add(e, 33).Meets(NeedKind.Manpower, 0.7f).Irreversible("spawns units");
            // Emancipation Proclamation — Union support +0.05, intervention +0.2; CSA morale +0.05 (210009-210016).
            Add(e, 34).Meets(NeedKind.Recognition, 0.7f).Meets(NeedKind.Support, 0.3f).Costs(NeedKind.Morale, 0.1f);
            // Militia Acts I-IV — contract 3/12/24/36 months, volunteers potential and growth (210022-210182).
            Add(e, 36).Meets(NeedKind.Manpower, 0.6f).Costs(NeedKind.ContractStability, 0.3f);
            Add(e, 37).Meets(NeedKind.Manpower, 0.6f).Meets(NeedKind.ContractStability, 0.3f);
            Add(e, 38).Meets(NeedKind.Manpower, 0.5f).Meets(NeedKind.ContractStability, 0.5f);
            Add(e, 47).Meets(NeedKind.Manpower, 0.4f).Meets(NeedKind.ContractStability, 0.7f);
            // Enrollment Acts I-II — mobilization +0.5/+0.3, morale −0.02, every contract reset (210257-210323).
            Add(e, 39).Meets(NeedKind.Manpower, 0.9f).Meets(NeedKind.ContractStability, 0.5f).Costs(NeedKind.Morale, 0.3f).Irreversible("resets every contract");
            Add(e, 40).Meets(NeedKind.Manpower, 0.7f).Meets(NeedKind.ContractStability, 0.5f).Costs(NeedKind.Morale, 0.3f).Irreversible("resets every contract");
            // Legal Blockade — intervention +0.2, trade-war cap ×1.2, European trade ×0.75 (210324-210344).
            Add(e, 41).Meets(NeedKind.Blockade, 0.8f).Meets(NeedKind.Recognition, 0.3f).Costs(NeedKind.Treasury, 0.1f);
            // Lieutenant General Act — CIC fame +1 and rank 8; battle losses cost morale ×1.2 (210345-210352).
            Add(e, 42).Meets(NeedKind.Morale, 0.2f).Costs(NeedKind.Morale, 0.1f);
            // Abolition of Slavery — intervention +0.1, Union morale +0.05, CSA morale +0.10 (210361-210368).
            Add(e, 44).Meets(NeedKind.Recognition, 0.5f).Meets(NeedKind.Morale, 0.4f).Costs(NeedKind.Morale, 0.2f);
            // Regulars Act — spawns seven regular regiments; military experience −0.05 (210183-210256).
            Add(e, 48).Meets(NeedKind.Manpower, 0.4f).Irreversible("spawns units");

            // CSA ladders.
            // King Cotton I-IV — agriculture subsidy cap; cotton is the CSA's export income (209781-209799).
            Add(e, 103).Meets(NeedKind.Agriculture, 0.4f).Meets(NeedKind.Treasury, 0.3f);
            Add(e, 104).Meets(NeedKind.Agriculture, 0.5f).Meets(NeedKind.Treasury, 0.3f);
            Add(e, 105).Meets(NeedKind.Agriculture, 0.6f).Meets(NeedKind.Treasury, 0.4f);
            Add(e, 106).Meets(NeedKind.Agriculture, 0.7f).Meets(NeedKind.Treasury, 0.4f);
            // Impressment Act — recruits +0.1, military supply cost ×0.8, support −0.03 (210571-210584).
            Add(e, 125).Meets(NeedKind.Manpower, 0.6f).Meets(NeedKind.Supply, 0.4f).Costs(NeedKind.Support, 0.3f);
            // Nationalized Trade Act — European cotton demand ×1.35; support −0.03 in slave states (210585-210607).
            Add(e, 129).Meets(NeedKind.Treasury, 0.5f).Meets(NeedKind.Recognition, 0.2f).Costs(NeedKind.Support, 0.3f);
            // Burn Cotton — own cotton stocks ×0.5; intervention +0.15 (210608-210623).
            Add(e, 130).Meets(NeedKind.Recognition, 0.6f).Costs(NeedKind.Treasury, 0.5f).Irreversible("burns half the cotton stock");
            // Restrict Cotton Trade — European cotton demand, intervention +0.1, public wealth ×0.95 (210624-210638).
            Add(e, 131).Meets(NeedKind.Recognition, 0.5f).Costs(NeedKind.Treasury, 0.2f);
            // Slavery Restriction Act — intervention +0.2; support −0.05 in slave states (210639-210655).
            Add(e, 132).Meets(NeedKind.Recognition, 0.7f).Costs(NeedKind.Support, 0.4f);
            // CSCT — spawns an army of three brigades; support −0.02 in slave states (210656-210683).
            Add(e, 133).Meets(NeedKind.Manpower, 0.7f).Costs(NeedKind.Support, 0.2f).Irreversible("spawns units");
            // Emancipation (CSA) — ends slavery, drafts +0.35, intervention +0.25, own support −0.1, Union morale −0.1 (210684-210702).
            Add(e, 134).Meets(NeedKind.Manpower, 0.8f).Meets(NeedKind.Recognition, 0.8f).Costs(NeedKind.Support, 0.6f).Irreversible("ends slavery");
            // Militia Acts I-III (CSA) — contract 12/24/36 months (militiaactcontract[1 + n], 210096-210163).
            Add(e, 136).Meets(NeedKind.Manpower, 0.6f).Meets(NeedKind.ContractStability, 0.3f);
            Add(e, 137).Meets(NeedKind.Manpower, 0.5f).Meets(NeedKind.ContractStability, 0.5f);
            Add(e, 138).Meets(NeedKind.Manpower, 0.4f).Meets(NeedKind.ContractStability, 0.7f);
            // Conscription Acts I-II — as Enrollment, plus CSA contracts reset to 36/99 months (210257-210323).
            Add(e, 139).Meets(NeedKind.Manpower, 0.9f).Meets(NeedKind.ContractStability, 0.6f).Costs(NeedKind.Morale, 0.3f).Irreversible("resets every contract");
            Add(e, 140).Meets(NeedKind.Manpower, 0.7f).Meets(NeedKind.ContractStability, 0.7f).Costs(NeedKind.Morale, 0.3f).Irreversible("resets every contract");
            // Free Trade Act — tariff cap ×0.15, intervention +0.1 (210703-210716).
            Add(e, 141).Meets(NeedKind.Recognition, 0.5f).Costs(NeedKind.Treasury, 0.3f);
            // Organized Blockade Running — blockade ratios ×0.75; Union morale +0.02 (210717-210723).
            Add(e, 142).Meets(NeedKind.Blockade, 0.8f).Meets(NeedKind.Industry, 0.2f);
            // Support Mexican Intervention — war with Mexico, intervention +0.15, Texas recruits −0.5, Union morale +0.03 (210724-210732).
            Add(e, 144).Meets(NeedKind.Recognition, 0.6f).Costs(NeedKind.Manpower, 0.3f).Irreversible("starts a war with Mexico");
            // Regulars Act (CSA) — spawns seven regular regiments (210183-210256).
            Add(e, 147).Meets(NeedKind.Manpower, 0.4f).Irreversible("spawns units");

            return e;
        }

        private static void Both(Dictionary<int, PolicyEffect> effects, int unionId, System.Action<PolicyEffect> configure)
        {
            configure(Add(effects, unionId));
            configure(Add(effects, unionId + 100));
        }

        private static PolicyEffect Add(Dictionary<int, PolicyEffect> effects, int id)
        {
            var effect = new PolicyEffect(id);
            effects.Add(id, effect);
            return effect;
        }
    }
}
