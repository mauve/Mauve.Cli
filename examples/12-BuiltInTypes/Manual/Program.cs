using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using Mauve.Cli.Conversion;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;

namespace BuiltInTypes.Manual;

internal static class Program
{
    private static int Main(string[] args)
    {
        ConverterRegistry enums = new ConverterRegistryBuilder()
            .RegisterEnum<DisplayMode>(
            [
                new("Compact", DisplayMode.Compact),
                new("Detailed", DisplayMode.Detailed)
            ])
            .Build();
        CommandModel model = CommandModel.Create(new CommandBuilder("built-ins")
        {
            Description = "Inspect built-in conversions without I/O."
        }
            .AddConverters(enums)
            .RegisterUnion<PortOrName>(union => union
                .Case<int>(static value => new PortOrName(value))
                .Case<string>(static value => new PortOrName(value)))
            .AddChild(CreateNumbers())
            .AddChild(CreateIdentity())
            .AddChild(CreateTime())
            .AddChild(CreateShapes()),
            includeStandardHelp: true);
        return CliInvoker.Run(model, args);
    }

    private static CommandBuilder CreateNumbers()
    {
        CommandBuilder command = new("numbers")
        {
            Description = "Parse invariant numeric values and a boolean."
        };
        List<Action<CommandInvocationContext>> outputs = [];
        Add<sbyte>(command, outputs, "--signed-byte");
        Add<byte>(command, outputs, "--unsigned-byte");
        Add<short>(command, outputs, "--signed-short");
        Add<ushort>(command, outputs, "--unsigned-short");
        Add<int>(command, outputs, "--signed-int");
        Add<uint>(command, outputs, "--unsigned-int");
        Add<long>(command, outputs, "--signed-long");
        Add<ulong>(command, outputs, "--unsigned-long");
        Add<Half>(command, outputs, "--half");
        Add<float>(command, outputs, "--single");
        Add<double>(command, outputs, "--double");
        Add<decimal>(command, outputs, "--decimal");
        Add<bool>(command, outputs, "--enabled", arity: OptionArity.BooleanFlag, isRequired: false);
        return WithOutputs(command, outputs);
    }

    private static CommandBuilder CreateIdentity()
    {
        CommandBuilder command = new("identity")
        {
            Description = "Parse text, identifiers, addresses, lexical paths and an enum."
        };
        List<Action<CommandInvocationContext>> outputs = [];
        Add<string>(command, outputs, "--text");
        Add<char>(command, outputs, "--letter");
        Add<Guid>(command, outputs, "--id");
        Add<Uri>(command, outputs, "--uri");
        Add<Version>(command, outputs, "--version");
        Add<IPAddress>(command, outputs, "--address");
        Add<FileInfo>(command, outputs, "--file", format: static value => value.Name);
        Add<DirectoryInfo>(command, outputs, "--directory", format: static value => value.Name);
        Add<DisplayMode>(command, outputs, "--mode");
        return WithOutputs(command, outputs);
    }

    private static CommandBuilder CreateTime()
    {
        CommandBuilder command = new("time")
        {
            Description = "Parse exact date, time, offset and duration formats."
        };
        List<Action<CommandInvocationContext>> outputs = [];
        Add<DateOnly>(command, outputs, "--date",
            format: static value => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add<TimeOnly>(command, outputs, "--time",
            format: static value => value.ToString("HH:mm:ss.fffffff", CultureInfo.InvariantCulture));
        Add<DateTimeOffset>(command, outputs, "--timestamp",
            format: static value => value.ToString("O", CultureInfo.InvariantCulture));
        Add<TimeSpan>(command, outputs, "--duration",
            format: static value => value.ToString("c", CultureInfo.InvariantCulture));
        return WithOutputs(command, outputs);
    }

    private static CommandBuilder CreateShapes()
    {
        OptionDefinition maybeDate = new("--maybe-date", typeof(DateOnly?), OptionArity.OptionalScalar,
            allowsNull: true, bareValuePolicy: BareValuePolicy.Null);
        OptionDefinition samples = new("--sample", typeof(IReadOnlyList<Half>),
            OptionArity.RepeatedSingleCollection, elementType: typeof(Half), isRequired: true);
        OptionDefinition target = new("--target", typeof(PortOrName), OptionArity.RequiredScalar, isRequired: true);
        OptionDefinition routes = new("--route", typeof(IReadOnlyList<PortOrName>),
            OptionArity.RepeatedSingleCollection, elementType: typeof(PortOrName), isRequired: true);
        return new CommandBuilder("shapes")
        {
            Description = "Parse nullable, repeated and ordered union values."
        }
            .AddOption(maybeDate)
            .AddOption(samples)
            .AddOption(target)
            .AddOption(routes)
            .SetHandler((context, _) =>
            {
                string date = context.TryGetOptionValue(maybeDate, out DateOnly? value)
                    ? value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "(null)"
                    : "(absent)";
                Print("maybe-date", date);
                foreach (Half sample in Read<IReadOnlyList<Half>>(context, samples))
                {
                    Print("sample", sample);
                }

                Print("target", Describe(Read<PortOrName>(context, target)));
                foreach (PortOrName route in Read<IReadOnlyList<PortOrName>>(context, routes))
                {
                    Print("route", Describe(route));
                }

                return 0;
            });
    }

    private static void Add<T>(
        CommandBuilder command,
        List<Action<CommandInvocationContext>> outputs,
        string name,
        OptionArity arity = OptionArity.RequiredScalar,
        bool isRequired = true,
        Func<T, string>? format = null)
    {
        OptionDefinition option = new(name, typeof(T), arity, isRequired: isRequired);
        _ = command.AddOption(option);
        outputs.Add(context =>
        {
            T value = Read<T>(context, option);
            if (format is null)
            {
                Print(name[2..], value);
            }
            else
            {
                Print(name[2..], format(value));
            }
        });
    }

    private static CommandBuilder WithOutputs(
        CommandBuilder command,
        List<Action<CommandInvocationContext>> outputs)
    {
        return command.SetHandler((context, _) =>
        {
            foreach (Action<CommandInvocationContext> output in outputs)
            {
                output(context);
            }

            return 0;
        });
    }

    private static T Read<T>(CommandInvocationContext context, OptionDefinition option)
        => context.TryGetOptionValue(option, out T? value) && value is not null
            ? value : throw new InvalidOperationException("The required option is unavailable.");

    private static void Print<T>(string name, T value)
        => Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{name}={value}"));

    private static string Describe(PortOrName value) => value.Value switch
    {
        int port => string.Create(CultureInfo.InvariantCulture, $"port:{port}"),
        string name => $"name:{name}",
        _ => throw new InvalidOperationException("The union case is unavailable.")
    };
}

internal enum DisplayMode
{
    Compact,
    Detailed
}

#if NET11_0_OR_GREATER
internal readonly union PortOrName(int, string);
#endif
