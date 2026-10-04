using System.Buffers.Binary;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace ServerManager.App;

// Integration check of the AC client protocol, separate from HTTP readiness.
internal static class HandshakeQA
{
    internal static async Task<byte[]> Request(int port, string model)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var payload=new MemoryStream();
        using var writer=new BinaryWriter(payload, Encoding.UTF8, leaveOpen:true);
        void Utf8(string value)
        {
            var bytes=Encoding.UTF8.GetBytes(value);
            writer.Write(checked((byte)bytes.Length)); writer.Write(bytes);
        }
        writer.Write((byte)0x3d); // RequestNewConnection
        writer.Write((ushort)202);
        Utf8("76561198000000000");
        const string name="ACSM handshake QA";
        writer.Write((byte)name.Length); writer.Write(Encoding.UTF32.GetBytes(name));
        Utf8(""); Utf8("GB"); Utf8(model); Utf8("");
        var features=Encoding.UTF8.GetBytes("WEATHERFX_V1,SPECTATING_AWARE,LOWER_CLIENTS_SENDING_RATE,EMOJI");
        writer.Write((short)features.Length); writer.Write(features); writer.Flush();
        using var frame=new MemoryStream();
        using (var framing=new BinaryWriter(frame, Encoding.UTF8, leaveOpen:true))
        { framing.Write(checked((ushort)payload.Length)); framing.Write(payload.ToArray()); }
        using var client=new TcpClient();
        await client.ConnectAsync("127.0.0.1",port,timeout.Token);
        await using var stream=client.GetStream();
        await stream.WriteAsync(frame.ToArray(),timeout.Token);
        var header=new byte[2]; await stream.ReadExactlyAsync(header,timeout.Token);
        var size=BinaryPrimitives.ReadUInt16LittleEndian(header);
        if(size==0) throw new InvalidDataException("Empty handshake response.");
        var response=new byte[size]; await stream.ReadExactlyAsync(response,timeout.Token);
        return response;
    }
}
