using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using NBitcoin;
using NBitcoin.Crypto;
using NBitcoin.DataEncoders;
using NBitcoin.Protocol;
using UnnamedCoin.Bitcoin.P2P.Protocol.Payloads;

namespace UnnamedCoin.Bitcoin.P2P.Protocol
{
    public class Message : IBitcoinSerializable
    {
        /// <summary>Size of the "command" part of the message in bytes.</summary>
        public const int CommandSize = 12;

        /// <summary>Size of the "length" part of the message in bytes.</summary>
        public const int LengthSize = 4;

        /// <summary>Size of the "checksum" part of the message in bytes, if it is present.</summary>
        public const int ChecksumSize = 4;

        /// <summary>A provider of network payload messages.</summary>
        readonly PayloadProvider payloadProvider;

        byte[] command = new byte[CommandSize];

        uint magic;

        Payload payloadObject;

        /// <summary>When parsing, maybe Magic is already parsed.</summary>
        bool skipMagic;

        public Message(PayloadProvider payloadProvider)
        {
            this.payloadProvider = payloadProvider;
        }

        public Message()
        {
        }

        public uint Magic
        {
            get => this.magic;
            set => this.magic = value;
        }

        public string Command
        {
            get => Encoders.ASCII.EncodeData(this.command);

            private set => this.command = Encoders.ASCII.DecodeData(value.Trim().PadRight(12, '\0'));
        }

        public Payload Payload
        {
            get => this.payloadObject;

            set
            {
                this.payloadObject = value;
                this.Command = this.payloadObject.Command;
            }
        }


        public void ReadWrite(BitcoinStream stream)
        {
            if (this.Payload == null && stream.Serializing)
                throw new InvalidOperationException("Payload not affected");

            if (stream.Serializing || !stream.Serializing && !this.skipMagic)
                stream.ReadWrite(ref this.magic);

            stream.ReadWrite(ref this.command);
            var length = 0;
            uint checksum = 0;
            var hasChecksum = false;
            var payloadBytes = stream.Serializing ? GetPayloadBytes(stream.ConsensusFactory, out length) : null;
            length = payloadBytes == null ? 0 : length;
            stream.ReadWrite(ref length);

            if (stream.ProtocolVersion >= ProtocolVersion.MEMPOOL_GD_VERSION)
            {
                if (stream.Serializing)
                    checksum = Hashes.Hash256(payloadBytes, 0, length).GetLow32();

                stream.ReadWrite(ref checksum);
                hasChecksum = true;
            }

            if (stream.Serializing)
            {
                stream.ReadWrite(ref payloadBytes, 0, length);
            }
            else
            {
                // MAX_SIZE 0x02000000 Serialize.h.
                if (length > 0x02000000)
                    throw new FormatException("Message payload too big ( > 0x02000000 bytes)");

                payloadBytes = new byte[length];
                stream.ReadWrite(ref payloadBytes, 0, length);

                if (hasChecksum)
                    if (!VerifyChecksum(checksum, payloadBytes, length))
                    {
                        if (NodeServerTrace.Trace.Switch.ShouldTrace(TraceEventType.Verbose))
                            NodeServerTrace.Trace.TraceEvent(TraceEventType.Verbose, 0,
                                "Invalid message checksum bytes");
                        throw new FormatException("Message checksum invalid");
                    }

                using (var ms = new MemoryStream(payloadBytes))
                {
                    var payloadStream = new BitcoinStream(ms, false)
                    {
                        ConsensusFactory = stream.ConsensusFactory
                    };

                    payloadStream.CopyParameters(stream);

                    var payloadType = this.payloadProvider.GetCommandType(this.Command);
                    var unknown = payloadType == typeof(UnknowPayload);
                    if (unknown)
                        NodeServerTrace.Trace.TraceEvent(TraceEventType.Warning, 0,
                            "Unknown command received : " + this.Command);

                    object payload = this.payloadObject;
                    payloadStream.ReadWrite(payloadType, ref payload);
                    if (unknown)
                        ((UnknowPayload) payload).UpdateCommand(this.Command);

                    this.Payload = (Payload) payload;
                }
            }
        }

        public bool IfPayloadIs<TPayload>(Action<TPayload> action) where TPayload : Payload
        {
            var payload = this.Payload as TPayload;
            if (payload != null)
                action(payload);
            return payload != null;
        }

        /// <summary>
        ///     Read the payload in to byte array.
        /// </summary>
        /// <param name="consensusFactory">The network consensus factory.</param>
        /// <param name="length">The length of the payload.</param>
        /// <returns>The payload in bytes.</returns>
        byte[] GetPayloadBytes(ConsensusFactory consensusFactory, out int length)
        {
            using (var ms = new MemoryStream())
            {
                var stream = new BitcoinStream(ms, true);
                stream.ConsensusFactory = consensusFactory;
                this.Payload.ReadWrite(stream);
                length = (int) ms.Position;
                return ms.ToArray();
            }
        }

        internal static bool VerifyChecksum(uint256 checksum, byte[] payload, int length)
        {
            return checksum == Hashes.Hash256(payload, 0, length).GetLow32();
        }


        public override string ToString()
        {
            return string.Format("{0}: {1}", this.Command, this.Payload);
        }

        public static Message ReadNext(Stream stream, Network network, ProtocolVersion version,
            CancellationToken cancellationToken, PayloadProvider payloadProvider, out PerformanceCounter counter)
        {
            var bitStream = new BitcoinStream(stream, false)
            {
                ProtocolVersion = version,
                ReadCancellationToken = cancellationToken,
                ConsensusFactory = network.Consensus.ConsensusFactory
            };

            if (!network.ReadMagic(stream, cancellationToken, true))
                throw new FormatException("Magic incorrect, the message comes from another network");

            var message = new Message(payloadProvider);
            using (message.SkipMagicScope(true))
            {
                message.Magic = network.Magic;
                message.ReadWrite(bitStream);
            }

            counter = bitStream.Counter;
            return message;
        }

        IDisposable SkipMagicScope(bool value)
        {
            var old = this.skipMagic;
            return new Scope(() => this.skipMagic = value, () => this.skipMagic = old);
        }
    }
}