using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace KafkaTool;

public class KafkaSettings : CommandSettings
{
    [CommandOption("--ini-file")]
    public string IniFile { get; set; }
    
    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(IniFile))
        {
            return ValidationResult.Error("IniFile is required");
        }

        return base.Validate();
    }
}