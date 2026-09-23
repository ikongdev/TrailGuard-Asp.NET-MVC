namespace TrailGuard.Services
{
    public enum RegisterButtonStyle
    {
        Primary,
        Muted,
        Disabled
    }

    public enum RegisterButtonTarget
    {
        None,
        Assessment,
        MyRegistrations
    }

    public record RegisterButtonState(string Label, RegisterButtonStyle Style, RegisterButtonTarget Target);

    public static class RegistrationButtonHelper
    {



        public static RegisterButtonState GetState(string? registrationStatus, bool isFull)
        {
            switch (registrationStatus)
            {
                case null:
                case "Cancelled":
                case "Voided":



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







                    return new RegisterButtonState("Alternative Recommended", RegisterButtonStyle.Muted, RegisterButtonTarget.MyRegistrations);

                default:
                    return new RegisterButtonState(registrationStatus, RegisterButtonStyle.Disabled, RegisterButtonTarget.None);
            }
        }
    }
}
