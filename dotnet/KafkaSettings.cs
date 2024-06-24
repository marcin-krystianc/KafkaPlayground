
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Spectre.Console;
using Spectre.Console.Cli;

namespace KafkaTool;

public class KafkaSettings : CommandSettings
{
    [CommandOption("--ini-file")]
    public string IniFile { get; set; }

    [CommandOption("--config <VALUE>")]
    [Description("Extre configuration key=value. Can be speciified mutliple times.")]
    [TypeConverter(typeof(TupleTypeConverter))]
    public Tuple<string, string>[] ConfigItems { get; set; }

    public IDictionary<string, string> ConfigDictionary =>
        ConfigItems?.ToDictionary(x => x.Item1, x => x.Item2) ?? new Dictionary<string, string>();

    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(IniFile))
        {
            return ValidationResult.Error("IniFile is required");
        }

        return base.Validate();
    }
}

// Use a custom TypeConverter to extract the data to a tuple
public sealed class TupleTypeConverter : TypeConverter
{
    public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
    {
        if (value is string stringValue)
        {
            var parts = stringValue.Split(new[] { '=' });
            if (parts.Length != 2)
            {
                throw new InvalidOperationException("Not a tuple!");
            }
            return Tuple.Create(parts[0], parts[1]);
        }
        throw new InvalidOperationException("Expected string");
    }
}
