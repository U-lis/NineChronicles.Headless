using System;
using GraphQL.Types;
using Libplanet.Crypto;
using Libplanet.Explorer.GraphTypes;
using Nekoyume.Model.Arena;

namespace NineChronicles.Headless.GraphTypes.States;

public class ArenaInformationType : ObjectGraphType<(Address, ArenaInformation?, ArenaScore?)>
{
    public ArenaInformationType()
    {
        Field<NonNullGraphType<AddressType>>(
            name: "avatarAddress",
            resolve: context => context.Source.Item1
        );
        Field<AddressType>(
            nameof(ArenaInformation.Address),
            resolve: context => context.Source.Item2?.Address
        );
        Field<IntGraphType>(
            nameof(ArenaInformation.Win),
            resolve: context => context.Source.Item2?.Win
        );
        Field<IntGraphType>(
            nameof(ArenaInformation.Lose),
            resolve: context => context.Source.Item2?.Lose
        );
        Field<IntGraphType>(
            nameof(ArenaInformation.Ticket),
            resolve: context => context.Source.Item2?.Ticket
        );
        Field<IntGraphType>(
            nameof(ArenaInformation.TicketResetCount),
            resolve: context => context.Source.Item2?.TicketResetCount
        );
        Field<IntGraphType>(
            nameof(ArenaInformation.PurchasedTicketCount),
            resolve: context => context.Source.Item2?.PurchasedTicketCount
        );
        Field<IntGraphType>(
            name: "score",
            resolve: context => context.Source.Item3?.Score
        );
    }
}
