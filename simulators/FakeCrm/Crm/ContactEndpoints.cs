using FakeCrm.State;
using FakeCrm.Testing;

namespace FakeCrm.Crm;

public sealed record ContactRequest(string? FirstName, string? LastName, string? Email, string? Phone);

public sealed record ContactResponse(
    string CandidateId,
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    ContactStatus Status,
    DateTimeOffset UpdatedAt)
{
    public static ContactResponse From(string candidateId, Contact contact) =>
        new(candidateId, contact.FirstName, contact.LastName, contact.Email, contact.Phone, contact.Status, contact.UpdatedAt);
}

/// <summary>The fake CRM API that Sync.Worker calls. Keyed by candidate id, so a repeated call is harmless.</summary>
public static class ContactEndpoints
{
    public static void MapContacts(this IEndpointRouteBuilder app)
    {
        var contacts = app.MapGroup("/contacts").AddEndpointFilter(AnomalyEndpoints.ApplyToApi);

        contacts.MapPut("/{candidateId}", Upsert);
        contacts.MapDelete("/{candidateId}", Delete);
    }

    // 201 on create and 200 on update, so a demo shows which one a redelivered event hit; the client must
    // treat both as success. A PUT on a deleted contact makes it Active again: the fake has no opinion on
    // reactivation, and Sync.Worker only PUTs a candidate that Teamtailor still returns.
    private static IResult Upsert(string candidateId, ContactRequest request, Store store, TimeProvider timeProvider)
    {
        var missing = new[]
            {
                (Name: "firstName", Value: request.FirstName),
                (Name: "lastName", Value: request.LastName),
                (Name: "email", Value: request.Email),
            }
            .Where(field => string.IsNullOrWhiteSpace(field.Value))
            .Select(field => field.Name)
            .ToArray();

        if (missing.Length > 0)
        {
            return Results.Problem($"Missing {string.Join(", ", missing)}", statusCode: StatusCodes.Status400BadRequest);
        }

        var contact = new Contact(
            request.FirstName!, request.LastName!, request.Email!, request.Phone, ContactStatus.Active, timeProvider.GetUtcNow());
        var created = true;
        store.Contacts.AddOrUpdate(candidateId, contact, (_, _) =>
        {
            created = false;
            return contact;
        });

        var response = ContactResponse.From(candidateId, contact);
        return created ? Results.Created($"/contacts/{candidateId}", response) : Results.Ok(response);
    }

    // Soft delete (D1): the CRM owns relationship data on the contact, so the record stays.
    private static IResult Delete(string candidateId, Store store, TimeProvider timeProvider)
    {
        if (!store.Contacts.TryGetValue(candidateId, out var contact))
        {
            return Results.NotFound();
        }

        if (contact.Status != ContactStatus.Deleted)
        {
            store.Contacts[candidateId] = contact with { Status = ContactStatus.Deleted, UpdatedAt = timeProvider.GetUtcNow() };
        }

        return Results.NoContent();
    }
}
