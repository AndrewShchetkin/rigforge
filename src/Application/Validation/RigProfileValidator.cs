using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using RigForge.Application.Effects;
using RigForge.Core.Models;

namespace RigForge.Application.Validation;

public sealed class RigProfileValidator : AbstractValidator<RigProfile>
{
    public RigProfileValidator(EffectRegistry effects)
    {
        RuleFor(p => p.SchemaVersion).InclusiveBetween(1, RigProfile.CurrentSchemaVersion);
        RuleFor(p => p.Canvas).NotNull().SetValidator(new VirtualCanvasValidator());
        RuleFor(p => p.Effect).NotNull().SetValidator(new EffectSettingsValidator(effects));
        RuleFor(p => p.Lcd).NotNull().SetValidator(new LcdSettingsValidator());

        var lcdValidator = new LcdSettingsValidator();
        RuleFor(p => p.Displays)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .WithMessage("Displays are required.")
            .Must(displays => displays is { Count: <= 16 })
            .WithMessage("At most 16 display overrides are supported.")
            .Custom((displays, context) => ValidateDisplays(displays, lcdValidator, context));

        RuleFor(p => p.FanCurves)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .Must(curves => curves.Count <= 64).WithMessage("At most 64 fan curves are supported.")
            .Must(curves => curves.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() == curves.Count)
            .WithMessage("Fan curve ids must be unique.")
            .Must(curves => curves.Select(c => (c.DeviceId, c.ChannelId)).Distinct().Count() == curves.Count)
            .WithMessage("A channel can only be driven by one fan curve.");

        RuleForEach(p => p.FanCurves).SetValidator(new FanCurveValidator());

        RuleFor(p => p.ColorCorrections)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .Must(corrections => corrections is not null && corrections.Count <= 64)
            .WithMessage("At most 64 color corrections are supported.")
            .Must(HasOneCorrectionPerDevice)
            .WithMessage("A device can only have one color correction.");

        RuleForEach(p => p.ColorCorrections).NotNull().SetValidator(new DeviceColorCorrectionValidator());

        RuleFor(p => p.ColorPalettes)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .Must(palettes => palettes is not null && palettes.Count <= 32)
            .WithMessage("At most 32 color palettes are supported.")
            .Must(HasUniquePaletteIds)
            .WithMessage("Color palette ids must be unique.")
            .Must(HasUniquePaletteNames)
            .WithMessage("Color palette names must be unique.");

        RuleForEach(p => p.ColorPalettes).NotNull().SetValidator(new ColorPaletteValidator());
    }

    /// <summary>Null list fails validation. Null entries are skipped so they cannot throw; the per-item rule rejects them.</summary>
    private static bool HasUniquePaletteIds(IReadOnlyList<ColorPalette> palettes)
    {
        if (palettes is null)
        {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var palette in palettes)
        {
            if (string.IsNullOrEmpty(palette?.Id))
            {
                continue;
            }

            if (!seen.Add(palette.Id))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Names are compared after trim, without regard to case. Empty names fail the per-item rule.</summary>
    private static bool HasUniquePaletteNames(IReadOnlyList<ColorPalette> palettes)
    {
        if (palettes is null)
        {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var palette in palettes)
        {
            var name = palette?.Name?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            if (!seen.Add(name))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Null list fails validation. Null entries are skipped so they cannot throw; the per-item rule rejects them.</summary>
    private static bool HasOneCorrectionPerDevice(IReadOnlyList<DeviceColorCorrection> corrections)
    {
        if (corrections is null)
        {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var correction in corrections)
        {
            if (correction?.DeviceId is not string deviceId)
            {
                continue;
            }

            if (!seen.Add(deviceId))
            {
                return false;
            }
        }

        return true;
    }

    private static void ValidateDisplays(
        IReadOnlyDictionary<string, LcdSettings> displays,
        LcdSettingsValidator lcdValidator,
        ValidationContext<RigProfile> context)
    {
        if (displays is null)
        {
            return;
        }

        foreach (var (deviceId, settings) in displays)
        {
            if (string.IsNullOrEmpty(deviceId) || deviceId.Length > 256)
            {
                context.AddFailure(nameof(RigProfile.Displays), "Display device id must be non-empty and at most 256 characters.");
            }

            if (settings is null)
            {
                context.AddFailure(nameof(RigProfile.Displays), "Display settings cannot be null.");
                continue;
            }

            var result = lcdValidator.Validate(settings);
            foreach (var error in result.Errors)
            {
                context.AddFailure(nameof(RigProfile.Displays), error.ErrorMessage);
            }
        }
    }
}

public sealed class FanCurveValidator : AbstractValidator<FanCurve>
{
    public FanCurveValidator()
    {
        RuleFor(c => c.Id).NotEmpty().MaximumLength(256);
        RuleFor(c => c.DeviceId).NotEmpty().MaximumLength(256);
        RuleFor(c => c.ChannelId).NotEmpty().MaximumLength(64);
        RuleFor(c => c.Mode).IsInEnum();
        RuleFor(c => c.FixedDuty).InclusiveBetween(0, 100);
        RuleFor(c => c.MinDuty).InclusiveBetween(0, 100);
        RuleFor(c => c.MaxDuty).InclusiveBetween(0, 100).GreaterThanOrEqualTo(c => c.MinDuty);
        RuleFor(c => c.HysteresisCelsius).InclusiveBetween(0f, 20f);
        RuleFor(c => c.RampUpPercentPerSecond).InclusiveBetween(0f, 100f);
        RuleFor(c => c.RampDownPercentPerSecond).InclusiveBetween(0f, 100f);
        RuleFor(c => c.TemperatureSmoothingSeconds).InclusiveBetween(0f, 60f);

        RuleFor(c => c.SensorSelectors).Cascade(CascadeMode.Stop).NotNull().Must(s => s.Count <= 8)
            .WithMessage("At most 8 temperature sources per curve.");
        RuleFor(c => c.SensorSelectors).NotEmpty().When(c => c.Mode == FanControlMode.Curve)
            .WithMessage("A curve needs at least one temperature source.");
        RuleForEach(c => c.SensorSelectors).NotEmpty().MaximumLength(256);

        RuleFor(c => c.Points)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .Must(p => p.Count is >= 2 and <= 16).WithMessage("A curve needs between 2 and 16 points.")
            .Must(p => p.Select(x => x.Temperature).Distinct().Count() == p.Count).WithMessage("Curve points need distinct temperatures.");
        RuleForEach(c => c.Points).ChildRules(point =>
        {
            point.RuleFor(p => p.Temperature).InclusiveBetween(0f, 120f);
            point.RuleFor(p => p.Duty).InclusiveBetween(0f, 100f);
        });
    }
}

public sealed class VirtualCanvasValidator : AbstractValidator<VirtualCanvas>
{
    public VirtualCanvasValidator()
    {
        RuleFor(c => c.Width).InclusiveBetween(32, 1920);
        RuleFor(c => c.Height).InclusiveBetween(32, 1080);
        RuleFor(c => c.Placements)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .Must(p => p.Count <= 512).WithMessage("At most 512 zone placements are supported.")
            .Must(p => p.Select(x => (x.DeviceId, x.ZoneId, x.SegmentId ?? string.Empty)).Distinct().Count() == p.Count)
            .WithMessage("Each zone segment can only be placed once.")
            .Custom(ValidateSegments);

        RuleForEach(c => c.Placements).ChildRules(placement =>
        {
            placement.RuleFor(p => p.DeviceId).NotEmpty().MaximumLength(256);
            placement.RuleFor(p => p.ZoneId).NotEmpty().MaximumLength(64);
            placement.RuleFor(p => p.Width).InclusiveBetween(1f, 4000f);
            placement.RuleFor(p => p.Height).InclusiveBetween(1f, 4000f);
            placement.RuleFor(p => p.X).InclusiveBetween(-4000f, 4000f);
            placement.RuleFor(p => p.Y).InclusiveBetween(-4000f, 4000f);
            placement.RuleFor(p => p.Rotation).InclusiveBetween(-360f, 360f);
            placement.RuleFor(p => p.LedCount).InclusiveBetween(1, 1000).When(p => p.LedCount is not null);
            placement.RuleFor(p => p.SegmentId).MaximumLength(64);
            placement.RuleFor(p => p.Name).MaximumLength(64);
            placement.RuleFor(p => p.Topology).IsInEnum().When(p => p.Topology is not null);
            placement.RuleFor(p => p.MatrixColumns).InclusiveBetween(1, 256).When(p => p.MatrixColumns is not null);
        });
    }

    private static void ValidateSegments(IReadOnlyList<ZonePlacement> placements, ValidationContext<VirtualCanvas> context)
    {
        foreach (var group in placements.GroupBy(p => (p.DeviceId, p.ZoneId)))
        {
            if (group.Count() <= 1)
            {
                continue;
            }

            var sum = 0;
            foreach (var placement in group)
            {
                if (string.IsNullOrWhiteSpace(placement.SegmentId))
                {
                    context.AddFailure(
                        nameof(VirtualCanvas.Placements),
                        $"Segmented zone '{placement.DeviceId}/{placement.ZoneId}' needs a segment id on every part.");
                }

                if (placement.LedCount is null or < 1)
                {
                    context.AddFailure(
                        nameof(VirtualCanvas.Placements),
                        $"Segment '{placement.SegmentId}' of '{placement.DeviceId}/{placement.ZoneId}' needs a LED count.");
                }

                sum += placement.LedCount ?? 0;
            }

            if (sum > 1000)
            {
                context.AddFailure(
                    nameof(VirtualCanvas.Placements),
                    $"Zone '{group.Key.DeviceId}/{group.Key.ZoneId}' cannot have more than 1000 LEDs.");
            }
        }
    }
}

public sealed class EffectSettingsValidator : AbstractValidator<EffectSettings>
{
    public EffectSettingsValidator(EffectRegistry effects)
    {
        RuleFor(e => e.EffectId).NotEmpty().Must(effects.Contains).WithMessage(e => $"Unknown effect '{e.EffectId}'.");
        RuleFor(e => e.Brightness).InclusiveBetween(0f, 1f);
        RuleFor(e => e.TargetFps).InclusiveBetween(1, 120);
        RuleFor(e => e.Parameters).Cascade(CascadeMode.Stop).NotNull()
            .Custom((parameters, context) => ValidateParameters(context.InstanceToValidate, effects, context));
    }

    private static void ValidateParameters(EffectSettings settings, EffectRegistry effects, ValidationContext<EffectSettings> context)
    {
        if (!effects.TryGetDescriptor(settings.EffectId, out var descriptor))
        {
            return; // The EffectId rule already reports the unknown effect.
        }

        var knownKeys = descriptor.Parameters.Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var key in settings.Parameters.Keys)
        {
            if (!knownKeys.Contains(key))
            {
                context.AddFailure(nameof(EffectSettings.Parameters), $"Unknown parameter '{key}' for effect '{settings.EffectId}'.");
            }
        }

        foreach (var definition in descriptor.Parameters)
        {
            // Missing keys are filled from the schema default before saving, so validate the effective value.
            EffectParameterValues.TryGetValue(settings, definition, out var value);
            switch (definition.Kind)
            {
                case EffectParameterKind.ColorList:
                    ValidateColorList(definition, value, context);
                    break;
                case EffectParameterKind.FloatRange:
                    if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number))
                    {
                        context.AddFailure(nameof(EffectSettings.Parameters), $"Parameter '{definition.Key}' must be a number.");
                    }
                    else if ((definition.Min is not null && number < definition.Min) || (definition.Max is not null && number > definition.Max))
                    {
                        context.AddFailure(nameof(EffectSettings.Parameters), $"Parameter '{definition.Key}' must be between {definition.Min} and {definition.Max}.");
                    }

                    break;
                case EffectParameterKind.Boolean:
                    if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    {
                        context.AddFailure(nameof(EffectSettings.Parameters), $"Parameter '{definition.Key}' must be a boolean.");
                    }

                    break;
                case EffectParameterKind.IntRange:
                    if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var integer))
                    {
                        context.AddFailure(nameof(EffectSettings.Parameters), $"Parameter '{definition.Key}' must be an integer.");
                    }
                    else if ((definition.Min is not null && integer < definition.Min) || (definition.Max is not null && integer > definition.Max))
                    {
                        context.AddFailure(nameof(EffectSettings.Parameters), $"Parameter '{definition.Key}' must be between {definition.Min} and {definition.Max}.");
                    }

                    break;
                case EffectParameterKind.Select:
                    if (value.ValueKind != JsonValueKind.String
                        || value.GetString() is not { } selected
                        || definition.Options is null
                        || !ContainsOption(definition.Options, selected))
                    {
                        context.AddFailure(nameof(EffectSettings.Parameters), $"Parameter '{definition.Key}' must be one of the allowed options.");
                    }

                    break;
                case EffectParameterKind.SensorSelector:
                    if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
                    {
                        context.AddFailure(nameof(EffectSettings.Parameters), $"Parameter '{definition.Key}' must name a sensor.");
                    }
                    else if (value.GetString()!.Length > 256)
                    {
                        context.AddFailure(nameof(EffectSettings.Parameters), $"Parameter '{definition.Key}' must be at most 256 characters.");
                    }

                    break;
            }
        }

        if (descriptor.FindParameter(EffectParameterValues.TemperatureLowKey) is not null
            && descriptor.FindParameter(EffectParameterValues.TemperatureHighKey) is not null)
        {
            var low = EffectParameterValues.GetFloat(settings, descriptor, EffectParameterValues.TemperatureLowKey, 35f);
            var high = EffectParameterValues.GetFloat(settings, descriptor, EffectParameterValues.TemperatureHighKey, 80f);
            if (high <= low)
            {
                context.AddFailure(nameof(EffectSettings.Parameters), "Parameter 'temperatureHigh' must be above 'temperatureLow'.");
            }
        }
    }

    private static bool ContainsOption(IReadOnlyList<EffectSelectOption> options, string selected)
    {
        foreach (var option in options)
        {
            if (string.Equals(option.Value, selected, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void ValidateColorList(
        EffectParameterDefinition definition,
        JsonElement value,
        ValidationContext<EffectSettings> context)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            context.AddFailure(nameof(EffectSettings.Parameters), $"Parameter '{definition.Key}' must be a list of #RRGGBB colors.");
            return;
        }

        var count = value.GetArrayLength();
        if (count < definition.MinColors || count > definition.MaxColors)
        {
            context.AddFailure(
                nameof(EffectSettings.Parameters),
                $"Parameter '{definition.Key}' needs between {definition.MinColors} and {definition.MaxColors} colors.");
        }

        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || !RgbColor.TryParse(item.GetString(), out _))
            {
                context.AddFailure(nameof(EffectSettings.Parameters), $"Parameter '{definition.Key}' colors must be #RRGGBB.");
                break;
            }
        }
    }
}

public sealed class DeviceColorCorrectionValidator : AbstractValidator<DeviceColorCorrection>
{
    public DeviceColorCorrectionValidator()
    {
        RuleFor(c => c.DeviceId).NotEmpty().MaximumLength(256);
        RuleFor(c => c.Gamma).InclusiveBetween(0.5f, 3f);
        RuleFor(c => c.WhiteBalance).Must(c => RgbColor.TryParse(c, out _)).WithMessage("White balance must be #RRGGBB.");
    }

    protected override bool PreValidate(ValidationContext<DeviceColorCorrection> context, ValidationResult result)
    {
        if (context.InstanceToValidate is not null)
        {
            return true;
        }

        result.Errors.Add(new ValidationFailure(nameof(DeviceColorCorrection), "A color correction entry cannot be null."));
        return false;
    }
}

public sealed class ColorPaletteValidator : AbstractValidator<ColorPalette>
{
    public ColorPaletteValidator()
    {
        RuleFor(p => p.Id).NotEmpty().MaximumLength(64);
        RuleFor(p => p.Name)
            .Must(name => name is not null && name.Trim().Length is >= 1 and <= 32)
            .WithMessage("Color palette name must be 1–32 characters.");
        RuleFor(p => p.Colors)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .Must(colors => colors is not null && colors.Count is >= 1 and <= 8)
            .WithMessage("A color palette needs between 1 and 8 colors.");
        RuleForEach(p => p.Colors)
            .Must(color => RgbColor.TryParse(color, out _))
            .WithMessage("Color palette colors must be #RRGGBB.")
            .When(p => p.Colors is not null);
    }

    protected override bool PreValidate(ValidationContext<ColorPalette> context, ValidationResult result)
    {
        if (context.InstanceToValidate is not null)
        {
            return true;
        }

        result.Errors.Add(new ValidationFailure(nameof(ColorPalette), "A color palette entry cannot be null."));
        return false;
    }
}

public sealed class LcdSettingsValidator : AbstractValidator<LcdSettings>
{
    public LcdSettingsValidator()
    {
        RuleFor(l => l.Mode).IsInEnum();
        RuleFor(l => l.Brightness).InclusiveBetween(0, 100);
        RuleFor(l => l.Rotation).Must(r => r is 0 or 90 or 180 or 270).WithMessage("Rotation must be 0, 90, 180 or 270.");
        RuleFor(l => l.FramesPerSecond).InclusiveBetween(0.1f, 30f);
        RuleFor(l => l.AccentColor).Must(c => RgbColor.TryParse(c, out _)).WithMessage("Accent color must be #RRGGBB.");
        RuleFor(l => l.SensorSelectors).Cascade(CascadeMode.Stop).NotNull().Must(s => s.Count <= 4)
            .WithMessage("At most 4 sensors can be shown.");
        RuleForEach(l => l.SensorSelectors).NotEmpty().MaximumLength(256);
    }
}
