using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Bencodex;
using Bencodex.Json;
using GraphQL;
using GraphQL.Types;
using Bencodex.Types;
using GraphQL.NewtonsoftJson;
using Lib9c;
using Libplanet.Blockchain;
using Libplanet.Types.Tx;
using Libplanet.Common;
using Libplanet.Types.Assets;
using Libplanet.Explorer.GraphTypes;
using Libplanet.Types.Blocks;
using Libplanet.Crypto;
using Libplanet.Store;
using Nekoyume.Action;

namespace NineChronicles.Headless.GraphTypes
{
    class TransactionHeadlessQuery : ObjectGraphType
    {
        public TransactionHeadlessQuery(StandaloneContext standaloneContext)
        {
            Field<NonNullGraphType<LongGraphType>>(
                name: "nextTxNonce",
                arguments: new QueryArguments(
                    new QueryArgument<NonNullGraphType<AddressType>> { Name = "address", Description = "Target address to query" }
                ),
                resolve: context =>
                {
                    if (!(standaloneContext.BlockChain is BlockChain blockChain))
                    {
                        throw new ExecutionError(
                            $"{nameof(StandaloneContext)}.{nameof(StandaloneContext.BlockChain)} was not set yet!");
                    }

                    Address address = context.GetArgument<Address>("address");
                    return blockChain.GetNextTxNonce(address);
                }
            );

            Field<TransactionType>(
                name: "getTx",
                arguments: new QueryArguments(
                    new QueryArgument<NonNullGraphType<TxIdType>>
                    { Name = "txId", Description = "transaction id." }
                ),
                resolve: context =>
                {
                    if (!(standaloneContext.BlockChain is BlockChain blockChain))
                    {
                        throw new ExecutionError(
                            $"{nameof(StandaloneContext)}.{nameof(StandaloneContext.BlockChain)} was not set yet!");
                    }

                    var txId = context.GetArgument<TxId>("txId");
                    return blockChain.GetTransaction(txId);
                }
            );


            Field<ListGraphType<TransactionType>>(
                name: "ncTransactions",
                arguments: new QueryArguments(
                    new QueryArgument<NonNullGraphType<LongGraphType>>
                    { Name = "startingBlockIndex", Description = "start block index for query tx." },
                    new QueryArgument<NonNullGraphType<LongGraphType>>
                    { Name = "limit", Description = "number of block to query." },
                    new QueryArgument<NonNullGraphType<StringGraphType>>
                    { Name = "actionType", Description = "filter tx by having actions' type. It is regular expression." }
                ),
                resolve: context =>
                {
                    if (standaloneContext.BlockChain is not { } blockChain)
                    {
                        throw new ExecutionError(
                            $"{nameof(StandaloneContext)}.{nameof(StandaloneContext.BlockChain)} was not set yet!");
                    }

                    var startingBlockIndex = context.GetArgument<long>("startingBlockIndex");
                    var limit = context.GetArgument<long>("limit");
                    var actionType = context.GetArgument<string>("actionType");

                    var blocks = ListBlocks(blockChain, startingBlockIndex, limit);
                    var transactions = blocks
                        .SelectMany(block => block.Transactions)
                        .Where(tx => tx.Actions.Any(rawAction =>
                        {
                            if (rawAction is not Dictionary action || action["type_id"] is not Text typeId)
                            {
                                return false;
                            }

                            return Regex.IsMatch(typeId, actionType);
                        }));

                    return transactions;
                }
            );

            Field<NonNullGraphType<StringGraphType>>(
                name: "createUnsignedTx",
                deprecationReason: "API update with action query. use unsignedTransaction",
                arguments: new QueryArguments(
                    new QueryArgument<NonNullGraphType<StringGraphType>>
                    {
                        Name = "publicKey",
                        Description = "The base64-encoded public key for Transaction.",
                    },
                    new QueryArgument<NonNullGraphType<StringGraphType>>
                    {
                        Name = "plainValue",
                        Description = "The base64-encoded plain value of action for Transaction.",
                    },
                    new QueryArgument<LongGraphType>
                    {
                        Name = "nonce",
                        Description = "The nonce for Transaction.",
                    }
                ),
                resolve: context =>
                {
                    if (!(standaloneContext.BlockChain is BlockChain blockChain))
                    {
                        throw new ExecutionError(
                            $"{nameof(StandaloneContext)}.{nameof(StandaloneContext.BlockChain)} was not set yet!");
                    }

                    string plainValueString = context.GetArgument<string>("plainValue");
                    var plainValue = new Bencodex.Codec().Decode(System.Convert.FromBase64String(plainValueString));
                    var action = NCActionUtils.ToAction(plainValue);

                    var publicKey = new PublicKey(Convert.FromBase64String(context.GetArgument<string>("publicKey")));
                    Address signer = publicKey.ToAddress();
                    long nonce = context.GetArgument<long?>("nonce") ?? blockChain.GetNextTxNonce(signer);
                    UnsignedTx unsignedTransaction =
                        new UnsignedTx(
                            new TxInvoice(
                                genesisHash: blockChain.Genesis.Hash,
                                actions: new TxActionList(new[] { action.PlainValue }),
                                gasLimit: action is ITransferAsset or ITransferAssets ? RequestPledge.DefaultRefillMead : 1L,
                                maxGasPrice: 1 * Currencies.Mead
                            ),
                            new TxSigningMetadata(publicKey: publicKey, nonce: nonce)
                        );
                    return Convert.ToBase64String(unsignedTransaction.SerializeUnsignedTx().ToArray());
                });

            Field<NonNullGraphType<StringGraphType>>(
                name: "attachSignature",
                deprecationReason: "Use signTransaction",
                arguments: new QueryArguments(
                    new QueryArgument<NonNullGraphType<StringGraphType>>
                    {
                        Name = "unsignedTransaction",
                        Description = "The base64-encoded unsigned transaction to attach the given signature."
                    },
                    new QueryArgument<NonNullGraphType<StringGraphType>>
                    {
                        Name = "signature",
                        Description = "The base64-encoded signature of the given unsigned transaction."
                    }
                ),
                resolve: context =>
                {
                    byte[] signature = Convert.FromBase64String(context.GetArgument<string>("signature"));
                    IUnsignedTx unsignedTransaction =
                        TxMarshaler.DeserializeUnsignedTx(
                            Convert.FromBase64String(context.GetArgument<string>("unsignedTransaction")));

                    Transaction signedTransaction = new Transaction(
                        unsignedTransaction,
                        signature.ToImmutableArray());

                    return Convert.ToBase64String(signedTransaction.Serialize());
                });

            Field<NonNullGraphType<TxResultType>>(
                name: "transactionResult",
                arguments: new QueryArguments(
                    new QueryArgument<NonNullGraphType<TxIdType>>
                    { Name = "txId", Description = "transaction id." }
                ),
                resolve: context =>
                {
                    if (!(standaloneContext.BlockChain is BlockChain blockChain))
                    {
                        throw new ExecutionError(
                            $"{nameof(StandaloneContext)}.{nameof(StandaloneContext.BlockChain)} was not set yet!");
                    }

                    if (!(standaloneContext.Store is IStore store))
                    {
                        throw new ExecutionError(
                            $"{nameof(StandaloneContext)}.{nameof(StandaloneContext.Store)} was not set yet!");
                    }

                    TxId txId = context.GetArgument<TxId>("txId");
                    if (!(store.GetFirstTxIdBlockHashIndex(txId) is { } txExecutedBlockHash))
                    {
                        return blockChain.GetStagedTransactionIds().Contains(txId)
                            ? new TxResult(TxStatus.STAGING, null, null, null, null, null)
                            : new TxResult(TxStatus.INVALID, null, null, null, null, null);
                    }

                    try
                    {
                        TxExecution execution = blockChain.GetTxExecution(txExecutedBlockHash, txId);
                        Block txExecutedBlock = blockChain[txExecutedBlockHash];
                        return new TxResult(
                            execution.Fail ? TxStatus.FAILURE : TxStatus.SUCCESS,
                            txExecutedBlock.Index,
                            txExecutedBlock.Hash.ToString(),
                            execution.InputState,
                            execution.OutputState,
                            execution.ExceptionNames);
                    }
                    catch (Exception)
                    {
                        return new TxResult(TxStatus.INVALID, null, null, null, null, null);
                    }
                }
            );

            Field<NonNullGraphType<ByteStringType>>(
                name: "unsignedTransaction",
                arguments: new QueryArguments(
                    new QueryArgument<NonNullGraphType<StringGraphType>>
                    {
                        Name = "publicKey",
                        Description = "The hexadecimal string of public key for Transaction.",
                    },
                    new QueryArgument<NonNullGraphType<StringGraphType>>
                    {
                        Name = "plainValue",
                        Description = "The hexadecimal string of plain value for Action.",
                    },
                    new QueryArgument<LongGraphType>
                    {
                        Name = "nonce",
                        Description = "The nonce for Transaction.",
                    },
                    new QueryArgument<FungibleAssetValueInputType>
                    {
                        Name = "maxGasPrice",
                        DefaultValue = 1 * Currencies.Mead
                    }
                ),
                resolve: context =>
                {
                    if (!(standaloneContext.BlockChain is BlockChain blockChain))
                    {
                        throw new ExecutionError(
                            $"{nameof(StandaloneContext)}.{nameof(StandaloneContext.BlockChain)} was not set yet!");
                    }

                    string plainValueString = context.GetArgument<string>("plainValue");
                    var plainValue = new Bencodex.Codec().Decode(ByteUtil.ParseHex(plainValueString));
                    var action = NCActionUtils.ToAction(plainValue);

                    var publicKey = new PublicKey(ByteUtil.ParseHex(context.GetArgument<string>("publicKey")));
                    Address signer = publicKey.ToAddress();
                    long nonce = context.GetArgument<long?>("nonce") ?? blockChain.GetNextTxNonce(signer);
                    long? gasLimit = action is ITransferAsset or ITransferAssets ? RequestPledge.DefaultRefillMead : 1L;
                    FungibleAssetValue? maxGasPrice = context.GetArgument<FungibleAssetValue?>("maxGasPrice");
                    UnsignedTx unsignedTransaction =
                        new UnsignedTx(
                            new TxInvoice(
                                genesisHash: blockChain.Genesis.Hash,
                                actions: new TxActionList(new[] { action.PlainValue }),
                                gasLimit: gasLimit,
                                maxGasPrice: maxGasPrice),
                            new TxSigningMetadata(publicKey, nonce));
                    return unsignedTransaction.SerializeUnsignedTx().ToArray();
                });

            Field<NonNullGraphType<ByteStringType>>(
                name: "signTransaction",
                arguments: new QueryArguments(
                    new QueryArgument<NonNullGraphType<StringGraphType>>
                    {
                        Name = "unsignedTransaction",
                        Description = "The hexadecimal string of unsigned transaction to attach the given signature."
                    },
                    new QueryArgument<NonNullGraphType<StringGraphType>>
                    {
                        Name = "signature",
                        Description = "The hexadecimal string of signature of the given unsigned transaction."
                    }
                ),
                resolve: context =>
                {
                    byte[] signature = ByteUtil.ParseHex(context.GetArgument<string>("signature"));
                    IUnsignedTx unsignedTransaction =
                        TxMarshaler.DeserializeUnsignedTx(
                            ByteUtil.ParseHex(context.GetArgument<string>("unsignedTransaction")));

                    Transaction signedTransaction =
                        new Transaction(unsignedTransaction, signature.ToImmutableArray());

                    return signedTransaction.Serialize();
                }
            );
        }

        private IEnumerable<Block> ListBlocks(BlockChain chain, long from, long limit)
        {
            if (chain.Tip.Index < from)
            {
                return new List<Block>();
            }

            var count = (int)Math.Min(limit, chain.Tip.Index - from);
            var blocks = Enumerable.Range(0, count)
                .ToList()
                .AsParallel()
                .Select(offset => chain[from + offset])
                .OrderBy(block => block.Index);

            return blocks;
        }
    }
}
