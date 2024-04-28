using System.Diagnostics;

using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers.Exceptions;

using Refitter.Core;
using Refitter.Validation;

using Spectre.Console;
using Spectre.Console.Cli;

namespace Refitter;

public sealed class GenerateCommand : AsyncCommand<Settings>
{
    private static readonly string Crlf = Environment.NewLine;

    public override ValidationResult Validate(CommandContext context, Settings settings)
    {
        if (!settings.NoLogging)
            Analytics.Configure();

        return SettingsValidator.Validate(settings);
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var refitGeneratorSettings = new RefitGeneratorSettings
        {
            OpenApiPath = settings.OpenApiPath!,
            Namespace = settings.Namespace ?? "GeneratedCode",
            AddAutoGeneratedHeader = !settings.NoAutoGeneratedHeader,
            AddAcceptHeaders = !settings.NoAcceptHeaders,
            GenerateContracts = !settings.InterfaceOnly,
            ReturnIApiResponse = settings.ReturnIApiResponse,
            ReturnIObservable = settings.ReturnIObservable,
            UseCancellationTokens = settings.UseCancellationTokens,
            GenerateOperationHeaders = !settings.NoOperationHeaders,
            UseIsoDateFormat = settings.UseIsoDateFormat,
            TypeAccessibility = settings.InternalTypeAccessibility
                ? TypeAccessibility.Internal
                : TypeAccessibility.Public,
            AdditionalNamespaces = settings.AdditionalNamespaces!,
            ExcludeNamespaces = settings.ExcludeNamespaces ?? Array.Empty<string>(),
            MultipleInterfaces = settings.MultipleInterfaces,
            IncludePathMatches = settings.MatchPaths ?? Array.Empty<string>(),
            IncludeTags = settings.Tags ?? Array.Empty<string>(),
            GenerateDeprecatedOperations = !settings.NoDeprecatedOperations,
            OperationNameTemplate = settings.OperationNameTemplate,
            OptionalParameters = settings.OptionalNullableParameters,
            TrimUnusedSchema = settings.TrimUnusedSchema,
            KeepSchemaPatterns = settings.KeepSchemaPatterns ?? Array.Empty<string>(),
            OperationNameGenerator = settings.OperationNameGenerator,
            GenerateDefaultAdditionalProperties = !settings.SkipDefaultAdditionalProperties,
        };

        try
        {
            var stopwatch = Stopwatch.StartNew();
            AnsiConsole.MarkupLine($"[green]Refitter v{GetType().Assembly.GetName().Version!}[/]");
            AnsiConsole.MarkupLine(
                settings.NoLogging
                    ? "[green]Support key: Unavailable when logging is disabled[/]"
                    : $"[green]Support key: {SupportInformation.GetSupportKey()}[/]");

            if (!string.IsNullOrWhiteSpace(settings.SettingsFilePath))
            {
                var json = await File.ReadAllTextAsync(settings.SettingsFilePath);
                refitGeneratorSettings = Serializer.Deserialize<RefitGeneratorSettings>(json);
                refitGeneratorSettings.OpenApiPath = settings.OpenApiPath!;
            }

            var generator = await RefitGenerator.CreateAsync(refitGeneratorSettings);
            if (!settings.SkipValidation)
                await ValidateOpenApiSpec(refitGeneratorSettings.OpenApiPath);

            var code = generator.Generate().ReplaceLineEndings();
            AnsiConsole.MarkupLine($"[green]Length: {code.Length} bytes[/]");

            var outputPath = GetOutputPath(settings, refitGeneratorSettings);
            AnsiConsole.MarkupLine($"[green]Output: {Path.GetFullPath(outputPath)}[/]");

            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            await File.WriteAllTextAsync(outputPath, code);

            if(refitGeneratorSettings.SplitContracts)
            {
                refitGeneratorSettings.GenerateContracts = true;
                refitGeneratorSettings.GenerateInterface = false;
                refitGeneratorSettings.GenerateDependencyInjection = false;
                generator = await RefitGenerator.CreateAsync(refitGeneratorSettings);
                var contractCode = generator.Generate().ReplaceLineEndings();
                var contractOutputPath = GetContractOutputPath(settings, refitGeneratorSettings);
                AnsiConsole.MarkupLine($"[green]Contract Output: {Path.GetFullPath(contractOutputPath)}[/]");

                var contractDirectory = Path.GetDirectoryName(contractOutputPath);
                if (!string.IsNullOrWhiteSpace(contractDirectory) && !Directory.Exists(contractDirectory))
                    Directory.CreateDirectory(contractDirectory);

                await File.WriteAllTextAsync(contractOutputPath, contractCode);
            }
            await Analytics.LogFeatureUsage(settings);

            AnsiConsole.MarkupLine($"[green]Duration: {stopwatch.Elapsed}{Crlf}[/]");

            if (!settings.NoBanner)
                DonationBanner();

            return 0;
        }
        catch (Exception exception)
        {
            AnsiConsole.WriteLine();
            if (exception is OpenApiUnsupportedSpecVersionException unsupportedSpecVersionException)
            {
                AnsiConsole.MarkupLine($"[red]Unsupported OpenAPI version: {unsupportedSpecVersionException.SpecificationVersion}[/]");
                AnsiConsole.WriteLine();
            }

            if (exception is not OpenApiValidationException)
            {
                AnsiConsole.MarkupLine($"[red]Error: {exception.Message}[/]");
                AnsiConsole.MarkupLine($"[red]Exception: {exception.GetType()}[/]");
                AnsiConsole.MarkupLine($"[yellow]Stack Trace:{Crlf}{exception.StackTrace}[/]");
                AnsiConsole.WriteLine();
            }

            if (!settings.SkipValidation)
            {
                AnsiConsole.MarkupLine("[yellow]Try using the --skip-validation argument.[/]");
                AnsiConsole.WriteLine();
            }

            AnsiConsole.MarkupLine("[yellow]#############################################################################[/]");
            AnsiConsole.MarkupLine("[yellow]#  Consider reporting the problem if you are unable to resolve it yourself  #[/]");
            AnsiConsole.MarkupLine("[yellow]#  https://github.com/christianhelle/refitter/issues                        #[/]");
            AnsiConsole.MarkupLine("[yellow]#############################################################################[/]");
            AnsiConsole.WriteLine();

            await Analytics.LogError(exception, settings);
            return exception.HResult;
        }
    }

    private static void DonationBanner()
    {
        AnsiConsole.MarkupLine("[dim]###################################################################[/]");
        AnsiConsole.MarkupLine("[dim]#  Do you find this tool useful and feel a bit generous?          #[/]");
        AnsiConsole.MarkupLine("[dim]#  https://github.com/sponsors/christianhelle                     #[/]");
        AnsiConsole.MarkupLine("[dim]#  https://www.buymeacoffee.com/christianhelle                    #[/]");
        AnsiConsole.MarkupLine("[dim]#                                                                 #[/]");
        AnsiConsole.MarkupLine("[dim]#  Does this tool not work or does it lack something you need?    #[/]");
        AnsiConsole.MarkupLine("[dim]#  https://github.com/christianhelle/refitter/issues              #[/]");
        AnsiConsole.MarkupLine("[dim]###################################################################[/]");
        AnsiConsole.WriteLine();
    }

    private static string GetOutputPath(Settings settings, RefitGeneratorSettings refitGeneratorSettings)
    {
        var outputPath = settings.OutputPath != Settings.DefaultOutputPath && !string.IsNullOrWhiteSpace(settings.OutputPath)
                        ? settings.OutputPath
                        : refitGeneratorSettings.OutputFilename ?? "Output.cs";

        if (!string.IsNullOrWhiteSpace(refitGeneratorSettings.OutputFolder) &&
            refitGeneratorSettings.OutputFolder != RefitGeneratorSettings.DefaultOutputFolder)
        {
            outputPath = Path.Combine(refitGeneratorSettings.OutputFolder, outputPath);
        }

        return outputPath;
    }

    private static string GetContractOutputPath(Settings settings, RefitGeneratorSettings refitGeneratorSettings)
    {
        var outputPath = settings.OutputPath != Settings.DefaultOutputPath && !string.IsNullOrWhiteSpace(settings.OutputPath)
            ? settings.OutputPath
            : refitGeneratorSettings.ContractOutputFilename ?? "Output.cs";

        if (!string.IsNullOrWhiteSpace(refitGeneratorSettings.OutputFolder) &&
            refitGeneratorSettings.OutputFolder != RefitGeneratorSettings.DefaultOutputFolder)
        {
            outputPath = Path.Combine(refitGeneratorSettings.OutputFolder, outputPath);
        }

        return outputPath;
    }

    private static async Task ValidateOpenApiSpec(string openApiPath)
    {
        var validationResult = await OpenApiValidator.Validate(openApiPath);
        if (!validationResult.IsValid)
        {
            AnsiConsole.MarkupLine($"[red]{Crlf}OpenAPI validation failed:{Crlf}[/]");

            foreach (var error in validationResult.Diagnostics.Errors)
            {
                TryWriteLine(error, "red", "Error");
            }

            foreach (var warning in validationResult.Diagnostics.Warnings)
            {
                TryWriteLine(warning, "yellow", "Warning");
            }

            validationResult.ThrowIfInvalid();
        }

        AnsiConsole.MarkupLine($"[green]{Crlf}OpenAPI statistics:{Crlf}{validationResult.Statistics}{Crlf}[/]");
    }

    private static void TryWriteLine(
        OpenApiError error,
        string color,
        string label)
    {
        try
        {
            AnsiConsole.MarkupLine($"[{color}]{label}:{Crlf}{error}{Crlf}[/]");
        }
        catch
        {
            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = color switch
            {
                "red" => ConsoleColor.Red,
                "yellow" => ConsoleColor.Yellow,
                _ => originalColor
            };

            Console.WriteLine($"{label}:{Crlf}{error}{Crlf}");

            Console.ForegroundColor = originalColor;
        }
    }
}