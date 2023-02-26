﻿using System.ComponentModel;
using Refitter.Core;
using Spectre.Console.Cli;
using ValidationResult = Spectre.Console.ValidationResult;


var app = new CommandApp<GenerateCommand>();
return app.Run(args);

internal sealed class GenerateCommand : AsyncCommand<GenerateCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [Description("Path to OpenAPI Specification file")]
        [CommandArgument(0, "[openApiPath]")]
        public string? OpenApiPath { get; set; }
        
        [Description("Default namespace to use for generated types")]
        [CommandOption("-n|--namespace")]
        [DefaultValue("GeneratedCode")]
        public string? Namespace { get; set; }
    }

    public override ValidationResult Validate(CommandContext context, Settings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.OpenApiPath))
            return ValidationResult.Error($"OpenApiPath is required");

        return File.Exists(settings.OpenApiPath)
            ? base.Validate(context, settings)
            : ValidationResult.Error($"File not found - {settings.OpenApiPath}");
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var searchPath = settings.OpenApiPath ?? ".";
        var refitGeneratorSettings = new RefitGeneratorSettings
        {
            OpenApiPath = searchPath,
            Namespace = settings.Namespace ?? "GeneratedCode"
        };

        var generator = await RefitGenerator.CreateAsync(refitGeneratorSettings);
        var code = generator.Generate();
        await File.WriteAllTextAsync("Output.cs", code);

        return 0;
    }
}