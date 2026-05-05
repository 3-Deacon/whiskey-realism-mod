namespace WhiskeyRealism.Strategic
{
    public static class DefenseIntentTelemetry
    {
        public static string Summary(DefenseIntentLedgerOutput output)
        {
            if (output == null || output.Responses == null)
                return "responses=0 active=0 guard=0 selected=0 suppressed=0 signature=";

            int active = 0;
            int guard = 0;
            int selected = 0;
            int suppressed = 0;

            for (int i = 0; i < output.Responses.Count; i++)
            {
                var response = output.Responses[i];
                if (response == null) continue;

                if (response.Threat != null)
                {
                    if (response.Threat.Posture == DefensePosture.CoastalGuard) guard++;
                    else if (response.Threat.Posture == DefensePosture.ActiveInvasion ||
                             response.Threat.Posture == DefensePosture.ContainAndCounterattack ||
                             response.Threat.Posture == DefensePosture.InvasionWatch)
                    {
                        active++;
                    }
                }

                if (response.SelectedPackage != null) selected += response.SelectedPackage.Count;
                if (response.Suppressed != null) suppressed += response.Suppressed.Count;
            }

            return "responses=" + output.Responses.Count +
                   " active=" + active +
                   " guard=" + guard +
                   " selected=" + selected +
                   " suppressed=" + suppressed +
                   " signature=" + (output.Signature ?? "");
        }
    }
}
