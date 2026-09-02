namespace TrailGuard.Services
{
    public enum RegisterButtonStyle
    {
        Primary,  // vivid CTA gradient, clickable
        Muted,    // subdued, clickable - not a call to action, just a pointer somewhere
        Disabled  // greyed out, not clickable
    }

    public enum RegisterButtonTarget
    {
        None,
        Assessment,       // Assessment/Form - start a new registration
        MyRegistrations   // Registration/MyRegistrations - continue/view an existing one
    }

    public record RegisterButtonState(string Label, RegisterButtonStyle Style, RegisterButtonTarget Target);

    public static class RegistrationButtonHelper
    {
        // registrationStatus is the participant's current EventRegistration.Status for this
        // event (resolved to the active one if they have one, else their most recent), or
        // null if they have never registered for it.
        public static RegisterButtonState GetState(string? registrationStatus, bool isFull)
        {
            switch (registrationStatus)
            {
                case null:
                case "Cancelled":
                case "Voided":
                    // Process outcomes with no judgement attached - the participant cancelled,
                    // or a payment window lapsed. Nothing about the trail changed, so they can
                    // just try again, capacity permitting.
                    return isFull
                        ? new RegisterButtonState("Full", RegisterButtonStyle.Disabled, RegisterButtonTarget.None)
                        : new RegisterButtonState("Register", RegisterButtonStyle.Primary, RegisterButtonTarget.Assessment);

                case "Pending":
                    return new RegisterButtonState("Pending Approval", RegisterButtonStyle.Disabled, RegisterButtonTarget.None);

                case "Awaiting Payment":
                    return new RegisterButtonState("Upload Payment", RegisterButtonStyle.Primary, RegisterButtonTarget.MyRegistrations);

                case "For Payment Verification":
                    return new RegisterButtonState("Payment Under Review", RegisterButtonStyle.Disabled, RegisterButtonTarget.None);

                case "Accepted":
                    return new RegisterButtonState("Registered", RegisterButtonStyle.Disabled, RegisterButtonTarget.None);

                case "Rejected":
                    return new RegisterButtonState("Not Accepted", RegisterButtonStyle.Disabled, RegisterButtonTarget.None);

                case "Alternative Recommended":
                    // The organizer looked at this participant and this trail and steered them
                    // elsewhere. Reversing that should take more than one click from a listing
                    // page - point them at My Registrations, where the suggested event and the
                    // organizer's reason are actually shown. Views/Participant/Events.cshtml no
                    // longer renders this as a button at all (it shows plain status text plus a
                    // single View Details action instead - a two-button row with this label wraps
                    // to two lines), but the case stays correct here for any other caller.
                    return new RegisterButtonState("Alternative Recommended", RegisterButtonStyle.Muted, RegisterButtonTarget.MyRegistrations);

                default:
                    return new RegisterButtonState(registrationStatus, RegisterButtonStyle.Disabled, RegisterButtonTarget.None);
            }
        }
    }
}
