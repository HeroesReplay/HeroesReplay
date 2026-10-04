using System;
using System.IO;
using System.Text;

namespace HeroesReplay.CLI.Commands.Services;

/// <summary>Copies what <see cref="Console.Out"/> and <see cref="Console.Error"/> print while it is open.</summary>
public sealed class ConsoleCapture : IDisposable
{
    private readonly TextWriter output;
    private readonly TextWriter error;
    private readonly StringWriter copy = new();

    public ConsoleCapture()
    {
        output = Console.Out;
        error = Console.Error;
        Console.SetOut(new Tee(output, copy));
        Console.SetError(new Tee(error, copy));
    }

    public string Text
    {
        get
        {
            lock (copy)
            {
                return copy.ToString();
            }
        }
    }

    public void Dispose()
    {
        Console.SetOut(output);
        Console.SetError(error);
    }

    private sealed class Tee(TextWriter shown, StringWriter kept) : TextWriter
    {
        public override Encoding Encoding => shown.Encoding;

        public override void Write(char value)
        {
            shown.Write(value);
            lock (kept)
            {
                kept.Write(value);
            }
        }

        public override void Write(string value)
        {
            shown.Write(value);
            lock (kept)
            {
                kept.Write(value);
            }
        }

        public override void Write(char[] buffer, int index, int count)
        {
            shown.Write(buffer, index, count);
            lock (kept)
            {
                kept.Write(buffer, index, count);
            }
        }

        public override void Flush() => shown.Flush();
    }
}
