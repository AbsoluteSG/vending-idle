using System.Text.Json.Serialization;

namespace VendingIdle.Core;

/// <summary>
/// One cell of the machine. Serialized directly into the save file, so keep it
/// to plain settable properties.
/// </summary>
public sealed class Slot
{
    /// <summary>Row-major index. Row 0 is the bottom row of the machine.</summary>
    public int Index { get; set; }

    public bool Unlocked { get; set; }

    /// <summary>Null when the slot is empty of any assignment.</summary>
    public string? DrinkId { get; set; }

    public int Stock { get; set; }

    public bool HasAutoRestocker { get; set; }

    /// <summary>Seconds accumulated toward the next auto-restock unit.</summary>
    public double AutoTimer { get; set; }

    [JsonIgnore] public int Row => Index / Balance.Columns;
    [JsonIgnore] public int Column => Index % Balance.Columns;

    [JsonIgnore] public DrinkDef? Drink => DrinkDatabase.Get(DrinkId);

    [JsonIgnore] public bool CanDispense => Unlocked && Stock > 0 && DrinkId is not null;
}
