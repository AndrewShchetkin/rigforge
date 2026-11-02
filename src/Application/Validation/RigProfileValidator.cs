using System.Text.Json;
using FluentValidation;
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

        RuleFor(p => p.FanCurves)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .Must(curves => curves.Count <= 64).WithMessage("At most 64 fan curves are supported.")
            .Must(curves => curves.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() == curves.Count)
            .WithMessage("Fan curve ids must be unique.")
            .Must(curves => curves.Select(c => (c.DeviceId, c.ChannelId)).Distinct().Count() == curves.Count)
            .WithMessage("A channel can only be driven by one fan curve.");

        RuleForEach(p => p.FanCurves).SetValidator(new FanCurveValidator());
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
            .Must(p => p.Select(x => (x.DeviceId, x.ZoneId)).Distinct().Count() == p.Count)
            .WithMessage("Each zone can only be placed once.");

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
        });
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
