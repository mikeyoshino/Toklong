namespace Toklong.Application.Abstractions;

public sealed record RegistrationTicketPair(
    string RawTicket,
    string TicketHash);

public interface IRegistrationTicketService
{
    RegistrationTicketPair Issue();

    string Hash(string rawTicket);
}
