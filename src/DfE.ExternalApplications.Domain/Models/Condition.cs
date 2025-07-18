﻿using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace DfE.ExternalApplications.Domain.Models;

[ExcludeFromCodeCoverage]
public class Condition
{
    [JsonPropertyName("triggerField")]
    public required string TriggerField { get; set; }

    [JsonPropertyName("operator")]
    public required string Operator { get; set; }

    [JsonPropertyName("value")]
    public required object Value { get; set; }
}