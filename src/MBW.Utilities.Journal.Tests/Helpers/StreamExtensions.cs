using System.Text;

namespace MBW.Utilities.Journal.Tests.Helpers;

internal static class StreamExtensions
{
    public static void WriteStr(this Stream stream, string str) => stream.Write(Encoding.UTF8.GetBytes(str));

    public static string ReadStr(this Stream stream, int length)
    {
        byte[] buffer = new byte[length];
        stream.ReadExactly(buffer);
        return Encoding.UTF8.GetString(buffer);
    }

    public static string ReadFullStr(this Stream stream)
    {
        long pos = stream.Position;
        stream.Seek(0, SeekOrigin.Begin);
        string res = ReadStr(stream, (int)stream.Length);
        stream.Position = pos;
        return res;
    }

    public static byte[] ReadFullBytes(this Stream stream)
    {
        long pos = stream.Position;
        stream.Seek(0, SeekOrigin.Begin);
        byte[] buffer = new byte[stream.Length];
        stream.ReadExactly(buffer);
        stream.Position = pos;
        return buffer;
    }
}