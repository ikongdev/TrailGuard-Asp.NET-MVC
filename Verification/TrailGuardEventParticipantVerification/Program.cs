using TrailGuard.Models;
using TrailGuard.Services;

var assertions = 0;
var now = new DateTime(2026, 10, 6, 9, 0, 0);

void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    assertions++;
}

EventRegistration Registration(int id, string userId, string status, DateTime registeredAt) => new()
{
    Id = id,
    EventId = 42,
    UserId = userId,
    ParticipantName = userId,
    Status = status,
    RegisteredAt = registeredAt
};

var registrations = new[]
{
    Registration(1, "pending", "Pending", now.AddMinutes(-8)),
    Registration(2, "accepted", "Accepted", now.AddMinutes(-7)),
    Registration(3, "awaiting", "Awaiting Payment", now.AddMinutes(-6)),
    Registration(4, "verification", "For Payment Verification", now.AddMinutes(-5)),
    Registration(5, "rejected", "Rejected", now.AddMinutes(-4)),
    Registration(6, "alternative", "Alternative Recommended", now.AddMinutes(-3)),
    Registration(7, "cancelled", "Cancelled", now.AddMinutes(-2)),
    Registration(8, "voided", "Voided", now.AddMinutes(-1)),
    Registration(9, "replaced", "Accepted", now.AddMinutes(-10)),
    Registration(10, "replaced", "Voided", now),
    Registration(11, "tie", "Pending", now.AddMinutes(-9)),
    Registration(12, "tie", "Awaiting Payment", now.AddMinutes(-9))
};

var rows = EventParticipantDisplaySelector.SelectCurrentRows(registrations);
Check(rows.Select(row => row.Status).SequenceEqual(["Awaiting Payment", "Pending", "Accepted", "Awaiting Payment", "For Payment Verification"]),
    "Only Pending, Accepted, Awaiting Payment, and For Payment Verification current rows must be displayed in stable RegisteredAt/Id order.");
Check(rows.All(row => row.Status is not "Rejected" and not "Alternative Recommended" and not "Cancelled" and not "Voided"),
    "Rejected, Alternative Recommended, Cancelled, and Voided current rows must be excluded.");
Check(!rows.Any(row => row.UserId == "replaced"),
    "A latest excluded row must not resurrect an older included row for the same participant.");
Check(rows.Single(row => row.UserId == "tie").Id == 12,
    "Equal RegisteredAt values must select the greatest registration Id deterministically.");

var capacityCount = registrations.Count(row => RegistrationStatusHelper.ActiveStatuses.Contains(row.Status));
var capacityWithoutCancelledOrVoided = registrations
    .Where(row => row.Status is not "Cancelled" and not "Voided")
    .Count(row => RegistrationStatusHelper.ActiveStatuses.Contains(row.Status));
Check(capacityCount == 7 && capacityCount == capacityWithoutCancelledOrVoided
      && !RegistrationStatusHelper.ActiveStatuses.Contains("Cancelled") && !RegistrationStatusHelper.ActiveStatuses.Contains("Voided"),
    "Cancelled and Voided registrations must not consume slots under the canonical active-status capacity policy.");
Check(!ProfileAccessPolicy.AllowsOrganizerRelationship("Voided")
      && ProfileAccessPolicy.AllowsOrganizerRelationship("Awaiting Payment"),
    "A Voided record must not grant organizer profile access, while included eligible relationships retain existing access policy.");

var statusRows = new Dictionary<string, EventParticipantRowViewModel>
{
    ["Accepted"] = new() { Status = "Accepted" },
    ["Pending"] = new() { Status = "Pending" },
    ["Awaiting Payment"] = new() { Status = "Awaiting Payment" },
    ["For Payment Verification"] = new() { Status = "For Payment Verification" }
};
Check(statusRows["Accepted"].StatusLabel == "Accepted"
      && statusRows["Pending"].StatusLabel == "Pending"
      && statusRows["Awaiting Payment"].StatusLabel == "Awaiting Payment"
      && statusRows["For Payment Verification"].StatusLabel == "Payment Verification",
    "All included statuses must have the expected presentation labels.");

Check(ProfileInitials.FromNames("Aira", "Bautista", null) == "AB"
      && ProfileInitials.FromNames("Benjie", "Alcantara", null) == "BA"
      && ProfileInitials.FromNames("Juanito", "Reyes", null) == "JR"
      && ProfileInitials.FromNames(null, null, "  Maria   De la Cruz ") == "MC"
      && ProfileInitials.FromNames(null, null, " Solo ") == "S"
      && ProfileInitials.FromNames(null, null, "   ") == "?",
    "Initials must safely use first and last names when present, otherwise derive from the displayed name.");

var peerRows = rows.Select(row => new ParticipantEventJoinedRowViewModel
{
    ParticipantName = row.ParticipantName,
    ProfilePictureUrl = row.User?.ProfilePictureUrl,
    Initials = ProfileInitials.FromNames(row.User?.FirstName, row.User?.LastName, row.ParticipantName)
}).ToList();
var peerRowProperties = typeof(ParticipantEventJoinedRowViewModel)
    .GetProperties()
    .Select(property => property.Name)
    .Order()
    .ToArray();
Check(peerRows.Count == rows.Count
      && peerRows.All(row => !string.IsNullOrWhiteSpace(row.ParticipantName))
      && peerRowProperties.SequenceEqual(["Initials", "ParticipantName", "ProfilePictureUrl"]),
    "The participant page projects every selected row into the minimal name, photo-reference, and initials peer model.");

Console.WriteLine($"TrailGuard Event Details participant verification passed ({assertions} assertions).");
