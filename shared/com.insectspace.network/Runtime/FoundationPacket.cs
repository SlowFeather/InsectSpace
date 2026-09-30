using System;
using System.IO;
using System.Text;
using Framework;
using Framework.Network;
using InsectSpace.Contracts;

namespace InsectSpace.Network
{
    // Diagnostics only. Reserve real login/world/battle message IDs in the schema.
    public sealed class FoundationPacket : Packet
    {
        public override int Id => 1;
        public string Message { get; set; } = string.Empty;
        public override void Clear() { Message = string.Empty; }
        public static FoundationPacket Create(string message)
        {
            var packet = ReferencePool.Acquire<FoundationPacket>();
            packet.Message = message;
            return packet;
        }
    }

    public sealed class FoundationPacketCodec : INetworkPacketCodec
    {
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        public int MaxPacketSize => ProtocolVersion.MaxPacketBytes;
        public bool Encode<T>(T packet, out byte[] payload) where T : Packet
        {
            payload = null;
            if (!(packet is FoundationPacket message) || message.Message == null) return false;
            byte[] text;
            try { text = Utf8.GetBytes(message.Message); }
            catch (EncoderFallbackException) { return false; }
            if (text.Length > MaxPacketSize - 12) return false;
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Utf8))
            {
                writer.Write(ProtocolVersion.Current);
                writer.Write(message.Id);
                writer.Write(text.Length);
                writer.Write(text);
                payload = stream.ToArray();
            }
            return true;
        }

        public Packet Decode(byte[] payload, int offset, int count, out object customErrorData)
        {
            customErrorData = null;
            if (payload == null || offset < 0 || count < 12 || count > MaxPacketSize || offset > payload.Length - count)
            {
                customErrorData = "Invalid packet bounds.";
                return null;
            }
            try
            {
                using (var stream = new MemoryStream(payload, offset, count, false))
                using (var reader = new BinaryReader(stream, Utf8))
                {
                    if (reader.ReadInt32() != ProtocolVersion.Current || reader.ReadInt32() != 1)
                        throw new InvalidDataException("Protocol or message ID mismatch.");
                    int length = reader.ReadInt32();
                    if (length < 0 || length != count - 12) throw new InvalidDataException("Invalid payload length.");
                    return FoundationPacket.Create(Utf8.GetString(reader.ReadBytes(length)));
                }
            }
            catch (Exception error) when (error is IOException || error is InvalidDataException || error is ArgumentException)
            {
                customErrorData = "Malformed foundation packet.";
                return null;
            }
        }
    }
}
