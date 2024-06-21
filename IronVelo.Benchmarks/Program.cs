using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;

namespace Benchmarks;

using IronVelo.Base64;
using IronVelo.Types;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

public class BenchmarksConfigSer : ManualConfig
{
    public BenchmarksConfigSer()
    {
        AddDiagnoser(MemoryDiagnoser.Default);
        AddLogger(ConsoleLogger.Default);
        AddColumn(
            TargetMethodColumn.Method, StatisticColumn.Median, StatisticColumn.StdDev,
            StatisticColumn.Q1, StatisticColumn.Q3
        );
    }
}

[Config(typeof(BenchmarksConfigSer))]
public class Base64DecodeBenchmarks
{
    private const string ToDecode = "aGhoaGhoaGhoaGhoaGhoaGhoaGhoaGhoaGhoaGhoaGhoaGhoaGhoaGhlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVra2tra2tra2tra2tra2tra2VhZGZsa2FzZGZrYWhzZGZrbGphc2hkZmtqYXNoZGZrbGFqc2hkZmtsamFzaGRma2xhc2hkZmxrYXNoZmtsYWpzZGhma2xhanNoZGZrbGFzamRma2xhc2pkZmtsYXNqZGhma2xhc2hkZmtsYWpzaGZrbGFzZGZrbA";
    private const string ToDecodePadded = $"{ToDecode}==";

    [Benchmark]
    public byte[] Base64DecodeNoPadCt()
    {
        return Base64.DecodeCt(ToDecode);
    }

    [Benchmark]
    public byte[] Base64DecodeNoPad()
    {
        return Base64.Decode(ToDecode);
    }

    [Benchmark]
    public byte[] BuiltInBase64Decode()
    {
        return Convert.FromBase64String(ToDecodePadded);
    }
}

[Config(typeof(BenchmarksConfigSer))]
public class Base64EncodeBenchmarks
{
    private static readonly byte[] ToEncode =
        "KLJALKJADSLFKJLKlkjaldkjflkadfjl"u8.ToArray();

    [Benchmark]
    public string Base64EncodeCt()
    {
        return Base64.EncodeCt(ToEncode);
    }
}

[Config(typeof(BenchmarksConfigSer))]
public class PasswordBenchmarks
{
    private const string InvalidLongPasswordD = "paaaaaaaaaaaaaaasssswwwwwwwwwwwooorrdd111234";
    private const string InvalidShortPasswordD = "password";
    private const string ValidLongPasswordD = "Passswwooooooooorrrrrrrrrddddd!23412A";
    private const string ValidShortPasswordD = "Abc123As!";

    [Benchmark]
    public Result<Password, PasswordInvalid> InvalidLongPassword()
    {
        return Password.From(InvalidLongPasswordD);
    }

    [Benchmark]
    public Result<Password, PasswordInvalid> InvalidShortPassword()
    {
        return Password.From(InvalidShortPasswordD);
    }

    [Benchmark]
    public Result<Password, PasswordInvalid> ValidLongPassword()
    {
        return Password.From(ValidLongPasswordD);
    }

    [Benchmark]
    public Result<Password, PasswordInvalid> ValidShortPassword()
    {
        return Password.From(ValidShortPasswordD);
    }
}

[Config(typeof(BenchmarksConfigSer))]
public class OtpBenchmarks
{
    private const string ValidSimpleOtpD = "123456";
    private const string InvalidSimpleOtpD = "12345a";
    private const string ValidTotpD = "12345678";
    private const string InvalidTotpD = "1234567a";

    [Benchmark]
    public Result<SimpleOtp, InvalidOtp> ValidSimpleOtp()
    {
        return SimpleOtp.From(ValidSimpleOtpD);
    }

    [Benchmark]
    public Result<SimpleOtp, InvalidOtp> InvalidSimpleOtp()
    {
        return SimpleOtp.From(InvalidSimpleOtpD);
    }

    [Benchmark]
    public Result<Totp, InvalidOtp> ValidTotp()
    {
        return Totp.From(ValidTotpD);
    }

    [Benchmark]
    public Result<Totp, InvalidOtp> InvalidTotp()
    {
        return Totp.From(InvalidTotpD);
    }
}

public abstract record NumValidator : OtpValidator<uint>
{
    public static Result<uint, InvalidOtp> From(string raw)
    {
        return IsNumeric(raw, () => 7);
    }
}

[Config(typeof(BenchmarksConfigSer))]
public class IsNumericBenchmarks
{
    private const string ValidD =
        "11111111111111222222223123187398712394871290387129038419028391028349012874091283490128340912874091283";

    private const string InvalidD =
        "1111111111111122222222312318739871239487129038712903841902839102834901287409128349012834091287409128a";

    [Benchmark]
    public Result<uint, InvalidOtp> Valid()
    {
        return NumValidator.From(ValidD);
    }

    [Benchmark]
    public Result<uint, InvalidOtp> Invalid()
    {
        return NumValidator.From(InvalidD);
    }
}

public enum Benches
{
    B64Decode,
    B64Encode,
    Password,
    Otp,
    IsNumeric
}

public enum EOptionIdent
{
    Help,
    Bench,
    List
}

public class UnknownOption : Exception
{
    public UnknownOption(string? message) : base(message) {}
}

public class MalformedArgs : Exception
{
    public MalformedArgs(string? message) : base(message) {}
}

public class MissingArg : Exception
{
    public MissingArg(string? message) : base(message) {}
}

public class BenchNotFound : Exception
{
    public BenchNotFound(string? message) : base(message) {}
}

public record OptionIdent
{
    public readonly EOptionIdent Ident;

    public OptionIdent(string raw)
    {
        if (raw.StartsWith("--"))
        {
            Ident = ParseLong(raw[2..]);
        } 
        else if (raw.StartsWith('-'))
        {
            Ident = ParseShort(raw[1]);
        }
        else
        {
            throw new MalformedArgs("An argument either starts with `-` for short or `--` for long.");
        }
    }

    private static EOptionIdent ParseShort(char ident)
    {
        return ident switch
        {
            'h' => EOptionIdent.Help,
            'b' => EOptionIdent.Bench,
            'l' => EOptionIdent.List,
            _ => throw new UnknownOption(
                $"Unknown short option provided {ident}. " +
                $"Available: [-h (help), -b (bench), -l (list benches)]"
            )
        };
    }

    private static EOptionIdent ParseLong(string ident)
    {
        return ident switch
        {
            "help" => EOptionIdent.Help,
            "bench" => EOptionIdent.Bench,
            "list" => EOptionIdent.List,
            _ => throw new UnknownOption(
                $"Unknown option provided {ident}. " +
                $"Available: [--help, --bench, --list]"
            )
        };
    }
}

public record Option(OptionIdent Ident, string? Arg = null)
{
    public Benches? CheckHandle()
    {
        switch (Ident.Ident)
        {
            case EOptionIdent.Bench:
                return HandleBench();
            case EOptionIdent.Help:
                WriteHelp();
                break;
            case EOptionIdent.List:
                ListBench();
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        return null;
    }

    private Benches HandleBench()
    {
        if (Arg == null)
        {
            throw new MissingArg("You must select a benchmark to run");
        }
        if (!Enum.TryParse(Arg, true, out Benches bench))
            throw new BenchNotFound(
                $"No bench found with name: {Arg}. To see the available benches use --list");
                
        return bench; 
    }

    private static void IndentedLn(string msg)
    {
        Console.WriteLine($"    {msg}");
    }

    private static void WriteOpt(char s, string l, string msg, string? opt = null, int pad = 0)
    {
        var oFmt = opt == null ? "" : $"<{opt}> ";
        IndentedLn($"-{s}, --{l} {oFmt.PadRight(pad)} {msg}");
    }

    internal static void WriteHelp()
    {
        Console.WriteLine("IronVelo Benchmarks\n");
        Console.WriteLine("Usage: Benchmarks[EXE] --bench <BENCH NAME>\n");
        Console.WriteLine("Options:");
        WriteOpt('h', "help", "Print Help", pad: 14);
        WriteOpt('l', "list", "List the available benchmarks", pad: 14);
        WriteOpt('b', "bench", "Run one of the benchmarks", opt: "BENCH NAME");
        Console.WriteLine();
    }

    private static void ListBench()
    {
        Console.WriteLine("Available Benchmarks:");
        foreach (Benches option in Enum.GetValues(typeof(Benches)))
        {
            IndentedLn(option.ToString());
        }
        Console.WriteLine();
    }
}

public class Handler 
{
    public Handler(string[] args)
    {
        if (args.Length == 0)
        {
            Option.WriteHelp();
            return;
        }
        var options = ParseOptions(args);
        var benches = new List<Benches>();

        foreach (var option in options)
        {
            if (option.CheckHandle() is { } bench)
            {
                benches.Add(bench);
            }
        }

        foreach (var _ in benches.Select(RunBench)) { }
    }

    private static List<Option> ParseOptions(string[] args)
    {
        OptionIdent? last = null;
        var options = new List<Option>();
        
        foreach (var arg in args)
        {
            if (last == null)
            {
                last = new OptionIdent(arg);
            }
            else
            {
                try
                {
                    // if we can parse this w/o MalformedArgs then the prev option was not provided an argument
                    var cur = new OptionIdent(arg); 
                    options.AddRange(new Option[] { new(last), new(cur) });
                }
                catch (MalformedArgs)
                {
                    options.Add(new Option(last, arg));
                }

                last = null;
            }
        }
        
        if (last != null) { options.Add(new Option(last) );}

        return options;
    }

    private static Summary RunBench(Benches bench)
    {
        return bench switch
        {
            Benches.B64Decode => BenchmarkRunner.Run<Base64DecodeBenchmarks>(),
            Benches.B64Encode => BenchmarkRunner.Run<Base64EncodeBenchmarks>(),
            Benches.Password => BenchmarkRunner.Run<PasswordBenchmarks>(),
            Benches.Otp => BenchmarkRunner.Run<OtpBenchmarks>(),
            Benches.IsNumeric => BenchmarkRunner.Run<IsNumericBenchmarks>(),
            _ => throw new ArgumentOutOfRangeException(nameof(bench), bench, null)
        };
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        try
        {
            var _ = new Handler(args);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[ERROR]: {e.Message}");
        }
    }
}