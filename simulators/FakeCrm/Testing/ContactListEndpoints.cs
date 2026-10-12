using FakeCrm.Crm;
using FakeCrm.State;

namespace FakeCrm.Testing;

/// <summary>What tests and the demo read to see the end result. Not counted in <c>/calls</c> and never broken by anomalies.</summary>
public static class ContactListEndpoints
{
    public static void MapContactList(this IEndpointRouteBuilder app) =>
        app.MapGet("/contacts", (Store store) =>
            store.Contacts
                .OrderBy(contact => contact.Key)
                .Select(contact => ContactResponse.From(contact.Key, contact.Value)));
}
